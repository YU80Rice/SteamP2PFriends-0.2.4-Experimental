using SDG.Unturned;
using SteamP2PFriends.Core.Identity;
using SteamP2PFriends.MultiObserver.Demand;
using SteamP2PFriends.MultiObserver.Lifecycle;
using SteamP2PFriends.MultiObserver.SPI;
using System;
using System.Collections.Generic;

namespace SteamP2PFriends.Adapters.Item
{
    public enum ItemProductionMode : byte
    {
        LegacyWriter = 0,
        Candidate = 1
    }

    /// <summary>
    /// Item 的唯一生产控制接缝：连接真实共享观察者空间事实、ItemDemandPolicy、
    /// 共享需求投影与生命周期编排引擎，并驱动 ItemExecutionPort 与 ItemPreparationAuthority。
    /// 01–04 受控候选独占旧 Writer，05 才正式切换。
    /// </summary>
    public sealed class ItemProductionControlSeam
    {
        private readonly ItemPreparationAuthority _preparationAuthority;
        private readonly ItemMetadataStore _metadataStore;
        private readonly ItemExecutionPort _executionPort;
        private readonly DemandProjectionEngine _projection;
        private readonly DemandPolicy _policy;
        private readonly LifecycleOrchestrationEngine _engine;
        private readonly ItemDemandCycle _cycle;
        private readonly ItemReturnSettlement _settlement;

        public ItemProductionControlSeam(
            ItemPreparationAuthority preparationAuthority,
            ItemMetadataStore metadataStore,
            ItemExecutionPort executionPort,
            DemandProjectionEngine projection,
            DemandPolicy itemDemandPolicy,
            float hysteresisSeconds,
            LifecycleOrchestrationEngine sharedEngine = null)
        {
            _preparationAuthority = preparationAuthority ?? throw new ArgumentNullException(nameof(preparationAuthority));
            _metadataStore = metadataStore ?? throw new ArgumentNullException(nameof(metadataStore));
            _executionPort = executionPort ?? throw new ArgumentNullException(nameof(executionPort));
            _projection = projection ?? throw new ArgumentNullException(nameof(projection));
            _policy = itemDemandPolicy ?? throw new ArgumentNullException(nameof(itemDemandPolicy));
            if (_policy.Domain != DomainIds.Item)
                throw new ArgumentException("Item 生产接缝只消费 Item 域 Demand Policy。", nameof(itemDemandPolicy));
            if (hysteresisSeconds < 0f) throw new ArgumentOutOfRangeException(nameof(hysteresisSeconds));

            _engine = sharedEngine ?? new LifecycleOrchestrationEngine(projection, DefaultLifecycleDiagnostics.Instance);
            _engine.Register(_policy, _executionPort);
            var itemRetry = new FailedRetryController(_executionPort.LifecyclePolicy.FailedRetry);
            _settlement = new ItemReturnSettlement(
                _preparationAuthority.Bridge,
                _metadataStore,
                region => _engine.GetDemand(DomainIds.Item, region),
                () => _executionPort.CurrentTime,
                () => _preparationAuthority.WorldSize,
                region => _executionPort.IsRegionActivated(region),
                region => _executionPort.ReadRegionGeneration(region).Value,
                () => _engine.SessionEpoch.Value,
                _executionPort.ClassifyRegionFailure,
                itemRetry);
            var nativeBridge = _preparationAuthority.Bridge as DefaultNativeItemWarehouseBridge;
            if (nativeBridge != null)
                nativeBridge.CoreConnectionCurrent = (observer, token) =>
                    _engine.TryGetConnectionGeneration(DomainIds.Item, observer, out ulong current)
                    && IsCoreConnectionGenerationCurrent(current, token);
            _settlement.RecoveryExhausted = (observer, token, region, possibleHalfPacket) =>
                RecoveryExhausted?.Invoke(observer, token, region, possibleHalfPacket);
            _settlement.RecoveryUnconfirmed = (observer, token, region) =>
                RecoveryUnconfirmed?.Invoke(observer, token, region);
            _cycle = new ItemDemandCycle(
                _preparationAuthority.Bridge,
                _metadataStore,
                region => _engine.GetDemand(DomainIds.Item, region),
                () => _executionPort.CurrentTime,
                () => _preparationAuthority.WorldSize,
                region => _executionPort.IsRegionActivated(region),
                region => _settlement.BlocksWarehouse(region),
                region => _settlement.IsIsolated(region));
            _executionPort.BindReturnSettlement(_settlement);
            _settlement.Deliver = (observer, token, region, host) =>
                _executionPort.DeliverSettlementBaseline(observer, token, region, host);
            _settlement.CallerCoversRegion = (observer, token, region) =>
                _engine.TryGetConnectionGeneration(DomainIds.Item, observer, out ulong current)
                && current == token
                && _projection.GetActiveRegions(_policy, observer).Contains(region);
            _settlement.ConnectionCurrent = (observer, token) =>
                _engine.TryGetConnectionGeneration(DomainIds.Item, observer, out ulong current)
                && IsCoreConnectionGenerationCurrent(current, token);
            ItemSettlementGate.AllowsConsumption = (x, y) => !_settlement.BlocksConsumption(new RegionKey(x, y));
            ItemSettlementGate.BlocksRegion = (x, y) =>
            {
                var region = new RegionKey(x, y);
                return _settlement.NeedsSettlement(region) || _settlement.BlocksConsumption(region);
            };
            ItemSettlementGate.BlocksRegionForObserver = (observer, token, x, y) =>
                _settlement.BlocksConsumption(observer, token, new RegionKey(x, y));
            ItemSettlementGate.EnsureStepFiveSettlement = (x, y) =>
                _settlement.EnsureForDemand(new RegionKey(x, y));
            ItemSettlementGate.StepFiveAdmission = new ItemStepFiveAdmission(
                request => RegisterNativeStepFiveDemand(request.ObserverId, request.ConnectionToken,
                    (byte)request.Center.X, (byte)request.Center.Y),
                (request, region) => RegisterNativeStepFiveObserver(request.ObserverId,
                    request.ConnectionToken, region, request.Host),
                (request, region) => _settlement.NeedsSettlement(region) || _settlement.BlocksConsumption(region),
                (request, region) => _settlement.WasDeliveredFor(region,
                    request.ObserverId, request.ConnectionToken),
                request => _settlement.Refresh());
            ItemSettlementGate.WasDelivered = (x, y) =>
                _settlement.WasDelivered(new RegionKey(x, y));
        }

