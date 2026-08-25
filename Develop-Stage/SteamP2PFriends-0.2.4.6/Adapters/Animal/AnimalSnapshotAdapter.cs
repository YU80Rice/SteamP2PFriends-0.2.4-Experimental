using SteamP2PFriends.Host;
using SteamP2PFriends.Shared;
using System;
using System.Collections.Generic;

namespace SteamP2PFriends.MultiObserver
{
    internal readonly struct AnimalSnapshotRecord
    {
        internal AnimalSnapshotRecord(ulong sessionEpoch, ulong connectionToken, byte bound, uint regionGeneration, uint deltaSequence)
        {
            SessionEpoch = sessionEpoch;
            ConnectionToken = connectionToken;
            Bound = bound;
            RegionGeneration = regionGeneration;
            DeltaSequence = deltaSequence;
        }

        internal ulong SessionEpoch { get; }
        internal ulong ConnectionToken { get; }
        internal byte Bound { get; }
        internal uint RegionGeneration { get; }
        internal uint DeltaSequence { get; }
    }

    internal sealed class AnimalSnapshotReplicationLedger
    {
        private readonly Dictionary<(ulong SteamId, byte Bound), AnimalSnapshotRecord> _snapshots =
            new Dictionary<(ulong SteamId, byte Bound), AnimalSnapshotRecord>();
        private readonly Dictionary<ulong, ulong> _connectionTokens = new Dictionary<ulong, ulong>();
        private readonly Dictionary<byte, uint> _regionGenerations = new Dictionary<byte, uint>();

        internal ulong SessionEpoch { get; private set; } = 1UL;
        internal int TrackedSnapshotCount => _snapshots.Count;

        internal void ResetSession(ulong epoch)
        {
            SessionEpoch = epoch == 0UL ? 1UL : epoch;
            _snapshots.Clear();
            _connectionTokens.Clear();
            _regionGenerations.Clear();
        }

        internal void UpdateRegionGeneration(byte bound, uint generation)
        {
            _regionGenerations[bound] = generation;
        }

        internal uint GetRegionGeneration(byte bound) =>
            _regionGenerations.TryGetValue(bound, out uint gen) ? gen : 0U;

        internal bool TryGetSnapshot(ulong steamId, byte bound, out AnimalSnapshotRecord snapshot) =>
            _snapshots.TryGetValue((steamId, bound), out snapshot);

        internal bool EnqueueInitialSnapshot(ulong steamId, ulong connectionToken, byte bound, uint regionGeneration)
        {
            if (steamId == 0UL || connectionToken == 0UL) return false;

            _connectionTokens[steamId] = connectionToken;
            var key = (steamId, bound);

            if (_snapshots.TryGetValue(key, out AnimalSnapshotRecord existing))
            {
                if (existing.SessionEpoch == SessionEpoch &&
                    existing.ConnectionToken == connectionToken &&
                    existing.RegionGeneration == regionGeneration)
                {
                    return false; // 已处于最新快照基线
                }
            }

            _snapshots[key] = new AnimalSnapshotRecord(
                SessionEpoch,
                connectionToken,
                bound,
                regionGeneration,
                1U);

            return true;
        }

        internal void OnObserverDisconnect(ulong steamId)
        {
            _connectionTokens.Remove(steamId);
            var keysToRemove = new List<(ulong SteamId, byte Bound)>();
            foreach (var key in _snapshots.Keys)
            {
                if (key.SteamId == steamId)
                {
                    keysToRemove.Add(key);
                }
            }
            foreach (var k in keysToRemove)
            {
                _snapshots.Remove(k);
            }
        }
    }

    /// <summary>
    /// 动物状态全量快照与增量同步适配器 (AnimalSnapshotAdapter)
    /// </summary>
    public static class AnimalSnapshotAdapter
    {
        private static readonly AnimalSnapshotReplicationLedger Ledger = new AnimalSnapshotReplicationLedger();
        private static readonly object SyncLock = new object();

        public static void ResetSession(ulong sessionEpoch)
        {
            lock (SyncLock)
            {
                Ledger.ResetSession(sessionEpoch);
            }
        }

        public static bool EnqueueInitialSnapshot(ulong steamId, ulong connectionToken, byte bound, uint regionGeneration)
        {
            lock (SyncLock)
            {
                return Ledger.EnqueueInitialSnapshot(steamId, connectionToken, bound, regionGeneration);
            }
        }

        public static void OnObserverDisconnect(ulong steamId)
        {
            lock (SyncLock)
            {
                Ledger.OnObserverDisconnect(steamId);
            }
        }

        public static void UpdateRegionGeneration(byte bound, uint generation)
        {
            lock (SyncLock)
            {
                Ledger.UpdateRegionGeneration(bound, generation);
            }
        }

        public static uint GetRegionGeneration(byte bound)
        {
            lock (SyncLock)
            {
                return Ledger.GetRegionGeneration(bound);
            }
        }
    }
}
