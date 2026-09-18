using SteamP2PFriends.Adapters.Resource;
using System;
using System.Reflection;

namespace SteamP2PFriends.WhitelistTests
{
    /// <summary>
    /// Ticket 02 的结构门禁：Control Plane 维护唯一的 World Presence Observer 空间事实，
    /// 共享 Demand Projection Engine 消费该权威事实并按领域 Demand Policy 投影，
    /// Resource 生产接缝不再持有自己的观察者索引，Resource 仍是唯一生产 Writer。
    /// 断言只读元数据与 IL，不把静态结构升级成 Runtime 证据。
    /// </summary>
    internal static class DemandProjectionStaticILContractTests
    {
        private const string DemandNamespace = "SteamP2PFriends.MultiObserver.Demand";
        private const string AuthorityTypeName = DemandNamespace + ".ObserverSpatialAuthority";
        private const string EngineTypeName = DemandNamespace + ".DemandProjectionEngine";
        private const string PolicyTypeName = DemandNamespace + ".DemandPolicy";
        private const string DemandTypeName = DemandNamespace + ".DomainDemand";
        private const string ProjectionTypeName = DemandNamespace + ".DomainDemandProjection";
        private const string SpatialIndexTypeName = "SteamP2PFriends.MultiObserver.Spatial.SpatialObserverIndex";
        private const string SeamTypeName = "SteamP2PFriends.Adapters.Resource.ResourceProductionControlSeam";
        private const string ResourcePolicyTypeName = "SteamP2PFriends.Adapters.Resource.ResourceDemandPolicy";
        private const string CoordinatorTypeName = "SteamP2PFriends.MultiObserver.MultiObserverShadowCoordinator";

        internal static bool Test_All()
        {
            return Test_ControlPlaneOwnsSingleSpatialAuthority()
                && Test_ProjectionEngineHasNoNativeOrUnityDependency()
                && Test_NoSharedDefaultRadiusInControlPlane()
                && Test_ProjectionEngineDoesNotWriteDomainState()
                && Test_ResourceDomainDeclaresItsOwnDemandPolicy()
                && Test_ResourceEligibilityIsDelegatedToPolicy();
        }

        private static Assembly ProductionAssembly => typeof(SteamP2PFriendsPlugin).Assembly;

        /// <summary>
        /// 唯一空间事实与唯一投影引擎都由 Control Plane 建立：协调器各构造一次，注入
        /// 唯一的生产接缝；领域接缝既不持有 SpatialObserverIndex 也不自建权威，
        /// 但必须持有注入的投影引擎。
        /// </summary>
        internal static bool Test_ControlPlaneOwnsSingleSpatialAuthority()
        {
            Assembly assembly = ProductionAssembly;
            Type authority = assembly.GetType(AuthorityTypeName);
            Type engine = assembly.GetType(EngineTypeName);
            Type policy = assembly.GetType(PolicyTypeName);
            Type demand = assembly.GetType(DemandTypeName);
            Type seam = assembly.GetType(SeamTypeName);
            MethodInfo configure = StaticMethod(CoordinatorTypeName, "ConfigureResourceProduction");

            bool controlPlaneBuildsOneOfEach = configure != null
                && IlContractProbe.CountMethodCalls(configure, AuthorityTypeName, ".ctor") == 1
                && IlContractProbe.CountMethodCalls(configure, EngineTypeName, ".ctor") == 1
                && IlContractProbe.CountMethodCalls(configure, SeamTypeName, ".ctor") == 1;

            bool seamOwnsNoSpatialAuthority = seam != null
                && CountDeclaredFieldsOfType(seam, SpatialIndexTypeName) == 0
                && CountDeclaredFieldsOfType(seam, AuthorityTypeName) == 0
                && CountDeclaredFieldsOfType(seam, EngineTypeName) == 1
                && CountDeclaredMethodCalls(seam, SpatialIndexTypeName, ".ctor") == 0
                && CountDeclaredMethodCalls(seam, AuthorityTypeName, ".ctor") == 0
                && seam.GetProperty("ProjectedDemandRegionCount", BindingFlags.Instance | BindingFlags.Public) != null
                && seam.GetMethod("UpdateObserver", BindingFlags.Instance | BindingFlags.Public, null,
                    new[] { typeof(ulong), typeof(ulong), typeof(byte), typeof(byte), typeof(bool) }, null) != null;
            // 观察者事实只有一份：接缝保留的连接代次登记不得再是空间索引（见上文字段断言）。

            bool engineConsumesSharedAuthority = engine != null
                && CountDeclaredFieldsOfType(engine, AuthorityTypeName) == 1;

            return authority != null
                && engine != null
                && policy != null
                && demand != null
                && controlPlaneBuildsOneOfEach
                && seamOwnsNoSpatialAuthority
                && engineConsumesSharedAuthority;
        }

        /// <summary>
        /// Control Plane 是纯内存协调面：Demand 命名空间不得触达 Unturned / Unity 类型，
        /// 也不得读取任何原生区域常量。
        /// </summary>
        internal static bool Test_ProjectionEngineHasNoNativeOrUnityDependency()
        {
            Assembly assembly = ProductionAssembly;
            if (!HasDemandNamespaceType(assembly)) return false;
            return IlContractProbe.SumOverMethodsInNamespace(
                assembly, IsDemandNamespaceType, method =>
                    IlContractProbe.CountMemberReferences(method, IsNativeOrUnityType)) == 0;
        }

