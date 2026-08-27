using SteamP2PFriends.Adapters.Resource;
using SteamP2PFriends.Core.Identity;
using System;

namespace SteamP2PFriends.WhitelistTests
{
    public static class ResourceRegionLifecycleAdapterTests
    {
        public static bool Test_M6R01_FirstAcquireActivatesRegion()
        {
            var ledger = new ResourceRegionLifecycleLedger();
            ledger.BeginSession(1UL);

            RegionKey regionKey = new RegionKey(15, 25);
            uint gen = ledger.CommitAcquire(regionKey);

            return gen == 1U && ledger.IsRegionActive(15, 25) && ledger.ActiveRegionCount == 1;
        }

        public static bool Test_M6R02_ReleaseUsesHysteresisDeadline()
        {
            var ledger = new ResourceRegionLifecycleLedger();
            ledger.BeginSession(1UL);
            RegionKey regionKey = new RegionKey(10, 20);
            ledger.CommitAcquire(regionKey);

            ResourceReleaseLease lease = ledger.ScheduleRelease(regionKey, 100.0f, 2.0f);

            return lease.SessionEpoch == 1UL &&
                   lease.RegionKey == regionKey &&
                   lease.RegionGeneration == 1U &&
                   Math.Abs(lease.Deadline - 102.0f) < 0.001f &&
                   ledger.PendingReleaseCount == 1;
        }

        public static bool Test_M6R03_DemandCancelsRelease()
        {
            var ledger = new ResourceRegionLifecycleLedger();
            ledger.BeginSession(1UL);
            RegionKey regionKey = new RegionKey(5, 5);
            ledger.CommitAcquire(regionKey);
            ledger.ScheduleRelease(regionKey, 10.0f, 2.0f);

            bool cancelled = ledger.CancelRelease(regionKey);

            return cancelled && ledger.PendingReleaseCount == 0 && ledger.IsRegionActive(5, 5);
        }

        public static bool Test_M6R04_CommitReleaseAdvancesGenerationAndDeactivates()
        {
            var ledger = new ResourceRegionLifecycleLedger();
            ledger.BeginSession(1UL);
            RegionKey regionKey = new RegionKey(8, 8);
            ledger.CommitAcquire(regionKey);
            ledger.ScheduleRelease(regionKey, 10.0f, 2.0f);

            bool committed = ledger.TryCommitRelease(regionKey, 1UL, 1U, out uint nextGen);

            return committed && nextGen == 2U && !ledger.IsRegionActive(8, 8) && ledger.ActiveRegionCount == 0;
        }

        public static bool Test_M6R05_StaleSessionCannotCommitRelease()
        {
            var ledger = new ResourceRegionLifecycleLedger();
            ledger.BeginSession(2UL);
            RegionKey regionKey = new RegionKey(1, 1);
            ledger.CommitAcquire(regionKey);
            ledger.ScheduleRelease(regionKey, 10.0f, 2.0f);

            bool committed = ledger.TryCommitRelease(regionKey, 1UL, 1U, out uint nextGen);

            return !committed && nextGen == 0U && ledger.IsRegionActive(1, 1);
        }

        public static bool Test_M6R06_StaleGenerationCannotCommitRelease()
        {
            var ledger = new ResourceRegionLifecycleLedger();
            ledger.BeginSession(1UL);
            RegionKey regionKey = new RegionKey(2, 2);
            ledger.CommitAcquire(regionKey);
            ledger.ScheduleRelease(regionKey, 10.0f, 2.0f);

            bool committed = ledger.TryCommitRelease(regionKey, 1UL, 99U, out uint nextGen);

            return !committed && nextGen == 0U && ledger.IsRegionActive(2, 2);
        }

        public static bool Test_M6R07_MultipleRegionsAreIsolated()
        {
            var ledger = new ResourceRegionLifecycleLedger();
            ledger.BeginSession(1UL);

            RegionKey r1 = new RegionKey(10, 10);
            RegionKey r2 = new RegionKey(20, 20);

            ledger.CommitAcquire(r1);
            ledger.CommitAcquire(r2);

            return ledger.IsRegionActive(10, 10) &&
                   ledger.IsRegionActive(20, 20) &&
                   !ledger.IsRegionActive(10, 20) &&
                   ledger.ActiveRegionCount == 2;
        }

        public static bool Test_M6R08_DisconnectCleansObserverState()
        {
            var ledger = new ResourceRegionLifecycleLedger();
            ledger.BeginSession(1UL);
            RegionKey regionKey = new RegionKey(7, 7);
            ledger.CommitAcquire(regionKey);
            ledger.ScheduleRelease(regionKey, 10.0f, 2.0f);

            ledger.CleanObserverDisconnect(regionKey);

            return !ledger.IsRegionActive(7, 7) && ledger.PendingReleaseCount == 0;
        }

        public static bool Test_M6R09_EndSessionClearsResourceState()
        {
            var ledger = new ResourceRegionLifecycleLedger();
            ledger.BeginSession(1UL);
            RegionKey regionKey = new RegionKey(9, 9);
            ledger.CommitAcquire(regionKey);
            ledger.ScheduleRelease(regionKey, 10.0f, 2.0f);
            ledger.RecordResourceDead(regionKey, 4);

            ledger.EndSession();

            return ledger.ActiveRegionCount == 0
                && ledger.PendingReleaseCount == 0
                && !ledger.IsResourceDead(regionKey, 4)
                && ledger.GetGeneration(regionKey) == 0U;
        }
    }
}
