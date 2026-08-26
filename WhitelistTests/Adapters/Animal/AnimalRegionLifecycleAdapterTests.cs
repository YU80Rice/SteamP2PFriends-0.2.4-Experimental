using SteamP2PFriends.Adapters.Animal;
using SteamP2PFriends.Core.Identity;
using System;

namespace SteamP2PFriends.WhitelistTests
{
    public static class AnimalRegionLifecycleAdapterTests
    {
        public static bool Test_M5A01_FirstAcquireCreatesOneGeneration()
        {
            var ledger = new AnimalRegionLifecycleLedger();
            ledger.BeginSession(10UL);

            BoundKey bound = BoundKey.FromNative(3);
            uint gen = ledger.CommitAcquire(bound);
            return gen == 1U && ledger.GetGeneration(bound) == 1U;
        }

        public static bool Test_M5A02_GenerationCommitAdvancesMonotonically()
        {
            var ledger = new AnimalRegionLifecycleLedger();
            ledger.BeginSession(10UL);

            BoundKey bound = BoundKey.FromNative(2);
            uint gen1 = ledger.CommitAcquire(bound);
            uint gen2 = ledger.CommitAcquire(bound);

            return gen1 == 1U && gen2 == 2U && ledger.GetGeneration(bound) == 2U;
        }

        public static bool Test_M5A03_ReleaseUsesHysteresisDeadline()
        {
            var ledger = new AnimalRegionLifecycleLedger();
            ledger.BeginSession(10UL);
            BoundKey bound = BoundKey.FromNative(5);
            ledger.CommitAcquire(bound);

            float now = 100.0f;
            float hysteresis = 2.0f;
            AnimalReleaseLease lease = ledger.ScheduleRelease(bound, now, hysteresis);

            return lease.SessionEpoch == 10UL &&
                   lease.Bound == bound &&
                   lease.RegionGeneration == 1U &&
                   Math.Abs(lease.Deadline - 102.0f) < 0.001f &&
                   ledger.PendingReleaseCount == 1;
        }

        public static bool Test_M5A04_DemandCancelsRelease()
        {
            var ledger = new AnimalRegionLifecycleLedger();
            ledger.BeginSession(10UL);
            BoundKey bound = BoundKey.FromNative(5);
            ledger.CommitAcquire(bound);
            ledger.ScheduleRelease(bound, 100.0f, 2.0f);

            bool cancelled = ledger.CancelRelease(bound);
            return cancelled && ledger.PendingReleaseCount == 0;
        }

        public static bool Test_M5A05_StaleSessionCannotRelease()
        {
            var ledger = new AnimalRegionLifecycleLedger();
            ledger.BeginSession(10UL);
            BoundKey bound = BoundKey.FromNative(1);
            ledger.CommitAcquire(bound);
            ledger.ScheduleRelease(bound, 100.0f, 2.0f);

            bool committed = ledger.TryCommitRelease(bound, 9UL, 1U, out uint nextGen);
            return !committed && nextGen == 0U && ledger.GetGeneration(bound) == 1U;
        }

        public static bool Test_M5A06_StaleGenerationCannotRelease()
        {
            var ledger = new AnimalRegionLifecycleLedger();
            ledger.BeginSession(10UL);
            BoundKey bound = BoundKey.FromNative(1);
            ledger.CommitAcquire(bound);
            ledger.ScheduleRelease(bound, 100.0f, 2.0f);

            bool committed = ledger.TryCommitRelease(bound, 10UL, 99U, out uint nextGen);
            return !committed && nextGen == 0U && ledger.GetGeneration(bound) == 1U;
        }

        public static bool Test_M5A07_BoundsAreIndependent()
        {
            var ledger = new AnimalRegionLifecycleLedger();
            ledger.BeginSession(10UL);

            BoundKey bound1 = BoundKey.FromNative(1);
            BoundKey bound2 = BoundKey.FromNative(2);
            ledger.CommitAcquire(bound1);
            ledger.CommitAcquire(bound2);
            ledger.CommitAcquire(bound2);

            return ledger.GetGeneration(bound1) == 1U && ledger.GetGeneration(bound2) == 2U;
        }

        public static bool Test_M5A08_CommitReleaseIsIdempotentAndAdvancesGeneration()
        {
            var ledger = new AnimalRegionLifecycleLedger();
            ledger.BeginSession(10UL);
            BoundKey bound = BoundKey.FromNative(4);
            ledger.CommitAcquire(bound);
            ledger.ScheduleRelease(bound, 100.0f, 2.0f);

            bool committed1 = ledger.TryCommitRelease(bound, 10UL, 1U, out uint nextGen1);
            bool committed2 = ledger.TryCommitRelease(bound, 10UL, 1U, out uint nextGen2);

            return committed1 && nextGen1 == 2U && !committed2 && nextGen2 == 0U && ledger.GetGeneration(bound) == 2U;
        }

        public static bool Test_M5A09_DisconnectCleansObserverState()
        {
            var ledger = new AnimalRegionLifecycleLedger();
            ledger.BeginSession(10UL);
            BoundKey bound = BoundKey.FromNative(7);
            ledger.CommitAcquire(bound);
            ledger.ScheduleRelease(bound, 100.0f, 2.0f);
            ledger.CompareDemand(bound, 1, 2);

            ledger.CleanObserverDisconnect(bound);

            return ledger.PendingReleaseCount == 0 && !ledger.IsQuarantined(bound);
        }

        public static bool Test_M5A10_DemandMismatchTriggersQuarantine()
        {
            var ledger = new AnimalRegionLifecycleLedger();
            ledger.BeginSession(10UL);

            BoundKey bound = BoundKey.FromNative(3);
            AnimalLifecycleAction action1 = ledger.CompareDemand(bound, 1, 2);
            bool isQ1 = ledger.IsQuarantined(bound);

            AnimalLifecycleAction action2 = ledger.CompareDemand(bound, 2, 2);
            bool isQ2 = ledger.IsQuarantined(bound);

            return action1 == AnimalLifecycleAction.QuarantineMismatch &&
                   isQ1 &&
                   action2 == AnimalLifecycleAction.None &&
                   !isQ2;
        }
    }
}
