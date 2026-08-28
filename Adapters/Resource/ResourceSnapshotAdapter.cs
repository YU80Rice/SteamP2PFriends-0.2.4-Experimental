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

        public int NativeSnapshotWriteCount { get; private set; }
        public int NativeSnapshotReceiveCount { get; private set; }
        public int NativeDeltaCount { get; private set; }
        public int NativeDeltaReceiveCount { get; private set; }
        public int StaleDeltaRejectCount { get; private set; }

        public ulong SessionEpoch { get; private set; } = 1UL;
        public int TrackedSnapshotCount => _snapshots.Count;

        public void ResetSession(ulong epoch)
        {
            SessionEpoch = epoch == 0UL ? 1UL : epoch;
            _snapshots.Clear();
            _connectionTokens.Clear();
            _regionGenerations.Clear();
            NativeSnapshotWriteCount = 0;
            NativeSnapshotReceiveCount = 0;
            NativeDeltaCount = 0;
            NativeDeltaReceiveCount = 0;
            StaleDeltaRejectCount = 0;
        }

        public void UpdateRegionGeneration(RegionKey regionKey, uint generation)
        {
            if (_regionGenerations.TryGetValue(regionKey, out uint current)
                && generation < current)
            {
                return;
            }

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
            if (_regionGenerations.TryGetValue(regionKey, out uint latestGeneration)
                && current.RegionGeneration != latestGeneration)
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

        public bool TryAdvanceDeltaSequence(
            ulong steamId, ulong connectionToken, RegionKey regionKey, out uint nextSequence)
        {
            nextSequence = 0U;
            if (!_connectionTokens.TryGetValue(steamId, out ulong currentToken)
                || currentToken != connectionToken)
            {
                return false;
            }

            var key = (steamId, regionKey);
            if (!_snapshots.TryGetValue(key, out ResourceSnapshotRecord current)
                || current.ConnectionToken != connectionToken
                || (_regionGenerations.TryGetValue(regionKey, out uint latestGeneration)
                    && current.RegionGeneration != latestGeneration))
            {
                return false;
            }

            nextSequence = current.DeltaSequence == uint.MaxValue ? 1U : current.DeltaSequence + 1U;
            if (nextSequence == 0U) nextSequence = 1U;
            _snapshots[key] = new ResourceSnapshotRecord(
                current.SessionEpoch,
                current.ConnectionToken,
                current.RegionKey,
                current.RegionGeneration,
                nextSequence);
            return true;
        }

        public bool RemoveSnapshot(ulong steamId, ulong connectionToken, RegionKey regionKey)
        {
            if (!_connectionTokens.TryGetValue(steamId, out ulong currentToken)
                || currentToken != connectionToken)
            {
                return false;
            }

            return _snapshots.Remove((steamId, regionKey));
        }

        public bool OnObserverDisconnect(ulong steamId, ulong connectionToken)
        {
            if (!_connectionTokens.TryGetValue(steamId, out ulong currentToken)
                || currentToken != connectionToken)
            {
                return false;
            }

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

            return true;
        }

        public int RecordNativeSnapshotWrite(RegionKey regionKey)
        {
            NativeSnapshotWriteCount++;
            int trackedObservers = 0;
            foreach (var key in _snapshots.Keys)
            {
                if (key.RegionKey == regionKey)
                {
                    trackedObservers++;
                }
            }

            return trackedObservers;
        }

        public int RecordNativeSnapshotReceive()
        {
            NativeSnapshotReceiveCount++;
            return NativeSnapshotReceiveCount;
        }

        public int RecordNativeDeltaReceive()
        {
            NativeDeltaReceiveCount++;
            return NativeDeltaReceiveCount;
        }

        public int RecordNativeDelta(RegionKey regionKey, uint regionGeneration)
        {
            NativeDeltaCount++;

            if (_regionGenerations.TryGetValue(regionKey, out uint latestGeneration)
                && regionGeneration < latestGeneration)
            {
                StaleDeltaRejectCount++;
                return 0;
            }

            _regionGenerations[regionKey] = regionGeneration;

            int acceptedObservers = 0;
            var keys = new List<(ulong SteamId, RegionKey RegionKey)>();
            foreach (var key in _snapshots.Keys)
            {
                if (key.RegionKey == regionKey)
                {
                    keys.Add(key);
                }
            }

            foreach (var key in keys)
            {
                ResourceSnapshotRecord current = _snapshots[key];
                if (current.RegionGeneration > regionGeneration)
                {
                    StaleDeltaRejectCount++;
                    continue;
                }

                uint nextSequence = current.DeltaSequence == uint.MaxValue
                    ? 1U
                    : current.DeltaSequence + 1U;
                if (nextSequence == 0U) nextSequence = 1U;
                _snapshots[key] = new ResourceSnapshotRecord(
                    current.SessionEpoch,
                    current.ConnectionToken,
                    current.RegionKey,
                    regionGeneration,
                    nextSequence);
                acceptedObservers++;
            }

            return acceptedObservers;
        }
    }

    /// <summary>
    /// 资源状态全量快照与采伐增量同步适配器 (ResourceSnapshotAdapter)
    /// </summary>
    public static class ResourceSnapshotAdapter
    {
        private static readonly ResourceSnapshotReplicationLedger Ledger = new ResourceSnapshotReplicationLedger();
        private static readonly object SyncLock = new object();

        public static int NativeSnapshotWriteCount
        {
            get { lock (SyncLock) return Ledger.NativeSnapshotWriteCount; }
        }

        public static int NativeSnapshotReceiveCount
        {
            get { lock (SyncLock) return Ledger.NativeSnapshotReceiveCount; }
        }

        public static int NativeDeltaCount
        {
            get { lock (SyncLock) return Ledger.NativeDeltaCount; }
        }

        public static int NativeDeltaReceiveCount
        {
            get { lock (SyncLock) return Ledger.NativeDeltaReceiveCount; }
        }

        public static int StaleDeltaRejectCount
        {
            get { lock (SyncLock) return Ledger.StaleDeltaRejectCount; }
        }

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

        public static bool TryGetSnapshot(ulong steamId, RegionKey regionKey, out ResourceSnapshotRecord snapshot)
        {
            lock (SyncLock)
            {
                return Ledger.TryGetSnapshot(steamId, regionKey, out snapshot);
            }
        }

        public static uint AdvanceDeltaSequence(ulong steamId, RegionKey regionKey)
        {
            lock (SyncLock)
            {
                return Ledger.AdvanceDeltaSequence(steamId, regionKey);
            }
        }

        public static bool TryAdvanceDeltaSequence(
            ulong steamId, ulong connectionToken, RegionKey regionKey, out uint nextSequence)
        {
            lock (SyncLock)
            {
                return Ledger.TryAdvanceDeltaSequence(steamId, connectionToken, regionKey, out nextSequence);
            }
        }

        public static bool RemoveSnapshot(ulong steamId, ulong connectionToken, RegionKey regionKey)
        {
            lock (SyncLock)
            {
                return Ledger.RemoveSnapshot(steamId, connectionToken, regionKey);
            }
        }

        public static bool OnObserverDisconnect(ulong steamId, ulong connectionToken)
        {
            lock (SyncLock)
            {
                return Ledger.OnObserverDisconnect(steamId, connectionToken);
            }
        }

        public static int RecordNativeSnapshotWrite(RegionKey regionKey)
        {
            lock (SyncLock)
            {
                return Ledger.RecordNativeSnapshotWrite(regionKey);
            }
        }

        public static int RecordNativeSnapshotReceive()
        {
            lock (SyncLock)
            {
                return Ledger.RecordNativeSnapshotReceive();
            }
        }

        public static int RecordNativeDeltaReceive()
        {
            lock (SyncLock)
            {
                return Ledger.RecordNativeDeltaReceive();
            }
        }

        public static int RecordNativeDelta(RegionKey regionKey, uint regionGeneration)
        {
            lock (SyncLock)
            {
                return Ledger.RecordNativeDelta(regionKey, regionGeneration);
            }
        }

        public static void OnReplicationTick(float deltaTime)
        {
            if (deltaTime < 0f) throw new ArgumentOutOfRangeException(nameof(deltaTime));
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
