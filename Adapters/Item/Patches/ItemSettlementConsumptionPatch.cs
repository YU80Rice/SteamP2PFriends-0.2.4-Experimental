using HarmonyLib;
using SDG.NetTransport;
using System;
using SDG.Unturned;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace SteamP2PFriends.Adapters.Item.Patches
{
    public static class ItemSettlementConsumptionPatch
    {
        internal static bool AllowPickup(byte x, byte y) =>
            ItemSettlementGate.TryAuthorizePickup(x, y);

        internal static bool AllowIncrement(byte arg1, byte arg2) =>
            ItemSettlementGate.TryAuthorizeIncrement(arg1, arg2);

        internal static bool AllowAskItems(byte x, byte y) =>
            ItemSettlementGate.TryAuthorizeIncrement(x, y);

        internal const string PendingBatchFieldName = "pendingInstantiations";

        internal enum PendingBatchShape : byte
        {
            Unsupported = 0,
            Client = 1,
            DedicatedWithoutBatch = 2
        }

        /// <summary>E02 §6：双 shape 探测——读取实际传入 managerType 的元数据，不读静态值。
        /// Client= pending(静态 List&lt;ItemInstantiationParameters&gt;) + timer(实例 Stopwatch)
        /// + spawnItem 签名 + Update 签名；DedicatedWithoutBatch= pending 正确但 timer 确实
        /// 不存在；其他（半缺成员/类型错）= Unsupported 并报告具体差异，不默认为专用服。</summary>
        internal static PendingBatchShape InspectPendingBatchShape(Type managerType)
        {
            if (managerType == null) return PendingBatchShape.Unsupported;
            FieldInfo pending = AccessTools.Field(managerType, PendingBatchFieldName);
            bool pendingOk = pending != null && pending.IsStatic && pending.FieldType.IsGenericType
                && pending.FieldType.GetGenericTypeDefinition() == typeof(List<>)
                && pending.FieldType.GetGenericArguments()[0].FullName
                    == "SDG.Unturned.ItemInstantiationParameters";
            MethodInfo spawn = AccessTools.Method(managerType, "spawnItem");
            System.Reflection.ParameterInfo[] spawnParams = spawn?.GetParameters();
            // E02 §6：spawnItem 完整九参签名 (byte,byte,ushort,byte,byte,byte[],Vector3,uint,bool)
            // 且必须 instance + void（static 或非 void 返回为 Unsupported 反例）
            bool spawnSignatureOk = spawn != null && !spawn.IsStatic
                && spawn.ReturnType == typeof(void)
                && spawnParams != null && spawnParams.Length == 9
                && spawnParams[0].ParameterType == typeof(byte)
                && spawnParams[1].ParameterType == typeof(byte)
                && spawnParams[2].ParameterType == typeof(ushort)
                && spawnParams[3].ParameterType == typeof(byte)
                && spawnParams[4].ParameterType == typeof(byte)
                && spawnParams[5].ParameterType == typeof(byte[])
                && spawnParams[6].ParameterType == typeof(Vector3)
                && spawnParams[7].ParameterType == typeof(uint)
                && spawnParams[8].ParameterType == typeof(bool);
            MethodInfo update = AccessTools.Method(managerType, "Update", Type.EmptyTypes);
            bool updateOk = update != null && !update.IsStatic
                && update.ReturnType == typeof(void);
            FieldInfo timer = AccessTools.Field(managerType, "instantiationTimer");
            bool timerOk = timer != null && !timer.IsStatic
                && timer.FieldType == typeof(System.Diagnostics.Stopwatch);
            // E02 §6：窗口识别与 transpiler 共用同一结果——从传入类型真实 IL 探测
            // pending 消费窗口；Client 需窗口唯一存在，DedicatedWithoutBatch 需确实无窗口
            bool windowPresent = false;
            if (updateOk)
            {
                var codes = HarmonyLib.PatchProcessor.GetCurrentInstructions(update, out _, maxTranspilers: 0);
                windowPresent = SteamP2PFriends.Adapters.Item.Patches
                    .ItemOwnedPresentationRuntimePatch.TryFindPendingBatchWindow(
                        codes, managerType, out _, out _);
            }
            if (pendingOk && spawnSignatureOk && updateOk && timerOk && windowPresent)
                return PendingBatchShape.Client;
            if (pendingOk && spawnSignatureOk && updateOk && timer == null && !windowPresent)
                return PendingBatchShape.DedicatedWithoutBatch;
            return PendingBatchShape.Unsupported;
        }

        public static void RegisterManual(Harmony harmony)
        {
            MethodInfo pickup = AccessTools.Method(typeof(ItemManager), "ReceiveTakeItemRequest");
            MethodInfo ask = AccessTools.Method(typeof(ItemManager), "askItems",
                new[] { typeof(ITransportConnection), typeof(byte), typeof(byte), typeof(float) });
            Type sendItem = typeof(ClientStaticMethod<byte, byte, ushort, byte, byte, byte[], Vector3, uint, bool>);
            MethodInfo increment = sendItem.GetMethod("Invoke", new[]
            {
                typeof(ENetReliability), typeof(List<ITransportConnection>),
                typeof(byte), typeof(byte), typeof(ushort), typeof(byte), typeof(byte),
                typeof(byte[]), typeof(Vector3), typeof(uint), typeof(bool)
            });
            MethodInfo update = AccessTools.Method(typeof(ItemManager), "Update", Type.EmptyTypes);
            MethodInfo destroy = AccessTools.Method(typeof(ItemManager), "ReceiveDestroyItem");
            MethodInfo receiveItem = AccessTools.Method(typeof(ItemManager), "ReceiveItem");
            // E02 §6：第一条 harmony.Patch 之前解析并验证全部必需 target（increment
            // 缺失不再静默跳过）
            if (pickup == null || ask == null || increment == null
                || update == null || destroy == null || receiveItem == null)
                throw new InvalidOperationException(
                    "Item settlement target missing: pickup=" + (pickup != null)
                    + " ask=" + (ask != null) + " increment=" + (increment != null)
                    + " update=" + (update != null) + " destroy=" + (destroy != null)
                    + " receiveItem=" + (receiveItem != null));
            PendingBatchShape shape = InspectPendingBatchShape(typeof(ItemManager));
            if (shape == PendingBatchShape.Unsupported)
                throw new InvalidOperationException(
                    "ItemManager pending batch shape unsupported: pending/timer/spawn/update mismatch");
            harmony.Patch(pickup, new HarmonyMethod(typeof(ItemSettlementConsumptionPatch), nameof(AllowPickup)));
            ItemAuthoritativePickupPatch.RegisterManual(harmony);
            harmony.Patch(ask, new HarmonyMethod(typeof(ItemSettlementConsumptionPatch), nameof(AllowAskItems)));
            harmony.Patch(increment, new HarmonyMethod(typeof(ItemSettlementConsumptionPatch), nameof(AllowIncrement)));
            if (shape == PendingBatchShape.Client)
                harmony.Patch(update, transpiler: new HarmonyMethod(typeof(ItemOwnedPresentationRuntimePatch), nameof(ItemOwnedPresentationRuntimePatch.Update_Transpiler)));
            harmony.Patch(destroy, prefix: new HarmonyMethod(typeof(ItemOwnedPresentationRuntimePatch), nameof(ItemOwnedPresentationRuntimePatch.ReceiveDestroyItem_Prefix)));
            harmony.Patch(receiveItem, prefix: new HarmonyMethod(typeof(ItemOwnedPresentationRuntimePatch), nameof(ItemOwnedPresentationRuntimePatch.ReceiveItem_Prefix)));
            if (shape == PendingBatchShape.Client)
                VerifyRegistered(harmony, update,
                    typeof(ItemOwnedPresentationRuntimePatch).GetMethod(nameof(ItemOwnedPresentationRuntimePatch.Update_Transpiler), BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic),
                    EExpectedPatchKind.Transpiler);
            VerifyRegistered(harmony, destroy,
                typeof(ItemOwnedPresentationRuntimePatch).GetMethod(nameof(ItemOwnedPresentationRuntimePatch.ReceiveDestroyItem_Prefix), BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic),
                EExpectedPatchKind.Prefix);
            VerifyRegistered(harmony, receiveItem,
                typeof(ItemOwnedPresentationRuntimePatch).GetMethod(nameof(ItemOwnedPresentationRuntimePatch.ReceiveItem_Prefix), BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic),
                EExpectedPatchKind.Prefix);
        }

        internal enum EExpectedPatchKind : byte
        {
            Prefix = 0,
            Transpiler = 1
        }

        /// <summary>E02 §6/R1：逐目标精确校验——按 target 的预期 patch 种类取对应集合，
        /// 完整寻找 (owner==harmony.Id 且 PatchMethod==expected) 的补丁，命中后只校验
        /// 该项 priority；同 owner 的其他合法补丁（如 dedicated gate 的 Update
        /// Transpiler sibling）不参与比对、不导致失败。required 方法缺失才失败；
        /// 错误种类诊断同样以 expected 方法为对象，不凭同 owner 无关补丁的存在报错。</summary>
        private static void VerifyRegistered(Harmony harmony, MethodInfo original,
            MethodInfo expectedPatchMethod, EExpectedPatchKind kind)
        {
            const int ExpectedPriority = (int)HarmonyLib.Priority.Normal;
            HarmonyLib.Patches info = Harmony.GetPatchInfo(original);
            if (info == null)
                throw new InvalidOperationException("Item settlement patch info missing: " + original.Name);
            IEnumerable<HarmonyLib.Patch> expectedCollection = kind == EExpectedPatchKind.Transpiler
                ? info.Transpilers : info.Prefixes;
            HarmonyLib.Patch matched = null;
            foreach (HarmonyLib.Patch patch in expectedCollection)
            {
                if (patch.owner != harmony.Id) continue;
                if (!Equals(patch.PatchMethod, expectedPatchMethod)) continue;
                matched = patch;
                break;
            }
            if (matched != null)
            {
                if (matched.priority != ExpectedPriority)
                    throw new InvalidOperationException(
                        "Item settlement patch priority mismatch on ItemManager." + original.Name
                        + ": expected=" + ExpectedPriority + " actual=" + matched.priority);
                return;
            }
            // required 方法缺失：先精确检查 expected 方法是否被同 owner 登记到另一
            // 集合（wrong kind，诊断针对 expected 方法本身，不凭无关 sibling 报错）
            IEnumerable<HarmonyLib.Patch> otherCollection = kind == EExpectedPatchKind.Transpiler
                ? info.Prefixes : info.Transpilers;
            foreach (HarmonyLib.Patch patch in otherCollection)
            {
                if (patch.owner != harmony.Id) continue;
                if (!Equals(patch.PatchMethod, expectedPatchMethod)) continue;
                throw new InvalidOperationException(
                    "Item settlement patch kind mismatch on ItemManager." + original.Name
                    + ": expected kind=" + kind + " but expected method "
                    + (expectedPatchMethod?.Name ?? "null") + " registered as the other kind");
            }
            // 其余缺失：诊断列出同 owner 在本集合实际登记的方法，不因
            // 合法 sibling 存在而误报 method/kind mismatch
            List<string> ownMethods = new List<string>();
            foreach (HarmonyLib.Patch patch in expectedCollection)
                if (patch.owner == harmony.Id)
                    ownMethods.Add(patch.PatchMethod?.Name ?? "null");
            throw new InvalidOperationException(
                "Item settlement patch not registered on ItemManager." + original.Name
                + " kind=" + kind
                + " expected=" + (expectedPatchMethod?.Name ?? "null")
                + " ownRegistered=[" + string.Join(",", ownMethods) + "]");
        }
    }
}
