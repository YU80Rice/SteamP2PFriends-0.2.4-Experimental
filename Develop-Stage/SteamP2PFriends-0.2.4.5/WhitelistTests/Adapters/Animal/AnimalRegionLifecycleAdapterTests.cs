using SteamP2PFriends.MultiObserver;
using System;

namespace SteamP2PFriends.WhitelistTests
{
    public static class AnimalRegionLifecycleAdapterTests
    {
        public static bool Test_M5A01_FirstAcquireCreatesOneGeneration()
        {
            var ledger = new AnimalRegionLifecycleLedger();
            ledger.BeginSession(10UL);

            uint gen = ledger.CommitAcquire(3);
            return gen == 1U && ledger.GetGeneration(3) == 1U;
        }

        public static bool Test_M5A02_GenerationCommitAdvancesMonotonically()
        {
            var ledger = new AnimalRegionLifecycleLedger();
            ledger.BeginSession(10UL);

            uint gen1 = ledger.CommitAcquire(2);
            uint gen2 = ledger.CommitAcquire(2);

            return gen1 == 1U && gen2 == 2U && ledger.GetGeneration(2) == 2U;
        }

        public static bool Test_M5A03_ReleaseUsesHysteresisDeadline()
        {
            var ledger = new AnimalRegionLifecycleLedger();
            ledger.BeginSession(10UL);
            ledger.CommitAcquire(5);

            float now = 100.0f;
            float hysteresis = 2.0f;
            AnimalReleaseLease lease = ledger.ScheduleRelease(5, now, hysteresis);

            return lease.SessionEpoch == 10UL &&
                   lease.Bound == 5 &&
                   lease.RegionGeneration == 1U &&
                   Math.Abs(lease.Deadline - 102.0f) < 0.001f &&
                   ledger.PendingReleaseCount == 1;
        }

        public static bool Test_M5A04_DemandCancelsRelease()
        {
            var ledger = new AnimalRegionLifecycleLedger();
            ledger.BeginSession(10UL);
            ledger.CommitAcquire(5);
            ledger.ScheduleRelease(5, 100.0f, 2.0f);

            bool cancelled = ledger.CancelRelease(5);
            return cancelled && ledger.PendingReleaseCount == 0;
        }

        public static bool Test_M5A05_StaleSessionCannotRelease()
        {
            var ledger = new AnimalRegionLifecycleLedger();
            ledger.BeginSession(10UL);
            ledger.CommitAcquire(1);
            ledger.ScheduleRelease(1, 100.0f, 2.0f);

            bool committed = ledger.TryCommitRelease(1, 9UL, 1U, out uint nextGen);
            return !committed && nextGen == 0U && ledger.GetGeneration(1) == 1U;
        }

        public static bool Test_M5A06_StaleGenerationCannotRelease()
        {
            var ledger = new AnimalRegionLifecycleLedger();
            ledger.BeginSession(10UL);
            ledger.CommitAcquire(1);
            ledger.ScheduleRelease(1, 100.0f, 2.0f);

            bool committed = ledger.TryCommitRelease(1, 10UL, 99U, out uint nextGen);
            return !committed && nextGen == 0U && ledger.GetGeneration(1) == 1U;
        }

        public static bool Test_M5A07_BoundsAreIndependent()
        {
            var ledger = new AnimalRegionLifecycleLedger();
            ledger.BeginSession(10UL);

            ledger.CommitAcquire(1);
            ledger.CommitAcquire(2);
            ledger.CommitAcquire(2);

            return ledger.GetGeneration(1) == 1U && ledger.GetGeneration(2) == 2U;
        }

        public static bool Test_M5A08_CommitReleaseIsIdempotentAndAdvancesGeneration()
        {
            var ledger = new AnimalRegionLifecycleLedger();
            ledger.BeginSession(10UL);
            ledger.CommitAcquire(4);
            ledger.ScheduleRelease(4, 100.0f, 2.0f);

            bool committed1 = ledger.TryCommitRelease(4, 10UL, 1U, out uint nextGen1);
            bool committed2 = ledger.TryCommitRelease(4, 10UL, 1U, out uint nextGen2);

            return committed1 && nextGen1 == 2U && !committed2 && nextGen2 == 0U && ledger.GetGeneration(4) == 2U;
        }

        public static bool Test_M5A09_DisconnectCleansObserverState()
        {
            var ledger = new AnimalRegionLifecycleLedger();
            ledger.BeginSession(10UL);
            ledger.CommitAcquire(7);
            ledger.ScheduleRelease(7, 100.0f, 2.0f);
            ledger.CompareDemand(7, 1, 2);

            ledger.CleanObserverDisconnect(7);

            return ledger.PendingReleaseCount == 0 && !ledger.IsQuarantined(7);
        }

        public static bool Test_M5A10_DemandMismatchTriggersQuarantine()
        {
            var ledger = new AnimalRegionLifecycleLedger();
            ledger.BeginSession(10UL);

            AnimalLifecycleAction action1 = ledger.CompareDemand(3, 1, 2);
            bool isQ1 = ledger.IsQuarantined(3);

            AnimalLifecycleAction action2 = ledger.CompareDemand(3, 2, 2);
            bool isQ2 = ledger.IsQuarantined(3);

            return action1 == AnimalLifecycleAction.QuarantineMismatch &&
                   isQ1 &&
                   action2 == AnimalLifecycleAction.None &&
                   !isQ2;
        }
    }
}
