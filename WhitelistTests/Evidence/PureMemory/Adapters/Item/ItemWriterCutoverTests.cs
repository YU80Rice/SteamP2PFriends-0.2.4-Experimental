using System;
using SDG.Unturned;
using SteamP2PFriends.Core.Identity;
using SteamP2PFriends.MultiObserver.Demand;
using SteamP2PFriends.MultiObserver.Lifecycle;
using SteamP2PFriends.MultiObserver.SPI;
using SteamP2PFriends.Adapters.Item;
using SteamP2PFriends.Adapters.Item.Patches;
using SteamP2PFriends.Adapters.Resource;
using SteamP2PFriends.Host;
using SteamP2PFriends.MultiObserver;
using System.Reflection;

namespace SteamP2PFriends.WhitelistTests
{
    internal static class ItemWriterCutoverTests
    {
        internal static bool Test_IT88_SessionBoundarySelectsSingleItemAuthorityWriter()
        {
            ItemProductionMode savedMode = MultiObserverShadowCoordinator.ItemProductionMode;
            bool savedSuppress = ItemLegacyCycleGate.SuppressFullMapWriter;
            try
            {
                MultiObserverShadowCoordinator.ItemProductionMode = ItemProductionMode.LegacyWriter;
                ItemLegacyCycleGate.SuppressFullMapWriter = false;
                bool selected = MultiObserverShadowCoordinator.TrySelectItemAuthorityWriter(
                    88UL, "session-a|srv|PEI");
                bool writer = selected
                    && MultiObserverShadowCoordinator.ItemAuthorityWriterOwnsSession
                    && MultiObserverShadowCoordinator.ItemProductionMode == ItemProductionMode.Candidate
                    && ItemLegacyCycleGate.SuppressFullMapWriter
                    && ItemLegacyCycleGate.ShouldSkipLegacy;
                bool sameSession = MultiObserverShadowCoordinator.TrySelectItemAuthorityWriter(
                    88UL, "session-a|srv|PEI");
                MultiObserverShadowCoordinator.ItemProductionMode = ItemProductionMode.LegacyWriter;
                bool noFallback = sameSession
                    && MultiObserverShadowCoordinator.ItemAuthorityWriterOwnsSession
                    && MultiObserverShadowCoordinator.ItemProductionMode == ItemProductionMode.Candidate
                    && ItemLegacyCycleGate.ShouldSkipLegacy;
                return writer && noFallback;
            }
            finally
            {
                MultiObserverShadowCoordinator.ReleaseItemAuthorityWriter();
                MultiObserverShadowCoordinator.ItemProductionMode = savedMode;
                ItemLegacyCycleGate.SuppressFullMapWriter = savedSuppress;
            }
        }

