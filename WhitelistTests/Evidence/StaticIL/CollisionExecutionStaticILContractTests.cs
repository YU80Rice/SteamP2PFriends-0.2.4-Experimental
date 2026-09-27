using SteamP2PFriends.Adapters.Collision;
using SteamP2PFriends.Core.Identity;
using SteamP2PFriends.MultiObserver.Lifecycle;
using SteamP2PFriends.MultiObserver.SPI;
using System;
using System.Linq;
using System.Reflection;

namespace SteamP2PFriends.WhitelistTests
{
    internal static class CollisionExecutionStaticILContractTests
    {
        private const string Namespace = "SteamP2PFriends.Adapters.Collision";
        private const string PortName = Namespace + ".CollisionExecutionPort";
        private const string StoreName = Namespace + ".ICollisionOverrideStore";
        private const string ReceiptName = Namespace + ".CollisionAcquisitionReceipt";
        private const string PolicyName = Namespace + ".CollisionLifecyclePolicy";
        private const string LifecycleNamespace = "SteamP2PFriends.MultiObserver.Lifecycle";

        internal static bool Test_All()
        {
            return Test_PortShapeAndIdentity()
                && Test_PortHasNoRosterOrNativeScan()
                && Test_ReceiptAndOverrideDomain()
                && Test_ProductionStoreHasSessionBoundary()
                && Test_ProductionStoreReassertsOwnedOverrides()
                && Test_NoProductionRegistration();
        }

        /// <summary>
        /// 票 09 实机 S3 缺陷回归锁：生产 Store 必须在生命周期维护拍重申已拥有的原生覆盖——
        /// 原版按主机本地剔除策略反复停用远区物件（旧 Writer 时代由
        /// UpdateActiveAndRenderersEnabled postfix 每次刷新后翻回激活），
        /// 切换后 OnLifecycleTick 是唯一的持续再断言点。
        /// </summary>
        internal static bool Test_ProductionStoreReassertsOwnedOverrides()
        {
            MethodInfo tick = typeof(LevelObjectCollisionAdapter).GetMethod(
                "OnLifecycleTick", BindingFlags.Instance | BindingFlags.Public);
            if (tick == null) return false;
            int activate = IlContractProbe.CountMethodCalls(tick, "UnityEngine.GameObject", "SetActive");
            int culling = IlContractProbe.CountMemberReferences(tick,
                type => type != null && type.FullName == "UnityEngine.Animation");
            int ledger = IlContractProbe.CountMethodCalls(tick,
                "SteamP2PFriends.Adapters.Collision.LevelObjectCollisionLedger", "IsRegionActive");
            return activate >= 1 && culling >= 1 && ledger >= 1;
        }

        internal static bool Test_PortShapeAndIdentity()
        {
            Assembly assembly = typeof(SteamP2PFriendsPlugin).Assembly;
            Type port = assembly.GetType(PortName);
            Type store = assembly.GetType(StoreName);
            Type policy = assembly.GetType(PolicyName);
            return port != null && store != null && policy != null
                && typeof(IDomainExecutionPort).IsAssignableFrom(port)
                && port.GetProperty("LifecyclePolicy") != null
                && policy.GetMethod("Create", BindingFlags.Static | BindingFlags.Public) != null;
        }

        internal static bool Test_PortHasNoRosterOrNativeScan()
        {
            Type port = typeof(CollisionExecutionPort);
            int roster = IlContractProbe.SumOverMethodsInNamespace(
                port.Assembly, type => type == port, method =>
                    IlContractProbe.CountFieldLoads(method, "SDG.Unturned.Provider", "clients")
                    + IlContractProbe.CountMemberReferences(method,
                        type => type?.FullName == "SteamP2PFriends.MultiObserver.Spatial.SpatialObserverIndex"));
            int native = IlContractProbe.SumOverMethodsInNamespace(
                port.Assembly, type => type == port, method =>
                    IlContractProbe.CountMemberReferences(method, IsNativeOrUnity));
            int lifecycleDomainRefs = IlContractProbe.SumOverMethodsInNamespace(
                port.Assembly, type => type.Namespace == LifecycleNamespace, method =>
                    IlContractProbe.CountMemberReferences(method, type =>
                        (type?.Namespace ?? string.Empty).StartsWith(Namespace, StringComparison.Ordinal)));
            return roster == 0 && native == 0 && lifecycleDomainRefs == 0;
        }

