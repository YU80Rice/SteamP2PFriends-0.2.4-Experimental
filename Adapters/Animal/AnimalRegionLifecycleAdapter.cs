using SDG.Unturned;
using SteamP2PFriends.Host;
using SteamP2PFriends.MultiObserver.SPI;
using SteamP2PFriends.Core.Identity;
using SteamP2PFriends.Shared;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SteamP2PFriends.Adapters.Animal
{
    public enum AnimalLifecycleAction : byte
    {
        None = 0,
        Acquire = 1,
        Hold = 2,
        ScheduleRelease = 3,
        CancelRelease = 4,
        CommitRelease = 5,
        QuarantineMismatch = 6
    }

    public readonly struct AnimalReleaseLease
    {
        public AnimalReleaseLease(ulong sessionEpoch, BoundKey bound, uint regionGeneration, float deadline)
        {
            SessionEpoch = sessionEpoch;
            Bound = bound;
            RegionGeneration = regionGeneration;
            Deadline = deadline;
        }

        public ulong SessionEpoch { get; }
        public BoundKey Bound { get; }
        public uint RegionGeneration { get; }
        public float Deadline { get; }
    }

    public sealed class AnimalRegionLifecycleLedger
    {
        private readonly Dictionary<BoundKey, uint> _generations = new Dictionary<BoundKey, uint>();
        private readonly Dictionary<BoundKey, AnimalReleaseLease> _releases = new Dictionary<BoundKey, AnimalReleaseLease>();
        private readonly HashSet<BoundKey> _quarantined = new HashSet<BoundKey>();

        public ulong SessionEpoch { get; private set; }
        public int PendingReleaseCount => _releases.Count;
        public int QuarantinedBoundCount => _quarantined.Count;

        public void BeginSession(ulong epoch)
        {
            if (epoch == 0UL) throw new ArgumentOutOfRangeException(nameof(epoch));
            if (SessionEpoch == epoch) return;
            SessionEpoch = epoch;
            _generations.Clear();
            _releases.Clear();
            _quarantined.Clear();
        }

        public uint GetGeneration(BoundKey bound) =>
            _generations.TryGetValue(bound, out uint generation) ? generation : 0U;

        public uint CommitAcquire(BoundKey bound)
        {
            CancelRelease(bound);
            uint next = GetGeneration(bound);
            next = next == uint.MaxValue ? 1U : next + 1U;
            if (next == 0U) next = 1U;
            _generations[bound] = next;
            return next;
        }

        public AnimalLifecycleAction CompareDemand(BoundKey bound, int nativeDemand, int observerDemand)
        {
            if (nativeDemand < 0 || observerDemand < 0)
                throw new ArgumentOutOfRangeException("Demand cannot be negative.");
            if (nativeDemand == observerDemand)
            {
                _quarantined.Remove(bound);
                return AnimalLifecycleAction.None;
            }
            _quarantined.Add(bound);
            return AnimalLifecycleAction.QuarantineMismatch;
        }

        public bool IsQuarantined(BoundKey bound) => _quarantined.Contains(bound);

        public AnimalReleaseLease ScheduleRelease(BoundKey bound, float now, float hysteresisSeconds)
        {
            var lease = new AnimalReleaseLease(
                SessionEpoch,
                bound,
                GetGeneration(bound),
                now + Math.Max(0f, hysteresisSeconds));
            _releases[bound] = lease;
            return lease;
        }

        public bool CancelRelease(BoundKey bound) => _releases.Remove(bound);

        public bool TryGetRelease(BoundKey bound, out AnimalReleaseLease lease) =>
            _releases.TryGetValue(bound, out lease);

        public bool TryCommitRelease(BoundKey bound, ulong expectedEpoch, uint expectedGeneration, out uint committedGeneration)
        {
            committedGeneration = 0U;
            if (!_releases.TryGetValue(bound, out AnimalReleaseLease lease))
                return false;
            if (lease.SessionEpoch != expectedEpoch)
                return false;
            if (lease.RegionGeneration != expectedGeneration)
                return false;
            if (GetGeneration(bound) != expectedGeneration)
                return false;

            _releases.Remove(bound);
            uint next = expectedGeneration == uint.MaxValue ? 1U : expectedGeneration + 1U;
            if (next == 0U) next = 1U;
            _generations[bound] = next;
            committedGeneration = next;
            return true;
        }

        public void CleanObserverDisconnect(BoundKey bound)
        {
            _releases.Remove(bound);
            _quarantined.Remove(bound);
        }
    }

    /// <summary>
    /// 动物区域生命周期协调适配器 (AnimalRegionLifecycleAdapter)
    /// 管理野生动物导航区域（Navmesh Bounds）的按需生成与滞回释放。
    /// </summary>
    public static class AnimalRegionLifecycleAdapter
    {
        private static readonly AnimalRegionLifecycleLedger Ledger = new AnimalRegionLifecycleLedger();
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

        public static uint OnObserverAcquire(BoundKey bound)
        {
            lock (SyncLock)
            {
                return Ledger.CommitAcquire(bound);
            }
        }

        public static AnimalReleaseLease OnObserverRelease(BoundKey bound, float now, float hysteresisSeconds = DefaultHysteresisSeconds)
        {
            lock (SyncLock)
            {
                return Ledger.ScheduleRelease(bound, now, hysteresisSeconds);
            }
        }

        public static bool CancelRelease(BoundKey bound)
        {
            lock (SyncLock)
            {
                return Ledger.CancelRelease(bound);
            }
        }

        public static bool TryCommitRelease(BoundKey bound, ulong sessionEpoch, uint generation, out uint committedGeneration)
        {
            lock (SyncLock)
            {
                return Ledger.TryCommitRelease(bound, sessionEpoch, generation, out committedGeneration);
            }
        }

        public static uint GetGeneration(BoundKey bound)
        {
            lock (SyncLock)
            {
                return Ledger.GetGeneration(bound);
            }
        }

        public static void OnObserverDisconnect(BoundKey bound)
        {
            lock (SyncLock)
            {
                Ledger.CleanObserverDisconnect(bound);
            }
        }

        public static void Tick()
        {
            // 运行时心跳与倒计时推进
        }
    }
}