        public DemandPolicy DemandPolicy => _policy;
        public int ProjectedDemandRegionCount => _projection.GetDemandRegionCount(_policy);
        public bool IsSessionActive => _engine.IsSessionActive;
        public SessionEpoch SessionEpoch => _engine.SessionEpoch;
        public int ObserverCount => _engine.ObserverCount(DomainIds.Item);
        public int ActiveLeaseCount => _engine.ActiveLeaseCount(DomainIds.Item);
        public int PendingReleaseCount => _engine.PendingReleaseCount(DomainIds.Item);
        public int DemandRegionCount => _engine.DemandRegionCount(DomainIds.Item);
        public int ReentryCount => _engine.ReentryCount(DomainIds.Item);
        public int PendingAcquireRetryCount => _engine.PendingAcquireRetryCount(DomainIds.Item);
        public bool RepairRequired => _engine.IsRepairRequired(DomainIds.Item);

        public ItemPreparationAuthority PreparationAuthority => _preparationAuthority;
        public ItemMetadataStore MetadataStore => _metadataStore;
        public ItemExecutionPort ExecutionPort => _executionPort;

        public void BeginSession(SessionEpoch sessionEpoch)
        {
            _engine.BeginSession(sessionEpoch);
            _settlement.ResetIdentity();
            _manualCooldown.Clear();
        }
        public void EndSession()
        {
            _engine.EndSession();
            _settlement.ResetIdentity();
            _manualCooldown.Clear();
        }
        public void AdvanceTime(float deltaTime) => _engine.AdvanceTime(deltaTime);
        public void Flush(float deltaTime)
        {
            _engine.Flush(deltaTime);
            StepItemWork();
        }
        public void Tick(float deltaTime)
        {
            _engine.Tick(deltaTime);
            StepItemWork();
        }

        private void StepItemWork()
        {
            _settlement.Refresh();
            int used = _settlement.Consume(ItemReturnSettlement.ActionsPerStep);
            _cycle.Step(used == 0, true);
        }
        public ItemDemandCycle DemandCycle => _cycle;
        public bool IsRegionIsolated(RegionKey region) => _settlement.IsIsolated(region);
        public string LastSettlementFault => _settlement.LastFault;
        public Action<ulong, ulong, RegionKey, bool> RecoveryExhausted { get; set; }
        public Action<ulong, ulong, RegionKey> RecoveryUnconfirmed { get; set; }

