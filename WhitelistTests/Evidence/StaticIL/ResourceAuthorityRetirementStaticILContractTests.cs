using SteamP2PFriends.Adapters.Resource;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace SteamP2PFriends.WhitelistTests
{
    /// <summary>
    /// Ticket 11 的静态退休契约。
    ///
    /// 确认旧的 Resource 租约释放调用已经退出生产调用图；原生
    /// ResourceManager 作为协议/数据面执行器保留，真实 Runtime 另行验收。
    /// </summary>
    internal static class ResourceAuthorityRetirementStaticILContractTests
    {
        internal static bool Test_All()
        {
            Assembly assembly = typeof(SteamP2PFriendsPlugin).Assembly;
            Type adapter = assembly.GetType("SteamP2PFriends.Adapters.Resource.ResourceDomainAdapter");
            Type seam = assembly.GetType("SteamP2PFriends.Adapters.Resource.ResourceProductionControlSeam");
            Type lifecycle = assembly.GetType("SteamP2PFriends.Adapters.Resource.ResourceRegionLifecycleAdapter");
            MethodInfo onRelease = adapter?.GetMethod("OnRelease");

            return adapter != null
                && seam != null
                && lifecycle != null
                && onRelease != null
                && lifecycle.GetMethod("OnObserverRelease") == null
                && CountMethodCalls(onRelease, lifecycle.FullName, "CommitRelease") == 1
                && CountAssemblyTypes(assembly, "SteamP2PFriends.Adapters.Resource.ResourceProductionControlSeam") == 1;
        }

        private static int CountAssemblyTypes(Assembly assembly, string fullName)
        {
            int count = 0;
            foreach (Type type in assembly.GetTypes())
            {
                if (type.FullName == fullName) count++;
            }
            return count;
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

                    if (called?.DeclaringType?.FullName == declaringTypeName && called.Name == methodName)
                        count++;
                }

                int operandSize = GetOperandSize(opCode.OperandType, il, offset);
                if (operandSize < 0 || offset + operandSize > il.Length) return -1;
                offset += operandSize;
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
