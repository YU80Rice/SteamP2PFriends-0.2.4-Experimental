using HarmonyLib;
using SDG.NetTransport;
using SDG.Unturned;
using SteamP2PFriends.Shared;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace SteamP2PFriends.Core.Patches.GunshotHostAudio
{
    /// <summary>
    /// t116：UseableGun.fire() 的 Harmony 接线。
    ///
    /// 只做三件事：
    ///   1) Prefix：在真实 fire 入口分配本次尝试序号（允许失败空洞）；
    ///   2) Transpiler：在 SendPlayShoot.Invoke 正常返回后插入一次 TryCompensateAfterSend；
    ///   3) Finalizer：清理 Prefix 栈，异常时把该键标 Failed（不重试、不改变原异常）。
    ///
    /// 锚点必须与实际 IL 匹配：匹配不上就在安装前 fail-closed，绝不按行号猜。
    /// </summary>
    internal static class GunshotHostAudioPatch
    {
        internal const string Label = "[Bug2HostGunshot]";
        private static bool _attempted;
        private static Harmony _harmony;

        internal static bool Installed { get; private set; }
        internal static bool AnchorVerified { get; private set; }
        internal static string AnchorSummary { get; private set; } = "未验证";
        internal static int TranspilerInsertions { get; private set; } = -1;
        internal static string FailureReason { get; private set; } = "";

        // 测试缝：真实宿主里 Harmony 对 fire() 的 IL 复制需要 UnityEx 等运行期程序集；
        // 测试进程用这些缝把目标换成无害的假方法，安装/卸载仍走真实 Harmony。
        //   FireTargetForTest / PlayGunshotTargetForTest：替换两个 patch 目标；
        //   SkipAnchorVerifyForTest：假 fire 没有真实锚点，跳过锚点校验；
        //   SkipFirePatchForTest：只安装观察器（验证 GetPatchInfo 回查路径）。
        internal static MethodInfo FireTargetForTest;
        internal static MethodInfo PlayGunshotTargetForTest;
        internal static bool SkipAnchorVerifyForTest;
        internal static bool SkipFirePatchForTest;

        // R25-01 测试缝：回查结果可注入。返回 false 表示「Harmony.Patch 已返回但 GetPatchInfo
        // 看不到我们的 Postfix」——生产路径必须因此拒绝启用并 fail-closed。
        internal static Func<Harmony, MethodInfo, MethodInfo, bool> PostfixVerifierForTest;

        private static MethodInfo _fireMethod;
        private static MethodInfo _playGunshotMethod;

        internal static bool RegisterManual(Harmony harmony)
        {
            // 默认关闭：Enable=false 时零订阅、零 patch。
            if (!GunshotHostAudioRuntime.Enabled)
            {
                return true;
            }

            // harmony=null 是明确的配置错误：不落 _attempted 闩锁，允许修复后重试。
            if (harmony == null)
            {
                Fail("harmony=null");
                return false;
            }

            if (_attempted)
            {
                return Installed;
            }

            MethodInfo fire = FireTargetForTest
                ?? AccessTools.Method(typeof(UseableGun), "fire", Type.EmptyTypes);
            if (fire == null)
            {
                Fail("UseableGun.fire not found");
                return false;
            }

            MethodInfo playGunshot = PlayGunshotTargetForTest
                ?? AccessTools.Method(typeof(UseableGun), "playGunshot", Type.EmptyTypes);
            if (playGunshot == null)
            {
                Fail("UseableGun.playGunshot not found");
                return false;
            }

            if (!SkipAnchorVerifyForTest && !VerifyAnchorBeforePatch(fire, out string anchorDetail))
            {
                Fail("anchor mismatch: " + anchorDetail);
                return false;
            }

            MethodInfo prefix = AccessTools.Method(typeof(GunshotHostAudioPatch), nameof(Fire_Prefix));
            MethodInfo finalizer = AccessTools.Method(typeof(GunshotHostAudioPatch), nameof(Fire_Finalizer));
            MethodInfo transpiler = AccessTools.Method(typeof(GunshotHostAudioPatch), nameof(Fire_Transpiler));
            MethodInfo nativeLeafPostfix = AccessTools.Method(
                typeof(GunshotHostAudioPatch), nameof(NativeLeaf_Postfix));
            if (prefix == null || finalizer == null || transpiler == null || nativeLeafPostfix == null)
            {
                Fail("patch method missing");
                return false;
            }

            _harmony = harmony;
            _fireMethod = fire;
            _playGunshotMethod = playGunshot;

            try
            {
                // T126-F1：先安装观察器，再用 Harmony.GetPatchInfo 回查「确实存在我们的 Postfix」。
                // 只有回查成功才允许进入后续步骤；否则立即卸载并 fail-closed，
                // 避免「删掉安装行后仍 SetNativeObserverInstalled(true)」造成双声静默回归。
                harmony.Patch(
                    playGunshot,
                    postfix: new HarmonyMethod(nativeLeafPostfix));
                bool postfixInstalled = PostfixVerifierForTest != null
                    ? PostfixVerifierForTest(harmony, playGunshot, nativeLeafPostfix)
                    : IsOurPostfixInstalled(harmony, playGunshot, nativeLeafPostfix);
                if (!postfixInstalled)
                {
                    Uninstall();
                    Fail("native leaf postfix not installed");
                    return false;
                }

                if (SkipFirePatchForTest)
                {
                    TranspilerInsertions = 2;
                }
                else
                {
                    harmony.Patch(
                        fire,
                        prefix: new HarmonyMethod(prefix),
                        finalizer: new HarmonyMethod(finalizer),
                        transpiler: new HarmonyMethod(transpiler));
                }

                // R26-03：原 `TranspilerInsertions != 2` 守卫删除——transpiler 在锚点失配时直接抛
                // InvalidOperationException，异常路径（下方 catch）已统一 Uninstall()+Fail；
                // 「插入数不符但没抛」在现有实现中不可达，保留该分支只会形成无法被用例覆盖的死代码。
                // 插入数仍记录在 TranspilerInsertions 中，供 ENTRY_READY 日志与测试断言使用。
                _attempted = true;
                GunshotHostAudioRuntime.SetNativeObserverInstalled(true);
                Installed = true;
                AnchorVerified = true;
                RoleLogger.Info("[Shared]", Label + " ENTRY_READY anchor=" + AnchorSummary + " enable=true");
                return true;
            }
            catch (Exception ex)
            {
                // R24-03：异常路径同样先卸载两个 patch，再 fail-closed。
                Uninstall();
                Fail("register exception: " + ex.GetType().Name + ": " + ex.Message);
                return false;
            }
        }

        private static bool IsOurPostfixInstalled(Harmony harmony, MethodInfo target, MethodInfo postfix)
        {
            HarmonyLib.Patches info = Harmony.GetPatchInfo(target);
            if (info == null || info.Postfixes == null)
            {
                return false;
            }

            for (int i = 0; i < info.Postfixes.Count; i++)
            {
                HarmonyLib.Patch patch = info.Postfixes[i];
                if (patch != null && patch.owner == harmony.Id && patch.PatchMethod == postfix)
                {
                    return true;
                }
            }

            return false;
        }

        internal static void Uninstall()
        {
            try
            {
                MethodInfo fire = _fireMethod
                    ?? AccessTools.Method(typeof(UseableGun), "fire", Type.EmptyTypes);
                Unpatch(fire);
                MethodInfo playGunshot = _playGunshotMethod
                    ?? AccessTools.Method(typeof(UseableGun), "playGunshot", Type.EmptyTypes);
                Unpatch(playGunshot);
            }
            catch
            {
                // 反注册失败只记录，不抛给配置事件循环。
            }

            _fireMethod = null;
            _playGunshotMethod = null;
            GunshotHostAudioRuntime.SetNativeObserverInstalled(false);
            _attempted = false;
            Installed = false;
            TranspilerInsertions = -1;
        }

        internal static void ResetForTest()
        {
            _attempted = false;
            _harmony = null;
            _fireMethod = null;
            _playGunshotMethod = null;
            Installed = false;
            AnchorVerified = false;
            AnchorSummary = "未验证";
            TranspilerInsertions = -1;
            FailureReason = "";
            FireTargetForTest = null;
            PlayGunshotTargetForTest = null;
            SkipAnchorVerifyForTest = false;
            SkipFirePatchForTest = false;
            PostfixVerifierForTest = null;
            GunshotHostAudioRuntime.SetNativeObserverInstalled(false);
        }

        /// <summary>
        /// 在真实 U3 IL 上定位唯一锚点：
        ///   ldsfld UseableGun::SendPlayShoot
        ///   ...
        ///   callvirt ClientInstanceMethod::Invoke(NetId, ENetReliability, List&lt;ITransportConnection&gt;)
        /// 仅当该调用点唯一、且其前 24 条指令内出现 SendPlayShoot 字段读取时才接受。
        /// </summary>
        internal static bool TryLocateAnchor(
            IReadOnlyList<GunshotIlScan.IlInstruction> codes, out int index, out string detail)
        {
            index = -1;
            detail = "";
            if (codes == null || codes.Count == 0)
            {
                detail = "empty IL";
                return false;
            }

            var candidates = new List<int>();
            for (int i = 0; i < codes.Count; i++)
            {
                GunshotIlScan.IlInstruction instruction = codes[i];
                if (instruction == null)
                {
                    continue;
                }

                if (instruction.OpCode != OpCodes.Call && instruction.OpCode != OpCodes.Callvirt)
                {
                    continue;
                }

                MethodInfo method = instruction.Operand as MethodInfo;
                if (method == null || method.DeclaringType != typeof(ClientInstanceMethod) || method.Name != "Invoke")
                {
                    continue;
                }

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length != 3 || parameters[2].ParameterType != typeof(List<ITransportConnection>))
                {
                    continue;
                }

                if (!HasSendPlayShootLoadBefore(codes, i))
                {
                    continue;
                }

                candidates.Add(i);
            }

            if (candidates.Count != 1)
            {
                detail = "candidate count=" + candidates.Count;
                return false;
            }

            index = candidates[0];
            detail = "ClientInstanceMethod.Invoke(NetId,ENetReliability,List<ITransportConnection>) at idx="
                + index + " off=0x" + codes[index].Offset.ToString("X4");
            return true;
        }

        private static bool HasSendPlayShootLoadBefore(
            IReadOnlyList<GunshotIlScan.IlInstruction> codes, int callIndex)
        {
            int lowerBound = Math.Max(0, callIndex - 24);
            for (int i = callIndex - 1; i >= lowerBound; i--)
            {
                GunshotIlScan.IlInstruction instruction = codes[i];
                if (instruction == null || instruction.OpCode != OpCodes.Ldsfld)
                {
                    continue;
                }

                FieldInfo field = instruction.Operand as FieldInfo;
                if (field != null && field.DeclaringType == typeof(UseableGun) && field.Name == "SendPlayShoot")
                {
                    return true;
                }
            }

            return false;
        }

        private static bool VerifyAnchorBeforePatch(MethodInfo fire, out string detail)
        {
            // 用自带扫描器，避免 Harmony/MonoMod 在测试宿主里因 UnityEx 缺失而解析失败。
            List<GunshotIlScan.IlInstruction> source = GunshotIlScan.Read(fire);
            if (source == null || source.Count == 0)
            {
                detail = "empty IL";
                return false;
            }

            if (!TryLocateAnchor(source, out int index, out detail))
            {
                return false;
            }

            AnchorSummary = "idx=" + index + " off=0x" + source[index].Offset.ToString("X4");
            return true;
        }

        [HarmonyPrefix]
        public static void Fire_Prefix(UseableGun __instance)
        {
            GunshotHostAudioRuntime.OnFireEnter(__instance);
        }

        /// <summary>
        /// 原生叶观察器：记录「该实例本次 fire 已进入 playGunshot」。
        /// 只观察，不改变原生播放行为；我们自己补的那一次由 Runtime 的深度标记排除。
        /// </summary>
        [HarmonyPostfix]
        public static void NativeLeaf_Postfix(UseableGun __instance)
        {
            GunshotHostAudioRuntime.OnNativeLeafObserved(__instance);
        }

        [HarmonyFinalizer]
        public static Exception Fire_Finalizer(UseableGun __instance, Exception __exception)
        {
            GunshotHostAudioRuntime.OnFireExit(__instance, __exception != null);
            return __exception;
        }

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Fire_Transpiler(
            IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var codes = new List<CodeInstruction>(instructions);
            var view = new List<GunshotIlScan.IlInstruction>(codes.Count);
            for (int i = 0; i < codes.Count; i++)
            {
                CodeInstruction code = codes[i];
                view.Add(code == null
                    ? null
                    : new GunshotIlScan.IlInstruction
                    {
                        Offset = i,
                        OpCode = code.opcode,
                        Operand = code.operand,
                    });
            }

            if (!TryLocateAnchor(view, out int anchor, out string detail))
            {
                TranspilerInsertions = -1;
                throw new InvalidOperationException("GunshotHostAudioPatch: anchor mismatch (" + detail + ")");
            }

            MethodInfo compensation = AccessTools.Method(
                typeof(GunshotHostAudioRuntime), nameof(GunshotHostAudioRuntime.TryCompensateAfterSend));
            if (compensation == null)
            {
                TranspilerInsertions = -1;
                throw new InvalidOperationException("GunshotHostAudioPatch: TryCompensateAfterSend not found");
            }

            // 栈平衡：锚点 callvirt 已消费全部参数并返回 void；插入 ldarg.0 + call(UseableGun) 净变化 0。
            codes.Insert(anchor + 1, new CodeInstruction(OpCodes.Ldarg_0));
            codes.Insert(anchor + 2, new CodeInstruction(OpCodes.Call, compensation));
            TranspilerInsertions = 2;
            return codes;
        }

        private static void Unpatch(MethodInfo fire)
        {
            try
            {
                if (_harmony != null && fire != null)
                {
                    _harmony.Unpatch(fire, HarmonyPatchType.All, _harmony.Id);
                }
            }
            catch
            {
                // 反注册失败也只能记录，不能把异常抛给插件启动流程。
            }
        }

        private static void Fail(string reason)
        {
            FailureReason = reason;
            Installed = false;
            GunshotHostAudioRuntime.SetFailClosed(reason);
            RoleLogger.Error("[Shared]", Label + " !!! FAIL_CLOSED " + reason);
        }
    }
}
