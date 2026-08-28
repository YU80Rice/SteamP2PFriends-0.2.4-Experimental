using SteamP2PFriends.Adapters.Resource;
using SteamP2PFriends.Core.Identity;
using System;

namespace SteamP2PFriends.WhitelistTests
{
    public static class ResourceSnapshotAdapterTests
    {
        public static bool Test_M6S01_InitialSnapshotEnqueued()
        {
            var ledger = new ResourceSnapshotReplicationLedger();
            ledger.ResetSession(1UL);

            RegionKey regionKey = new RegionKey(12, 34);
            bool enqueued = ledger.EnqueueInitialSnapshot(76561198000000001UL, 101UL, regionKey, 1U);
            bool found = ledger.TryGetSnapshot(76561198000000001UL, regionKey, out ResourceSnapshotRecord record);

            return enqueued &&
                   found &&
                   record.SessionEpoch == 1UL &&
                   record.ConnectionToken == 101UL &&
                   record.RegionKey == regionKey &&
                   record.RegionGeneration == 1U &&
                   record.DeltaSequence == 1U;
        }

        public static bool Test_M6S02_DuplicateSnapshotIgnored()
        {
            var ledger = new ResourceSnapshotReplicationLedger();
            ledger.ResetSession(1UL);

            RegionKey regionKey = new RegionKey(10, 20);
            bool first = ledger.EnqueueInitialSnapshot(76561198000000001UL, 101UL, regionKey, 1U);
            bool second = ledger.EnqueueInitialSnapshot(76561198000000001UL, 101UL, regionKey, 1U);

            return first && !second && ledger.TrackedSnapshotCount == 1;
        }

        public static bool Test_M6S03_StaleGenerationForcesResync()
        {
            var ledger = new ResourceSnapshotReplicationLedger();
            ledger.ResetSession(1UL);

            RegionKey regionKey = new RegionKey(5, 5);
            ledger.EnqueueInitialSnapshot(76561198000000001UL, 101UL, regionKey, 1U);
            bool resynced = ledger.EnqueueInitialSnapshot(76561198000000001UL, 101UL, regionKey, 2U);

            bool found = ledger.TryGetSnapshot(76561198000000001UL, regionKey, out ResourceSnapshotRecord record);

            return resynced && found && record.RegionGeneration == 2U;
        }

        public static bool Test_M6S04_ReconnectInvalidatesOldSnapshotToken()
        {
            var ledger = new ResourceSnapshotReplicationLedger();
            ledger.ResetSession(1UL);

            RegionKey regionKey = new RegionKey(8, 8);
            ledger.EnqueueInitialSnapshot(76561198000000001UL, 101UL, regionKey, 1U);
            bool reconnected = ledger.EnqueueInitialSnapshot(76561198000000001UL, 202UL, regionKey, 1U);

            bool found = ledger.TryGetSnapshot(76561198000000001UL, regionKey, out ResourceSnapshotRecord record);

            return reconnected && found && record.ConnectionToken == 202UL;
        }

        public static bool Test_M6S05_OverlappingObserversHaveIndependentSnapshots()
        {
            var ledger = new ResourceSnapshotReplicationLedger();
            ledger.ResetSession(1UL);

            RegionKey regionKey = new RegionKey(1, 1);
            ledger.EnqueueInitialSnapshot(76561198000000001UL, 101UL, regionKey, 1U);
            ledger.EnqueueInitialSnapshot(76561198000000002UL, 102UL, regionKey, 1U);

            bool found1 = ledger.TryGetSnapshot(76561198000000001UL, regionKey, out var r1);
            bool found2 = ledger.TryGetSnapshot(76561198000000002UL, regionKey, out var r2);

            return found1 && found2 && r1.ConnectionToken == 101UL && r2.ConnectionToken == 102UL && ledger.TrackedSnapshotCount == 2;
        }

        public static bool Test_M6S06_DisconnectCleansObserver()
        {
            var ledger = new ResourceSnapshotReplicationLedger();
            ledger.ResetSession(1UL);

            RegionKey r1 = new RegionKey(1, 1);
            RegionKey r2 = new RegionKey(2, 2);

            ledger.EnqueueInitialSnapshot(76561198000000001UL, 101UL, r1, 1U);
            ledger.EnqueueInitialSnapshot(76561198000000001UL, 101UL, r2, 1U);
            ledger.EnqueueInitialSnapshot(76561198000000002UL, 102UL, r1, 1U);

            bool disconnected = ledger.OnObserverDisconnect(76561198000000001UL, 101UL);

            bool found1 = ledger.TryGetSnapshot(76561198000000001UL, r1, out _);
            bool found2 = ledger.TryGetSnapshot(76561198000000002UL, r1, out _);

            return disconnected && !found1 && found2 && ledger.TrackedSnapshotCount == 1;
        }

