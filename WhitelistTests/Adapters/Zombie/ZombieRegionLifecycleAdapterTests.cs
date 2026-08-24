using SteamP2PFriends.MultiObserver;

namespace SteamP2PFriends.WhitelistTests
{
    internal static class ZombieRegionLifecycleAdapterTests
    {
        internal static bool Test_M3Z01_FirstAcquireCreatesOneGeneration()
        {
            var ledger = NewLedger();
            return ledger.CommitAcquire(7) == 1U && ledger.GetGeneration(7) == 1U;
        }

        internal static bool Test_M3Z02_RepeatedAcquireAdvancesOnlyOnRealCommit()
        {
            var ledger = NewLedger();
            ledger.CommitAcquire(7);
            return ledger.GetGeneration(7) == 1U;
        }

        internal static bool Test_M3Z03_ReleaseUsesHysteresis()
        {
            var ledger = NewLedger();
            ledger.CommitAcquire(7);
            ZombieReleaseLease lease = ledger.ScheduleRelease(7, 10f, 2f);
            return !ledger.CanCommitRelease(lease, 11.99f, 0)
                && ledger.CanCommitRelease(lease, 12f, 0);
        }

        internal static bool Test_M3Z04_DemandCancelsRelease()
        {
            var ledger = NewLedger();
            ledger.CommitAcquire(7);
            ZombieReleaseLease lease = ledger.ScheduleRelease(7, 10f, 2f);
            return !ledger.CanCommitRelease(lease, 13f, 1)
                && ledger.CancelRelease(7)
                && ledger.PendingReleaseCount == 0;
        }

        internal static bool Test_M3Z05_StaleSessionCannotRelease()
        {
            var ledger = NewLedger();
            ledger.CommitAcquire(7);
            ZombieReleaseLease lease = ledger.ScheduleRelease(7, 10f, 2f);
            ledger.BeginSession(2UL);
            return !ledger.CanCommitRelease(lease, 13f, 0)
                && !ledger.CommitRelease(lease);
        }

        internal static bool Test_M3Z06_StaleRegionGenerationCannotRelease()
        {
            var ledger = NewLedger();
            ledger.CommitAcquire(7);
            ZombieReleaseLease lease = ledger.ScheduleRelease(7, 10f, 2f);
            ledger.CommitAcquire(7);
            return !ledger.CanCommitRelease(lease, 13f, 0)
                && ledger.GetGeneration(7) == 2U;
        }

        internal static bool Test_M3Z07_NativeDemandIsComparedWithoutRewrite()
        {
            var ledger = NewLedger();
            return ledger.CompareDemand(7, 2, 1) == ZombieLifecycleAction.QuarantineMismatch
                && ledger.IsQuarantined(7)
                && ledger.GetGeneration(7) == 0U;
        }

        internal static bool Test_M3Z08_MismatchRecoveryClearsQuarantine()
        {
            var ledger = NewLedger();
            ledger.CompareDemand(7, 2, 1);
            return ledger.CompareDemand(7, 2, 2) == ZombieLifecycleAction.None
                && !ledger.IsQuarantined(7);
        }

        internal static bool Test_M3Z09_BoundsAreIndependent()
        {
            var ledger = NewLedger();
            ledger.CommitAcquire(7);
            ledger.CommitAcquire(8);
            ZombieReleaseLease lease = ledger.ScheduleRelease(7, 1f, 2f);
            return ledger.GetGeneration(7) == 1U
                && ledger.GetGeneration(8) == 1U
                && ledger.CanCommitRelease(lease, 3f, 0)
                && ledger.PendingReleaseCount == 1;
        }

        internal static bool Test_M3Z10_CommitReleaseIsIdempotent()
        {
            var ledger = NewLedger();
            ledger.CommitAcquire(7);
            ZombieReleaseLease lease = ledger.ScheduleRelease(7, 1f, 0f);
            return ledger.CommitRelease(lease)
                && !ledger.CommitRelease(lease)
                && ledger.PendingReleaseCount == 0;
        }

        internal static bool Test_M3Z11_NativeDemandMismatchDoesNotChangeLedgerGeneration()
        {
            var ledger = NewLedger();
            return ledger.CompareDemand(7, 1, 0) == ZombieLifecycleAction.QuarantineMismatch
                && ledger.GetGeneration(7) == 0U;
        }

        internal static bool Test_M3Z12_QuarantineBlocksReleaseUntilDemandRecovers()
        {
            var ledger = NewLedger();
            ledger.CommitAcquire(7);
            ZombieReleaseLease lease = ledger.ScheduleRelease(7, 1f, 0f);
            ledger.CompareDemand(7, 0, 1);
            bool blocked = !ledger.CanCommitRelease(lease, 2f, 0);
            ledger.CompareDemand(7, 0, 0);
            return blocked && ledger.CanCommitRelease(lease, 2f, 0);
        }

        private static ZombieRegionLifecycleLedger NewLedger()
        {
            ZombieRegionLifecycleLedger ledger = ZombieRegionLifecycleAdapter.CreateLedgerForTests();
            ledger.BeginSession(1UL);
            return ledger;
        }
    }
}