        internal static bool Test_ReceiptAndOverrideDomain()
        {
            Type receipt = typeof(CollisionAcquisitionReceipt);
            Type overrideType = typeof(CollisionOverride);
            MethodInfo release = typeof(CollisionExecutionPort).GetMethods()
                .SingleOrDefault(method => method.Name == "TryRelease"
                    && method.GetParameters().Length == 1
                    && method.GetParameters()[0].ParameterType == typeof(LeaseTicket));
            MethodInfo receiptRelease = typeof(CollisionExecutionPort).GetMethods()
                .SingleOrDefault(method => method.Name == "TryRelease"
                    && method.GetParameters().Length == 2
                    && method.GetParameters()[1].ParameterType == typeof(CollisionAcquisitionReceipt));
            return receipt.GetProperty("DomainId") != null
                && receipt.GetProperty("SessionEpoch") != null
                && receipt.GetProperty("RegionGeneration") != null
                && receipt.GetProperty("AcquireGeneration") != null
                && receipt.GetProperty("Overrides") != null
                && overrideType.GetProperty("PluginOwned") != null
                && release != null
                && IlContractProbe.CountMethodCalls(release, StoreName, "TryRevokeOwnedAtomically") == 1
                && IlContractProbe.CountMethodCalls(release, StoreName, "TryRevoke") == 0
                && receiptRelease != null
                && IlContractProbe.CountMethodCalls(receiptRelease, PortName, "IsCurrentReceipt") == 1
                && IlContractProbe.CountMethodCalls(receiptRelease, StoreName, "TryRevoke") == 0;
        }

        internal static bool Test_ProductionStoreHasSessionBoundary()
        {
            Type store = typeof(LevelObjectCollisionAdapter);
            return typeof(ICollisionOverrideStore).IsAssignableFrom(store)
                && store.GetMethod("OnSessionBegin") != null
                && store.GetMethod("OnSessionEnd") != null
                && IlContractProbe.CountMethodCalls(
                    store.GetMethod("OnSessionBegin"),
                    "SteamP2PFriends.Adapters.Collision.LevelObjectCollisionLedger",
                    "BeginSession") == 1
                && IlContractProbe.CountMethodCalls(
                    store.GetMethod("OnSessionEnd"),
                    "SteamP2PFriends.Adapters.Collision.LevelObjectCollisionLedger",
                    "EndSession") == 1;
        }

        internal static bool Test_NoProductionRegistration()
        {
            Assembly assembly = typeof(SteamP2PFriendsPlugin).Assembly;
            int registrations = IlContractProbe.SumOverDeclaredMethods(assembly, method =>
                IlContractProbe.CountMethodCalls(method,
                    "SteamP2PFriends.MultiObserver.Lifecycle.LifecycleOrchestrationEngine", "Register"));
            int portRegistrations = IlContractProbe.SumOverMethodsInNamespace(
                assembly, type => type == typeof(CollisionExecutionPort), method =>
                    IlContractProbe.CountMethodCalls(method,
                        "SteamP2PFriends.MultiObserver.Lifecycle.LifecycleOrchestrationEngine", "Register"));
            return registrations == 2 && portRegistrations == 0
                && assembly.GetTypes().Count(type => type.FullName == PortName) == 1;
        }

        private static bool IsNativeOrUnity(Type type)
        {
            string space = type?.Namespace ?? string.Empty;
            return space.StartsWith("SDG.", StringComparison.Ordinal)
                || space.StartsWith("UnityEngine", StringComparison.Ordinal);
        }
    }
}
