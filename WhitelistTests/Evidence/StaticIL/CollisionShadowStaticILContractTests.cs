using System;
using System.Collections.Generic;
using System.Reflection;

namespace SteamP2PFriends.WhitelistTests
{
    /// <summary>
    /// 票 08 的 Collision 切换门禁：Collision 继续通过声明式 Demand Policy
    /// 复用共享观察者事实；正式生产不再运行影子比较，旧 RemoteCoverage Writer
    /// 也不再登记或写入。断言只读元数据与 IL，不把静态结构升级成 Runtime 证据。
    /// </summary>
    internal static class CollisionShadowStaticILContractTests
    {
        private const string CollisionNamespace = "SteamP2PFriends.Adapters.Collision";
        private const string PolicyTypeName = CollisionNamespace + ".CollisionDemandPolicy";
        private const string ComparatorTypeName = CollisionNamespace + ".CollisionShadowComparator";
        private const string FrameTypeName = CollisionNamespace + ".CollisionShadowFrame";
        private const string ReportTypeName = CollisionNamespace + ".CollisionShadowReport";
        private const string LegacyClaimTypeName = CollisionNamespace + ".CollisionShadowLegacyClaim";
        private const string ClaimTypeName = CollisionNamespace + ".CollisionShadowClaim";
        private const string AdmissionPlanTypeName =
            "SteamP2PFriends.MultiObserver.Demand.ObserverSampleAdmissionPlan";
        private const string LegacyPatchTypeName = CollisionNamespace + ".Patches.LevelObjectRemoteCollisionPatch";
        private const string LedgerAdapterTypeName = CollisionNamespace + ".LevelObjectCollisionAdapter";
        private const string DemandPolicyTypeName = "SteamP2PFriends.MultiObserver.Demand.DemandPolicy";
        private const string DemandEngineTypeName = "SteamP2PFriends.MultiObserver.Demand.DemandProjectionEngine";
        private const string PresenceTypeName = "SteamP2PFriends.MultiObserver.Demand.ObserverPresence";
        private const string AuthorityTypeName = "SteamP2PFriends.MultiObserver.Demand.ObserverSpatialAuthority";
        private const string CoordinatorTypeName = "SteamP2PFriends.MultiObserver.MultiObserverShadowCoordinator";

        internal static bool Test_All()
        {
            return Test_CollisionDeclaresItsOwnDemandPolicy()
                && Test_CollisionPolicyReusesWorldPresenceEligibility()
                && Test_ShadowPathIsPureMemoryWithoutRosterScan()
                && Test_LegacySnapshotIsReadOnly()
                && Test_ShadowPathDoesNotWriteNativeState()
                && Test_LegacyWriterAndShadowHaveNoProductionCalls()
                && Test_ShadowComparisonHasNoProductionConsumer()
                && Test_DeferredDemandStaysInItsDomain();
        }

        private static Assembly ProductionAssembly => typeof(SteamP2PFriendsPlugin).Assembly;

        /// <summary>
        /// Collision 的 Demand Policy 由 Collision 域自己声明：Domain Id = Collision、
        /// 半径来源是原版物件区域常量名、形状与半径都由调用方传入（不得内置共享默认半径）。
        /// </summary>
        internal static bool Test_CollisionDeclaresItsOwnDemandPolicy()
        {
            MethodInfo factory = StaticMethod(PolicyTypeName, "Create");
            return factory != null
                && factory.ReturnType.FullName == DemandPolicyTypeName
                && IlContractProbe.CountMethodCalls(factory, DemandPolicyTypeName, ".ctor") == 1
                && IlContractProbe.CountFieldLoads(factory, "SteamP2PFriends.Core.Identity.DomainIds",
                    "Collision") == 1
                && IlContractProbe.ContainsStringLiteral(factory, "LevelObjects.OBJECT_REGIONS")
                && IlContractProbe.CountFieldLoads(factory, "SDG.Unturned.LevelObjects",
                    "OBJECT_REGIONS") == 0;
        }

