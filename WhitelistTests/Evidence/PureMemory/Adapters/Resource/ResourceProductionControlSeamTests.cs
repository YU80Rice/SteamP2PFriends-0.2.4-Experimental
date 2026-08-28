using SteamP2PFriends.Adapters.Resource;
using SteamP2PFriends.Core.Identity;
using SteamP2PFriends.MultiObserver.SPI;
using System;
using System.Collections.Generic;

namespace SteamP2PFriends.WhitelistTests
{
    internal static class ResourceProductionControlSeamTests
    {
        internal static bool Test_M6P01_ObserverUnionAcquiresOnce()
        {
            var fake = new FakeResourceAdapters();
            var seam = new ResourceProductionControlSeam(
                fake.Lifecycle, fake.Replication, _ => fake.Generation, 64, 0, 2.0f);
            seam.BeginSession(new SessionEpoch(7UL));

            seam.UpdateObserver(100UL, 1001UL, 10, 10);
            seam.UpdateObserver(200UL, 2001UL, 10, 10);

            return seam.GetDemand(new RegionKey(10, 10)) == 2
                && fake.Lifecycle.Acquires.Count == 1
                && fake.Lifecycle.Acquires[0].ActiveDemandCount == 1
                && fake.Replication.Entered.Count == 2;
        }

        internal static bool Test_M6P02_LastObserverSchedulesTwoSecondRelease()
        {
            var fake = new FakeResourceAdapters();
            var seam = new ResourceProductionControlSeam(
                fake.Lifecycle, fake.Replication, _ => fake.Generation, 64, 0, 2.0f);
            seam.BeginSession(new SessionEpoch(7UL));
            seam.UpdateObserver(100UL, 1001UL, 10, 10);
            seam.RemoveObserver(100UL);

            seam.Tick(1.99f);
            bool heldDuringHysteresis = seam.IsLeased(new RegionKey(10, 10))
                && fake.Lifecycle.Releases.Count == 0;
            seam.Tick(0.01f);

            return heldDuringHysteresis
                && !seam.IsLeased(new RegionKey(10, 10))
                && fake.Lifecycle.Releases.Count == 1
                && Math.Abs(fake.Lifecycle.Releases[0].RegionKey.X - 10) == 0;
        }

        internal static bool Test_M6P03_ReentryCancelsReleaseWithoutSecondAcquire()
        {
            var fake = new FakeResourceAdapters();
            var seam = new ResourceProductionControlSeam(
                fake.Lifecycle, fake.Replication, _ => fake.Generation, 64, 0, 2.0f);
            seam.BeginSession(new SessionEpoch(7UL));
            seam.UpdateObserver(100UL, 1001UL, 10, 10);
            seam.RemoveObserver(100UL);
            seam.Tick(1.0f);
            seam.UpdateObserver(100UL, 1002UL, 10, 10);
            seam.Tick(2.0f);

            return seam.GetDemand(new RegionKey(10, 10)) == 1
                && seam.IsLeased(new RegionKey(10, 10))
                && fake.Lifecycle.Acquires.Count == 1
                && fake.Lifecycle.Releases.Count == 0
                && seam.ReentryCount == 1
                && fake.Replication.Entered.Count == 2;
        }

        internal static bool Test_M6P04_ReconnectAndSessionResetInvalidateState()
        {
            var fake = new FakeResourceAdapters();
            var seam = new ResourceProductionControlSeam(
                fake.Lifecycle, fake.Replication, _ => fake.Generation, 64, 0, 2.0f);
            seam.BeginSession(new SessionEpoch(7UL));
            seam.UpdateObserver(100UL, 1001UL, 10, 10);
            seam.UpdateObserver(100UL, 1002UL, 10, 10);

            bool reconnectObserved = fake.Replication.Exited.Count == 1
                && fake.Replication.Entered.Count == 2
                && fake.Replication.Exited[0].ConnectionToken == 1001UL
                && fake.Replication.Entered[1].ConnectionToken == 1002UL
                && fake.Lifecycle.Disconnects.Count == 1
                && fake.Lifecycle.Disconnects[0].ConnectionToken == 1001UL;

            seam.EndSession();
            return reconnectObserved
                && seam.ActiveLeaseCount == 0
                && seam.ObserverCount == 0
                && fake.Lifecycle.SessionEnds == 1
                && fake.Replication.ResetCount >= 2;
        }

