using System;
using System.Collections.Generic;
using System.Reflection;

namespace SteamP2PFriends.WhitelistTests
{
    /// <summary>
    /// 票 05 的结构门禁：Collision 以自己声明的 Demand Policy（原版物件区域半径、切比雪夫、
    /// 沿用 World Presence Observer 资格）从共享观察者事实投影 typed Collision Demand；
    /// 只读影子对照不写原生状态、不扫描客户端名册、不自建观察者索引，旧 Writer 仍是唯一
    /// 生产写入者。断言只读元数据与 IL，不把静态结构升级成 Runtime 证据。
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
                && Test_LegacyWriterRemainsOnlyProductionWriter()
                && Test_ShadowComparisonHasSingleReadOnlyConsumer();
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
            MethodInfo snapshot = StaticMethod(LegacyPatchTypeName, "CaptureShadowSnapshot", internalOnly: true);
            if (snapshot == null) return false;

            bool readsLegacyState =
                IlContractProbe.CountFieldLoads(snapshot, LegacyPatchTypeName, "RemoteCoverage") == 1
                && IlContractProbe.CountFieldLoads(snapshot, LegacyPatchTypeName, "RemotePlayerRegions") == 1;
            // 快照不得调用旧补丁自己的任何方法：那些方法（重建覆盖、刷新区域、移除远端玩家）
            // 都是写入路径，快照顺手调用它们就等于影子期多了一条写入口。
            int legacyMutatorCalls = IlContractProbe.CountCallsWhere(snapshot,
                called => called.DeclaringType?.FullName == LegacyPatchTypeName);
            int nativeCalls = IlContractProbe.CountCallsWhere(snapshot,
                called => IsNativeOrUnityType(called.DeclaringType) || IsLegacyWriterEntryPoint(called));
            return readsLegacyState && legacyMutatorCalls == 0 && nativeCalls == 0;
        }

        /// <summary>
        /// 影子路径不写原生状态：它不激活物件、不改门动画剔除、不写可采集树、不动碰撞账本，
        /// 也不驱动 Resource 生产接缝——影子期旧 Writer 仍是唯一生产写入者。
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
        /// 旧 Writer 仍是唯一生产写入者：旧补丁与碰撞账本适配器都不消费新投影/比较器/政策，
        /// 因此新投影不可能借它们写入；旧补丁只多出一个只读快照入口。
        /// </summary>
        internal static bool Test_LegacyWriterRemainsOnlyProductionWriter()
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

        /// <summary>
        /// 比较器在程序集里只有一个调用点（协调器的只读影子路径）：影子对照没有第二个消费者，
        /// 更不是任何生产权威。该路径同时必须「确认离开才清理」：它消费准入计划的缺席移除资格
        /// 与暂缓集合，并对确认缺席者走一次 RemoveObserver——否则离开者的需求会永久残留，
        /// 之后每一拍都会被报成「凭空需求」这种禁止差异。
        /// </summary>
        internal static bool Test_ShadowComparisonHasSingleReadOnlyConsumer()
        {
            Assembly assembly = ProductionAssembly;
            int compareCalls = IlContractProbe.CountAssemblyMethodCalls(assembly, ComparatorTypeName, "Compare");
            MethodInfo runShadow = StaticMethod(CoordinatorTypeName, "RunCollisionShadow", internalOnly: true);
            MethodInfo snapshot = StaticMethod(LegacyPatchTypeName, "CaptureShadowSnapshot", internalOnly: true);
            // 缺席移除资格被读两次：一次把住移除动作，一次决定旧侧认领者能否被断言为「离开」。
            // 暂缓者还必须从控制面最后已知事实合成一条暂缓认领（否则其保留区域会被当成凭空需求）。
            bool consumesAdmissionPlan = runShadow != null
                && IlContractProbe.CountMethodCalls(runShadow, AdmissionPlanTypeName, "get_AllowAbsenceRemoval") == 2
                && IlContractProbe.CountMethodCalls(runShadow, AuthorityTypeName, "TryGet") == 1
                && IlContractProbe.CountMethodCalls(runShadow, ClaimTypeName, ".ctor") == 2
                && IlContractProbe.CountMethodCalls(runShadow, AdmissionPlanTypeName, "get_DeferredObserverIds") == 1
                && IlContractProbe.CountMethodCalls(runShadow, DemandEngineTypeName, "RemoveObserver") == 1
                && IlContractProbe.CountMethodCalls(runShadow, LegacyClaimTypeName, ".ctor") == 1;
            return compareCalls == 1
                && runShadow != null
                && snapshot != null
                && consumesAdmissionPlan
                && IlContractProbe.CountMethodCalls(runShadow, ComparatorTypeName, "Compare") == 1
                && IlContractProbe.CountMethodCalls(runShadow, DemandEngineTypeName, "Observe") == 1
                && IlContractProbe.CountMethodCalls(runShadow, DemandEngineTypeName, "GetActiveRegions") == 1
                && IlContractProbe.CountMethodCalls(runShadow, LegacyPatchTypeName, "CaptureShadowSnapshot") == 1
                && IlContractProbe.CountMethodCalls(runShadow, PresenceTypeName, ".ctor") == 1
                && IlContractProbe.CountFieldLoads(runShadow, "SDG.Unturned.Provider", "clients") == 0
                && IlContractProbe.CountMethodCalls(runShadow,
                    "SteamP2PFriends.MultiObserver.ObserverShadowSample", "get_IsLocalPlayer") == 1
                && IlContractProbe.CountMethodCalls(runShadow, FrameTypeName, ".ctor") == 1;
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
