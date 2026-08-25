using SteamP2PFriends.MultiObserver;
using SteamP2PFriends.MultiObserver.SPI;
using SteamP2PFriends.Shared;
using System;
using System.Collections.Generic;

namespace SteamP2PFriends.Adapters.Collision
{
    public readonly struct CollisionReleaseLease
    {
        public CollisionReleaseLease(ulong sessionEpoch, int regionKey, uint regionGeneration, float deadline)
        {
            SessionEpoch = sessionEpoch;
            RegionKey = regionKey;
            RegionGeneration = regionGeneration;
            Deadline = deadline;
        }

        public ulong SessionEpoch { get; }
        public int RegionKey { get; }
        public uint RegionGeneration { get; }
        public float Deadline { get; }
    }

    public sealed class LevelObjectCollisionLedger
    {
        private readonly Dictionary<int, uint> _generations = new Dictionary<int, uint>();
        private readonly Dictionary<int, CollisionReleaseLease> _releases = new Dictionary<int, CollisionReleaseLease>();
        private readonly HashSet<int> _activeRegions = new HashSet<int>();

        public ulong SessionEpoch { get; private set; } = 1UL;
        public int ActiveRegionCount => _activeRegions.Count;
        public int PendingReleaseCount => _releases.Count;

        public void BeginSession(ulong epoch)
        {
            if (epoch == 0UL) throw new ArgumentOutOfRangeException(nameof(epoch));
            if (SessionEpoch == epoch) return;
            SessionEpoch = epoch;
            _generations.Clear();
            _releases.Clear();
            _activeRegions.Clear();
        }

        public uint GetGeneration(int regionKey) =>
            _generations.TryGetValue(regionKey, out uint generation) ? generation : 0U;

        public bool IsRegionActive(int regionKey) => _activeRegions.Contains(regionKey);

        public bool IsRegionActive(byte x, byte y) => IsRegionActive((x << 8) | y);

        public uint CommitAcquire(int regionKey)
        {
            CancelRelease(regionKey);
            _activeRegions.Add(regionKey);
            uint next = GetGeneration(regionKey);
            next = next == uint.MaxValue ? 1U : next + 1U;
            if (next == 0U) next = 1U;
            _generations[regionKey] = next;
            return next;
        }

        public CollisionReleaseLease ScheduleRelease(int regionKey, float now, float hysteresisSeconds)
        {
            var lease = new CollisionReleaseLease(
                SessionEpoch,
                regionKey,
                GetGeneration(regionKey),
                now + Math.Max(0f, hysteresisSeconds));
            _releases[regionKey] = lease;
            return lease;
        }

        public bool CancelRelease(int regionKey) => _releases.Remove(regionKey);

        public bool TryGetRelease(int regionKey, out CollisionReleaseLease lease) =>
            _releases.TryGetValue(regionKey, out lease);

        public bool TryCommitRelease(int regionKey, ulong expectedEpoch, uint expectedGeneration, out uint committedGeneration)
        {
            committedGeneration = 0U;
            if (!_releases.TryGetValue(regionKey, out CollisionReleaseLease lease))
                return false;
            if (lease.SessionEpoch != expectedEpoch)
                return false;
            if (lease.RegionGeneration != expectedGeneration)
                return false;
            if (GetGeneration(regionKey) != expectedGeneration)
                return false;

            _releases.Remove(regionKey);
            _activeRegions.Remove(regionKey);
            uint next = expectedGeneration == uint.MaxValue ? 1U : expectedGeneration + 1U;
            if (next == 0U) next = 1U;
            _generations[regionKey] = next;
            committedGeneration = next;
            return true;
        }

        public void CleanObserverDisconnect(int regionKey)
        {
            _releases.Remove(regionKey);
            _activeRegions.Remove(regionKey);
        }
    }

    /// <summary>
    /// 静态场景物件与权限门物理碰撞适配器 (LevelObjectCollisionAdapter)
    /// 统一管理远区静态物件、刷卡门/钥匙门物理碰撞的按需激活与滞回释放。
    /// </summary>
    public sealed class LevelObjectCollisionAdapter : ILifecycleDomainAdapter
    {
        private static readonly LevelObjectCollisionLedger Ledger = new LevelObjectCollisionLedger();
        private static readonly object SyncLock = new object();
        private static bool _registrationReady;
        private static ulong _currentSessionEpoch = 1UL;
        public const float DefaultHysteresisSeconds = 2.0f;

        public string DomainName => "Collision";
        public string Capability => "StaticRootReactivation+DynamicAnimationCulling+HysteresisRelease";

        public static bool RegistrationReady => _registrationReady;
        public static ulong CurrentSessionEpoch => _currentSessionEpoch;

        public static bool IsRemoteCollisionRequired(byte x, byte y)
        {
            lock (SyncLock)
            {
                return Ledger.IsRegionActive(x, y);
            }
        }

        public static uint GetGeneration(int regionKey)
        {
            lock (SyncLock)
            {
                return Ledger.GetGeneration(regionKey);
            }
        }

        public void OnSessionBegin(uint sessionEpoch)
        {
            lock (SyncLock)
            {
                _registrationReady = true;
                _currentSessionEpoch = sessionEpoch == 0U ? 1UL : sessionEpoch;
                Ledger.BeginSession(_currentSessionEpoch);
            }
        }

        public void OnSessionEnd()
        {
            lock (SyncLock)
            {
                _registrationReady = false;
            }
        }

        public void OnAcquire(LeaseTicket ticket)
        {
            if (ticket.Valid)
            {
                lock (SyncLock)
                {
                    Ledger.CommitAcquire(ticket.RegionKey);
                }
            }
        }

        public void OnRelease(LeaseTicket ticket)
        {
            if (ticket.Valid)
            {
                lock (SyncLock)
                {
                    Ledger.ScheduleRelease(ticket.RegionKey, 0f, DefaultHysteresisSeconds);
                }
            }
        }

        public void OnTick(float deltaTime)
        {
            // 碰撞生命周期驱动
        }

        public void OnObserverDisconnect(ulong observerId, ulong connectionToken)
        {
            // 观察者注销
        }
    }
}
