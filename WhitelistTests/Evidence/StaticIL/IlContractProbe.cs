using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace SteamP2PFriends.WhitelistTests
{
    /// <summary>
    /// StaticIL 契约的共享 IL 探针：解析方法体操作数并按解析结果计数。
    /// 各契约文件不再各自复制一份 IL 遍历（票 01 审计 §8-4 具名的重复项在此收敛）。
    /// 探针只读元数据，不执行被测代码，也不把静态结构升级成 Runtime 证据。
    /// </summary>
    internal static class IlContractProbe
    {
        /// <summary>
        /// 构造函数也是合法的契约目标（例如「唯一注册点位于该类型的构造函数内」），
        /// 因此 IL 计数入口接受 MethodBase 而不只是 MethodInfo。
        /// </summary>
        internal static int CountMethodCalls(MethodBase method, string declaringTypeName, string methodName)
        {
            return CountCallsWhere(method, called =>
                called.DeclaringType?.FullName == declaringTypeName && called.Name == methodName);
        }

        internal static int CountCallsWhere(MethodBase method, Func<MethodBase, bool> matches)
        {
            if (matches == null) throw new ArgumentNullException(nameof(matches));
            return CountOperands(method, (owner, operandType, token) =>
            {
                if (operandType != OperandType.InlineMethod) return false;
                MethodBase called;
                try { called = owner.ResolveMethod(token); }
                catch { called = null; }
                return called != null && matches(called);
            });
        }

        internal static int CountFieldLoads(MethodBase method, string declaringTypeName, string fieldName)
        {
            return CountOperands(method, (owner, operandType, token) =>
            {
                if (operandType != OperandType.InlineField) return false;
                FieldInfo field;
                try { field = owner.ResolveField(token); }
                catch { field = null; }
                return field != null
                    && field.DeclaringType?.FullName == declaringTypeName
                    && field.Name == fieldName;
            });
        }

        /// <summary>
        /// 统计方法体内引用「声明类型满足 predicate」的成员 token（方法调用、字段访问、
        /// 构造函数、类型操作数都算），用于证明某模块不触达原生/Unity 类型。
        /// </summary>
        internal static int CountMemberReferences(MethodBase method, Func<Type, bool> declaringTypeMatches)
        {
            if (declaringTypeMatches == null) throw new ArgumentNullException(nameof(declaringTypeMatches));
            return CountOperands(method, (owner, operandType, token) =>
            {
                MemberInfo member;
                try { member = owner.ResolveMember(token); }
                catch { member = null; }
                switch (member)
                {
                    case MethodBase calledMethod: return declaringTypeMatches(calledMethod.DeclaringType);
                    case FieldInfo field: return declaringTypeMatches(field.DeclaringType);
                    case Type type: return declaringTypeMatches(type);
                    default: return false;
                }
            });
        }

        internal static int CountAssemblyMethodCalls(Assembly assembly, string declaringTypeName, string methodName)
        {
            return SumOverDeclaredMethods(assembly, method => CountMethodCalls(method, declaringTypeName, methodName));
        }

        internal static int CountAssemblyMemberReferences(Assembly assembly, Func<Type, bool> declaringTypeMatches)
        {
            return SumOverDeclaredMethods(assembly, method => CountMemberReferences(method, declaringTypeMatches));
        }

        internal static int SumOverDeclaredMethods(Assembly assembly, Func<MethodBase, int> count)
        {
            int total = 0;
            foreach (Type type in assembly.GetTypes())
            {
                foreach (MethodBase method in DeclaredMethodsAndConstructors(type))
                {
                    int calls = count(method);
                    if (calls < 0) return -1;
                    total += calls;
                }
            }
            return total;
        }

        internal static int SumOverMethodsInNamespace(
            Assembly assembly, Func<Type, bool> typeMatches, Func<MethodBase, int> count)
        {
            int total = 0;
            foreach (Type type in assembly.GetTypes())
            {
                if (!typeMatches(type)) continue;
                foreach (MethodBase method in DeclaredMethodsAndConstructors(type))
                {
                    int calls = count(method);
                    if (calls < 0) return -1;
                    total += calls;
                }
            }
            return total;
        }

        /// <summary>
        /// 类型的全部声明方法，含构造函数——注册、接线与初始化调用往往只出现在构造函数里，
        /// 漏掉它们会让「唯一注册点」「不得触达某组类型」这类契约假绿。
        /// </summary>
        private static IEnumerable<MethodBase> DeclaredMethodsAndConstructors(Type type)
        {
            const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;
            foreach (MethodInfo method in type.GetMethods(Declared)) yield return method;
            foreach (ConstructorInfo constructor in type.GetConstructors(Declared)) yield return constructor;
        }

        internal static int CountOperands(
            MethodBase method, Func<Module, OperandType, int, bool> matches)
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

                if (opCode.OperandType == OperandType.InlineMethod
                    || opCode.OperandType == OperandType.InlineField
                    || opCode.OperandType == OperandType.InlineType
                    || opCode.OperandType == OperandType.InlineTok)
                {
                    if (offset + 4 > il.Length) return -1;
                    int token = BitConverter.ToInt32(il, offset);
                    if (matches(method.Module, opCode.OperandType, token)) count++;
                }

                int operandSize = GetOperandSize(opCode.OperandType, il, offset);
                if (operandSize < 0 || offset + operandSize > il.Length) return -1;
                offset += operandSize;
            }

            return count;
        }

        internal static int FindCallOffset(MethodInfo method, string name)
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

        internal static bool ContainsStringLiteral(MethodInfo method, string expected)
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

        internal static int GetOperandSize(OperandType operandType, byte[] il, int offset)
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
