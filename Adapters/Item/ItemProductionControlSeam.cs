using SDG.Unturned;
using SteamP2PFriends.Core.Identity;
using SteamP2PFriends.MultiObserver.Demand;
using SteamP2PFriends.MultiObserver.Lifecycle;
using SteamP2PFriends.MultiObserver.SPI;
using System;

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
            _settlement = new ItemReturnSettlement(
                _preparationAuthority.Bridge,
                _metadataStore,
                region => _engine.GetDemand(DomainIds.Item, region),
                () => _executionPort.CurrentTime,
                () => _preparationAuthority.WorldSize,
                region => _executionPort.IsRegionActivated(region),
                region => _executionPort.ReadRegionGeneration(region).Value,
                () => _engine.SessionEpoch.Value);
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
            _settlement.ConnectionCurrent = (observer, token) =>
                _engine.TryGetConnectionGeneration(DomainIds.Item, observer, out ulong current) && current == token;
            ItemSettlementGate.AllowsConsumption = (x, y) => !_settlement.BlocksConsumption(new RegionKey(x, y));
            ItemSettlementGate.BlocksStepFive = (x, y) =>
            {
                int radius = ItemManager.ITEM_REGIONS;
                for (int rx = x - radius; rx <= x + radius; rx++)
                    for (int ry = y - radius; ry <= y + radius; ry++)
                            if (rx >= 0 && ry >= 0)
                            {
                                var region = new RegionKey((byte)rx, (byte)ry);
                                _settlement.EnsureForDemand(region);
                                if (_settlement.NeedsSettlement(region) || _settlement.BlocksConsumption(region)) return true;
                        }
                return false;
            };
            ItemSettlementGate.BlocksRegion = (x, y) =>
            {
                var region = new RegionKey(x, y);
                return _settlement.NeedsSettlement(region) || _settlement.BlocksConsumption(region);
            };
            ItemSettlementGate.EnsureStepFiveSettlement = (x, y) =>
                _settlement.EnsureForDemand(new RegionKey(x, y));
            ItemSettlementGate.SuppressDeliveredStepFive = (x, y) =>
                _settlement.WasDelivered(new RegionKey(x, y));
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
        }
        public void EndSession()
        {
            _engine.EndSession();
            _settlement.ResetIdentity();
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

        public DomainDemandProjection UpdateObserver(
            ulong observerId,
            ulong connectionToken,
            byte centerX,
            byte centerY,
            bool gameplayAuthorized = true)
        {
            DomainDemandProjection result = _engine.Observe(DomainIds.Item, observerId, connectionToken, centerX, centerY, gameplayAuthorized);
            _settlement.CommitPending();
            return result;
        }

        public void DeferObserver(ulong observerId, string reason) =>
            _engine.DeferObserver(DomainIds.Item, observerId, reason);

        public void RemoveObserver(ulong observerId)
        {
            _engine.RemoveObserver(DomainIds.Item, observerId);
            _settlement.Refresh();
        }

        public bool IsLeased(RegionKey regionKey) =>
            _engine.IsLeased(DomainIds.Item, regionKey);

        public int GetDemand(RegionKey regionKey) =>
            _engine.GetDemand(DomainIds.Item, regionKey);

        public bool TryGetConnectionGeneration(ulong observerId, out ulong connectionGeneration) =>
            _engine.TryGetConnectionGeneration(DomainIds.Item, observerId, out connectionGeneration);
    }
}
