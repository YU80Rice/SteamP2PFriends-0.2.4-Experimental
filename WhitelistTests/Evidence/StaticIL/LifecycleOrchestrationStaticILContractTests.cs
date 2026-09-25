using SteamP2PFriends.Adapters.Resource;
using SteamP2PFriends.Core.Identity;
using SteamP2PFriends.MultiObserver;
using SteamP2PFriends.MultiObserver.SPI;
using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;

namespace SteamP2PFriends.WhitelistTests
{
    /// <summary>
    /// 票 03 的结构门禁：共享 Lifecycle Orchestration Engine 拥有需求聚合、Acquire/Release、
    /// 滞回、身份校验、retry、补偿与故障隔离；Resource 只经 Domain Execution Port 操作原生
    /// 状态，不再持有第二套通用生命周期状态机。断言只读元数据与 IL，不把静态结构升级成
    /// Runtime 证据。
    /// </summary>
    internal static class LifecycleOrchestrationStaticILContractTests
    {
        private const string LifecycleNamespace = "SteamP2PFriends.MultiObserver.Lifecycle";
        private const string EngineTypeName = LifecycleNamespace + ".LifecycleOrchestrationEngine";
        private const string PortTypeName = LifecycleNamespace + ".IDomainExecutionPort";
        private const string PolicyTypeName = LifecycleNamespace + ".LifecyclePolicy";
        private const string DiagnosticsTypeName = LifecycleNamespace + ".ILifecycleDiagnostics";
        private const string DemandNamespace = "SteamP2PFriends.MultiObserver.Demand";
        private const string DemandEngineTypeName = DemandNamespace + ".DemandProjectionEngine";
        private const string SpatialIndexTypeName = "SteamP2PFriends.MultiObserver.Spatial.SpatialObserverIndex";
        private const string SeamTypeName = "SteamP2PFriends.Adapters.Resource.ResourceProductionControlSeam";
        private const string PortImplementationTypeName = "SteamP2PFriends.Adapters.Resource.ResourceExecutionPort";
        private const string LifecyclePolicyDeclarationTypeName =
            "SteamP2PFriends.Adapters.Resource.ResourceLifecyclePolicy";
        private const string ResourceLifecycleTypeName =
            "SteamP2PFriends.Adapters.Resource.ResourceRegionLifecycleAdapter";
        private const string CoordinatorTypeName = "SteamP2PFriends.MultiObserver.MultiObserverShadowCoordinator";

        private static readonly string[] ResourceExecutionTypes =
        {
            "SteamP2PFriends.Adapters.Resource.ResourceDomainAdapter",
            ResourceLifecycleTypeName,
            "SteamP2PFriends.Adapters.Resource.ResourceSnapshotAdapter",
            SeamTypeName
        };

        internal static bool Test_All()
        {
            return Test_SharedEngineAndPortExist()
                && Test_SeamHoldsNoOrchestrationState()
                && Test_EngineReachesNativeStateOnlyThroughPort()
                && Test_EngineConsumesTypedDemandWithoutReprojectingSpatialFacts()
                && Test_ResourceDeclaresItsOwnLifecyclePolicy()
                && Test_SingleDomainRegistrationSiteIsResource()
                && Test_EngineHasNoDomainBranch();
        }

        /// <summary>
        /// 共享编排引擎与领域执行端口都在 Control Plane 的 Lifecycle 命名空间内声明；
        /// 该命名空间是纯内存协调面：不得引用 Unturned / Unity 类型，也不得引用 Resource 适配器。
        /// </summary>
        internal static bool Test_SharedEngineAndPortExist()
        {
            Assembly assembly = ProductionAssembly;
            Type engine = LifecycleType(EngineTypeName);
            Type port = LifecycleType(PortTypeName);
            Type policy = LifecycleType(PolicyTypeName);
            Type diagnostics = LifecycleType(DiagnosticsTypeName);
            if (engine == null || port == null || policy == null || diagnostics == null) return false;
            if (!HasLifecycleNamespaceType(assembly)) return false;

            int nativeReferences = IlContractProbe.SumOverMethodsInNamespace(
                assembly, IsLifecycleNamespaceType, method =>
                    IlContractProbe.CountMemberReferences(method, IsNativeOrUnityType));

            return engine.IsPublic && port.IsInterface && diagnostics.IsInterface
                && nativeReferences == 0;
        }

        /// <summary>
        /// Resource 生产接缝不再持有通用生命周期状态机：需求计数、租约、滞回释放、acquire retry
        /// 等编排集合一个都不留在接缝里（零泛型集合字段、零嵌套编排类型），唯一持有的编排对象
        /// 是共享引擎。薄门面只做转发与注入。
        /// </summary>
        internal static bool Test_SeamHoldsNoOrchestrationState()
        {
            Type seam = LifecycleType(SeamTypeName);
            Type engine = LifecycleType(EngineTypeName);
            if (seam == null || engine == null) return false;

            // 编排状态在旧接缝里就是一批泛型集合（Dictionary/HashSet 键控 RegionKey 与观察者）：
            // 迁入共享引擎后接缝不得再声明任何一个泛型字段。
            int orchestrationCollections = DeclaredFields(seam).Count(field => field.FieldType.IsGenericType);

            int engineFields = DeclaredFields(seam).Count(field => field.FieldType == engine);
            int nestedOrchestrationTypes = seam.GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public)
                .Count(nested => nested.Name == "PendingRelease" || nested.Name == "AcquireRetry");

