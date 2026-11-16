using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace SteamP2PFriends.Core.Patches.GunshotHostAudio
{
    /// <summary>
    /// 自带的只读 IL 扫描器。
    ///
    /// 为什么不用 Harmony 的 PatchProcessor.GetCurrentInstructions：
    /// 它内部走 MonoMod.DynamicMethodDefinition，会解析方法体里的**每一个**元数据 token。
    /// UseableGun.fire() 的 IL 引用了 UnityEx 程序集；在缺少 UnityEx.dll 的进程
    /// （例如自动化测试宿主）里，解析会抛 FileNotFoundException，导致锚点验证无法进行。
    ///
    /// 本扫描器只在需要判断的指令上按需解析 token，且解析失败只把操作数留空，
    /// 不会因为无关 token 缺失而整体失败。生产运行时（Unity）两种方式等价；
    /// 测试宿主则只能走这一条路径。
    /// </summary>
    internal static class GunshotIlScan
    {
        internal sealed class IlInstruction
        {
            internal int Offset;
            internal OpCode OpCode;
            internal object Operand;
        }

        private static readonly Dictionary<short, OpCode> OpCodeTable = BuildOpCodeTable();

        internal static List<IlInstruction> Read(MethodBase method)
        {
            if (method == null)
            {
                throw new ArgumentNullException(nameof(method));
            }

            var result = new List<IlInstruction>();
            MethodBody body = method.GetMethodBody();
            if (body == null)
            {
                return result;
            }

            byte[] il = body.GetILAsByteArray();
            if (il == null || il.Length == 0)
            {
                return result;
            }

            Module module = method.Module;
            Type[] typeArguments = method.DeclaringType != null
                ? method.DeclaringType.GetGenericArguments()
                : Type.EmptyTypes;
            Type[] methodArguments = method.IsGenericMethod
                ? method.GetGenericArguments()
                : Type.EmptyTypes;

            int position = 0;
            while (position < il.Length)
            {
                int offset = position;
                short key = il[position++];
                if (key == 0xFE && position < il.Length)
                {
                    key = (short)(0xFE00 | il[position++]);
                }

                OpCode opcode;
                if (!OpCodeTable.TryGetValue(key, out opcode))
                {
                    throw new InvalidOperationException("unknown IL opcode 0x" + key.ToString("X4"));
                }

                object operand = ReadOperand(il, ref position, opcode, module, typeArguments, methodArguments);
                result.Add(new IlInstruction { Offset = offset, OpCode = opcode, Operand = operand });
            }

            return result;
        }

        private static object ReadOperand(
            byte[] il,
            ref int position,
            OpCode opcode,
            Module module,
            Type[] typeArguments,
            Type[] methodArguments)
        {
            switch (opcode.OperandType)
            {
                case OperandType.InlineNone:
                    return null;

                case OperandType.ShortInlineI:
                    return (int)(sbyte)il[position++];

                case OperandType.ShortInlineVar:
                    return (int)il[position++];

                case OperandType.ShortInlineBrTarget:
                {
                    sbyte delta = (sbyte)il[position++];
                    return position + delta;
                }

                case OperandType.InlineVar:
                {
                    int value = BitConverter.ToUInt16(il, position);
                    position += 2;
                    return value;
                }

                case OperandType.InlineI:
                {
                    int value = BitConverter.ToInt32(il, position);
                    position += 4;
                    return value;
                }

                case OperandType.InlineBrTarget:
                {
                    int delta = BitConverter.ToInt32(il, position);
                    position += 4;
                    return position + delta;
                }

                case OperandType.InlineI8:
                {
                    long value = BitConverter.ToInt64(il, position);
                    position += 8;
                    return value;
                }

                case OperandType.ShortInlineR:
                {
                    float value = BitConverter.ToSingle(il, position);
                    position += 4;
                    return value;
                }

                case OperandType.InlineR:
                {
                    double value = BitConverter.ToDouble(il, position);
                    position += 8;
                    return value;
                }

                case OperandType.InlineString:
                {
                    int token = BitConverter.ToInt32(il, position);
                    position += 4;
                    try
                    {
                        return module.ResolveString(token);
                    }
                    catch
                    {
                        return null;
                    }
                }

                case OperandType.InlineSwitch:
                {
                    int count = BitConverter.ToInt32(il, position);
                    position += 4;
                    int basePosition = position + (4 * count);
                    var targets = new int[count];
                    for (int i = 0; i < count; i++)
                    {
                        int delta = BitConverter.ToInt32(il, position);
                        position += 4;
                        targets[i] = basePosition + delta;
                    }

                    return targets;
                }

                case OperandType.InlineMethod:
                {
                    int token = BitConverter.ToInt32(il, position);
                    position += 4;
                    try
                    {
                        return module.ResolveMethod(token, typeArguments, methodArguments);
                    }
                    catch
                    {
                        return null;
                    }
                }

                case OperandType.InlineField:
                {
                    int token = BitConverter.ToInt32(il, position);
                    position += 4;
                    try
                    {
                        return module.ResolveField(token, typeArguments, methodArguments);
                    }
                    catch
                    {
                        return null;
                    }
                }

                case OperandType.InlineType:
                {
                    int token = BitConverter.ToInt32(il, position);
                    position += 4;
                    try
                    {
                        return module.ResolveType(token, typeArguments, methodArguments);
                    }
                    catch
                    {
                        return null;
                    }
                }

                case OperandType.InlineTok:
                {
                    int token = BitConverter.ToInt32(il, position);
                    position += 4;
                    try
                    {
                        return module.ResolveMethod(token, typeArguments, methodArguments);
                    }
                    catch
                    {
                        try
                        {
                            return module.ResolveField(token, typeArguments, methodArguments);
                        }
                        catch
                        {
                            try
                            {
                                return module.ResolveType(token, typeArguments, methodArguments);
                            }
                            catch
                            {
                                return null;
                            }
                        }
                    }
                }

                case OperandType.InlineSig:
                    position += 4;
                    return null;

                default:
                    throw new InvalidOperationException("unsupported operand type " + opcode.OperandType);
            }
        }

        private static Dictionary<short, OpCode> BuildOpCodeTable()
        {
            var table = new Dictionary<short, OpCode>();
            foreach (FieldInfo field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (field.FieldType != typeof(OpCode))
                {
                    continue;
                }

                OpCode opcode = (OpCode)field.GetValue(null);
                table[opcode.Value] = opcode;
            }

            return table;
        }
    }
}
