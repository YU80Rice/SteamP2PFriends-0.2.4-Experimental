using System;
using System.Linq;
using System.Reflection;

namespace SteamP2PFriends.WhitelistTests
{
    /// <summary>
    /// 票 04 的结构门禁：共享控制面的正式切换准入不变量必须由结构本身锁住，而不是只靠行为
    /// 用例——坏样本不得冻结整批、暂缓不得触发破坏性释放、共享外层捕获不得结束会话、
    /// retry 必须是事务粒度、持续故障必须有界心跳。断言只读元数据与 IL，不把静态结构
    /// 升级成 Runtime 证据，也不切换 Collision Authority Writer。
    /// </summary>
    internal static class ControlPlaneReadinessStaticILContractTests
    {
        private const string LifecycleNamespace = "SteamP2PFriends.MultiObserver.Lifecycle";
        private const string EngineTypeName = LifecycleNamespace + ".LifecycleOrchestrationEngine";
        private const string AdmissionTypeName =
            "SteamP2PFriends.MultiObserver.Demand.ObserverSampleAdmission";
        private const string IdentityGateTypeName = LifecycleNamespace + ".SessionIdentityGate";
        private const string IdentityGateStateTypeName = LifecycleNamespace + ".ESessionIdentityState";
        private const string HeartbeatTypeName = LifecycleNamespace + ".BoundedHeartbeat";
        private const string HeartbeatPolicyTypeName = LifecycleNamespace + ".HeartbeatPolicy";
        private const string CoordinatorTypeName = "SteamP2PFriends.MultiObserver.MultiObserverShadowCoordinator";
        private const string LedgerTypeName = "SteamP2PFriends.MultiObserver.MultiObserverShadowLedger";
        private const string ZombieAdapterTypeName =
            "SteamP2PFriends.Adapters.Zombie.ZombieRegionLifecycleAdapter";

        private static Assembly ProductionAssembly => typeof(SteamP2PFriendsPlugin).Assembly;

        private static Type ProductionType(string name) => ProductionAssembly.GetType(name);

