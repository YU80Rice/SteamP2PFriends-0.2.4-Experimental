using SteamP2PFriends.Adapters.Animal;
using SteamP2PFriends.Core.Identity;
using System;

namespace SteamP2PFriends.WhitelistTests
{
    public static class AnimalSnapshotAdapterTests
    {
        public static bool Test_M5S01_InitialSnapshotEnqueued()
        {
            var ledger = new AnimalSnapshotReplicationLedger();
            ledger.ResetSession(1UL);

            BoundKey bound = BoundKey.FromNative(2);
            bool enqueued = ledger.EnqueueInitialSnapshot(76561198000000001UL, 101UL, bound, 1U);
            bool found = ledger.TryGetSnapshot(76561198000000001UL, bound, out AnimalSnapshotRecord record);

            return enqueued &&
                   found &&
                   record.SessionEpoch == 1UL &&
                   record.ConnectionToken == 101UL &&
                   record.Bound == bound &&
                   record.RegionGeneration == 1U &&
                   record.DeltaSequence == 1U;
        }

        public static bool Test_M5S02_DuplicateSnapshotIgnored()
        {
            var ledger = new AnimalSnapshotReplicationLedger();
            ledger.ResetSession(1UL);

            BoundKey bound = BoundKey.FromNative(2);
            bool first = ledger.EnqueueInitialSnapshot(76561198000000001UL, 101UL, bound, 1U);
            bool second = ledger.EnqueueInitialSnapshot(76561198000000001UL, 101UL, bound, 1U);

            return first && !second && ledger.TrackedSnapshotCount == 1;
        }

        public static bool Test_M5S03_StaleGenerationForcesResync()
        {
            var ledger = new AnimalSnapshotReplicationLedger();
            ledger.ResetSession(1UL);

            BoundKey bound = BoundKey.FromNative(2);
            ledger.EnqueueInitialSnapshot(76561198000000001UL, 101UL, bound, 1U);
            bool resynced = ledger.EnqueueInitialSnapshot(76561198000000001UL, 101UL, bound, 2U);

            bool found = ledger.TryGetSnapshot(76561198000000001UL, bound, out AnimalSnapshotRecord record);

            return resynced && found && record.RegionGeneration == 2U;
        }

        public static bool Test_M5S04_ReconnectInvalidatesOldSnapshotToken()
        {
            var ledger = new AnimalSnapshotReplicationLedger();
            ledger.ResetSession(1UL);

            BoundKey bound = BoundKey.FromNative(2);
            ledger.EnqueueInitialSnapshot(76561198000000001UL, 101UL, bound, 1U);
            bool reconnected = ledger.EnqueueInitialSnapshot(76561198000000001UL, 202UL, bound, 1U);

            bool found = ledger.TryGetSnapshot(76561198000000001UL, bound, out AnimalSnapshotRecord record);

            return reconnected && found && record.ConnectionToken == 202UL;
        }

        public static bool Test_M5S05_OverlappingObserversHaveIndependentSnapshots()
        {
            var ledger = new AnimalSnapshotReplicationLedger();
            ledger.ResetSession(1UL);

            BoundKey bound = BoundKey.FromNative(1);
            ledger.EnqueueInitialSnapshot(76561198000000001UL, 101UL, bound, 1U);
            ledger.EnqueueInitialSnapshot(76561198000000002UL, 102UL, bound, 1U);

            bool found1 = ledger.TryGetSnapshot(76561198000000001UL, bound, out var r1);
            bool found2 = ledger.TryGetSnapshot(76561198000000002UL, bound, out var r2);

            return found1 && found2 && r1.ConnectionToken == 101UL && r2.ConnectionToken == 102UL && ledger.TrackedSnapshotCount == 2;
        }

        public static bool Test_M5S06_BoundsDoNotCrossCommit()
        {
            var ledger = new AnimalSnapshotReplicationLedger();
            ledger.ResetSession(1UL);

            BoundKey bound1 = BoundKey.FromNative(1);
            BoundKey bound2 = BoundKey.FromNative(2);
            ledger.EnqueueInitialSnapshot(76561198000000001UL, 101UL, bound1, 1U);
            ledger.EnqueueInitialSnapshot(76561198000000001UL, 101UL, bound2, 5U);

            bool found1 = ledger.TryGetSnapshot(76561198000000001UL, bound1, out var r1);
            bool found2 = ledger.TryGetSnapshot(76561198000000001UL, bound2, out var r2);

            return found1 && found2 && r1.RegionGeneration == 1U && r2.RegionGeneration == 5U;
        }

        public static bool Test_M5S07_DisconnectCleansObserver()
        {
            var ledger = new AnimalSnapshotReplicationLedger();
            ledger.ResetSession(1UL);

            BoundKey bound1 = BoundKey.FromNative(1);
            BoundKey bound2 = BoundKey.FromNative(2);
            ledger.EnqueueInitialSnapshot(76561198000000001UL, 101UL, bound1, 1U);
            ledger.EnqueueInitialSnapshot(76561198000000001UL, 101UL, bound2, 1U);
            ledger.EnqueueInitialSnapshot(76561198000000002UL, 102UL, bound1, 1U);

            ledger.OnObserverDisconnect(76561198000000001UL);

            bool found1 = ledger.TryGetSnapshot(76561198000000001UL, bound1, out _);
            bool found2 = ledger.TryGetSnapshot(76561198000000002UL, bound1, out _);

            return !found1 && found2 && ledger.TrackedSnapshotCount == 1;
        }
    }
}
