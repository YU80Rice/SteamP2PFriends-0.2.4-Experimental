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

        /// <summary>
        /// 方法体声明的异常处理子句数（try/catch/finally）。用于证明某个域 tick 处于自己的
        /// 故障边界内，而不是躲在共享外层捕获后面：没有边界就没有独立故障隔离。
        /// </summary>
        internal static int CountExceptionHandlers(MethodBase method)
        {
            if (method == null) return -1;
            MethodBody body = method.GetMethodBody();
            return body == null ? 0 : body.ExceptionHandlingClauses.Count;
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

        internal static bool ContainsIntegerLiteral(MethodBase method, int expected)
        {
            if (method == null) return false;
            byte[] il = method.GetMethodBody()?.GetILAsByteArray();
            if (il == null) return false;
            for (int offset = 0; offset < il.Length; offset++)
            {
                switch (il[offset])
                {
                    case 0x15: if (expected == -1) return true; break;
                    case 0x16: if (expected == 0) return true; break;
                    case 0x17: if (expected == 1) return true; break;
                    case 0x18: if (expected == 2) return true; break;
                    case 0x19: if (expected == 3) return true; break;
                    case 0x1A: if (expected == 4) return true; break;
                    case 0x1B: if (expected == 5) return true; break;
                    case 0x1C: if (expected == 6) return true; break;
                    case 0x1D: if (expected == 7) return true; break;
                    case 0x1E: if (expected == 8) return true; break;
                    case 0x1F:
                        if (offset + 1 < il.Length && (sbyte)il[offset + 1] == expected) return true;
                        offset++;
                        break;
                    case 0x20:
                        if (offset + 4 < il.Length && BitConverter.ToInt32(il, offset + 1) == expected) return true;
                        offset += 4;
                        break;
                }
            }
            return false;
        }

        internal sealed class IlInstruction
        {
            public int Offset;
            public OpCode OpCode;
            public int Operand;
        }

        /// <summary>
        /// 把方法体 IL 解析成指令序列（含操作数原始值：token / 局部变量索引 / 字面量）。
        /// 返回 null 表示无方法体或无法解析；调用方必须 fail-closed，不得把 null 当成 0。
        /// </summary>
        internal static List<IlInstruction> ReadInstructions(MethodBase method)
        {
            if (method == null) return null;
            byte[] il = method.GetMethodBody()?.GetILAsByteArray();
            if (il == null) return null;
            var result = new List<IlInstruction>();
            int offset = 0;
            while (offset < il.Length)
            {
                int start = offset;
                ushort value = il[offset++];
                if (value == 0xfe)
                {
                    if (offset >= il.Length) return null;
                    value = (ushort)(0xfe00 | il[offset++]);
                }
                if (!OpCodesByValue.TryGetValue(value, out OpCode opCode)) return null;
                int operandSize = GetOperandSize(opCode.OperandType, il, offset);
                if (operandSize < 0 || offset + operandSize > il.Length) return null;
                result.Add(new IlInstruction
                {
                    Offset = start,
                    OpCode = opCode,
                    Operand = ReadOperand(opCode, il, offset),
                });
                offset += operandSize;
            }
            return result;
        }

        private static int ReadOperand(OpCode opCode, byte[] il, int offset)
        {
            switch (opCode.OperandType)
            {
                case OperandType.InlineNone: return 0;
                case OperandType.ShortInlineI:
                case OperandType.ShortInlineVar:
                case OperandType.ShortInlineBrTarget: return (sbyte)il[offset];
                case OperandType.InlineVar: return BitConverter.ToInt16(il, offset);
                default: return offset + 4 <= il.Length ? BitConverter.ToInt32(il, offset) : 0;
            }
        }

        /// <summary>该指令是否是对「声明类型 + 方法名」的调用（call/callvirt 都算）。</summary>
        internal static bool IsCallTo(IlInstruction instruction, MethodBase owner, string declaringTypeName, string methodName)
        {
            if (instruction == null || instruction.OpCode.OperandType != OperandType.InlineMethod) return false;
            MethodBase called;
            try { called = owner.Module.ResolveMethod(instruction.Operand); }
            catch { called = null; }
            return called != null
                && called.DeclaringType?.FullName == declaringTypeName
                && called.Name == methodName;
        }

        /// <summary>
        /// 统计「返回值被条件分支消费」的调用点数量（R12-01 判据）。
        /// 跟踪 call 之后的返回值（跳过 nop）：直接遇到条件分支即采纳；
        /// 先 stloc 暂存、随后 ldloc 同一局部变量再条件分支也算采纳；
        /// 先遇到 pop（丢弃）或其它形态一律判未采纳（fail-closed）。
        /// lookahead=8 的理由：规范写法 `if (!Call(...))` 在 call 之后 1 条指令即分支，
        /// 允许 8 条只为容纳 stloc/ldloc 暂存与 nop 填充；更远就不再是「该调用结果的即时控制决策」。
        /// 漏判情形：若编译器把结果先写入字段、经其它方法中转后再判定，本判据会漏判并判红。
        /// </summary>
        internal static int CountCallsWithBranchOnResult(
            MethodBase method, string declaringTypeName, string methodName, int lookahead)
        {
            List<IlInstruction> instructions = ReadInstructions(method);
            if (instructions == null) return -1;
            int count = 0;
            for (int i = 0; i < instructions.Count; i++)
            {
                if (!IsCallTo(instructions[i], method, declaringTypeName, methodName)) continue;
                if (ResultFeedsConditionalBranch(instructions, i, lookahead)) count++;
            }
            return count;
        }

        /// <summary>
        /// 统计「实参来自生产者调用」的调用点数量（R12-02 判据）：call 前第一条非 nop 指令必须是对
        /// producer 的调用，或是一个局部变量的 ldloc 且该变量的 stloc 紧接 producer 调用。
        /// `ApplyRetryOutcome(new Bug1RetryOutcome())`（newobj/initobj 默认值）因此被判红。
        /// </summary>
        internal static int CountCallsWithArgumentFromProducer(
            MethodBase method, string calleeType, string calleeName, string producerType, string producerName)
        {
            List<IlInstruction> instructions = ReadInstructions(method);
            if (instructions == null) return -1;
            int count = 0;
            for (int i = 0; i < instructions.Count; i++)
            {
                if (!IsCallTo(instructions[i], method, calleeType, calleeName)) continue;
                if (ArgumentComesFromProducer(instructions, i, method, producerType, producerName)) count++;
            }
            return count;
        }

        /// <summary>R18-04：判定「call 的返回值是否真的决定了一个条件分支」。internal 供离线用例
        /// 直接喂入手工构造的 Debug 否定形态（ldloc; ldc.i4.0; ceq; stloc; ldloc; br）做正反证据。</summary>
        internal static bool ResultFeedsConditionalBranch(List<IlInstruction> instructions, int callIndex, int lookahead)
        {
            // R18-04：按数据流判定「call 的返回值是否决定了一个条件分支」。
            // Release 形态是 call; brtrue；Debug 形态是 call; ldc.i4.0; ceq; stloc L; ldloc L; brfalse，
            // 也可能出现 call; stloc L; ldloc L; brtrue。三种都落在同一状态机里；call; pop 一律判红。
            bool pending = true;
            var tainted = new HashSet<int>();
            int inspected = 0;
            for (int i = callIndex + 1; i < instructions.Count && inspected < lookahead; i++)
            {
                IlInstruction current = instructions[i];
                if (current.OpCode.Value == OpCodes.Nop.Value) continue;
                inspected++;
                if (current.OpCode == OpCodes.Pop) return false;
                if (IsConditionalBranch(current.OpCode)) return pending;
                int local;
                if (TryGetStoreLocal(current, out local))
                {
                    tainted.Add(local);
                    pending = false;
                    continue;
                }
                if (TryGetLoadLocal(current, out local))
                {
                    pending = tainted.Contains(local);
                    continue;
                }
                if (IsInt32Constant(current)) continue;
                if (IsComparisonOperator(current.OpCode)) continue;
                if (current.OpCode == OpCodes.Dup) continue;
                return false;
            }
            return false;
        }

        private static bool ArgumentComesFromProducer(
            List<IlInstruction> instructions, int callIndex, MethodBase owner, string producerType, string producerName)
        {
            int previous = PreviousNonNop(instructions, callIndex - 1);
            if (previous < 0) return false;
            IlInstruction argument = instructions[previous];
            if (IsCallTo(argument, owner, producerType, producerName)) return true;
            int loadedLocal;
            if (!TryGetLoadLocal(argument, out loadedLocal)) return false;
            for (int i = previous - 1; i >= 0; i--)
            {
                if (instructions[i].OpCode.Value == OpCodes.Nop.Value) continue;
                int storedLocal;
                if (!TryGetStoreLocal(instructions[i], out storedLocal) || storedLocal != loadedLocal) continue;
                int before = PreviousNonNop(instructions, i - 1);
                return before >= 0 && IsCallTo(instructions[before], owner, producerType, producerName);
            }
            return false;
        }

        private static int PreviousNonNop(List<IlInstruction> instructions, int index)
        {
            for (int i = index; i >= 0; i--)
            {
                if (instructions[i].OpCode.Value != OpCodes.Nop.Value) return i;
            }
            return -1;
        }

        private static bool IsConditionalBranch(OpCode opCode)
        {
            return opCode.FlowControl == FlowControl.Cond_Branch;
        }

        /// <summary>R18-04：否定比较会用到的比较算子（只做栈上变换，不引入新来源）。</summary>
        private static bool IsComparisonOperator(OpCode opCode)
        {
            return opCode == OpCodes.Ceq || opCode == OpCodes.Cgt || opCode == OpCodes.Cgt_Un
                || opCode == OpCodes.Clt || opCode == OpCodes.Clt_Un;
        }

        /// <summary>R18-04：ldc.i4 系列常量（含 ldc.i4.m1 与短/长形式）。</summary>
        private static bool IsInt32Constant(IlInstruction instruction)
        {
            int value = (ushort)instruction.OpCode.Value;
            if (value >= 0x15 && value <= 0x1e) return true;
            return value == 0x1f || value == 0x20;
        }

        private static bool TryGetLoadLocal(IlInstruction instruction, out int index)
        {
            index = -1;
            int value = (ushort)instruction.OpCode.Value;
            if (value >= 0x06 && value <= 0x09) { index = value - 0x06; return true; }
            if (value == 0x11 || value == 0xfe0c) { index = instruction.Operand; return true; }
            return false;
        }

        private static bool TryGetStoreLocal(IlInstruction instruction, out int index)
        {
            index = -1;
            int value = (ushort)instruction.OpCode.Value;
            if (value >= 0x0a && value <= 0x0d) { index = value - 0x0a; return true; }
            if (value == 0x13 || value == 0xfe0e) { index = instruction.Operand; return true; }
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
