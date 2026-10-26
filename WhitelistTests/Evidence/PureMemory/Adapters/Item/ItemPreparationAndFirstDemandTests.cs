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
        internal static bool Test_NIL00_NativeFixturePreflight()
        {
            Console.WriteLine("NIL00 begin native SteamPlayerID construction");
            var nativeId = new SteamPlayerID(
                new Steamworks.CSteamID(76561198000000001UL), 0, "NIL", "NIL", "NIL",
                new Steamworks.CSteamID(0UL));
            Console.WriteLine("NIL00 SteamPlayerID constructed: " + nativeId.GetType().AssemblyQualifiedName);
            Console.WriteLine("NIL00 begin native SteamPlayer allocation");
            var nativePlayer = (SteamPlayer)System.Runtime.Serialization.FormatterServices
                .GetUninitializedObject(typeof(SteamPlayer));
            Console.WriteLine("NIL00 SteamPlayer allocated: " + nativePlayer.GetType().AssemblyQualifiedName);
            return !object.ReferenceEquals(nativeId, null) && !object.ReferenceEquals(nativePlayer, null);
        }

        internal static bool Test_NIL04_RealIdentityValueAndReferenceSemantics()
        {
            const ulong id = 76561198000000001UL;
            var first = new SteamPlayerID(new Steamworks.CSteamID(id), 0, "NIL", "NIL", "NIL",
                new Steamworks.CSteamID(0UL));
            var equalValue = new SteamPlayerID(new Steamworks.CSteamID(id), 0, "NIL2", "NIL2", "NIL2",
                new Steamworks.CSteamID(0UL));
            SteamPlayerID absent = null;
            bool distinctReferences = !object.ReferenceEquals(first, equalValue);
            bool sameSteamIdValue = first.steamID.m_SteamID == equalValue.steamID.m_SteamID;
            bool equalityOverloadThrew = false;
            bool inequalityOverloadThrew = false;
            bool nullEqualityOverloadThrew = false;
            bool nullInequalityOverloadThrew = false;
            try { _ = first == absent; }
            catch (NullReferenceException) { equalityOverloadThrew = true; }
            try { _ = first != absent; }
            catch (NullReferenceException) { inequalityOverloadThrew = true; }
            try { _ = absent == null; }
            catch (NullReferenceException) { nullEqualityOverloadThrew = true; }
            try { _ = absent != null; }
            catch (NullReferenceException) { nullInequalityOverloadThrew = true; }
            Console.WriteLine($"NIL04 distinct references={distinctReferences}, same SteamID value={sameSteamIdValue}");
            return distinctReferences && sameSteamIdValue
                && !object.ReferenceEquals(first, null) && object.ReferenceEquals(null, null)
                && equalityOverloadThrew && inequalityOverloadThrew
                && nullEqualityOverloadThrew && nullInequalityOverloadThrew;
        }

        internal static bool Test_NIL01_RealNativeLookupContract()
        {
            const ulong targetId = 76561198000000001UL;
            var clientsField = typeof(Provider).GetField("_clients", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            var playerIdField = typeof(SteamPlayer).GetField("_playerID", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var find = typeof(DefaultNativeItemWarehouseBridge).GetMethod("FindClient", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            if (clientsField == null || playerIdField == null || find == null) throw new MissingMemberException("native FindClient fixture member missing");
            var originalClients = Provider.clients;
            SteamPlayer MakePlayer(SteamPlayerID id)
            {
                var player = (SteamPlayer)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(SteamPlayer));
                playerIdField.SetValue(player, id);
                return player;
            }
            object Lookup(ulong id) => find.Invoke(null, new object[] { id });
            try
            {
                clientsField.SetValue(null, null);
                if (!object.ReferenceEquals(Lookup(targetId), null)) return false;
                clientsField.SetValue(null, new List<SteamPlayer>());
                if (!object.ReferenceEquals(Lookup(targetId), null)) return false;
                var firstId = new SteamPlayerID(new Steamworks.CSteamID(targetId), 0, "NIL", "NIL", "NIL", new Steamworks.CSteamID(0UL));
                var duplicateId = new SteamPlayerID(new Steamworks.CSteamID(targetId), 0, "NIL2", "NIL2", "NIL2", new Steamworks.CSteamID(0UL));
                var first = MakePlayer(firstId);
                var duplicate = MakePlayer(duplicateId);
                clientsField.SetValue(null, new List<SteamPlayer> { null, MakePlayer(null), first, duplicate });
                if (!object.ReferenceEquals(Lookup(targetId), first)) return false;
                var missing = new SteamPlayerID(new Steamworks.CSteamID(targetId + 1UL), 0, "NIL3", "NIL3", "NIL3", new Steamworks.CSteamID(0UL));
                return object.ReferenceEquals(Lookup(targetId + 1UL), null)
                    && object.ReferenceEquals(Lookup(missing.steamID.m_SteamID), null);
            }
            finally { clientsField.SetValue(null, originalClients); }
        }

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

        internal static bool Test_IT28_OneObserverLeavingDoesNotStopTheOtherDemandCycle()
        {
            var bridge = new TestNativeItemWarehouseBridge();
            var prep = new ItemPreparationAuthority(bridge);
            bridge.GenerateNaturalItem(0, 0, CreateTestItemData(1001U, isDropped: false, lastDropped: 0f));
            var meta = new ItemMetadataStore();
            float now = 0f;
            var port = new ItemExecutionPort(prep, meta, _ => 1U, CreateTestPolicy(), () => now);
            var proj = new DemandProjectionEngine(new ObserverSpatialAuthority());
            var policy = ItemDemandPolicy.Create(radius: 0, worldSize: 1);
            proj.Register(policy);
            var seam = new ItemProductionControlSeam(prep, meta, port, proj, policy, 2.0f);
            seam.BeginSession(new SessionEpoch(101UL));
            if (!prep.PrepareFullMap(new SessionEpoch(101UL), "MapPEI", 1, meta, 0f)) return false;
            seam.UpdateObserver(1001UL, 201UL, 0, 0, true);
            seam.UpdateObserver(1002UL, 202UL, 0, 0, true);
            seam.Tick(0f);
            var region = new RegionKey(0, 0);
            if (seam.GetDemand(region) != 2) return false;
            seam.RemoveObserver(1002UL);
            seam.Tick(0f);
            if (seam.GetDemand(region) != 1 || seam.ActiveLeaseCount != 1) return false;
            ItemCycleThresholds.Override = new ItemCycleThresholds(10f, 60f, 999f, 0f, true);
            try
            {
                now = 30f;
                for (int i = 0; i < 4; i++) seam.Tick(0f);
                bool naturalRemoved = bridge.Regions[(0, 0)].Find(it => it != null && it.instanceID == 1001U) == null;
                return naturalRemoved && seam.GetDemand(region) == 1;
            }
            finally
            {
                ItemCycleThresholds.Override = null;
            }
        }

        internal static bool Test_IT30_UnvisitedDroppedExpiresWithoutStartingNaturalCycle()
        {
            var bridge = new TestNativeItemWarehouseBridge();
            var prep = new ItemPreparationAuthority(bridge);
            var meta = new ItemMetadataStore();
            float now = 0f;
            var port = new ItemExecutionPort(prep, meta, _ => 1U, CreateTestPolicy(), () => now);
            var proj = new DemandProjectionEngine(new ObserverSpatialAuthority());
            var policy = ItemDemandPolicy.Create(radius: 0, worldSize: 2);
            proj.Register(policy);
            var seam = new ItemProductionControlSeam(prep, meta, port, proj, policy, 2.0f);
            seam.BeginSession(new SessionEpoch(101UL));
            if (!prep.PrepareFullMap(new SessionEpoch(101UL), "MapPEI", 2, meta, 0f)) return false;
            bridge.GenerateNaturalItem(1, 1, CreateTestItemData(1001U, isDropped: false, lastDropped: 0f));
            bridge.DropItem(1, 1, CreateTestItemData(2001U, isDropped: false, lastDropped: 0f));
            ItemCycleThresholds.Override = new ItemCycleThresholds(10f, 10f, 999f, 0f, true);
            try
            {
                now = 30f;
                for (int i = 0; i < 4; i++) seam.Tick(0f);
                var region = new RegionKey(1, 1);
                bool dropGone = bridge.Regions[(1, 1)].Find(it => it != null && it.instanceID == 2001U) == null;
                bool naturalKept = bridge.Regions[(1, 1)].Find(it => it != null && it.instanceID == 1001U) != null;
                return dropGone && naturalKept && !port.IsRegionActivated(region) && seam.GetDemand(region) == 0;
            }
            finally
            {
                ItemCycleThresholds.Override = null;
            }
        }

        internal static bool Test_IT31_DemandRunsRespawnAndRecordsOnlyRealCreation()
        {
            var bridge = new TestNativeItemWarehouseBridge();
            var prep = new ItemPreparationAuthority(bridge);
            var meta = new ItemMetadataStore();
            float now = 0f;
            var port = new ItemExecutionPort(prep, meta, _ => 1U, CreateTestPolicy(), () => now);
            var proj = new DemandProjectionEngine(new ObserverSpatialAuthority());
            var policy = ItemDemandPolicy.Create(radius: 0, worldSize: 1);
            proj.Register(policy);
            var seam = new ItemProductionControlSeam(prep, meta, port, proj, policy, 2.0f);
            seam.BeginSession(new SessionEpoch(101UL));
            if (!prep.PrepareFullMap(new SessionEpoch(101UL), "MapPEI", 1, meta, 0f)) return false;
            seam.UpdateObserver(1001UL, 201UL, 0, 0, true);
            seam.Tick(0f);
            bridge.SpawnPointCount = 1;
            ItemCycleThresholds.Override = new ItemCycleThresholds(999f, 999f, 10f, 1f, true);
            try
            {
                now = 30f;
                for (int i = 0; i < 3; i++) seam.Tick(0f);
                var list = bridge.Regions[(0, 0)];
                ItemData created = list.Find(it => it != null && it.instanceID != 0U);
                if (created == null) return false;
                bool trackedNatural = meta.TryGetMetadata(created.instanceID, out ItemMetadataRecord rec)
                    && rec.Origin == ItemOriginKind.Natural;
                bool respawnAdvanced = bridge.RegionRespawnTimes.TryGetValue((0, 0), out float stamp)
                    && Math.Abs(stamp - 30f) < 0.001f;
                return trackedNatural && respawnAdvanced && seam.GetDemand(new RegionKey(0, 0)) == 1;
            }
            finally
            {
                ItemCycleThresholds.Override = null;
            }
        }

        internal static bool Test_IT32_NestedRespawnDropStaysDroppedAndIsNotReplaced()
        {
            var bridge = new TestNativeItemWarehouseBridge();
            var prep = new ItemPreparationAuthority(bridge);
            var meta = new ItemMetadataStore();
            float now = 0f;
            var port = new ItemExecutionPort(prep, meta, _ => 1U, CreateTestPolicy(), () => now);
            var proj = new DemandProjectionEngine(new ObserverSpatialAuthority());
            var policy = ItemDemandPolicy.Create(radius: 0, worldSize: 1);
            proj.Register(policy);
            var seam = new ItemProductionControlSeam(prep, meta, port, proj, policy, 2.0f);
            seam.BeginSession(new SessionEpoch(101UL));
            if (!prep.PrepareFullMap(new SessionEpoch(101UL), "MapPEI", 1, meta, 0f)) return false;
            seam.UpdateObserver(1001UL, 201UL, 0, 0, true);
            seam.Tick(0f);
            bridge.SpawnPointCount = 2;
            bridge.OnRespawn = (x, y) => bridge.DropItem(x, y, CreateTestItemData(8001U, isDropped: false, lastDropped: 5f));
            ItemCycleThresholds.Override = new ItemCycleThresholds(999f, 999f, 10f, 1f, true);
            try
            {
                now = 30f;
                seam.Tick(0f);
                var list = bridge.Regions[(0, 0)];
                ItemData nested = list.Find(it => it != null && it.instanceID == 8001U);
                ItemData created = list.Find(it => it != null && it.instanceID != 8001U && it.instanceID != 0U);
                if (nested == null || created == null) return false;
                bool dropKept = meta.TryGetMetadata(8001U, out ItemMetadataRecord drop)
                    && drop.Origin == ItemOriginKind.Dropped
                    && Math.Abs(drop.CreationTime - 5f) < 0.001f;
                bool naturalTracked = meta.TryGetMetadata(created.instanceID, out ItemMetadataRecord natural)
                    && natural.Origin == ItemOriginKind.Natural;
                return dropKept && naturalTracked && Math.Abs(nested.lastDropped - 5f) < 0.001f;
            }
            finally
            {
                ItemCycleThresholds.Override = null;
            }
        }

        internal static bool Test_IT33_HysteresisDoesNotResetAgeOrImpersonateDemand()
        {
            var bridge = new TestNativeItemWarehouseBridge();
            var prep = new ItemPreparationAuthority(bridge);
            bridge.GenerateNaturalItem(0, 0, CreateTestItemData(1001U, isDropped: false, lastDropped: 0f));
            var meta = new ItemMetadataStore();
            float now = 0f;
            var port = new ItemExecutionPort(prep, meta, _ => 1U, CreateTestPolicy(), () => now);
            var proj = new DemandProjectionEngine(new ObserverSpatialAuthority());
            var policy = ItemDemandPolicy.Create(radius: 0, worldSize: 1);
            proj.Register(policy);
            var seam = new ItemProductionControlSeam(prep, meta, port, proj, policy, 2.0f);
            seam.BeginSession(new SessionEpoch(101UL));
            if (!prep.PrepareFullMap(new SessionEpoch(101UL), "MapPEI", 1, meta, 0f)) return false;
            seam.UpdateObserver(1001UL, 201UL, 0, 0, true);
            seam.Tick(0f);
            bridge.SpawnPointCount = 1;
            var region = new RegionKey(0, 0);
            float activation = port.GetActivationTime(region);
            seam.RemoveObserver(1001UL);
            seam.Tick(0f);
            if (seam.GetDemand(region) != 0 || seam.PendingReleaseCount != 1 || !seam.IsLeased(region)) return false;
            ItemCycleThresholds.Override = new ItemCycleThresholds(10f, 10f, 10f, 1f, true);
            try
            {
                now = 30f;
                int before = bridge.Regions[(0, 0)].Count;
                for (int i = 0; i < 4; i++) seam.Tick(0f);
                bool agedUntouched = bridge.Regions[(0, 0)].Find(it => it != null && it.instanceID == 1001U) != null
                    && Math.Abs(bridge.Regions[(0, 0)].Find(it => it.instanceID == 1001U).lastDropped - activation) < 0.001f;
                bool noRespawn = bridge.Regions[(0, 0)].Count == before;
                seam.Tick(2f);
                bool released = seam.PendingReleaseCount == 0 && !seam.IsLeased(region);
                bool recordKept = port.IsRegionActivated(region)
                    && Math.Abs(port.GetActivationTime(region) - activation) < 0.001f
                    && meta.TryGetMetadata(1001U, out ItemMetadataRecord rec)
                    && rec.Origin == ItemOriginKind.Natural;
                return agedUntouched && noRespawn && released && recordKept && seam.GetDemand(region) == 0;
            }
            finally
            {
                ItemCycleThresholds.Override = null;
            }
        }

        internal static bool Test_IT34_NaturalAndDroppedUseDifferentU3dsLimits()
        {
            var bridge = new TestNativeItemWarehouseBridge();
            var prep = new ItemPreparationAuthority(bridge);
            bridge.GenerateNaturalItem(0, 0, CreateTestItemData(1001U, isDropped: false, lastDropped: 0f));
            bridge.DropItem(0, 0, CreateTestItemData(2001U, isDropped: false, lastDropped: 0f));
            var meta = new ItemMetadataStore();
            float now = 0f;
            var port = new ItemExecutionPort(prep, meta, _ => 1U, CreateTestPolicy(), () => now);
            var proj = new DemandProjectionEngine(new ObserverSpatialAuthority());
            var policy = ItemDemandPolicy.Create(radius: 0, worldSize: 1);
            proj.Register(policy);
            var seam = new ItemProductionControlSeam(prep, meta, port, proj, policy, 2.0f);
            seam.BeginSession(new SessionEpoch(101UL));
            if (!prep.PrepareFullMap(new SessionEpoch(101UL), "MapPEI", 1, meta, 0f)) return false;
            seam.UpdateObserver(1001UL, 201UL, 0, 0, true);
            seam.Tick(0f);
            ItemCycleThresholds.Override = new ItemCycleThresholds(100f, 10f, 999f, 0f, true);
            try
            {
                now = 30f;
                for (int i = 0; i < 3; i++) seam.Tick(0f);
                bool naturalKept = bridge.Regions[(0, 0)].Find(it => it != null && it.instanceID == 1001U) != null;
                bool dropGone = bridge.Regions[(0, 0)].Find(it => it != null && it.instanceID == 2001U) == null;
                bool unknownSkipped = true;
                bridge.Regions[(0, 0)].Add(CreateTestItemData(3001U, isDropped: false, lastDropped: 0f));
                seam.Tick(0f);
                unknownSkipped = bridge.Regions[(0, 0)].Find(it => it != null && it.instanceID == 3001U) != null
                    && meta.TryGetMetadata(3001U, out ItemMetadataRecord unknown)
                    && unknown.Origin == ItemOriginKind.Unknown;
                return naturalKept && dropGone && unknownSkipped;
            }
            finally
            {
                ItemCycleThresholds.Override = null;
            }
        }

        internal static bool Test_IT36_OneStepDoesNotClearEveryExpiredDrop()
        {
            var bridge = new TestNativeItemWarehouseBridge();
            var prep = new ItemPreparationAuthority(bridge);
            var meta = new ItemMetadataStore();
            float now = 0f;
            var port = new ItemExecutionPort(prep, meta, _ => 1U, CreateTestPolicy(), () => now);
            var proj = new DemandProjectionEngine(new ObserverSpatialAuthority());
            var policy = ItemDemandPolicy.Create(radius: 0, worldSize: 1);
            proj.Register(policy);
            var seam = new ItemProductionControlSeam(prep, meta, port, proj, policy, 2.0f);
            seam.BeginSession(new SessionEpoch(101UL));
            if (!prep.PrepareFullMap(new SessionEpoch(101UL), "MapPEI", 1, meta, 0f)) return false;
            bridge.DropItem(0, 0, CreateTestItemData(2001U, isDropped: true, lastDropped: 0f));
            bridge.DropItem(0, 0, CreateTestItemData(2002U, isDropped: true, lastDropped: 0f));
            ItemCycleThresholds.Override = new ItemCycleThresholds(999f, 10f, 999f, 0f, true);
            try
            {
                now = 30f;
                seam.Tick(0f);
                int remaining = 0;
                var list = bridge.Regions[(0, 0)];
                for (int i = 0; i < list.Count; i++)
                    if (list[i] != null && (list[i].instanceID == 2001U || list[i].instanceID == 2002U)) remaining++;
                return remaining == 1;
            }
            finally
            {
                ItemCycleThresholds.Override = null;
            }
        }

        internal static bool Test_IT37_DeclinedRespawnDoesNotCommitTimeAndArenaSkipsExpiry()
        {
            var bridge = new TestNativeItemWarehouseBridge();
            var prep = new ItemPreparationAuthority(bridge);
            var meta = new ItemMetadataStore();
            float now = 0f;
            var port = new ItemExecutionPort(prep, meta, _ => 1U, CreateTestPolicy(), () => now);
            var proj = new DemandProjectionEngine(new ObserverSpatialAuthority());
            var policy = ItemDemandPolicy.Create(radius: 0, worldSize: 1);
            proj.Register(policy);
            var seam = new ItemProductionControlSeam(prep, meta, port, proj, policy, 2.0f);
            seam.BeginSession(new SessionEpoch(101UL));
            if (!prep.PrepareFullMap(new SessionEpoch(101UL), "MapPEI", 1, meta, 0f)) return false;
            seam.UpdateObserver(1001UL, 201UL, 0, 0, true);
            seam.Tick(0f);
            bridge.SpawnPointCount = 1;
            bridge.DeclineRespawn = true;
            bridge.DropItem(0, 0, CreateTestItemData(2001U, isDropped: true, lastDropped: 0f));
            ItemCycleThresholds.Override = new ItemCycleThresholds(10f, 10f, 10f, 1f, false);
            try
            {
                now = 30f;
                seam.Tick(0f);
                bool dropKept = bridge.Regions[(0, 0)].Find(it => it != null && it.instanceID == 2001U) != null;
                bool timeUnmoved = Math.Abs(bridge.ReadLastRespawn(0, 0) - 0f) < 0.001f;
                bool noCreate = bridge.Regions[(0, 0)].Find(it => it != null && it.instanceID >= 7001U) == null;
                return dropKept && timeUnmoved && noCreate;
            }
            finally
            {
                ItemCycleThresholds.Override = null;
            }
        }

        internal static bool Test_IT38_ReentryDoesNotRestartNaturalTiming()
        {
            var bridge = new TestNativeItemWarehouseBridge();
            var prep = new ItemPreparationAuthority(bridge);
            bridge.GenerateNaturalItem(0, 0, CreateTestItemData(1001U, isDropped: false, lastDropped: 4f));
            var meta = new ItemMetadataStore();
            float now = 10f;
            var port = new ItemExecutionPort(prep, meta, _ => 1U, CreateTestPolicy(), () => now);
            var proj = new DemandProjectionEngine(new ObserverSpatialAuthority());
            var policy = ItemDemandPolicy.Create(radius: 0, worldSize: 1);
            proj.Register(policy);
            var seam = new ItemProductionControlSeam(prep, meta, port, proj, policy, 2.0f);
            seam.BeginSession(new SessionEpoch(101UL));
            if (!prep.PrepareFullMap(new SessionEpoch(101UL), "MapPEI", 1, meta, 4f)) return false;
            seam.UpdateObserver(1001UL, 201UL, 0, 0, true);
            seam.Tick(0f);
            var region = new RegionKey(0, 0);
            float first = port.GetActivationTime(region);
            seam.RemoveObserver(1001UL);
            seam.Tick(2f);
            now = 40f;
            seam.UpdateObserver(1001UL, 201UL, 0, 0, true);
            seam.Tick(0f);
            bool sameActivation = Math.Abs(port.GetActivationTime(region) - first) < 0.001f
                && Math.Abs(first - 10f) < 0.001f;
            bool ageKept = Math.Abs(bridge.Regions[(0, 0)].Find(it => it.instanceID == 1001U).lastDropped - 10f) < 0.001f;
            return sameActivation && ageKept && seam.GetDemand(region) == 1 && meta.TryGetMetadata(1001U, out _);
        }

        internal static bool Test_IT39_ReturnClearsAllExpiredBeforeAnyRestock()
        {
            var bridge = new TestNativeItemWarehouseBridge();
            var prep = new ItemPreparationAuthority(bridge);
            bridge.GenerateNaturalItem(0, 0, CreateTestItemData(1001U, false, 0f));
            bridge.GenerateNaturalItem(0, 0, CreateTestItemData(1002U, false, 0f));
            bridge.GenerateNaturalItem(0, 0, CreateTestItemData(1003U, false, 0f));
            var meta = new ItemMetadataStore();
            float now = 10f;
            var port = new ItemExecutionPort(prep, meta, _ => 1U, CreateTestPolicy(), () => now);
            var proj = new DemandProjectionEngine(new ObserverSpatialAuthority());
            var policy = ItemDemandPolicy.Create(radius: 0, worldSize: 1);
            proj.Register(policy);
            var seam = new ItemProductionControlSeam(prep, meta, port, proj, policy, 2.0f);
            seam.BeginSession(new SessionEpoch(101UL));
            if (!prep.PrepareFullMap(new SessionEpoch(101UL), "MapPEI", 1, meta, 0f)) return false;
            seam.UpdateObserver(1001UL, 201UL, 0, 0, true);
            seam.Tick(0f);
            seam.RemoveObserver(1001UL);
            seam.Tick(2f);
            now = 40f;
            bridge.SpawnPointCount = 3;
            ItemCycleThresholds.Override = new ItemCycleThresholds(10f, 60f, 1f, 1f, true);
            try
            {
                seam.UpdateObserver(1001UL, 201UL, 0, 0, true);
                seam.Tick(0f);
                int left = 0;
                var items = bridge.Regions[(0, 0)];
                for (int i = 0; i < items.Count; i++)
                    if (items[i] != null && items[i].instanceID >= 1001U && items[i].instanceID <= 1003U) left++;
                bool restocked = items.Find(it => it != null && it.instanceID >= 7001U) != null;
                return left == 2 && !restocked && seam.PendingAcquireRetryCount == 0;
            }
            finally { ItemCycleThresholds.Override = null; }
        }

        internal static bool Test_IT40_SurvivorAgeKeptAndFailedRestockStops()
        {
            var bridge = new TestNativeItemWarehouseBridge();
            var prep = new ItemPreparationAuthority(bridge);
            bridge.GenerateNaturalItem(0, 0, CreateTestItemData(1001U, false, 0f));
            var meta = new ItemMetadataStore();
            float now = 10f;
            var port = new ItemExecutionPort(prep, meta, _ => 1U, CreateTestPolicy(), () => now);
            var proj = new DemandProjectionEngine(new ObserverSpatialAuthority());
            var policy = ItemDemandPolicy.Create(radius: 0, worldSize: 1);
            proj.Register(policy);
            var seam = new ItemProductionControlSeam(prep, meta, port, proj, policy, 2.0f);
            seam.BeginSession(new SessionEpoch(101UL));
            if (!prep.PrepareFullMap(new SessionEpoch(101UL), "MapPEI", 1, meta, 0f)) return false;
            seam.UpdateObserver(1001UL, 201UL, 0, 0, true);
            seam.Tick(0f);
            seam.RemoveObserver(1001UL);
            seam.Tick(2f);
            bridge.GenerateNaturalItem(0, 0, CreateTestItemData(1002U, false, 35f));
            now = 40f;
            bridge.SpawnPointCount = 3;
            bridge.DeclineRespawn = true;
            ItemCycleThresholds.Override = new ItemCycleThresholds(10f, 60f, 30f, 1f, true);
            try
            {
                seam.UpdateObserver(1001UL, 201UL, 0, 0, true);
                for (int i = 0; i < 8; i++) seam.Tick(0f);
                var items = bridge.Regions[(0, 0)];
                bool expiredGone = items.Find(it => it != null && it.instanceID == 1001U) == null;
                ItemData survivor = items.Find(it => it != null && it.instanceID == 1002U);
                bool ageKept = survivor != null && Math.Abs(survivor.lastDropped - 35f) < 0.001f;
                bool unfilled = items.Find(it => it != null && it.instanceID >= 7001U) == null;
                return expiredGone && ageKept && unfilled && seam.PendingAcquireRetryCount == 0;
            }
            finally { ItemCycleThresholds.Override = null; }
        }

        internal static bool Test_IT41_ReturnWaitsAndDeliversWithoutFailureBudget()
        {
            var bridge = new TestNativeItemWarehouseBridge();
            var prep = new ItemPreparationAuthority(bridge);
            for (uint id = 1; id <= 12; id++)
                bridge.GenerateNaturalItem(0, 0, CreateTestItemData(id, false, 0f));
            var meta = new ItemMetadataStore();
            float now = 10f;
            var presented = new List<ulong>();
            var port = new ItemExecutionPort(prep, meta, _ => 1U, CreateTestPolicy(), () => now, null,
                id => id == 1001UL, region => presented.Add(1001UL),
                (id, token, region) => presented.Add(id));
            var proj = new DemandProjectionEngine(new ObserverSpatialAuthority());
            var policy = ItemDemandPolicy.Create(radius: 0, worldSize: 1);
            proj.Register(policy);
            var seam = new ItemProductionControlSeam(prep, meta, port, proj, policy, 2.0f);
            seam.BeginSession(new SessionEpoch(101UL));
            if (!prep.PrepareFullMap(new SessionEpoch(101UL), "MapPEI", 1, meta, 0f)) return false;
            seam.UpdateObserver(1001UL, 201UL, 0, 0, true);
            seam.Tick(0f);
            int firstVisit = presented.Count;
            seam.RemoveObserver(1001UL);
            seam.Tick(2f);
            now = 40f;
            ItemCycleThresholds.Override = new ItemCycleThresholds(10f, 60f, 99f, 0f, true);
            try
            {
                seam.UpdateObserver(1001UL, 201UL, 0, 0, true);
                bool held = presented.Count == firstVisit;
                for (int i = 0; i < 13; i++) { now += 1f; seam.Tick(1f); }
                bool delivered = presented.Count == firstVisit + 1 && presented[presented.Count - 1] == 1001UL;
                return held && delivered && seam.PendingAcquireRetryCount == 0 && !seam.RepairRequired;
            }
            finally { ItemCycleThresholds.Override = null; }
        }

        internal static bool Test_IT42_HysteresisReentryStillSettlesBeforePresent()
        {
            var bridge = new TestNativeItemWarehouseBridge();
            var prep = new ItemPreparationAuthority(bridge);
            bridge.GenerateNaturalItem(0, 0, CreateTestItemData(1001U, false, 0f));
            var meta = new ItemMetadataStore();
            float now = 10f;
            int presented = 0;
            var port = new ItemExecutionPort(prep, meta, _ => 1U, CreateTestPolicy(), () => now, null,
                id => true, region => presented++, (id, token, region) => presented++);
            var proj = new DemandProjectionEngine(new ObserverSpatialAuthority());
            var policy = ItemDemandPolicy.Create(radius: 0, worldSize: 1);
            proj.Register(policy);
            var seam = new ItemProductionControlSeam(prep, meta, port, proj, policy, 2.0f);
            seam.BeginSession(new SessionEpoch(101UL));
            if (!prep.PrepareFullMap(new SessionEpoch(101UL), "MapPEI", 1, meta, 0f)) return false;
            seam.UpdateObserver(1001UL, 201UL, 0, 0, true);
            seam.Tick(0f);
            int first = presented;
            seam.RemoveObserver(1001UL);
            now = 40f;
            ItemCycleThresholds.Override = new ItemCycleThresholds(10f, 60f, 99f, 0f, true);
            try
            {
                seam.UpdateObserver(1001UL, 201UL, 0, 0, true);
                bool held = presented == first && seam.ActiveLeaseCount == 1;
                seam.Tick(0f);
                seam.Tick(0f);
                bool delivered = presented == first + 1;
                bool cleared = bridge.Regions[(0, 0)].Find(it => it != null && it.instanceID == 1001U) == null;
                return held && delivered && cleared && seam.PendingAcquireRetryCount == 0;
            }
            finally { ItemCycleThresholds.Override = null; }
        }

        internal static bool Test_IT43_LastDemandLeavesFinishesWithoutList()
        {
            var bridge = new TestNativeItemWarehouseBridge();
            var prep = new ItemPreparationAuthority(bridge);
            bridge.GenerateNaturalItem(0, 0, CreateTestItemData(1001U, false, 0f));
            bridge.GenerateNaturalItem(0, 0, CreateTestItemData(1002U, false, 0f));
            var meta = new ItemMetadataStore();
            float now = 10f;
            int presented = 0;
            var port = new ItemExecutionPort(prep, meta, _ => 1U, CreateTestPolicy(), () => now, null,
                id => true, region => presented++, (id, token, region) => presented++);
            var proj = new DemandProjectionEngine(new ObserverSpatialAuthority());
            var policy = ItemDemandPolicy.Create(radius: 0, worldSize: 1);
            proj.Register(policy);
            var seam = new ItemProductionControlSeam(prep, meta, port, proj, policy, 2.0f);
            seam.BeginSession(new SessionEpoch(101UL));
            if (!prep.PrepareFullMap(new SessionEpoch(101UL), "MapPEI", 1, meta, 0f)) return false;
            seam.UpdateObserver(1001UL, 201UL, 0, 0, true);
            seam.Tick(0f);
            int first = presented;
            seam.RemoveObserver(1001UL);
            seam.Tick(2f);
            now = 40f;
            bridge.SpawnPointCount = 2;
            ItemCycleThresholds.Override = new ItemCycleThresholds(10f, 60f, 1f, 1f, true);
            try
            {
                seam.UpdateObserver(1001UL, 201UL, 0, 0, true);
                seam.Tick(0f);
                seam.RemoveObserver(1001UL);
                for (int i = 0; i < 6; i++) seam.Tick(0f);
                int created = bridge.Regions[(0, 0)].FindAll(it => it != null && it.instanceID >= 7001U).Count;
                seam.Tick(0f);
                int after = bridge.Regions[(0, 0)].FindAll(it => it != null && it.instanceID >= 7001U).Count;
                bool originalsGone = bridge.Regions[(0, 0)].Find(it => it != null && it.instanceID == 1001U) == null
                    && bridge.Regions[(0, 0)].Find(it => it != null && it.instanceID == 1002U) == null;
                return originalsGone && presented == first && created > 0 && after == created
                    && seam.GetDemand(new RegionKey(0, 0)) == 0 && seam.PendingAcquireRetryCount == 0;
            }
            finally { ItemCycleThresholds.Override = null; }
        }

        internal static bool Test_IT44_UnreadyPickupDoesNotWriteOrDelete()
        {
            var bridge = new TestNativeItemWarehouseBridge();
            var prep = new ItemPreparationAuthority(bridge);
            bridge.GenerateNaturalItem(0, 0, CreateTestItemData(1001U, false, 0f));
            bridge.GenerateNaturalItem(0, 0, CreateTestItemData(1002U, false, 0f));
            var meta = new ItemMetadataStore();
            float now = 10f;
            var port = new ItemExecutionPort(prep, meta, _ => 1U, CreateTestPolicy(), () => now);
            var proj = new DemandProjectionEngine(new ObserverSpatialAuthority());
            var policy = ItemDemandPolicy.Create(radius: 0, worldSize: 1);
            proj.Register(policy);
            var seam = new ItemProductionControlSeam(prep, meta, port, proj, policy, 2.0f);
            seam.BeginSession(new SessionEpoch(101UL));
            if (!prep.PrepareFullMap(new SessionEpoch(101UL), "MapPEI", 1, meta, 0f)) return false;
            seam.UpdateObserver(1001UL, 201UL, 0, 0, true);
            seam.Tick(0f);
            seam.RemoveObserver(1001UL);
            seam.Tick(2f);
            now = 40f;
            ItemCycleThresholds.Override = new ItemCycleThresholds(10f, 60f, 99f, 0f, true);
            try
            {
                seam.UpdateObserver(1001UL, 201UL, 0, 0, true);
                int writes = 0;
                bool took = ItemSettlementGate.TryPickup(0, 0, () => writes++, () => bridge.TryRemoveItem(0, 0, 1002U));
                bool stillThere = bridge.Regions[(0, 0)].Find(it => it != null && it.instanceID == 1002U) != null;
                return !took && writes == 0 && stillThere && ItemSettlementGate.BlockedPickups == 1;
            }
            finally { ItemCycleThresholds.Override = null; }
        }

        internal static bool Test_IT45_RegionsShareOneSettlementAction()
        {
            var bridge = new TestNativeItemWarehouseBridge();
            var prep = new ItemPreparationAuthority(bridge);
            bridge.GenerateNaturalItem(0, 0, CreateTestItemData(1001U, false, 0f));
            bridge.GenerateNaturalItem(0, 0, CreateTestItemData(1002U, false, 0f));
            bridge.GenerateNaturalItem(1, 0, CreateTestItemData(2001U, false, 0f));
            bridge.GenerateNaturalItem(1, 0, CreateTestItemData(2002U, false, 0f));
            var meta = new ItemMetadataStore();
            float now = 10f;
            var port = new ItemExecutionPort(prep, meta, _ => 1U, CreateTestPolicy(), () => now);
            var proj = new DemandProjectionEngine(new ObserverSpatialAuthority());
            var policy = ItemDemandPolicy.Create(radius: 0, worldSize: 2);
            proj.Register(policy);
            var seam = new ItemProductionControlSeam(prep, meta, port, proj, policy, 2.0f);
            seam.BeginSession(new SessionEpoch(101UL));
            if (!prep.PrepareFullMap(new SessionEpoch(101UL), "MapPEI", 2, meta, 0f)) return false;
            seam.UpdateObserver(1001UL, 201UL, 0, 0, true);
            seam.UpdateObserver(1002UL, 202UL, 1, 0, true);
            seam.Tick(0f);
            seam.RemoveObserver(1001UL);
            seam.RemoveObserver(1002UL);
            seam.Tick(2f);
            now = 40f;
            ItemCycleThresholds.Override = new ItemCycleThresholds(10f, 60f, 99f, 0f, true);
            try
            {
                seam.UpdateObserver(1001UL, 201UL, 0, 0, true);
                seam.UpdateObserver(1002UL, 202UL, 1, 0, true);
                int before = bridge.Regions[(0, 0)].Count + bridge.Regions[(1, 0)].Count;
                seam.Tick(0f);
                int after = bridge.Regions[(0, 0)].Count + bridge.Regions[(1, 0)].Count;
                return before - after == 1 && seam.PendingAcquireRetryCount == 0;
            }
            finally { ItemCycleThresholds.Override = null; }
        }

        internal static bool Test_IT46_LaterGuestWaitsForSameResult()
        {
            var bridge = new TestNativeItemWarehouseBridge();
            var prep = new ItemPreparationAuthority(bridge);
            bridge.GenerateNaturalItem(0, 0, CreateTestItemData(1001U, false, 0f));
            bridge.GenerateNaturalItem(0, 0, CreateTestItemData(1002U, false, 0f));
            bridge.GenerateNaturalItem(0, 0, CreateTestItemData(1003U, false, 0f));
            var meta = new ItemMetadataStore();
            float now = 10f;
            var presented = new List<ulong>();
            var port = new ItemExecutionPort(prep, meta, _ => 1U, CreateTestPolicy(), () => now, null,
                id => id == 1001UL, region => presented.Add(1001UL),
                (id, token, region) => presented.Add(id));
            var proj = new DemandProjectionEngine(new ObserverSpatialAuthority());
            var policy = ItemDemandPolicy.Create(radius: 0, worldSize: 1);
            proj.Register(policy);
            var seam = new ItemProductionControlSeam(prep, meta, port, proj, policy, 2.0f);
            seam.BeginSession(new SessionEpoch(101UL));
            if (!prep.PrepareFullMap(new SessionEpoch(101UL), "MapPEI", 1, meta, 0f)) return false;
            seam.UpdateObserver(1001UL, 201UL, 0, 0, true);
            seam.Tick(0f);
            seam.RemoveObserver(1001UL);
            seam.Tick(2f);
            now = 40f;
            ItemCycleThresholds.Override = new ItemCycleThresholds(10f, 60f, 99f, 0f, true);
            try
            {
                seam.UpdateObserver(1001UL, 201UL, 0, 0, true);
                seam.Tick(0f);
                int mid = presented.Count;
                seam.UpdateObserver(1002UL, 202UL, 0, 0, true);
                bool guestHeld = presented.Count == mid;
                for (int i = 0; i < 4; i++) seam.Tick(0f);
                bool both = presented.Contains(1001UL) && presented.Contains(1002UL);
                return guestHeld && both && bridge.RemovalCalls == 3 && seam.PendingAcquireRetryCount == 0;
            }
            finally { ItemCycleThresholds.Override = null; }
        }

        internal static bool Test_IT47_StaleRegionIdentityIsolatesTheJob()
        {
            var bridge = new TestNativeItemWarehouseBridge();
            var prep = new ItemPreparationAuthority(bridge);
            bridge.GenerateNaturalItem(0, 0, CreateTestItemData(1001U, false, 0f));
            bridge.GenerateNaturalItem(0, 0, CreateTestItemData(1002U, false, 0f));
            var meta = new ItemMetadataStore();
            float now = 10f;
            uint generation = 1U;
            int presented = 0;
            var port = new ItemExecutionPort(prep, meta, _ => generation, CreateTestPolicy(), () => now, null,
                id => true, region => presented++, (id, token, region) => presented++);
            var proj = new DemandProjectionEngine(new ObserverSpatialAuthority());
            var policy = ItemDemandPolicy.Create(radius: 0, worldSize: 1);
            proj.Register(policy);
            var seam = new ItemProductionControlSeam(prep, meta, port, proj, policy, 2.0f);
            seam.BeginSession(new SessionEpoch(101UL));
            if (!prep.PrepareFullMap(new SessionEpoch(101UL), "MapPEI", 1, meta, 0f)) return false;
            seam.UpdateObserver(1001UL, 201UL, 0, 0, true);
            seam.Tick(0f);
            int first = presented;
            seam.RemoveObserver(1001UL);
            seam.Tick(2f);
            now = 40f;
            ItemCycleThresholds.Override = new ItemCycleThresholds(10f, 60f, 99f, 0f, true);
            try
            {
                seam.UpdateObserver(1001UL, 201UL, 0, 0, true);
                seam.Tick(0f);
                generation = 2U;
                for (int i = 0; i < 4; i++) seam.Tick(0f);
                bool oneLeft = bridge.Regions[(0, 0)].Find(it => it != null && it.instanceID == 1002U) != null
                    || bridge.Regions[(0, 0)].Find(it => it != null && it.instanceID == 1001U) != null;
                bool notBothGone = bridge.Regions[(0, 0)].Count > 0;
                return oneLeft && notBothGone && presented == first && seam.PendingAcquireRetryCount == 0;
            }
            finally { ItemCycleThresholds.Override = null; }
        }

        internal static bool Test_IT48_DropExpiresDuringSettlementWithoutResurrection()
        {
            var bridge = new TestNativeItemWarehouseBridge();
            var prep = new ItemPreparationAuthority(bridge);
            bridge.GenerateNaturalItem(0, 0, CreateTestItemData(1001U, false, 0f));
            var meta = new ItemMetadataStore();
            float now = 10f;
            var port = new ItemExecutionPort(prep, meta, _ => 1U, CreateTestPolicy(), () => now);
            var proj = new DemandProjectionEngine(new ObserverSpatialAuthority());
            var policy = ItemDemandPolicy.Create(radius: 0, worldSize: 1);
            proj.Register(policy);
            var seam = new ItemProductionControlSeam(prep, meta, port, proj, policy, 2.0f);
            seam.BeginSession(new SessionEpoch(101UL));
            if (!prep.PrepareFullMap(new SessionEpoch(101UL), "MapPEI", 1, meta, 0f)) return false;
            seam.UpdateObserver(1001UL, 201UL, 0, 0, true);
            seam.Tick(0f);
            seam.RemoveObserver(1001UL);
            seam.Tick(2f);
            now = 40f;
            bridge.SpawnPointCount = 1;
            ItemCycleThresholds.Override = new ItemCycleThresholds(10f, 10f, 99f, 1f, true);
            try
            {
                seam.UpdateObserver(1001UL, 201UL, 0, 0, true);
                bridge.DropItem(0, 0, CreateTestItemData(2001U, true, 0f));
                bridge.DropItem(0, 0, CreateTestItemData(2002U, true, 40f));
                for (int i = 0; i < 6; i++) seam.Tick(0f);
                var items = bridge.Regions[(0, 0)];
                bool naturalGone = items.Find(it => it != null && it.instanceID == 1001U) == null;
                bool expiredDropGone = items.Find(it => it != null && it.instanceID == 2001U) == null;
                ItemData fresh = items.Find(it => it != null && it.instanceID == 2002U);
                bool ageKept = fresh != null && Math.Abs(fresh.lastDropped - 40f) < 0.001f;
                return naturalGone && expiredDropGone && ageKept && seam.PendingAcquireRetryCount == 0;
            }
            finally { ItemCycleThresholds.Override = null; }
        }

        internal static bool Test_IT49_WrapUpReentryDoesNotRedoAndNextReturnRejudges()
        {
            var bridge = new TestNativeItemWarehouseBridge();
            var prep = new ItemPreparationAuthority(bridge);
            bridge.GenerateNaturalItem(0, 0, CreateTestItemData(1001U, false, 0f));
            bridge.GenerateNaturalItem(0, 0, CreateTestItemData(1002U, false, 0f));
            var meta = new ItemMetadataStore();
            float now = 10f;
            int presented = 0;
            var port = new ItemExecutionPort(prep, meta, _ => 1U, CreateTestPolicy(), () => now, null,
                id => true, region => presented++, (id, token, region) => presented++);
            var proj = new DemandProjectionEngine(new ObserverSpatialAuthority());
            var policy = ItemDemandPolicy.Create(radius: 0, worldSize: 1);
            proj.Register(policy);
            var seam = new ItemProductionControlSeam(prep, meta, port, proj, policy, 2.0f);
            seam.BeginSession(new SessionEpoch(101UL));
            if (!prep.PrepareFullMap(new SessionEpoch(101UL), "MapPEI", 1, meta, 0f)) return false;
            seam.UpdateObserver(1001UL, 201UL, 0, 0, true);
            seam.Tick(0f);
            int first = presented;
            seam.RemoveObserver(1001UL);
            seam.Tick(2f);
            now = 40f;
            ItemCycleThresholds.Override = new ItemCycleThresholds(10f, 60f, 99f, 0f, true);
            try
            {
                seam.UpdateObserver(1001UL, 201UL, 0, 0, true);
                seam.Tick(0f);
                seam.RemoveObserver(1001UL);
                seam.UpdateObserver(1001UL, 201UL, 0, 0, true);
                for (int i = 0; i < 4; i++) seam.Tick(0f);
                bool once = bridge.RemovalCalls == 2 && presented == first + 1;
                seam.RemoveObserver(1001UL);
                seam.Tick(2f);
                bridge.GenerateNaturalItem(0, 0, CreateTestItemData(3001U, false, 0f));
                seam.UpdateObserver(1001UL, 201UL, 0, 0, true);
                seam.Tick(0f);
                seam.Tick(0f);
                bool rejudged = bridge.Regions[(0, 0)].Find(it => it != null && it.instanceID == 3001U) == null;
                return once && rejudged && seam.PendingAcquireRetryCount == 0;
            }
            finally { ItemCycleThresholds.Override = null; }
        }

        internal static bool Test_IT50_StepFivePendingTransfersToCommittedJob()
        {
            var bridge = new TestNativeItemWarehouseBridge();
            var prep = new ItemPreparationAuthority(bridge);
            bridge.GenerateNaturalItem(0, 0, CreateTestItemData(1001U, false, 0f));
            var meta = new ItemMetadataStore();
            float now = 10f;
            int presented = 0;
            var port = new ItemExecutionPort(prep, meta, _ => 1U, CreateTestPolicy(), () => now, null,
                id => id == 1001UL, region => presented++, (id, token, region) => presented++);
            var proj = new DemandProjectionEngine(new ObserverSpatialAuthority());
            var policy = ItemDemandPolicy.Create(radius: 0, worldSize: 1);
            proj.Register(policy);
            var seam = new ItemProductionControlSeam(prep, meta, port, proj, policy, 2.0f);
            seam.BeginSession(new SessionEpoch(101UL));
            if (!prep.PrepareFullMap(new SessionEpoch(101UL), "MapPEI", 1, meta, 0f)) return false;
            seam.UpdateObserver(1001UL, 201UL, 0, 0, true);
            seam.Tick(0f);
            seam.RemoveObserver(1001UL);
            seam.Tick(2f);
            now = 40f;
            ItemCycleThresholds.Override = new ItemCycleThresholds(10f, 60f, 99f, 0f, true);
            try
            {
                seam.RegisterStepFiveObserver(1001UL, 201UL, new RegionKey(0, 0), true);
                bool heldBeforeDemand = ItemSettlementGate.ShouldHoldRegion(0, 0);
                seam.UpdateObserver(1001UL, 201UL, 0, 0, true);
                seam.Tick(0f);
                seam.Tick(0f);
                return !heldBeforeDemand && seam.PendingAcquireRetryCount == 0 && presented == 2;
            }
            finally { ItemCycleThresholds.Override = null; }
        }

        internal static bool Test_IT51_NaturalCreationExceptionIsIsolated()
        {
            var bridge = new TestNativeItemWarehouseBridge { ThrowOnRespawn = true, SpawnPointCount = 1 };
            var prep = new ItemPreparationAuthority(bridge);
            bridge.GenerateNaturalItem(0, 0, CreateTestItemData(1001U, false, 0f));
            var meta = new ItemMetadataStore();
            float now = 10f;
            var port = new ItemExecutionPort(prep, meta, _ => 1U, CreateTestPolicy(), () => now);
            var proj = new DemandProjectionEngine(new ObserverSpatialAuthority());
            var policy = ItemDemandPolicy.Create(radius: 0, worldSize: 1);
            proj.Register(policy);
            var seam = new ItemProductionControlSeam(prep, meta, port, proj, policy, 2.0f);
            seam.BeginSession(new SessionEpoch(101UL));
            if (!prep.PrepareFullMap(new SessionEpoch(101UL), "MapPEI", 1, meta, 0f)) return false;
            seam.UpdateObserver(1001UL, 201UL, 0, 0, true);
            seam.Tick(0f);
            seam.RemoveObserver(1001UL);
            seam.Tick(2f);
            now = 40f;
            ItemCycleThresholds.Override = new ItemCycleThresholds(10f, 60f, 1f, 1f, true);
            try
            {
                seam.UpdateObserver(1001UL, 201UL, 0, 0, true);
                seam.Tick(0f);
                seam.Tick(0f);
                return !string.IsNullOrEmpty(seam.LastSettlementFault)
                    && seam.PendingAcquireRetryCount == 0;
            }
            finally { ItemCycleThresholds.Override = null; }
        }

        internal static bool Test_IT52_CompletedRegionDoesNotHoldNextBaseline()
        {
            var bridge = new TestNativeItemWarehouseBridge();
            var prep = new ItemPreparationAuthority(bridge);
            bridge.GenerateNaturalItem(0, 0, CreateTestItemData(1001U, false, 0f));
            var meta = new ItemMetadataStore();
            float now = 10f;
            int presented = 0;
            var port = new ItemExecutionPort(prep, meta, _ => 1U, CreateTestPolicy(), () => now,
                null, id => id == 1001UL, region => presented++);
            var proj = new DemandProjectionEngine(new ObserverSpatialAuthority());
            var policy = ItemDemandPolicy.Create(radius: 0, worldSize: 1);
            proj.Register(policy);
            var seam = new ItemProductionControlSeam(prep, meta, port, proj, policy, 2.0f);
            seam.BeginSession(new SessionEpoch(101UL));
            if (!prep.PrepareFullMap(new SessionEpoch(101UL), "MapPEI", 1, meta, 0f)) return false;
            seam.UpdateObserver(1001UL, 201UL, 0, 0, true);
            seam.Tick(0f);
            seam.RemoveObserver(1001UL);
            seam.Tick(2f);
            now = 40f;
            ItemCycleThresholds.Override = new ItemCycleThresholds(10f, 60f, 99f, 0f, true);
            try
            {
                seam.RegisterStepFiveObserver(1001UL, 201UL, new RegionKey(0, 0), true);
                seam.UpdateObserver(1001UL, 201UL, 0, 0, true);
                seam.Tick(0f);
                seam.Tick(0f);
                var nextObserverLoaded = new HashSet<RegionKey>();
                ItemSettlementGate.StepFiveAdmission.Admit(
                    new ItemStepFiveRequest(1002UL, 202UL, new RegionKey(0, 0), 0, 1, false),
                    new ItemStepFiveLoadedProjection(region => nextObserverLoaded.Add(region)));
                return presented == 2 && ItemSettlementGate.WasDelivered(0, 0)
                    && !ItemSettlementGate.ShouldHoldRegion(0, 0)
                    && !nextObserverLoaded.Contains(new RegionKey(0, 0));
            }
            finally { ItemCycleThresholds.Override = null; }
        }

        internal static bool Test_IT53_StepFiveAdmissionUsesDeliveredSuppression()
        {
            var loaded = new HashSet<RegionKey>();
            int demandCalls = 0;
            int observerCalls = 0;
            RegionKey center = new RegionKey(4, 5);
            var admission = new ItemStepFiveAdmission(
                request => demandCalls++,
                (request, region) => observerCalls++,
                (request, region) => false,
                (request, region) => region == center);
            var request = new ItemStepFiveRequest(1001UL, 201UL, center, 0, 64, true);
            admission.Admit(request, new ItemStepFiveLoadedProjection(region => loaded.Add(region)));
            return demandCalls == 1 && observerCalls == 1 && loaded.Contains(center);
        }

        internal static bool Test_IT54_StepFiveAdmissionKeepsNeighborIndependent()
        {
            var loaded = new HashSet<RegionKey>();
            RegionKey center = new RegionKey(4, 5);
            var admission = new ItemStepFiveAdmission(
                request => { },
                (request, region) => { },
                (request, region) => region == center,
                (request, region) => false);
            admission.Admit(new ItemStepFiveRequest(1001UL, 201UL, center, 1, 64, true),
                new ItemStepFiveLoadedProjection(region => loaded.Add(region)));
            return loaded.Count == 1 && loaded.Contains(center)
                && !loaded.Contains(new RegionKey(3, 5))
                && !loaded.Contains(new RegionKey(4, 6));
        }

        internal static bool Test_IT55_StepFiveAdmissionPreparesSettlementBeforeDemand()
        {
            var order = new List<string>();
            var admission = new ItemStepFiveAdmission(
                request => order.Add("demand"),
                (request, region) => { },
                (request, region) => false,
                (request, region) => false,
                request => order.Add("refresh"));
            admission.Admit(new ItemStepFiveRequest(1001UL, 201UL, new RegionKey(0, 0), 0, 1, true),
                new ItemStepFiveLoadedProjection(region => { }));
            return order.Count == 2 && order[0] == "refresh" && order[1] == "demand";
        }

        internal static bool Test_IT56_MissingInputsDoNotFinishSettlement()
        {
            var bridge = new TestNativeItemWarehouseBridge
            {
                SpawnPointCount = 1,
                MissingNaturalInputs = true
            };
            var prep = new ItemPreparationAuthority(bridge);
            bridge.GenerateNaturalItem(0, 0, CreateTestItemData(1001U, false, 0f));
            var meta = new ItemMetadataStore();
            float now = 10f;
            int presented = 0;
            var port = new ItemExecutionPort(prep, meta, _ => 1U, CreateTestPolicy(), () => now,
                null, id => id == 1001UL, region => presented++);
            var proj = new DemandProjectionEngine(new ObserverSpatialAuthority());
            var policy = ItemDemandPolicy.Create(radius: 0, worldSize: 1);
            proj.Register(policy);
            var seam = new ItemProductionControlSeam(prep, meta, port, proj, policy, 2.0f);
            seam.BeginSession(new SessionEpoch(101UL));
            if (!prep.PrepareFullMap(new SessionEpoch(101UL), "MapPEI", 1, meta, 0f)) return false;
            seam.UpdateObserver(1001UL, 201UL, 0, 0, true);
            seam.Tick(0f);
            seam.RemoveObserver(1001UL);
            seam.Tick(2f);
            now = 40f;
            ItemCycleThresholds.Override = new ItemCycleThresholds(10f, 60f, 1f, 1f, true);
            try
            {
                seam.UpdateObserver(1001UL, 201UL, 0, 0, true);
                seam.Tick(0f);
                seam.Tick(0f);
                return seam.IsRegionIsolated(new RegionKey(0, 0)) && presented == 1;
            }
            finally { ItemCycleThresholds.Override = null; }
        }

        internal static bool Test_IT59_MissingInputsDoNotFinishSettlement()
        {
            var bridge = new TestNativeItemWarehouseBridge { MissingNaturalInputs = true };
            var prep = new ItemPreparationAuthority(bridge);
            bridge.GenerateNaturalItem(0, 0, CreateTestItemData(1001U, false, 0f));
            var meta = new ItemMetadataStore();
            float now = 10f;
            int presented = 0;
            var port = new ItemExecutionPort(prep, meta, _ => 1U, CreateTestPolicy(), () => now,
                null, id => id == 1001UL, region => presented++);
            var proj = new DemandProjectionEngine(new ObserverSpatialAuthority());
            var policy = ItemDemandPolicy.Create(radius: 0, worldSize: 1);
            proj.Register(policy);
            var seam = new ItemProductionControlSeam(prep, meta, port, proj, policy, 2.0f);
            seam.BeginSession(new SessionEpoch(101UL));
            if (!prep.PrepareFullMap(new SessionEpoch(101UL), "MapPEI", 1, meta, 0f)) return false;
            seam.UpdateObserver(1001UL, 201UL, 0, 0, true);
            seam.Tick(0f);
            seam.RemoveObserver(1001UL);
            seam.Tick(2f);
            now = 40f;
            ItemCycleThresholds.Override = new ItemCycleThresholds(10f, 60f, 1f, 1f, true);
            try
            {
                seam.UpdateObserver(1001UL, 201UL, 0, 0, true);
                seam.Tick(0f);
                seam.Tick(0f);
                return seam.IsRegionIsolated(new RegionKey(0, 0)) && presented == 1;
            }
            finally { ItemCycleThresholds.Override = null; }
        }

        internal static bool Test_IT133_ProductionNaturalInputsDistinguishEmptyFromMissing()
        {
            int size = 2;
            var spawnPoints = new List<ItemSpawnpoint>[size, size];
            spawnPoints[0, 0] = new List<ItemSpawnpoint>();
            var regions = new ItemRegion[size, size];
            regions[0, 0] = (ItemRegion)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(ItemRegion));
            regions[0, 0].items = new List<ItemData>();
            bool emptyAvailable = ItemPreparationAuthority.TryGetNaturalInputs(
                spawnPoints, regions, new RegionKey(0, 0), out List<ItemSpawnpoint> empty, out string emptyReason);
            spawnPoints[0, 0] = null;
            bool missingSpawnRejected = !ItemPreparationAuthority.TryGetNaturalInputs(
                spawnPoints, regions, new RegionKey(0, 0), out _, out string missingSpawnReason);
            spawnPoints[0, 0] = new List<ItemSpawnpoint>();
            regions[0, 0] = null;
            bool missingRegionRejected = !ItemPreparationAuthority.TryGetNaturalInputs(
                spawnPoints, regions, new RegionKey(0, 0), out _, out string missingRegionReason);
            return emptyAvailable && empty != null && empty.Count == 0 && string.IsNullOrEmpty(emptyReason)
                && missingSpawnRejected && missingSpawnReason == "spawn-input-unavailable"
                && missingRegionRejected && missingRegionReason == "region-input-unavailable";
        }

        internal static bool Test_IT136_UnconfirmedNoticeDeduplicatesPerObserverRegion()
        {
            var bridge = new TestNativeItemWarehouseBridge();
            var prep = new ItemPreparationAuthority(bridge);
            var meta = new ItemMetadataStore();
            var port = new ItemExecutionPort(prep, meta, _ => 1U, ItemLifecyclePolicy.Create());
            var projection = new DemandProjectionEngine(new ObserverSpatialAuthority());
            var policy = ItemDemandPolicy.Create(radius: 0, worldSize: 2);
            projection.Register(policy);
            var seam = new ItemProductionControlSeam(prep, meta, port, projection, policy, 2.0f);
            int notices = 0;
            seam.RecoveryUnconfirmed = (_, __, ___) => notices++;
            seam.BeginSession(new SessionEpoch(101UL));
            seam.UpdateObserver(1001UL, 201UL, 0, 0, true);
            seam.UpdateObserver(1002UL, 202UL, 0, 0, true);
            var mark = ItemSettlementGate.MarkUnconfirmedSideEffect;
            mark(1001UL, 201UL, 0, 0, 4001U);
            mark(1001UL, 201UL, 0, 0, 4001U);
            mark(1002UL, 202UL, 0, 0, 4001U);
            seam.UpdateObserver(1001UL, 201UL, 1, 1, true);
            mark(1001UL, 201UL, 0, 0, 4001U);
            return notices == 3 && ItemSettlementGate.UnconfirmedSideEffectCount?.Invoke() == 1;
        }

        internal static bool Test_IT135_UncertainReplicationFailureNotifiesOnceWithTrace()
        {
            var bridge = new TestNativeItemWarehouseBridge();
            var prep = new ItemPreparationAuthority(bridge);
            var meta = new ItemMetadataStore();
            var diagnostics = new List<string>();
            int notices = 0;
            var port = new ItemExecutionPort(prep, meta, _ => 1U, ItemLifecyclePolicy.Create(),
                diagnosticSink: detail => diagnostics.Add(detail));
            var projection = new DemandProjectionEngine(new ObserverSpatialAuthority());
            var policy = ItemDemandPolicy.Create(radius: 0, worldSize: 1);
            projection.Register(policy);
            var seam = new ItemProductionControlSeam(prep, meta, port, projection, policy, 2.0f);
            seam.RecoveryExhausted = (_, __, ___, half) => { if (half) notices++; };
            seam.BeginSession(new SessionEpoch(101UL));
            Exception cause;
            try { throw new InvalidOperationException("native askItems failed"); }
            catch (Exception ex) { cause = ex; }
            var failure = new ItemObserverReplicationFailureException("send result unknown", false, cause);
            var region = new RegionKey(0, 0);
            var first = port.HandleObserverReplicationFailure(1001UL, 201UL, region, failure);
            var duplicate = port.HandleObserverReplicationFailure(1001UL, 201UL, region, failure);
            return first == EObserverReplicationFailureDisposition.Quarantine
                && duplicate == EObserverReplicationFailureDisposition.Quarantine
                && notices == 1 && diagnostics.Count == 2
                && diagnostics[0].Contains("observer=1001") && diagnostics[0].Contains("connection=201")
                && diagnostics[0].Contains("attempt=1") && diagnostics[0].Contains("budget=3/10s")
                && diagnostics[0].Contains("native askItems failed") && diagnostics[0].Contains("stack=")
                && diagnostics[1].Contains("type=possible-half-packet")
                && diagnostics[1].Contains("target=1001") && diagnostics[1].Contains("region=(0,0)");
        }

        internal static bool Test_IT134_ZeroSpawnPointRegionSettlesAndDelivers()
        {
            var bridge = new TestNativeItemWarehouseBridge { SpawnPointCount = 0 };
            var prep = new ItemPreparationAuthority(bridge);
            bridge.GenerateNaturalItem(0, 0, CreateTestItemData(1001U, false, 0f));
            bridge.GenerateNaturalItem(0, 0, CreateTestItemData(1002U, true, 0f));
            var meta = new ItemMetadataStore();
            float now = 10f;
            int guestBaselines = 0;
            int notices = 0;
            var port = new ItemExecutionPort(prep, meta, _ => 1U, ItemLifecyclePolicy.Create(),
                () => now, guestBaselineBinder: (_, __, ___) => guestBaselines++);
            var projection = new DemandProjectionEngine(new ObserverSpatialAuthority());
            var policy = ItemDemandPolicy.Create(radius: 0, worldSize: 1);
            projection.Register(policy);
            var seam = new ItemProductionControlSeam(prep, meta, port, projection, policy, 2.0f);
            seam.RecoveryExhausted = (_, __, ___, ____) => notices++;
            seam.BeginSession(new SessionEpoch(101UL));
            if (!prep.PrepareFullMap(new SessionEpoch(101UL), "MapPEI", 1, meta, 0f)) return false;
            seam.UpdateObserver(1001UL, 201UL, 0, 0, true);
            seam.Tick(0f);
            seam.RemoveObserver(1001UL);
            seam.Tick(2f);
            now = 40f;
            ItemCycleThresholds.Override = new ItemCycleThresholds(10f, 60f, 1f, 1f, true);
            try
            {
                seam.UpdateObserver(1001UL, 201UL, 0, 0, true);
                seam.Tick(0f);
                seam.Tick(0f);
                bool naturalExpired = !bridge.Regions[(0, 0)].Exists(item => item.instanceID == 1001U);
                bool droppedKept = bridge.Regions[(0, 0)].Exists(item => item.instanceID == 1002U);
                return guestBaselines == 2 && notices == 0 && naturalExpired && droppedKept
                    && !seam.IsRegionIsolated(new RegionKey(0, 0));
            }
            finally { ItemCycleThresholds.Override = null; }
        }

        internal static bool Test_IT137_HalfPacketExitReturnDeliversFreshBaseline()
        {
            var bridge = new TestNativeItemWarehouseBridge();
            var prep = new ItemPreparationAuthority(bridge);
            var meta = new ItemMetadataStore();
            var diagnostics = new List<string>();
            int attempts = 0;
            int notices = 0;
            var delivered = new List<RegionKey>();
            var port = new ItemExecutionPort(prep, meta, _ => 1U, ItemLifecyclePolicy.Create(),
                diagnosticSink: detail => diagnostics.Add(detail),
                guestBaselineBinder: (observer, token, region) =>
                {
                    attempts++;
                    if (attempts == 1)
                        throw new ItemObserverReplicationFailureException(
                            "send result unknown", false, new InvalidOperationException("partial native send"));
                    delivered.Add(region);
                });
            var projection = new DemandProjectionEngine(new ObserverSpatialAuthority());
            var policy = ItemDemandPolicy.Create(radius: 0, worldSize: 2);
            projection.Register(policy);
            var seam = new ItemProductionControlSeam(prep, meta, port, projection, policy, 2.0f);
            seam.RecoveryExhausted = (_, __, ___, half) => { if (half) notices++; };
            ItemCycleThresholds.Override = new ItemCycleThresholds(10f, 60f, 1f, 1f, true);
            try
            {
                seam.BeginSession(new SessionEpoch(101UL));
                if (!prep.PrepareFullMap(new SessionEpoch(101UL), "MapPEI", 2, meta, 0f)) return false;
                seam.UpdateObserver(1001UL, 201UL, 0, 0, true);
                seam.UpdateObserver(1001UL, 201UL, 0, 0, true);
                seam.UpdateObserver(1001UL, 201UL, 1, 1, true);
                seam.Tick(2.1f);
                seam.UpdateObserver(1001UL, 201UL, 0, 0, true);
                for (int step = 0; step < 16 && attempts < 3; step++) seam.Tick(0.25f);
                string trace = string.Join("\n", diagnostics);
                bool regionUnblocked = !(ItemSettlementGate.BlocksRegionForObserver?.Invoke(1001UL, 201UL, 0, 0) ?? true);
                return attempts == 3 && notices == 1 && delivered.Count == 2
                    && delivered[0].Equals(new RegionKey(1, 1))
                    && delivered[1].Equals(new RegionKey(0, 0))
                    && regionUnblocked
                    && trace.Contains("event=first-observer-failure")
                    && trace.Contains("type=possible-half-packet");
            }
            finally { ItemCycleThresholds.Override = null; }
        }

        internal static bool Test_IT138_SettlementConfirmedNotSentRetriesOnlyGuestDelivery()
        {
            var bridge = new TestNativeItemWarehouseBridge();
            var prep = new ItemPreparationAuthority(bridge);
            var meta = new ItemMetadataStore();
            var diagnostics = new List<string>();
            float now = 0f;
            int firstGuestCalls = 0;
            int secondGuestCalls = 0;
            var delivered = new List<string>();
            int halfNotices = 0;
            bool delayHonored = false;
            bool targetGateObserved = false, peerGateOpen = false;
            var port = new ItemExecutionPort(prep, meta, _ => 1U, ItemLifecyclePolicy.Create(),
                () => now,
                diagnosticSink: detail => diagnostics.Add(detail),
                guestBaselineBinder: (observer, token, region) =>
                {
                    bool targetRegion = region.Equals(new RegionKey(0, 0));
                    if (targetRegion && observer == 1001UL && ++firstGuestCalls == 2)
                        ItemObserverReplicationAdapter.DispatchCandidateGuestAskItems(
                            () => ItemObserverReplicationAdapter.NoteAskItemsOriginalRan(false), null);
                    if (targetRegion && observer == 1002UL) secondGuestCalls++;
                    ItemObserverReplicationAdapter.DispatchCandidateGuestAskItems(
                        () => ItemObserverReplicationAdapter.NoteAskItemsOriginalRan(true), () => { });
                    delivered.Add(observer + "/" + region);
                });
            var projection = new DemandProjectionEngine(new ObserverSpatialAuthority());
            var policy = ItemDemandPolicy.Create(radius: 0, worldSize: 2);
            projection.Register(policy);
            var seam = new ItemProductionControlSeam(prep, meta, port, projection, policy, 2f);
            seam.RecoveryExhausted = (_, __, ___, half) => { if (half) halfNotices++; };
            ItemCycleThresholds.Override = new ItemCycleThresholds(10f, 60f, 1f, 1f, true);
            try
            {
                seam.BeginSession(new SessionEpoch(101UL));
                if (!prep.PrepareFullMap(new SessionEpoch(101UL), "MapPEI", 2, meta, 0f)) return false;
                seam.UpdateObserver(1001UL, 201UL, 0, 0, true);
                seam.UpdateObserver(1002UL, 202UL, 0, 0, true);
                seam.UpdateObserver(1001UL, 201UL, 1, 1, true);
                seam.UpdateObserver(1002UL, 202UL, 1, 1, true);
                now += 2.1f;
                seam.Tick(2.1f);
                seam.UpdateObserver(1001UL, 201UL, 0, 0, true);
                seam.UpdateObserver(1002UL, 202UL, 0, 0, true);
                for (int step = 0; step < 40 && firstGuestCalls < 3; step++)
                {
                    now += 0.25f;
                    seam.Tick(0.25f);
                    if (!delayHonored && firstGuestCalls == 2
                        && string.Join("\n", diagnostics).Contains("event=delivery-confirmed-not-sent"))
                    {
                        targetGateObserved = ItemSettlementGate.BlocksRegionForObserver?.Invoke(1001UL, 201UL, 0, 0) ?? false;
                        peerGateOpen = !(ItemSettlementGate.BlocksRegionForObserver?.Invoke(1002UL, 202UL, 0, 0) ?? true);
                        ItemCycleThresholds.Override = new ItemCycleThresholds(10f, 60f, 1f, 1f, false);
                        now += 0.25f;
                        seam.Tick(0.25f);
                        delayHonored = firstGuestCalls == 2;
                    }
                }
                bool passed = delayHonored && targetGateObserved && peerGateOpen
                    && firstGuestCalls == 3 && secondGuestCalls == 2 && halfNotices == 0
                    && delivered.Contains("1001/(0,0)") && delivered.Contains("1002/(0,0)")
                    && !seam.IsRegionIsolated(new RegionKey(0, 0));
                if (!passed) Console.WriteLine($"IT138 first={firstGuestCalls} second={secondGuestCalls} halfNotices={halfNotices} delivered={string.Join(";", delivered)} isolated={seam.IsRegionIsolated(new RegionKey(0, 0))} diagnostics={string.Join("|", diagnostics)}");
                return passed;
            }
            finally { ItemCycleThresholds.Override = null; }
        }

        internal static bool Test_IT139_ConfirmedNotSentExhaustionAndManualRestart()
        {
            var bridge = new TestNativeItemWarehouseBridge();
            var prep = new ItemPreparationAuthority(bridge);
            var meta = new ItemMetadataStore();
            float now = 0f;
            int firstCalls = 0, secondCalls = 0, safeNotices = 0;
            var firstCallTimes = new List<float>();
            bool allowManualSuccess = false;
            var port = new ItemExecutionPort(prep, meta, _ => 1U, ItemLifecyclePolicy.Create(),
                () => now, guestBaselineBinder: (observer, token, region) =>
                {
                    if (!region.Equals(new RegionKey(0, 0))) return;
                    if (observer == 1001UL)
                    {
                        firstCalls++;
                        firstCallTimes.Add(now);
                        if (firstCalls > 1 && !allowManualSuccess)
                            ItemObserverReplicationAdapter.DispatchCandidateGuestAskItems(
                                () => ItemObserverReplicationAdapter.NoteAskItemsOriginalRan(false), null);
                    }
                    if (observer == 1002UL) secondCalls++;
                    ItemObserverReplicationAdapter.DispatchCandidateGuestAskItems(
                        () => ItemObserverReplicationAdapter.NoteAskItemsOriginalRan(true), () => { });
                });
            var projection = new DemandProjectionEngine(new ObserverSpatialAuthority());
            var policy = ItemDemandPolicy.Create(radius: 0, worldSize: 2);
            projection.Register(policy);
            var seam = new ItemProductionControlSeam(prep, meta, port, projection, policy, 2f);
            seam.RecoveryExhausted = (observer, _, __, half) =>
                { if (observer == 1001UL && !half) safeNotices++; };
            ItemCycleThresholds.Override = new ItemCycleThresholds(10f, 60f, 1f, 1f, true);
            try
            {
                seam.BeginSession(new SessionEpoch(101UL));
                if (!prep.PrepareFullMap(new SessionEpoch(101UL), "MapPEI", 2, meta, 0f)) return false;
                seam.UpdateObserver(1001UL, 201UL, 0, 0, true);
                seam.UpdateObserver(1002UL, 202UL, 0, 0, true);
                seam.UpdateObserver(1001UL, 201UL, 1, 1, true);
                seam.UpdateObserver(1002UL, 202UL, 1, 1, true);
                now += 2.1f; seam.Tick(2.1f);
                seam.UpdateObserver(1001UL, 201UL, 0, 0, true);
                seam.UpdateObserver(1002UL, 202UL, 0, 0, true);
                for (int i = 0; i < 80 && firstCalls < 5; i++) { now += 0.25f; seam.Tick(0.25f); }
                for (int i = 0; i < 8; i++) { now += 0.25f; seam.Tick(0.25f); }
                if (firstCalls != 5 || secondCalls != 2 || safeNotices != 1
                    || firstCallTimes[2] - firstCallTimes[1] < 0.49f
                    || firstCallTimes[3] - firstCallTimes[2] < 0.99f
                    || firstCallTimes[4] - firstCallTimes[3] < 1.99f) return false;
                allowManualSuccess = true;
                if (!seam.TryManualRecovery(1001UL, 201UL, new RegionKey(0, 0))) return false;
                if (seam.TryManualRecovery(1001UL, 201UL, new RegionKey(0, 0))) return false;
                seam.Tick(0f);
                return firstCalls == 6 && secondCalls == 2 && safeNotices == 1;
            }
            finally { ItemCycleThresholds.Override = null; }
        }

        internal static bool Test_IT140_ConfirmedNotSentRetryDeadlineStopsDelivery()
        {
            var bridge = new TestNativeItemWarehouseBridge();
            var prep = new ItemPreparationAuthority(bridge);
            var meta = new ItemMetadataStore();
            float now = 0f, firstFaultAt = -1f;
            int firstCalls = 0, safeNotices = 0;
            var port = new ItemExecutionPort(prep, meta, _ => 1U, ItemLifecyclePolicy.Create(),
                () => now, guestBaselineBinder: (observer, token, region) =>
                {
                    if (!region.Equals(new RegionKey(0, 0)) || observer != 1001UL) return;
                    firstCalls++;
                    if (firstCalls == 2) firstFaultAt = now;
                    if (firstCalls > 1)
                        ItemObserverReplicationAdapter.DispatchCandidateGuestAskItems(
                            () => ItemObserverReplicationAdapter.NoteAskItemsOriginalRan(false), null);
                    ItemObserverReplicationAdapter.DispatchCandidateGuestAskItems(
                        () => ItemObserverReplicationAdapter.NoteAskItemsOriginalRan(true), () => { });
                });
            var projection = new DemandProjectionEngine(new ObserverSpatialAuthority());
            var policy = ItemDemandPolicy.Create(radius: 0, worldSize: 2);
            projection.Register(policy);
            var seam = new ItemProductionControlSeam(prep, meta, port, projection, policy, 2f);
            seam.RecoveryExhausted = (observer, _, __, half) =>
                { if (observer == 1001UL && !half) safeNotices++; };
            ItemCycleThresholds.Override = new ItemCycleThresholds(10f, 60f, 1f, 1f, true);
            try
            {
                seam.BeginSession(new SessionEpoch(101UL));
                if (!prep.PrepareFullMap(new SessionEpoch(101UL), "MapPEI", 2, meta, 0f)) return false;
                seam.UpdateObserver(1001UL, 201UL, 0, 0, true);
                seam.UpdateObserver(1001UL, 201UL, 1, 1, true);
                ItemCycleThresholds.Override = new ItemCycleThresholds(10f, 60f, 1f, 1f, false);
                now += 20f; seam.Tick(20f);
                seam.UpdateObserver(1001UL, 201UL, 0, 0, true);
                now += 20f; seam.Tick(20f);
                ItemCycleThresholds.Override = new ItemCycleThresholds(10f, 60f, 1f, 1f, true);
                for (int i = 0; i < 40 && firstCalls < 4; i++) { now += 0.25f; seam.Tick(0.25f); }
                if (firstCalls != 4 || firstFaultAt < 40f) return false;
                now = firstFaultAt + 10f;
                seam.Tick(0f);
                for (int i = 0; i < 4; i++) seam.Tick(0f);
                return firstCalls == 4 && safeNotices == 1;
            }
            finally { ItemCycleThresholds.Override = null; }
        }

        internal static bool Test_IT141_UnknownDuringDeliveryRetryQuarantinesOnce()
        {
            var bridge = new TestNativeItemWarehouseBridge();
            var prep = new ItemPreparationAuthority(bridge);
            var meta = new ItemMetadataStore();
            float now = 0f;
            int calls = 0, halfNotices = 0, safeNotices = 0;
            var port = new ItemExecutionPort(prep, meta, _ => 1U, ItemLifecyclePolicy.Create(),
                () => now, guestBaselineBinder: (observer, token, region) =>
                {
                    if (!region.Equals(new RegionKey(0, 0))) return;
                    calls++;
                    if (calls == 2)
                        ItemObserverReplicationAdapter.DispatchCandidateGuestAskItems(
                            () => ItemObserverReplicationAdapter.NoteAskItemsOriginalRan(false), null);
                    if (calls == 3)
                        ItemObserverReplicationAdapter.DispatchCandidateGuestAskItems(
                            () => throw new InvalidOperationException("native askItems result unknown"), null);
                    ItemObserverReplicationAdapter.DispatchCandidateGuestAskItems(
                        () => ItemObserverReplicationAdapter.NoteAskItemsOriginalRan(true), () => { });
                });
            var projection = new DemandProjectionEngine(new ObserverSpatialAuthority());
            var policy = ItemDemandPolicy.Create(radius: 0, worldSize: 2);
            projection.Register(policy);
            var seam = new ItemProductionControlSeam(prep, meta, port, projection, policy, 2f);
            seam.RecoveryExhausted = (_, __, ___, half) => { if (half) halfNotices++; else safeNotices++; };
            ItemCycleThresholds.Override = new ItemCycleThresholds(10f, 60f, 1f, 1f, true);
            try
            {
                seam.BeginSession(new SessionEpoch(101UL));
                if (!prep.PrepareFullMap(new SessionEpoch(101UL), "MapPEI", 2, meta, 0f)) return false;
                seam.UpdateObserver(1001UL, 201UL, 0, 0, true);
                seam.UpdateObserver(1001UL, 201UL, 1, 1, true);
                now += 2.1f; seam.Tick(2.1f);
                seam.UpdateObserver(1001UL, 201UL, 0, 0, true);
                for (int i = 0; i < 20 && calls < 3; i++) { now += 0.25f; seam.Tick(0.25f); }
                for (int i = 0; i < 20; i++) { now += 0.25f; seam.Tick(0.25f); }
                bool blocked = ItemSettlementGate.BlocksRegionForObserver?.Invoke(1001UL, 201UL, 0, 0) ?? false;
                return calls == 3 && halfNotices == 1 && safeNotices == 0 && blocked;
            }
            finally { ItemCycleThresholds.Override = null; }
        }

        internal static bool Test_IT142_FailureTraceReopensOnlyAfterRealObserverExit()
        {
            var bridge = new TestNativeItemWarehouseBridge();
            var prep = new ItemPreparationAuthority(bridge);
            var meta = new ItemMetadataStore();
            var diagnostics = new List<string>();
            var port = new ItemExecutionPort(prep, meta, _ => 1U, ItemLifecyclePolicy.Create(),
                diagnosticSink: detail => diagnostics.Add(detail));
            var projection = new DemandProjectionEngine(new ObserverSpatialAuthority());
            var policy = ItemDemandPolicy.Create(radius: 0, worldSize: 2);
            projection.Register(policy);
            var seam = new ItemProductionControlSeam(prep, meta, port, projection, policy, 2f);
            seam.BeginSession(new SessionEpoch(101UL));
            if (!prep.PrepareFullMap(new SessionEpoch(101UL), "MapPEI", 2, meta, 0f)) return false;
            seam.UpdateObserver(1001UL, 201UL, 0, 0, true);
            seam.UpdateObserver(1002UL, 202UL, 0, 0, true);
            Exception failure = null;
            try { ItemObserverReplicationAdapter.DispatchCandidateGuestAskItems(
                () => ItemObserverReplicationAdapter.NoteAskItemsOriginalRan(false), null); }
            catch (Exception ex) { failure = ex; }
            if (failure == null) return false;
            var region = new RegionKey(0, 0);
            port.HandleObserverReplicationFailure(1001UL, 201UL, region, failure);
            port.HandleObserverReplicationFailure(1002UL, 202UL, region, failure);
            port.OnObserverReplicationExited(1001UL, 201UL, region);
            port.HandleObserverReplicationFailure(1001UL, 201UL, region, failure);
            int firstTraces = diagnostics.FindAll(line => line.Contains("event=first-observer-failure")).Count;
            seam.UpdateObserver(1001UL, 201UL, 1, 1, true);
            seam.UpdateObserver(1001UL, 201UL, 0, 0, true);
            port.HandleObserverReplicationFailure(1001UL, 201UL, region, failure);
            port.HandleObserverReplicationFailure(1002UL, 202UL, region, failure);
            int reopenedTraces = diagnostics.FindAll(line => line.Contains("event=first-observer-failure")).Count;
            port.DeliverSettlementBaseline(1001UL, 201UL, region, false);
            port.HandleObserverReplicationFailure(1001UL, 201UL, region, failure);
            int afterSuccessTraces = diagnostics.FindAll(line => line.Contains("event=first-observer-failure")).Count;
            return firstTraces == 2 && reopenedTraces == 3 && afterSuccessTraces == 4;
        }

        internal static bool Test_IT144_ConnectionFailureCleanupUsesExactIdentity()
        {
            var prep = new ItemPreparationAuthority(new TestNativeItemWarehouseBridge());
            var meta = new ItemMetadataStore();
            var diagnostics = new List<string>();
            var port = new ItemExecutionPort(prep, meta, _ => 1U, ItemLifecyclePolicy.Create(),
                diagnosticSink: detail => diagnostics.Add(detail));
            var projection = new DemandProjectionEngine(new ObserverSpatialAuthority());
            var policy = ItemDemandPolicy.Create(0, 2);
            projection.Register(policy);
            var seam = new ItemProductionControlSeam(prep, meta, port, projection, policy, 2f);
            Exception safe = null, unknown = null;
            try { ItemObserverReplicationAdapter.DispatchCandidateGuestAskItems(
                () => ItemObserverReplicationAdapter.NoteAskItemsOriginalRan(false), null); }
            catch (Exception ex) { safe = ex; }
            try { ItemObserverReplicationAdapter.DispatchCandidateGuestAskItems(
                () => throw new InvalidOperationException("askItems outcome unknown"), null); }
            catch (Exception ex) { unknown = ex; }
            var targetA = new RegionKey(0, 0);
            var targetB = new RegionKey(0, 1);
            var collision = new RegionKey(1, 5);
            port.HandleObserverReplicationFailure(5UL, 7UL, targetA, safe);
            port.HandleObserverReplicationFailure(7UL, 7UL, collision, safe);
            port.HandleObserverReplicationFailure(5UL, 7UL, targetB, unknown);
            port.OnObserverDisconnected(5UL, 7UL);
            port.HandleObserverReplicationFailure(5UL, 7UL, targetA, safe);
            port.HandleObserverReplicationFailure(7UL, 7UL, collision, safe);
            port.HandleObserverReplicationFailure(5UL, 7UL, targetB, unknown);
            int firstTraces = diagnostics.FindAll(line => line.Contains("event=first-observer-failure")).Count;
            if (firstTraces != 5) Console.WriteLine($"IT144 expected 5 first traces after exact disconnect cleanup, actual={firstTraces}");
            return firstTraces == 5;
        }

        internal static bool Test_IT145_ObserverExitFailureReportsIdentityAndStack()
        {
            var diagnostics = new List<string>();
            int unbindCalls = 0;
            bool propagated = false;
            var prep = new ItemPreparationAuthority(new TestNativeItemWarehouseBridge { SpawnPointCount = 1 });
            var meta = new ItemMetadataStore();
            var port = new ItemExecutionPort(prep, meta, _ => 1U, ItemLifecyclePolicy.Create(),
                diagnosticSink: detail => diagnostics.Add(detail), isHostObserver: _ => false,
                guestBaselineUnbinder: (observer, token, region) =>
                {
                    unbindCalls++;
                    throw new NullReferenceException("simulated destroyed guest projection");
                });
            var projection = new DemandProjectionEngine(new ObserverSpatialAuthority());
            var policy = ItemDemandPolicy.Create(0, 2);
            projection.Register(policy);
            var seam = new ItemProductionControlSeam(prep, meta, port, projection, policy, 2f);
            seam.BeginSession(new SessionEpoch(145UL));
            if (!prep.PrepareFullMap(new SessionEpoch(145UL), "MapPEI", 2, meta, 0f)) return false;
            seam.UpdateObserver(1001UL, 201UL, 0, 0, true);
            seam.Tick(0f);
            try { seam.RemoveObserver(1001UL); }
            catch (NullReferenceException) { propagated = true; }
            string trace = string.Join("\n", diagnostics);
            bool reported = trace.Contains("event=observer-exit-failed")
                && trace.Contains("action=guest-baseline-unbind")
                && trace.Contains("observer=1001") && trace.Contains("connection=201")
                && trace.Contains("region=(0,0)") && trace.Contains("NullReferenceException")
                && trace.Contains("ItemExecutionPort.OnObserverReplicationExited")
                && trace.Contains("simulated destroyed guest projection");
            if (unbindCalls == 0 || !reported)
                Console.WriteLine($"IT145 exit trace expected unbind/identity/stack; calls={unbindCalls} reported={reported} trace={trace}");
            return propagated && unbindCalls > 0 && reported;
        }

        internal static bool Test_IT148_StaleExitPreservesCurrentAndOtherGuestDemand()
        {
            var prep = new ItemPreparationAuthority(new TestNativeItemWarehouseBridge { SpawnPointCount = 1 });
            var meta = new ItemMetadataStore();
            var port = new ItemExecutionPort(prep, meta, _ => 1U, ItemLifecyclePolicy.Create());
            var projection = new DemandProjectionEngine(new ObserverSpatialAuthority());
            var policy = ItemDemandPolicy.Create(0, 4);
            projection.Register(policy);
            var seam = new ItemProductionControlSeam(prep, meta, port, projection, policy, 2f);
            seam.BeginSession(new SessionEpoch(148UL));
            if (!prep.PrepareFullMap(new SessionEpoch(148UL), "MapPEI", 4, meta, 0f)) return false;
            seam.UpdateObserver(1001UL, 201UL, 0, 0, true);
            seam.UpdateObserver(1002UL, 202UL, 0, 0, true);
            seam.Tick(0f);
            seam.UpdateObserver(1001UL, 203UL, 3, 3, true);
            seam.Tick(0f);
            bool staleRemoved = seam.RemoveObserver(1001UL, 201UL);
            bool preserved = seam.TryGetConnectionGeneration(1001UL, out ulong current) && current == 203UL
                && seam.GetDemand(new RegionKey(0, 0)) == 1 && seam.GetDemand(new RegionKey(3, 3)) == 1;
            bool currentRemoved = seam.RemoveObserver(1001UL, 203UL);
            return !staleRemoved && preserved && currentRemoved
                && !seam.TryGetConnectionGeneration(1001UL, out _)
                && seam.GetDemand(new RegionKey(0, 0)) == 1 && seam.GetDemand(new RegionKey(3, 3)) == 0;
        }

        internal static bool Test_IT143_ExitedDeliveryIsRevokedWithoutAffectingOtherGuest()
        {
            var bridge = new TestNativeItemWarehouseBridge();
            var prep = new ItemPreparationAuthority(bridge);
            var meta = new ItemMetadataStore();
            float now = 0f;
            int firstCalls = 0, secondCalls = 0;
            var diagnostics = new List<string>();
            var port = new ItemExecutionPort(prep, meta, _ => 1U, ItemLifecyclePolicy.Create(),
                () => now, diagnosticSink: detail => diagnostics.Add(detail),
                guestBaselineBinder: (observer, token, region) =>
                {
                    if (!region.Equals(new RegionKey(0, 0))) return;
                    if (observer == 1001UL && ++firstCalls == 2)
                        ItemObserverReplicationAdapter.DispatchCandidateGuestAskItems(
                            () => ItemObserverReplicationAdapter.NoteAskItemsOriginalRan(false), null);
                    if (observer == 1002UL) secondCalls++;
                    ItemObserverReplicationAdapter.DispatchCandidateGuestAskItems(
                        () => ItemObserverReplicationAdapter.NoteAskItemsOriginalRan(true), () => { });
                });
            var projection = new DemandProjectionEngine(new ObserverSpatialAuthority());
            var policy = ItemDemandPolicy.Create(radius: 0, worldSize: 2);
            projection.Register(policy);
            var seam = new ItemProductionControlSeam(prep, meta, port, projection, policy, 2f);
            ItemCycleThresholds.Override = new ItemCycleThresholds(10f, 60f, 1f, 1f, true);
            try
            {
                seam.BeginSession(new SessionEpoch(101UL));
                if (!prep.PrepareFullMap(new SessionEpoch(101UL), "MapPEI", 2, meta, 0f)) return false;
                seam.UpdateObserver(1001UL, 201UL, 0, 0, true);
                seam.UpdateObserver(1002UL, 202UL, 0, 0, true);
                seam.UpdateObserver(1001UL, 201UL, 1, 1, true);
                seam.UpdateObserver(1002UL, 202UL, 1, 1, true);
                now += 2.1f; seam.Tick(2.1f);
                seam.UpdateObserver(1001UL, 201UL, 0, 0, true);
                seam.UpdateObserver(1002UL, 202UL, 0, 0, true);
                for (int i = 0; i < 20 && !string.Join("\n", diagnostics).Contains("event=delivery-confirmed-not-sent"); i++)
                    { now += 0.25f; seam.Tick(0.25f); }
                if (firstCalls != 2 || secondCalls != 2) return false;
                seam.UpdateObserver(1001UL, 201UL, 1, 1, true);
                now += 5f; seam.Tick(5f);
                seam.UpdateObserver(1001UL, 203UL, 1, 1, true);
                bool oldTokenRejected = !seam.TryManualRecovery(1001UL, 201UL, new RegionKey(1, 1));
                return oldTokenRejected && firstCalls == 2 && secondCalls == 2
                    && !ItemSettlementGate.BlocksRegionForObserver(1002UL, 202UL, 0, 0);
            }
            finally { ItemCycleThresholds.Override = null; }
        }

        internal static bool Test_IT60_MissingAssetDoesNotFinishSettlement()
        {
            var bridge = new TestNativeItemWarehouseBridge { SpawnPointCount = 1, MissingNaturalAsset = true };
            var prep = new ItemPreparationAuthority(bridge);
            bridge.GenerateNaturalItem(0, 0, CreateTestItemData(1001U, false, 0f));
            var meta = new ItemMetadataStore();
            float now = 10f;
            int presented = 0;
            var port = new ItemExecutionPort(prep, meta, _ => 1U, CreateTestPolicy(), () => now,
                null, id => id == 1001UL, region => presented++);
            var proj = new DemandProjectionEngine(new ObserverSpatialAuthority());
            var policy = ItemDemandPolicy.Create(radius: 0, worldSize: 1);
            proj.Register(policy);
            var seam = new ItemProductionControlSeam(prep, meta, port, proj, policy, 2.0f);
            seam.BeginSession(new SessionEpoch(101UL));
            if (!prep.PrepareFullMap(new SessionEpoch(101UL), "MapPEI", 1, meta, 0f)) return false;
            seam.UpdateObserver(1001UL, 201UL, 0, 0, true);
            seam.Tick(0f);
            seam.RemoveObserver(1001UL);
            seam.Tick(2f);
            now = 40f;
            ItemCycleThresholds.Override = new ItemCycleThresholds(10f, 60f, 10f, 1f, true);
            try
            {
                seam.UpdateObserver(1001UL, 201UL, 0, 0, true);
                seam.Tick(0f);
                seam.Tick(0f);
                return seam.IsRegionIsolated(new RegionKey(0, 0)) && presented == 1;
            }
            finally { ItemCycleThresholds.Override = null; }
        }

        internal static bool Test_IT61_ItemFailureRecoveryPolicyIsBounded()
        {
            LifecyclePolicy policy = ItemLifecyclePolicy.Create();
            RetrySchedule retry = policy.FailedRetry;

            return Math.Abs(retry.BaseIntervalSeconds - 0.5f) < 0.001f
                && Math.Abs(retry.IntervalFor(1) - 0.5f) < 0.001f
                && Math.Abs(retry.IntervalFor(2) - 1.0f) < 0.001f
                && Math.Abs(retry.IntervalFor(3) - 2.0f) < 0.001f
                && Math.Abs(retry.CapSeconds - 2.0f) < 0.001f
                && retry.MaxRetries == 3
                && Math.Abs(retry.MaxElapsedSeconds - 10.0f) < 0.001f;
        }

        internal static bool Test_IT62_ExceptionRecoveryRetriesOnlyTheFailedRegion()
        {
            var bridge = new TestNativeItemWarehouseBridge { SpawnPointCount = 1 };
            var prep = new ItemPreparationAuthority(bridge);
            bridge.GenerateNaturalItem(0, 0, CreateTestItemData(1001U, false, 0f));
            var meta = new ItemMetadataStore();
            float now = 10f;
            int presented = 0;
            var port = new ItemExecutionPort(prep, meta, _ => 1U, ItemLifecyclePolicy.Create(), () => now,
                null, id => true, region => presented++);
            var projection = new DemandProjectionEngine(new ObserverSpatialAuthority());
            DemandPolicy policy = ItemDemandPolicy.Create(radius: 0, worldSize: 1);
            projection.Register(policy);
            var seam = new ItemProductionControlSeam(prep, meta, port, projection, policy, 2.0f);
            seam.BeginSession(new SessionEpoch(101UL));
            if (!prep.PrepareFullMap(new SessionEpoch(101UL), "MapPEI", 1, meta, 0f)) return false;
            seam.UpdateObserver(1001UL, 201UL, 0, 0, true);
            seam.Tick(0f);
            seam.RemoveObserver(1001UL);
            seam.Tick(2f);
            now = 40f;
            bridge.ThrowOnRespawn = true;
            ItemCycleThresholds.Override = new ItemCycleThresholds(10f, 60f, 1f, 1f, true);
            try
            {
                seam.UpdateObserver(1001UL, 201UL, 0, 0, true);
                seam.Tick(0f);
                seam.Tick(0f);
                bool isolated = seam.IsRegionIsolated(new RegionKey(0, 0));
                bridge.ThrowOnRespawn = false;
                now += 0.5f;
                seam.Tick(0.5f);
                seam.Tick(0f);
                return isolated && presented == 2 && seam.LastSettlementFault == null;
            }
            finally { ItemCycleThresholds.Override = null; }
        }

        internal static bool Test_IT57_NetworkedDeletionStillBroadcastsWhenGateClosed()
        {
            int cancelled = 0;
            int removed = 0;
            int broadcasts = 0;
            var dispatch = new ItemDeletionDispatch();
            bool result = dispatch.Remove(
                new ItemDeletionRequest(0, 0, 1001U, true),
                () => false,
                () => cancelled++,
                () => { removed++; return true; },
                () => { broadcasts++; return true; });
            int pendingCancels = 0;
            int pendingBroadcasts = 0;
            bool pendingResult = dispatch.Remove(
                new ItemDeletionRequest(0, 0, 1002U, false),
                () => true,
                () => pendingCancels++,
                () => true,
                () => { pendingBroadcasts++; return true; });
            return result && cancelled == 1 && removed == 1 && broadcasts == 1
                && pendingResult && pendingCancels == 1 && pendingBroadcasts == 1;
        }

        internal static bool Test_IT58_UnpresentedDeletionKeepsNativeDestroyAction()
        {
            int broadcasts = 0;
            var dispatch = new ItemDeletionDispatch();
            bool result = dispatch.Remove(
                new ItemDeletionRequest(0, 0, 1002U, false),
                () => true,
                () => { },
                () => true,
                () => { broadcasts++; return true; });
            return result && broadcasts == 1;
        }

        internal static bool Test_IT67_IsolatedRegionReentryStartsFreshRecovery()
        {
            var bridge = new TestNativeItemWarehouseBridge { MissingNaturalInputs = true };
            var prep = new ItemPreparationAuthority(bridge);
            bridge.GenerateNaturalItem(0, 0, CreateTestItemData(1001U, false, 0f));
            var meta = new ItemMetadataStore();
            float now = 10f;
            int presented = 0;
            var port = new ItemExecutionPort(prep, meta, _ => 1U, CreateTestPolicy(), () => now,
                null, id => id == 1001UL, region => presented++);
            var projection = new DemandProjectionEngine(new ObserverSpatialAuthority());
            var policy = ItemDemandPolicy.Create(radius: 0, worldSize: 1);
            projection.Register(policy);
            var seam = new ItemProductionControlSeam(prep, meta, port, projection, policy, 2.0f);
            seam.BeginSession(new SessionEpoch(101UL));
            if (!prep.PrepareFullMap(new SessionEpoch(101UL), "MapPEI", 1, meta, 0f)) return false;
            seam.UpdateObserver(1001UL, 201UL, 0, 0, true);
            seam.Tick(0f);
            seam.RemoveObserver(1001UL);
            seam.Tick(2f);
            now = 40f;
            ItemCycleThresholds.Override = new ItemCycleThresholds(10f, 60f, 1f, 1f, true);
            try
            {
                seam.UpdateObserver(1001UL, 201UL, 0, 0, true);
                seam.Tick(0f);
                seam.Tick(0f);
                if (!seam.IsRegionIsolated(new RegionKey(0, 0))) return false;
                seam.RemoveObserver(1001UL);
                seam.Tick(3f);
                bridge.MissingNaturalInputs = false;
                seam.UpdateObserver(1001UL, 201UL, 0, 0, true);
                for (int i = 0; i < 4; i++) seam.Tick(0f);
                return !seam.IsRegionIsolated(new RegionKey(0, 0))
                    && seam.GetDemand(new RegionKey(0, 0)) == 1;
            }
            finally { ItemCycleThresholds.Override = null; }
        }

        internal static bool Test_IT78_GuestReplicationFailureStaysObserverLocal()
        {
            var bridge = new TestNativeItemWarehouseBridge();
            var prep = new ItemPreparationAuthority(bridge);
            var meta = new ItemMetadataStore();
            var epoch = new SessionEpoch(401UL);
            var policy = ItemDemandPolicy.Create(radius: 1, worldSize: 4);
            int binderCalls = 0;
            Action<ulong, ulong, RegionKey> binder = (_, _, _) =>
            {
                binderCalls++;
                if (binderCalls == 1)
                    throw new ItemObserverReplicationFailureException(
                        "guest baseline send failure", true);
            };
            prep.PrepareFullMap(epoch, "MapPEI", 4, meta, 0f);
            var port = new ItemExecutionPort(prep, meta, _ => 1U, CreateTestPolicy(),
                guestBaselineBinder: binder);
            var projection = new DemandProjectionEngine(new ObserverSpatialAuthority());
            projection.Register(policy);
            var seam = new ItemProductionControlSeam(prep, meta, port, projection, policy, 2.0f);
            seam.BeginSession(epoch);

            bool firstAccepted = true;
            try { seam.UpdateObserver(1001UL, 201UL, 0, 0, true); }
            catch (Exception) { firstAccepted = false; }
            bool secondAccepted = true;
            try { seam.UpdateObserver(1002UL, 202UL, 3, 3, true); }
            catch (Exception) { secondAccepted = false; }
            return firstAccepted && secondAccepted
                && seam.ObserverCount == 2
                && seam.PendingAcquireRetryCount == 1
                && seam.DemandRegionCount == 7
                && seam.ActiveLeaseCount == 8
                && !seam.RepairRequired
                && binderCalls == 8;
        }

        internal static bool Test_IT79_UncertainGuestSendIsQuarantinedNotRetried()
        {
            var bridge = new TestNativeItemWarehouseBridge();
            var prep = new ItemPreparationAuthority(bridge);
            var meta = new ItemMetadataStore();
            var epoch = new SessionEpoch(402UL);
            var policy = ItemDemandPolicy.Create(radius: 0, worldSize: 2);
            int calls = 0;
            Action<ulong, ulong, RegionKey> binder = (_, _, _) =>
            {
                calls++;
                if (calls == 1) throw new InvalidOperationException("native send uncertain");
            };
            prep.PrepareFullMap(epoch, "MapPEI", 2, meta, 0f);
            var port = new ItemExecutionPort(prep, meta, _ => 1U, CreateTestPolicy(),
                guestBaselineBinder: binder);
            var projection = new DemandProjectionEngine(new ObserverSpatialAuthority());
            projection.Register(policy);
            var seam = new ItemProductionControlSeam(prep, meta, port, projection, policy, 2.0f);
            seam.BeginSession(epoch);
            bool accepted = true;
            try { seam.UpdateObserver(1001UL, 201UL, 0, 0, true); }
            catch (Exception) { accepted = false; }
            seam.AdvanceTime(10f);
            seam.UpdateObserver(1001UL, 201UL, 0, 0, true);
            return accepted && calls == 1 && seam.PendingAcquireRetryCount == 0
                && seam.ExecutionPort.GuestBaselineBindingCount == 0;
        }

        internal static bool Test_IT80_UnknownItemsDoNotRestartClearingOrRestock()
        {
            var bridge = new TestNativeItemWarehouseBridge { SpawnPointCount = 1 };
            var prep = new ItemPreparationAuthority(bridge);
            bridge.GenerateNaturalItem(0, 0, CreateTestItemData(1001U, false, 0f));
            var meta = new ItemMetadataStore();
            float now = 10f;
            int creates = 0;
            bridge.OnRespawn = (_, __) => creates++;
            var port = new ItemExecutionPort(prep, meta, _ => 1U, CreateTestPolicy(), () => now,
                null, _ => true, _ => { });
            var projection = new DemandProjectionEngine(new ObserverSpatialAuthority());
            var policy = ItemDemandPolicy.Create(radius: 0, worldSize: 1);
            projection.Register(policy);
            var seam = new ItemProductionControlSeam(prep, meta, port, projection, policy, 2.0f);
            seam.BeginSession(new SessionEpoch(101UL));
            if (!prep.PrepareFullMap(new SessionEpoch(101UL), "MapPEI", 1, meta, 0f)) return false;
            seam.UpdateObserver(1001UL, 201UL, 0, 0, true);
            seam.Tick(0f);
            seam.RemoveObserver(1001UL);
            seam.Tick(2f);
            now = 40f;
            ItemCycleThresholds.Override = new ItemCycleThresholds(10f, 0.2f, 1f, 1f, true);
            try
            {
                seam.UpdateObserver(1001UL, 201UL, 0, 0, true);
                seam.Tick(0f);
                bridge.Regions[(0, 0)] = null;
                seam.Tick(0f);
                bridge.Regions[(0, 0)] = new List<ItemData>();
                int removals = bridge.RemovalCalls;
                now += 0.5f;
                seam.Tick(0.5f);
                seam.Tick(0f);
                bool isolated = seam.IsRegionIsolated(new RegionKey(0, 0));
                int left = bridge.GetRegionItems(0, 0) == null ? -1 : bridge.GetRegionItems(0, 0).Count;
                return isolated && creates == 0 && bridge.RemovalCalls == removals && left == 0;
            }
            finally { ItemCycleThresholds.Override = null; }
        }

        internal static bool Test_IT81_UnobservedAskItemsIsNotSafeRetry()
        {
            bool saw = false;
            bool safe = true;
            try
            {
                ItemObserverReplicationAdapter.DispatchCandidateGuestAskItems(() => { }, null);
            }
            catch (ItemObserverReplicationFailureException ex)
            {
                saw = true;
                safe = ex.SafeToRetry;
            }
            return saw && !safe;
        }

        internal static bool Test_IT82_AppliedCreateIsNotRedrawnAfterThrow()
        {
            var bridge = new TestNativeItemWarehouseBridge { SpawnPointCount = 1 };
            var prep = new ItemPreparationAuthority(bridge);
            var meta = new ItemMetadataStore();
            float now = 10f;
            int creates = 0;
            bridge.OnRespawn = (x, y) =>
            {
                creates++;
                bridge.GenerateNaturalItem(x, y, CreateTestItemData(7101U, false, now));
                throw new InvalidOperationException("created-then-threw");
            };
            var port = new ItemExecutionPort(prep, meta, _ => 1U, ItemLifecyclePolicy.Create(), () => now,
                null, _ => true, _ => { });
            var projection = new DemandProjectionEngine(new ObserverSpatialAuthority());
            var policy = ItemDemandPolicy.Create(radius: 0, worldSize: 1);
            projection.Register(policy);
            var seam = new ItemProductionControlSeam(prep, meta, port, projection, policy, 2.0f);
            seam.BeginSession(new SessionEpoch(101UL));
            if (!prep.PrepareFullMap(new SessionEpoch(101UL), "MapPEI", 1, meta, 0f)) return false;
            seam.UpdateObserver(1001UL, 201UL, 0, 0, true);
            seam.Tick(0f);
            seam.RemoveObserver(1001UL);
            seam.Tick(2f);
            now = 40f;
            ItemCycleThresholds.Override = new ItemCycleThresholds(10f, 60f, 1f, 1f, true);
            try
            {
                seam.UpdateObserver(1001UL, 201UL, 0, 0, true);
                seam.Tick(0f);
                seam.Tick(0f);
                now += 0.5f;
                seam.Tick(0.5f);
                seam.Tick(0f);
                bool kept = false;
                var items = bridge.GetRegionItems(0, 0);
                if (items != null)
                    for (int i = 0; i < items.Count; i++)
                        kept |= items[i] != null && items[i].instanceID == 7101U;
                return creates == 1 && kept && !seam.IsRegionIsolated(new RegionKey(0, 0))
                    && seam.LastSettlementFault == null;
            }
            finally { ItemCycleThresholds.Override = null; }
        }

        internal static bool Test_IT83_ExhaustedSettlementNotifiesCurrentWaiterOnce()
        {
            var bridge = new TestNativeItemWarehouseBridge { SpawnPointCount = 1, ThrowOnRespawn = true };
            var prep = new ItemPreparationAuthority(bridge);
            bridge.GenerateNaturalItem(0, 0, CreateTestItemData(1001U, false, 0f));
            var meta = new ItemMetadataStore();
            float now = 10f;
            int notices = 0;
            var diagnostics = new List<string>();
            var port = new ItemExecutionPort(prep, meta, _ => 1U, ItemLifecyclePolicy.Create(), () => now,
                detail => diagnostics.Add(detail), _ => true, _ => { });
            var projection = new DemandProjectionEngine(new ObserverSpatialAuthority());
            var policy = ItemDemandPolicy.Create(radius: 0, worldSize: 1);
            projection.Register(policy);
            var seam = new ItemProductionControlSeam(prep, meta, port, projection, policy, 2.0f);
            seam.RecoveryExhausted = (observer, token, region, half) =>
            {
                if (observer == 1001UL && token == 201UL && region == new RegionKey(0, 0) && !half)
                    notices++;
            };
            seam.BeginSession(new SessionEpoch(101UL));
            if (!prep.PrepareFullMap(new SessionEpoch(101UL), "MapPEI", 1, meta, 0f)) return false;
            seam.UpdateObserver(1001UL, 201UL, 0, 0, true);
            seam.Tick(0f);
            seam.RemoveObserver(1001UL);
            seam.Tick(2f);
            now = 40f;
            ItemCycleThresholds.Override = new ItemCycleThresholds(10f, 60f, 1f, 1f, true);
            try
            {
                seam.UpdateObserver(1001UL, 201UL, 0, 0, true);
                seam.Tick(0f);
                seam.Tick(0f);
                now = 51f;
                seam.Tick(0f);
                seam.Tick(0f);
                string trace = string.Join("\n", diagnostics);
                return notices == 1 && seam.IsRegionIsolated(new RegionKey(0, 0))
                    && trace.Contains("event=settlement-first-failure")
                    && trace.Contains("target=1001/201") && trace.Contains("attempt=1")
                    && trace.Contains("budget=3/10s") && trace.Contains("simulated natural creation exception")
                    && trace.Contains("event=recovery-notice type=recovery-exhausted");
            }
            finally { ItemCycleThresholds.Override = null; }
        }

        internal static bool Test_IT84_ManualRecoveryRejectsCallerOutsideProjection()
        {
            var bridge = new TestNativeItemWarehouseBridge { SpawnPointCount = 1, ThrowOnRespawn = true };
            var prep = new ItemPreparationAuthority(bridge);
            var meta = new ItemMetadataStore();
            float now = 10f;
            var port = new ItemExecutionPort(prep, meta, _ => 1U, ItemLifecyclePolicy.Create(), () => now,
                null, _ => false, _ => { });
            var projection = new DemandProjectionEngine(new ObserverSpatialAuthority());
            var policy = ItemDemandPolicy.Create(radius: 0, worldSize: 4);
            projection.Register(policy);
            var seam = new ItemProductionControlSeam(prep, meta, port, projection, policy, 2.0f);
            seam.BeginSession(new SessionEpoch(101UL));
            if (!prep.PrepareFullMap(new SessionEpoch(101UL), "MapPEI", 4, meta, 0f)) return false;
            seam.UpdateObserver(2002UL, 302UL, 3, 3, true);
            seam.Tick(0f);
            seam.RemoveObserver(2002UL);
            seam.Tick(2f);
            now = 40f;
            ItemCycleThresholds.Override = new ItemCycleThresholds(10f, 60f, 1f, 1f, true);
            seam.UpdateObserver(2002UL, 302UL, 3, 3, true);
            seam.Tick(0f);
            seam.Tick(0f);
            seam.UpdateObserver(1001UL, 201UL, 0, 0, true);
            bool rejected = !seam.TryManualRecovery(1001UL, 201UL, new RegionKey(3, 3));
            bool isolated = seam.IsRegionIsolated(new RegionKey(3, 3));
            ItemCycleThresholds.Override = null;
            return rejected && isolated;
        }

        internal static bool Test_IT85_CoveringCallerManualRecoveryDoesNotUseAnotherCooldown()
        {
            var bridge = new TestNativeItemWarehouseBridge { SpawnPointCount = 1, ThrowOnRespawn = true };
            var prep = new ItemPreparationAuthority(bridge);
            var meta = new ItemMetadataStore();
            float now = 10f;
            var port = new ItemExecutionPort(prep, meta, _ => 1U, ItemLifecyclePolicy.Create(), () => now,
                null, _ => false, _ => { });
            var projection = new DemandProjectionEngine(new ObserverSpatialAuthority());
            var policy = ItemDemandPolicy.Create(radius: 0, worldSize: 4);
            projection.Register(policy);
            var seam = new ItemProductionControlSeam(prep, meta, port, projection, policy, 2.0f);
            seam.BeginSession(new SessionEpoch(101UL));
            if (!prep.PrepareFullMap(new SessionEpoch(101UL), "MapPEI", 4, meta, 0f)) return false;
            seam.UpdateObserver(2002UL, 302UL, 3, 3, true);
            seam.Tick(0f);
            seam.RemoveObserver(2002UL);
            seam.Tick(2f);
            now = 40f;
            ItemCycleThresholds.Override = new ItemCycleThresholds(10f, 60f, 1f, 1f, true);
            try
            {
                seam.UpdateObserver(2002UL, 302UL, 3, 3, true);
                seam.Tick(0f);
                seam.Tick(0f);
                seam.UpdateObserver(1001UL, 201UL, 0, 0, true);
                bridge.ThrowOnRespawn = false;
                now = 51f;
                seam.Tick(0f);
                bool outsider = seam.TryManualRecovery(1001UL, 201UL, new RegionKey(0, 0));
                bool first = seam.TryManualRecovery(2002UL, 302UL, new RegionKey(3, 3));
                bool second = seam.TryManualRecovery(2002UL, 302UL, new RegionKey(3, 3));
                bool otherCaller = seam.TryManualRecovery(1001UL, 201UL, new RegionKey(3, 3));
                if (!outsider && first && !second && !otherCaller) return true;
                throw new InvalidOperationException("outsider=" + outsider + " first=" + first
                    + " second=" + second + " other=" + otherCaller
                    + " isolated=" + seam.IsRegionIsolated(new RegionKey(3, 3)));
            }
            finally { ItemCycleThresholds.Override = null; }
        }

        internal static bool Test_IT87_SeamCooldownIsPerCoveringCaller()
        {
            var bridge = new TestNativeItemWarehouseBridge { SpawnPointCount = 1, ThrowOnRespawn = true };
            var prep = new ItemPreparationAuthority(bridge);
            var meta = new ItemMetadataStore();
            float now = 10f;
            var port = new ItemExecutionPort(prep, meta, _ => 1U, ItemLifecyclePolicy.Create(), () => now,
                null, _ => false, _ => { });
            var projection = new DemandProjectionEngine(new ObserverSpatialAuthority());
            var policy = ItemDemandPolicy.Create(radius: 0, worldSize: 4);
            projection.Register(policy);
            var seam = new ItemProductionControlSeam(prep, meta, port, projection, policy, 2.0f);
            seam.BeginSession(new SessionEpoch(101UL));
            if (!prep.PrepareFullMap(new SessionEpoch(101UL), "MapPEI", 4, meta, 0f)) return false;
            seam.UpdateObserver(2002UL, 302UL, 2, 2, true);
            seam.Tick(0f);
            seam.RemoveObserver(2002UL);
            seam.Tick(2f);
            now = 40f;
            ItemCycleThresholds.Override = new ItemCycleThresholds(10f, 60f, 1f, 1f, true);
            try
            {
                seam.UpdateObserver(2002UL, 302UL, 2, 2, true);
                seam.UpdateObserver(1001UL, 201UL, 2, 2, true);
                seam.Tick(0f);
                seam.Tick(0f);
                now = 51f;
                bridge.ThrowOnRespawn = false;
                seam.Tick(0f);
                bool first = seam.TryManualRecovery(2002UL, 302UL, new RegionKey(2, 2));
                bridge.ThrowOnRespawn = true;
                seam.Tick(0f);
                now = 51.5f;
                seam.Tick(0f);
                now = 52.5f;
                seam.Tick(0f);
                now = 54.5f;
                seam.Tick(0f);
                bool aBlocked = !seam.TryManualRecovery(2002UL, 302UL, new RegionKey(2, 2));
                bool bAccepted = seam.TryManualRecovery(1001UL, 201UL, new RegionKey(2, 2));
                return first && aBlocked && bAccepted;
            }
            finally { ItemCycleThresholds.Override = null; }
        }

        internal static bool Test_IT86_UnconfirmedNativeResultNotifiesWithoutReplay()
        {
            var bridge = new TestNativeItemWarehouseBridge { SpawnPointCount = 1 };
            var prep = new ItemPreparationAuthority(bridge);
            bridge.GenerateNaturalItem(0, 0, CreateTestItemData(1001U, false, 0f));
            var meta = new ItemMetadataStore();
            float now = 10f;
            int notices = 0;
            int creates = 0;
            bridge.OnRespawn = (_, __) => creates++;
            var port = new ItemExecutionPort(prep, meta, _ => 1U, ItemLifecyclePolicy.Create(), () => now,
                null, _ => true, _ => { });
            var projection = new DemandProjectionEngine(new ObserverSpatialAuthority());
            var policy = ItemDemandPolicy.Create(radius: 0, worldSize: 1);
            projection.Register(policy);
            var seam = new ItemProductionControlSeam(prep, meta, port, projection, policy, 2.0f);
            seam.RecoveryUnconfirmed = (_, __, ___) => notices++;
            seam.BeginSession(new SessionEpoch(101UL));
            if (!prep.PrepareFullMap(new SessionEpoch(101UL), "MapPEI", 1, meta, 0f)) return false;
            seam.UpdateObserver(1001UL, 201UL, 0, 0, true);
            seam.Tick(0f);
            seam.RemoveObserver(1001UL);
            seam.Tick(2f);
            now = 40f;
            ItemCycleThresholds.Override = new ItemCycleThresholds(10f, 60f, 1f, 1f, true);
            try
            {
                seam.UpdateObserver(1001UL, 201UL, 0, 0, true);
                seam.Tick(0f);
                bridge.Regions[(0, 0)] = null;
                seam.Tick(0f);
                seam.Tick(0f);
                return notices == 1 && creates == 0 && seam.IsRegionIsolated(new RegionKey(0, 0));
            }
            finally { ItemCycleThresholds.Override = null; }
        }

        internal sealed class TestNativeItemWarehouseBridge : INativeItemWarehouseBridge
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
            public int RemovalCalls;
            public bool TryRemoveItem(byte x, byte y, uint instanceId)
            {
                RemovalCalls++;
                if (instanceId == 0U || !Regions.TryGetValue((x, y), out var list) || list == null) return false;
                int index = list.FindIndex(it => it != null && it.instanceID == instanceId);
                if (index < 0) return false;
                list.RemoveAt(index);
                return list.Find(it => it != null && it.instanceID == instanceId) == null;
            }

            public int SpawnPointCount;
            public bool DeclineRespawn;
            public bool ThrowOnRespawn;
            public bool MissingNaturalInputs;
            public bool MissingNaturalAsset;
            public Action<byte, byte> OnRespawn;
            private uint _nextRespawnId = 7001U;

            public int CountSpawnPoints(byte x, byte y) => SpawnPointCount;

            public bool AreNaturalInputsAvailable(byte x, byte y, out string failureReason)
            {
                failureReason = MissingNaturalInputs ? "test-natural-inputs-unavailable" : string.Empty;
                return !MissingNaturalInputs;
            }

            public float ReadLastRespawn(byte x, byte y) =>
                RegionRespawnTimes.TryGetValue((x, y), out float time) ? time : 0f;

            public bool TryCreateOneNatural(byte x, byte y, float now) => TryApplyNativeRespawn(x, y, now);

            public ItemNaturalCreationOutcome CreateOneNatural(byte x, byte y, float now)
            {
                if (ThrowOnRespawn) throw new InvalidOperationException("simulated natural creation exception");
                if (MissingNaturalInputs) return ItemNaturalCreationOutcome.MissingInputs;
                if (SpawnPointCount <= 0) return ItemNaturalCreationOutcome.NoChange;
                if (MissingNaturalAsset) return ItemNaturalCreationOutcome.MissingAsset;
                if (DeclineRespawn) return ItemNaturalCreationOutcome.Declined;
                return TryApplyNativeRespawn(x, y, now)
                    ? ItemNaturalCreationOutcome.Created
                    : ItemNaturalCreationOutcome.NoChange;
            }

            public bool TryApplyNativeRespawn(byte x, byte y, float now)
            {
                if (ThrowOnRespawn) throw new InvalidOperationException("simulated natural creation exception");
                if (DeclineRespawn) return false;
                OnRespawn?.Invoke(x, y);
                var created = CreateTestItemData(_nextRespawnId++, isDropped: false, lastDropped: now);
                GenerateNaturalItem(x, y, created);
                return Regions.TryGetValue((x, y), out var list)
                    && list.Find(it => it != null && it.instanceID == created.instanceID) != null;
            }

            public bool CommitRespawnTime(byte x, byte y, float now)
            {
                RegionRespawnTimes[(x, y)] = now;
                return RegionRespawnTimes.TryGetValue((x, y), out float time) && Math.Abs(time - now) <= 0.001f;
            }

            public void BindOriginTracker(IItemOriginTracker tracker)
            {
                OriginTracker = tracker;
                ItemOriginTrackingSeam.SetActiveTracker(tracker);
            }

            public object ReadNativeRegionsScope() => null;

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
