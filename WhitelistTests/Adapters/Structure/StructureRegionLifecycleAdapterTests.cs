using SteamP2PFriends.Adapters.Structure;
using System;

namespace SteamP2PFriends.WhitelistTests
{
    public static class StructureRegionLifecycleAdapterTests
    {
        public static bool Test_M7S01_FirstAcquireCreatesGeneration()
        {
            var ledger = StructureRegionLifecycleAdapter.CreateLedgerForTests(2.0f);
            ledger.BeginSession(1);

            int key = StructureRegionLifecycleLedger.EncodeKey(8, 16);
            uint gen = ledger.AcquireObserver(key);

            return gen == 1 && ledger.GetGeneration(key) == 1 && ledger.IsRegionActive(key);
        }

        public static bool Test_M7S02_PlaceStructureAdvancesGeneration()
        {
            var ledger = StructureRegionLifecycleAdapter.CreateLedgerForTests(2.0f);
            ledger.BeginSession(1);

            int key = StructureRegionLifecycleLedger.EncodeKey(8, 16);
            ledger.AcquireObserver(key);

            uint genAfterPlace = ledger.RecordStructureChange(8, 16, 3);
            return genAfterPlace == 2 && ledger.GetGeneration(key) == 2 && ledger.GetDeltaSequence(key) == 1;
        }

        public static bool Test_M7S03_DamageStructureAdvancesGeneration()
        {
            var ledger = StructureRegionLifecycleAdapter.CreateLedgerForTests(2.0f);
            ledger.BeginSession(1);

            int key = StructureRegionLifecycleLedger.EncodeKey(8, 16);
            ledger.AcquireObserver(key);

            uint gen1 = ledger.RecordStructureChange(8, 16, 1);
            uint gen2 = ledger.RecordStructureChange(8, 16, 2);

            return gen1 == 2 && gen2 == 3 && ledger.GetGeneration(key) == 3 && ledger.GetDeltaSequence(key) == 2;
        }

        public static bool Test_M7S04_SalvageStructureAdvancesGeneration()
        {
            var ledger = StructureRegionLifecycleAdapter.CreateLedgerForTests(2.0f);
            ledger.BeginSession(1);

            int key = StructureRegionLifecycleLedger.EncodeKey(8, 16);
            ledger.AcquireObserver(key);

            uint genAfterSalvage = ledger.RecordStructureChange(8, 16, 0);
            return genAfterSalvage == 2 && ledger.GetGeneration(key) == 2;
        }

        public static bool Test_M7S05_ReleaseUsesHysteresisDeadline()
        {
            var ledger = StructureRegionLifecycleAdapter.CreateLedgerForTests(2.0f);
            ledger.BeginSession(1);

            int key = StructureRegionLifecycleLedger.EncodeKey(18, 22);
            uint gen = ledger.AcquireObserver(key);

            bool scheduled = ledger.ScheduleRelease(key, 100.0f);
            bool premature = ledger.CommitRelease(key, 101.0f, 1, gen);
            bool committed = ledger.CommitRelease(key, 102.0f, 1, gen);

            return scheduled && !premature && committed && !ledger.IsRegionActive(key) && ledger.GetGeneration(key) == 2;
        }

        public static bool Test_M7S06_StaleSessionCannotRelease()
        {
            var ledger = StructureRegionLifecycleAdapter.CreateLedgerForTests(2.0f);
            ledger.BeginSession(1);

            int key = StructureRegionLifecycleLedger.EncodeKey(25, 25);
            uint gen = ledger.AcquireObserver(key);
            ledger.ScheduleRelease(key, 50.0f);

            bool wrongSession = ledger.CommitRelease(key, 52.5f, 99, gen);
            return !wrongSession && ledger.IsRegionActive(key);
        }

        public static bool Test_M7S07_ReconnectInvalidationResync()
        {
            var ledger = StructureSnapshotAdapter.CreateLedgerForTests();
            int key = StructureRegionLifecycleLedger.EncodeKey(30, 40);

            bool first = ledger.ShouldReplicateSnapshot(5001, 1, key, 1);
            bool duplicate = ledger.ShouldReplicateSnapshot(5001, 1, key, 1);
            bool reconnected = ledger.ShouldReplicateSnapshot(5001, 2, key, 1);

            return first && !duplicate && reconnected;
        }

        public static bool Test_M7S08_DisconnectCleansObserverState()
        {
            var ledger = StructureSnapshotAdapter.CreateLedgerForTests();
            int key = StructureRegionLifecycleLedger.EncodeKey(30, 40);

            ledger.ShouldReplicateSnapshot(6001, 1, key, 1);
            ledger.RemoveObserver(6001);

            bool resync = ledger.ShouldReplicateSnapshot(6001, 1, key, 1);
            return resync;
        }
    }
}
