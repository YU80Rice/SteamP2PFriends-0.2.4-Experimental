using SDG.Unturned;
using SteamP2PFriends.Adapters.Item;
using SteamP2PFriends.Core.Identity;
using SteamP2PFriends.MultiObserver.Demand;
using SteamP2PFriends.MultiObserver.Lifecycle;
using SteamP2PFriends.MultiObserver.SPI;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SteamP2PFriends.WhitelistTests
{
    internal static class ItemPreparationAndFirstDemandTests
    {
        private static LeaseTicket CreateTicket(
            RegionKey region, ulong epoch = 101UL, uint generation = 1U, int demand = 1)
        {
            return new LeaseTicket(
                DomainIds.Item, region, new SessionEpoch(epoch),
                new RegionGeneration(generation), demand, true);
        }

        private static LifecyclePolicy CreateTestPolicy()
        {
            return new LifecyclePolicy(
                2.0f,
                new RetrySchedule(0.5f, 10.0f, 4),
                new RetrySchedule(1.0f, 10.0f, int.MaxValue),
                new HeartbeatPolicy(5.0f, 6),
                "ItemTestLifecyclePolicy");
        }

        internal static bool Test_IT01_EarlyOnlineOperationBeforePreparationIsDeferred()
        {
            var bridge = new TestNativeItemWarehouseBridge();
            var prepAuthority = new ItemPreparationAuthority(bridge);
            var metaStore = new ItemMetadataStore();
            var port = new ItemExecutionPort(prepAuthority, metaStore, _ => 1U, CreateTestPolicy());
            var proj = new DemandProjectionEngine(new ObserverSpatialAuthority());
            var policy = ItemDemandPolicy.Create(radius: 0, worldSize: 10);
            proj.Register(policy);

            var seam = new ItemProductionControlSeam(prepAuthority, metaStore, port, proj, policy, 2.0f);
            var epoch = new SessionEpoch(101UL);
            seam.BeginSession(epoch);

            // Observer arrives before full map preparation is complete
            seam.UpdateObserver(1001UL, 201UL, 5, 5, gameplayAuthorized: true);
            seam.AdvanceTime(0f);
            seam.Flush(0f);

            var region = new RegionKey(5, 5);
            bool deferredByPort = !port.IsRegionActivated(region);
            bool retryScheduled = seam.PendingAcquireRetryCount > 0;

            return deferredByPort && retryScheduled;
        }

        internal static bool Test_IT02_FullMapPreparationPreparesOnceIncludingEmptyRegions()
        {
            var bridge = new TestNativeItemWarehouseBridge();
            var prepAuthority = new ItemPreparationAuthority(bridge);
            var epoch = new SessionEpoch(101UL);
            const string worldId = "MapPEI";
            const byte worldSize = 4; // 16 regions total

            bool prepared = prepAuthority.PrepareFullMap(epoch, worldId, worldSize);
            if (!prepared) return false;

            bool isFullMapReady = prepAuthority.IsReady && prepAuthority.PreparedRegionCount == 16;
            bool region00Ready = prepAuthority.IsRegionPrepared(new RegionKey(0, 0));
            bool region33Ready = prepAuthority.IsRegionPrepared(new RegionKey(3, 3));
            bool outOfBounds = prepAuthority.IsRegionPrepared(new RegionKey(4, 4));

            // Repeated preparation attempt in same session is idempotent
            bool repeatIdempotent = prepAuthority.PrepareFullMap(epoch, worldId, worldSize);

            return isFullMapReady && region00Ready && region33Ready && !outOfBounds && repeatIdempotent;
        }

        internal static bool Test_IT03_FirstObserverDemandActivatesNaturalTimingWithoutModifyingDroppedAge()
        {
            var bridge = new TestNativeItemWarehouseBridge();
            var prepAuthority = new ItemPreparationAuthority(bridge);
            bridge.GenerateNaturalItem(0, 0, CreateTestItemData(1001U, isDropped: false, lastDropped: 5.0f));
            bridge.DropItem(0, 0, CreateTestItemData(2001U, isDropped: true, lastDropped: 10.0f));
            var metaStore = new ItemMetadataStore();
            float simulatedTime = 10.0f;
            var port = new ItemExecutionPort(
                prepAuthority, metaStore, _ => 1U, CreateTestPolicy(), () => simulatedTime);
            var proj = new DemandProjectionEngine(new ObserverSpatialAuthority());
            var policy = ItemDemandPolicy.Create(radius: 0, worldSize: 1);
            proj.Register(policy);

            var seam = new ItemProductionControlSeam(prepAuthority, metaStore, port, proj, policy, 2.0f);
            var epoch = new SessionEpoch(101UL);
            seam.BeginSession(epoch);

            prepAuthority.PrepareFullMap(epoch, "MapPEI", 1, metaStore, simulatedTime);

            // First observer arrives via high-level seam at simulatedTime = 100.0s
            simulatedTime = 100.0f;
            seam.UpdateObserver(1001UL, 201UL, 0, 0, gameplayAuthorized: true);
            seam.AdvanceTime(0f);
            seam.Flush(0f);

            var region = new RegionKey(0, 0);
            if (!port.IsRegionActivated(region)) return false;
            if (Math.Abs(port.GetActivationTime(region) - 100.0f) > 0.001f) return false;

            metaStore.TryGetMetadata(1001U, out ItemMetadataRecord naturalItem);
            metaStore.TryGetMetadata(2001U, out ItemMetadataRecord droppedItem);

            bool naturalShifted = Math.Abs(naturalItem.TimingBenchmark - 100.0f) < 0.001f;
            bool droppedUnshifted = Math.Abs(droppedItem.CreationTime - 10.0f) < 0.001f
                && Math.Abs(droppedItem.TimingBenchmark - 10.0f) < 0.001f;

            bool nativeRespawnBound = bridge.RegionRespawnTimes.TryGetValue((0, 0), out float respawnTime)
                && Math.Abs(respawnTime - 100.0f) < 0.001f;
            bool nativeNaturalShifted = Math.Abs(bridge.Regions[(0, 0)][0].lastDropped - 100.0f) < 0.001f;
            bool nativeDroppedUnshifted = Math.Abs(bridge.Regions[(0, 0)][1].lastDropped - 10.0f) < 0.001f;

            simulatedTime = 150.0f;
            bool naturalNotExpired = !naturalItem.IsExpired(simulatedTime, despawnNaturalTime: 60f, despawnDroppedTime: 60f);
            bool droppedExpired = droppedItem.IsExpired(simulatedTime, despawnNaturalTime: 60f, despawnDroppedTime: 60f);

            return naturalShifted && droppedUnshifted && nativeRespawnBound && nativeNaturalShifted && nativeDroppedUnshifted && naturalNotExpired && droppedExpired;
        }

        internal static bool Test_IT04_SecondObserverReceivesIndependentBaselineWithoutRegenerationOrTimingReset()
        {
            var bridge = new TestNativeItemWarehouseBridge();
            var prepAuthority = new ItemPreparationAuthority(bridge);
            bridge.GenerateNaturalItem(0, 0, CreateTestItemData(1001U, isDropped: false));
            var metaStore = new ItemMetadataStore();
            float simulatedTime = 100.0f;
            var port = new ItemExecutionPort(
                prepAuthority, metaStore, _ => 1U, CreateTestPolicy(), () => simulatedTime);
            var proj = new DemandProjectionEngine(new ObserverSpatialAuthority());
            var policy = ItemDemandPolicy.Create(radius: 0, worldSize: 1);
            proj.Register(policy);

            var seam = new ItemProductionControlSeam(prepAuthority, metaStore, port, proj, policy, 2.0f);
            var epoch = new SessionEpoch(101UL);
            seam.BeginSession(epoch);
            prepAuthority.PrepareFullMap(epoch, "MapPEI", 1, metaStore, 10.0f);

            var region = new RegionKey(0, 0);

            // First observer arrives at t=100s
            simulatedTime = 100.0f;
            seam.UpdateObserver(1001UL, 201UL, 0, 0, gameplayAuthorized: true);
            seam.AdvanceTime(0f);
            seam.Flush(0f);

            port.TryGetReceipt(region, out ItemAcquisitionReceipt receipt1);
            float firstActivationTime = port.GetActivationTime(region);

            // Second observer arrives at t=160s
            simulatedTime = 160.0f;
            seam.UpdateObserver(1002UL, 202UL, 0, 0, gameplayAuthorized: true);
            seam.AdvanceTime(0f);
            seam.Flush(0f);

            port.TryGetReceipt(region, out ItemAcquisitionReceipt receipt2);
            float secondActivationTime = port.GetActivationTime(region);

            // Timing base must NOT be reset by second observer
            bool activationTimePreserved = Math.Abs(secondActivationTime - firstActivationTime) < 0.001f
                && Math.Abs(secondActivationTime - 100.0f) < 0.001f;

            bool validReceipts = receipt1.Valid && receipt2.Valid && receipt2.RegionKey.Equals(region);
            bool itemsPreserved = metaStore.GetItemIdsInRegion(region).Count == 1;
            bool nativeTimingNotReset = bridge.RegionRespawnTimes.TryGetValue((0, 0), out float rTime)
                && Math.Abs(rTime - 100.0f) < 0.001f
                && Math.Abs(bridge.Regions[(0, 0)][0].lastDropped - 100.0f) < 0.001f;

            return activationTimePreserved && validReceipts && itemsPreserved && nativeTimingNotReset;
        }

        internal static bool Test_IT05_CoreSessionBindingRetainsPreparedDataWhileStaleEpochIsRejected()
        {
            var bridge = new TestNativeItemWarehouseBridge();
            var prepAuthority = new ItemPreparationAuthority(bridge);
            var epoch = new SessionEpoch(101UL);
            const string worldId = "MapPEI";

            prepAuthority.PrepareFullMap(epoch, worldId, 1);
            var region = new RegionKey(0, 0);

            // Re-binding with same session epoch and world retains prepared state
            prepAuthority.BindCoreSession(epoch, worldId);
            bool retained = prepAuthority.IsReady && prepAuthority.IsRegionPrepared(region);

            // Binding to a new session epoch invalidates old prepared state
            var newEpoch = new SessionEpoch(102UL);
            prepAuthority.BindCoreSession(newEpoch, worldId);
            bool resetOnNewEpoch = !prepAuthority.IsReady && !prepAuthority.IsRegionPrepared(region);

            return retained && resetOnNewEpoch;
        }

        internal static bool Test_IT06_OriginTrackingPreservesNestedDroppedItemsDuringPreparation()
        {
            var bridge = new TestNativeItemWarehouseBridge();
            var region = new RegionKey(0, 0);
            var prepAuthority = new ItemPreparationAuthority(bridge);
            bridge.GenerateNaturalItem(0, 0, CreateTestItemData(101U, isDropped: false));
            bridge.DropItem(0, 0, CreateTestItemData(202U, isDropped: true, lastDropped: 5.5f));
            var metaStore = new ItemMetadataStore();
            var epoch = new SessionEpoch(101UL);
            prepAuthority.PrepareFullMap(epoch, "MapPEI", 1, metaStore, currentTime: 10.0f);

            IReadOnlyList<uint> items = metaStore.GetItemIdsInRegion(region);
            if (items.Count != 2) return false;

            metaStore.TryGetMetadata(101U, out ItemMetadataRecord naturalRecord);
            metaStore.TryGetMetadata(202U, out ItemMetadataRecord droppedRecord);

            return naturalRecord != null && naturalRecord.Origin == ItemOriginKind.Natural
                && droppedRecord != null && droppedRecord.Origin == ItemOriginKind.Dropped
                && Math.Abs(droppedRecord.CreationTime - 5.5f) < 0.001f;
        }

        internal static bool Test_IT07_ExpiryClassificationAlignsWithU3DS()
        {
            var region = new RegionKey(2, 2);

            // Natural item with benchmark 100s
            var natural = new ItemMetadataRecord(1U, region, ItemOriginKind.Natural, creationTime: 0f, timingBenchmark: 100f);
            // Dropped item with creation time 80s
            var dropped = new ItemMetadataRecord(2U, region, ItemOriginKind.Dropped, creationTime: 80f, timingBenchmark: 80f);
            // Unknown item
            var unknown = new ItemMetadataRecord(3U, region, ItemOriginKind.Unknown, creationTime: 50f, timingBenchmark: 50f);

            // Natural threshold: 600s, Dropped threshold: 120s
            const float naturalThreshold = 600f;
            const float droppedThreshold = 120f;

            // At t=210s:
            // Natural age = 210 - 100 = 110s < 600s -> not expired
            // Dropped age = 210 - 80 = 130s > 120s -> EXPIRED
            // Unknown does not expire through dormant natural reconciliation -> not expired
            float t1 = 210.0f;
            bool natural1 = natural.IsExpired(t1, naturalThreshold, droppedThreshold);
            bool dropped1 = dropped.IsExpired(t1, naturalThreshold, droppedThreshold);
            bool unknown1 = unknown.IsExpired(t1, naturalThreshold, droppedThreshold);

            // At t=750s:
            // Natural age = 750 - 100 = 650s > 600s -> EXPIRED
            float t2 = 750.0f;
            bool natural2 = natural.IsExpired(t2, naturalThreshold, droppedThreshold);

            return !natural1 && dropped1 && !unknown1 && natural2;
        }

        internal static bool Test_IT08_FailedPreparationDoesNotMarkReady()
        {
            var prepAuthority = new ItemPreparationAuthority();
            var epoch = new SessionEpoch(101UL);
            const string worldId = "MapPEI";

            // If inputs are not available, preparation fails immediately
            bool beginFailed = !prepAuthority.TryBeginPreparation(epoch, worldId, 2, inputsAvailable: false);
            bool notReady1 = !prepAuthority.IsReady;

            // If preparation is completed with success = false
            prepAuthority.TryBeginPreparation(epoch, worldId, 2, inputsAvailable: true);
            prepAuthority.MarkRegionPrepared(new RegionKey(0, 0));
            bool completeFailed = !prepAuthority.CompletePreparation(epoch, worldId, success: false);
            bool notReady2 = !prepAuthority.IsReady && prepAuthority.Status == ItemPreparationStatus.Failed;

            return beginFailed && notReady1 && completeFailed && notReady2;
        }

        internal static bool Test_IT09_ItemDemandPolicyDeclaration()
        {
            DemandPolicy policy = ItemDemandPolicy.Create(radius: 2, worldSize: 64);
            bool isItemDomain = policy.Domain == DomainIds.Item;
            bool isRadius2 = policy.Radius == 2;
            bool isWorldSize64 = policy.WorldSize == 64;
            bool isChebyshev = policy.Shape == EDemandRegionShape.ChebyshevSquare2D;
            bool isSource = string.Equals(policy.RadiusSource, "ItemManager.ITEM_REGIONS", StringComparison.Ordinal);

            bool authorizedAllowed = policy.IsEligible(new ObserverPresence(1UL, 101UL, new RegionKey(10, 10), gameplayAuthorized: true));
            bool unauthorizedDenied = !policy.IsEligible(new ObserverPresence(2UL, 202UL, new RegionKey(10, 10), gameplayAuthorized: false));

            return isItemDomain && isRadius2 && isWorldSize64 && isChebyshev && isSource && authorizedAllowed && unauthorizedDenied;
        }

        internal static bool Test_IT10_ItemExecutionPortAndSharedEngineIntegration()
        {
            var bridge = new TestNativeItemWarehouseBridge();
            var prepAuthority = new ItemPreparationAuthority(bridge);
            var metaStore = new ItemMetadataStore();
            float simulatedTime = 50.0f;
            var port = new ItemExecutionPort(
                prepAuthority, metaStore, _ => 1U, CreateTestPolicy(), () => simulatedTime);

            var epoch = new SessionEpoch(101UL);
            prepAuthority.TryBeginPreparation(epoch, "MapPEI", 8, true);
            for (byte x = 0; x < 8; x++)
            {
                for (byte y = 0; y < 8; y++)
                {
                    prepAuthority.MarkRegionPrepared(new RegionKey(x, y));
                }
            }
            prepAuthority.CompletePreparation(epoch, "MapPEI", true);

            var projection = new DemandProjectionEngine(new ObserverSpatialAuthority());
            DemandPolicy policy = ItemDemandPolicy.Create(radius: 0, worldSize: 8);
            projection.Register(policy);

            var engine = new LifecycleOrchestrationEngine(projection);
            engine.Register(policy, port);
            engine.BeginSession(epoch);

            // Observer enters region (3, 3)
            simulatedTime = 50.0f;
            engine.Observe(DomainIds.Item, 999UL, 10001UL, 3, 3, gameplayAuthorized: true);

            bool activated = port.IsRegionActivated(new RegionKey(3, 3));
            bool hasReceipt = port.TryGetReceipt(new RegionKey(3, 3), out ItemAcquisitionReceipt receipt);
            bool leased = engine.IsLeased(DomainIds.Item, new RegionKey(3, 3));

            return activated && hasReceipt && receipt.Valid && leased;
        }

        internal static bool Test_IT11_StaleOrUnboundSessionAcquireIsRejected()
        {
            var prepAuthority = new ItemPreparationAuthority();
            var metaStore = new ItemMetadataStore();
            var port = new ItemExecutionPort(prepAuthority, metaStore, _ => 1U, CreateTestPolicy());
            var region = new RegionKey(0, 0);

            // Attempt acquire before OnSessionBegin
            var ticketUnbound = CreateTicket(region, 101UL);
            bool unboundRejected = false;
            try
            {
                port.OnAcquire(ticketUnbound);
            }
            catch (Exception ex)
            {
                unboundRejected = port.ClassifyRegionFailure(ex) == EDomainFailureKind.Rejected;
            }

            // Begin session 101, but send ticket from session 99
            port.OnSessionBegin(101U);
            var ticketStale = CreateTicket(region, 99UL);
            bool staleRejected = false;
            try
            {
                port.OnAcquire(ticketStale);
            }
            catch (Exception ex)
            {
                staleRejected = port.ClassifyRegionFailure(ex) == EDomainFailureKind.Rejected;
            }

            return unboundRejected && staleRejected;
        }

        internal static bool Test_IT12_StaleReceiptOrEpochReleaseIsRejected()
        {
            var bridge = new TestNativeItemWarehouseBridge();
            var prepAuthority = new ItemPreparationAuthority(bridge);
            var metaStore = new ItemMetadataStore();
            var port = new ItemExecutionPort(prepAuthority, metaStore, _ => 1U, CreateTestPolicy());
            var region = new RegionKey(0, 0);

            var epoch = new SessionEpoch(101UL);
            prepAuthority.TryBeginPreparation(epoch, "MapPEI", 1, true);
            prepAuthority.MarkRegionPrepared(region);
            prepAuthority.CompletePreparation(epoch, "MapPEI", true);

            port.OnSessionBegin(101U);
            var acquireTicket = CreateTicket(region, 101UL, 1U, 1);
            port.OnAcquire(acquireTicket);

            // Release with active demand != 0 should be rejected
            var invalidDemandRelease = CreateTicket(region, 101UL, 1U, 1);
            bool invalidDemandRejected = false;
            try
            {
                port.OnRelease(invalidDemandRelease);
            }
            catch (Exception ex)
            {
                invalidDemandRejected = port.ClassifyReleaseFailure(ex) == EDomainFailureKind.Rejected;
            }

            // Valid release ticket with demand = 0
            var validRelease = new LeaseTicket(DomainIds.Item, region, epoch, new RegionGeneration(1U), 0, true);
            port.OnRelease(validRelease);
            bool receiptCleared = !port.TryGetReceipt(region, out _);

            // Double release should be rejected
            bool doubleReleaseRejected = false;
            try
            {
                port.OnRelease(validRelease);
            }
            catch (Exception ex)
            {
                doubleReleaseRejected = port.ClassifyReleaseFailure(ex) == EDomainFailureKind.Rejected;
            }

            return invalidDemandRejected && receiptCleared && doubleReleaseRejected;
        }

        internal static bool Test_IT13_PreparationRejectsOutOfBoundsRegionsAndZeroEpoch()
        {
            var prepAuthority = new ItemPreparationAuthority();
            // Default uninitialized epoch (value=0) rejected
            bool zeroEpochRejected = !prepAuthority.TryBeginPreparation(default(SessionEpoch), "MapPEI", 4, true);

            var epoch = new SessionEpoch(101UL);
            bool beginOk = prepAuthority.TryBeginPreparation(epoch, "MapPEI", 2, true);
            // Re-entrant preparation rejected while preparing
            bool reentrantRejected = !prepAuthority.TryBeginPreparation(epoch, "MapPEI", 2, true);

            // Mark out-of-bounds region (X=2, Y=0 is out of bounds for worldSize=2)
            prepAuthority.MarkRegionPrepared(new RegionKey(2, 0));
            // Only 1 out of 4 valid regions marked
            prepAuthority.MarkRegionPrepared(new RegionKey(0, 0));

            // Complete preparation should fail because count is 1 < 4 (out-of-bounds region was ignored)
            bool completeFailed = !prepAuthority.CompletePreparation(epoch, "MapPEI", true);
            bool outOfBoundsNotPrepared = !prepAuthority.IsRegionPrepared(new RegionKey(2, 0));

            return zeroEpochRejected && beginOk && reentrantRejected && completeFailed && outOfBoundsNotPrepared;
        }

        internal static bool Test_IT14_ReceiptFullIdentityAndEquality()
        {
            var region = new RegionKey(1, 2);
            var epoch = new SessionEpoch(101UL);
            var generation = new RegionGeneration(3U);

            var validReceipt = new ItemAcquisitionReceipt(
                DomainIds.Item, region, epoch, generation, 1UL, 10.0f);
            var duplicateReceipt = new ItemAcquisitionReceipt(
                DomainIds.Item, region, epoch, generation, 1UL, 10.0f);
            var diffReceipt = new ItemAcquisitionReceipt(
                DomainIds.Item, region, epoch, generation, 2UL, 10.0f);

            // Invariant failures produce Valid == false
            var invalidEpochReceipt = new ItemAcquisitionReceipt(
                DomainIds.Item, region, default(SessionEpoch), generation, 1UL, 10.0f);
            var invalidGenReceipt = new ItemAcquisitionReceipt(
                DomainIds.Item, region, epoch, default(RegionGeneration), 1UL, 10.0f);
            var invalidAcquireGenReceipt = new ItemAcquisitionReceipt(
                DomainIds.Item, region, epoch, generation, 0UL, 10.0f);

            bool invariantsPass = validReceipt.Valid
                && !invalidEpochReceipt.Valid
                && !invalidGenReceipt.Valid
                && !invalidAcquireGenReceipt.Valid;

            bool equalityPass = validReceipt.Equals(duplicateReceipt)
                && validReceipt == duplicateReceipt
                && !validReceipt.Equals(diffReceipt)
                && validReceipt != diffReceipt
                && validReceipt.GetHashCode() == duplicateReceipt.GetHashCode();

            return invariantsPass && equalityPass;
        }

        internal static bool Test_IT15_MetadataStoreItemRemovalAndSessionCleanup()
        {
            var prepAuthority = new ItemPreparationAuthority();
            var metaStore = new ItemMetadataStore();
            var port = new ItemExecutionPort(prepAuthority, metaStore, _ => 1U, CreateTestPolicy());

            var region1 = new RegionKey(1, 1);
            var region2 = new RegionKey(2, 2);

            // Register item in region 1
            metaStore.RegisterItem(1001U, region1, ItemOriginKind.Natural, 10.0f);
            if (metaStore.GetItemIdsInRegion(region1).Count != 1) return false;

            // Re-register item in region 2: should be removed from region 1
            metaStore.RegisterItem(1001U, region2, ItemOriginKind.Dropped, 20.0f);
            bool region1Cleared = metaStore.GetItemIdsInRegion(region1).Count == 0;
            bool region2HasItem = metaStore.GetItemIdsInRegion(region2).Count == 1;

            // RemoveItem removes it completely
            bool removed = metaStore.RemoveItem(1001U);
            bool storeEmpty = metaStore.Count == 0 && metaStore.GetItemIdsInRegion(region2).Count == 0;

            // Register another item and verify OnSessionEnd clears the store
            metaStore.RegisterItem(2002U, region1, ItemOriginKind.Natural, 30.0f);
            port.OnSessionEnd();
            bool clearedOnSessionEnd = metaStore.Count == 0;

            return region1Cleared && region2HasItem && removed && storeEmpty && clearedOnSessionEnd;
        }

        private static ItemData CreateTestItemData(uint instanceId, bool isDropped, float lastDropped = 0f)
        {
            var data = (ItemData)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(ItemData));
            typeof(ItemData).GetField("_instanceID", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.SetValue(data, instanceId);
            typeof(ItemData).GetField("_isDropped", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.SetValue(data, isDropped);
            typeof(ItemData).GetField("_lastDropped", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.SetValue(data, lastDropped);
            return data;
        }

        internal static bool Test_IT16_ProductionSeamFullMapPreparationWithWarehouseBridge()
        {
            var bridge = new TestNativeItemWarehouseBridge();
            bridge.Regions[(0, 0)] = new List<ItemData>
            {
                CreateTestItemData(1001U, false),
                CreateTestItemData(2001U, true)
            };
            bridge.Regions[(0, 1)] = new List<ItemData>();
            bridge.Regions[(1, 0)] = new List<ItemData>();
            bridge.Regions[(1, 1)] = new List<ItemData>();

            var prepAuthority = new ItemPreparationAuthority(bridge);
            prepAuthority.TrackNaturalItem(1001U);
            var metaStore = new ItemMetadataStore();
            var epoch = new SessionEpoch(101UL);

            bool prepared = prepAuthority.PrepareFullMap(epoch, "MapPEI", 2, metaStore, 10.0f);
            if (!prepared) return false;

            bool isFullMapReady = prepAuthority.IsReady && prepAuthority.PreparedRegionCount == 4;
            bool region00Ready = prepAuthority.IsRegionPrepared(new RegionKey(0, 0));
            bool emptyRegionReady = prepAuthority.IsRegionPrepared(new RegionKey(0, 1));

            bool hasNatural = metaStore.TryGetMetadata(1001U, out ItemMetadataRecord naturalItem)
                && naturalItem.Origin == ItemOriginKind.Natural;
            bool hasDropped = metaStore.TryGetMetadata(2001U, out ItemMetadataRecord droppedItem)
                && droppedItem.Origin == ItemOriginKind.Dropped;

            bool repeatIdempotent = prepAuthority.PrepareFullMap(epoch, "MapPEI", 2, metaStore, 15.0f)
                && metaStore.Count == 2;

            return isFullMapReady && region00Ready && emptyRegionReady && hasNatural && hasDropped && repeatIdempotent;
        }

        internal static bool Test_IT17_ProductionSeamFirstDemandTimingAndAgePreservation()
        {
            var bridge = new TestNativeItemWarehouseBridge();
            var prep = new ItemPreparationAuthority(bridge);
            bridge.GenerateNaturalItem(0, 0, CreateTestItemData(1001U, false));
            bridge.DropItem(0, 0, CreateTestItemData(2001U, true, 25.0f));
            bridge.Regions[(0, 1)] = new List<ItemData>();
            bridge.Regions[(1, 0)] = new List<ItemData>();
            bridge.Regions[(1, 1)] = new List<ItemData>();

            var metaStore = new ItemMetadataStore();
            float simulatedTime = 100.0f;
            var port = new ItemExecutionPort(prep, metaStore, _ => 1U, CreateTestPolicy(), () => simulatedTime);
            var proj = new DemandProjectionEngine(new ObserverSpatialAuthority());
            var policy = ItemDemandPolicy.Create(radius: 0, worldSize: 2);
            proj.Register(policy);

            var seam = new ItemProductionControlSeam(prep, metaStore, port, proj, policy, 2.0f);
            var epoch = new SessionEpoch(101UL);
            seam.BeginSession(epoch);

            prep.PrepareFullMap(epoch, "MapPEI", 2, metaStore, 10.0f);

            simulatedTime = 100.0f;
            seam.UpdateObserver(1001UL, 501UL, 0, 0, gameplayAuthorized: true);
            seam.AdvanceTime(0f);
            seam.Flush(0f);

            var region = new RegionKey(0, 0);
            if (!port.IsRegionActivated(region)) return false;

            metaStore.TryGetMetadata(1001U, out ItemMetadataRecord naturalItem);
            metaStore.TryGetMetadata(2001U, out ItemMetadataRecord droppedItem);

            bool naturalShifted = Math.Abs(naturalItem.TimingBenchmark - 100.0f) < 0.001f;
            bool droppedUnshifted = Math.Abs(droppedItem.TimingBenchmark - 25.0f) < 0.001f;

            simulatedTime = 150.0f;
            bool naturalActive = !naturalItem.IsExpired(simulatedTime, 60f, 60f);
            bool droppedExpired = droppedItem.IsExpired(simulatedTime, 60f, 60f);

            return naturalShifted && droppedUnshifted && naturalActive && droppedExpired;
        }

        internal static bool Test_IT18_ProductionSeamIndependentObserverBaselinesAndHostPresentation()
        {
            var bridge = new TestNativeItemWarehouseBridge();
            var prep = new ItemPreparationAuthority(bridge);
            bridge.GenerateNaturalItem(0, 0, CreateTestItemData(1001U, false));
            bridge.Regions[(0, 1)] = new List<ItemData>();
            bridge.Regions[(1, 0)] = new List<ItemData>();
            bridge.Regions[(1, 1)] = new List<ItemData>();

            var metaStore = new ItemMetadataStore();
            var hostPresentations = new List<RegionKey>();
            var guestBaselines = new List<(ulong observerId, ulong token, RegionKey region)>();

            var port = new ItemExecutionPort(
                prep, metaStore, _ => 1U, CreateTestPolicy(), () => 100.0f, null,
                observerId => observerId == 1000UL,
                region => hostPresentations.Add(region),
                (obsId, token, region) => guestBaselines.Add((obsId, token, region)));

            var proj = new DemandProjectionEngine(new ObserverSpatialAuthority());
            var policy = ItemDemandPolicy.Create(radius: 0, worldSize: 2);
            proj.Register(policy);

            var seam = new ItemProductionControlSeam(prep, metaStore, port, proj, policy, 2.0f);
            var epoch = new SessionEpoch(101UL);
            seam.BeginSession(epoch);
            prep.PrepareFullMap(epoch, "MapPEI", 2, metaStore, 10.0f);

            var region = new RegionKey(0, 0);

            // 1. Host observer arrives (driven solely via high-level seam)
            seam.UpdateObserver(1000UL, 101UL, 0, 0, gameplayAuthorized: true);
            seam.AdvanceTime(0f);
            seam.Flush(0f);

            // 2. Guest 1 arrives (driven solely via high-level seam)
            seam.UpdateObserver(2001UL, 201UL, 0, 0, gameplayAuthorized: true);
            seam.AdvanceTime(0f);
            seam.Flush(0f);

            // 3. Guest 2 arrives (driven solely via high-level seam)
            seam.UpdateObserver(2002UL, 202UL, 0, 0, gameplayAuthorized: true);
            seam.AdvanceTime(0f);
            seam.Flush(0f);

            bool hostPres = hostPresentations.Count == 1 && hostPresentations[0].Equals(region);
            bool guestBinds = guestBaselines.Count == 2
                && guestBaselines[0].observerId == 2001UL && guestBaselines[0].token == 201UL
                && guestBaselines[1].observerId == 2002UL && guestBaselines[1].token == 202UL;
            bool noDuplicate = port.HostPresentationCount == 1 && port.GuestBaselineBindingCount == 2;

            return hostPres && guestBinds && noDuplicate;
        }

        internal static bool Test_IT19_ProductionSeamUnpreparedOperationGateRejection()
        {
            var bridge = new TestNativeItemWarehouseBridge();
            var prep = new ItemPreparationAuthority(bridge);
            var metaStore = new ItemMetadataStore();
            var port = new ItemExecutionPort(prep, metaStore, _ => 1U, CreateTestPolicy());
            var region = new RegionKey(1, 1);

            // 1. Before preparation: CanPerformOperation is false
            bool blockedByGate = !prep.CanPerformOperation(region);

            // 2. Replication enter before preparation throws and is rejected
            bool enterRejected = false;
            try
            {
                port.OnObserverReplicationEntered(1001UL, 201UL, region);
            }
            catch (Exception ex)
            {
                enterRejected = ex.Message.Contains("not prepared");
            }

            // 3. Prepare full map and verify gate unlocks
            var epoch = new SessionEpoch(101UL);
            prep.PrepareFullMap(epoch, "MapPEI", 2);

            bool unlockedAfterPrep = prep.CanPerformOperation(region);

            return blockedByGate && enterRejected && unlockedAfterPrep;
        }

        internal static bool Test_IT20_WarehouseBridgeExceptionPropagationAndIdentityCheck()
        {
            var bridge = new TestNativeItemWarehouseBridge();
            bridge.Regions[(0, 0)] = new List<ItemData>();
            var prep = new ItemPreparationAuthority(bridge);
            var epoch = new SessionEpoch(101UL);
            prep.PrepareFullMap(epoch, "MapPEI", 1);
            var region = new RegionKey(0, 0);

            // 1. Host presenter throws: exception must propagate and presentation count must NOT increment
            var portWithFailingHost = new ItemExecutionPort(
                prep, new ItemMetadataStore(), _ => 1U, CreateTestPolicy(), () => 10f, null,
                isHostObserver: _ => true,
                hostPresenter: _ => throw new InvalidOperationException("Host presenter crash"));

            bool hostPropagated = false;
            try { portWithFailingHost.OnObserverReplicationEntered(1000UL, 101UL, region); }
            catch (InvalidOperationException) { hostPropagated = true; }
            bool hostCountZero = portWithFailingHost.HostPresentationCount == 0;

            // 2. Guest binder throws: exception must propagate and binding count must NOT increment
            var portWithFailingGuest = new ItemExecutionPort(
                prep, new ItemMetadataStore(), _ => 1U, CreateTestPolicy(), () => 10f, null,
                isHostObserver: _ => false,
                guestBaselineBinder: (_, _, _) => throw new InvalidOperationException("Guest binder crash"));

            bool guestPropagated = false;
            try { portWithFailingGuest.OnObserverReplicationEntered(2001UL, 201UL, region); }
            catch (InvalidOperationException) { guestPropagated = true; }
            bool guestCountZero = portWithFailingGuest.GuestBaselineBindingCount == 0;

            // 3. DefaultNativeItemWarehouseBridge identity check: missing client must throw
            var defaultBridge = new DefaultNativeItemWarehouseBridge();
            bool guestMissingClientThrows = false;
            try { defaultBridge.BindGuestBaseline(999999UL, 1UL, 0, 0); }
            catch (InvalidOperationException) { guestMissingClientThrows = true; }

            return hostPropagated && hostCountZero && guestPropagated && guestCountZero && guestMissingClientThrows;
        }

        internal static bool Test_IT21_CreationPathTrackingAndNestedDropPreservation()
        {
            var bridge = new TestNativeItemWarehouseBridge();
            bridge.Regions[(0, 0)] = new List<ItemData>
            {
                CreateTestItemData(2001U, isDropped: true, lastDropped: 25.0f),
                CreateTestItemData(5001U, isDropped: false, lastDropped: 10.0f)
            };

            bridge.OnSubmitGeneration = (x, y) =>
            {
                // Native generation replaces list and drops old natural item 5001U:
                bridge.Regions[(x, y)] = new List<ItemData>
                {
                    CreateTestItemData(2001U, isDropped: true, lastDropped: 25.0f),
                    CreateTestItemData(3001U, isDropped: true, lastDropped: 0.0f),
                    CreateTestItemData(1001U, isDropped: false, lastDropped: 50.0f),
                    CreateTestItemData(0U, isDropped: false)
                };
            };

            var prep = new ItemPreparationAuthority(bridge);
            var metaStore = new ItemMetadataStore();
            var epoch = new SessionEpoch(101UL);

            prep.PrepareFullMap(epoch, "MapPEI", 1, metaStore, currentTime: 100.0f);

            bool hasPreDrop = metaStore.TryGetMetadata(2001U, out ItemMetadataRecord preDrop);
            bool hasNestedDrop = metaStore.TryGetMetadata(3001U, out ItemMetadataRecord nestedDrop);
            bool hasNatural = metaStore.TryGetMetadata(1001U, out ItemMetadataRecord natural);
            bool hasUnknown = metaStore.TryGetMetadata(0U, out ItemMetadataRecord unknownRec);
            bool oldNaturalExcluded = !metaStore.TryGetMetadata(5001U, out _);

            bool preDropAgePreserved = hasPreDrop && preDrop.Origin == ItemOriginKind.Dropped
                && Math.Abs(preDrop.CreationTime - 25.0f) < 0.001f;
            bool nestedDropZeroAgePreserved = hasNestedDrop && nestedDrop.Origin == ItemOriginKind.Dropped
                && Math.Abs(nestedDrop.CreationTime - 0.0f) < 0.001f;
            bool naturalTimingUnstarted = hasNatural && natural.Origin == ItemOriginKind.Natural
                && natural.TimingBenchmark <= 0f;
            bool unknownNotNatural = hasUnknown && unknownRec.Origin == ItemOriginKind.Unknown;

            return preDropAgePreserved && nestedDropZeroAgePreserved && naturalTimingUnstarted && unknownNotNatural && oldNaturalExcluded;
        }

        internal static bool Test_IT22_CandidateLegacyMutualExclusionAndPreparationGates()
        {
            var bridge = new TestNativeItemWarehouseBridge();
            bridge.InputsAvailable = false;
            var prep = new ItemPreparationAuthority(bridge);
            var epoch = new SessionEpoch(101UL);

            // 1. Missing inputs: PrepareFullMap waits, status is WaitingForInputs, NOT Failed, NOT Ready
            bool prepResult = prep.PrepareFullMap(epoch, "MapPEI", 2);
            bool waitingStatus = prep.Status == ItemPreparationStatus.WaitingForInputs && !prep.IsReady;

            // 2. Normal waiting allows preparation once inputs become available
            bridge.InputsAvailable = true;
            bool recoveredPrep = prep.PrepareFullMap(epoch, "MapPEI", 2);
            bool recoveredReady = recoveredPrep && prep.IsReady;

            // 3. Manifest gate: unprepared region replication throws
            var unpreparedPrep = new ItemPreparationAuthority(bridge);
            var port = new ItemExecutionPort(unpreparedPrep, new ItemMetadataStore(), _ => 1U, CreateTestPolicy());
            bool manifestRejected = false;
            try { port.OnObserverReplicationEntered(2001UL, 201UL, new RegionKey(0, 0)); }
            catch (ItemRegionNotPreparedException) { manifestRejected = true; }

            // 4. Candidate / Legacy mutual exclusion behavioral check
            bridge.InputsAvailable = true;
            var prep2 = new ItemPreparationAuthority(bridge);
            prep2.PrepareFullMap(new SessionEpoch(202UL), "MapPEI", 1);
            var prepRegion = new RegionKey(0, 0);

            var currentMode = ItemProductionMode.LegacyWriter;
            var mutualPort = new ItemExecutionPort(
                prep2, new ItemMetadataStore(), _ => 1U, CreateTestPolicy(), () => 10f, null,
                isHostObserver: _ => false,
                guestBaselineBinder: (_, _, _) => { },
                isCandidateActive: () => currentMode == ItemProductionMode.Candidate);

            // In LegacyWriter mode: candidate replication is suppressed (count == 0)
            mutualPort.OnObserverReplicationEntered(3001UL, 301UL, prepRegion);
            bool candidateSuppressedInLegacy = mutualPort.GuestBaselineBindingCount == 0;

            // In Candidate mode: candidate replication executes (count == 1)
            currentMode = ItemProductionMode.Candidate;
            mutualPort.OnObserverReplicationEntered(3001UL, 301UL, prepRegion);
            bool candidateActiveInCandidate = mutualPort.GuestBaselineBindingCount == 1;

            bool behavioralMutualExclusion = candidateSuppressedInLegacy && candidateActiveInCandidate;

            return !prepResult && waitingStatus && recoveredReady && manifestRejected && behavioralMutualExclusion;
        }

        internal static bool Test_IT23_NativeBridgeValidationAndListReplacementSafety()
        {
            var defaultBridge = new DefaultNativeItemWarehouseBridge();
            bool oobThrows = false;
            try { defaultBridge.PresentRegionToHost(255, 255); }
            catch (InvalidOperationException) { oobThrows = true; }

            bool unpresentOobThrows = false;
            try { defaultBridge.UnpresentRegionFromHost(255, 255); }
            catch (InvalidOperationException) { unpresentOobThrows = true; }

            var bridge = new TestNativeItemWarehouseBridge();
            bridge.Regions[(0, 0)] = new List<ItemData>
            {
                CreateTestItemData(2001U, isDropped: true, lastDropped: 33.0f)
            };
            bridge.OnSubmitGeneration = (x, y) =>
            {
                bridge.Regions[(x, y)] = new List<ItemData>
                {
                    CreateTestItemData(101U, isDropped: false)
                };
            };

            var prep = new ItemPreparationAuthority(bridge);
            var metaStore = new ItemMetadataStore();
            prep.PrepareFullMap(new SessionEpoch(303UL), "MapPEI", 1, metaStore, 100.0f);

            bool dropRestored = bridge.Regions[(0, 0)].Count == 2;
            bool hasDrop = metaStore.TryGetMetadata(2001U, out ItemMetadataRecord dropRec)
                && dropRec.Origin == ItemOriginKind.Dropped && Math.Abs(dropRec.CreationTime - 33.0f) < 0.001f;
            bool hasNatural = metaStore.TryGetMetadata(101U, out ItemMetadataRecord natRec)
                && natRec.Origin == ItemOriginKind.Natural && natRec.TimingBenchmark <= 0f;

            return oobThrows && unpresentOobThrows && dropRestored && hasDrop && hasNatural;
        }

        internal static bool Test_IT24_StrictCreationPathTrackingAndNativeTimingBinding()
        {
            var bridge = new TestNativeItemWarehouseBridge();
            bridge.Regions[(0, 0)] = new List<ItemData>
            {
                CreateTestItemData(9999U, isDropped: false, lastDropped: 0.0f)
            };
            var prep = new ItemPreparationAuthority(bridge);

            bridge.OnSubmitGeneration = (x, y) =>
            {
                bridge.DropItem(x, y, CreateTestItemData(8888U, isDropped: false, lastDropped: 15.0f));
                bridge.Regions[(x, y)] = new List<ItemData>
                {
                    CreateTestItemData(1001U, isDropped: false, lastDropped: 200.0f),
                    CreateTestItemData(9999U, isDropped: false, lastDropped: 0.0f)
                };
            };

            var metaStore = new ItemMetadataStore();
            prep.PrepareFullMap(new SessionEpoch(101UL), "MapPEI", 1, metaStore, currentTime: 200.0f);

            bool unknownClassified = metaStore.TryGetMetadata(9999U, out var rec9999)
                && rec9999.Origin == ItemOriginKind.Unknown && Math.Abs(rec9999.CreationTime - 0.0f) < 0.001f;

            ItemData found8888 = null;
            ItemData found1001 = null;
            var reg00 = bridge.Regions[(0, 0)];
            for (int i = 0; i < reg00.Count; i++)
            {
                if (reg00[i].instanceID == 8888U) found8888 = reg00[i];
                if (reg00[i].instanceID == 1001U) found1001 = reg00[i];
            }

            bool droppedRescued = found8888 != null;
            bool droppedPreserved = metaStore.TryGetMetadata(8888U, out var rec8888)
                && rec8888.Origin == ItemOriginKind.Dropped && Math.Abs(rec8888.CreationTime - 15.0f) < 0.001f;

            bool naturalClassified = metaStore.TryGetMetadata(1001U, out var rec1001)
                && rec1001.Origin == ItemOriginKind.Natural && rec1001.TimingBenchmark <= 0f;

            bridge.ActivateRegionTiming(0, 0, 300.0f, id => metaStore.TryGetMetadata(id, out var r) && r.Origin == ItemOriginKind.Natural);
            bool dropAgePreservedAfterTiming = found8888 != null && Math.Abs(found8888.lastDropped - 15.0f) < 0.001f;
            bool naturalAgeShifted = found1001 != null && Math.Abs(found1001.lastDropped - 300.0f) < 0.001f;

            return unknownClassified && droppedRescued && droppedPreserved && naturalClassified
                && dropAgePreservedAfterTiming && naturalAgeShifted;
        }

        internal static bool Test_IT25_ActivateRegionTimingFailsOnMissingRegionOrField()
        {
            var bridge = new TestNativeItemWarehouseBridge();
            bridge.SimulateMissingArray = true;
            bool missingArrayThrows = false;
            try { bridge.ActivateRegionTiming(0, 0, 100.0f); }
            catch (InvalidOperationException) { missingArrayThrows = true; }
            bridge.SimulateMissingArray = false;

            bridge.UnallocatedRegions.Add((0, 0));
            bool unallocatedThrows = false;
            try { bridge.ActivateRegionTiming(0, 0, 100.0f); }
            catch (InvalidOperationException) { unallocatedThrows = true; }
            bridge.UnallocatedRegions.Remove((0, 0));

            bool oobThrows = false;
            try { bridge.ActivateRegionTiming(64, 64, 100.0f); }
            catch (InvalidOperationException) { oobThrows = true; }

            bridge.SimulateMissingLastDroppedField = true;
            bool missingFieldThrows = false;
            try { bridge.ActivateRegionTiming(0, 0, 100.0f); }
            catch (InvalidOperationException) { missingFieldThrows = true; }
            bridge.SimulateMissingLastDroppedField = false;

            var prep = new ItemPreparationAuthority(bridge);
            bridge.GenerateNaturalItem(0, 0, CreateTestItemData(1001U, isDropped: false));
            var metaStore = new ItemMetadataStore();
            var port = new ItemExecutionPort(prep, metaStore, _ => 1U, CreateTestPolicy(), () => 100.0f);
            port.OnSessionBegin(101U);
            prep.PrepareFullMap(new SessionEpoch(101UL), "MapPEI", 1, metaStore, 100.0f);

            bridge.SimulateMissingLastDroppedField = true;
            var ticket = CreateTicket(new RegionKey(0, 0));
            bool acquireFailed = !port.TryAcquire(ticket, out _);
            bool failureDiagnosticLogged = port.LastAcquireDetail != null
                && port.LastAcquireDetail.Contains("activation-timing-exception");
            bridge.SimulateMissingLastDroppedField = false;

            bool acquireSucceeded = port.TryAcquire(ticket, out _);
            bool verifiedRespawn = bridge.RegionRespawnTimes.TryGetValue((0, 0), out float rTime)
                && Math.Abs(rTime - 100.0f) < 0.001f;
            bool verifiedDropped = Math.Abs(bridge.Regions[(0, 0)][0].lastDropped - 100.0f) < 0.001f;

            return missingArrayThrows && unallocatedThrows && oobThrows && missingFieldThrows && acquireFailed
                && failureDiagnosticLogged && acquireSucceeded && verifiedRespawn && verifiedDropped;
        }

        internal static bool Test_IT26_ActivateRegionTimingRollbackAndAcquireFailureClassification()
        {
            var bridge = new TestNativeItemWarehouseBridge();
            var prep = new ItemPreparationAuthority(bridge);
            var item1 = CreateTestItemData(1001U, isDropped: false, lastDropped: 5.0f);
            var item2 = CreateTestItemData(1002U, isDropped: false, lastDropped: 8.0f);
            bridge.GenerateNaturalItem(0, 0, item1);
            bridge.GenerateNaturalItem(0, 0, item2);
            bridge.RegionRespawnTimes[(0, 0)] = 2.0f;
            bridge.FailOnItemInstanceId = 1002U;

            bool timingFailed = false;
            try { bridge.ActivateRegionTiming(0, 0, 100.0f); }
            catch (InvalidOperationException) { timingFailed = true; }

            bool respawnRolledBack = Math.Abs(bridge.RegionRespawnTimes[(0, 0)] - 2.0f) < 0.001f;
            bool item1RolledBack = Math.Abs(item1.lastDropped - 5.0f) < 0.001f;
            bool item2RolledBack = Math.Abs(item2.lastDropped - 8.0f) < 0.001f;

            var metaStore = new ItemMetadataStore();
            var port = new ItemExecutionPort(prep, metaStore, _ => 1U, CreateTestPolicy(), () => 100.0f);
            port.OnSessionBegin(101U);
            prep.PrepareFullMap(new SessionEpoch(101UL), "MapPEI", 1, metaStore, 100.0f);

            var ticket = CreateTicket(new RegionKey(0, 0));
            bool acquireThrewFailed = false;
            try { port.OnAcquire(ticket); }
            catch (Exception ex)
            {
                acquireThrewFailed = port.ClassifyRegionFailure(ex) == EDomainFailureKind.Failed;
            }

            return timingFailed && respawnRolledBack && item1RolledBack && item2RolledBack && acquireThrewFailed;
        }

        private sealed class TestNativeItemWarehouseBridge : INativeItemWarehouseBridge
        {
            public bool InputsAvailable = true;
            public readonly Dictionary<(byte, byte), List<ItemData>> Regions =
                new Dictionary<(byte, byte), List<ItemData>>();

            public bool AreInputsAvailable(byte worldSize, out string failureReason)
            {
                failureReason = InputsAvailable ? string.Empty : "test inputs unavailable";
                return InputsAvailable;
            }

            public Action<byte, byte> OnSubmitGeneration;
            public readonly List<ItemData> NestedDropsDuringGeneration = new List<ItemData>();

            public void SubmitRegionGeneration(byte x, byte y)
            {
                if (!Regions.ContainsKey((x, y)))
                {
                    Regions[(x, y)] = new List<ItemData>();
                }
                var preList = Regions[(x, y)];
                var preIds = new HashSet<uint>();
                var preDropped = new List<ItemData>();
                if (preList != null)
                {
                    for (int i = 0; i < preList.Count; i++)
                    {
                        var it = preList[i];
                        if (it != null && it.instanceID != 0U)
                        {
                            preIds.Add(it.instanceID);
                            if (it.isDropped || ItemOriginTrackingSeam.IsTrackedDropped(it.instanceID))
                                preDropped.Add(it);
                        }
                    }
                }

                OnSubmitGeneration?.Invoke(x, y);

                if (Regions.TryGetValue((x, y), out var postList) && postList != null)
                {
                    var postIds = new HashSet<uint>();
                    for (int i = 0; i < postList.Count; i++)
                        if (postList[i] != null && postList[i].instanceID != 0U)
                            postIds.Add(postList[i].instanceID);

                    for (int i = 0; i < preDropped.Count; i++)
                    {
                        var dropped = preDropped[i];
                        if (dropped != null && postIds.Add(dropped.instanceID))
                            postList.Add(dropped);
                    }

                    for (int i = 0; i < NestedDropsDuringGeneration.Count; i++)
                    {
                        var dropped = NestedDropsDuringGeneration[i];
                        if (dropped != null && postIds.Add(dropped.instanceID))
                            postList.Add(dropped);
                    }
                    NestedDropsDuringGeneration.Clear();

                    for (int i = 0; i < postList.Count; i++)
                    {
                        ItemData item = postList[i];
                        if (item != null && item.instanceID != 0U && !preIds.Contains(item.instanceID))
                        {
                            if (!ItemOriginTrackingSeam.IsTrackedDropped(item.instanceID) && !item.isDropped)
                            {
                                OriginTracker?.TrackNaturalItem(item.instanceID);
                                ItemOriginTrackingSeam.NotifyNaturalItem(item.instanceID);
                            }
                        }
                    }
                }
            }

            public IReadOnlyList<ItemData> GetRegionItems(byte x, byte y) =>
                Regions.TryGetValue((x, y), out var list) ? list : null;

            public bool IsRegionAllocated(byte x, byte y) => true;

            public readonly List<(byte x, byte y)> HostPresentations = new List<(byte, byte)>();
            public readonly List<(byte x, byte y)> HostUnpresentations = new List<(byte, byte)>();
            public readonly List<(ulong observerId, ulong token, byte x, byte y)> GuestBindings =
                new List<(ulong, ulong, byte, byte)>();
            public readonly List<(ulong observerId, ulong token, byte x, byte y)> GuestUnbindings =
                new List<(ulong, ulong, byte, byte)>();

            public void PresentRegionToHost(byte x, byte y) => HostPresentations.Add((x, y));
            public void UnpresentRegionFromHost(byte x, byte y) => HostUnpresentations.Add((x, y));
            public void BindGuestBaseline(ulong observerId, ulong connectionToken, byte x, byte y) =>
                GuestBindings.Add((observerId, connectionToken, x, y));
            public void UnbindGuestBaseline(ulong observerId, ulong connectionToken, byte x, byte y) =>
                GuestUnbindings.Add((observerId, connectionToken, x, y));

            public readonly Dictionary<(byte, byte), float> RegionRespawnTimes =
                new Dictionary<(byte, byte), float>();

            public bool SimulateMissingArray = false;
            public readonly HashSet<(byte, byte)> UnallocatedRegions = new HashSet<(byte, byte)>();
            public bool SimulateMissingLastDroppedField = false;
            public uint FailOnItemInstanceId = 0U;

            public bool ActivateRegionTiming(byte x, byte y, float activationTime, Func<uint, bool> isNaturalItem = null)
            {
                if (SimulateMissingArray || Regions == null)
                {
                    throw new InvalidOperationException("ItemManager.regions array is null.");
                }
                if (x >= 64 || y >= 64 || UnallocatedRegions.Contains((x, y)))
                {
                    throw new InvalidOperationException($"Region ({x}, {y}) out of bounds or unallocated in test bridge.");
                }
                var lastDroppedField = typeof(ItemData).GetField("_lastDropped",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                if (lastDroppedField == null || SimulateMissingLastDroppedField)
                {
                    throw new InvalidOperationException("ItemData._lastDropped reflection field not found in test bridge.");
                }

                float originalRespawn = RegionRespawnTimes.TryGetValue((x, y), out float oldR) ? oldR : 0f;
                var modifiedItems = new List<(ItemData item, float previousLastDropped)>();

                try
                {
                    RegionRespawnTimes[(x, y)] = activationTime;
                    if (!RegionRespawnTimes.TryGetValue((x, y), out float actualRespawn) || Math.Abs(actualRespawn - activationTime) > 0.001f)
                    {
                        throw new InvalidOperationException($"RegionRespawnTimes verification failed for ({x}, {y}).");
                    }

                    if (!Regions.TryGetValue((x, y), out var list))
                    {
                        list = new List<ItemData>();
                        Regions[(x, y)] = list;
                    }

                    for (int i = 0; i < list.Count; i++)
                    {
                        ItemData data = list[i];
                        if (data == null) continue;
                        bool isNatural = isNaturalItem != null ? isNaturalItem(data.instanceID) : !data.isDropped;
                        if (isNatural)
                        {
                            modifiedItems.Add((data, data.lastDropped));
                            if (FailOnItemInstanceId != 0U && data.instanceID == FailOnItemInstanceId)
                            {
                                throw new InvalidOperationException($"Simulated failure on instance {data.instanceID}.");
                            }
                            lastDroppedField.SetValue(data, activationTime);
                            if (Math.Abs(data.lastDropped - activationTime) > 0.001f)
                            {
                                throw new InvalidOperationException($"ItemData._lastDropped verification failed for instance {data.instanceID}: expected {activationTime}, actual {data.lastDropped}.");
                            }
                        }
                    }
                    return true;
                }
                catch
                {
                    RegionRespawnTimes[(x, y)] = originalRespawn;
                    for (int i = 0; i < modifiedItems.Count; i++)
                    {
                        try { lastDroppedField.SetValue(modifiedItems[i].item, modifiedItems[i].previousLastDropped); }
                        catch { }
                    }
                    throw;
                }
            }

            public IItemOriginTracker OriginTracker { get; set; }
            public void BindOriginTracker(IItemOriginTracker tracker)
            {
                OriginTracker = tracker;
                ItemOriginTrackingSeam.SetActiveTracker(tracker);
            }

            public void DropItem(byte x, byte y, ItemData data)
            {
                if (!Regions.TryGetValue((x, y), out var list))
                {
                    list = new List<ItemData>();
                    Regions[(x, y)] = list;
                }
                list.Add(data);
                NestedDropsDuringGeneration.Add(data);
                OriginTracker?.TrackDroppedItem(data.instanceID);
                ItemOriginTrackingSeam.NotifyItemDropped(data.instanceID);
            }

            public void GenerateNaturalItem(byte x, byte y, ItemData data)
            {
                if (!Regions.TryGetValue((x, y), out var list))
                {
                    list = new List<ItemData>();
                    Regions[(x, y)] = list;
                }
                list.Add(data);
                OriginTracker?.TrackNaturalItem(data.instanceID);
                ItemOriginTrackingSeam.NotifyNaturalItem(data.instanceID);
            }
        }
    }
}
