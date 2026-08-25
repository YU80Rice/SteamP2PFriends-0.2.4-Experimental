using SteamP2PFriends.MultiObserver;
using SteamP2PFriends.MultiObserver.SPI;
using SteamP2PFriends.Shared;
using System;
using System.Collections.Generic;

namespace SteamP2PFriends.Adapters.Resource
{
    public readonly struct ResourceReleaseLease
    {
        public ResourceReleaseLease(ulong sessionEpoch, int regionKey, uint regionGeneration, float deadline)
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

    public sealed class ResourceRegionLifecycleLedger
    {
        private readonly Dictionary<int, uint> _generations = new Dictionary<int, uint>();
        private readonly Dictionary<int, ResourceReleaseLease> _releases = new Dictionary<int, ResourceReleaseLease>();
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

        public ResourceReleaseLease ScheduleRelease(int regionKey, float now, float hysteresisSeconds)
        {
            var lease = new ResourceReleaseLease(
                SessionEpoch,
                regionKey,
                GetGeneration(regionKey),
                now + Math.Max(0f, hysteresisSeconds));
            _releases[regionKey] = lease;
            return lease;
        }

        public bool CancelRelease(int regionKey) => _releases.Remove(regionKey);

        public bool TryGetRelease(int regionKey, out ResourceReleaseLease lease) =>
            _releases.TryGetValue(regionKey, out lease);

        public bool TryCommitRelease(int regionKey, ulong expectedEpoch, uint expectedGeneration, out uint committedGeneration)
        {
            committedGeneration = 0U;
            if (!_releases.TryGetValue(regionKey, out ResourceReleaseLease lease))
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
    /// 树木与矿物资源生命周期协调适配器 (ResourceRegionLifecycleAdapter)
    /// 管理 2D 矩形网格 (byte x, byte y) 资源区域的按需激活与滞回释放。
    /// </summary>
    public static class ResourceRegionLifecycleAdapter
    {
        private static readonly ResourceRegionLifecycleLedger Ledger = new ResourceRegionLifecycleLedger();
        private static readonly object SyncLock = new object();
        private static bool _registrationReady;
        private static ulong _currentSessionEpoch = 1UL;
        public const float DefaultHysteresisSeconds = 2.0f;

        public static bool RegistrationReady => _registrationReady;
        public static ulong CurrentSessionEpoch => _currentSessionEpoch;

        public static void SetRegistrationReady(bool ready)
        {
            lock (SyncLock)
            {
                _registrationReady = ready;
                if (ready)
                {
                    Ledger.BeginSession(_currentSessionEpoch);
                }
            }
        }

        public static void BeginSession(ulong sessionEpoch)
        {
            lock (SyncLock)
            {
                _currentSessionEpoch = sessionEpoch == 0UL ? 1UL : sessionEpoch;
                Ledger.BeginSession(_currentSessionEpoch);
            }
        }

        public static bool IsRegionActive(byte x, byte y)
        {
            lock (SyncLock)
            {
                return Ledger.IsRegionActive(x, y);
            }
        }

        public static uint OnObserverAcquire(int regionKey)
        {
            lock (SyncLock)
            {
                return Ledger.CommitAcquire(regionKey);
            }
        }

        public static ResourceReleaseLease OnObserverRelease(int regionKey, float now, float hysteresisSeconds = DefaultHysteresisSeconds)
        {
            lock (SyncLock)
            {
                return Ledger.ScheduleRelease(regionKey, now, hysteresisSeconds);
            }
        }

        public static bool CancelRelease(int regionKey)
        {
            lock (SyncLock)
            {
                return Ledger.CancelRelease(regionKey);
            }
        }

        public static bool TryCommitRelease(int regionKey, ulong sessionEpoch, uint generation, out uint committedGeneration)
        {
            lock (SyncLock)
            {
                return Ledger.TryCommitRelease(regionKey, sessionEpoch, generation, out committedGeneration);
            }
        }

        public static uint GetGeneration(int regionKey)
        {
            lock (SyncLock)
            {
                return Ledger.GetGeneration(regionKey);
            }
        }

        public static void OnObserverDisconnect(int regionKey)
        {
            lock (SyncLock)
            {
                Ledger.CleanObserverDisconnect(regionKey);
            }
        }

        public static void Tick()
        {
            // 周期性生命周期维护
        }
    }
}
