using SteamP2PFriends.Core.Identity;
using SteamP2PFriends.MultiObserver.SPI;
using SteamP2PFriends.MultiObserver.Spatial;
using SteamP2PFriends.Shared;
using System;
using System.Collections.Generic;

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

        private readonly SpatialObserverIndex _spatialIndex = new SpatialObserverIndex();
        private readonly ILifecycleDomainAdapter _lifecycle;
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
            if (_sessionActive && _sessionEpoch == sessionEpoch)
            {
                ResourceObservability.NoticeOnce("session-already-active", "[Host]", "SessionBegin", "-",
                    sessionEpoch.Value, 0UL, 0U, "SPI", true, "skipped", "reason=already-active");
                return;
            }

            if (_sessionActive)
                EndSession();

            _spatialIndex.Clear();
            _demand.Clear();
            _leases.Clear();
            _pendingReleases.Clear();
            _observers.Clear();
            _connectionTokens.Clear();
            _observerCount = 0;
            ReentryCount = 0;
            _clock = 0f;
            _sessionEpoch = sessionEpoch;
            _sessionActive = true;
            _repairRequired = false;
            _lifecycle.OnSessionBegin(ToAdapterEpoch(sessionEpoch));
            _replication.ResetReplication(ToAdapterEpoch(sessionEpoch));
        }

        public void EndSession()
        {
            if (!_sessionActive)
            {
                ResourceObservability.NoticeOnce("session-end-inactive", "[Shared]", "SessionEnd", "-",
                    0UL, 0UL, 0U, "Fallback", false, "skipped", "reason=session-not-active");
                return;
            }

            _lifecycle.OnSessionEnd();
            _replication.ResetReplication(ToAdapterEpoch(_sessionEpoch));
            _spatialIndex.Clear();
            _demand.Clear();
            _leases.Clear();
            _pendingReleases.Clear();
            _observers.Clear();
            _connectionTokens.Clear();
            _observerCount = 0;
            ReentryCount = 0;
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
            HashSet<RegionKey> previousRegions = _spatialIndex.GetActiveRegions(observerId);
            Dictionary<RegionKey, int> previousDemand = new Dictionary<RegionKey, int>(_demand);
            Dictionary<RegionKey, RegionGeneration> previousLeases = new Dictionary<RegionKey, RegionGeneration>(_leases);
            Dictionary<RegionKey, PendingRelease> previousPending = ClonePendingReleases();
            int previousObserverCount = _observerCount;

            var compensations = new List<Action>();
            try
            {
                _observers.Add(observerId);
                bool connectionChanged = hadConnection && previousConnectionToken != connectionToken;
                SpatialRelevanceDiff diff = _spatialIndex.UpdateGrid2D(
                    observerId, connectionToken, centerX, centerY, _radius, _worldSize);
                ulong exitedConnectionToken = connectionChanged ? previousConnectionToken : connectionToken;
                ProcessExited(observerId, exitedConnectionToken, diff.ExitedRegions, compensations);
                ProcessEntered(observerId, connectionToken, diff.EnteredRegions, compensations);
                if (connectionChanged)
                    _lifecycle.OnObserverDisconnect(observerId, previousConnectionToken);
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
            SpatialRelevanceDiff diff = _spatialIndex.RemoveObserver(observerId);
            if (diff.HasChanges)
                ProcessExited(observerId, diff.ConnectionToken, diff.ExitedRegions, compensations);
            if (_connectionTokens.TryGetValue(observerId, out ulong connectionToken))
            {
                _lifecycle.OnObserverDisconnect(observerId, connectionToken);
                _connectionTokens.Remove(observerId);
                _observers.Remove(observerId);
                _observerCount = _observers.Count;
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
                _leases[region] = ReadGeneration(region, _leases[region]);
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
                RegionGeneration current = ReadGeneration(pending.RegionKey, pending.RegionGeneration);
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
                try
                {
                    _lifecycle.OnRelease(CreateTicket(pending.RegionKey, pending.RegionGeneration, 0));
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
                catch (Exception ex)
                {
                    currentPending.Deadline = _clock + Math.Max(_hysteresisSeconds, 0.1f);
                    ResourceObservability.Error(
                        "[Host]",
                        "LeaseReleaseFailed",
                        pending.RegionKey.ToString(),
                        pending.SessionEpoch.Value,
                        0UL,
                        pending.RegionGeneration.Value,
                        "Fallback",
                        true,
                        "failed",
                        "retryable=true exception=" + ex.GetType().Name);
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
                try
                {
                    _replication.OnObserverExited(observerId, connectionToken, region);
                    compensations.Add(() => _replication.OnObserverEntered(observerId, connectionToken, region));
                }
                catch (Exception ex)
                {
                    ResourceObservability.Error("[Host]", "SnapshotRemove", region.ToString(),
                        _sessionEpoch.Value, connectionToken, ReadGeneration(region, default).Value,
                        "Fallback", true, "failed", "observer=" + observerId + " exception=" + ex.GetType().Name);
                    throw;
                }

                int next = DecrementDemand(region);
                if (next == 0 && _leases.ContainsKey(region))
                {
                    _pendingReleases[region] = new PendingRelease
                    {
                        SessionEpoch = _sessionEpoch,
                        RegionKey = region,
                        RegionGeneration = ReadGeneration(region, _leases[region]),
                        Deadline = _clock + _hysteresisSeconds
                    };
                    ResourceObservability.Info("[Host]", "LeaseReleaseScheduled", region.ToString(),
                        _sessionEpoch.Value, 0UL, _pendingReleases[region].RegionGeneration.Value,
                        "SPI", true, "scheduled",
                        $"authority=ResourceProductionControlSeam hysteresisSeconds={_hysteresisSeconds:0.###} demand=0");
                }
            }
        }

        private void ProcessEntered(ulong observerId, ulong connectionToken, RegionKey[] regions, List<Action> compensations)
        {
            foreach (RegionKey region in regions)
            {
                int previous = GetDemand(region);
                bool hasLease = _leases.ContainsKey(region);
                bool hasPendingRelease = _pendingReleases.ContainsKey(region);
                if (previous == 0 && !hasLease)
                {
                    RegionGeneration beforeAcquire = ReadGeneration(region, default);
                    try
                    {
                        _lifecycle.OnAcquire(CreateTicket(region, beforeAcquire, 1));
                        RegionGeneration acquiredGeneration = ReadGeneration(region, beforeAcquire);
                        _leases[region] = acquiredGeneration;
                        compensations.Add(() => _lifecycle.OnRelease(CreateTicket(region, acquiredGeneration, 0)));
                    }
                    catch (Exception ex)
                    {
                        ResourceObservability.Error("[Host]", "LeaseAcquire", region.ToString(),
                            _sessionEpoch.Value, connectionToken, beforeAcquire.Value, "Fallback", true,
                            "failed", "exception=" + ex.GetType().Name);
                        throw;
                    }
                }

                try
                {
                    _replication.OnObserverEntered(observerId, connectionToken, region);
                }
                catch (Exception ex)
                {
                    ResourceObservability.Error("[Host]", "SnapshotEnqueue", region.ToString(),
                        _sessionEpoch.Value, connectionToken, ReadGeneration(region, default).Value,
                        "Fallback", true, "failed", "observer=" + observerId + " exception=" + ex.GetType().Name);
                    throw;
                }

                compensations.Add(() => _replication.OnObserverExited(observerId, connectionToken, region));

                _demand[region] = previous + 1;
                if (previous == 0 && hasPendingRelease)
                {
                    _pendingReleases.Remove(region);
                    ReentryCount++;
                    ResourceObservability.Info("[Host]", "LeaseReentry", region.ToString(),
                        _sessionEpoch.Value, connectionToken,
                        ReadGeneration(region, default).Value, "SPI", true, "success",
                        "hysteresisCancelled=true");
                }
            }

            _observerCount = _observers.Count;
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

        private RegionGeneration ReadGeneration(RegionKey region, RegionGeneration fallback)
        {
            uint value = _generationReader(region);
            return value == 0U ? fallback : RegionGeneration.FromNative(value);
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