        public DomainDemandProjection UpdateObserver(
            ulong observerId,
            ulong connectionToken,
            byte centerX,
            byte centerY,
            bool gameplayAuthorized = true)
        {
            DomainDemandProjection result = _engine.Observe(DomainIds.Item, observerId, connectionToken, centerX, centerY, gameplayAuthorized);
            ReleaseExitedRegions(result);
            _settlement.CommitPending();
            return result;
        }

        private void ReleaseExitedRegions(DomainDemandProjection result)
        {
            for (int i = 0; i < result.ExitedRegions.Length; i++)
            {
                _settlement.ReleaseWaiter(
                    result.ExitedRegions[i], result.ObserverId, result.ConnectionToken);
                _settlement.ReleaseHalfPacketForExitedRegion(
                    result.ObserverId, result.ConnectionToken, result.ExitedRegions[i]);
            }
        }

        public void DeferObserver(ulong observerId, string reason) =>
            _engine.DeferObserver(DomainIds.Item, observerId, reason);

        public void RemoveObserver(ulong observerId)
        {
            if (_engine.TryGetConnectionGeneration(DomainIds.Item, observerId, out ulong token))
                _settlement.ReleaseObserver(observerId, token);
            _engine.RemoveObserver(DomainIds.Item, observerId);
            _settlement.Refresh();
        }

        public bool IsLeased(RegionKey regionKey) =>
            _engine.IsLeased(DomainIds.Item, regionKey);

        public int GetDemand(RegionKey regionKey) =>
            _engine.GetDemand(DomainIds.Item, regionKey);

        public bool TryGetConnectionGeneration(ulong observerId, out ulong connectionGeneration) =>
            _engine.TryGetConnectionGeneration(DomainIds.Item, observerId, out connectionGeneration);

        private readonly Dictionary<string, float> _manualCooldown = new Dictionary<string, float>();

        public bool TryManualRecovery(ulong observerId, ulong connectionToken, RegionKey region)
        {
            if (!TryGetConnectionGeneration(observerId, out ulong current)
                || current == 0UL || current != connectionToken) return false;
            if (!_projection.GetActiveRegions(_policy, observerId).Contains(region)) return false;
            string cooldownKey = SessionEpoch.Value + ":" + observerId + ":" + current + ":"
                + region.X + ":" + region.Y;
            if (_manualCooldown.TryGetValue(cooldownKey, out float last)
                && _executionPort.CurrentTime - last < 10f) return false;
            EAcquireRecoveryRequestResult result = _engine.RequestManualAcquireRetry(
                DomainIds.Item, observerId, connectionToken, region);
            if (result == EAcquireRecoveryRequestResult.Started)
            {
                _manualCooldown[cooldownKey] = _executionPort.CurrentTime;
                return true;
            }
            if (result != EAcquireRecoveryRequestResult.NotEligible) return false;
            bool started = _settlement.RequestManualRecovery(observerId, connectionToken, region)
                == EItemRecoveryRequestResult.Started;
            if (started) _manualCooldown[cooldownKey] = _executionPort.CurrentTime;
            return started;
        }

        internal static bool ShouldTrySettlementManualRecovery(EAcquireRecoveryRequestResult result) =>
            result == EAcquireRecoveryRequestResult.NotEligible;

        internal static bool IsCoreConnectionGenerationCurrent(
            ulong expectedGeneration, ulong providedGeneration) =>
            expectedGeneration != 0UL && expectedGeneration == providedGeneration;

        public void RegisterStepFiveObserver(ulong observerId, ulong connectionToken, RegionKey region, bool host)
        {
            _settlement.RegisterPending(region, observerId, connectionToken, host);
        }

        public void RegisterNativeStepFiveObserver(ulong observerId, ulong connectionToken, RegionKey region, bool host)
        {
            if (observerId == 0UL || connectionToken == 0UL || !_settlement.IsReturnPending(region)) return;
            _settlement.RegisterPending(region, observerId, connectionToken, host);
            _settlement.CommitPending();
        }

        private void RegisterNativeStepFiveDemand(ulong observerId, ulong connectionToken, byte centerX, byte centerY)
        {
            if (observerId == 0UL || connectionToken == 0UL || !_engine.IsSessionActive) return;
            DomainDemandProjection result = _engine.ObserveForNativeStepFive(
                DomainIds.Item, observerId, connectionToken,
                centerX, centerY, gameplayAuthorized: true,
                suppressReplication: region => true);
            ReleaseExitedRegions(result);
            _settlement.Refresh();
        }
    }
}
