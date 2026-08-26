using SteamP2PFriends.Host;
using SteamP2PFriends.Shared;
using SteamP2PFriends.Core.Identity;
using System;
using System.Collections.Generic;

namespace SteamP2PFriends.Adapters.Resource
{
    public readonly struct ResourceSnapshotRecord
    {
        public ResourceSnapshotRecord(ulong sessionEpoch, ulong connectionToken, RegionKey regionKey, uint regionGeneration, uint deltaSequence)
        {
            SessionEpoch = sessionEpoch;
            ConnectionToken = connectionToken;
            RegionKey = regionKey;
            RegionGeneration = regionGeneration;
            DeltaSequence = deltaSequence;
        }

        public ulong SessionEpoch { get; }
        public ulong ConnectionToken { get; }
        public RegionKey RegionKey { get; }
        public uint RegionGeneration { get; }
        public uint DeltaSequence { get; }
    }

    public sealed class ResourceSnapshotReplicationLedger
    {
        private readonly Dictionary<(ulong SteamId, RegionKey RegionKey), ResourceSnapshotRecord> _snapshots =
            new Dictionary<(ulong SteamId, RegionKey RegionKey), ResourceSnapshotRecord>();
        private readonly Dictionary<ulong, ulong> _connectionTokens = new Dictionary<ulong, ulong>();
        private readonly Dictionary<RegionKey, uint> _regionGenerations = new Dictionary<RegionKey, uint>();

        public ulong SessionEpoch { get; private set; } = 1UL;
        public int TrackedSnapshotCount => _snapshots.Count;

        public void ResetSession(ulong epoch)
        {
            SessionEpoch = epoch == 0UL ? 1UL : epoch;
            _snapshots.Clear();
            _connectionTokens.Clear();
            _regionGenerations.Clear();
        }

        public void UpdateRegionGeneration(RegionKey regionKey, uint generation)
        {
            _regionGenerations[regionKey] = generation;
        }

        public uint GetRegionGeneration(RegionKey regionKey) =>
            _regionGenerations.TryGetValue(regionKey, out uint gen) ? gen : 0U;

        public bool TryGetSnapshot(ulong steamId, RegionKey regionKey, out ResourceSnapshotRecord snapshot) =>
            _snapshots.TryGetValue((steamId, regionKey), out snapshot);

        public bool EnqueueInitialSnapshot(ulong steamId, ulong connectionToken, RegionKey regionKey, uint regionGeneration)
        {
            if (steamId == 0UL || connectionToken == 0UL) return false;

            _connectionTokens[steamId] = connectionToken;
            var key = (steamId, regionKey);

            if (_snapshots.TryGetValue(key, out ResourceSnapshotRecord existing))
            {
                if (existing.SessionEpoch == SessionEpoch &&
                    existing.ConnectionToken == connectionToken &&
                    existing.RegionGeneration == regionGeneration)
                {
                    return false;
                }
            }

            _snapshots[key] = new ResourceSnapshotRecord(
                SessionEpoch,
                connectionToken,
                regionKey,
                regionGeneration,
                1U);

            return true;
        }

        public uint AdvanceDeltaSequence(ulong steamId, RegionKey regionKey)
        {
            var key = (steamId, regionKey);
            if (!_snapshots.TryGetValue(key, out ResourceSnapshotRecord current))
            {
                return 0U;
            }

            uint nextSeq = current.DeltaSequence == uint.MaxValue ? 1U : current.DeltaSequence + 1U;
            if (nextSeq == 0U) nextSeq = 1U;

            _snapshots[key] = new ResourceSnapshotRecord(
                current.SessionEpoch,
                current.ConnectionToken,
                current.RegionKey,
                current.RegionGeneration,
                nextSeq);

            return nextSeq;
        }

        public void OnObserverDisconnect(ulong steamId)
        {
            _connectionTokens.Remove(steamId);
            var keysToRemove = new List<(ulong SteamId, RegionKey RegionKey)>();
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
    /// 资源状态全量快照与采伐增量同步适配器 (ResourceSnapshotAdapter)
    /// </summary>
    public static class ResourceSnapshotAdapter
    {
        private static readonly ResourceSnapshotReplicationLedger Ledger = new ResourceSnapshotReplicationLedger();
        private static readonly object SyncLock = new object();

        public static void ResetSession(ulong sessionEpoch)
        {
            lock (SyncLock)
            {
                Ledger.ResetSession(sessionEpoch);
            }
        }

        public static bool EnqueueInitialSnapshot(ulong steamId, ulong connectionToken, RegionKey regionKey, uint regionGeneration)
        {
            lock (SyncLock)
            {
                return Ledger.EnqueueInitialSnapshot(steamId, connectionToken, regionKey, regionGeneration);
            }
        }

        public static uint AdvanceDeltaSequence(ulong steamId, RegionKey regionKey)
        {
            lock (SyncLock)
            {
                return Ledger.AdvanceDeltaSequence(steamId, regionKey);
            }
        }

        public static void OnObserverDisconnect(ulong steamId)
        {
            lock (SyncLock)
            {
                Ledger.OnObserverDisconnect(steamId);
            }
        }

        public static void UpdateRegionGeneration(RegionKey regionKey, uint generation)
        {
            lock (SyncLock)
            {
                Ledger.UpdateRegionGeneration(regionKey, generation);
            }
        }

        public static uint GetRegionGeneration(RegionKey regionKey)
        {
            lock (SyncLock)
            {
                return Ledger.GetRegionGeneration(regionKey);
            }
        }
    }
}
