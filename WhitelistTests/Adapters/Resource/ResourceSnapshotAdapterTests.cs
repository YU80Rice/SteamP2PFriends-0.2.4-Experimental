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

            ledger.OnObserverDisconnect(76561198000000001UL);

            bool found1 = ledger.TryGetSnapshot(76561198000000001UL, r1, out _);
            bool found2 = ledger.TryGetSnapshot(76561198000000002UL, r1, out _);

            return !found1 && found2 && ledger.TrackedSnapshotCount == 1;
        }
    }
}