        /// <summary>按名字取生产方法（静态或实例都要能取到：契约目标不只落在静态入口上）。</summary>
        private static MethodInfo ProductionMethod(string declaringTypeName, string methodName)
        {
            Type type = ProductionType(declaringTypeName);
            return type == null
                ? null
                : type.GetMethods(BindingFlags.Static | BindingFlags.Instance
                        | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(method => method.Name == methodName);
        }

        internal static bool Test_All()
        {
            return Test_AcquireRetryIsTransactionScoped()
                && Test_DeferredDemandNeverReleases()
                && Test_SessionIdentityGateIsPureAndBounded()
                && Test_SampleAdmissionIsPerRecord()
                && Test_SharedOuterCatchDoesNotEndSession()
                && Test_PersistentFaultHeartbeatIsNotOneShot()
                && Test_ZombieFaultClosureBypassesSharedLogQuota()
                && Test_SharedFaultChannelIsBoundedAndCloses();
        }

        /// <summary>
        /// 共享面故障通道同样是有界心跳 + 恢复闭环：故障入口经心跳写出（不再用计数器封顶，
        /// 那会让持续故障在若干条之后永久静默），成功路径写闭环。
        /// </summary>
        internal static bool Test_SharedFaultChannelIsBoundedAndCloses()
        {
            Type coordinator = ProductionType(CoordinatorTypeName);
            Type heartbeat = ProductionType(HeartbeatTypeName);
            MethodInfo faultEntry = ProductionMethod(CoordinatorTypeName, "HandleTickFailure");
            MethodInfo tick = ProductionMethod(CoordinatorTypeName, "Tick");
            MethodInfo recovered = ProductionMethod(CoordinatorTypeName, "ReportSharedFaultRecovered");
            if (coordinator == null || heartbeat == null || faultEntry == null
                || tick == null || recovered == null)
            {
                return false;
            }

            bool ownsHeartbeat = DeclaredFields(coordinator).Any(field => field.FieldType == heartbeat);
            bool entryGoesThroughHeartbeat = IlContractProbe.CountCallsWhere(faultEntry,
                    called => called.Name == "ReportSharedFaultHeartbeat") >= 1
                && IlContractProbe.CountCallsWhere(faultEntry,
                    called => called.Name == "SafeWarn") == 0;
            bool tickClosesLoop = IlContractProbe.CountCallsWhere(tick,
                    called => called.Name == "ReportSharedFaultRecovered") >= 1;
            // 恢复闭环必须由「故障 episode 是否活跃」决定，不能由心跳是否仍在运行决定：
            // 心跳用尽（Exhausted）后仍在故障中，恢复时同样要写闭环。
            bool closureFollowsEpisode = recovered != null
                && IlContractProbe.CountCallsWhere(recovered,
                    called => called.Name == "get_IsRunning") == 0;

            return ownsHeartbeat && entryGoesThroughHeartbeat && tickClosesLoop
                && closureFollowsEpisode;
        }

        /// <summary>
        /// 僵尸域故障心跳与恢复闭环必须走不受共享日志配额约束的独立出口：普通日志配额被打满时，
        /// 持续异常的终止记录与恢复闭环若一起被静默，有界心跳就只是状态机在空转。
        /// </summary>
        internal static bool Test_ZombieFaultClosureBypassesSharedLogQuota()
        {
            MethodInfo faultEntry = ProductionMethod(ZombieAdapterTypeName, "ReportTickFailure");
            MethodInfo recovered = ProductionMethod(ZombieAdapterTypeName, "ReportTickRecovered");
            if (faultEntry == null || recovered == null) return false;

            return IlContractProbe.CountCallsWhere(faultEntry,
                       called => called.Name == "SafeFault") >= 1
                && IlContractProbe.CountCallsWhere(faultEntry,
                       called => called.Name == "SafeWarn") == 0
                && IlContractProbe.CountCallsWhere(recovered,
                       called => called.Name == "SafeFault") >= 1
                && IlContractProbe.CountCallsWhere(recovered,
                       called => called.Name == "SafeWarn") == 0;
        }

        /// <summary>引擎声明事务粒度的 retry 键（区域 + 观察者 + 连接代次），不再是区域单槽。</summary>
        internal static bool Test_AcquireRetryIsTransactionScoped()
        {
            Type engine = ProductionType(EngineTypeName);
            Type key = engine == null ? null : FindNestedType(engine, "AcquireRetryKey");
            if (key == null) return false;

            string[] keyFields = key.GetFields(BindingFlags.Instance | BindingFlags.Public
                | BindingFlags.NonPublic).Select(field => field.FieldType.Name).ToArray();
            bool keyCarriesTransactionIdentity = keyFields.Contains("RegionKey")
                && keyFields.Contains("UInt64") && keyFields.Length >= 3;
            Type retryTable = DeclaredFields(engine)
                .Concat(engine.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
                    .SelectMany(DeclaredFields))
                .Where(field => field.Name == "AcquireRetries")
                .Select(field => field.FieldType)
                .FirstOrDefault();
            bool keyedByTransaction = retryTable != null
                && retryTable.GetGenericArguments().Any(argument => argument == key);
            bool singleSlotGone = retryTable == null
                || retryTable.GetGenericArguments()[0] != typeof(SteamP2PFriends.Core.Identity.RegionKey);

            return keyCarriesTransactionIdentity && keyedByTransaction && singleSlotGone;
        }

        /// <summary>
        /// 暂缓路径不产生破坏性释放：把无效样本登记为 Deferred Observer Demand 的方法体内
        /// 不得递减需求、不得登记待释放、不得调用领域释放——「无法证明离开」时宁可不关。
        /// </summary>
        internal static bool Test_DeferredDemandNeverReleases()
        {
            MethodInfo defer = ProductionMethod(EngineTypeName, "DeferObserver");
            if (defer == null) return false;

            return IlContractProbe.CountCallsWhere(defer,
                       called => called.Name == "OnRelease") == 0
                && IlContractProbe.CountCallsWhere(defer,
                       called => called.Name == "DecrementDemand") == 0
                && IlContractProbe.CountCallsWhere(defer,
                       called => called.Name == "OnObserverReplicationExited") == 0;
        }

        /// <summary>
        /// 会话身份门是纯 Control Plane 类型：有界恢复窗口 + 显式熔断状态，零原生依赖；
        /// 它只产出决策（就绪/恢复中/已熔断），不自己结束任何会话。
        /// </summary>
        internal static bool Test_SessionIdentityGateIsPureAndBounded()
        {
            Type gate = ProductionType(IdentityGateTypeName);
            if (gate == null || !gate.IsPublic) return false;

            Type state = ProductionType(IdentityGateStateTypeName);
            bool declaresStates = state != null && state.IsEnum && Enum.GetNames(state).Length == 3;
            int nativeReferences = IlContractProbe.SumOverDeclaredMethods(ProductionAssembly, method =>
                method.DeclaringType == gate
                    ? IlContractProbe.CountMemberReferences(method, IsNativeOrUnityType)
                    : 0);
            bool hasRecoveryWindow = DeclaredFields(gate).Any(field => field.FieldType == typeof(float));
            bool endsNoSession = gate.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .All(method => !string.Equals(method.Name, "EndSession", StringComparison.Ordinal));

            return declaresStates && nativeReferences == 0
                && hasRecoveryWindow && endsNoSession;
        }

        /// <summary>
        /// 样本准入是逐条的：协调器必须经共享准入策略决定「哪些样本可用、哪些观察者进入暂缓」，
        /// 而不是自己在一处整批判定后跳过全部 reconcile。
        /// </summary>
        internal static bool Test_SampleAdmissionIsPerRecord()
        {
            Type admission = ProductionType(AdmissionTypeName);
            Type coordinator = ProductionType(CoordinatorTypeName);
            if (admission == null || coordinator == null) return false;

            int admissionReferences = IlContractProbe.SumOverDeclaredMethods(ProductionAssembly, method =>
                method.DeclaringType == coordinator
                    ? IlContractProbe.CountMemberReferences(method,
                        type => type != null && type.FullName == AdmissionTypeName)
                    : 0);

            // 资源侧接线必须消费准入计划的缺席移除资格：不具备资格（本拍有身份不可读记录）时
            // 不得移除任何观察者，否则「读不全的记录」仍会经旁路触发破坏性释放。
            // 契约形状：协调器读取 `AllowAbsenceRemoval`，且删除动作所在方法的入参带该闸门。
            MethodInfo reconcile = ProductionMethod(CoordinatorTypeName, "ReconcileResourceProduction");
            bool absenceRemovalIsGated = reconcile != null
                && IlContractProbe.CountCallsWhere(reconcile,
                    called => called.Name == "get_AllowAbsenceRemoval") >= 1
                && IlContractProbe.CountCallsWhere(reconcile,
                    called => called.Name == "RemoveObserver") >= 1;

            return admission.IsPublic && admissionReferences >= 1 && absenceRemovalIsGated;
        }

        /// <summary>
        /// 共享外层捕获不再结束会话：shadow 故障处理入口不得调用任何 EndSession；
        /// 僵尸域 tick 处于自己的异常边界内，且不经过 shadow 的故障处理入口。
        /// </summary>
        internal static bool Test_SharedOuterCatchDoesNotEndSession()
        {
            MethodInfo faultHandler = ProductionMethod(CoordinatorTypeName, "HandleTickFailure");
            MethodInfo zombieTick = ProductionMethod(ZombieAdapterTypeName, "Tick");
            Type plugin = typeof(SteamP2PFriendsPlugin);
            MethodInfo zombieBoundary = plugin.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
                .FirstOrDefault(method => method.Name == "TickZombieIsolated");
            MethodInfo shadowBoundary = plugin.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
                .FirstOrDefault(method => method.Name == "TickMultiObserverShadowIsolated");
            if (faultHandler == null || zombieTick == null
                || zombieBoundary == null || shadowBoundary == null)
            {
                return false;
            }

            // 故障处理入口（含它的任何收尾包装，例如 EndSessionIfNeeded）都不得结束会话：
            // 一旦它能结束会话，僵尸/旁路异常就会经共享外层捕获跨域清掉其它领域的租约。
            bool faultHandlerEndsNoSession = IlContractProbe.CountCallsWhere(faultHandler,
                called => called.Name.IndexOf("EndSession", StringComparison.Ordinal) >= 0) == 0;
            bool zombieIsolated = IlContractProbe.CountCallsWhere(zombieBoundary,
                    called => called.DeclaringType?.FullName == ZombieAdapterTypeName) >= 1
                && IlContractProbe.CountExceptionHandlers(zombieBoundary) >= 1
                && IlContractProbe.CountCallsWhere(zombieBoundary,
                    called => string.Equals(called.Name, "HandleTickFailure", StringComparison.Ordinal)) == 0;
            bool shadowIsolated = IlContractProbe.CountCallsWhere(shadowBoundary,
                    called => called.DeclaringType?.FullName == CoordinatorTypeName) >= 1
                && IlContractProbe.CountExceptionHandlers(shadowBoundary) >= 1;

            // 更新入口只经这两个独立边界调用各域 tick：入口自己不得直连任一域，
            // 否则「共享外层捕获」会从旁路复活。
            MethodInfo update = plugin.GetMethod("Update",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            bool updateDelegatesToBoundaries = update != null
                && IlContractProbe.CountCallsWhere(update,
                    called => called.Name == "TickZombieIsolated"
                        || called.Name == "TickMultiObserverShadowIsolated") >= 2
                && IlContractProbe.CountCallsWhere(update,
                    called => called.DeclaringType?.FullName == ZombieAdapterTypeName) == 0
                && IlContractProbe.CountCallsWhere(update,
                    called => called.DeclaringType?.FullName == CoordinatorTypeName) == 0;

            return faultHandlerEndsNoSession && zombieIsolated && shadowIsolated
                && updateDelegatesToBoundaries;
        }

        /// <summary>
        /// 持续故障的心跳不是一次去重：熔断领域被跳过时的诊断由有界心跳状态机驱动
        /// （首条 + 按间隔重复 + 有界 + 显式终止），不再只用 TransitionOnce 打一条就永久静默。
        /// </summary>
        internal static bool Test_PersistentFaultHeartbeatIsNotOneShot()
        {
            Type engine = ProductionType(EngineTypeName);
            Type heartbeat = ProductionType(HeartbeatTypeName);
            Type policy = ProductionType(HeartbeatPolicyTypeName);
            MethodInfo skipped = ProductionMethod(EngineTypeName, "ReportSkippedFaultedDomain");
            if (engine == null || heartbeat == null || policy == null || skipped == null) return false;

            bool heartbeatIsDeclared = policy.IsPublic && heartbeat.IsPublic;
            bool engineOwnsHeartbeatState = engine.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
                .SelectMany(DeclaredFields)
                .Any(field => field.FieldType == heartbeat);
            bool skippedEmitsThroughHeartbeat = IlContractProbe.CountCallsWhere(skipped,
                called => called.DeclaringType?.FullName == HeartbeatTypeName) >= 1
                || IlContractProbe.CountCallsWhere(skipped,
                    called => string.Equals(called.Name, "NextFaultHeartbeat", StringComparison.Ordinal)) >= 1;
            bool notOneShotDedup = IlContractProbe.CountCallsWhere(skipped,
                called => string.Equals(called.Name, "TransitionOnce", StringComparison.Ordinal)) == 0;

            return heartbeatIsDeclared && engineOwnsHeartbeatState
                && skippedEmitsThroughHeartbeat && notOneShotDedup;
        }

        private static bool IsNativeOrUnityType(Type type)
        {
            if (type == null) return false;
            string space = type.Namespace ?? string.Empty;
            return space.StartsWith("SDG.", StringComparison.Ordinal)
                || space.StartsWith("UnityEngine", StringComparison.Ordinal);
        }

        private static FieldInfo[] DeclaredFields(Type type) => type.GetFields(
            BindingFlags.Instance | BindingFlags.Static
            | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

        private static Type FindNestedType(Type type, string name)
        {
            foreach (Type nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (string.Equals(nested.Name, name, StringComparison.Ordinal)) return nested;
                Type deeper = FindNestedType(nested, name);
                if (deeper != null) return deeper;
            }
            return null;
        }
    }
}