            return orchestrationCollections == 0
                && engineFields == 1
                && nestedOrchestrationTypes == 0;
        }

        /// <summary>
        /// 编排引擎不直接触达任何领域执行类型：原生状态只能经 IDomainExecutionPort 到达，
        /// 端口实现（ResourceExecutionPort）是 Resource 侧唯一的生产执行入口。
        /// </summary>
        internal static bool Test_EngineReachesNativeStateOnlyThroughPort()
        {
            Type engine = LifecycleType(EngineTypeName);
            Type port = LifecycleType(PortTypeName);
            Type portImplementation = LifecycleType(PortImplementationTypeName);
            if (engine == null || port == null || portImplementation == null) return false;

            bool engineTouchesNoDomainExecutionType =
                DeclaredFields(engine).All(field => !ResourceExecutionTypes.Contains(field.FieldType.FullName))
                && IlContractProbe.SumOverMethodsInNamespace(
                    engine.Assembly, candidate => candidate == engine, method =>
                        IlContractProbe.CountCallsWhere(method,
                            called => ResourceExecutionTypes.Contains(called.DeclaringType?.FullName))) == 0;
            // 端口由引擎的每域编排状态持有（每域恰一份），引擎自身不得出现端口的具体实现类型。
            int portFields = CountDeclaredFieldsOfType(engine, port.FullName)
                + engine.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
                    .Sum(nested => CountDeclaredFieldsOfType(nested, port.FullName));
            bool engineDependsOnPort = portFields == 1;
            bool portImplementationServesTheInterface = port.IsAssignableFrom(portImplementation)
                && CountDeclaredFieldsOfType(engine, PortImplementationTypeName) == 0;

            return engineTouchesNoDomainExecutionType && engineDependsOnPort
                && portImplementationServesTheInterface;
        }

        private static Assembly ProductionAssembly => typeof(SteamP2PFriendsPlugin).Assembly;

        private static Type LifecycleType(string name) => ProductionAssembly.GetType(name);

        /// <summary>
        /// 编排引擎消费 typed Domain Demand，不重新投影空间：它持有唯一共享投影引擎并调用其
        /// 投影入口，但既不持有 SpatialObserverIndex，也不调用任何空间枚举/差异方法。
        /// </summary>
        internal static bool Test_EngineConsumesTypedDemandWithoutReprojectingSpatialFacts()
        {
            Type engine = LifecycleType(EngineTypeName);
            if (engine == null) return false;

            bool consumesProjection = CountDeclaredFieldsOfType(engine, DemandEngineTypeName) == 1
                && IlContractProbe.SumOverMethodsInNamespace(
                    engine.Assembly, candidate => candidate == engine, method =>
                        IlContractProbe.CountMethodCalls(method, DemandEngineTypeName, "Observe")) >= 1;
            bool ownsNoSpatialIndex = CountDeclaredFieldsOfType(engine, SpatialIndexTypeName) == 0
                && IlContractProbe.SumOverMethodsInNamespace(
                    engine.Assembly, candidate => candidate == engine, method =>
                        IlContractProbe.CountCallsWhere(method,
                            called => called.DeclaringType?.FullName == SpatialIndexTypeName)) == 0;

            return consumesProjection && ownsNoSpatialIndex;
        }