        internal static bool Test_M6P06_AdvanceBeforeFlushAllowsSameFrameReentry()
        {
            var fake = new FakeResourceAdapters();
            var seam = new ResourceProductionControlSeam(
                fake.Lifecycle, fake.Replication, _ => fake.Generation, 64, 0, 2.0f);
            seam.BeginSession(new SessionEpoch(7UL));
            seam.UpdateObserver(100UL, 1001UL, 10, 10);
            seam.RemoveObserver(100UL);

            seam.AdvanceTime(2.0f);
            seam.UpdateObserver(100UL, 1002UL, 10, 10);
            seam.Flush(0f);

            return seam.IsLeased(new RegionKey(10, 10))
                && seam.GetDemand(new RegionKey(10, 10)) == 1
                && fake.Lifecycle.Releases.Count == 0;
        }

        internal static bool Test_M6P07_RealResourceAdapterHighLevelSeam()
        {
            var adapter = new ResourceDomainAdapter();
            adapter.OnSessionBegin(99U);
            var seam = new ResourceProductionControlSeam(
                adapter,
                adapter,
                ResourceRegionLifecycleAdapter.GetGeneration,
                64, 0, 2.0f);
            seam.BeginSession(new SessionEpoch(99UL));
            RegionKey regionKey = new RegionKey(5, 6);
            seam.UpdateObserver(777UL, 5001UL, regionKey.X, regionKey.Y);

            bool acquired = ResourceRegionLifecycleAdapter.IsRegionActive(regionKey.X, regionKey.Y);
            bool snapshotQueued = ResourceSnapshotAdapter.AdvanceDeltaSequence(777UL, regionKey) == 2U;

            seam.UpdateObserver(777UL, 5002UL, regionKey.X, regionKey.Y);
            uint reconnectSequence = 0U;
            bool reconnected = ResourceSnapshotAdapter.TryGetSnapshot(
                777UL, regionKey, out ResourceSnapshotRecord reconnectSnapshot)
                && reconnectSnapshot.ConnectionToken == 5002UL
                && !ResourceSnapshotAdapter.TryAdvanceDeltaSequence(
                    777UL, 5001UL, regionKey, out _)
                && ResourceSnapshotAdapter.TryAdvanceDeltaSequence(
                    777UL, 5002UL, regionKey, out reconnectSequence)
                && reconnectSequence == 2U;

            uint harvestGeneration = ResourceRegionLifecycleAdapter.RecordResourceDead(regionKey.X, regionKey.Y, 17);
            ResourceSnapshotAdapter.UpdateRegionGeneration(regionKey, harvestGeneration);
            bool staleIncrementRejected = ResourceSnapshotAdapter.AdvanceDeltaSequence(777UL, regionKey) == 0U;
            bool resynced = ResourceSnapshotAdapter.EnqueueInitialSnapshot(
                777UL, 5002UL, regionKey, harvestGeneration);
            bool incrementalGeneration = resynced
                && ResourceSnapshotAdapter.AdvanceDeltaSequence(777UL, regionKey) == 2U;

            seam.RemoveObserver(777UL);
            seam.Tick(2.0f);
            bool released = !ResourceRegionLifecycleAdapter.IsRegionActive(regionKey.X, regionKey.Y);
            seam.EndSession();
            bool lifecycleCleared = !ResourceRegionLifecycleAdapter.IsRegionActive(regionKey.X, regionKey.Y)
                && ResourceRegionLifecycleAdapter.GetGeneration(regionKey) == 0U;
            return acquired && snapshotQueued && reconnected && staleIncrementRejected
                && incrementalGeneration && released && lifecycleCleared;
        }

        internal static bool Test_M6P08_StaleRegionGenerationDelaysRelease()
        {
            var fake = new FakeResourceAdapters();
            var seam = new ResourceProductionControlSeam(
                fake.Lifecycle, fake.Replication, _ => fake.Generation, 64, 0, 2.0f);
            seam.BeginSession(new SessionEpoch(7UL));
            RegionKey regionKey = new RegionKey(10, 10);
            seam.UpdateObserver(100UL, 1001UL, regionKey.X, regionKey.Y);
            seam.RemoveObserver(100UL);

            fake.Generation = 2U;
            seam.Tick(2.0f);
            bool staleReleaseRejected = fake.Lifecycle.Releases.Count == 0
                && seam.PendingReleaseCount == 1
                && seam.IsLeased(regionKey);

            seam.Tick(2.0f);
            return staleReleaseRejected
                && fake.Lifecycle.Releases.Count == 1
                && fake.Lifecycle.Releases[0].RegionGeneration.Value == 2U;
        }

