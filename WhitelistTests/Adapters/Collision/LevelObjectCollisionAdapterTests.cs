using SteamP2PFriends.Adapters.Collision;
using System;

namespace SteamP2PFriends.WhitelistTests
{
    public static class LevelObjectCollisionAdapterTests
    {
        public static bool Test_M6C01_FirstAcquireActivatesRegion()
        {
            var ledger = new LevelObjectCollisionLedger();
            ledger.BeginSession(1UL);

            int regionKey = (12 << 8) | 34;
            uint gen = ledger.CommitAcquire(regionKey);

            return gen == 1U && ledger.IsRegionActive(12, 34) && ledger.ActiveRegionCount == 1;
        }

        public static bool Test_M6C02_ReleaseUsesHysteresisDeadline()
        {
            var ledger = new LevelObjectCollisionLedger();
            ledger.BeginSession(1UL);
            int regionKey = (10 << 8) | 20;
            ledger.CommitAcquire(regionKey);

            CollisionReleaseLease lease = ledger.ScheduleRelease(regionKey, 50.0f, 2.0f);

            return lease.SessionEpoch == 1UL &&
                   lease.RegionKey == regionKey &&
                   lease.RegionGeneration == 1U &&
                   Math.Abs(lease.Deadline - 52.0f) < 0.001f &&
                   ledger.PendingReleaseCount == 1;
        }

        public static bool Test_M6C03_DemandCancelsRelease()
        {
            var ledger = new LevelObjectCollisionLedger();
            ledger.BeginSession(1UL);
            int regionKey = (5 << 8) | 5;
            ledger.CommitAcquire(regionKey);
            ledger.ScheduleRelease(regionKey, 10.0f, 2.0f);

            bool cancelled = ledger.CancelRelease(regionKey);

            return cancelled && ledger.PendingReleaseCount == 0 && ledger.IsRegionActive(5, 5);
        }

        public static bool Test_M6C04_CommitReleaseAdvancesGenerationAndDeactivates()
        {
            var ledger = new LevelObjectCollisionLedger();
            ledger.BeginSession(1UL);
            int regionKey = (8 << 8) | 8;
            ledger.CommitAcquire(regionKey);
            ledger.ScheduleRelease(regionKey, 10.0f, 2.0f);

            bool committed = ledger.TryCommitRelease(regionKey, 1UL, 1U, out uint nextGen);

            return committed && nextGen == 2U && !ledger.IsRegionActive(8, 8) && ledger.ActiveRegionCount == 0;
        }

        public static bool Test_M6C05_StaleSessionCannotCommitRelease()
        {
            var ledger = new LevelObjectCollisionLedger();
            ledger.BeginSession(2UL);
            int regionKey = (1 << 8) | 1;
            ledger.CommitAcquire(regionKey);
            ledger.ScheduleRelease(regionKey, 10.0f, 2.0f);

            bool committed = ledger.TryCommitRelease(regionKey, 1UL, 1U, out uint nextGen);

            return !committed && nextGen == 0U && ledger.IsRegionActive(1, 1);
        }

        public static bool Test_M6C06_StaleGenerationCannotCommitRelease()
        {
            var ledger = new LevelObjectCollisionLedger();
            ledger.BeginSession(1UL);
            int regionKey = (2 << 8) | 2;
            ledger.CommitAcquire(regionKey);
            ledger.ScheduleRelease(regionKey, 10.0f, 2.0f);

            bool committed = ledger.TryCommitRelease(regionKey, 1UL, 99U, out uint nextGen);

            return !committed && nextGen == 0U && ledger.IsRegionActive(2, 2);
        }

        public static bool Test_M6C07_MultipleRegionsAreIsolated()
        {
            var ledger = new LevelObjectCollisionLedger();
            ledger.BeginSession(1UL);

            int r1 = (10 << 8) | 10;
            int r2 = (20 << 8) | 20;

            ledger.CommitAcquire(r1);
            ledger.CommitAcquire(r2);

            return ledger.IsRegionActive(10, 10) &&
                   ledger.IsRegionActive(20, 20) &&
                   !ledger.IsRegionActive(10, 20) &&
                   ledger.ActiveRegionCount == 2;
        }

        public static bool Test_M6C08_DisconnectCleansObserverState()
        {
            var ledger = new LevelObjectCollisionLedger();
            ledger.BeginSession(1UL);
            int regionKey = (7 << 8) | 7;
            ledger.CommitAcquire(regionKey);
            ledger.ScheduleRelease(regionKey, 10.0f, 2.0f);

            ledger.CleanObserverDisconnect(regionKey);

            return !ledger.IsRegionActive(7, 7) && ledger.PendingReleaseCount == 0;
        }
    }
}