        /// <summary>
        /// 资格沿用 World Presence Observer 的玩法资格，不在 Collision 域里另判授权：
        /// 政策声明面读 <see cref="PresenceTypeName"/> 的玩法资格，且零授权/审核类型引用。
        /// </summary>
        internal static bool Test_CollisionPolicyReusesWorldPresenceEligibility()
        {
            Assembly assembly = ProductionAssembly;
            int presenceReads = SumOverShadowMethods(assembly, PolicyTypeName,
                method => IlContractProbe.CountMethodCalls(method, PresenceTypeName, "get_GameplayAuthorized"));
            int authorizationReferences = SumOverShadowMethods(assembly, PolicyTypeName,
                method => IlContractProbe.CountCallsWhere(method,
                    called => (called.DeclaringType?.FullName ?? string.Empty)
                        .StartsWith("SteamP2PFriends.Security", StringComparison.Ordinal)));
            return presenceReads == 1 && authorizationReferences == 0;
        }

        /// <summary>
        /// 新影子路径是纯内存面：政策、对照帧、对照结论与比较器都不触达 Unturned/Unity 类型，
        /// 也不持有第二份观察者索引（不得复制 Observer Spatial Authority），
        /// 更不自己扫描客户端名册。
        /// </summary>
        internal static bool Test_ShadowPathIsPureMemoryWithoutRosterScan()
        {
            Assembly assembly = ProductionAssembly;
            foreach (string typeName in new[] { PolicyTypeName, ComparatorTypeName, FrameTypeName, ReportTypeName })
            {
                if (assembly.GetType(typeName) == null) return false;
                int nativeReferences = SumOverShadowMethods(assembly, typeName,
                    method => IlContractProbe.CountMemberReferences(method, IsNativeOrUnityType));
                int rosterScans = SumOverShadowMethods(assembly, typeName,
                    method => IlContractProbe.CountFieldLoads(method, "SDG.Unturned.Provider", "clients")
                        + IlContractProbe.CountMemberReferences(method,
                            type => type?.FullName == "SteamP2PFriends.MultiObserver.Spatial.SpatialObserverIndex"));
                if (nativeReferences != 0 || rosterScans != 0) return false;
            }
            return true;
        }

        /// <summary>
        /// 旧覆盖快照只读：抄出覆盖区域与远端玩家中心，零原生类型调用、零旧 Writer 写入入口调用；
        /// 编码转换留在受控的适配器边界内。
        /// </summary>
        internal static bool Test_LegacySnapshotIsReadOnly()
        {
            Type legacy = ProductionAssembly.GetType(LegacyPatchTypeName);
            return legacy != null
                && legacy.GetMethod("CaptureShadowSnapshot",
                    BindingFlags.Static | BindingFlags.NonPublic) == null;
        }

        /// <summary>
        /// 影子路径不写原生状态：它不激活物件、不改门动画剔除、不写可采集树、不动碰撞账本，
        /// 也不驱动 Resource 生产接缝；正式 Collision 写入只允许经 Execution Port 发生。
        /// </summary>
        internal static bool Test_ShadowPathDoesNotWriteNativeState()
        {
            Assembly assembly = ProductionAssembly;
            int writeCalls = SumOverShadowMethods(assembly, CoordinatorTypeName, method =>
                {
                    if (!IsCollisionShadowMethod(method)) return 0;
                    return IlContractProbe.CountCallsWhere(method,
                        called => IsNativeOrUnityType(called.DeclaringType)
                            || IsLegacyWriterEntryPoint(called)
                            || (called.DeclaringType?.FullName ?? string.Empty)
                                == "SteamP2PFriends.Adapters.Resource.ResourceProductionControlSeam");
                });
            return writeCalls == 0;
        }

        /// <summary>
        /// 旧 Writer 与 Collision Ledger 均不再消费影子类型、Demand Engine 或 Policy，
        /// 正式 Collision Writer 由共享编排引擎经 Execution Port 承担。
        /// </summary>
        internal static bool Test_LegacyWriterAndShadowHaveNoProductionCalls()
        {
            Assembly assembly = ProductionAssembly;
            foreach (string typeName in new[] { LegacyPatchTypeName, LedgerAdapterTypeName })
            {
                Type type = assembly.GetType(typeName);
                if (type == null) return false;
                int shadowReferences = SumOverShadowMethods(assembly, typeName, method =>
                    {
                        if (IsShadowSnapshotEntryPoint(method)) return 0;
                        return IlContractProbe.CountCallsWhere(method, called =>
                                IsCollisionShadowType(called.DeclaringType?.FullName)
                                || (called.DeclaringType?.FullName ?? string.Empty) == DemandEngineTypeName
                                || (called.DeclaringType?.FullName ?? string.Empty) == PolicyTypeName);
                    });
                if (shadowReferences != 0) return false;
            }
            return true;
        }

