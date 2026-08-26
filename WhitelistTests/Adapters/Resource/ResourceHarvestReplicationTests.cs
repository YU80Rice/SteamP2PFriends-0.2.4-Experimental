using SteamP2PFriends.Adapters.Resource;
using SteamP2PFriends.Core.Identity;
using System;
using System.Collections.Generic;

namespace SteamP2PFriends.WhitelistTests
{
    public static class ResourceHarvestReplicationTests
    {
        public static bool Test_M6H01_ResourceDeadAdvancesGeneration()
        {
            var ledger = new ResourceRegionLifecycleLedger();
            ledger.BeginSession(1UL);

            RegionKey regionKey = new RegionKey(12, 34);
            ledger.CommitAcquire(regionKey);

            uint genBefore = ledger.GetGeneration(regionKey);
            uint genAfter = ledger.RecordResourceDead(regionKey, 5);

            return genAfter > genBefore &&
                   ledger.IsResourceDead(regionKey, 5) &&
                   !ledger.IsResourceDead(regionKey, 6);
        }

        public static bool Test_M6H02_MultipleResourcesDeadTrackedIndependently()
        {
            var ledger = new ResourceRegionLifecycleLedger();
            ledger.BeginSession(1UL);

            RegionKey regionKey = new RegionKey(10, 20);
            ledger.CommitAcquire(regionKey);

            ledger.RecordResourceDead(regionKey, 1);
            ledger.RecordResourceDead(regionKey, 2);
            ledger.RecordResourceDead(regionKey, 99);

            HashSet<ushort> deadSet = ledger.GetDeadResourceIndices(regionKey);

            return deadSet.Count == 3 &&
                   deadSet.Contains(1) &&
                   deadSet.Contains(2) &&
                   deadSet.Contains(99) &&
                   !deadSet.Contains(3);
        }

        public static bool Test_M6H03_ResourceAliveRevivesAndAdvancesGeneration()
        {
            var ledger = new ResourceRegionLifecycleLedger();
            ledger.BeginSession(1UL);

            RegionKey regionKey = new RegionKey(5, 5);
            ledger.CommitAcquire(regionKey);
            ledger.RecordResourceDead(regionKey, 42);

            uint genDead = ledger.GetGeneration(regionKey);
            uint genAlive = ledger.RecordResourceAlive(regionKey, 42);

            return genAlive > genDead &&
                   !ledger.IsResourceDead(regionKey, 42) &&
                   ledger.GetDeadResourceIndices(regionKey).Count == 0;
        }

        public static bool Test_M6H04_SnapshotIncludesDeadList()
        {
            var lifecycle = new ResourceRegionLifecycleLedger();
            lifecycle.BeginSession(1UL);
            var snapshot = new ResourceSnapshotReplicationLedger();
            snapshot.ResetSession(1UL);

            RegionKey regionKey = new RegionKey(8, 8);
            lifecycle.CommitAcquire(regionKey);
            uint gen = lifecycle.RecordResourceDead(regionKey, 10);
            snapshot.UpdateRegionGeneration(regionKey, gen);

            bool enqueued = snapshot.EnqueueInitialSnapshot(76561198000000001UL, 101UL, regionKey, gen);
            bool found = snapshot.TryGetSnapshot(76561198000000001UL, regionKey, out var record);

            return enqueued &&
                   found &&
                   record.RegionGeneration == gen &&
                   record.DeltaSequence == 1U;
        }

        public static bool Test_M6H05_DeltaSequenceAdvancesOnHarvest()
        {
            var snapshot = new ResourceSnapshotReplicationLedger();
            snapshot.ResetSession(1UL);

            RegionKey regionKey = new RegionKey(1, 1);
            snapshot.EnqueueInitialSnapshot(76561198000000001UL, 101UL, regionKey, 1U);

            uint seq1 = snapshot.AdvanceDeltaSequence(76561198000000001UL, regionKey);
            uint seq2 = snapshot.AdvanceDeltaSequence(76561198000000001UL, regionKey);

            return seq1 == 2U && seq2 == 3U;
        }

        public static bool Test_M6H06_ReconnectGetsUpdatedGeneration()
        {
            var lifecycle = new ResourceRegionLifecycleLedger();
            lifecycle.BeginSession(1UL);
            var snapshot = new ResourceSnapshotReplicationLedger();
            snapshot.ResetSession(1UL);

            RegionKey regionKey = new RegionKey(2, 2);
            lifecycle.CommitAcquire(regionKey);
            snapshot.EnqueueInitialSnapshot(76561198000000001UL, 101UL, regionKey, 1U);

            uint gen2 = lifecycle.RecordResourceDead(regionKey, 7);
            snapshot.UpdateRegionGeneration(regionKey, gen2);

            bool reconnected = snapshot.EnqueueInitialSnapshot(76561198000000001UL, 202UL, regionKey, gen2);
            bool found = snapshot.TryGetSnapshot(76561198000000001UL, regionKey, out var record);

            return reconnected &&
                   found &&
                   record.ConnectionToken == 202UL &&
                   record.RegionGeneration == gen2;
        }

        public static bool Test_M6H07_DifferentRegionsHarvestIsolated()
        {
            var ledger = new ResourceRegionLifecycleLedger();
            ledger.BeginSession(1UL);

            RegionKey r1 = new RegionKey(10, 10);
            RegionKey r2 = new RegionKey(20, 20);

            ledger.CommitAcquire(r1);
            ledger.CommitAcquire(r2);

            ledger.RecordResourceDead(r1, 5);

            return ledger.IsResourceDead(r1, 5) &&
                   !ledger.IsResourceDead(r2, 5) &&
                   ledger.GetDeadResourceIndices(r1).Count == 1 &&
                   ledger.GetDeadResourceIndices(r2).Count == 0;
        }

        public static bool Test_M6H08_SessionResetClearsDeadResources()
        {
            var ledger = new ResourceRegionLifecycleLedger();
            ledger.BeginSession(1UL);

            RegionKey regionKey = new RegionKey(7, 7);
            ledger.CommitAcquire(regionKey);
            ledger.RecordResourceDead(regionKey, 10);
            ledger.RecordResourceDead(regionKey, 20);

            ledger.BeginSession(2UL);

            return !ledger.IsResourceDead(regionKey, 10) &&
                   !ledger.IsResourceDead(regionKey, 20) &&
                   ledger.GetDeadResourceIndices(regionKey).Count == 0 &&
                   ledger.SessionEpoch == 2UL;
        }
    }
}
