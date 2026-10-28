using SteamP2PFriends.Adapters.Item;
using SteamP2PFriends.MultiObserver.Lifecycle;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace SteamP2PFriends.WhitelistTests
{
    internal static class ItemExecutionStaticILContractTests
    {
        private const string Namespace = "SteamP2PFriends.Adapters.Item";
        private const string PortName = Namespace + ".ItemExecutionPort";
        private const string DemandPolicyName = Namespace + ".ItemDemandPolicy";
        private const string PrepName = Namespace + ".ItemPreparationAuthority";
        private const string StoreName = Namespace + ".ItemMetadataStore";
        private const string ReceiptName = Namespace + ".ItemAcquisitionReceipt";

        internal static bool Test_NIL02_LookupDoesNotCallNativeNullOperators()
        {
            MethodInfo find = typeof(DefaultNativeItemWarehouseBridge).GetMethod(
                "FindClient", BindingFlags.Static | BindingFlags.NonPublic);
            if (find == null) return false;
            int equality = IlContractProbe.CountMethodCalls(find, "SDG.Unturned.SteamPlayerID", "op_Equality");
            int inequality = IlContractProbe.CountMethodCalls(find, "SDG.Unturned.SteamPlayerID", "op_Inequality");
            Console.WriteLine($"NIL02 FindClient SteamPlayerID operators: == {equality}, != {inequality}");
            return equality == 0 && inequality == 0;
        }

        internal static bool Test_NIL03_BindAndUnbindShareLookupWithoutCatch()
        {
            Type bridge = typeof(DefaultNativeItemWarehouseBridge);
            MethodInfo find = bridge.GetMethod("FindClient", BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo bind = bridge.GetMethod("BindGuestBaseline");
            MethodInfo unbind = bridge.GetMethod("UnbindGuestBaseline");
            MethodInfo bindCore = bridge.GetMethod("BindGuestBaselineCore", BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo unbindCore = bridge.GetMethod("UnbindGuestBaselineCore", BindingFlags.Instance | BindingFlags.NonPublic);
            if (find == null || bind == null || unbind == null || bindCore == null || unbindCore == null) return false;
            int bindLookups = IlContractProbe.CountMethodCalls(bindCore, bridge.FullName, "FindClient");
            int unbindLookups = IlContractProbe.CountMethodCalls(unbindCore, bridge.FullName, "FindClient");
            int coreCalls = IlContractProbe.CountMethodCalls(bind, bridge.FullName, "BindGuestBaselineCore")
                + IlContractProbe.CountMethodCalls(unbind, bridge.FullName, "UnbindGuestBaselineCore");
            int handlers = find.GetMethodBody().ExceptionHandlingClauses.Count
                + bind.GetMethodBody().ExceptionHandlingClauses.Count
                + unbind.GetMethodBody().ExceptionHandlingClauses.Count
                + bindCore.GetMethodBody().ExceptionHandlingClauses.Count
                + unbindCore.GetMethodBody().ExceptionHandlingClauses.Count;
            Console.WriteLine($"NIL03 core FindClient calls bind={bindLookups}, unbind={unbindLookups}; wrappers={coreCalls}; handlers={handlers}");
            return bindLookups == 1 && unbindLookups == 1 && coreCalls == 2 && handlers == 0;
        }

        internal static bool Test_All()
        {
            return Test_PortShapeAndIdentity()
                && Test_DemandPolicyDeclaration()
                && Test_PortHasNoRosterOrSpatialIndexScan()
                && Test_PreparationAuthorityAndMetadataStoreExist()
                && Test_ReceiptDomain()
                && Test_ProductionCoordinatorWiresItemDemandAndLifecycle()
                && Test_NativeActionInvocationsAndPreparationGate()
                && Test_GuestBindingAdmissionWiring()
                && Test_CoordinatorCallsNativeBridgeAndPreparationChain()
                && Test_DirectConstructionAndRegistrationEvolutionContract()
                && Test_SessionBoundaryRetiresLegacyItemWriter()
                && Test_ProductionFrameProgressWiring();
        }

        internal static bool Test_ProductionFrameProgressWiring()
        {
            Type coordinator = typeof(SteamP2PFriendsPlugin).Assembly.GetType(
                "SteamP2PFriends.MultiObserver.MultiObserverShadowCoordinator");
            MethodInfo tick = coordinator?.GetMethod("Tick", BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo frame = coordinator?.GetMethod("RunActiveFrame", BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo reconcile = coordinator?.GetMethod("ReconcileCurrentObservers", BindingFlags.Static | BindingFlags.NonPublic);
            if (tick == null || frame == null || reconcile == null) return false;
            int frameCalls = IlContractProbe.CountMethodCalls(tick, coordinator.FullName, "RunActiveFrame");
            int callbackCalls = IlContractProbe.CountCallsWhere(frame, called => called.Name == "Invoke");
            int productionReconcileCalls = IlContractProbe.CountAssemblyMethodCalls(
                coordinator.Assembly, coordinator.FullName, "ReconcileCurrentObservers");
            int clockCalls = IlContractProbe.CountMethodCalls(frame, coordinator.FullName, "AdvanceEngineClock");
            int sharedFlushes = IlContractProbe.CountMethodCalls(reconcile, "SteamP2PFriends.MultiObserver.Lifecycle.LifecycleOrchestrationEngine", "Flush");
            int itemFlushes = IlContractProbe.CountMethodCalls(reconcile, "SteamP2PFriends.Adapters.Item.ItemProductionControlSeam", "Flush");
            Console.WriteLine($"SFP IL frame={frameCalls} callback={callbackCalls} reconcile={productionReconcileCalls} clock={clockCalls} sharedFlush={sharedFlushes} itemFlush={itemFlushes}");
            return frameCalls == 1 && callbackCalls == 1 && productionReconcileCalls == 1 && clockCalls == 1
                && sharedFlushes == 1 && itemFlushes == 0;
        }

        internal static bool Test_PortShapeAndIdentity()
        {
            Assembly assembly = typeof(SteamP2PFriendsPlugin).Assembly;
            Type port = assembly.GetType(PortName);
            return port != null
                && typeof(IDomainExecutionPort).IsAssignableFrom(port)
                && port.GetProperty("LifecyclePolicy") != null
                && port.GetProperty("PreparationAuthority") != null
                && port.GetProperty("MetadataStore") != null;
        }

        internal static bool Test_DemandPolicyDeclaration()
        {
            Assembly assembly = typeof(SteamP2PFriendsPlugin).Assembly;
            Type policy = assembly.GetType(DemandPolicyName);
            return policy != null
                && policy.GetMethod("Create", BindingFlags.Static | BindingFlags.Public) != null
                && policy.GetField("RadiusSource", BindingFlags.Static | BindingFlags.Public) != null;
        }

        internal static bool Test_PortHasNoRosterOrSpatialIndexScan()
        {
            Type port = typeof(ItemExecutionPort);
            int roster = IlContractProbe.SumOverMethodsInNamespace(
                port.Assembly, type => type == port, method =>
                    IlContractProbe.CountFieldLoads(method, "SDG.Unturned.Provider", "clients")
                    + IlContractProbe.CountMemberReferences(method,
                        type => type?.FullName == "SteamP2PFriends.MultiObserver.Spatial.SpatialObserverIndex"));
            return roster == 0;
        }

        internal static bool Test_PreparationAuthorityAndMetadataStoreExist()
        {
            Assembly assembly = typeof(SteamP2PFriendsPlugin).Assembly;
            return assembly.GetType(PrepName) != null && assembly.GetType(StoreName) != null;
        }

        internal static bool Test_ReceiptDomain()
        {
            Assembly assembly = typeof(SteamP2PFriendsPlugin).Assembly;
            Type receipt = assembly.GetType(ReceiptName);
            Type port = assembly.GetType(PortName);
            return receipt != null && port != null
                && receipt.GetProperty("DomainId") != null
                && receipt.GetProperty("RegionKey") != null
                && receipt.GetProperty("SessionEpoch") != null
                && receipt.GetProperty("RegionGeneration") != null
                && receipt.GetProperty("AcquireGeneration") != null
                && receipt.GetProperty("ActivationTime") != null
                && receipt.GetProperty("Valid") != null
                && port.GetMethod("TryAcquire") != null
                && port.GetMethod("TryRelease", new[] { typeof(SteamP2PFriends.MultiObserver.SPI.LeaseTicket) }) != null
                && port.GetMethod("TryRelease", new[] { typeof(SteamP2PFriends.MultiObserver.SPI.LeaseTicket), receipt }) != null;
        }

        internal static bool Test_ProductionCoordinatorWiresItemDemandAndLifecycle()
        {
            Assembly assembly = typeof(SteamP2PFriendsPlugin).Assembly;
            Type coordinator = assembly.GetType("SteamP2PFriends.MultiObserver.MultiObserverShadowCoordinator");
            if (coordinator == null) return false;

            FieldInfo itemPolicyField = coordinator.GetField("_itemDemandPolicy", BindingFlags.NonPublic | BindingFlags.Static);
            FieldInfo itemPrepField = coordinator.GetField("ItemPreparation", BindingFlags.NonPublic | BindingFlags.Static);
            FieldInfo itemPortField = coordinator.GetField("ItemExecution", BindingFlags.NonPublic | BindingFlags.Static);
            FieldInfo itemSeamField = coordinator.GetField("ItemProduction", BindingFlags.NonPublic | BindingFlags.Static);

            return itemPolicyField != null && itemPrepField != null && itemPortField != null && itemSeamField != null;
        }

        internal static bool Test_GuestBindingAdmissionWiring()
        {
            Assembly assembly = typeof(SteamP2PFriendsPlugin).Assembly;
            Type coordinator = assembly.GetType("SteamP2PFriends.MultiObserver.MultiObserverShadowCoordinator");
            Type bridge = assembly.GetType(Namespace + ".DefaultNativeItemWarehouseBridge");
            Type seam = assembly.GetType(Namespace + ".ItemProductionControlSeam");
            MethodInfo snapshot = coordinator?.GetMethod("ReadCommittedItemBinding", BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo epoch = coordinator?.GetMethod("ReadCurrentItemBindingEpoch", BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo configure = seam?.GetMethod("ConfigureGuestBindingBridge", BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo bindCore = bridge?.GetMethod("BindGuestBaselineCore", BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo unbindCore = bridge?.GetMethod("UnbindGuestBaselineCore", BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo productionConfigure = coordinator?.GetMethod("ConfigureItemControlPlane", BindingFlags.Static | BindingFlags.NonPublic);
            if (snapshot == null || epoch == null || configure == null || bindCore == null
                || unbindCore == null || productionConfigure == null) return false;
            int readerUsesCommittedConnection = IlContractProbe.CountMethodCalls(snapshot,
                coordinator.FullName, "TryReadCommittedItemConnection");
            ConstructorInfo productionConstructor = seam.GetConstructors().SingleOrDefault();
            int productionReaders = productionConstructor == null ? 0
                : IlContractProbe.CountMethodCalls(productionConstructor,
                    coordinator.FullName, "ReadCurrentItemBindingEpoch")
                  + IlContractProbe.CountMethodCalls(productionConstructor,
                    coordinator.FullName, "ReadCommittedItemBinding");
            int coreEvaluate = IlContractProbe.CountMethodCalls(bindCore,
                Namespace + ".ItemGuestBindingAdmission", "Evaluate")
                + IlContractProbe.CountMethodCalls(unbindCore,
                    Namespace + ".ItemGuestBindingAdmission", "Evaluate");
            bool constructorWired = IlContractProbe.CountMethodCalls(productionConfigure, Namespace + ".ItemProductionControlSeam", ".ctor") == 1;
            return readerUsesCommittedConnection == 1 && productionReaders == 2
                && constructorWired
                && coreEvaluate == 2
                && IlContractProbe.CountMethodCalls(bindCore,
                    "SteamP2PFriends.Adapters.Item.ItemObserverReplicationAdapter",
                    "DispatchCandidateGuestAskItems") == 1;
        }

        internal static bool Test_NativeActionInvocationsAndPreparationGate()
        {
            Assembly assembly = typeof(SteamP2PFriendsPlugin).Assembly;
            Type prep = assembly.GetType(PrepName);
            Type port = assembly.GetType(PortName);
            if (prep == null || port == null) return false;

            MethodInfo prepareFullMap = prep.GetMethod("PrepareFullMap");
            MethodInfo canOperate = prep.GetMethod("CanPerformOperation");
            PropertyInfo hostPresent = port.GetProperty("HostPresentationCount");
            PropertyInfo guestBind = port.GetProperty("GuestBaselineBindingCount");

            return prepareFullMap != null && canOperate != null && hostPresent != null && guestBind != null;
        }

        internal static bool Test_CoordinatorCallsNativeBridgeAndPreparationChain()
        {
            Assembly assembly = typeof(SteamP2PFriendsPlugin).Assembly;
            Type coordinator = assembly.GetType("SteamP2PFriends.MultiObserver.MultiObserverShadowCoordinator");
            if (coordinator == null) return false;

            int prepCalls = IlContractProbe.CountAssemblyMethodCalls(
                assembly, "SteamP2PFriends.Adapters.Item.ItemPreparationAuthority", "PrepareFullMap");
            int hostPresentCalls = IlContractProbe.CountAssemblyMethodCalls(
                assembly, "SteamP2PFriends.Adapters.Item.INativeItemWarehouseBridge", "PresentRegionToHost");
            int guestBindCalls = IlContractProbe.CountAssemblyMethodCalls(
                assembly, "SteamP2PFriends.Adapters.Item.INativeItemWarehouseBridge", "BindGuestBaseline");
            MethodInfo bind = assembly.GetType("SteamP2PFriends.Adapters.Item.DefaultNativeItemWarehouseBridge")
                ?.GetMethod("BindGuestBaseline");
            MethodInfo bindCore = assembly.GetType("SteamP2PFriends.Adapters.Item.DefaultNativeItemWarehouseBridge")
                ?.GetMethod("BindGuestBaselineCore", BindingFlags.Instance | BindingFlags.NonPublic);
            int dispatchCalls = bind == null ? 0 : IlContractProbe.CountMethodCalls(
                bindCore,
                "SteamP2PFriends.Adapters.Item.ItemObserverReplicationAdapter",
                "DispatchCandidateGuestAskItems");
            int coreRoute = bind == null ? 0 : IlContractProbe.CountMethodCalls(bind,
                "SteamP2PFriends.Adapters.Item.DefaultNativeItemWarehouseBridge", "BindGuestBaselineCore");
            return prepCalls > 0 && hostPresentCalls > 0 && guestBindCalls > 0
                && coreRoute == 1 && dispatchCalls == 1;
        }

        internal static bool Test_DirectConstructionAndRegistrationEvolutionContract()
        {
            Assembly assembly = typeof(SteamP2PFriendsPlugin).Assembly;
            Type itemPolicyType = assembly.GetType("SteamP2PFriends.Adapters.Item.ItemLifecyclePolicy");
            MethodInfo createMethod = itemPolicyType?.GetMethod("Create", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            Type seamType = assembly.GetType("SteamP2PFriends.Adapters.Item.ItemProductionControlSeam");
            ConstructorInfo[] constructors = seamType?.GetConstructors();

            int directPolicyCtors = createMethod != null
                ? IlContractProbe.CountMethodCalls(createMethod, "SteamP2PFriends.MultiObserver.Lifecycle.LifecyclePolicy", ".ctor")
                : 0;

            int directEngineRegisters = constructors != null
                ? constructors.Sum(c => IlContractProbe.CountCallsWhere(c,
                    called => called.DeclaringType?.FullName == "SteamP2PFriends.MultiObserver.Lifecycle.LifecycleOrchestrationEngine" && called.Name == "Register"))
                : 0;

            return directPolicyCtors == 1 && directEngineRegisters == 1;
        }

        internal static bool Test_DemandCycleBindsNativeRespawnAndSuppressesLegacyWriter()
        {
            Assembly assembly = typeof(SteamP2PFriendsPlugin).Assembly;
            Type bridge = assembly.GetType("SteamP2PFriends.Adapters.Item.DefaultNativeItemWarehouseBridge");
            FieldInfo respawn = bridge?.GetField("RespawnItemsMethod", BindingFlags.NonPublic | BindingFlags.Static);
            MethodInfo bound = respawn?.GetValue(null) as MethodInfo;
            bool nativeBound = bound != null
                && bound.Name == "respawnItems"
                && bound.DeclaringType == typeof(SDG.Unturned.ItemManager);

            Type patch = assembly.GetType("SteamP2PFriends.Adapters.Item.Patches.ItemManagerUpdateDedicatedGatePatch");
            MethodInfo skip = patch?.GetMethod("SkipLegacyCycleWhenCandidateOwnsSession",
                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
            int gateLoads = skip == null ? 0 : IlContractProbe.CountMethodCalls(
                skip, "SteamP2PFriends.Adapters.Item.ItemLegacyCycleGate", "get_ShouldSkipLegacy");

            int suppressSets = IlContractProbe.CountAssemblyMethodCalls(
                assembly,
                "SteamP2PFriends.Adapters.Item.ItemLegacyCycleGate",
                "set_SuppressFullMapWriter");
            return nativeBound && gateLoads > 0 && suppressSets > 0;
        }

        internal static bool Test_DemandCycleRemovalInvokesNativeDestroy()
        {
            Assembly assembly = typeof(SteamP2PFriendsPlugin).Assembly;
            Type bridge = assembly.GetType("SteamP2PFriends.Adapters.Item.DefaultNativeItemWarehouseBridge");
            FieldInfo cached = bridge?.GetField("SendDestroyItemField", BindingFlags.NonPublic | BindingFlags.Static);
            FieldInfo native = cached?.GetValue(null) as FieldInfo;
            MethodInfo remove = bridge?.GetMethod("TryRemoveItem");
            int dispatchCalls = remove == null ? 0 : IlContractProbe.CountMethodCalls(
                remove, "SteamP2PFriends.Adapters.Item.ItemDeletionDispatch", "Remove");
            return native != null
                && native.Name == "SendDestroyItem"
                && native.DeclaringType == typeof(SDG.Unturned.ItemManager)
                && dispatchCalls == 1;
        }

        internal static bool Test_SettlementConsumptionBindsPickupIncrementAndHost()
        {
            Assembly assembly = typeof(SteamP2PFriendsPlugin).Assembly;
            Type patch = assembly.GetType("SteamP2PFriends.Adapters.Item.Patches.ItemSettlementConsumptionPatch");
            MethodInfo pickup = patch?.GetMethod("AllowPickup", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            MethodInfo increment = patch?.GetMethod("AllowIncrement", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            MethodInfo ask = patch?.GetMethod("AllowAskItems", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            int pickupCalls = pickup == null ? 0 : IlContractProbe.CountMethodCalls(
                pickup, "SteamP2PFriends.Adapters.Item.ItemSettlementGate", "TryAuthorizePickup");
            int incrementCalls = increment == null ? 0 : IlContractProbe.CountMethodCalls(
                increment, "SteamP2PFriends.Adapters.Item.ItemSettlementGate", "TryAuthorizeIncrement");
            int askCalls = ask == null ? 0 : IlContractProbe.CountMethodCalls(
                ask, "SteamP2PFriends.Adapters.Item.ItemSettlementGate", "TryAuthorizeIncrement");
            MethodInfo present = assembly.GetType("SteamP2PFriends.Adapters.Item.DefaultNativeItemWarehouseBridge")
                ?.GetMethod("PresentRegionToHost");
            int hostCalls = present == null ? 0 : IlContractProbe.CountMethodCalls(
                present, "SteamP2PFriends.Adapters.Item.ItemSettlementGate", "TryAuthorizeIncrement");
            MethodInfo register = patch?.GetMethod("RegisterManual", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            bool bindsPickup = register != null
                && IlContractProbe.ContainsStringLiteral(register, "ReceiveTakeItemRequest")
                && IlContractProbe.ContainsStringLiteral(register, "askItems")
                && IlContractProbe.ContainsStringLiteral(register, "Invoke");
            return patch != null && pickupCalls > 0 && incrementCalls > 0 && askCalls > 0 && hostCalls > 0 && bindsPickup;
        }

        /// <summary>E02 §6/D：pending 窗口识别与 transpiler 共用同一结果（真实 Update IL）。</summary>
        internal static bool Test_E02_D2_PendingWindowSharedDetection()
        {
            MethodInfo update = AccessTools.Method(
                typeof(SDG.Unturned.ItemManager), "Update", Type.EmptyTypes);
            if (update == null) return false;
            var codes = PatchProcessor.GetCurrentInstructions(update, out _, maxTranspilers: 0);
            var probe = typeof(SteamP2PFriends.Adapters.Item.Patches.ItemOwnedPresentationRuntimePatch);
            var find = probe.GetMethods(System.Reflection.BindingFlags.Static
                    | System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Public)
                .Single(m => m.Name == "TryFindPendingBatchWindow"
                    && m.GetParameters().Length == 3);
            if (find == null) return false;
            var args = new object[] { codes, -1, -1 };
            bool found = (bool)find.Invoke(null, args);
            // 真实客户端 Update：唯一 pending 窗口（timer Restart..Stop），锚点唯一
            return found && (int)args[1] > 0 && (int)args[2] > (int)args[1];
        }

        internal static bool Test_HostStepFiveGenerationUsesSettlementGate()
        {
            MethodInfo method = typeof(ItemGenerationAuthorityAdapter).GetMethod(
                "ShouldGenerateForObserver", BindingFlags.Static | BindingFlags.Public);
            int calls = method == null ? 0 : IlContractProbe.CountMethodCalls(
                method, "SteamP2PFriends.Adapters.Item.ItemSettlementGate", "TryAuthorizeHostGeneration");
            MethodInfo prefix = typeof(SteamP2PFriends.Adapters.Item.Patches.ItemManagerRegionSyncPatch).GetMethod(
                "OnRegionUpdated_Prefix", BindingFlags.Static | BindingFlags.Public);
            MethodInfo owned = typeof(SteamP2PFriends.Adapters.Item.ItemLocalPresentationDispatch).GetMethod(
                "AdmitOwnedStepFive", BindingFlags.Static | BindingFlags.Public);
            MethodInfo legacy = typeof(SteamP2PFriends.Adapters.Item.Patches.ItemManagerRegionSyncPatch).GetMethod(
                "AdmitUnverified", BindingFlags.Static | BindingFlags.NonPublic);
            int ownedAdmit = owned == null ? 0 : IlContractProbe.CountMethodCalls(
                owned, "SteamP2PFriends.Adapters.Item.ItemStepFiveAdmission", "Admit");
            int legacyAdmit = legacy == null ? 0 : IlContractProbe.CountMethodCalls(
                legacy, "SteamP2PFriends.Adapters.Item.ItemStepFiveAdmission", "Admit");
            MethodInfo handler = typeof(SteamP2PFriends.Adapters.Item.Patches.ItemManagerRegionSyncPatch)
                .GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
                .FirstOrDefault(m => m.Name == "HandleNativeStepFive" && m.GetParameters().Length == 6);
            int prefixOwned = handler == null ? 0 : IlContractProbe.CountMethodCalls(
                handler, "SteamP2PFriends.Adapters.Item.ItemLocalPresentationDispatch", "AdmitOwnedStepFive");
            int prefixHandler = prefix == null ? 0 : IlContractProbe.CountMethodCalls(
                prefix, "SteamP2PFriends.Adapters.Item.Patches.ItemManagerRegionSyncPatch", "HandleNativeStepFive");
            return calls == 1 && ownedAdmit == 1 && legacyAdmit == 1 && prefixOwned == 1
                && prefixHandler == 1;
        }

        internal static bool Test_StepFiveAndDestroyConsumeGatesAreBound()
        {
            MethodInfo prefix = typeof(SteamP2PFriends.Adapters.Item.Patches.ItemManagerRegionSyncPatch)
                .GetMethod("OnRegionUpdated_Prefix", BindingFlags.Static | BindingFlags.Public);
            MethodInfo ownedAdmit = typeof(SteamP2PFriends.Adapters.Item.ItemLocalPresentationDispatch)
                .GetMethod("AdmitOwnedStepFive", BindingFlags.Static | BindingFlags.Public);
            MethodInfo legacyAdmit = typeof(SteamP2PFriends.Adapters.Item.Patches.ItemManagerRegionSyncPatch)
                .GetMethod("AdmitUnverified", BindingFlags.Static | BindingFlags.NonPublic);
            int stepGate = (ownedAdmit == null ? 0 : IlContractProbe.CountMethodCalls(
                    ownedAdmit, "SteamP2PFriends.Adapters.Item.ItemStepFiveAdmission", "Admit"))
                + (legacyAdmit == null ? 0 : IlContractProbe.CountMethodCalls(
                    legacyAdmit, "SteamP2PFriends.Adapters.Item.ItemStepFiveAdmission", "Admit"));
            int nativeScope = prefix == null ? 0 : IlContractProbe.CountMethodCalls(
                prefix, "SteamP2PFriends.Adapters.Item.ItemObserverReplicationAdapter", "EnterNativeStepFiveAskItems");
            MethodInfo finalizer = typeof(SteamP2PFriends.Adapters.Item.Patches.ItemManagerRegionSyncPatch)
                .GetMethod("OnRegionUpdated_Finalizer", BindingFlags.Static | BindingFlags.Public);
            int destroyGate = IlContractProbe.CountAssemblyMethodCalls(
                typeof(SteamP2PFriendsPlugin).Assembly,
                "SteamP2PFriends.Adapters.Item.ItemSettlementGate", "TryAuthorizeIncrement");
            return stepGate == 2 && nativeScope == 1 && finalizer != null && destroyGate > 0;
        }

        internal static bool Test_RegionPrefixRegistrationIncludesOrderedFinalizer()
        {
            MethodInfo register = typeof(SteamP2PFriends.Adapters.Item.Patches.ItemManagerRegionSyncPatch)
                .GetMethod("RegisterRegionPrefix", BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo finalizer = typeof(SteamP2PFriends.Adapters.Item.Patches.ItemManagerRegionSyncPatch)
                .GetMethod("OnRegionUpdated_Finalizer", BindingFlags.Static | BindingFlags.Public);
            int patchCalls = IlContractProbe.CountMethodCalls(register, "HarmonyLib.Harmony", "Patch");
            int harmonyMethods = IlContractProbe.CountCallsWhere(register, called =>
                called.DeclaringType?.FullName == "HarmonyLib.HarmonyMethod" && called.Name == ".ctor");
            int verificationCalls = IlContractProbe.CountMethodCalls(register,
                "SteamP2PFriends.Adapters.Item.Patches.ItemManagerRegionSyncPatch", "VerifyExactPatch");
            return register != null && finalizer != null && patchCalls == 2 && harmonyMethods == 2
                && verificationCalls == 4
                && IlContractProbe.ContainsStringLiteral(register, "OnRegionUpdated_Finalizer")
                && IlContractProbe.ContainsIntegerLiteral(register, Priority.First)
                && IlContractProbe.ContainsIntegerLiteral(register, Priority.Last);
        }

        /// <summary>R2C 补齐结构证据：实际 ReceiveDestroyItem_Prefix 真正 mode-first——
        /// ReadMode 调用 offset（实测 6）在 pendingInstantiations 的 Type.GetField/
        /// FieldInfo.GetValue（42/55）与 NativeItemLocalPresentationView 构造（79）
        /// 之前；nonCandidate 提前 return 不触 owned 静态面/视图。ReceiveItem_Prefix
        /// 无 owned 存储读取（GetField/GetValue=0），dispatch 在 view 构造之后。</summary>
        internal static bool Test_DestroyWrapperReadsModeBeforeOwnedState()
        {
            const BindingFlags All = BindingFlags.Static | BindingFlags.Public;
            MethodInfo destroy = typeof(SteamP2PFriends.Adapters.Item.Patches.ItemOwnedPresentationRuntimePatch)
                .GetMethod("ReceiveDestroyItem_Prefix", All);
            MethodInfo increment = typeof(SteamP2PFriends.Adapters.Item.Patches.ItemOwnedPresentationRuntimePatch)
                .GetMethod("ReceiveItem_Prefix", All);
            if (destroy == null || increment == null) return false;
            int FindCallOffset(MethodInfo method, string declaringTypeName, string name)
            {
                byte[] il = method.GetMethodBody()?.GetILAsByteArray();
                if (il == null) return -1;
                for (int offset = 0; offset + 4 < il.Length; offset++)
                {
                    int token = BitConverter.ToInt32(il, offset);
                    try
                    {
                        MethodBase called = method.Module.ResolveMethod(token);
                        if (called != null
                            && called.DeclaringType?.FullName == declaringTypeName
                            && called.Name == name)
                            return offset;
                    }
                    catch { }
                }
                return -1;
            }
            int destroyReadMode = FindCallOffset(destroy,
                "SteamP2PFriends.Adapters.Item.NativeItemStepFiveReader", "ReadMode");
            int destroyGetField = FindCallOffset(destroy, "System.Type", "GetField");
            int destroyGetValue = FindCallOffset(destroy,
                "System.Reflection.FieldInfo", "GetValue");
            int destroyViewCtor = FindCallOffset(destroy,
                "SteamP2PFriends.Adapters.Item.NativeItemLocalPresentationView", ".ctor");
            // ReceiveItem_Prefix：无 owned 存储读取；dispatch 在 view 构造之后
            int incrementGetField = FindCallOffset(increment, "System.Type", "GetField");
            int incrementGetValue = FindCallOffset(increment,
                "System.Reflection.FieldInfo", "GetValue");
            int incrementViewCtor = FindCallOffset(increment,
                "SteamP2PFriends.Adapters.Item.NativeItemLocalPresentationView", ".ctor");
            int incrementDispatch = FindCallOffset(increment,
                "SteamP2PFriends.Adapters.Item.ItemLocalPresentationDispatch", "DispatchIncrement");
            return destroyReadMode >= 0
                && destroyGetField > destroyReadMode
                && destroyGetValue > destroyReadMode
                && (destroyViewCtor < 0 || destroyViewCtor > destroyReadMode)
                && incrementGetField < 0 && incrementGetValue < 0
                && incrementViewCtor >= 0 && incrementDispatch > incrementViewCtor;
        }

        /// <summary>R2B 补齐结构证据：实际 OnRegionUpdated_Prefix 中 non-Candidate 的
        /// HandleNativeStepFive 调用（私有 3 参重载）不在任何异常处理区间内——异常按
        /// 原合同向上传播；方法体仅存 owned 局部 catch 一个 handler。</summary>
        internal static bool Test_RegionPrefixLegacyCallPropagatesExceptions()
        {
            MethodInfo prefix = typeof(SteamP2PFriends.Adapters.Item.Patches.ItemManagerRegionSyncPatch)
                .GetMethod("OnRegionUpdated_Prefix", BindingFlags.Static | BindingFlags.Public);
            if (prefix == null) return false;
            MethodInfo ownedHandler = typeof(SteamP2PFriends.Adapters.Item.Patches.ItemManagerRegionSyncPatch)
                .GetMethod("HandleOwnedStepFive", BindingFlags.Static | BindingFlags.NonPublic);
            if (ownedHandler == null) return false;
            System.Reflection.MethodBody body = prefix.GetMethodBody();
            if (body == null) return false;
            // 唯一异常 handler = owned 局部 catch（原 non-Candidate catch 已移除）
            if (body.ExceptionHandlingClauses.Count != 1) return false;
            // 找到 non-Candidate 腿的 HandleNativeStepFive(3参) 调用 offset
            byte[] il = body.GetILAsByteArray();
            int legacyCallOffset = -1;
            for (int offset = 0; offset + 4 < il.Length; offset++)
            {
                int token = BitConverter.ToInt32(il, offset);
                try
                {
                    MethodBase called = prefix.Module.ResolveMethod(token);
                    if (called != null && called.Name == "HandleNativeStepFive"
                        && called.GetParameters().Length == 3)
                    {
                        legacyCallOffset = offset;
                        break;
                    }
                }
                catch { }
            }
            if (legacyCallOffset < 0) return false;
            foreach (System.Reflection.ExceptionHandlingClause clause in body.ExceptionHandlingClauses)
                if (legacyCallOffset >= clause.TryOffset
                    && legacyCallOffset < clause.TryOffset + clause.TryLength)
                    return false;
            return true;
        }

        internal static bool Test_SessionBoundaryRetiresLegacyItemWriter()
        {
            Assembly assembly = typeof(SteamP2PFriendsPlugin).Assembly;
            Type coordinator = assembly.GetType("SteamP2PFriends.MultiObserver.MultiObserverShadowCoordinator");
            MethodInfo tick = coordinator?.GetMethod("Tick", BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo configure = coordinator?.GetMethod("ConfigureItemControlPlane", BindingFlags.Static | BindingFlags.NonPublic);
            int beginCalls = tick == null ? 0 : IlContractProbe.CountMethodCalls(
                tick, "SteamP2PFriends.MultiObserver.MultiObserverShadowCoordinator", "BeginItemAuthoritySession");
            int bindCalls = configure == null ? 0 : IlContractProbe.CountMethodCalls(
                configure, "SteamP2PFriends.MultiObserver.MultiObserverShadowCoordinator", "BindItemAuthorityProduction");
            MethodInfo generate = typeof(ItemGenerationAuthorityAdapter).GetMethod("ShouldGenerateForObserver");
            MethodInfo replicate = typeof(ItemObserverReplicationAdapter).GetMethod("ShouldReplicateForObserver");
            int generateGates = generate == null ? 0 : IlContractProbe.CountMethodCalls(
                generate, "SteamP2PFriends.MultiObserver.MultiObserverShadowCoordinator", "get_ItemAuthorityWriterOwnsSession");
            int replicateGates = replicate == null ? 0 : IlContractProbe.CountMethodCalls(
                replicate, "SteamP2PFriends.MultiObserver.MultiObserverShadowCoordinator", "get_ItemAuthorityWriterOwnsSession");
            MethodInfo regenerate = typeof(SteamP2PFriends.Adapters.Item.Patches.ItemManagerP0B6RegenerateOnLevelLoadedPatch)
                .GetMethod("TryRegenerateOnLevelLoaded");
            int oldFullMap = regenerate == null ? 1 : IlContractProbe.CountMethodCalls(
                regenerate, "SteamP2PFriends.Adapters.Item.Patches.ItemManagerP0B6RegenerateOnLevelLoadedPatch", "RegenerateAllItems");
            return beginCalls == 1 && bindCalls == 1 && generateGates == 1 && replicateGates == 1 && oldFullMap == 0;
        }

        internal static bool Test_SettlementRegistrationVerifiedThroughPatchInfo()
        {
            MethodInfo register = typeof(SteamP2PFriends.Adapters.Item.Patches.ItemSettlementConsumptionPatch)
                .GetMethod("RegisterManual");
            MethodInfo verify = typeof(SteamP2PFriends.Adapters.Item.Patches.ItemSettlementConsumptionPatch)
                .GetMethod("VerifyRegistered", BindingFlags.NonPublic | BindingFlags.Static);
            if (register == null || verify == null) return false;
            int verifiedTargets = IlContractProbe.CountMethodCalls(
                register, "SteamP2PFriends.Adapters.Item.Patches.ItemSettlementConsumptionPatch", "VerifyRegistered");
            int patchInfoReads = IlContractProbe.CountMethodCalls(verify, "HarmonyLib.Harmony", "GetPatchInfo");
            return verifiedTargets == 3 && patchInfoReads == 1;
        }

        internal static bool Test_OwnedPendingTranspilerBindsAfterDedicatedGateChain()
        {
            MethodInfo update = AccessTools.Method(
                typeof(SDG.Unturned.ItemManager), "Update", Type.EmptyTypes);
            if (update == null) return false;
            var generator = new DynamicMethod("it115", typeof(void), Type.EmptyTypes).GetILGenerator();
            var chained = new List<CodeInstruction>(
                SteamP2PFriends.Adapters.Item.Patches.ItemOwnedPresentationRuntimePatch.Update_Transpiler(
                    SteamP2PFriends.Adapters.Item.Patches.ItemManagerUpdateDedicatedGatePatch.Update_Transpiler(
                        PatchProcessor.GetCurrentInstructions(update, out _, maxTranspilers: 0), null),
                    generator));
            if (chained.Count == 0) return false;
            if (SteamP2PFriends.Adapters.Item.Patches.ItemOwnedPresentationRuntimePatch.AnchorMatchCount != 1) return false;
            int consume = 0;
            int spawn = 0;
            foreach (CodeInstruction instruction in chained)
            {
                MethodInfo called = instruction.operand as MethodInfo;
                if (called == null) continue;
                if (called.DeclaringType == typeof(SteamP2PFriends.Adapters.Item.Patches.ItemOwnedPresentationRuntimePatch)
                    && called.Name == "ConsumeOwnedPendingBatch") consume++;
                if (called.Name == "spawnItem") spawn++;
            }
            return consume == 1 && spawn == 1;
        }
    }
}
