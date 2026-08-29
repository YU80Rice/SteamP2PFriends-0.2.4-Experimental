using SteamP2PFriends.Host;
using SteamP2PFriends.Shared;
using SteamP2PFriends.Core.Identity;
using System;
using System.Collections.Generic;

namespace SteamP2PFriends.Adapters.Resource
{
    public readonly struct ResourceDeltaReceiveObservation
    {
        public ResourceDeltaReceiveObservation(ulong sessionEpoch, ulong connectionGeneration,
            uint regionGeneration, uint deltaSequence, bool decisionAvailable, bool accepted, bool stale)
        {
            SessionEpoch = sessionEpoch;
            ConnectionGeneration = connectionGeneration;
            RegionGeneration = regionGeneration;
            DeltaSequence = deltaSequence;
            DecisionAvailable = decisionAvailable;
            Accepted = accepted;
            Stale = stale;
        }

        public ulong SessionEpoch { get; }
        public ulong ConnectionGeneration { get; }
        public uint RegionGeneration { get; }
        public uint DeltaSequence { get; }
        public bool DecisionAvailable { get; }
        public bool Accepted { get; }
        public bool Stale { get; }
    }

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

    /// <summary>
    /// 观察者复制账本的可逆断连快照。
    /// </summary>
    public sealed class ResourceObserverReplicationState
    {
        internal ResourceObserverReplicationState(
            bool hasConnectionToken,
            ulong connectionToken,
            ResourceSnapshotRecord[] snapshots)
        {
            HasConnectionToken = hasConnectionToken;
            ConnectionToken = connectionToken;
            Snapshots = snapshots ?? new ResourceSnapshotRecord[0];
        }

        public bool HasConnectionToken { get; }
        public ulong ConnectionToken { get; }
        public IReadOnlyList<ResourceSnapshotRecord> Snapshots { get; }
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

        public uint GetLatestDeltaSequence(RegionKey regionKey)
        {
            uint latest = 0U;
            foreach (var pair in _snapshots)
            {
                if (pair.Key.RegionKey == regionKey && pair.Value.DeltaSequence > latest)
                    latest = pair.Value.DeltaSequence;
            }
            return latest;
        }

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
            TryUpdateRegionGeneration(regionKey, generation, out _);
        }

        public bool TryUpdateRegionGeneration(RegionKey regionKey, uint generation, out string reason)
        {
            if (_regionGenerations.TryGetValue(regionKey, out uint current)
                && generation < current)
            {
                reason = "stale-region-generation";
                return false;
            }

            _regionGenerations[regionKey] = generation;
            reason = "none";
            return true;
        }

        public uint GetRegionGeneration(RegionKey regionKey) =>
            _regionGenerations.TryGetValue(regionKey, out uint gen) ? gen : 0U;

        public bool TryGetSnapshot(ulong steamId, RegionKey regionKey, out ResourceSnapshotRecord snapshot) =>
            _snapshots.TryGetValue((steamId, regionKey), out snapshot);

        public bool EnqueueInitialSnapshot(ulong steamId, ulong connectionToken, RegionKey regionKey, uint regionGeneration)
        {
            if (steamId == 0UL || connectionToken == 0UL) return false;

            if (_connectionTokens.TryGetValue(steamId, out ulong currentConnectionToken)
                && connectionToken < currentConnectionToken)
            {
                return false;
            }

            var key = (steamId, regionKey);

            if (_regionGenerations.TryGetValue(regionKey, out uint latestGeneration)
                && regionGeneration < latestGeneration)
            {
                return false;
            }

            if (_snapshots.TryGetValue(key, out ResourceSnapshotRecord existing))
            {
                if (existing.SessionEpoch == SessionEpoch &&
                    existing.ConnectionToken == connectionToken)
                {
                    if (regionGeneration <= existing.RegionGeneration)
                    {
                        return false;
                    }
                }
            }

            _connectionTokens[steamId] = connectionToken;
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

        public ResourceObserverReplicationState CaptureObserverState(ulong steamId)
        {
            bool hasToken = _connectionTokens.TryGetValue(steamId, out ulong token);
            var snapshots = new List<ResourceSnapshotRecord>();
            foreach (KeyValuePair<(ulong SteamId, RegionKey RegionKey), ResourceSnapshotRecord> pair in _snapshots)
            {
                if (pair.Key.SteamId == steamId) snapshots.Add(pair.Value);
            }
            return new ResourceObserverReplicationState(hasToken, token, snapshots.ToArray());
        }

        public void RestoreObserverState(ulong steamId, ResourceObserverReplicationState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));

