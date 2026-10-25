using HarmonyLib;
using SDG.Unturned;
using SteamP2PFriends.Adapters.Item;
using SteamP2PFriends.Shared;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;

namespace SteamP2PFriends.Adapters.Item.Patches
{
    public static class ItemOwnedPresentationRuntimePatch
    {
        public static int AnchorMatchCount { get; private set; }

        private const long InstantiationBudgetMilliseconds = 1L;
        private const int MinimumBatchActions = 5;

        /// <summary>E02 §8/§9：共同消费入口的分类回调（wrapper 与 fixture 同一实现）。</summary>
        internal static EConsumeOutcome ClassifyForConsumption(
            IItemLocalPresentationView view, PendingView pending,
            MethodInfo spawn, object receiver)
        {
            ItemSpawnResult classified = InvokeAndClassify(view, pending, spawn, receiver);
            // Uncertain/NotStarted 由 batch 停批处理；Registered→提交；带 Failure 的
            // Registered 仍提交（登记进度），后置失败独立上报
            if (classified.Progress == ItemSpawnProgress.Registered && classified.Failure != null)
                RoleLogger.Warn("[Host]", "[ItemPresentation] post-spawn failure instance="
                    + pending.InstanceId + " reason=" + classified.Reason
                    + " error=" + classified.Failure.GetType().Name);
            else if (classified.Progress == ItemSpawnProgress.NotStarted && classified.Failure == null)
                RoleLogger.Info("[Host]", "[ItemPresentation] spawn not-started instance="
                    + pending.InstanceId + " reason=" + classified.Reason);
            return classified.Progress == ItemSpawnProgress.Registered
                ? EConsumeOutcome.Consumed
                : classified.Progress == ItemSpawnProgress.NotStarted
                    ? EConsumeOutcome.NotStarted
                    : EConsumeOutcome.Uncertain;
        }

        public static bool ConsumeOwnedPendingBatch(ItemManager manager)
        {
            if (!ItemPresentationRetirement.IsRetiredListenHost()) return false;
            if (manager == null) return true;
            Player host = ItemLocalPresentationDispatch.ResolveCurrentHostPlayer();
            if (host == null) return true;
            MethodInfo spawn = typeof(ItemManager).GetMethod("spawnItem", BindingFlags.Instance | BindingFlags.NonPublic);
            if (spawn == null) throw new InvalidOperationException("ItemManager.spawnItem missing.");
            FieldInfo timerField = typeof(ItemManager).GetField("instantiationTimer", BindingFlags.Instance | BindingFlags.NonPublic);
            var timer = timerField?.GetValue(manager) as Stopwatch;
            System.Collections.IList rawPending = typeof(ItemManager)
                .GetField("pendingInstantiations", BindingFlags.Static | BindingFlags.NonPublic)
                ?.GetValue(null) as System.Collections.IList;
            ConsumeBatchResult result = ItemLocalPresentationDispatch.ConsumeOwnedBatch(
                Adapters.Item.NativeItemStepFiveReader.Instance, host,
                MultiObserver.MultiObserverShadowCoordinator.ItemProductionSeam,
                new NativeItemLocalPresentationView(host), spawn, manager,
                () => timer != null && timer.ElapsedMilliseconds < InstantiationBudgetMilliseconds,
                MinimumBatchActions, rawPending);
            return true;
        }

        internal static EConsumeOutcome CreateOrClassify(
            IItemLocalPresentationView view, PendingView pending, Action<PendingView> spawnItem)
        {
            if (view == null || pending == null || spawnItem == null) return EConsumeOutcome.Uncertain;
            if (!view.ProjectionAvailable(pending.RegionX, pending.RegionY))
                return EConsumeOutcome.NotStarted;
            try
            {
                spawnItem(pending);
            }
            catch (Exception error)
            {
                bool registered = HasAssociation(view, pending);
                RoleLogger.Warn("[Host]", "[ItemPresentation] spawn failed region=(" + pending.RegionX + ","
                    + pending.RegionY + ") instance=" + pending.InstanceId
                    + " error=" + error.GetType().Name
                    + (registered ? " -> consumed after registration" : " -> uncertain"));
                return registered ? EConsumeOutcome.Consumed : EConsumeOutcome.Uncertain;
            }
            return HasAssociation(view, pending) ? EConsumeOutcome.Consumed : EConsumeOutcome.Uncertain;
        }

        private static bool HasAssociation(IItemLocalPresentationView view, PendingView pending)
        {
            System.Collections.IList models = view.Models(pending.RegionX, pending.RegionY);
            if (models == null) return false;
            for (int i = 0; i < models.Count; i++)
            {
                ModelView model = models[i] as ModelView;
                if (model != null && model.InstanceId == pending.InstanceId && model.Handle != null) return true;
            }
            return false;
        }

