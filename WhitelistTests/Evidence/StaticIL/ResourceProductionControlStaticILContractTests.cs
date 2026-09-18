using SteamP2PFriends.Adapters.Resource;
using SteamP2PFriends.Client;
using SteamP2PFriends.MultiObserver;
using SteamP2PFriends.Core.Identity;
using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;

namespace SteamP2PFriends.WhitelistTests
{
    /// <summary>
    /// Ticket 10 的编译产物结构门禁：确认 Resource 生产接缝、唯一领域适配器
    /// 和生产生命周期提交入口存在，且控制面入口由现有协调器覆盖。
    /// 这组断言不把静态结构升级为真实游戏 Runtime 证据。
    /// </summary>
    internal static class ResourceProductionControlStaticILContractTests
    {
        internal static bool Test_All()
        {
            Assembly assembly = typeof(SteamP2PFriendsPlugin).Assembly;
            Type seam = assembly.GetTypes().SingleOrDefault(type =>
                type.FullName == "SteamP2PFriends.Adapters.Resource.ResourceProductionControlSeam");
            Type adapter = assembly.GetTypes().SingleOrDefault(type =>
                type.FullName == "SteamP2PFriends.Adapters.Resource.ResourceDomainAdapter");
            Type coordinator = assembly.GetTypes().SingleOrDefault(type =>
                type.FullName == "SteamP2PFriends.MultiObserver.MultiObserverShadowCoordinator");
            Type lifecycle = assembly.GetTypes().SingleOrDefault(type =>
                type.FullName == "SteamP2PFriends.Adapters.Resource.ResourceRegionLifecycleAdapter");
            Type nativeState = assembly.GetTypes().SingleOrDefault(type =>
                type.FullName == "SteamP2PFriends.Adapters.Resource.ResourceNativeRegionState");
            Type regionSync = assembly.GetType("SteamP2PFriends.Adapters.Resource.Patches.ResourceManagerRegionSyncPatch");
            Type worldSync = assembly.GetType("SteamP2PFriends.Adapters.Resource.Patches.ResourceManagerWorldSyncDiagnosticPatch");
            MethodInfo onRelease = adapter?.GetMethod("OnRelease", BindingFlags.Instance | BindingFlags.Public,
                null, new[] { typeof(SteamP2PFriends.MultiObserver.SPI.LeaseTicket) }, null);
            MethodInfo configure = coordinator?.GetMethod("ConfigureControlPlane",
                BindingFlags.Static | BindingFlags.NonPublic);

            return seam != null
                && adapter != null
                && coordinator != null
                && lifecycle != null
                && nativeState != null
                && seam.GetMethod("UpdateObserver", BindingFlags.Instance | BindingFlags.Public, null,
                    new[] { typeof(ulong), typeof(ulong), typeof(byte), typeof(byte) }, null) != null
                && seam.GetMethod("RemoveObserver") != null
                && seam.GetMethod("Tick") != null
                && seam.GetMethod("AdvanceTime") != null
                && seam.GetMethod("Flush") != null
                && seam.GetMethod("BeginSession") != null
                && seam.GetMethod("EndSession") != null
                && configure != null
                && coordinator.GetMethod("ReconcileResourceProduction",
                    BindingFlags.Static | BindingFlags.NonPublic) != null
                && lifecycle.GetMethod("CommitRelease", BindingFlags.Static | BindingFlags.Public,
                    null, new[] { typeof(SteamP2PFriends.Core.Identity.RegionKey), typeof(ulong), typeof(uint),
                        typeof(uint).MakeByRefType() }, null) != null
                && onRelease != null
                && CountMethodCalls(onRelease, lifecycle.FullName, "CommitRelease") == 1
                && CountMethodCalls(onRelease, lifecycle.FullName, "OnObserverRelease") == 0
                && CountMethodCalls(adapter?.GetMethod("OnAcquire", BindingFlags.Instance | BindingFlags.Public),
                    lifecycle.FullName, "TryCommitAcquire") == 1
                && CountMethodCalls(adapter?.GetMethod("OnAcquire", BindingFlags.Instance | BindingFlags.Public),
                    lifecycle.FullName, "OnObserverAcquire") == 0
                && adapter.GetMethod("OnObserverExited") != null
                && CountMethodCalls(configure, seam.FullName, ".ctor") == 1
                && CountMethodCalls(assembly.GetType("SteamP2PFriends.Adapters.Resource.Patches.ResourceManagerRegionSyncPatch")?.GetMethod(
                    "SendResources_Write_Prefix", BindingFlags.Static | BindingFlags.Public),
                    "SteamP2PFriends.Adapters.Resource.ResourceSnapshotAdapter", "RecordNativeSnapshotWrite") == 1
                && regionSync?.GetMethod("SendResources_Write_Prefix", BindingFlags.Static | BindingFlags.Public) != null
                && worldSync?.GetMethod("SendResources_Write_Prefix", BindingFlags.Static | BindingFlags.Public) == null
                && CountMethodCalls(regionSync.GetMethod("SendResources_Write_Prefix", BindingFlags.Static | BindingFlags.Public),
                    "SteamP2PFriends.Adapters.Resource.ResourceObservability", "NativePath") == 1
                && CountMethodCalls(assembly.GetType("SteamP2PFriends.Adapters.Resource.Patches.ResourceManagerWorldSyncDiagnosticPatch")?.GetMethod(
                    "ReceiveResources_Prefix", BindingFlags.Static | BindingFlags.Public),
                    "SteamP2PFriends.Adapters.Resource.ResourceSnapshotAdapter", "RecordNativeSnapshotReceive") == 1
                && CountMethodCalls(assembly.GetType("SteamP2PFriends.Adapters.Resource.Patches.ResourceManagerHarvestReplicationPatch")?.GetMethod(
                    "ServerSetResourceDead_Postfix", BindingFlags.Static | BindingFlags.Public),
                    "SteamP2PFriends.Adapters.Resource.ResourceSnapshotAdapter", "RecordNativeDelta") == 1
                && CountMethodCalls(assembly.GetType("SteamP2PFriends.Adapters.Resource.Patches.ResourceManagerHarvestReplicationPatch")?.GetMethod(
                    "ServerSetResourceAlive_Postfix", BindingFlags.Static | BindingFlags.Public),
                    "SteamP2PFriends.Adapters.Resource.ResourceSnapshotAdapter", "RecordNativeDelta") == 1
                && CountMethodCalls(assembly.GetType("SteamP2PFriends.Adapters.Resource.Patches.ResourceManagerHarvestReplicationPatch")?.GetMethod(
                    "ReceiveResourceDead_Postfix", BindingFlags.Static | BindingFlags.Public),
                    "SteamP2PFriends.Adapters.Resource.Patches.ResourceManagerHarvestReplicationPatch", "RecordClientDelta") == 1
                && CountMethodCalls(assembly.GetType("SteamP2PFriends.Adapters.Resource.Patches.ResourceManagerHarvestReplicationPatch")?.GetMethod(
                    "ReceiveResourceAlive_Postfix", BindingFlags.Static | BindingFlags.Public),
                    "SteamP2PFriends.Adapters.Resource.Patches.ResourceManagerHarvestReplicationPatch", "RecordClientDelta") == 1
                && CountMethodCalls(assembly.GetType("SteamP2PFriends.Adapters.Resource.Patches.ResourceManagerHarvestReplicationPatch")?.GetMethod(
                    "RecordClientDelta", BindingFlags.Static | BindingFlags.NonPublic),
                    "SteamP2PFriends.Adapters.Resource.ResourceSnapshotAdapter", "RecordNativeDeltaReceive") == 1
                && lifecycle.GetMethod("CaptureNativeRegionState", BindingFlags.Static | BindingFlags.NonPublic) != null
                && lifecycle.GetMethod("RestoreNativeRegionState", BindingFlags.Static | BindingFlags.NonPublic) != null
                && nativeState.GetProperty("IsNetworked") != null
                && nativeState.GetProperty("RespawnResourceIndex") != null
                && nativeState.GetProperty("ResourceCount") != null
                && nativeState.GetProperty("DeadResourceIndices") != null
                && CountMethodCalls(lifecycle.GetMethod("CaptureRegionState", BindingFlags.Static | BindingFlags.Public),
                    lifecycle.FullName, "CaptureNativeRegionState") == 1
                && CountMethodCalls(lifecycle.GetMethod("RestoreRegionState", BindingFlags.Static | BindingFlags.Public),
                    lifecycle.FullName, "RestoreNativeRegionState") == 1
                && CountAssemblyMethodCalls(assembly, lifecycle.FullName, "OnObserverRelease") == 0
                && assembly.GetTypes().Count(type =>
                    type.FullName == "SteamP2PFriends.Adapters.Resource.ResourceProductionControlSeam") == 1;
        }

