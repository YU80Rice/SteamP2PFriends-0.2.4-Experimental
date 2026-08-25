using SteamP2PFriends.Adapters.Structure;
using System;

namespace SteamP2PFriends.WhitelistTests
{
    public static class BarricadeRegionLifecycleAdapterTests
    {
        public static bool Test_M7B01_FirstAcquireCreatesGeneration()
        {
            var ledger = BarricadeRegionLifecycleAdapter.CreateLedgerForTests(2.0f);
            ledger.BeginSession(1);

            int key = BarricadeRegionLifecycleLedger.EncodeKey(10, 20, 0);
            uint gen = ledger.AcquireObserver(key);

            return gen == 1 && ledger.GetGeneration(key) == 1 && ledger.IsRegionActive(key);
        }

        public static bool Test_M7B02_PlaceBarricadeAdvancesGeneration()
        {
            var ledger = BarricadeRegionLifecycleAdapter.CreateLedgerForTests(2.0f);
            ledger.BeginSession(1);

            int key = BarricadeRegionLifecycleLedger.EncodeKey(10, 20, 0);
            ledger.AcquireObserver(key);

            uint genAfterPlace = ledger.RecordBarricadeChange(10, 20, 0, 5);
            return genAfterPlace == 2 && ledger.GetGeneration(key) == 2 && ledger.GetDeltaSequence(key) == 1;
        }

        public static bool Test_M7B03_DamageBarricadeAdvancesGeneration()
        {
            var ledger = BarricadeRegionLifecycleAdapter.CreateLedgerForTests(2.0f);
            ledger.BeginSession(1);

            int key = BarricadeRegionLifecycleLedger.EncodeKey(10, 20, 0);
            ledger.AcquireObserver(key);

            uint gen1 = ledger.RecordBarricadeChange(10, 20, 0, 1);
            uint gen2 = ledger.RecordBarricadeChange(10, 20, 0, 2);

            return gen1 == 2 && gen2 == 3 && ledger.GetGeneration(key) == 3 && ledger.GetDeltaSequence(key) == 2;
        }

        public static bool Test_M7B04_UpdateStateAdvancesDeltaSequence()
        {
            var ledger = BarricadeSnapshotAdapter.CreateLedgerForTests();
            int key = BarricadeRegionLifecycleLedger.EncodeKey(5, 5, 0);

            ledger.AdvanceDeltaSequence(key);
            uint seq1 = ledger.GetDeltaSequence(key);
            ledger.AdvanceDeltaSequence(key);
            uint seq2 = ledger.GetDeltaSequence(key);

            return seq1 == 1 && seq2 == 2;
        }

        public static bool Test_M7B05_ReleaseUsesHysteresisDeadline()
        {
            var ledger = BarricadeRegionLifecycleAdapter.CreateLedgerForTests(2.0f);
            ledger.BeginSession(1);

            int key = BarricadeRegionLifecycleLedger.EncodeKey(12, 14, 0);
            uint gen = ledger.AcquireObserver(key);

            bool scheduled = ledger.ScheduleRelease(key, 10.0f);
            bool premature = ledger.CommitRelease(key, 11.0f, 1, gen);
            bool committed = ledger.CommitRelease(key, 12.0f, 1, gen);

            return scheduled && !premature && committed && !ledger.IsRegionActive(key) && ledger.GetGeneration(key) == 2;
        }

        public static bool Test_M7B06_StaleSessionCannotRelease()
        {
            var ledger = BarricadeRegionLifecycleAdapter.CreateLedgerForTests(2.0f);
            ledger.BeginSession(1);

            int key = BarricadeRegionLifecycleLedger.EncodeKey(15, 15, 0);
            uint gen = ledger.AcquireObserver(key);
            ledger.ScheduleRelease(key, 10.0f);

            bool wrongSession = ledger.CommitRelease(key, 12.5f, 2, gen);
            return !wrongSession && ledger.IsRegionActive(key);
        }

        public static bool Test_M7B07_ReconnectInvalidationResync()
        {
            var ledger = BarricadeSnapshotAdapter.CreateLedgerForTests();
            int key = BarricadeRegionLifecycleLedger.EncodeKey(20, 20, 0);

            bool first = ledger.ShouldReplicateSnapshot(1001, 1, key, 1);
            bool duplicate = ledger.ShouldReplicateSnapshot(1001, 1, key, 1);
            bool reconnected = ledger.ShouldReplicateSnapshot(1001, 2, key, 1);

            return first && !duplicate && reconnected;
        }

        public static bool Test_M7B08_DisconnectCleansObserverState()
        {
            var ledger = BarricadeSnapshotAdapter.CreateLedgerForTests();
            int key = BarricadeRegionLifecycleLedger.EncodeKey(30, 30, 0);

            ledger.ShouldReplicateSnapshot(2001, 1, key, 1);
            ledger.RemoveObserver(2001);

            bool resync = ledger.ShouldReplicateSnapshot(2001, 1, key, 1);
            return resync;
        }
    }
}
