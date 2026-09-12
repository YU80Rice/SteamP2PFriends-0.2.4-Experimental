using HarmonyLib;
using SDG.Unturned;
using SteamP2PFriends.Adapters.Item.Patches;
using SteamP2PFriends.Shared;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace SteamP2PFriends.WhitelistTests
{
    /// <summary>
    /// Ticket 02（listen-host-dedicated-gate）StaticIL 契约：
    ///   IG1 当前 U3 构建的 ItemManager.Update 方法体内
    ///       Dedicator.get_IsDedicatedServer 调用点恰为 1 处（尾部 despawn/respawn
    ///       早退守卫唯一性前提；onRegionUpdated 内的调用点属另一方法，不在本契约内）。
    ///   IG2 单点 transpiler 契约：ReplacementCount==1；替换后 IL 中
    ///       get_IsDedicatedServer 为 0 处、IsDedicatedOrP2PHost 恰 1 处；
    ///       指令总数不变、非目标指令逐位原样（含调用点后短路分支消费者），
    ///       替换目标是现有资格函数本身（禁止第二资格函数）。
    /// 先例：Ticket01 ZG1/ZG2（M1I06 直读 IL + 空 ILGenerator 应用）。
    /// </summary>
    internal static class ItemUpdateDedicatedGateStaticILTests
    {
        internal static bool Test_IG1_CurrentU3IlHasExactlyOneDedicatedCallSite()
        {
            MethodInfo original = AccessTools.Method(
                typeof(ItemManager), "Update", System.Type.EmptyTypes);
            if (original == null) return false;
            if (original.IsStatic) return false;
            if (original.ReturnType != typeof(void)) return false;

            List<CodeInstruction> source = PatchProcessor.GetCurrentInstructions(
                original, out _, maxTranspilers: 0);
            if (source == null || source.Count == 0) return false;

            MethodInfo dedicatedGetter = DedicatedGetter();
            if (dedicatedGetter == null) return false;

            return CountCalls(source, dedicatedGetter) == 1;
        }

        internal static bool Test_IG2_TranspilerReplacesExactlyTheGuardCall()
        {
            MethodInfo original = AccessTools.Method(
                typeof(ItemManager), "Update", System.Type.EmptyTypes);
            if (original == null) return false;

            List<CodeInstruction> source = PatchProcessor.GetCurrentInstructions(
                original, out _, maxTranspilers: 0);
            if (source == null || source.Count == 0) return false;

            MethodInfo dedicatedGetter = DedicatedGetter();
            MethodInfo eligibility = EligibilityMethod();
            if (dedicatedGetter == null || eligibility == null) return false;
            if (CountCalls(source, dedicatedGetter) != 1) return false;

            // 栈平衡契约：两函数同为 0 参、bool（i4）返回 ⇒ 调用点栈净变化一致
            // （无参不弹栈，返回均压 1 个 i4），替换栈平衡由签名等价性保证。
            if (dedicatedGetter.ReturnType != typeof(bool)) return false;
            if (eligibility.ReturnType != typeof(bool)) return false;
            if (dedicatedGetter.GetParameters().Length != 0) return false;
            if (eligibility.GetParameters().Length != 0) return false;

            // transpiler 是原位改写，必须先快照 opcode/operand
            var opcodes = new OpCode[source.Count];
            var operands = new object[source.Count];
            for (int i = 0; i < source.Count; i++)
            {
                opcodes[i] = source[i].opcode;
                operands[i] = source[i].operand;
            }

            var result = new List<CodeInstruction>(
                ItemManagerUpdateDedicatedGatePatch.Update_Transpiler(source, null));
            if (result == null || result.Count != source.Count) return false;
            if (ItemManagerUpdateDedicatedGatePatch.ReplacementCount != 1) return false;
            if (CountCalls(result, dedicatedGetter) != 0) return false;
            if (CountCalls(result, eligibility) != 1) return false;

            bool replacedAtGuard = false;
            for (int i = 0; i < result.Count; i++)
            {
                CodeInstruction after = result[i];

                bool wasDedicatedCall = (opcodes[i] == OpCodes.Call || opcodes[i] == OpCodes.Callvirt)
                    && Equals(operands[i], dedicatedGetter);
                if (wasDedicatedCall)
                {
                    if (replacedAtGuard) return false;
                    if (after.opcode != OpCodes.Call || !Equals(after.operand, eligibility)) return false;
                    replacedAtGuard = true;

                    // 布尔消费者（!A || !B 的短路分支）必须原样保留在调用点之后
                    if (i + 1 < result.Count && result[i + 1].opcode != opcodes[i + 1]) return false;
                    continue;
                }

                if (after.opcode != opcodes[i]) return false;
                if (!Equals(after.operand, operands[i])) return false;
            }

            return replacedAtGuard;
        }

        private static MethodInfo DedicatedGetter()
        {
            return AccessTools.PropertyGetter(typeof(Dedicator), nameof(Dedicator.IsDedicatedServer));
        }

        private static MethodInfo EligibilityMethod()
        {
            return AccessTools.Method(
                typeof(ListenRegionSyncEligibility),
                nameof(ListenRegionSyncEligibility.IsDedicatedOrP2PHost),
                System.Type.EmptyTypes);
        }

        private static int CountCalls(List<CodeInstruction> codes, MethodInfo target)
        {
            int n = 0;
            foreach (CodeInstruction code in codes)
            {
                if (code != null && code.Calls(target)) n++;
            }
            return n;
        }
    }
}