        /// 影子比较器与旧快照在正式程序集均没有生产消费者。
        /// 纯函数比较器只由 PureMemory 测试直接使用，正式协调器不再保留影子空壳。
        /// </summary>
        internal static bool Test_ShadowComparisonHasNoProductionConsumer()
        {
            Assembly assembly = ProductionAssembly;
            int compareCalls = IlContractProbe.CountAssemblyMethodCalls(assembly, ComparatorTypeName, "Compare");
            int snapshotCalls = IlContractProbe.CountAssemblyMethodCalls(assembly, LegacyPatchTypeName,
                "CaptureShadowSnapshot");
            return compareCalls == 0 && snapshotCalls == 0;
        }

        internal static bool Test_DeferredDemandStaysInItsDomain()
        {
            MethodInfo defer = StaticMethod(CoordinatorTypeName, "DeferKnownObservers");
            if (defer == null) return false;
            int unionCalls = IlContractProbe.CountCallsWhere(defer,
                called => called.Name == "UnionWith");
            int collisionCalls = IlContractProbe.CountCallsWhere(defer,
                called => called.Name == "DeferObserver"
                    && called.DeclaringType?.FullName
                        == "SteamP2PFriends.MultiObserver.Lifecycle.LifecycleOrchestrationEngine");
            return unionCalls == 0 && collisionCalls == 1;
        }

        internal static bool Test_CutoverRetiresLegacyHarmonyRegistration()
        {
            Type legacy = ProductionAssembly.GetType(LegacyPatchTypeName);
            if (legacy == null) return false;
            MethodInfo register = legacy.GetMethod("RegisterManual",
                BindingFlags.Static | BindingFlags.Public);
            return register != null
                && IlContractProbe.CountMethodCalls(register,
                    "HarmonyLib.Harmony", "Patch") == 0;
        }

        private static bool IsCollisionShadowMethod(MethodBase method)
        {
            string name = method?.Name ?? string.Empty;
            return name == "RunCollisionShadow" || name == "ReportCollisionShadow"
                || name == "StartCollisionShadowForbiddenHeartbeat" || name == "CollisionShadowDemandRegionCount";
        }

        private static bool IsShadowSnapshotEntryPoint(MethodBase method)
        {
            return string.Equals(method?.Name, "CaptureShadowSnapshot", StringComparison.Ordinal);
        }

        private static bool IsLegacyWriterEntryPoint(MethodBase called)
        {
            string declaringType = called?.DeclaringType?.FullName ?? string.Empty;
            if (declaringType == LedgerAdapterTypeName
                || declaringType == CollisionNamespace + ".LevelObjectCollisionLedger")
            {
                return true;
            }
            return declaringType == LegacyPatchTypeName
                && !string.Equals(called.Name, "CaptureShadowSnapshot", StringComparison.Ordinal);
        }

        private static bool IsCollisionShadowType(string typeName)
        {
            return typeName == ComparatorTypeName || typeName == FrameTypeName || typeName == ReportTypeName;
        }

        private static int SumOverShadowMethods(Assembly assembly, string typeName, Func<MethodBase, int> count)
        {
            Type declared = assembly.GetType(typeName);
            if (declared == null) return -1;
            return IlContractProbe.SumOverMethodsInNamespace(assembly,
                type => type == declared
                    || (type?.DeclaringType?.FullName ?? string.Empty) == typeName,
                count);
        }

        private static MethodInfo StaticMethod(string typeName, string methodName, bool internalOnly = false)
        {
            Type type = ProductionAssembly.GetType(typeName);
            return type?.GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic
                | (internalOnly ? 0 : BindingFlags.Public));
        }

        private static bool IsNativeOrUnityType(Type type)
        {
            if (type == null) return false;
            string space = type.Namespace ?? string.Empty;
            return space.StartsWith("SDG.", StringComparison.Ordinal)
                || space.StartsWith("UnityEngine", StringComparison.Ordinal);
        }
    }
}