        public static bool ReceiveItem_Prefix(byte x, byte y, ushort id, byte amount, byte quality, byte[] state, Vector3 point, uint instanceID, bool shouldPlayEffect)
        {
            // R2C：mode-first。non-Candidate 不查 owned Host 即原样透传；owned 用无
            // Player 依赖的 NativeItemLocalPresentationView()，不因无 Host 回原生
            // 旧 append；分类（networked/缺失/未知）由共同 dispatch 执行。
            ItemStepFiveResult result = ItemLocalPresentationDispatch.DispatchIncrement(
                Adapters.Item.NativeItemStepFiveReader.Instance,
                new NativeItemLocalPresentationView(),
                x, y, id, amount, quality, state, point, instanceID, shouldPlayEffect);
            if (result == ItemStepFiveResult.RunOriginal)
            {
                RoleLogger.Info("[Host]", "[ItemPresentation] increment passthrough instance=" + instanceID);
                return true;
            }
            return false;
        }

        internal static bool ShouldEnqueueIncrement(bool retired, bool hostPresent, bool networked) =>
            retired && hostPresent && networked;

        internal static bool ShouldSuppressDestroy(bool retired, bool hostPresent, bool regionReady) =>
            retired && hostPresent && regionReady;

        public static bool ReceiveDestroyItem_Prefix(byte x, byte y, uint instanceID, bool shouldPlayEffect)
        {
            // R2C：mode-first——先读真实模式；non-Candidate 在任何 owned 数据/视图
            // 构造（含 pendingInstantiations 静态读取）之前直接透传原生。
            if (!Adapters.Item.NativeItemStepFiveReader.Instance.ReadMode().IsCandidate)
                return true;
            System.Collections.IList rawPending = null;
            try
            {
                rawPending = typeof(ItemManager)
                    .GetField("pendingInstantiations", System.Reflection.BindingFlags.Static
                        | System.Reflection.BindingFlags.NonPublic)
                    ?.GetValue(null) as System.Collections.IList;
            }
            catch { rawPending = null; }
            ItemStepFiveResult result = ItemLocalPresentationDispatch.DispatchDestroy(
                Adapters.Item.NativeItemStepFiveReader.Instance,
                new NativeItemLocalPresentationView(),
                x, y, instanceID, shouldPlayEffect, rawPending);
            if (result != ItemStepFiveResult.RunOriginal) return false;
            RoleLogger.Info("[Host]", "[ItemPresentation] destroy passthrough instance=" + instanceID);
            return true;
        }

        /// <summary>E02 §6：pending 消费窗口识别（只读）——注册探测与 transpiler 共用同一结果；
        /// 锚点成员按传入 declaring type 解析（合成 shape 亦可探测）。</summary>
        internal static bool TryFindPendingBatchWindow(
            System.Collections.Generic.IReadOnlyList<CodeInstruction> codes,
            out int insertAt, out int stopAt)
        {
            return TryFindPendingBatchWindow(
                codes, typeof(ItemManager), out insertAt, out stopAt);
        }

        internal static bool TryFindPendingBatchWindow(
            System.Collections.Generic.IReadOnlyList<CodeInstruction> codes,
            Type declaringType,
            out int insertAt, out int stopAt)
        {
            insertAt = -1;
            stopAt = -1;
            FieldInfo timer = AccessTools.Field(declaringType, "instantiationTimer");
            FieldInfo pending = AccessTools.Field(declaringType, "pendingInstantiations");
            MethodInfo restart = AccessTools.Method(typeof(Stopwatch), "Restart");
            MethodInfo stop = AccessTools.Method(typeof(Stopwatch), "Stop");
            MethodInfo spawn = AccessTools.Method(declaringType, "spawnItem");
            if (timer == null || pending == null || restart == null || stop == null || spawn == null)
                return false;
            int matches = 0;
            for (int i = 0; i + 4 < codes.Count; i++)
            {
                if (codes[i].opcode != OpCodes.Ldarg_0) continue;
                if (codes[i + 1].opcode != OpCodes.Ldfld
                    || !Equals(codes[i + 1].operand, timer)) continue;
                if (!codes[i + 2].Calls(restart)) continue;
                if (codes[i + 3].opcode != OpCodes.Ldc_I4_0) continue;
                if (codes[i + 4].opcode != OpCodes.Stloc_1) continue;
                insertAt = i + 3;
                matches++;
            }
            if (matches != 1) return false;
            try
            {
                stopAt = FindStop(codes, insertAt, timer, stop, spawn, pending);
            }
            catch (InvalidOperationException)
            {
                return false;
            }
            return true;
        }