        internal static bool Test_M6P09_ReleaseFailureRetainsRetryableState()
        {
            var fake = new FakeResourceAdapters();
            var seam = new ResourceProductionControlSeam(
                fake.Lifecycle, fake.Replication, _ => fake.Generation, 64, 0, 2.0f);
            seam.BeginSession(new SessionEpoch(7UL));
            RegionKey regionKey = new RegionKey(10, 10);
            seam.UpdateObserver(100UL, 1001UL, regionKey.X, regionKey.Y);
            seam.RemoveObserver(100UL);
            fake.Lifecycle.ThrowOnRelease = true;

            bool didNotEscape = true;
            try { seam.Tick(2.0f); }
            catch { didNotEscape = false; }

            bool retained = seam.PendingReleaseCount == 1 && seam.IsLeased(regionKey);
            fake.Lifecycle.ThrowOnRelease = false;
            seam.Tick(2.0f);
            return didNotEscape && retained
                && seam.PendingReleaseCount == 0
                && !seam.IsLeased(regionKey)
                && fake.Lifecycle.Releases.Count == 2;
        }

        internal static bool Test_M6P11_ReplicationEnterFailureDoesNotCommitDemand()
        {
            var fake = new FakeResourceAdapters();
            fake.Replication.ThrowOnEntered = true;
            var seam = new ResourceProductionControlSeam(
                fake.Lifecycle, fake.Replication, _ => fake.Generation, 64, 0, 2.0f);
            seam.BeginSession(new SessionEpoch(7UL));
            RegionKey regionKey = new RegionKey(10, 10);

            bool threw = false;
            try { seam.UpdateObserver(100UL, 1001UL, regionKey.X, regionKey.Y); }
            catch (InvalidOperationException) { threw = true; }

            return threw
                && seam.GetDemand(regionKey) == 0
                && !seam.IsLeased(regionKey)
                && seam.PendingReleaseCount == 0;
        }

        internal static bool Test_M6P12_ReplicationExitFailureRetainsDemand()
        {
            var fake = new FakeResourceAdapters();
            var seam = new ResourceProductionControlSeam(
                fake.Lifecycle, fake.Replication, _ => fake.Generation, 64, 0, 2.0f);
            seam.BeginSession(new SessionEpoch(7UL));
            RegionKey regionKey = new RegionKey(10, 10);
            seam.UpdateObserver(100UL, 1001UL, regionKey.X, regionKey.Y);
            fake.Replication.ThrowOnExited = true;

            bool threw = false;
            try { seam.RemoveObserver(100UL); }
            catch (InvalidOperationException) { threw = true; }

            return threw
                && seam.GetDemand(regionKey) == 1
                && seam.IsLeased(regionKey)
                && seam.PendingReleaseCount == 0;
        }

        internal static bool Test_M6P13_MultiRegionEnterFailureCompensates()
        {
            var fake = new FakeResourceAdapters();
            fake.Replication.ThrowOnEnteredAfter = 1;
            var seam = new ResourceProductionControlSeam(
                fake.Lifecycle, fake.Replication, _ => fake.Generation, 64, 1, 2.0f);
            seam.BeginSession(new SessionEpoch(7UL));

            bool threw = false;
            try { seam.UpdateObserver(100UL, 1001UL, 10, 10); }
            catch (InvalidOperationException) { threw = true; }

            return threw
                && seam.GetDemand(new RegionKey(10, 10)) == 0
                && seam.ActiveLeaseCount == 0
                && seam.PendingReleaseCount == 0
                && fake.Replication.Exited.Count >= 1;
        }

        internal static bool Test_M6P14_MultiRegionExitFailureCompensates()
        {
            var fake = new FakeResourceAdapters();
            var seam = new ResourceProductionControlSeam(
                fake.Lifecycle, fake.Replication, _ => fake.Generation, 64, 1, 2.0f);
            seam.BeginSession(new SessionEpoch(7UL));
            seam.UpdateObserver(100UL, 1001UL, 10, 10);
            fake.Replication.ThrowOnExitedAfter = 1;

            bool threw = false;
            try { seam.RemoveObserver(100UL); }
            catch (InvalidOperationException) { threw = true; }

            return threw
                && seam.ObserverCount == 1
                && seam.DemandRegionCount == 9
                && seam.GetDemand(new RegionKey(10, 10)) == 1
                && seam.ActiveLeaseCount == 9
                && fake.Replication.Entered.Count > 9;
        }

