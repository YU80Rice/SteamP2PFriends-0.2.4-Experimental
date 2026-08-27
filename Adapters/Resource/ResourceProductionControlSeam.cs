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

        private int _observerCount;

        public void BeginSession(SessionEpoch sessionEpoch)
        {
            if (_sessionActive && _sessionEpoch == sessionEpoch) return;

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
            _lifecycle.OnSessionBegin(ToAdapterEpoch(sessionEpoch));
            _replication.ResetReplication(ToAdapterEpoch(sessionEpoch));
        }

        public void EndSession()
        {
            if (!_sessionActive) return;

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
        }

        public SpatialRelevanceDiff UpdateObserver(
            ulong observerId,
            ulong connectionToken,
            byte centerX,
            byte centerY)
        {
            EnsureSession();
            if (observerId == 0UL) throw new ArgumentOutOfRangeException(nameof(observerId));
            if (connectionToken == 0UL) throw new ArgumentOutOfRangeException(nameof(connectionToken));

            _observers.Add(observerId);
            bool connectionChanged = _connectionTokens.TryGetValue(observerId, out ulong previousConnectionToken)
                && previousConnectionToken != connectionToken;

            SpatialRelevanceDiff diff = _spatialIndex.UpdateGrid2D(
                observerId, connectionToken, centerX, centerY, _radius, _worldSize);
            ulong exitedConnectionToken = connectionChanged ? previousConnectionToken : connectionToken;
            ProcessExited(observerId, exitedConnectionToken, diff.ExitedRegions);
            if (connectionChanged)
                _lifecycle.OnObserverDisconnect(observerId, previousConnectionToken);
            ProcessEntered(observerId, connectionToken, diff.EnteredRegions);
            _connectionTokens[observerId] = connectionToken;
            return diff;
        }

        public SpatialRelevanceDiff RemoveObserver(ulong observerId)
        {
            EnsureSession();
            SpatialRelevanceDiff diff = _spatialIndex.RemoveObserver(observerId);
            if (diff.HasChanges)
                ProcessExited(observerId, diff.ConnectionToken, diff.ExitedRegions);
            if (_connectionTokens.TryGetValue(observerId, out ulong connectionToken))
            {
                _lifecycle.OnObserverDisconnect(observerId, connectionToken);
                _connectionTokens.Remove(observerId);
                _observers.Remove(observerId);
                _observerCount = _observers.Count;
            }
            return diff;
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
                    _pendingReleases.Remove(pending.RegionKey);
                    _leases.Remove(pending.RegionKey);
                    continue;
                }
                RegionGeneration current = ReadGeneration(pending.RegionKey, pending.RegionGeneration);
                if (current != pending.RegionGeneration)
                {
                    currentPending.RegionGeneration = current;
                    currentPending.Deadline = _clock + _hysteresisSeconds;
                    _leases[pending.RegionKey] = current;
                    RoleLogger.Info("[Host]",
                        $"[ResourceSPI] event=LeaseReleaseDeferred reason=staleRegionGeneration " +
                        $"region={pending.RegionKey} sessionEpoch={pending.SessionEpoch.Value} " +
                        $"regionGeneration={current.Value} deadlineInSeconds={_hysteresisSeconds:0.###}");
                    continue;
                }

                _pendingReleases.Remove(pending.RegionKey);
                _lifecycle.OnRelease(CreateTicket(pending.RegionKey, pending.RegionGeneration, 0));
                _leases.Remove(pending.RegionKey);
            }

            _lifecycle.OnTick(deltaTime);
            _replication.OnReplicationTick(deltaTime);
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

        private void ProcessExited(ulong observerId, ulong connectionToken, RegionKey[] regions)
        {
            foreach (RegionKey region in regions)
            {
                int next = DecrementDemand(region);
                _replication.OnObserverExited(observerId, connectionToken, region);
                if (next == 0 && _leases.ContainsKey(region))
                {
                    _pendingReleases[region] = new PendingRelease
                    {
                        SessionEpoch = _sessionEpoch,
                        RegionKey = region,
                        RegionGeneration = ReadGeneration(region, _leases[region]),
                        Deadline = _clock + _hysteresisSeconds
                    };
                    RoleLogger.Info("[Host]",
                        $"[ResourceSPI] event=LeaseReleaseScheduled authority=ResourceProductionControlSeam " +
                        $"region={region} sessionEpoch={_sessionEpoch.Value} " +
                        $"regionGeneration={_pendingReleases[region].RegionGeneration.Value} " +
                        $"hysteresisSeconds={_hysteresisSeconds:0.###} demand=0");
                }
            }
        }

        private void ProcessEntered(ulong observerId, ulong connectionToken, RegionKey[] regions)
        {
            foreach (RegionKey region in regions)
            {
                int previous = GetDemand(region);
                _demand[region] = previous + 1;
                bool cancelledPendingRelease = _pendingReleases.Remove(region);

                if (previous == 0 && !_leases.ContainsKey(region))
                {
                    RegionGeneration beforeAcquire = ReadGeneration(region, default);
                    _lifecycle.OnAcquire(CreateTicket(region, beforeAcquire, 1));
                    _leases[region] = ReadGeneration(region, beforeAcquire);
                }
                else if (previous == 0 && cancelledPendingRelease)
                {
                    ReentryCount++;
                    RoleLogger.Info("[Host]",
                        $"[ResourceSPI] event=LeaseReentry region={region} " +
                        $"connectionToken={connectionToken} hysteresisCancelled=true");
                }

                _replication.OnObserverEntered(observerId, connectionToken, region);
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
            if (!_sessionActive) throw new InvalidOperationException("Resource production session is not active.");
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