        /// <summary>
        /// Lifecycle Policy 由领域声明（滞回窗口、retry 节奏、静默阈值），引擎只执行：
        /// Resource 的声明点读自己的领域常量，不得由共享引擎写入领域默认值。
        /// </summary>
        internal static bool Test_ResourceDeclaresItsOwnLifecyclePolicy()
        {
            Type declaration = LifecycleType(LifecyclePolicyDeclarationTypeName);
            Type policy = LifecycleType(PolicyTypeName);
            if (declaration == null || policy == null) return false;

            MethodInfo factory = declaration.GetMethod("Create", BindingFlags.Static | BindingFlags.Public
                | BindingFlags.NonPublic);
            if (factory == null || factory.ReturnType.FullName != PolicyTypeName) return false;

            int assemblyConstructions = IlContractProbe.SumOverDeclaredMethods(
                ProductionAssembly, method =>
                    IlContractProbe.CountMethodCalls(method, PolicyTypeName, ".ctor"));
            int engineConstructions = IlContractProbe.SumOverMethodsInNamespace(
                ProductionAssembly, IsLifecycleNamespaceType, method =>
                    IlContractProbe.CountMethodCalls(method, PolicyTypeName, ".ctor"));

            // 全程序集只有一处 Lifecycle Policy 构造：Resource 自己的声明。共享引擎内部不构造
            // 任何政策，也不读取领域常量——不存在跨域共享的默认滞回或默认 retry 节奏。
            int engineDeclarations = IlContractProbe.SumOverMethodsInNamespace(
                ProductionAssembly, IsLifecycleNamespaceType, method =>
                    IlContractProbe.CountMethodCalls(method, LifecyclePolicyDeclarationTypeName, "Create"));

            Type collisionDeclaration = ProductionAssembly.GetType(
                "SteamP2PFriends.Adapters.Collision.CollisionLifecyclePolicy");
            MethodInfo collisionFactory = collisionDeclaration?.GetMethod("Create",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            return IlContractProbe.CountMethodCalls(factory, PolicyTypeName, ".ctor") == 1
                && IlContractProbe.CountMethodCalls(collisionFactory, PolicyTypeName, ".ctor") == 1
                && assemblyConstructions == 2
                && engineConstructions == 0
                && engineDeclarations == 0;
        }

        /// <summary>
        /// 本切片只有 Resource 一个领域取得编排注册资格：全程序集恰有一个编排注册点，且它位于
        /// 协调器的 Resource 接线方法内。Collision 即使被编译进 DLL 也没有注册入口。
        /// </summary>
        internal static bool Test_SingleDomainRegistrationSiteIsResource()
        {
            Assembly assembly = ProductionAssembly;
            int registrationSites = IlContractProbe.SumOverDeclaredMethods(assembly, method =>
                IlContractProbe.CountMethodCalls(method, EngineTypeName, "Register"));
            Type seam = LifecycleType(SeamTypeName);
            ConstructorInfo seamConstructor = seam?.GetConstructors().SingleOrDefault();
            MethodInfo configure = StaticMethod(CoordinatorTypeName, "ConfigureControlPlane");

            // 唯一注册点位于 Resource 生产接缝的构造函数内；协调器只经该接缝接线。
            bool registeredOnlyOnResourceWiring = seamConstructor != null
                && IlContractProbe.CountCallsWhere(seamConstructor,
                    called => called.DeclaringType?.FullName == EngineTypeName && called.Name == "Register") == 1
                && configure != null
                && IlContractProbe.CountMethodCalls(configure, SeamTypeName, ".ctor") == 1;
            bool collisionHasNoOrchestrationPort = assembly.GetTypes().All(type =>
                !(type.Namespace ?? string.Empty).StartsWith(LifecycleNamespace, StringComparison.Ordinal)
                || !string.Equals(type.Name, "CollisionExecutionPort", StringComparison.Ordinal));

            return registrationSites == 1 && registeredOnlyOnResourceWiring
                && collisionHasNoOrchestrationPort;
        }

        /// <summary>
        /// 共享引擎内部没有领域分支：Lifecycle 命名空间不读取任何 Domain Id 常量、
        /// 不引用 Collision 域类型。领域身份只作为数据穿过引擎，不参与分支判断。
        /// </summary>
        internal static bool Test_EngineHasNoDomainBranch()
        {
            Assembly assembly = ProductionAssembly;
            if (!HasLifecycleNamespaceType(assembly)) return false;

            int domainIdLoads = IlContractProbe.SumOverMethodsInNamespace(
                assembly, IsLifecycleNamespaceType, method =>
                    IlContractProbe.CountFieldLoads(method, "SteamP2PFriends.Core.Identity.DomainIds", "Resource")
                    + IlContractProbe.CountFieldLoads(method, "SteamP2PFriends.Core.Identity.DomainIds", "Collision"));
            int collisionReferences = IlContractProbe.SumOverMethodsInNamespace(
                assembly, IsLifecycleNamespaceType, method =>
                    IlContractProbe.CountMemberReferences(method, type =>
                        (type.Namespace ?? string.Empty).StartsWith(
                            "SteamP2PFriends.Adapters.Collision", StringComparison.Ordinal)));

            return domainIdLoads == 0 && collisionReferences == 0;
        }

        private static IEnumerable<FieldInfo> DeclaredFields(Type type) => type.GetFields(
            BindingFlags.Instance | BindingFlags.Static
            | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

        private static int CountDeclaredFieldsOfType(Type type, string fieldTypeName) =>
            DeclaredFields(type).Count(field => field.FieldType.FullName == fieldTypeName);

        private static MethodInfo StaticMethod(string declaringTypeName, string methodName)
        {
            Type type = ProductionAssembly.GetType(declaringTypeName);
            return type?.GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic);
        }

        private static bool HasLifecycleNamespaceType(Assembly assembly) =>
            assembly.GetTypes().Any(IsLifecycleNamespaceType);

        private static bool IsLifecycleNamespaceType(Type type) =>
            type != null && string.Equals(type.Namespace, LifecycleNamespace, StringComparison.Ordinal);

        private static bool IsNativeOrUnityType(Type type)
        {
            if (type == null) return false;
            string space = type.Namespace ?? string.Empty;
            return space.StartsWith("SDG.", StringComparison.Ordinal)
                || space.StartsWith("UnityEngine", StringComparison.Ordinal);
        }
    }
}
