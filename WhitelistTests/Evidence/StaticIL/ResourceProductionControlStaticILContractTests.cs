using SteamP2PFriends.Adapters.Resource;
using SteamP2PFriends.Client;
using SteamP2PFriends.MultiObserver;
using SteamP2PFriends.Core.Identity;
using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
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
            MethodInfo configure = coordinator?.GetMethod("ConfigureResourceProduction",
                BindingFlags.Static | BindingFlags.NonPublic);

            return seam != null
                && adapter != null
                && coordinator != null
                && lifecycle != null
                && nativeState != null
                && seam.GetMethod("UpdateObserver") != null
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
            if (method == null) return -1;
            byte[] il = method.GetMethodBody()?.GetILAsByteArray();
            if (il == null) return -1;
            for (int offset = 0; offset + 4 < il.Length; offset++)
            {
                int token = BitConverter.ToInt32(il, offset);
                try
                {
                    MethodBase called = method.Module.ResolveMethod(token);
                    if (called != null && string.Equals(called.Name, name, StringComparison.Ordinal))
                        return offset;
                }
                catch { }
            }
            return -1;
        }

        internal static bool Test_GenerationReadDoesNotUseFallbackGuess()
        {
            Type seam = typeof(ResourceProductionControlSeam);
            MethodInfo[] readers = seam.GetMethods(BindingFlags.Static | BindingFlags.Instance |
                BindingFlags.Public | BindingFlags.NonPublic)
                .Where(method => method.Name == "ReadGeneration")
                .ToArray();

            return readers.Length == 1
                && readers[0].GetParameters().Length == 1
                && readers[0].GetParameters()[0].ParameterType == typeof(RegionKey);
        }

        internal static bool Test_FailureClassificationDoesNotParseExceptionText()
        {
            MethodInfo entered = typeof(ResourceProductionControlSeam).GetMethod(
                "ProcessEntered", BindingFlags.Instance | BindingFlags.NonPublic);
            return entered != null
                && CountMethodCalls(entered, "System.Exception", "get_Message") == 0;
        }

        internal static bool Test_AcquireFailureHelperEmbedsExceptionMessage()
        {
            // 与 Test_FailureClassificationDoesNotParseExceptionText 互补：ProcessEntered 的
            // EDG 不读取 Message（契约），但独立取证 helper DescribeAcquireFailure 必须真正
            // 读取 Message（运行时日志定位需要）。二者共同保证取证信息进入可观测输出、
            // 又不污染 ProcessEntered 的分类边界。
            MethodInfo helper = typeof(ResourceProductionControlSeam).GetMethod(
                "DescribeAcquireFailure", BindingFlags.Static | BindingFlags.NonPublic);
            return helper != null
                && CountMethodCalls(helper, "System.Exception", "get_Message") == 1
                && CountMethodCalls(helper, "System.Exception", "get_StackTrace") == 0;
        }

        internal static bool Test_SingleRegionEntryDoesNotParseExceptionText()
        {
            // ProcessEntered 的单区域处理体（含 acquire 失败分类 catch）抽入
            // ProcessSingleRegionEntry 后，分类边界随之迁移；本契约镜像
            // Test_FailureClassificationDoesNotParseExceptionText，守护新的分类边界
            // 同样不解析异常文本（Message 仅经 DescribeAcquireFailure helper 承载）。
            MethodInfo singleRegion = typeof(ResourceProductionControlSeam).GetMethod(
                "ProcessSingleRegionEntry", BindingFlags.Instance | BindingFlags.NonPublic);
            return singleRegion != null
                && CountMethodCalls(singleRegion, "System.Exception", "get_Message") == 0;
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

        private static int CountMethodCalls(MethodInfo method, string declaringTypeName, string methodName)
        {
            if (method == null) return -1;
            byte[] il = method.GetMethodBody()?.GetILAsByteArray();
            if (il == null) return 0;

            int count = 0;
            int offset = 0;
            while (offset < il.Length)
            {
                ushort value = il[offset++];
                if (value == 0xfe)
                {
                    if (offset >= il.Length) return -1;
                    value = (ushort)(0xfe00 | il[offset++]);
                }
                if (!OpCodesByValue.TryGetValue(value, out OpCode opCode)) return -1;

                if (opCode.OperandType == OperandType.InlineMethod)
                {
                    if (offset + 4 > il.Length) return -1;
                    int token = BitConverter.ToInt32(il, offset);
                    MethodBase called;
                    try { called = method.Module.ResolveMethod(token); }
                    catch { called = null; }
                    if (called?.DeclaringType?.FullName == declaringTypeName
                        && called.Name == methodName)
                    {
                        count++;
                    }
                }

                int operandSize = GetOperandSize(opCode.OperandType, il, offset);
                if (operandSize < 0 || offset + operandSize > il.Length) return -1;
                offset += operandSize;
            }

            return count;
        }

        private static int CountAssemblyMethodCalls(Assembly assembly, string declaringTypeName, string methodName)
        {
            int count = 0;
            foreach (Type type in assembly.GetTypes())
            {
                foreach (MethodInfo method in type.GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static
                    | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    int calls = CountMethodCalls(method, declaringTypeName, methodName);
                    if (calls < 0) return -1;
                    count += calls;
                }
            }
            return count;
        }

        private static bool ContainsStringLiteral(MethodInfo method, string expected)
        {
            if (method == null || expected == null) return false;
            byte[] il = method.GetMethodBody()?.GetILAsByteArray();
            if (il == null) return false;
            for (int offset = 0; offset + 4 < il.Length; offset++)
            {
                if (il[offset] != OpCodes.Ldstr.Value) continue;
                int token = BitConverter.ToInt32(il, offset + 1);
                try
                {
                    if (string.Equals(method.Module.ResolveString(token), expected, StringComparison.Ordinal))
                        return true;
                }
                catch { }
            }
            return false;
        }

        private static int GetOperandSize(OperandType operandType, byte[] il, int offset)
        {
            switch (operandType)
            {
                case OperandType.InlineNone: return 0;
                case OperandType.ShortInlineBrTarget:
                case OperandType.ShortInlineI:
                case OperandType.ShortInlineVar: return 1;
                case OperandType.InlineVar: return 2;
                case OperandType.InlineI8:
                case OperandType.InlineR: return 8;
                case OperandType.ShortInlineR: return 4;
                case OperandType.InlineSwitch:
                    if (offset + 4 > il.Length) return -1;
                    int cases = BitConverter.ToInt32(il, offset);
                    return cases < 0 || cases > (il.Length - offset - 4) / 4 ? -1 : 4 + cases * 4;
                default: return 4;
            }
        }

        private static readonly Dictionary<ushort, OpCode> OpCodesByValue = CreateOpCodeMap();

        private static Dictionary<ushort, OpCode> CreateOpCodeMap()
        {
            var result = new Dictionary<ushort, OpCode>();
            foreach (FieldInfo field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (field.FieldType == typeof(OpCode))
                {
                    OpCode opCode = (OpCode)field.GetValue(null);
                    result[(ushort)opCode.Value] = opCode;
                }
            }
            return result;
        }
    }
}
