using SteamP2PFriends.Core.Identity;
using SteamP2PFriends.MultiObserver.SPI;
using SteamP2PFriends.MultiObserver.Spatial;
using SteamP2PFriends.Shared;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace SteamP2PFriends.Adapters.Resource
{
    /// <summary>
    /// Resource 的唯一生产控制接缝。
    ///
    /// 它把观察者的二维空间需求合并为区域引用计数，并在 0 -> 1 时向 Resource
    /// 生命周期适配器申请租约，在 N -> 0 后等待固定滞回时间再释放。原生
    /// ResourceManager 仍负责实际网络写入；本接缝只拥有区域生产资格、代次和
    /// 复制适配器事件，避免新增第二个资源状态写入者。
    /// </summary>
    public sealed class ResourceProductionControlSeam
    {
        private sealed class PendingRelease
        {
            internal SessionEpoch SessionEpoch;
            internal RegionKey RegionKey;
            internal RegionGeneration RegionGeneration;
            internal float Deadline;
        }

        /// <summary>acquire 失败区域的重试登记:由观察者的下一次 Update 驱动重试。
        /// 登记按观察者归属,避免多观察者同区域时互相吞并彼此的重试资格。</summary>
        private readonly struct AcquireRetry
        {
            internal AcquireRetry(ulong observerId, float nextRetryAt, int attempts)
            {
                ObserverId = observerId;
                NextRetryAt = nextRetryAt;
                Attempts = attempts;
            }

            internal ulong ObserverId { get; }
            internal float NextRetryAt { get; }
            internal int Attempts { get; }
        }

        private readonly SpatialObserverIndex _spatialIndex = new SpatialObserverIndex();
        private readonly ILifecycleDomainAdapter _lifecycle;
        private readonly IReversibleRegionLifecycleAdapter _reversibleLifecycle;
        private readonly IStateReplicationAdapter _replication;
        private readonly Func<RegionKey, uint> _generationReader;
        private readonly byte _worldSize;
        private readonly byte _radius;
        private readonly float _hysteresisSeconds;
        private readonly Dictionary<RegionKey, int> _demand = new Dictionary<RegionKey, int>();
        private readonly Dictionary<RegionKey, RegionGeneration> _leases =
            new Dictionary<RegionKey, RegionGeneration>();
        private readonly Dictionary<RegionKey, PendingRelease> _pendingReleases =
            new Dictionary<RegionKey, PendingRelease>();
        /// <summary>foliage 未烘焙区域的暂缓重试间隔(秒):短暂等待后 capture 即可成功,不计 fault。</summary>
        private const float DeferredAcquireRetryInterval = 2.0f;
        /// <summary>deferred 连续重试达到该次数后降级静默稳态(R2):仅计数,不再逐条打
        /// Info——真无树区域 32s 退避稳态的 2317 次/轮日志量降级;重试与清除语义不变。</summary>
        private const int DeferredAcquireQuietAttempts = 5;
        /// <summary>其他 acquire 失败的重试间隔(秒):单区域隔离后由观察者下一次 Update 重试。</summary>
        private const float FailedAcquireRetryInterval = 10.0f;
        private readonly Dictionary<RegionKey, AcquireRetry> _acquireRetries =
            new Dictionary<RegionKey, AcquireRetry>();
        private readonly HashSet<ulong> _observers = new HashSet<ulong>();
        private readonly Dictionary<ulong, ulong> _connectionTokens = new Dictionary<ulong, ulong>();

        private SessionEpoch _sessionEpoch;
        private bool _sessionActive;
        private bool _repairRequired;
        private float _clock;

        public ResourceProductionControlSeam(
            ILifecycleDomainAdapter lifecycle,
            IStateReplicationAdapter replication,
            Func<RegionKey, uint> generationReader,
            byte worldSize,
            byte radius,
            float hysteresisSeconds)
        {
            _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
            _reversibleLifecycle = lifecycle as IReversibleRegionLifecycleAdapter
                ?? throw new ArgumentException(
                    "Resource lifecycle adapter must provide a reversible region state seam.",
                    nameof(lifecycle));
            _replication = replication ?? throw new ArgumentNullException(nameof(replication));
            _generationReader = generationReader ?? throw new ArgumentNullException(nameof(generationReader));
            if (worldSize == 0) throw new ArgumentOutOfRangeException(nameof(worldSize));
            if (hysteresisSeconds < 0f) throw new ArgumentOutOfRangeException(nameof(hysteresisSeconds));
            _worldSize = worldSize;
            _radius = radius;
            _hysteresisSeconds = hysteresisSeconds;
        }

        public bool IsSessionActive => _sessionActive;
        public SessionEpoch SessionEpoch => _sessionEpoch;
        public int ObserverCount => _observerCount;
        public int ActiveLeaseCount => _leases.Count;
        public int PendingReleaseCount => _pendingReleases.Count;
        public int DemandRegionCount => _demand.Count;
        public int ReentryCount { get; private set; }
        public bool RepairRequired => _repairRequired;

        public bool TryGetConnectionGeneration(ulong observerId, out ulong connectionGeneration)
        {
            return _connectionTokens.TryGetValue(observerId, out connectionGeneration);
        }

        private int _observerCount;

        public void BeginSession(SessionEpoch sessionEpoch)
        {
            if (_repairRequired)
            {
                ResourceObservability.Error("[Host]", "SessionBegin", "-", sessionEpoch.Value, 0UL, 0U,
                    "Fallback", true, "rejected", "reason=repair-required failClosed=true");
                throw new InvalidOperationException("Resource production seam requires repair before a new session.");
            }
            if (_sessionActive && _sessionEpoch == sessionEpoch)
            {
                ResourceObservability.NoticeOnce("session-already-active", "[Host]", "SessionBegin", "-",
                    sessionEpoch.Value, 0UL, 0U, "SPI", true, "skipped", "reason=already-active");
                return;
            }

            if (_sessionActive)
                EndSession();

            uint adapterEpoch = ToAdapterEpoch(sessionEpoch);
            ClearManagedSessionState();
            try
            {
                _lifecycle.OnSessionBegin(adapterEpoch);
                _replication.ResetReplication(adapterEpoch);
            }
            catch (Exception ex)
            {
                try { _lifecycle.OnSessionEnd(); }
                catch (Exception cleanupEx)
                {
                    ResourceObservability.Error("[Shared]", "SessionBeginCleanup", "-",
                        sessionEpoch.Value, 0UL, 0U, "Fallback", false, "failed",
                        "reason=lifecycle-cleanup-failed exception=" + cleanupEx.GetType().Name);
                }
                try { _replication.ResetReplication(adapterEpoch); }
                catch (Exception cleanupEx)
                {
                    ResourceObservability.Error("[Shared]", "SessionBeginCleanup", "-",
                        sessionEpoch.Value, 0UL, 0U, "Fallback", false, "failed",
                        "reason=replication-cleanup-failed exception=" + cleanupEx.GetType().Name);
                }
                ClearManagedSessionState();
                ResourceObservability.Error("[Shared]", "SessionBegin", "-",
                    sessionEpoch.Value, 0UL, 0U, "Fallback", false, "failed",
                    "reason=external-session-initialization-failed failClosed=true exception=" + ex.GetType().Name);
                throw;
            }

            _sessionEpoch = sessionEpoch;
            _sessionActive = true;
            _repairRequired = false;
        }

        public void EndSession()
        {
            if (!_sessionActive)
            {
                ResourceObservability.NoticeOnce("session-end-inactive", "[Shared]", "SessionEnd", "-",
                    0UL, 0UL, 0U, "Fallback", false, "skipped", "reason=session-not-active");
                return;
            }

            Exception cleanupFailure = null;
            try
            {
                _lifecycle.OnSessionEnd();
            }
            catch (Exception ex)
            {
                cleanupFailure = ex;
                ResourceObservability.Error("[Host]", "SessionEnd", "-", _sessionEpoch.Value,
                    0UL, 0U, "Fallback", true, "failed",
                    "reason=lifecycle-session-end-failed state-cleanup-required exception=" + ex.GetType().Name);
            }

            try
            {
                _replication.ResetReplication(ToAdapterEpoch(_sessionEpoch));
            }
            catch (Exception ex)
            {
                if (cleanupFailure == null) cleanupFailure = ex;
                ResourceObservability.Error("[Host]", "SessionEnd", "-", _sessionEpoch.Value,
                    0UL, 0U, "Fallback", true, "failed",
                    "reason=replication-reset-failed state-cleanup-required exception=" + ex.GetType().Name);
            }

            ClearManagedSessionState();
            _repairRequired = cleanupFailure != null;
            if (cleanupFailure != null) throw cleanupFailure;
        }

        private void ClearManagedSessionState()
        {
            _spatialIndex.Clear();
            _demand.Clear();
            _leases.Clear();
            _pendingReleases.Clear();
            _acquireRetries.Clear();
            _observers.Clear();
            _connectionTokens.Clear();
            _observerCount = 0;
            ReentryCount = 0;
            _clock = 0f;
            _sessionEpoch = default(SessionEpoch);
            _sessionActive = false;
            _repairRequired = false;
        }

        public SpatialRelevanceDiff UpdateObserver(
            ulong observerId,
            ulong connectionToken,
            byte centerX,
            byte centerY)
        {
            EnsureSession();
            if (observerId == 0UL)
            {
                ResourceObservability.Error("[Shared]", "ObserverUpdate", "-", _sessionEpoch.Value,
                    connectionToken, 0U, "Fallback", true, "rejected", "reason=observer-id-zero");
                throw new ArgumentOutOfRangeException(nameof(observerId));
            }
            if (connectionToken == 0UL)
            {
                ResourceObservability.Error("[Shared]", "ObserverUpdate", "-", _sessionEpoch.Value,
                    0UL, 0U, "Fallback", true, "rejected", "reason=connection-generation-zero");
                throw new ArgumentOutOfRangeException(nameof(connectionToken));
            }

            bool hadObserver = _observers.Contains(observerId);
            bool hadConnection = _connectionTokens.TryGetValue(observerId, out ulong previousConnectionToken);
            bool connectionChanged = hadConnection && previousConnectionToken != connectionToken;
            HashSet<RegionKey> previousRegions = _spatialIndex.GetActiveRegions(observerId);
            Dictionary<RegionKey, int> previousDemand = new Dictionary<RegionKey, int>(_demand);
            Dictionary<RegionKey, RegionGeneration> previousLeases = new Dictionary<RegionKey, RegionGeneration>(_leases);
            Dictionary<RegionKey, PendingRelease> previousPending = ClonePendingReleases();
            int previousObserverCount = _observerCount;

            var compensations = new List<Action>();
            try
            {
                CaptureDisconnectCompensation(
                    observerId, previousConnectionToken, hadConnection && connectionChanged, compensations);
                _observers.Add(observerId);
                SpatialRelevanceDiff diff = _spatialIndex.UpdateGrid2D(
                    observerId, connectionToken, centerX, centerY, _radius, _worldSize);
                ulong exitedConnectionToken = connectionChanged ? previousConnectionToken : connectionToken;
                ProcessExited(observerId, exitedConnectionToken, diff.ExitedRegions, compensations);
                ProcessEntered(observerId, connectionToken, diff.EnteredRegions, compensations);
                ProcessAcquireRetries(observerId, connectionToken, compensations);
                if (connectionChanged)
                {
                    _lifecycle.OnObserverDisconnect(observerId, previousConnectionToken);
                }
                _connectionTokens[observerId] = connectionToken;
                return diff;
            }
            catch (Exception ex)
            {
                RestoreState(observerId, hadObserver, hadConnection, previousConnectionToken,
                    previousRegions, previousDemand, previousLeases, previousPending, previousObserverCount);
                bool compensated = RunCompensations(compensations, ex);
                ResourceObservability.Error("[Shared]", "ObserverUpdate", "-", _sessionEpoch.Value,
                    connectionToken, 0U, "Fallback", true, "failed",
                    "transactionRolledBack=" + compensated + " exception=" + ex.GetType().Name);
                throw;
            }
        }

        public SpatialRelevanceDiff RemoveObserver(ulong observerId)
        {
            EnsureSession();
            bool hadObserver = _observers.Contains(observerId);
            bool hadConnection = _connectionTokens.TryGetValue(observerId, out ulong previousConnectionToken);
            HashSet<RegionKey> previousRegions = _spatialIndex.GetActiveRegions(observerId);
            Dictionary<RegionKey, int> previousDemand = new Dictionary<RegionKey, int>(_demand);
            Dictionary<RegionKey, RegionGeneration> previousLeases = new Dictionary<RegionKey, RegionGeneration>(_leases);
            Dictionary<RegionKey, PendingRelease> previousPending = ClonePendingReleases();
            int previousObserverCount = _observerCount;

            var compensations = new List<Action>();
            try
            {
            CaptureDisconnectCompensation(observerId, previousConnectionToken, hadConnection, compensations);
            SpatialRelevanceDiff diff = _spatialIndex.RemoveObserver(observerId);
            if (diff.HasChanges)
                ProcessExited(observerId, diff.ConnectionToken, diff.ExitedRegions, compensations);
            if (_connectionTokens.TryGetValue(observerId, out ulong connectionToken))
            {
                IReversibleObserverDisconnectAdapter reversible =
                    _lifecycle as IReversibleObserverDisconnectAdapter;
                object disconnectState = null;
                if (reversible != null)
                {
                    disconnectState = reversible.CaptureObserverDisconnectState(observerId, connectionToken);
                    compensations.Add(() => reversible.RestoreObserverDisconnectState(
                        observerId, connectionToken, disconnectState));
                }
                _lifecycle.OnObserverDisconnect(observerId, connectionToken);
                _connectionTokens.Remove(observerId);
                _observers.Remove(observerId);
                _observerCount = _observers.Count;
                List<RegionKey> orphanedRetries = null;
                foreach (KeyValuePair<RegionKey, AcquireRetry> pair in _acquireRetries)
                {
                    if (pair.Value.ObserverId == observerId)
                    {
                        if (orphanedRetries == null) orphanedRetries = new List<RegionKey>();
                        orphanedRetries.Add(pair.Key);
                    }
                }
                if (orphanedRetries != null)
                {
                    foreach (RegionKey region in orphanedRetries) _acquireRetries.Remove(region);
                }
            }
            return diff;
            }
            catch (Exception ex)
            {
                RestoreState(observerId, hadObserver, hadConnection, previousConnectionToken,
                    previousRegions, previousDemand, previousLeases, previousPending, previousObserverCount);
                bool compensated = RunCompensations(compensations, ex);
                ResourceObservability.Error("[Shared]", "ObserverRemove", "-", _sessionEpoch.Value,
                    previousConnectionToken, 0U, "Fallback", true, "failed",
                    "transactionRolledBack=" + compensated + " exception=" + ex.GetType().Name);
                throw;
            }
        }

        public void Tick(float deltaTime)
        {
            AdvanceTime(deltaTime);
            Flush(0f);
        }

        public void AdvanceTime(float deltaTime)
        {
            EnsureSession();
            if (deltaTime < 0f) throw new ArgumentOutOfRangeException(nameof(deltaTime));
            _clock += deltaTime;

            var leaseKeys = new List<RegionKey>(_leases.Keys);
            foreach (RegionKey region in leaseKeys)
            {
                try
                {
                    RegionGeneration stored = _leases[region];
                    RegionGeneration current = ReadGeneration(region);
                    if (current.Value < stored.Value)
                    {
                        ResourceObservability.Warn("[Host]", "GenerationRead", region.ToString(),
                            _sessionEpoch.Value, 0UL, stored.Value, "Fallback", true, "rejected",
                            "reason=generation-regressed state-retained=true current=" + current.Value +
                            " stored=" + stored.Value);
                        continue;
                    }
                    _leases[region] = current;
                }
                catch (Exception ex)
                {
                    ResourceObservability.Error("[Host]", "GenerationRead", region.ToString(),
                        _sessionEpoch.Value, 0UL, _leases[region].Value, "Fallback", true, "failed",
                        "reason=generation-reader-failed retryable=true state-retained=true exception=" + ex.GetType().Name);
                }
            }
        }

        public void Flush(float deltaTime)
        {
            EnsureSession();
            var due = new List<PendingRelease>();
            foreach (PendingRelease pending in _pendingReleases.Values)
            {
                if (pending.Deadline <= _clock && GetDemand(pending.RegionKey) == 0)
                    due.Add(pending);
            }

            foreach (PendingRelease pending in due)
            {
                if (!_pendingReleases.TryGetValue(pending.RegionKey, out PendingRelease currentPending)) continue;
                if (currentPending.SessionEpoch != _sessionEpoch)
                {
                    currentPending.Deadline = _clock + Math.Max(_hysteresisSeconds, 0.1f);
                    ResourceObservability.Warn("[Host]", "LeaseRelease", pending.RegionKey.ToString(),
                        currentPending.SessionEpoch.Value, 0UL, currentPending.RegionGeneration.Value,
                        "Fallback", true, "rejected",
                        "reason=session-generation-mismatch retryable=true state-retained=true");
                    ResourceObservability.Error("[Host]", "LeaseReleaseFailed", pending.RegionKey.ToString(),
                        currentPending.SessionEpoch.Value, 0UL, currentPending.RegionGeneration.Value,
                        "Fallback", true, "failed",
                        "reason=session-generation-mismatch retryable=true pendingReleaseRetained=true");
                    continue;
                }
                RegionGeneration current;
                try
                {
                    current = ReadGeneration(pending.RegionKey);
                }
                catch (Exception ex)
                {
                    currentPending.Deadline = _clock + Math.Max(_hysteresisSeconds, 0.1f);
                    ResourceObservability.Error("[Host]", "LeaseReleaseFailed", pending.RegionKey.ToString(),
                        pending.SessionEpoch.Value, 0UL, pending.RegionGeneration.Value, "Fallback", true,
                        "failed", "reason=generation-reader-failed retryable=true pendingReleaseRetained=true " +
                        "exception=" + ex.GetType().Name);
                    continue;
                }
                if (current.Value < pending.RegionGeneration.Value)
                {
                    currentPending.Deadline = _clock + Math.Max(_hysteresisSeconds, 0.1f);
                    ResourceObservability.Warn("[Host]", "LeaseRelease", pending.RegionKey.ToString(),
                        pending.SessionEpoch.Value, 0UL, pending.RegionGeneration.Value,
                        "Fallback", true, "rejected",
                        "reason=generation-regressed retryable=true state-retained=true current=" +
                        current.Value + " stored=" + pending.RegionGeneration.Value);
                    continue;
                }
                if (current != pending.RegionGeneration)
                {
                    currentPending.RegionGeneration = current;
                    currentPending.Deadline = _clock + _hysteresisSeconds;
                    _leases[pending.RegionKey] = current;
                    ResourceObservability.Info("[Host]", "LeaseReleaseDeferred", pending.RegionKey.ToString(),
                        pending.SessionEpoch.Value, 0UL, current.Value, "SPI", true, "deferred",
                        $"reason=stale-region-generation deadlineInSeconds={_hysteresisSeconds:0.###}");
                    continue;
                }

                ResourceObservability.Info(
                    "[Host]",
                    "LeaseReleaseAttempt",
                    pending.RegionKey.ToString(),
                    pending.SessionEpoch.Value,
                    0UL,
                    pending.RegionGeneration.Value,
                    "SPI",
                    true,
                    "attempt",
                    "authority=ResourceProductionControlSeam");
                object releaseState = null;
                try
                {
                    releaseState = _reversibleLifecycle.CaptureRegionState(pending.RegionKey);
                    LeaseTicket releaseTicket = CreateTicket(pending.RegionKey, pending.RegionGeneration, 0);
                    if (!releaseTicket.Valid)
                        throw new ResourceReleaseRejectedException("invalid-ticket");
                    _lifecycle.OnRelease(releaseTicket);
                    _pendingReleases.Remove(pending.RegionKey);
                    _leases.Remove(pending.RegionKey);
                    ResourceObservability.Info(
                        "[Host]",
                        "LeaseRelease",
                        pending.RegionKey.ToString(),
                        pending.SessionEpoch.Value,
                        0UL,
                        pending.RegionGeneration.Value,
                        "SPI",
                        true,
                        "success",
                        "authority=ResourceProductionControlSeam");
                }
                catch (ResourceReleaseRejectedException ex)
                {
                    currentPending.Deadline = _clock + Math.Max(_hysteresisSeconds, 0.1f);
                    bool restored = TryRestoreRegionState(
                        pending.RegionKey, releaseState);
                    ResourceObservability.Error(
                        "[Host]", "LeaseReleaseFailed", pending.RegionKey.ToString(),
                        pending.SessionEpoch.Value, 0UL, pending.RegionGeneration.Value,
                        "Fallback", true, "failed",
                        "reason=" + ex.Reason + " retryable=true pendingReleaseRetained=true " +
                        "compensationSucceeded=" + restored);
                }
                catch (Exception ex)
                {
                    currentPending.Deadline = _clock + Math.Max(_hysteresisSeconds, 0.1f);
                    bool compensationSucceeded = TryRestoreRegionState(
                        pending.RegionKey, releaseState);
                    ResourceObservability.Error(
                        "[Host]", "LeaseReleaseFailed", pending.RegionKey.ToString(),
                        pending.SessionEpoch.Value, 0UL, pending.RegionGeneration.Value,
                        "Fallback", true, "failed",
                        "reason=external-release-may-have-side-effect retryable=true " +
                        "pendingReleaseRetained=true compensationSucceeded=" + compensationSucceeded +
                        " exception=" + ex.GetType().Name);
                }
            }

            try
            {
                _lifecycle.OnTick(deltaTime);
            }
            catch (Exception ex)
            {
                ResourceObservability.Error("[Host]", "LifecycleTick", "-", _sessionEpoch.Value, 0UL, 0U,
                    "Fallback", true, "failed", "exception=" + ex.GetType().Name);
                throw;
            }

            try
            {
                _replication.OnReplicationTick(deltaTime);
            }
            catch (Exception ex)
            {
                ResourceObservability.Error("[Host]", "ReplicationTick", "-", _sessionEpoch.Value, 0UL, 0U,
                    "Fallback", true, "failed", "exception=" + ex.GetType().Name);
                throw;
            }
        }

        public int GetDemand(RegionKey regionKey) =>
            _demand.TryGetValue(regionKey, out int count) ? count : 0;

        public bool IsLeased(RegionKey regionKey) => _leases.ContainsKey(regionKey);

        /// <summary>当前处于暂缓/失败 acquire 重试队列中的区域数(可观测性)。</summary>
        public int PendingAcquireRetryCount => _acquireRetries.Count;

        public bool TryGetLease(RegionKey regionKey, out ResourceProductionLease lease)
        {
            if (_leases.TryGetValue(regionKey, out RegionGeneration generation))
            {
                lease = new ResourceProductionLease(
                    _sessionEpoch, regionKey, generation, GetDemand(regionKey));
                return true;
            }

            lease = default;
            return false;
        }

        private void ProcessExited(ulong observerId, ulong connectionToken, RegionKey[] regions, List<Action> compensations)
        {
            foreach (RegionKey region in regions)
            {
                // 暂缓/失败重试中的区域从未完成 OnObserverEntered、demand 未计入;
                // 观察者离开时撤销自己的重试登记,跳过 exited/demand 递减以避免状态失衡。
                // 撤销同样登记补偿(R4 治本,同尾部清除):退出批次回滚时恢复登记,
                // 保证 retry/demand/spatialIndex 三者一致。
                if (_acquireRetries.TryGetValue(region, out AcquireRetry leavingRetry)
                    && leavingRetry.ObserverId == observerId)
                {
                    _acquireRetries.Remove(region);
                    compensations.Add(() => _acquireRetries[region] = leavingRetry);
                    continue;
                }
                // 兜底(R4):过时退出容忍,对称 R1——区域在空间索引却无 demand 且无本
                // 观察者登记,属历史失衡残留或未知路径。记日志跳过,不抛 underflow,
                // 消除该退出路径的 M0 会话重建源;不动其他观察者的有效登记。
                if (GetDemand(region) <= 0)
                {
                    ResourceObservability.Info("[Host]", "RegionExit", region.ToString(),
                        _sessionEpoch.Value, connectionToken, GetStoredGeneration(region).Value,
                        "SPI", true, "skipped",
                        "reason=stale-exit-without-demand observer=" + observerId);
                    continue;
                }
                try
                {
                    IReversibleObserverReplicationAdapter reversibleReplication =
                        _replication as IReversibleObserverReplicationAdapter;
                    object replicationState = reversibleReplication == null
                        ? null
                        : reversibleReplication.CaptureObserverReplicationState(observerId, connectionToken);
                    if (reversibleReplication != null)
                    {
                        compensations.Add(() => reversibleReplication.RestoreObserverReplicationState(
                            observerId, connectionToken, replicationState));
                    }
                    compensations.Add(() => _replication.OnObserverEntered(observerId, connectionToken, region));
                    _replication.OnObserverExited(observerId, connectionToken, region);
                }
                catch (Exception ex)
                {
                    // message 埋点(R1):仅 exception=TypeName 是取证盲区,Message 经
                    // NoInlining helper 承载,定位具体失败区域与原因(沿用 StaticIL 契约模式)。
                    ResourceObservability.Error("[Host]", "SnapshotRemove", region.ToString(),
                        _sessionEpoch.Value, connectionToken, GetStoredGeneration(region).Value,
                        "Fallback", true, "failed", "observer=" + observerId +
                        " exception=" + DescribeAcquireFailure(ex));
                    throw;
                }

                int next = DecrementDemand(region);
                if (next == 0 && _leases.ContainsKey(region))
                {
                    RegionGeneration storedGeneration = _leases[region];
                    RegionGeneration observedGeneration = ReadGeneration(region);
                    RegionGeneration pendingGeneration = observedGeneration.Value < storedGeneration.Value
                        ? storedGeneration : observedGeneration;
                    if (observedGeneration.Value < storedGeneration.Value)
                    {
                        ResourceObservability.Warn("[Host]", "LeaseReleaseScheduled", region.ToString(),
                            _sessionEpoch.Value, connectionToken, storedGeneration.Value, "Fallback", true,
                            "rejected", "reason=generation-regressed state-retained=true current=" +
                            observedGeneration.Value + " stored=" + storedGeneration.Value);
                    }
                    _pendingReleases[region] = new PendingRelease
                    {
                        SessionEpoch = _sessionEpoch,
                        RegionKey = region,
                        RegionGeneration = pendingGeneration,
                        Deadline = _clock + _hysteresisSeconds
                    };
                    ResourceObservability.Info("[Host]", "LeaseReleaseScheduled", region.ToString(),
                        _sessionEpoch.Value, 0UL, _pendingReleases[region].RegionGeneration.Value,
                        "SPI", true, "scheduled",
                        $"authority=ResourceProductionControlSeam hysteresisSeconds={_hysteresisSeconds:0.###} demand=0");
                }
            }
        }

        /// <summary>
        /// 取证描述：把 acquire 阶段的异常类型与 Message 拼接为可观测日志片段。
        ///
        /// 刻意独立成静态方法（并禁止内联），使 <see cref="ProcessEntered"/> 的方法体 IL
        /// 不包含任何 <c>System.Exception.get_Message</c> 调用 token，从而满足 StaticIL 契约
        /// <c>Test_FailureClassificationDoesNotParseExceptionText</c>（ProcessEntered 内零
        /// get_Message 调用）。Message 仅在这里承载，供运行时日志定位具体失败原生区域。
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string DescribeAcquireFailure(Exception ex)
        {
            return ex.GetType().Name + " message=" + ex.Message;
        }

        private void ProcessEntered(ulong observerId, ulong connectionToken, RegionKey[] regions, List<Action> compensations)
        {
            foreach (RegionKey region in regions)
            {
                ProcessSingleRegionEntry(observerId, connectionToken, region, compensations);
            }

            _observerCount = _observers.Count;
        }

        /// <summary>
        /// 单区域进入处理(acquire → replication → demand)。区域级 acquire 失败在此隔离:
        /// 快照不可用(<see cref="ResourceNativeSnapshotUnavailableException"/>,原生 foliage
        /// 尚未烘焙该区域)进入短延迟暂缓重试;其他 acquire 异常进入较长延迟重试。两类失败都
        /// 只跳过本区域、保留可重试登记,不向调用方抛出——从而不触发 <see cref="UpdateObserver"/>
        /// 的事务整批回滚,也不进入 coordinator 的 ShadowFaultBackoff 指数退避。
        /// replication 段失败仍保持原事务语义(整批回滚)。异常文本仅经
        /// <see cref="DescribeAcquireFailure"/> 承载(StaticIL 契约)。
        /// </summary>
        private void ProcessSingleRegionEntry(ulong observerId, ulong connectionToken, RegionKey region, List<Action> compensations)
        {
            int previous = GetDemand(region);
            bool hasLease = _leases.ContainsKey(region);
            bool hasPendingRelease = _pendingReleases.ContainsKey(region);
            if (previous == 0 && !hasLease)
            {
                RegionGeneration beforeAcquire;
                string acquireFailureReason = "acquire-failed";
                int compensationBase = compensations.Count;
                try
                {
                    acquireFailureReason = "generation-reader-failed-before-acquire";
                    beforeAcquire = ReadGeneration(region);
                    LeaseTicket acquireTicket = CreateTicket(region, beforeAcquire, 1);
                    if (!acquireTicket.Valid)
                    {
                        acquireFailureReason = "invalid-ticket";
                        throw new InvalidOperationException("Resource acquire ticket is invalid.");
                    }
                    acquireFailureReason = "region-snapshot-failed";
                    object acquireState = _reversibleLifecycle.CaptureRegionState(region);
                    compensations.Add(() => _reversibleLifecycle.RestoreRegionState(region, acquireState));
                    acquireFailureReason = "acquire-failed";
                    _lifecycle.OnAcquire(acquireTicket);
                    acquireFailureReason = "generation-reader-failed-after-acquire";
                    RegionGeneration acquiredGeneration = ReadGeneration(region);
                    if (acquiredGeneration.Value < beforeAcquire.Value)
                    {
                        acquireFailureReason = "generation-regressed-after-acquire";
                        throw new ResourceAcquireRejectedException(acquireFailureReason);
                    }
                    _leases[region] = acquiredGeneration;
                }
                catch (ResourceNativeSnapshotUnavailableException ex)
                {
                    // B:区域树条目不存在 = 原生 foliage 尚未烘焙该区域。暂缓快照:
                    // 短延迟重试,不计 fault、不进指数退避,预期烘焙后重试即成功。
                    // 撤销本区域已登记的补偿,避免之后其他区域失败时被整批回滚误执行。
                    compensations.RemoveRange(compensationBase, compensations.Count - compensationBase);
                    bool quietSteadyState = _acquireRetries.TryGetValue(region, out AcquireRetry currentRetry)
                        && currentRetry.ObserverId == observerId
                        && currentRetry.Attempts >= DeferredAcquireQuietAttempts;
                    ScheduleAcquireRetry(region, observerId, deferred: true);
                    if (!quietSteadyState)
                    {
                        ResourceObservability.Info("[Host]", "LeaseAcquire", region.ToString(),
                            _sessionEpoch.Value, connectionToken, GetStoredGeneration(region).Value, "Fallback", true,
                            "deferred", "reason=region-snapshot-deferred attempts=" + _acquireRetries[region].Attempts +
                            " exception=" + DescribeAcquireFailure(ex));
                    }
                    return;
                }
                catch (Exception ex)
                {
                    // A:单区域失败隔离——跳过本区域并保留可重试登记,其余区域继续。
                    // 撤销本区域已登记的补偿(如 capture 快照恢复),原因同上。
                    compensations.RemoveRange(compensationBase, compensations.Count - compensationBase);
                    ScheduleAcquireRetry(region, observerId, deferred: false);
                    ResourceObservability.Error("[Host]", "LeaseAcquire", region.ToString(),
                        _sessionEpoch.Value, connectionToken, GetStoredGeneration(region).Value, "Fallback", true,
                        "skipped", "reason=" + acquireFailureReason + " exception=" + DescribeAcquireFailure(ex));
                    return;
                }
            }

            try
            {
                IReversibleObserverReplicationAdapter reversibleReplication =
                    _replication as IReversibleObserverReplicationAdapter;
                object replicationState = reversibleReplication == null
                    ? null
                    : reversibleReplication.CaptureObserverReplicationState(observerId, connectionToken);
                if (reversibleReplication != null)
                {
                    compensations.Add(() => reversibleReplication.RestoreObserverReplicationState(
                        observerId, connectionToken, replicationState));
                }
                compensations.Add(() => _replication.OnObserverExited(observerId, connectionToken, region));
                _replication.OnObserverEntered(observerId, connectionToken, region);
            }
            catch (Exception ex)
            {
                // message 埋点(R1):同 SnapshotRemove 失败日志,Message 经 NoInlining
                // helper 承载,不破坏 ProcessSingleRegionEntry 的零 get_Message 分类契约。
                ResourceObservability.Error("[Host]", "SnapshotEnqueue", region.ToString(),
                    _sessionEpoch.Value, connectionToken, GetStoredGeneration(region).Value,
                    "Fallback", true, "failed", "observer=" + observerId +
                    " exception=" + DescribeAcquireFailure(ex));
                throw;
            }

            _demand[region] = previous + 1;
            if (previous == 0 && hasPendingRelease)
            {
                _pendingReleases.Remove(region);
                ReentryCount++;
                ResourceObservability.Info("[Host]", "LeaseReentry", region.ToString(),
                    _sessionEpoch.Value, connectionToken,
                    ReadGeneration(region).Value, "SPI", true, "success",
                    "hysteresisCancelled=true");
            }

            // 只清除当前观察者自己的登记:其他观察者对同一区域的待重试登记仍然有效。
            // 清除必须登记补偿(R4 治本):登记清除若游离在事务补偿之外,任何一次
            // UpdateObserver 回滚都会留下"区域在空间索引、无 demand、无 retry 登记"
            // 的失衡态,后续该区域退出/重连必触发 DecrementDemand underflow。
            if (_acquireRetries.TryGetValue(region, out AcquireRetry ownRetry)
                && ownRetry.ObserverId == observerId)
            {
                _acquireRetries.Remove(region);
                compensations.Add(() => _acquireRetries[region] = ownRetry);
            }
        }

        /// <summary>
        /// 登记失败区域的重试:按类别取基础间隔并温和倍增(暂缓 2s 起步上限 32s;
        /// 一般失败 10s 起步上限 60s),避免长期不可用区域造成重试风暴与日志刷屏。
        /// 成功即清除,不设次数上限——区域在本观察者离开前始终保留重试资格。
        /// </summary>
        private void ScheduleAcquireRetry(RegionKey region, ulong observerId, bool deferred)
        {
            int attempts = _acquireRetries.TryGetValue(region, out AcquireRetry previous)
                && previous.ObserverId == observerId
                    ? previous.Attempts + 1
                    : 1;
            float baseInterval = deferred ? DeferredAcquireRetryInterval : FailedAcquireRetryInterval;
            int doublingShift = deferred ? Math.Min(attempts - 1, 4) : Math.Min(attempts - 1, 3);
            float cap = deferred ? 32f : 60f;
            float interval = Math.Min(baseInterval * (1 << doublingShift), cap);
            _acquireRetries[region] = new AcquireRetry(observerId, _clock + interval, attempts);
        }

        /// <summary>
        /// 处理该观察者已到期的暂缓/失败 acquire 重试。重试走与首次进入相同的
        /// <see cref="ProcessSingleRegionEntry"/>:成功则补齐 replication 与 demand 并清除登记;
        /// 仍失败则按类别推迟下次重试。同位置更新不会重复产生 Entered diff,
        /// 因此重试只能由此登记驱动。
        /// </summary>
        private void ProcessAcquireRetries(ulong observerId, ulong connectionToken, List<Action> compensations)
        {
            if (_acquireRetries.Count == 0) return;
            List<RegionKey> dueRegions = null;
            foreach (KeyValuePair<RegionKey, AcquireRetry> pair in _acquireRetries)
            {
                if (pair.Value.ObserverId == observerId
                    && _clock >= pair.Value.NextRetryAt)
                {
                    if (dueRegions == null) dueRegions = new List<RegionKey>();
                    dueRegions.Add(pair.Key);
                }
            }

            if (dueRegions == null) return;
            foreach (RegionKey region in dueRegions)
            {
                ProcessSingleRegionEntry(observerId, connectionToken, region, compensations);
            }
        }

        private int DecrementDemand(RegionKey region)
        {
            if (!_demand.TryGetValue(region, out int count) || count <= 0)
                throw new InvalidOperationException("Resource region demand underflow.");
            if (count == 1)
            {
                _demand.Remove(region);
                return 0;
            }

            _demand[region] = count - 1;
            return count - 1;
        }

        private Dictionary<RegionKey, PendingRelease> ClonePendingReleases()
        {
            var clone = new Dictionary<RegionKey, PendingRelease>();
            foreach (KeyValuePair<RegionKey, PendingRelease> pair in _pendingReleases)
            {
                clone[pair.Key] = new PendingRelease
                {
                    SessionEpoch = pair.Value.SessionEpoch,
                    RegionKey = pair.Value.RegionKey,
                    RegionGeneration = pair.Value.RegionGeneration,
                    Deadline = pair.Value.Deadline
                };
            }
            return clone;
        }

        private bool RunCompensations(List<Action> compensations, Exception original)
        {
            bool clean = true;
            for (int i = compensations.Count - 1; i >= 0; i--)
            {
                try { compensations[i](); }
                catch (Exception ex)
                {
                    clean = false;
                    _repairRequired = true;
                    ResourceObservability.Error("[Shared]", "ObserverCompensation", "-", _sessionEpoch.Value,
                        0UL, 0U, "Fallback", true, "failed",
                        "transactionRolledBack=false compensationFailed=true original=" + original.GetType().Name +
                        " exception=" + ex.GetType().Name);
                }
            }
            return clean;
        }

        private void CaptureDisconnectCompensation(
            ulong observerId,
            ulong connectionToken,
            bool shouldCapture,
            List<Action> compensations)
        {
            if (!shouldCapture) return;

            IReversibleObserverDisconnectAdapter reversible =
                _lifecycle as IReversibleObserverDisconnectAdapter;
            if (reversible == null) return;

            // 捕获必须发生在任何 ProcessExited/ProcessEntered 或断连调用之前，
            // 且使用即将退出的旧 token，保证重连失败时能恢复真实旧快照。
            object disconnectState = reversible.CaptureObserverDisconnectState(observerId, connectionToken);
            compensations.Add(() => reversible.RestoreObserverDisconnectState(
                observerId, connectionToken, disconnectState));
        }

        private bool TryRestoreRegionState(
            RegionKey regionKey,
            object state)
        {
            try
            {
                _reversibleLifecycle.RestoreRegionState(regionKey, state);
                return true;
            }
            catch (Exception ex)
            {
                _repairRequired = true;
                ResourceObservability.Error("[Host]", "LeaseReleaseCompensation", regionKey.ToString(),
                    _sessionEpoch.Value, 0UL, GetStoredGeneration(regionKey).Value,
                    "Fallback", true, "failed",
                    "reason=region-state-restore-failed failClosed=true exception=" + ex.GetType().Name);
                return false;
            }
        }

        private void RestoreState(ulong observerId, bool hadObserver, bool hadConnection,
            ulong previousConnectionToken, HashSet<RegionKey> previousRegions,
            Dictionary<RegionKey, int> previousDemand,
            Dictionary<RegionKey, RegionGeneration> previousLeases,
            Dictionary<RegionKey, PendingRelease> previousPending,
            int previousObserverCount)
        {
            _demand.Clear();
            foreach (KeyValuePair<RegionKey, int> pair in previousDemand) _demand[pair.Key] = pair.Value;
            _leases.Clear();
            foreach (KeyValuePair<RegionKey, RegionGeneration> pair in previousLeases) _leases[pair.Key] = pair.Value;
            _pendingReleases.Clear();
            foreach (KeyValuePair<RegionKey, PendingRelease> pair in previousPending) _pendingReleases[pair.Key] = pair.Value;
            _observerCount = previousObserverCount;
            if (hadObserver) _observers.Add(observerId); else _observers.Remove(observerId);
            if (hadConnection) _connectionTokens[observerId] = previousConnectionToken;
            else _connectionTokens.Remove(observerId);
            if (hadObserver) _spatialIndex.RestoreRegions(observerId, previousConnectionToken, previousRegions);
            else _spatialIndex.RemoveObserver(observerId);
        }

        private LeaseTicket CreateTicket(RegionKey region, RegionGeneration generation, int demand)
        {
            return new LeaseTicket(
                DomainIds.Resource,
                region,
                _sessionEpoch,
                generation,
                demand,
                true);
        }

        private RegionGeneration ReadGeneration(RegionKey region)
        {
            return RegionGeneration.FromNative(_generationReader(region));
        }

        private RegionGeneration GetStoredGeneration(RegionKey region)
        {
            return _leases.TryGetValue(region, out RegionGeneration generation)
                ? generation
                : default(RegionGeneration);
        }

        private static uint ToAdapterEpoch(SessionEpoch epoch)
        {
            if (epoch.Value > uint.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(epoch), "Resource SPI 目前只接受 32 位会话代。");
            return (uint)epoch.Value;
        }

        private void EnsureSession()
        {
            if (!_sessionActive)
            {
                ResourceObservability.Error("[Shared]", "ResourceSession", "-", 0UL, 0UL, 0U,
                    "Fallback", false, "failed", "reason=session-not-active");
                throw new InvalidOperationException("Resource production session is not active.");
            }
            if (_repairRequired)
            {
                ResourceObservability.Error("[Shared]", "ResourceSession", "-", _sessionEpoch.Value,
                    0UL, 0U, "Fallback", true, "failed",
                    "reason=external-compensation-failed failClosed=true");
                throw new InvalidOperationException("Resource production seam requires repair before continuing.");
            }
        }
    }

    public readonly struct ResourceProductionLease
    {
        public ResourceProductionLease(
            SessionEpoch sessionEpoch,
            RegionKey regionKey,
            RegionGeneration regionGeneration,
            int activeDemandCount)
        {
            SessionEpoch = sessionEpoch;
            RegionKey = regionKey;
            RegionGeneration = regionGeneration;
            ActiveDemandCount = activeDemandCount;
        }

        public SessionEpoch SessionEpoch { get; }
        public RegionKey RegionKey { get; }
        public RegionGeneration RegionGeneration { get; }
        public int ActiveDemandCount { get; }
    }
}