        public static IEnumerable<CodeInstruction> Update_Transpiler(
            IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var codes = new List<CodeInstruction>(instructions);
            FieldInfo timer = AccessTools.Field(typeof(ItemManager), "instantiationTimer");
            FieldInfo pending = AccessTools.Field(typeof(ItemManager), "pendingInstantiations");
            MethodInfo restart = AccessTools.Method(typeof(Stopwatch), "Restart");
            MethodInfo stop = AccessTools.Method(typeof(Stopwatch), "Stop");
            MethodInfo spawn = AccessTools.Method(typeof(ItemManager), "spawnItem");
            MethodInfo consume = AccessTools.Method(typeof(ItemOwnedPresentationRuntimePatch), nameof(ConsumeOwnedPendingBatch));
            int insertAt = -1;
            int matches = 0;
            for (int i = 0; i + 4 < codes.Count; i++)
            {
                if (codes[i].opcode != OpCodes.Ldarg_0) continue;
                if (codes[i + 1].opcode != OpCodes.Ldfld || !Equals(codes[i + 1].operand, timer)) continue;
                if (!codes[i + 2].Calls(restart)) continue;
                if (codes[i + 3].opcode != OpCodes.Ldc_I4_0) continue;
                if (codes[i + 4].opcode != OpCodes.Stloc_1) continue;
                insertAt = i + 3;
                matches++;
            }
            if (matches != 1 || spawn == null || consume == null || pending == null)
                throw new InvalidOperationException("Item pending batch anchor count=" + matches);
            int stopAt = FindStop(codes, insertAt, timer, stop, spawn, pending);
            Label original = generator.DefineLabel();
            Label after = generator.DefineLabel();
            var divert = new List<CodeInstruction>
            {
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Call, consume),
                new CodeInstruction(OpCodes.Brfalse, original),
                new CodeInstruction(OpCodes.Br, after)
            };
            divert[0].labels.AddRange(codes[insertAt].labels);
            codes[insertAt].labels.Clear();
            codes[insertAt].labels.Add(original);
            codes[stopAt].labels.Add(after);
            codes.InsertRange(insertAt, divert);
            AnchorMatchCount = 1;
            return codes;
        }

        private static bool HasPendingLoad(System.Collections.Generic.IReadOnlyList<CodeInstruction> codes, int index, FieldInfo pending)
        {
            for (int i = index; i >= 0 && i >= index - 4; i--)
                if (codes[i].opcode == OpCodes.Ldsfld && Equals(codes[i].operand, pending)) return true;
            return false;
        }

        private static int FindStop(System.Collections.Generic.IReadOnlyList<CodeInstruction> codes, int insertAt, FieldInfo timer, MethodInfo stop, MethodInfo spawn, FieldInfo pending)
        {
            int spawnAt = -1;
            int removeAt = -1;
            int stopAt = -1;
            for (int i = insertAt; i + 2 < codes.Count; i++)
            {
                if (spawnAt < 0 && codes[i].operand is MethodInfo called && called.Name == "spawnItem") spawnAt = i;
                if (spawnAt >= 0 && removeAt < 0 && codes[i].operand is MethodInfo remove && remove.Name == "RemoveRange"
                    && HasPendingLoad(codes, i, pending))
                    removeAt = i;
                if (removeAt >= 0 && codes[i].opcode == OpCodes.Ldarg_0
                    && codes[i + 1].opcode == OpCodes.Ldfld && Equals(codes[i + 1].operand, timer)
                    && codes[i + 2].Calls(stop))
                {
                    stopAt = i;
                    break;
                }
            }
            if (spawnAt < 0 || removeAt < 0 || stopAt < 0)
                throw new InvalidOperationException("Item pending batch stop anchor missing.");
            for (int i = insertAt; i < stopAt; i++)
                if (codes[i].blocks != null && codes[i].blocks.Count > 0)
                    throw new InvalidOperationException("Item pending batch crosses an exception block.");
            return stopAt;
        }

        private static void InvokeSpawn(ItemManager manager, MethodInfo spawn, PendingView pending)
        {
            if (spawn == null) throw new InvalidOperationException("ItemManager.spawnItem missing.");
            try
            {
                spawn.Invoke(manager, new object[]
                {
                    pending.RegionX, pending.RegionY, pending.AssetId, pending.Amount, pending.Quality,
                    pending.State, pending.Point, pending.InstanceId, pending.ShouldPlayEffect
                });
            }
            catch (TargetInvocationException ex)
            {
                Exception inner = ex.InnerException ?? ex;
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(inner).Throw();
            }
        }