        internal static bool Test_NativeSnapshotNullResourceListFailsClosed()
        {
            MethodInfo capture = typeof(ResourceRegionLifecycleAdapter).GetMethod(
                "CaptureNativeRegionState", BindingFlags.Static | BindingFlags.NonPublic);
            return ContainsStringLiteral(capture, "native-resource-trees-unavailable");
        }

        internal static bool Test_NativeRestoreValidatesBeforeApplyAndCanRollback()
        {
            MethodInfo restore = typeof(ResourceRegionLifecycleAdapter).GetMethod(
                "RestoreNativeRegionState", BindingFlags.Static | BindingFlags.NonPublic);
            return typeof(ResourceRegionLifecycleAdapter).GetMethod(
                    "ValidateNativeRegionState", BindingFlags.Static | BindingFlags.NonPublic) != null
                && typeof(ResourceRegionLifecycleAdapter).GetMethod(
                    "ApplyNativeRegionState", BindingFlags.Static | BindingFlags.NonPublic) != null
                && FindCallOffset(restore, "ValidateNativeRegionState") >= 0
                && FindCallOffset(restore, "ApplyNativeRegionState") > FindCallOffset(restore, "ValidateNativeRegionState")
                && CountMethodCalls(restore, typeof(ResourceRegionLifecycleAdapter).FullName,
                    "ApplyNativeRegionState") >= 2;
        }