        /// <summary>
        /// 不存在跨域共享的默认半径：Demand 命名空间不读原版区域常量（RESOURCE_REGIONS /
        /// OBJECT_REGIONS / WORLD_SIZE）。半径只经 Demand Policy 由领域声明传入。
        /// </summary>
        internal static bool Test_NoSharedDefaultRadiusInControlPlane()
        {
            Assembly assembly = ProductionAssembly;
            if (!HasDemandNamespaceType(assembly)) return false;
            return IlContractProbe.SumOverMethodsInNamespace(
                assembly, IsDemandNamespaceType, method =>
                {
                    int resource = IlContractProbe.CountFieldLoads(
                        method, "SDG.Unturned.LevelGround", "RESOURCE_REGIONS");
                    int objects = IlContractProbe.CountFieldLoads(
                        method, "SDG.Unturned.ItemManager", "ITEM_REGIONS");
                    int world = IlContractProbe.CountFieldLoads(
                        method, "SDG.Unturned.LevelGround", "OBJECT_REGIONS");
                    int worldSize = IlContractProbe.CountFieldLoads(
                        method, "SDG.Unturned.Regions", "WORLD_SIZE");
                    if (resource < 0 || objects < 0 || world < 0 || worldSize < 0) return -1;
                    return resource + objects + world + worldSize;
                }) == 0;
        }

        /// <summary>
        /// 投影引擎不改原生状态、也不拥有领域执行：Demand 命名空间对 Resource 适配器、
        /// 生命周期适配器、快照适配器和生产接缝的方法调用数必须为零（执行端口才是写入口）。
        /// </summary>
        internal static bool Test_ProjectionEngineDoesNotWriteDomainState()
        {
            Assembly assembly = ProductionAssembly;
            if (!HasDemandNamespaceType(assembly)) return false;
            return IlContractProbe.SumOverMethodsInNamespace(
                assembly, IsDemandNamespaceType, method =>
                    IlContractProbe.CountCallsWhere(method,
                        called => IsDomainExecutionType(called.DeclaringType?.FullName))) == 0;
        }

        private static bool IsDomainExecutionType(string typeName)
        {
            return typeName == "SteamP2PFriends.Adapters.Resource.ResourceDomainAdapter"
                || typeName == "SteamP2PFriends.Adapters.Resource.ResourceRegionLifecycleAdapter"
                || typeName == "SteamP2PFriends.Adapters.Resource.ResourceSnapshotAdapter"
                || typeName == SeamTypeName;
        }

        /// <summary>
        /// Resource 只声明自己的 Demand Policy（资格、半径来源、切比雪夫形状、Domain Id）：
        /// 声明点是 Resource 域内的 ResourceDemandPolicy，且该策略显式携带 Resource Domain Id。
        /// </summary>
        internal static bool Test_ResourceDomainDeclaresItsOwnDemandPolicy()
        {
            Assembly assembly = ProductionAssembly;
            Type declaration = assembly.GetType(ResourcePolicyTypeName);
            Type policy = assembly.GetType(PolicyTypeName);
            if (declaration == null || policy == null) return false;

            MethodInfo factory = declaration.GetMethod("Create", BindingFlags.Static | BindingFlags.Public
                | BindingFlags.NonPublic);
            if (factory == null || factory.ReturnType.FullName != PolicyTypeName) return false;

            return IlContractProbe.CountMethodCalls(factory, PolicyTypeName, ".ctor") == 1
                && IlContractProbe.CountFieldLoads(factory, "SteamP2PFriends.Core.Identity.DomainIds",
                    "Resource") == 1;
        }

        /// <summary>
        /// Resource 资格不在协调器里被自行判定后丢样本：协调器把样本的玩法资格交给共享引擎，
        /// 由 Demand Policy 判定——不合格只暂缓，不被当作确认离开。
        /// </summary>
        internal static bool Test_ResourceEligibilityIsDelegatedToPolicy()
        {
            MethodInfo reconcile = StaticMethod(CoordinatorTypeName, "ReconcileResourceProduction");
            return reconcile != null
                && IlContractProbe.CountMethodCalls(
                    reconcile, "SteamP2PFriends.MultiObserver.ObserverShadowSample", "get_GameplayAuthorized") == 1
                && IlContractProbe.CountMethodCalls(reconcile, SeamTypeName, "UpdateObserver") == 1;
        }

        private static MethodInfo StaticMethod(string declaringTypeName, string methodName)
        {
            Type type = ProductionAssembly.GetType(declaringTypeName);
            return type?.GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic);
        }

        private static bool HasDemandNamespaceType(Assembly assembly)
        {
            foreach (Type type in assembly.GetTypes())
            {
                if (IsDemandNamespaceType(type)) return true;
            }
            return false;
        }

        private static bool IsDemandNamespaceType(Type type)
        {
            return type != null && string.Equals(type.Namespace, DemandNamespace, StringComparison.Ordinal);
        }

        private static bool IsNativeOrUnityType(Type type)
        {
            if (type == null) return false;
            string space = type.Namespace ?? string.Empty;
            return space.StartsWith("SDG.", StringComparison.Ordinal)
                || space.StartsWith("UnityEngine", StringComparison.Ordinal);
        }

        private static int CountDeclaredFieldsOfType(Type type, string fieldTypeName)
        {
            int count = 0;
            foreach (FieldInfo field in type.GetFields(
                BindingFlags.Instance | BindingFlags.Static
                | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (field.FieldType.FullName == fieldTypeName) count++;
            }
            return count;
        }

        private static int CountDeclaredMethodCalls(Type type, string declaringTypeName, string methodName)
        {
            return IlContractProbe.SumOverMethodsInNamespace(
                type.Assembly, candidate => candidate == type,
                method => IlContractProbe.CountMethodCalls(method, declaringTypeName, methodName));
        }
    }
}
