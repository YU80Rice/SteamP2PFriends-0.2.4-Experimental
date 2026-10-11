using SteamP2PFriends.Adapters.Item;
using SteamP2PFriends.MultiObserver.Lifecycle;
using System;
using System.Linq;
using System.Reflection;

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

        internal static bool Test_All()
        {
            return Test_PortShapeAndIdentity()
                && Test_DemandPolicyDeclaration()
                && Test_PortHasNoRosterOrSpatialIndexScan()
                && Test_PreparationAuthorityAndMetadataStoreExist()
                && Test_ReceiptDomain()
                && Test_ProductionCoordinatorWiresItemDemandAndLifecycle()
                && Test_NativeActionInvocationsAndPreparationGate()
                && Test_CoordinatorCallsNativeBridgeAndPreparationChain()
                && Test_DirectConstructionAndRegistrationEvolutionContract();
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
            int dispatchCalls = bind == null ? 0 : IlContractProbe.CountMethodCalls(
                bind,
                "SteamP2PFriends.Adapters.Item.ItemObserverReplicationAdapter",
                "DispatchCandidateGuestAskItems");
            return prepCalls > 0 && hostPresentCalls > 0 && guestBindCalls > 0 && dispatchCalls >= 1;
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
            int usesCache = remove == null ? 0 : IlContractProbe.CountFieldLoads(
                remove, "SteamP2PFriends.Adapters.Item.DefaultNativeItemWarehouseBridge", "SendDestroyItemField");
            return native != null
                && native.Name == "SendDestroyItem"
                && native.DeclaringType == typeof(SDG.Unturned.ItemManager)
                && usesCache > 0;
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

        internal static bool Test_HostStepFiveGenerationUsesSettlementGate()
        {
            MethodInfo method = typeof(ItemGenerationAuthorityAdapter).GetMethod(
                "ShouldGenerateForObserver", BindingFlags.Static | BindingFlags.Public);
            int calls = method == null ? 0 : IlContractProbe.CountMethodCalls(
                method, "SteamP2PFriends.Adapters.Item.ItemSettlementGate", "TryAuthorizeHostGeneration");
            MethodInfo prefix = typeof(SteamP2PFriends.Adapters.Item.Patches.ItemManagerRegionSyncPatch).GetMethod(
                "OnRegionUpdated_Prefix", BindingFlags.Static | BindingFlags.Public);
            int context = prefix == null ? 0 : IlContractProbe.CountMethodCalls(
                prefix, "SteamP2PFriends.Adapters.Item.ItemSettlementGate", "SetRegionContext");
            return calls == 1 && context == 1;
        }
    }
}