        private static int FindCallOffset(MethodInfo method, string name)
        {
            return IlContractProbe.FindCallOffset(method, name);
        }

        /// <summary>
        /// 票 03 迁移：原生区域代次读取从生产接缝移入领域执行端口，唯一入口不变——
        /// 恰有一个接受 Region Key 的读取方法，且它只转发给领域声明的代次来源。
        /// </summary>
        internal static bool Test_GenerationReadDoesNotUseFallbackGuess()
        {
            Type port = typeof(ResourceExecutionPort);
            MethodInfo[] readers = port.GetMethods(BindingFlags.Static | BindingFlags.Instance |
                BindingFlags.Public | BindingFlags.NonPublic)
                .Where(method => method.Name == "ReadRegionGeneration")
                .ToArray();

            return readers.Length == 1
                && readers[0].GetParameters().Length == 1
                && readers[0].GetParameters()[0].ParameterType == typeof(RegionKey)
                && CountMethodCalls(readers[0], "SteamP2PFriends.Core.Identity.RegionGeneration",
                    "FromNative") == 1
                && CountAssemblyMethodCalls(typeof(SteamP2PFriendsPlugin).Assembly,
                    typeof(ResourceProductionControlSeam).FullName, "ReadGeneration") == 0;
        }

        /// <summary>
        /// 票 03 迁移：分类边界随编排逻辑移入共享引擎的 ProcessEntered，契约不变——
        /// 转换路径不解析异常文本（Message 只经具名 helper 承载）。
        /// </summary>
        internal static bool Test_FailureClassificationDoesNotParseExceptionText()
        {
            MethodInfo entered = LifecycleMethod("ProcessEntered");
            return entered != null
                && CountMethodCalls(entered, "System.Exception", "get_Message") == 0;
        }

        /// <summary>
        /// 与 Test_FailureClassificationDoesNotParseExceptionText 互补：转换路径不读取 Message
        /// （契约），但独立取证 helper 必须真正读取 Message（运行时日志定位需要）。二者共同保证
        /// 取证信息进入可观测输出、又不污染分类边界。
        /// </summary>
        internal static bool Test_AcquireFailureHelperEmbedsExceptionMessage()
        {
            MethodInfo helper = LifecycleMethod("DescribeFailure");
            return helper != null
                && CountMethodCalls(helper, "System.Exception", "get_Message") == 1
                && CountMethodCalls(helper, "System.Exception", "get_StackTrace") == 0;
        }

        /// <summary>
        /// 单区域处理体（含 acquire 失败分类 catch）迁入共享引擎后，分类边界随之迁移；
        /// 本契约镜像 Test_FailureClassificationDoesNotParseExceptionText，守护新边界同样不解析
        /// 异常文本——领域失败分类只经执行端口的 ClassifyRegionFailure 声明，不在引擎里读文本。
        /// </summary>
        internal static bool Test_SingleRegionEntryDoesNotParseExceptionText()
        {
            MethodInfo singleRegion = LifecycleMethod("ProcessSingleRegionEntry");
            return singleRegion != null
                && CountMethodCalls(singleRegion, "System.Exception", "get_Message") == 0;
        }

        private static MethodInfo LifecycleMethod(string name)
        {
            Type engine = typeof(ResourceProductionControlSeam).Assembly.GetType(
                "SteamP2PFriends.MultiObserver.Lifecycle.LifecycleOrchestrationEngine");
            return engine?.GetMethod(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic);
        }