        public static bool Test_M6S07_StaleDisconnectDoesNotClearReconnectedObserver()
        {
            var ledger = new ResourceSnapshotReplicationLedger();
            ledger.ResetSession(1UL);

            ulong steamId = 76561198000000001UL;
            RegionKey regionKey = new RegionKey(3, 3);
            ledger.EnqueueInitialSnapshot(steamId, 101UL, regionKey, 1U);
            ledger.EnqueueInitialSnapshot(steamId, 202UL, regionKey, 1U);

            bool staleIgnored = !ledger.OnObserverDisconnect(steamId, 101UL);
            bool currentPreserved = ledger.TryGetSnapshot(steamId, regionKey, out ResourceSnapshotRecord record)
                && record.ConnectionToken == 202UL;

            return staleIgnored && currentPreserved && ledger.TrackedSnapshotCount == 1;
        }

        public static bool Test_M6S08_NativeDataPlaneEventsAreRecordedThroughResourceLedger()
        {
            var ledger = new ResourceSnapshotReplicationLedger();
            ledger.ResetSession(1UL);

            ulong steamId = 76561198000000001UL;
            RegionKey regionKey = new RegionKey(11, 12);
            ledger.EnqueueInitialSnapshot(steamId, 101UL, regionKey, 1U);
            ledger.UpdateRegionGeneration(regionKey, 1U);

            int snapshotObservers = ledger.RecordNativeSnapshotWrite(regionKey);
            int receiveCount = ledger.RecordNativeSnapshotReceive();
            int firstDeltaReceive = ledger.RecordNativeDeltaReceive();
            int secondDeltaReceive = ledger.RecordNativeDeltaReceive();

            int acceptedDeltaCount = ledger.RecordNativeDelta(regionKey, 1U);
            int staleDeltaCount = ledger.RecordNativeDelta(regionKey, 0U);
            bool staleRejected = ledger.StaleDeltaRejectCount == 1;

            ledger.UpdateRegionGeneration(regionKey, 3U);
            ledger.UpdateRegionGeneration(regionKey, 2U);
            bool generationRemainsMonotonic = ledger.GetRegionGeneration(regionKey) == 3U;
            int nextGenerationDeltaCount = ledger.RecordNativeDelta(regionKey, 3U);
            bool acceptedGenerationAdvanced = ledger.TryGetSnapshot(
                steamId, regionKey, out ResourceSnapshotRecord latest)
                && latest.RegionGeneration == 3U
                && latest.DeltaSequence == 3U;

            return snapshotObservers == 1
                && receiveCount == 1
                && firstDeltaReceive == 1
                && secondDeltaReceive == 2
                && staleDeltaCount == 0
                && staleRejected
                && generationRemainsMonotonic
                && acceptedDeltaCount == 1
                && nextGenerationDeltaCount == 1
                && acceptedGenerationAdvanced
                && ledger.NativeSnapshotWriteCount == 1
                && ledger.NativeSnapshotReceiveCount == 1
                && ledger.NativeDeltaReceiveCount == 2
                && ledger.NativeDeltaCount == 3;
        }

        public static bool Test_M6S09_OldBaselineCannotOverwriteNewerBaseline()
        {
            var ledger = new ResourceSnapshotReplicationLedger();
            ledger.ResetSession(1UL);

            ulong steamId = 76561198000000001UL;
            RegionKey regionKey = new RegionKey(21, 22);
            ledger.EnqueueInitialSnapshot(steamId, 101UL, regionKey, 1U);
            ledger.UpdateRegionGeneration(regionKey, 3U);

            bool currentAccepted = ledger.EnqueueInitialSnapshot(steamId, 101UL, regionKey, 3U);
            bool oldRejected = !ledger.EnqueueInitialSnapshot(steamId, 101UL, regionKey, 2U);
            bool preserved = ledger.TryGetSnapshot(steamId, regionKey, out ResourceSnapshotRecord snapshot)
                && snapshot.RegionGeneration == 3U
                && snapshot.DeltaSequence == 1U;

            return currentAccepted && oldRejected && preserved;
        }

        public static bool Test_M6S10_RegionGenerationOverflowFailsClosed()
        {
            bool advanced = ResourceGenerationRules.TryAdvance(uint.MaxValue, out uint next);
            return !advanced && next == uint.MaxValue;
        }
    }
}