        internal static bool Test_M6P05_RegionGenerationFlowsIntoSnapshotAndRelease()
        {
            var fake = new FakeResourceAdapters { Generation = 4U };
            var seam = new ResourceProductionControlSeam(
                fake.Lifecycle, fake.Replication, key => fake.Generation, 64, 0, 2.0f);
            seam.BeginSession(new SessionEpoch(9UL));
            RegionKey regionKey = new RegionKey(3, 4);
            seam.UpdateObserver(100UL, 1001UL, regionKey.X, regionKey.Y);
            bool generationObserved = seam.TryGetLease(regionKey, out ResourceProductionLease lease)
                && lease.RegionGeneration.Value == 4U;
            seam.RemoveObserver(100UL);
            seam.Tick(2.0f);

            return fake.Replication.Entered.Count == 1
                && generationObserved
                && fake.Lifecycle.Releases.Count == 1
                && fake.Lifecycle.Releases[0].RegionGeneration.Value == 4U;
        }

        private sealed class FakeResourceAdapters
        {
            internal uint Generation = 1U;
            internal readonly FakeLifecycleAdapter Lifecycle = new FakeLifecycleAdapter();
            internal readonly FakeReplicationAdapter Replication = new FakeReplicationAdapter();
        }

        private sealed class FakeLifecycleAdapter : ILifecycleDomainAdapter
        {
            internal readonly List<LeaseTicket> Acquires = new List<LeaseTicket>();
            internal readonly List<LeaseTicket> Releases = new List<LeaseTicket>();
            internal readonly List<DisconnectEvent> Disconnects = new List<DisconnectEvent>();
            internal int SessionEnds;
            internal bool ThrowOnRelease;

            public DomainId DomainId => DomainIds.Resource;
            public string DisplayName => "Resource";
            public string Capability => "test";
            public void OnSessionBegin(uint sessionEpoch) { }
            public void OnSessionEnd() { SessionEnds++; }
            public void OnAcquire(LeaseTicket ticket) { Acquires.Add(ticket); }
            public void OnRelease(LeaseTicket ticket)
            {
                Releases.Add(ticket);
                if (ThrowOnRelease) throw new InvalidOperationException("test release failure");
            }
            public void OnTick(float deltaTime) { }
            public void OnObserverDisconnect(ulong observerId, ulong connectionToken)
            {
                Disconnects.Add(new DisconnectEvent(observerId, connectionToken));
            }
        }

        private readonly struct DisconnectEvent
        {
            internal DisconnectEvent(ulong observerId, ulong connectionToken)
            {
                ObserverId = observerId;
                ConnectionToken = connectionToken;
            }

            internal ulong ObserverId { get; }
            internal ulong ConnectionToken { get; }
        }

        private sealed class FakeReplicationAdapter : IStateReplicationAdapter
        {
            internal readonly List<ReplicationEvent> Entered = new List<ReplicationEvent>();
            internal readonly List<ReplicationEvent> Exited = new List<ReplicationEvent>();
            internal int ResetCount;
            internal bool ThrowOnEntered;
            internal bool ThrowOnExited;
            internal int ThrowOnEnteredAfter = -1;
            internal int ThrowOnExitedAfter = -1;

            public DomainId DomainId => DomainIds.Resource;
            public string DisplayName => "Resource";
            public void OnObserverEntered(ulong observerId, ulong connectionToken, RegionKey regionKey)
            {
                Entered.Add(new ReplicationEvent(observerId, connectionToken, regionKey, 0U));
                if (ThrowOnEntered || (ThrowOnEnteredAfter >= 0 && Entered.Count > ThrowOnEnteredAfter))
                    throw new InvalidOperationException("test replication enter failure");
            }
            public void OnObserverExited(ulong observerId, ulong connectionToken, RegionKey regionKey)
            {
                Exited.Add(new ReplicationEvent(observerId, connectionToken, regionKey, 0U));
                if (ThrowOnExited || (ThrowOnExitedAfter >= 0 && Exited.Count > ThrowOnExitedAfter))
                    throw new InvalidOperationException("test replication exit failure");
            }
            public void OnReplicationTick(float deltaTime) { }
            public void ResetReplication(uint sessionEpoch) { ResetCount++; }
        }

        private readonly struct ReplicationEvent
        {
            internal ReplicationEvent(ulong observerId, ulong connectionToken, RegionKey regionKey, uint regionGeneration)
            {
                ObserverId = observerId;
                ConnectionToken = connectionToken;
                RegionKey = regionKey;
                RegionGeneration = regionGeneration;
            }

            internal ulong ObserverId { get; }
            internal ulong ConnectionToken { get; }
            internal RegionKey RegionKey { get; }
            internal uint RegionGeneration { get; }
        }
    }
}