        internal static bool Test_ClientConnectionGenerationFailureRequestsTeardown()
        {
            MethodInfo connected = typeof(P2PJoinManager).GetMethod(
                "OnClientConnected", BindingFlags.Static | BindingFlags.NonPublic);
            return connected != null
                && CountMethodCalls(connected, typeof(P2PJoinManager).FullName, "RequestDisconnect") == 1;
        }

        internal static bool Test_CoordinatorSessionEndClearsAfterResourceFailure()
        {
            MethodInfo endSession = typeof(MultiObserverShadowCoordinator).GetMethod(
                "EndSessionIfNeeded", BindingFlags.Static | BindingFlags.NonPublic);
            if (endSession == null || endSession.GetMethodBody() == null) return false;

            bool hasFinally = endSession.GetMethodBody().ExceptionHandlingClauses
                .Any(clause => clause.Flags == ExceptionHandlingClauseOptions.Finally);
            return hasFinally
                && CountMethodCalls(endSession, typeof(MultiObserverShadowCoordinator).FullName, "SafeWarn") >= 1
                && CountMethodCalls(endSession, typeof(MultiObserverShadowCoordinator).FullName, "SafeInfo") >= 1;
        }

        internal static bool Test_ResourceDomainEndLogsSuccessAfterCleanup()
        {
            MethodInfo endSession = typeof(ResourceDomainAdapter).GetMethod(
                "OnSessionEnd", BindingFlags.Instance | BindingFlags.Public);
            MethodInfo lifecycleEnd = typeof(ResourceRegionLifecycleAdapter).GetMethod(
                "EndSession", BindingFlags.Static | BindingFlags.Public);
            return endSession != null && lifecycleEnd != null
                && CountMethodCalls(endSession, typeof(ResourceRegionLifecycleAdapter).FullName, "EndSession") == 1
                && CountMethodCalls(endSession, typeof(ResourceRegionLifecycleAdapter).FullName, "SetRegistrationReady") >= 1
                && CountMethodCalls(endSession, typeof(ResourceSnapshotAdapter).FullName, "ResetSession") == 1
                && FindCallOffset(endSession, "EndSession") < FindCallOffset(endSession, "Info");
        }

        internal static bool Test_DeltaWriteUsesPerCallRejectDelta()
        {
            MethodInfo method = typeof(ResourceSnapshotAdapter).GetMethod(
                "RecordNativeDelta", BindingFlags.Static | BindingFlags.Public,
                null, new[] { typeof(RegionKey), typeof(uint) }, null);
            return method != null
                && CountMethodCalls(method, typeof(ResourceSnapshotReplicationLedger).FullName,
                    "get_StaleDeltaRejectCount") >= 2;
        }

        /// <summary>
        /// 票 01 表征门：生产接缝的半径与世界尺寸各自读取声明源——半径读原版物件区域
        /// 常量（LevelGround.RESOURCE_REGIONS），世界尺寸读 Regions.WORLD_SIZE。该断言锁
        /// 「半径来源」契约：物件半径不得升格为跨域共享默认半径（迁入共享引擎后仍须成立）。
        /// </summary>
        internal static bool Test_ProductionRadiusComesFromVanillaObjectRegionSource()
        {
            MethodInfo configure = typeof(MultiObserverShadowCoordinator).GetMethod(
                "ConfigureControlPlane", BindingFlags.Static | BindingFlags.NonPublic);
            return configure != null
                && CountFieldLoads(configure, "SDG.Unturned.LevelGround", "RESOURCE_REGIONS") == 1
                && CountFieldLoads(configure, "SDG.Unturned.Regions", "WORLD_SIZE") == 1
                && CountFieldLoads(configure, "SDG.Unturned.ItemManager", "ITEM_REGIONS") == 0;
        }

        // IL 遍历收敛到 IlContractProbe（票 01 审计 §8-4 具名的跨文件重复）；以下薄包装
        // 保持本文件既有调用点与断言不变。
        private static int CountMethodCalls(MethodInfo method, string declaringTypeName, string methodName)
        {
            return IlContractProbe.CountMethodCalls(method, declaringTypeName, methodName);
        }

        private static int CountFieldLoads(MethodInfo method, string declaringTypeName, string fieldName)
        {
            return IlContractProbe.CountFieldLoads(method, declaringTypeName, fieldName);
        }

        private static int CountAssemblyMethodCalls(Assembly assembly, string declaringTypeName, string methodName)
        {
            return IlContractProbe.CountAssemblyMethodCalls(assembly, declaringTypeName, methodName);
        }

        private static bool ContainsStringLiteral(MethodInfo method, string expected)
        {
            return IlContractProbe.ContainsStringLiteral(method, expected);
        }
    }
}
