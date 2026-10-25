using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace SteamP2PFriends.WhitelistTests
{
    /// <summary>
    /// E02 §7 native leaf probe（v3，主调度核对后纠正）：窗口取自真实
    /// Assembly-CSharp.dll 的 ItemManager.onRegionUpdated step5 共享呈现段
    /// （本版本锚点 IL_020A..0338：items 门 → instantiationsToInsert 清空 →
    /// foreach 填参 → 唯一 finally EH → FindInsertionIndex → pendingInstantiations
    /// .InsertRange）。仅重定位外部访问链：region 数组→arg0、pending→arg1、
    /// scratch(instantiationsToInsert)→arg2、x/y/sort→arg3..5；generateItems/
    /// askItems/DestroyAllInRegion 等 Unity 动作不在窗口内。其余指令、Enumerator
    /// finally、分支原样复制；窗口出口（原 IL_0339 循环推进）映射为唯一 ret。
    /// </summary>
    internal static class NativePendingBatchLeafProbe
    {
        internal sealed class ProbeResult
        {
            public Action<object, object, object, byte, byte, float, object, Action<object>>
                Consume;
            public List<string> Detail = new List<string>();
        }

        internal static readonly Type ParamType = typeof(SDG.Unturned.ItemManager).Assembly
            .GetType("SDG.Unturned.ItemInstantiationParameters", true);
        internal static readonly Type ParamListType = typeof(List<>).MakeGenericType(ParamType);
        internal static readonly Type RegionType = typeof(SDG.Unturned.ItemManager).Assembly
            .GetType("SDG.Unturned.ItemRegion", true);
        internal static readonly Type ItemDataType = typeof(SDG.Unturned.ItemManager).Assembly
            .GetType("SDG.Unturned.ItemData", true);
        internal static readonly Type ItemType = typeof(SDG.Unturned.ItemManager).Assembly
            .GetType("SDG.Unturned.Item", true);
        internal static readonly Type LoadedRegionType = typeof(SDG.Unturned.ItemManager)
            .Assembly.GetType("SDG.Unturned.LoadedRegion", true);

        internal static Action SpawnObserver;

        private static readonly Dictionary<short, OpCode> RefEmitMap = BuildRefEmitMap();

        private static Dictionary<short, OpCode> BuildRefEmitMap()
        {
            var map = new Dictionary<short, OpCode>();
            foreach (FieldInfo field in typeof(OpCodes).GetFields(
                BindingFlags.Public | BindingFlags.Static))
            {
                if (field.GetValue(null) is OpCode op) map[op.Value] = op;
            }
            return map;
        }

        internal static ProbeResult Build(List<string> detail)
        {
            var result = new ProbeResult { Detail = detail ?? new List<string>() };
            Assembly asm = typeof(SDG.Unturned.ItemManager).Assembly;
            Type managerType = asm.GetType("SDG.Unturned.ItemManager");
            MethodInfo target = managerType.GetMethod("onRegionUpdated",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (target == null) throw new InvalidOperationException(
                "probe: ItemManager.onRegionUpdated unresolved");
            var dmd = new MonoMod.Utils.DynamicMethodDefinition(target);
            System.Collections.Generic.IList<Mono.Cecil.Cil.Instruction> codes =
                dmd.Definition.Body.Instructions;

            int start = FindWindowStart(codes);
            int stop = FindWindowEnd(codes, start);
            result.Detail.Add("window=" + codes[start].Offset.ToString("X4")
                + ".." + codes[stop].Offset.ToString("X4"));

            var asmName = new AssemblyName("SteamP2PFriendsNativeLeafProbe");
            var asmBuilder = AppDomain.CurrentDomain.DefineDynamicAssembly(
                asmName, AssemblyBuilderAccess.RunAndSave);
            var moduleBuilder = asmBuilder.DefineDynamicModule(
                "SteamP2PFriendsNativeLeafProbe", "SteamP2PFriendsNativeLeafProbe.dll");
            var typeBuilder = moduleBuilder.DefineType("NativePendingBatchLeafHolder",
                TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
            var methodBuilder = typeBuilder.DefineMethod("NativePendingBatchLeaf",
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(void),
                new[] { typeof(object), typeof(object), typeof(object),
                    typeof(byte), typeof(byte), typeof(float),
                    typeof(object), typeof(object) });
            ILGenerator verifyIl = methodBuilder.GetILGenerator();

            var dyn = new DynamicMethod("NativePendingBatchLeaf",
                typeof(void),
                new[] { typeof(object), typeof(object), typeof(object),
                    typeof(byte), typeof(byte), typeof(float),
                    typeof(object), typeof(object) },
                typeof(NativePendingBatchLeafProbe).Module,
                skipVisibility: true);
            ILGenerator runIl = dyn.GetILGenerator();

            var window = new List<Mono.Cecil.Cil.Instruction>();
            var offsets = new Dictionary<int, int>();
            for (int i = start; i <= stop; i++)
            {
                offsets[codes[i].Offset] = window.Count;
                window.Add(codes[i]);
            }

            // E02 §2.1：本地生成段（IsLocalPlayer→generateItems）与 Dedicated 分流
            // （IsDedicatedServer→askItems→br 出口）由同一 Candidate Writer/generation
            // 政策与非 Dedicated 模式证明不执行，窗口内跳过发射（不传假布尔）。
            var skipIndexes = new HashSet<int>();
            for (int i = 0; i < window.Count; i++)
            {
                if (window[i].OpCode.Code != Mono.Cecil.Cil.Code.Call
                    && window[i].OpCode.Code != Mono.Cecil.Cil.Code.Callvirt) continue;
                string scanName = ((Mono.Cecil.MethodReference)window[i].Operand).Name;
                if (scanName == "get_IsLocalPlayer")
                {
                    int skipFrom = i - 1; // get_channel
                    if (skipFrom - 1 >= 0
                        && window[skipFrom - 1].OpCode.Code == Mono.Cecil.Cil.Code.Ldarg_S)
                        skipFrom--; // ldarg.1 (player)
                    for (int j = Math.Max(0, skipFrom); j < window.Count; j++)
                    {
                        skipIndexes.Add(j);
                        if (window[j].OpCode.Code == Mono.Cecil.Cil.Code.Call
                            && ((Mono.Cecil.MethodReference)window[j].Operand).Name
                                == "generateItems")
                            break;
                    }
                }
                if (scanName == "get_IsDedicatedServer")
                {
                    for (int j = i; j < window.Count; j++)
                    {
                        skipIndexes.Add(j);
                        if (window[j].Operand is Mono.Cecil.Cil.Instruction branchOut
                            && !offsets.ContainsKey(branchOut.Offset))
                            break; // askItems 后跳到邻域推进（窗口外）＝段尾
                    }
                }
            }
            result.Detail.Add("skipped=" + skipIndexes.Count);
            if (skipIndexes.Count > 0)
            {
                result.Detail.Add("skipped-window-indexes="
                    + string.Join(",", skipIndexes.OrderBy(k => k).Select(k => k.ToString())
                        .ToArray()));
            }

            var localTypes = new Dictionary<int, Mono.Cecil.TypeReference>();
            foreach (Mono.Cecil.Cil.VariableDefinition variable in dmd.Definition.Body.Variables)
                localTypes[variable.Index] = variable.VariableType;

            var usedIndexes = new List<int>();
            foreach (Mono.Cecil.Cil.Instruction scan in window)
                CollectLocalIndex(scan, usedIndexes);
            usedIndexes.Sort();
            int maxSlot = usedIndexes.Count == 0
                ? -1 : usedIndexes[usedIndexes.Count - 1];

            var verifyLabels = new List<Label>();
            var verifyLocals = new Dictionary<int, LocalBuilder>();
            var runLabels = new List<Label>();
            var runLocals = new Dictionary<int, LocalBuilder>();
            for (int slot = 0; slot <= maxSlot; slot++)
            {
                Type slotType = typeof(object);
                if (localTypes.TryGetValue(slot, out Mono.Cecil.TypeReference slotTypeRef))
                    slotType = ResolveLocalType(slotTypeRef);
                verifyLocals[slot] = verifyIl.DeclareLocal(slotType);
                runLocals[slot] = runIl.DeclareLocal(slotType);
            }
            for (int i = 0; i < window.Count; i++)
            {
                verifyLabels.Add(verifyIl.DefineLabel());
                runLabels.Add(runIl.DefineLabel());
            }
            verifyLabels.Add(verifyIl.DefineLabel());
            runLabels.Add(runIl.DefineLabel());

            // 窗口内 EH（本版本为 foreach 的唯一 finally）：offset 从 Cecil body 取
            int tryStartOffset = -1;
            int finallyStartOffset = -1;
            int handlerEndOffset = -1;
            foreach (Mono.Cecil.Cil.ExceptionHandler handler in dmd.Definition.Body.ExceptionHandlers)
            {
                if (handler.TryStart == null || handler.HandlerStart == null) continue;
                bool tryInside = window.Any(w => w.Offset == handler.TryStart.Offset);
                bool handlerInside = window.Any(w => w.Offset == handler.HandlerStart.Offset);
                if (tryInside && handlerInside)
                {
                    tryStartOffset = handler.TryStart.Offset;
                    finallyStartOffset = handler.HandlerStart.Offset;
                    handlerEndOffset = handler.HandlerEnd.Offset;
                }
            }

            EmitWindow(verifyIl, window, offsets, verifyLabels, verifyLocals,
                localTypes, tryStartOffset, finallyStartOffset, handlerEndOffset,
                skipIndexes, result);
            EmitWindow(runIl, window, offsets, runLabels, runLocals,
                localTypes, tryStartOffset, finallyStartOffset, handlerEndOffset,
                skipIndexes, result);

            Type holder = typeBuilder.CreateType();
            result.Detail.Add("saved-EH=" + holder.GetMethod("NativePendingBatchLeaf")
                .GetMethodBody().ExceptionHandlingClauses.Count);
            try
            {
                asmBuilder.Save("SteamP2PFriendsNativeLeafProbe.dll");
                result.Detail.Add("probe-saved="
                    + Path.GetFullPath("SteamP2PFriendsNativeLeafProbe.dll"));
            }
            catch (Exception saveError)
            {
                result.Detail.Add("probe-save-failed=" + saveError.GetType().Name);
            }
            result.Consume = (Action<object, object, object, byte, byte, float, object,
                Action<object>>)
                dyn.CreateDelegate(typeof(Action<object, object, object, byte, byte, float,
                    object, Action<object>>));
            result.Detail.Add("probe-generated=true");
            return result;
        }

        private static void CollectLocalIndex(
            Mono.Cecil.Cil.Instruction scan, List<int> usedIndexes)
        {
            int scanned = -1;
            switch (scan.OpCode.Code)
            {
                case Mono.Cecil.Cil.Code.Stloc:
                case Mono.Cecil.Cil.Code.Stloc_S:
                case Mono.Cecil.Cil.Code.Ldloc:
                case Mono.Cecil.Cil.Code.Ldloc_S:
                    scanned = ((Mono.Cecil.Cil.VariableDefinition)scan.Operand).Index;
                    break;
                case Mono.Cecil.Cil.Code.Stloc_0:
                case Mono.Cecil.Cil.Code.Stloc_1:
                case Mono.Cecil.Cil.Code.Stloc_2:
                case Mono.Cecil.Cil.Code.Stloc_3:
                    scanned = scan.OpCode.Value - (short)OpCodes.Stloc_0.Value;
                    break;
                case Mono.Cecil.Cil.Code.Ldloc_0:
                case Mono.Cecil.Cil.Code.Ldloc_1:
                case Mono.Cecil.Cil.Code.Ldloc_2:
                case Mono.Cecil.Cil.Code.Ldloc_3:
                    scanned = scan.OpCode.Value - (short)OpCodes.Ldloc_0.Value;
                    break;
            }
            if (scanned >= 0 && !usedIndexes.Contains(scanned)) usedIndexes.Add(scanned);
        }

        private static void EmitWindow(
            ILGenerator il,
            List<Mono.Cecil.Cil.Instruction> window,
            Dictionary<int, int> offsets,
            List<Label> labels,
            Dictionary<int, LocalBuilder> locals,
            Dictionary<int, Mono.Cecil.TypeReference> localTypes,
            int tryStartOffset,
            int finallyStartOffset,
            int handlerEndOffset,
            HashSet<int> skipIndexes,
            ProbeResult result)
        {
            try
            {
                // 发射窗口前把 arg3/4/5 存入原号 locals V3/V4/V5（所有 ldloc 形式读同一值）
                il.Emit(OpCodes.Ldarg_3); il.Emit(OpCodes.Stloc, locals[3]);
                il.Emit(OpCodes.Ldarg_S, (byte)4); il.Emit(OpCodes.Stloc, locals[4]);
                il.Emit(OpCodes.Ldarg_S, (byte)5); il.Emit(OpCodes.Stloc, locals[5]);
                for (int i = 0; i < window.Count; i++)
                {
                    Mono.Cecil.Cil.Instruction ins = window[i];
                    string offset = "IL_" + ins.Offset.ToString("X4");

                    if (skipIndexes.Contains(i))
                    {
                        // 省略段仍占 label 位（分支目标可能指向段边界后的第一条）
                        il.MarkLabel(labels[i]);
                        Log(result, "skip:" + ins.OpCode.Name + "@" + offset);
                        continue;
                    }

                    if (ins.Offset == tryStartOffset) il.BeginExceptionBlock();
                    if (ins.Offset == finallyStartOffset) il.BeginFinallyBlock();
                    if (ins.Offset == handlerEndOffset) il.EndExceptionBlock();
                    il.MarkLabel(labels[i]);

                    // Player→movement→loadedRegions 访问链整体重定位为 arg6：
                    // ldarg.1 / get_movement / get_loadedRegions 不压栈，由后续
                    // LoadedRegion[,] Get 重定位压 arg6
                    if (ins.OpCode.Code == Mono.Cecil.Cil.Code.Ldarg_1
                        || (ins.OpCode.Code == Mono.Cecil.Cil.Code.Ldarg_S
                            && ((Mono.Cecil.ParameterDefinition)ins.Operand).Index == 1))
                    {
                        Log(result, "reloc:player-nop@" + offset);
                        continue;
                    }
                    if (ins.OpCode.Code == Mono.Cecil.Cil.Code.Callvirt)
                    {
                        string virtName = ((Mono.Cecil.MethodReference)ins.Operand).Name;
                        if (virtName == "get_movement" || virtName == "get_loadedRegions"
                            || virtName == "get_channel" || virtName == "get_IsLocalPlayer")
                        {
                            Log(result, "reloc:player-chain-nop@" + offset);
                            continue;
                        }
                    }
                    switch (ins.OpCode.Code)
                    {
                        case Mono.Cecil.Cil.Code.Call:
                        {
                            string name = ((Mono.Cecil.MethodReference)ins.Operand).Name;
                            if (name == "get_regions")
                            {
                                // regions[x,y] 访问链：数组取 itself 由 Get 处理
                                Log(result, "reloc:regions-nop@" + offset);
                                continue;
                            }
                            if (name == "Get")
                            {
                                // 判定 Get 的声明数组：LoadedRegion[,] → arg6；ItemRegion[,] → arg0
                                string declaring = ((Mono.Cecil.MethodReference)ins.Operand)
                                    .DeclaringType.Name;
                                bool loadedArray = declaring.Contains("LoadedRegion");
                                il.Emit(OpCodes.Pop);
                                il.Emit(OpCodes.Pop);
                                if (loadedArray)
                                {
                                    il.Emit(OpCodes.Ldarg_S, (byte)6);
                                    il.Emit(OpCodes.Castclass, LoadedRegionType);
                                    Log(result, "reloc:loaded-arg6@" + offset);
                                }
                                else
                                {
                                    il.Emit(OpCodes.Ldarg_0);
                                    il.Emit(OpCodes.Castclass, RegionType);
                                    Log(result, "reloc:region-arg0@" + offset);
                                }
                                continue;
                            }
                            if (name == "DestroyAllInRegion")
                            {
                                // Unity 动作槽 → arg7 外部记录动作：先把栈上原实参
                                // （Get 重定位压入的 region）存入有类型临时 local 消费，
                                // 再按 [delegate, region] 形参序调用（同一原实参，不另造）
                                LocalBuilder regionArgument = il.DeclareLocal(RegionType);
                                il.Emit(OpCodes.Stloc, regionArgument);
                                il.Emit(OpCodes.Ldarg_S, (byte)7);
                                il.Emit(OpCodes.Castclass, typeof(Action<object>));
                                il.Emit(OpCodes.Ldloc, regionArgument);
                                il.Emit(OpCodes.Callvirt, typeof(Action<object>)
                                    .GetMethod("Invoke", new[] { typeof(object) }));
                                Log(result, "reloc:destroy-arg7@" + offset);
                                continue;
                            }
                            if (name == "HorizontalDistanceFromCenterSquared")
                            {
                                // sort 为外部位置输入（窗口前已装 V5）：弹 x,y,cameraPos，
                                // 压 arg5 供随后的 stloc.s V_5 原样存回
                                il.Emit(OpCodes.Pop);
                                il.Emit(OpCodes.Pop);
                                il.Emit(OpCodes.Pop);
                                il.Emit(OpCodes.Ldarg_S, (byte)5);
                                Log(result, "reloc:sort-arg5@" + offset);
                                continue;
                            }
                            if (name == "FindInsertionIndex")
                            {
                                MethodInfo insertion = typeof(SDG.Unturned.ListExtension)
                                    .GetMethods(BindingFlags.Static
                                        | BindingFlags.Public | BindingFlags.NonPublic)
                                    .Single(m => m.Name == "FindInsertionIndex"
                                        && m.GetParameters().Length == 2)
                                    .MakeGenericMethod(ParamType);
                                il.Emit(OpCodes.Call, insertion);
                                Log(result, "emit:FindInsertionIndex@" + offset);
                                continue;
                            }
                            if (name == "get_Current" || name == "MoveNext" || name == "Dispose")
                            {
                                MethodInfo enumerator = ResolveEnumeratorMethod(name);
                                if (enumerator == null)
                                    throw new InvalidOperationException("probe: enumerator."
                                        + name + " unresolved @" + offset);
                                il.Emit(name == "Dispose"
                                    ? OpCodes.Callvirt : OpCodes.Call, enumerator);
                                Log(result, "emit:" + name + "@" + offset);
                                continue;
                            }
                            throw new InvalidOperationException("probe: call " + name
                                + " unexpected @" + offset);
                        }
                        case Mono.Cecil.Cil.Code.Ldsfld:
                        {
                            string name = ((Mono.Cecil.FieldReference)ins.Operand).Name;
                            if (name == "pendingInstantiations")
                            {
                                il.Emit(OpCodes.Ldarg_1);
                                il.Emit(OpCodes.Castclass, ParamListType);
                                Log(result, "reloc:pending-arg1@" + offset);
                                continue;
                            }
                            if (name == "instantiationsToInsert")
                            {
                                il.Emit(OpCodes.Ldarg_2);
                                il.Emit(OpCodes.Castclass, ParamListType);
                                Log(result, "reloc:scratch-arg2@" + offset);
                                continue;
                            }
                            throw new InvalidOperationException("probe: ldsfld " + name
                                + " unexpected @" + offset);
                        }
                        case Mono.Cecil.Cil.Code.Ldloc:
                        case Mono.Cecil.Cil.Code.Ldloc_S:
                        {
                            int index = ((Mono.Cecil.Cil.VariableDefinition)ins.Operand).Index;
                            // V3/V4/V5 已在窗口前由参数装载；所有 ldloc 形式读同一 locals
                            il.Emit(OpCodes.Ldloc, locals[index]);
                            continue;
                        }
                        case Mono.Cecil.Cil.Code.Ldloc_0:
                        case Mono.Cecil.Cil.Code.Ldloc_1:
                        case Mono.Cecil.Cil.Code.Ldloc_2:
                        case Mono.Cecil.Cil.Code.Ldloc_3:
                        {
                            int index = ins.OpCode.Value - (short)OpCodes.Ldloc_0.Value;
                            il.Emit(OpCodes.Ldloc, locals[index]);
                            continue;
                        }
                        case Mono.Cecil.Cil.Code.Ldloca:
                        case Mono.Cecil.Cil.Code.Ldloca_S:
                        {
                            Log(result, "step:ldloca@" + offset
                                + " operand=" + (ins.Operand == null
                                    ? "null" : ins.Operand.GetType().Name));
                            int index = ((Mono.Cecil.Cil.VariableDefinition)ins.Operand).Index;
                            il.Emit(OpCodes.Ldloca, locals[index]);
                            continue;
                        }
                        case Mono.Cecil.Cil.Code.Stloc:
                        case Mono.Cecil.Cil.Code.Stloc_S:
                        {
                            int index = ((Mono.Cecil.Cil.VariableDefinition)ins.Operand).Index;
                            il.Emit(OpCodes.Stloc, locals[index]);
                            continue;
                        }
                        case Mono.Cecil.Cil.Code.Stloc_0:
                        case Mono.Cecil.Cil.Code.Stloc_1:
                        case Mono.Cecil.Cil.Code.Stloc_2:
                        case Mono.Cecil.Cil.Code.Stloc_3:
                        {
                            il.Emit(ResolveOp(ins));
                            continue;
                        }
                        case Mono.Cecil.Cil.Code.Callvirt:
                        {
                            string name = ((Mono.Cecil.MethodReference)ins.Operand).Name;
                            if (name == "get_Current" || name == "MoveNext" || name == "Dispose")
                            {
                                // 原生 constrained Enumerator + callvirt IDisposable.Dispose：
                                // Dispose 目标必须是接口方法，不是值类型具体 Dispose
                                MethodInfo method = name == "Dispose"
                                    ? typeof(IDisposable).GetMethod("Dispose")
                                    : ResolveEnumeratorMethod(name);
                                if (method == null)
                                    throw new InvalidOperationException("probe: enumerator."
                                        + name + " unresolved @" + offset);
                                il.Emit(OpCodes.Callvirt, method);
                                Log(result, "emit:" + name + "@" + offset);
                                continue;
                            }
                            MethodInfo resolved = ResolveMethod(name);
                            if (resolved == null)
                                throw new InvalidOperationException("probe: callvirt " + name
                                    + " unresolved @" + offset);
                            il.Emit(OpCodes.Callvirt, resolved);
                            Log(result, "emit:" + resolved.Name + "@" + offset);
                            continue;
                        }
                        case Mono.Cecil.Cil.Code.Ldfld:
                        {
                            string name = ((Mono.Cecil.FieldReference)ins.Operand).Name;
                            FieldInfo field = RegionType.GetField(name,
                                BindingFlags.Instance | BindingFlags.Public
                                    | BindingFlags.NonPublic)
                                ?? ParamType.GetField(name, BindingFlags.Instance
                                    | BindingFlags.Public | BindingFlags.NonPublic)
                                ?? ItemDataType.GetField(name,
                                    BindingFlags.Instance | BindingFlags.Public
                                        | BindingFlags.NonPublic)
                                ?? ItemType.GetField(name,
                                    BindingFlags.Instance | BindingFlags.Public
                                        | BindingFlags.NonPublic)
                                ?? LoadedRegionType.GetField(name,
                                    BindingFlags.Instance | BindingFlags.Public
                                        | BindingFlags.NonPublic);
                            if (field == null)
                                throw new InvalidOperationException("probe: ldfld " + name
                                    + " unresolved @" + offset);
                            il.Emit(ResolveOp(ins), field);
                            Log(result, "emit:ldfld." + name + "@" + offset);
                            continue;
                        }
                        case Mono.Cecil.Cil.Code.Ldarg_S:
                        {
                            // V5(sortOrder) 读改为参数 sort（外部预计算）
                            int index = ((Mono.Cecil.ParameterDefinition)ins.Operand).Index;
                            if (index == 5)
                            {
                                il.Emit(OpCodes.Ldarg_S, (byte)5);
                                Log(result, "reloc:v5-sort-arg5@" + offset);
                                continue;
                            }
                            throw new InvalidOperationException("probe: ldarg.s " + index
                                + " unexpected @" + offset);
                        }
                        case Mono.Cecil.Cil.Code.Conv_U1:
                        {
                            // 邻域循环变量 conv.u1 保留（参数已是 byte，无副作用）
                            il.Emit(ResolveOp(ins));
                            continue;
                        }
                        case Mono.Cecil.Cil.Code.Initobj:
                        {
                            // operand 是 TypeReference；byref 已由前一指令 ldloca 压栈
                            il.Emit(OpCodes.Initobj, ParamType);
                            Log(result, "emit:initobj@" + offset);
                            continue;
                        }
                        case Mono.Cecil.Cil.Code.Endfinally:
                        {
                            // EndExceptionBlock 会发射真实 endfinally 并登记 EH；
                            // 结构化 API 承接同一个 handler，不再手工 emit
                            Log(result, "structured:endfinally@" + offset);
                            continue;
                        }
                        case Mono.Cecil.Cil.Code.Constrained:
                        {
                            il.Emit(OpCodes.Constrained,
                                typeof(List<SDG.Unturned.ItemData>.Enumerator));
                            Log(result, "emit:constrained@" + offset);
                            continue;
                        }
                        case Mono.Cecil.Cil.Code.Stfld:
                        {
                            string name = ((Mono.Cecil.FieldReference)ins.Operand).Name;
                            FieldInfo field = ParamType.GetField(name,
                                BindingFlags.Instance | BindingFlags.Public
                                    | BindingFlags.NonPublic);
                            if (field == null && name == "isItemsLoaded")
                                field = LoadedRegionType.GetField(name,
                                    BindingFlags.Instance | BindingFlags.Public
                                        | BindingFlags.NonPublic);
                            if (field == null && name == "isNetworked")
                                field = RegionType.GetField(name,
                                    BindingFlags.Instance | BindingFlags.Public
                                        | BindingFlags.NonPublic);
                            if (field == null)
                                throw new InvalidOperationException("probe: stfld " + name
                                    + " unresolved @" + offset);
                            il.Emit(ResolveOp(ins), field);
                            Log(result, "emit:stfld." + name + "@" + offset);
                            continue;
                        }
                        case Mono.Cecil.Cil.Code.Leave:
                        case Mono.Cecil.Cil.Code.Leave_S:
                        {
                            // 0305 leave.s→0315 必须保持 Leave 长形式（0315 label 在
                            // EndExceptionBlock 之后、handler 外），不能改 br
                            int target = ((Mono.Cecil.Cil.Instruction)ins.Operand).Offset;
                            if (!offsets.TryGetValue(target, out int targetIndex))
                                targetIndex = window.Count; // 窗口出口 → ret
                            il.Emit(OpCodes.Leave, labels[targetIndex]);
                            Log(result, "emit:leave@" + offset);
                            continue;
                        }
                        default:
                        {
                            if (ins.Operand is Mono.Cecil.Cil.Instruction branchTarget)
                            {
                                if (!offsets.TryGetValue(branchTarget.Offset, out int targetIndex))
                                    targetIndex = window.Count; // 窗口出口 → ret
                                il.Emit(ResolveOp(ins), labels[targetIndex]);
                                Log(result, "emit:" + ins.OpCode.Name + "@" + offset);
                                continue;
                            }
                            if (!RefEmitMap.TryGetValue(ins.OpCode.Value, out OpCode emit))
                                throw new InvalidOperationException("probe: opcode "
                                    + ins.OpCode.Name + " unmapped @" + offset);
                            if (emit.OperandType != OperandType.InlineNone)
                                throw new InvalidOperationException("probe: opcode "
                                    + ins.OpCode.Name + " operand unresolved @" + offset);
                            il.Emit(emit);
                            continue;
                        }
                    }
                }
                il.MarkLabel(labels[window.Count]);
                il.Emit(OpCodes.Ret);
            }
            catch (Exception error)
            {
                string steps = result != null && result.Detail != null
                    ? string.Join(",", result.Detail.ToArray()) : "no-detail";
                throw new InvalidOperationException("probe build failed: " + error
                    + " steps=" + steps, error);
            }
        }

        private static OpCode ResolveOp(Mono.Cecil.Cil.Instruction ins)
        {
            if (!RefEmitMap.TryGetValue(ins.OpCode.Value, out OpCode op))
                throw new InvalidOperationException("probe: opcode " + ins.OpCode.Name + " unmapped");
            return op;
        }

        private static Type ResolveLocalType(Mono.Cecil.TypeReference type)
        {
            string fullName = type.FullName;
            if (fullName == "System.Int32") return typeof(int);
            if (fullName == "System.Byte") return typeof(byte);
            if (fullName == "System.Single") return typeof(float);
            if (fullName == "UnityEngine.Vector3") return typeof(UnityEngine.Vector3);
            if (fullName == "SDG.Unturned.ItemInstantiationParameters") return ParamType;
            if (fullName == "SDG.Unturned.ItemData") return ItemDataType;
            if (fullName.StartsWith("System.Collections.Generic.List`1/Enumerator")
                || (fullName.Contains("Enumerator") && fullName.Contains("ItemData")))
                return typeof(List<SDG.Unturned.ItemData>.Enumerator);
            throw new InvalidOperationException("probe: local type " + fullName + " unresolved");
        }

        private static void Log(ProbeResult result, string message)
        {
            if (result != null) result.Detail.Add(message);
        }

        private static MethodInfo ResolveEnumeratorMethod(string name)
        {
            MethodInfo method = typeof(List<SDG.Unturned.ItemData>.Enumerator)
                .GetMethod(name, BindingFlags.Instance | BindingFlags.Public);
            if (method == null)
                throw new InvalidOperationException("probe: enumerator." + name + " unresolved");
            return method;
        }

        private static MethodInfo ResolveMethod(string name)
        {
            try
            {
                switch (name)
                {
                    case "GetEnumerator":
                        return ItemDataType == null ? null
                            : typeof(List<SDG.Unturned.ItemData>).GetMethod("GetEnumerator",
                                Type.EmptyTypes);
                    case "Clear":
                        return ParamListType.GetMethod("Clear", Type.EmptyTypes);
                    case "Add":
                        return ParamListType.GetMethod("Add", new[] { ParamType });
                    case "InsertRange":
                        return ParamListType.GetMethod("InsertRange",
                            new[] { typeof(int), typeof(System.Collections.Generic.IEnumerable<>)
                                .MakeGenericType(ParamType) });
                    case "get_item":
                    case "get_id":
                    case "get_point":
                    case "get_instanceID":
                    case "get_Count":
                    case "get_Item":
                    {
                        // Cecil operand 是 getter 方法名；属性名需剥离 get_ 前缀
                        string propertyName = name.Substring("get_".Length);
                        PropertyInfo property = (name == "get_Count"
                            ? (Type)typeof(List<SDG.Unturned.ItemData>)
                            : name == "get_Item" ? ParamListType
                            : name == "get_id" ? ItemType
                            : ItemDataType)
                            .GetProperty(propertyName);
                        if (property == null)
                            throw new InvalidOperationException("probe: property " + name
                                + " missing on " + ItemDataType.Assembly.Location);
                        MethodInfo getter = property.GetGetMethod();
                        if (getter == null)
                            throw new InvalidOperationException("probe: getter " + name
                                + " missing");
                        return getter;
                    }
                    default:
                        throw new InvalidOperationException("probe: method " + name + " unresolved");
                }
            }
            catch (Exception error)
            {
                throw new InvalidOperationException("probe: ResolveMethod(" + name
                    + ") failed: " + error, error);
            }
        }

        private static int FindIndexByOffset(
            List<Mono.Cecil.Cil.Instruction> window, int offset)
        {
            for (int i = 0; i < window.Count; i++)
                if (window[i].Offset == offset) return i;
            return -1;
        }

        private static int FindWindowStart(
            System.Collections.Generic.IList<Mono.Cecil.Cil.Instruction> codes)
        {
            // 锚点（E02 §2.1）：loadedRegions[x,y].isItemsLoaded 读取门
            // （ldarg.1 → movement → loadedRegions → Get(V3,V4) → ldfld isItemsLoaded，
            // 即 IL_0163..017B）。step0 段（IL_00A8）结构相同，因此取最后一个命中。
            int last = -1;
            for (int i = 0; i + 5 < codes.Count; i++)
            {
                if (codes[i].OpCode.Code != Mono.Cecil.Cil.Code.Ldarg_1) continue;
                if (codes[i + 1].OpCode.Code != Mono.Cecil.Cil.Code.Callvirt
                    || ((Mono.Cecil.MethodReference)codes[i + 1].Operand).Name != "get_movement")
                    continue;
                if (codes[i + 2].OpCode.Code != Mono.Cecil.Cil.Code.Callvirt
                    || ((Mono.Cecil.MethodReference)codes[i + 2].Operand).Name != "get_loadedRegions")
                    continue;
                if (codes[i + 3].OpCode.Code != Mono.Cecil.Cil.Code.Ldloc_3) continue;
                if (codes[i + 4].OpCode.Code != Mono.Cecil.Cil.Code.Ldloc_S
                    && codes[i + 4].OpCode.Code != Mono.Cecil.Cil.Code.Ldloc) continue;
                return i;
            }
            throw new InvalidOperationException("probe: loaded-region gate anchor not found");
        }

        private static int FindWindowEnd(
            System.Collections.Generic.IList<Mono.Cecil.Cil.Instruction> codes, int start)
        {
            // 终点：InsertRange 调用（IL_0334 附近）
            for (int i = start; i < codes.Count; i++)
            {
                if (codes[i].OpCode.Code != Mono.Cecil.Cil.Code.Callvirt) continue;
                if (((Mono.Cecil.MethodReference)codes[i].Operand).Name != "InsertRange")
                    continue;
                return i;
            }
            throw new InvalidOperationException("probe: InsertRange anchor not found");
        }
    }
}