        internal static bool Test_IT89_ListenHostFullMapCycleDoesNotDrive()
        {
            FieldInfo hostMode = typeof(HostManager).GetField("_hostMode", BindingFlags.Static | BindingFlags.NonPublic);
            FieldInfo p2p = typeof(HostManager).GetField("<IsP2PServerActive>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic);
            if (hostMode == null || p2p == null) return false;
            object savedMode = hostMode.GetValue(null);
            object savedP2p = p2p.GetValue(null);
            ItemProductionMode savedWriter = MultiObserverShadowCoordinator.ItemProductionMode;
            string savedSummary = ItemManagerP0B6RegenerateOnLevelLoadedPatch.RegenerationSummary;
            try
            {
                MultiObserverShadowCoordinator.ReleaseItemAuthorityWriter();
                MultiObserverShadowCoordinator.ItemProductionMode = ItemProductionMode.LegacyWriter;
                hostMode.SetValue(null, Enum.Parse(hostMode.FieldType, "P2P"));
                p2p.SetValue(null, true);
                ItemManagerP0B6RegenerateOnLevelLoadedPatch.ResetRegenerationFlag();
                ItemManagerP0B6RegenerateOnLevelLoadedPatch.TryRegenerateOnLevelLoaded(Level.BUILD_INDEX_GAME);
                string summary = ItemManagerP0B6RegenerateOnLevelLoadedPatch.RegenerationSummary ?? string.Empty;
                return !ItemManagerP0B6RegenerateOnLevelLoadedPatch.RegenerationDone
                    && summary.IndexOf("跳过预生成", StringComparison.Ordinal) < 0
                    && summary.IndexOf("全地图", StringComparison.Ordinal) < 0;
            }
            finally
            {
                ItemManagerP0B6RegenerateOnLevelLoadedPatch.ResetRegenerationFlag();
                p2p.SetValue(null, savedP2p);
                hostMode.SetValue(null, savedMode);
                MultiObserverShadowCoordinator.ReleaseItemAuthorityWriter();
                MultiObserverShadowCoordinator.ItemProductionMode = savedWriter;
            }
        }

        internal static bool Test_IT90_OwnedSessionRejectsLegacyEligibilityAndFallback()
        {
            ItemProductionMode saved = MultiObserverShadowCoordinator.ItemProductionMode;
            bool savedSuppress = ItemLegacyCycleGate.SuppressFullMapWriter;
            try
            {
                MultiObserverShadowCoordinator.ReleaseItemAuthorityWriter();
                bool selected = MultiObserverShadowCoordinator.TrySelectItemAuthorityWriter(90UL, "session-b|srv|PEI");
                MultiObserverShadowCoordinator.ItemProductionMode = ItemProductionMode.LegacyWriter;
                bool generationRetired = !ItemGenerationAuthorityAdapter.ShouldGenerateForObserver(null);
                bool replicationRetired = !ItemObserverReplicationAdapter.ShouldReplicateForObserver(null);
                bool cycleRetired = ItemLegacyCycleGate.ShouldSkipLegacy;
                return selected && generationRetired && replicationRetired && cycleRetired
                    && MultiObserverShadowCoordinator.ItemProductionMode == ItemProductionMode.Candidate;
            }
            finally
            {
                MultiObserverShadowCoordinator.ReleaseItemAuthorityWriter();
                MultiObserverShadowCoordinator.ItemProductionMode = saved;
                ItemLegacyCycleGate.SuppressFullMapWriter = savedSuppress;
            }
        }

        internal static bool Test_IT91_SessionSwitchDropsOldConnectionAndDoesNotRepeatPrepare()
        {
            var bridge = new ItemPreparationAndFirstDemandTests.TestNativeItemWarehouseBridge();
            int submits = 0;
            bridge.OnSubmitGeneration = (x, y) => submits++;
            var prep = new ItemPreparationAuthority(bridge);
            var meta = new ItemMetadataStore();
            var policy = ItemDemandPolicy.Create(0, 8);
            var proj = new DemandProjectionEngine(new ObserverSpatialAuthority());
            proj.Register(policy);
            var port = new ItemExecutionPort(prep, meta, _ => 1U, new LifecyclePolicy(
                2f, new RetrySchedule(0.5f, 10f, 4), new RetrySchedule(1f, 10f, int.MaxValue),
                new HeartbeatPolicy(5f, 6), "ItemCutover"));
            var seam = new ItemProductionControlSeam(prep, meta, port, proj, policy, 2f);
            MultiObserverShadowCoordinator.BindItemAuthorityProduction(seam, prep);
            try
            {
                bool first = MultiObserverShadowCoordinator.BeginItemAuthoritySession(91UL, "a|srv|PEI");
                MultiObserverShadowCoordinator.ObserveItemAuthority(1001UL, 11UL, 0, 0);
                int afterFirst = submits;
                ulong gen = MultiObserverShadowCoordinator.GetItemConnectionGeneration(1001UL);
                bool second = MultiObserverShadowCoordinator.BeginItemAuthoritySession(91UL, "a|srv|PEI");
                bool noRepeat = second && submits == afterFirst && gen == 11UL && afterFirst > 0;
                MultiObserverShadowCoordinator.EndItemAuthoritySession();
                MultiObserverShadowCoordinator.ObserveItemAuthority(1001UL, 11UL, 0, 0);
                bool dropped = MultiObserverShadowCoordinator.GetItemConnectionGeneration(1001UL) == 0UL
                    && !MultiObserverShadowCoordinator.TryManualItemRecovery(1001UL, 11UL, new RegionKey(0, 0));
                bool switched = MultiObserverShadowCoordinator.BeginItemAuthoritySession(92UL, "b|srv|Russia");
                MultiObserverShadowCoordinator.ObserveItemAuthority(1001UL, 22UL, 1, 1);
                return first && noRepeat && dropped && switched
                    && MultiObserverShadowCoordinator.GetItemConnectionGeneration(1001UL) == 22UL
                    && !MultiObserverShadowCoordinator.TryManualItemRecovery(1001UL, 11UL, new RegionKey(0, 0));
            }
            finally
            {
                MultiObserverShadowCoordinator.EndItemAuthoritySession();
                MultiObserverShadowCoordinator.BindItemAuthorityProduction(null, null);
            }
        }

        internal static bool Test_SFP01_ResourceStartedEngineStillInitializesItemPerWorld()
        {
            var projection = new DemandProjectionEngine(new ObserverSpatialAuthority());
            var shared = new LifecycleOrchestrationEngine(projection, DefaultLifecycleDiagnostics.Instance);
            var resource = new ResourceProductionControlSeamTests.FakeResourceAdapters();
            DemandPolicy resourcePolicy = ResourceDemandPolicy.Create(0, (byte)Regions.WORLD_SIZE);
            DemandPolicy policy = ItemDemandPolicy.Create(0, (byte)Regions.WORLD_SIZE);
            projection.Register(resourcePolicy);
            projection.Register(policy);
            var resourceSeam = new ResourceProductionControlSeam(resource.Lifecycle, resource.Replication,
                _ => resource.Generation, projection, resourcePolicy,
                2f, shared);
            var bridge = new ItemPreparationAndFirstDemandTests.TestNativeItemWarehouseBridge();
            var preparation = new ItemPreparationAuthority(bridge);
            var metadata = new ItemMetadataStore();
            var execution = new ItemExecutionPort(preparation, metadata, _ => 1U, ItemLifecyclePolicy.Create());
            var seam = new ItemProductionControlSeam(preparation, metadata, execution, projection, policy, 2f, shared);
            MultiObserverShadowCoordinator.BindItemAuthorityProduction(seam, preparation);
            ItemStepFiveAdmission staleAdmission = seam.Admission;
            int submissions = 0;
            bridge.OnSubmitGeneration = (_, __) => submissions++;
            try
            {
                resourceSeam.BeginSession(new SessionEpoch(201UL));
                bool first = MultiObserverShadowCoordinator.BeginItemAuthoritySession(201UL, "one|srv|PEI");
                var firstAdmission = seam.Admission;
                var request = new ItemStepFiveRequest(1001UL, 11UL, new RegionKey(0, 0), 0,
                    Regions.WORLD_SIZE, true);
                staleAdmission.Admit(request, new ItemStepFiveLoadedProjection(_ => { }));
                bool staleRejected = seam.ObserverCount == 0;
                bool firstWorldReady = first && !ReferenceEquals(staleAdmission, firstAdmission)
                    && preparation.IsReady && preparation.SessionEpoch.Value == 201UL
                    && preparation.WorldIdentity == "one|srv|PEI" && submissions == Regions.WORLD_SIZE * Regions.WORLD_SIZE;
                MultiObserverShadowCoordinator.ObserveItemAuthority(1001UL, 11UL, 0, 0);
                firstAdmission.Admit(new ItemStepFiveRequest(1001UL, 11UL, new RegionKey(1, 1), 0,
                    Regions.WORLD_SIZE, false), new ItemStepFiveLoadedProjection(_ => { }));
                bool firstAdmissionCurrent = shared.GetDemand(DomainIds.Item, new RegionKey(0, 0)) == 0
                    && shared.GetDemand(DomainIds.Item, new RegionKey(1, 1)) == 1;
                resourceSeam.BeginSession(new SessionEpoch(202UL));
                int endsBeforeItemBegin = resource.Lifecycle.SessionEnds;
                bool second = MultiObserverShadowCoordinator.BeginItemAuthoritySession(202UL, "two|srv|Russia");
                bool secondWorldReady = second && resourceSeam.IsSessionActive && seam.SessionEpoch.Value == 202UL
                    && preparation.IsReady && preparation.SessionEpoch.Value == 202UL
                    && preparation.WorldIdentity == "two|srv|Russia" && submissions == 2 * Regions.WORLD_SIZE * Regions.WORLD_SIZE
                    && resource.Lifecycle.SessionEnds == endsBeforeItemBegin;
                var staleWorldRequest = new ItemStepFiveRequest(2002UL, 22UL, new RegionKey(0, 0), 0,
                    Regions.WORLD_SIZE, false);
                firstAdmission.Admit(staleWorldRequest, new ItemStepFiveLoadedProjection(_ => { }));
                bool oldAdmissionRejected = seam.ObserverCount == 0;
                MultiObserverShadowCoordinator.ObserveItemAuthority(2002UL, 22UL, 0, 0);
                seam.Admission.Admit(new ItemStepFiveRequest(2002UL, 22UL, new RegionKey(2, 2), 0,
                    Regions.WORLD_SIZE, false), new ItemStepFiveLoadedProjection(_ => { }));
                bool currentAdmission = shared.GetDemand(DomainIds.Item, new RegionKey(0, 0)) == 0
                    && shared.GetDemand(DomainIds.Item, new RegionKey(2, 2)) == 1;
                return staleRejected && firstWorldReady && firstAdmissionCurrent && secondWorldReady
                    && oldAdmissionRejected && currentAdmission;
            }
            finally
            {
                MultiObserverShadowCoordinator.EndItemAuthoritySession();
                MultiObserverShadowCoordinator.BindItemAuthorityProduction(null, null);
            }
        }

        internal static bool Test_IT92_ListenHostLegacyCycleStaysRetiredAfterSessionEnd()
        {
            FieldInfo hostMode = typeof(HostManager).GetField("_hostMode", BindingFlags.Static | BindingFlags.NonPublic);
            FieldInfo p2p = typeof(HostManager).GetField("<IsP2PServerActive>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic);
            FieldInfo server = typeof(Provider).GetField("_isServer", BindingFlags.Static | BindingFlags.NonPublic);
            if (hostMode == null || p2p == null || server == null) return false;
            object savedHost = hostMode.GetValue(null);
            object savedP2p = p2p.GetValue(null);
            object savedServer = server.GetValue(null);
            ItemProductionMode savedWriter = MultiObserverShadowCoordinator.ItemProductionMode;
            try
            {
                hostMode.SetValue(null, Enum.Parse(hostMode.FieldType, "P2P"));
                p2p.SetValue(null, true);
                server.SetValue(null, true);
                MultiObserverShadowCoordinator.BeginItemAuthoritySession(92UL, "c|srv|PEI");
                MultiObserverShadowCoordinator.EndItemAuthoritySession();
                bool listenRetired = !ItemManagerUpdateDedicatedGatePatch.SkipLegacyCycleWhenCandidateOwnsSession();
                ItemLegacyCycleGate.InOwnerStep = true;
                bool ownerRuns = ItemManagerUpdateDedicatedGatePatch.SkipLegacyCycleWhenCandidateOwnsSession();
                ItemLegacyCycleGate.InOwnerStep = false;
                server.SetValue(null, false);
                p2p.SetValue(null, false);
                hostMode.SetValue(null, Enum.Parse(hostMode.FieldType, "None"));
                bool singleplayerRuns = ItemManagerUpdateDedicatedGatePatch.SkipLegacyCycleWhenCandidateOwnsSession();
                return listenRetired && ownerRuns && singleplayerRuns;
            }
            finally
            {
                ItemLegacyCycleGate.InOwnerStep = false;
                server.SetValue(null, savedServer);
                p2p.SetValue(null, savedP2p);
                hostMode.SetValue(null, savedHost);
                MultiObserverShadowCoordinator.EndItemAuthoritySession();
                MultiObserverShadowCoordinator.ItemProductionMode = savedWriter;
            }
        }

        internal static bool Test_IT93_WorldChangeIsolatesOldItemSessionWithoutFallback()
        {
            var bridge = new ItemPreparationAndFirstDemandTests.TestNativeItemWarehouseBridge();
            var prep = new ItemPreparationAuthority(bridge);
            var meta = new ItemMetadataStore();
            var policy = ItemDemandPolicy.Create(0, 4);
            var proj = new DemandProjectionEngine(new ObserverSpatialAuthority());
            proj.Register(policy);
            var port = new ItemExecutionPort(prep, meta, _ => 1U, new LifecyclePolicy(
                2f, new RetrySchedule(0.5f, 10f, 4), new RetrySchedule(1f, 10f, int.MaxValue),
                new HeartbeatPolicy(5f, 6), "ItemCutoverMap"));
            var seam = new ItemProductionControlSeam(prep, meta, port, proj, policy, 2f);
            MultiObserverShadowCoordinator.BindItemAuthorityProduction(seam, prep);
            try
            {
                if (!MultiObserverShadowCoordinator.BeginItemAuthoritySession(93UL, "a|srv|PEI")) return false;
                MultiObserverShadowCoordinator.ObserveItemAuthority(1001UL, 11UL, 0, 0);
                bool changed = MultiObserverShadowCoordinator.BeginItemAuthoritySession(94UL, "a|srv|Russia");
                return changed
                    && MultiObserverShadowCoordinator.ItemAuthorityWriterOwnsSession
                    && MultiObserverShadowCoordinator.ItemProductionMode == ItemProductionMode.Candidate
                    && seam.SessionEpoch.Value == 94UL
                    && MultiObserverShadowCoordinator.GetItemConnectionGeneration(1001UL) == 0UL
                    && !MultiObserverShadowCoordinator.TryManualItemRecovery(1001UL, 11UL, new RegionKey(0, 0));
            }
            finally
            {
                MultiObserverShadowCoordinator.EndItemAuthoritySession();
                MultiObserverShadowCoordinator.BindItemAuthorityProduction(null, null);
            }
        }

        internal static bool Test_IT94_ListenHostEligibilityStaysRetiredWhenWriterUnowned()
        {
            FieldInfo hostMode = typeof(HostManager).GetField("_hostMode", BindingFlags.Static | BindingFlags.NonPublic);
            FieldInfo p2p = typeof(HostManager).GetField("<IsP2PServerActive>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic);
            FieldInfo server = typeof(Provider).GetField("_isServer", BindingFlags.Static | BindingFlags.NonPublic);
            FieldInfo dedicated = typeof(Dedicator).GetField("_isDedicated", BindingFlags.Static | BindingFlags.NonPublic);
            if (hostMode == null || p2p == null || server == null || dedicated == null) return false;
            object savedHost = hostMode.GetValue(null);
            object savedP2p = p2p.GetValue(null);
            object savedServer = server.GetValue(null);
            object savedDedicated = dedicated.GetValue(null);
            try
            {
                MultiObserverShadowCoordinator.EndItemAuthoritySession();
                hostMode.SetValue(null, Enum.Parse(hostMode.FieldType, "P2P"));
                p2p.SetValue(null, true);
                server.SetValue(null, true);
                dedicated.SetValue(null, false);
                bool generationRetired = !ItemGenerationAuthorityAdapter.ShouldGenerateForObserver(null);
                bool replicationRetired = !ItemObserverReplicationAdapter.ShouldReplicateForObserver(null);
                dedicated.SetValue(null, true);
                bool u3dsRuns = ItemObserverReplicationAdapter.ShouldReplicateForObserver(null);
                return generationRetired && replicationRetired && u3dsRuns
                    && !MultiObserverShadowCoordinator.ItemAuthorityWriterOwnsSession;
            }
            finally
            {
                dedicated.SetValue(null, savedDedicated);
                server.SetValue(null, savedServer);
                p2p.SetValue(null, savedP2p);
                hostMode.SetValue(null, savedHost);
            }
        }
    }
}
