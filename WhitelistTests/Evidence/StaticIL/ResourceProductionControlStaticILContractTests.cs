using SteamP2PFriends.Adapters.Resource;
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
            MethodInfo onRelease = adapter?.GetMethod("OnRelease");
            MethodInfo configure = coordinator?.GetMethod("ConfigureResourceProduction",
                BindingFlags.Static | BindingFlags.NonPublic);

            return seam != null
                && adapter != null
                && coordinator != null
                && lifecycle != null
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
                && lifecycle.GetMethod("CommitRelease",
                    BindingFlags.Static | BindingFlags.Public) != null
                && adapter.GetMethod("OnRelease") != null
                && CountMethodCalls(onRelease, lifecycle.FullName, "CommitRelease") == 1
                && CountMethodCalls(onRelease, lifecycle.FullName, "OnObserverRelease") == 0
                && adapter.GetMethod("OnObserverExited") != null
                && CountMethodCalls(configure, seam.FullName, ".ctor") == 1
                && CountMethodCalls(assembly.GetType("SteamP2PFriends.Adapters.Resource.Patches.ResourceManagerRegionSyncPatch")?.GetMethod(
                    "SendResources_Write_Prefix", BindingFlags.Static | BindingFlags.Public),
                    "SteamP2PFriends.Adapters.Resource.ResourceSnapshotAdapter", "RecordNativeSnapshotWrite") == 1
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
                && CountAssemblyMethodCalls(assembly, lifecycle.FullName, "OnObserverRelease") == 0
                && assembly.GetTypes().Count(type =>
                    type.FullName == "SteamP2PFriends.Adapters.Resource.ResourceProductionControlSeam") == 1;
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