            _connectionTokens.Remove(steamId);
            var keysToRemove = new List<(ulong SteamId, RegionKey RegionKey)>();
            foreach (var key in _snapshots.Keys)
            {
                if (key.SteamId == steamId) keysToRemove.Add(key);
            }
            foreach (var key in keysToRemove) _snapshots.Remove(key);

            if (!state.HasConnectionToken) return;
            _connectionTokens[steamId] = state.ConnectionToken;
            foreach (ResourceSnapshotRecord snapshot in state.Snapshots)
                _snapshots[(steamId, snapshot.RegionKey)] = snapshot;
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

        public ResourceDeltaReceiveObservation RecordNativeDeltaReceive(
            RegionKey regionKey, ulong connectionGeneration)
        {
            uint deltaSequence = checked((uint)RecordNativeDeltaReceive());
            return new ResourceDeltaReceiveObservation(
                SessionEpoch, connectionGeneration, GetRegionGeneration(regionKey),
                deltaSequence, decisionAvailable: false, accepted: false, stale: false);
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

        public static ulong CurrentSessionEpoch
        {
            get { lock (SyncLock) return Ledger.SessionEpoch; }
        }

        public static ulong LocalConnectionGeneration { get; private set; }

        public static bool BeginLocalConnection()
        {
            lock (SyncLock)
            {
                if (LocalConnectionGeneration == ulong.MaxValue)
                {
                    ResourceObservability.Error("[Guest]", "ConnectionGeneration", "-",
                        Ledger.SessionEpoch, LocalConnectionGeneration, 0U,
                        "Fallback", false, "failed",
                        "reason=connection-generation-overflow failClosed=true");
                    return false;
                }

                LocalConnectionGeneration++;
                if (LocalConnectionGeneration == 0UL) LocalConnectionGeneration = 1UL;
                ResourceObservability.Info("[Guest]", "ConnectionGeneration", "-",
                    Ledger.SessionEpoch, LocalConnectionGeneration, 0U, "Native", false,
                    "success", "source=Provider.onClientConnected");
                return true;
            }
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
            bool accepted;
            lock (SyncLock)
            {
                accepted = Ledger.EnqueueInitialSnapshot(steamId, connectionToken, regionKey, regionGeneration);
            }
            if (!accepted)
                ResourceObservability.Warn("[Host]", "SnapshotEnqueue", regionKey.ToString(),
                    CurrentSessionEpoch, connectionToken, regionGeneration, "Fallback", true,
                    "rejected", "reason=invalid-or-stale-snapshot-baseline observer=" + steamId);
            return accepted;
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
            uint next;
            lock (SyncLock)
            {
                next = Ledger.AdvanceDeltaSequence(steamId, regionKey);
            }
            if (next == 0U)
                ResourceObservability.Warn("[Host]", "DeltaSequence", regionKey.ToString(),
                    CurrentSessionEpoch, 0UL, GetRegionGeneration(regionKey), "Fallback", true,
                    "rejected", "reason=missing-or-stale-snapshot-baseline observer=" + steamId);
            return next;
        }

        public static bool TryAdvanceDeltaSequence(
            ulong steamId, ulong connectionToken, RegionKey regionKey, out uint nextSequence)
        {
            bool accepted;
            lock (SyncLock)
            {
                accepted = Ledger.TryAdvanceDeltaSequence(steamId, connectionToken, regionKey, out nextSequence);
            }
            if (!accepted)
                ResourceObservability.Warn("[Guest]", "DeltaSequence", regionKey.ToString(),
                    CurrentSessionEpoch, connectionToken, GetRegionGeneration(regionKey), "Fallback", false,
                    "rejected", "reason=connection-or-region-generation-mismatch observer=" + steamId);
            return accepted;
        }

        public static bool RemoveSnapshot(ulong steamId, ulong connectionToken, RegionKey regionKey)
        {
            bool removed;
            lock (SyncLock)
            {
                removed = Ledger.RemoveSnapshot(steamId, connectionToken, regionKey);
            }
            if (!removed)
                ResourceObservability.Warn("[Host]", "SnapshotRemove", regionKey.ToString(),
                    CurrentSessionEpoch, connectionToken, GetRegionGeneration(regionKey), "Fallback", true,
                    "rejected", "reason=connection-generation-mismatch-or-missing-snapshot observer=" + steamId);
            return removed;
        }

        public static bool OnObserverDisconnect(ulong steamId, ulong connectionToken)
        {
            bool removed;
            lock (SyncLock)
            {
                removed = Ledger.OnObserverDisconnect(steamId, connectionToken);
            }
            if (!removed)
                ResourceObservability.Warn("[Guest]", "ObserverDisconnect", "-",
                    CurrentSessionEpoch, connectionToken, 0U, "Fallback", false,
                    "rejected", "reason=connection-generation-mismatch observer=" + steamId);
            return removed;
        }

        public static ResourceObserverReplicationState CaptureObserverState(ulong steamId)
        {
            lock (SyncLock)
            {
                return Ledger.CaptureObserverState(steamId);
            }
        }

        public static void RestoreObserverState(ulong steamId, object state)
        {
            lock (SyncLock)
            {
                Ledger.RestoreObserverState(steamId, state as ResourceObserverReplicationState);
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

        public static ResourceDeltaReceiveObservation RecordNativeDeltaReceive(
            RegionKey regionKey, ulong connectionGeneration)
        {
            try
            {
                lock (SyncLock)
                {
                    return Ledger.RecordNativeDeltaReceive(regionKey, connectionGeneration);
                }
            }
            catch (Exception ex)
            {
                ResourceObservability.Error("[Guest]", "DeltaReceive", regionKey.ToString(),
                    CurrentSessionEpoch, connectionGeneration, GetRegionGeneration(regionKey),
                    "Fallback", false, "failed", "reason=delta-sequence-overflow exception=" + ex.GetType().Name);
                throw;
            }
        }

        public static uint GetLatestDeltaSequence(RegionKey regionKey)
        {
            lock (SyncLock)
            {
                return Ledger.GetLatestDeltaSequence(regionKey);
            }
        }

        public static int RecordNativeDelta(RegionKey regionKey, uint regionGeneration)
        {
            int accepted;
            bool staleRejectedThisCall;
            lock (SyncLock)
            {
                int staleRejectsBefore = Ledger.StaleDeltaRejectCount;
                accepted = Ledger.RecordNativeDelta(regionKey, regionGeneration);
                staleRejectedThisCall = Ledger.StaleDeltaRejectCount > staleRejectsBefore;
            }
            if (staleRejectedThisCall)
                ResourceObservability.Warn("[Host]", "DeltaWrite", regionKey.ToString(),
                    CurrentSessionEpoch, 0UL, regionGeneration, "Fallback", true,
                    "rejected", "reason=stale-region-generation");
            return accepted;
        }

        public static void OnReplicationTick(float deltaTime)
        {
            if (deltaTime < 0f)
            {
                ResourceObservability.Error("[Host]", "ReplicationTick", "-",
                    CurrentSessionEpoch, 0UL, 0U, "Fallback", true, "failed",
                    "reason=negative-delta-time");
                throw new ArgumentOutOfRangeException(nameof(deltaTime));
            }

            ResourceObservability.Info("[Host]", "ReplicationTick", "-",
                CurrentSessionEpoch, 0UL, 0U, "SPI", true, "observed",
                "implementation=ledger-only nativeTransport=ResourceManager");
        }

        public static void UpdateRegionGeneration(RegionKey regionKey, uint generation)
        {
            bool accepted;
            string reason;
            lock (SyncLock)
            {
                accepted = Ledger.TryUpdateRegionGeneration(regionKey, generation, out reason);
            }
            if (!accepted)
                ResourceObservability.Warn("[Shared]", "RegionGeneration", regionKey.ToString(),
                    CurrentSessionEpoch, 0UL, generation, "Fallback", true,
                    "rejected", "reason=" + reason);
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