        /// <summary>E02 §8.2 共同分类：九参预检→before/after 关联快照→真实 MethodInfo.Invoke。
        /// 关联=同 region 同 instanceId 且 Handle 非空的有效模型。</summary>
        internal static ItemSpawnResult InvokeAndClassify(
            IItemLocalPresentationView view, PendingView pending,
            MethodInfo spawn, object receiver)
        {
            if (view == null || pending == null)
                return new ItemSpawnResult(ItemSpawnProgress.NotStarted, null, "view/pending null");
            if (spawn == null || receiver == null)
                return new ItemSpawnResult(ItemSpawnProgress.NotStarted, null, "target unresolved");
            if (!spawn.DeclaringType.IsInstanceOfType(receiver))
                return new ItemSpawnResult(ItemSpawnProgress.NotStarted, null,
                    "receiver type mismatch");
            System.Reflection.ParameterInfo[] spawnParams = spawn.GetParameters();
            if (spawnParams.Length != 9
                || spawnParams[0].ParameterType != typeof(byte)
                || spawnParams[2].ParameterType != typeof(ushort)
                || spawnParams[5].ParameterType != typeof(byte[])
                || spawnParams[6].ParameterType != typeof(Vector3)
                || spawnParams[7].ParameterType != typeof(uint)
                || spawnParams[8].ParameterType != typeof(bool))
                return new ItemSpawnResult(ItemSpawnProgress.NotStarted, null,
                    "spawn signature mismatch");
            if (!view.ProjectionAvailable(pending.RegionX, pending.RegionY))
                return new ItemSpawnResult(ItemSpawnProgress.NotStarted, null, "projection unavailable");
            int beforeCount;
            try
            {
                beforeCount = CountValidAssociation(view, pending);
            }
            catch (Exception snapshotError)
            {
                return new ItemSpawnResult(ItemSpawnProgress.Uncertain, snapshotError,
                    "association snapshot read failed");
            }
            if (beforeCount == 1)
                // 有效模型已存在：去重消费——native 调用 0，batch 提交该项
                return new ItemSpawnResult(ItemSpawnProgress.Registered, null,
                    "before-existing valid association; pending deduplicated");
            if (beforeCount > 1)
                return new ItemSpawnResult(ItemSpawnProgress.Uncertain, null,
                    "before-existing multiple associations; isolated");
            try
            {
                spawn.Invoke(receiver, new object[]
                {
                    pending.RegionX, pending.RegionY, pending.AssetId, pending.Amount,
                    pending.Quality, pending.State, pending.Point, pending.InstanceId,
                    pending.ShouldPlayEffect
                });
            }
            catch (TargetInvocationException ex)
            {
                Exception inner = ex.InnerException ?? ex;
                // E02 §8.2：Invoke 后关联快照自身抛错同样属于无法确认——收束为
                // Uncertain 并保留原始 inner，不能漏到 Update 或回放原生
                int afterCount;
                try
                {
                    afterCount = CountValidAssociation(view, pending);
                }
                catch (Exception snapshotError)
                {
                    return new ItemSpawnResult(ItemSpawnProgress.Uncertain, inner,
                        "post-invocation association snapshot read failed (original: "
                            + inner.Message + "; snapshot: " + snapshotError.Message + ")");
                }
                if (afterCount == 1)
                    return new ItemSpawnResult(ItemSpawnProgress.Registered, inner,
                        "registered then post-spawn failure");
                return new ItemSpawnResult(ItemSpawnProgress.Uncertain, inner,
                    "invoked without confirmable association");
            }
            int registered;
            try
            {
                registered = CountValidAssociation(view, pending);
            }
            catch (Exception snapshotError)
            {
                return new ItemSpawnResult(ItemSpawnProgress.Uncertain, snapshotError,
                    "post-invocation association snapshot read failed");
            }
            return registered == 1
                ? new ItemSpawnResult(ItemSpawnProgress.Registered, null, null)
                : new ItemSpawnResult(ItemSpawnProgress.Uncertain, null,
                    "invoked without confirmable association");
        }

        private static int CountValidAssociation(IItemLocalPresentationView view, PendingView pending)
        {
            System.Collections.IList models = view.Models(pending.RegionX, pending.RegionY);
            if (models == null) return 0;
            int count = 0;
            for (int i = 0; i < models.Count; i++)
            {
                ModelView model = models[i] as ModelView;
                if (model != null && model.InstanceId == pending.InstanceId && model.Handle != null)
                    count++;
            }
            return count;
        }
    }
}
