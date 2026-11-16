using HarmonyLib;
using SDG.Unturned;
using SteamP2PFriends.Shared;
using System;
using System.Reflection;

namespace SteamP2PFriends.Platform.Diagnostics.Patches
{
    /// <summary>
    /// Bug1 仪表登记：默认关闭时不安装任何 hook、不写任何日志；
    /// 开启后只加 Prefix/Postfix 只读观察，不 return false、不改参数、不压制原生方法。
    /// </summary>
    public static class Bug1ProbePatch
    {
        private const string Label = "[SPF-Bug1Probe]";

        public static bool Registered { get; private set; }
        public static bool Attempted { get; private set; }
        public static bool FailClosed { get; private set; }
        public static MethodInfo SimulateTarget { get; private set; }
        public static MethodInfo UpdateTarget { get; private set; }
        /// <summary>t28：真实收包/入队/出队边界；解析不到即 FailClosed，不退化成入口 Postfix 冒充。</summary>
        public static MethodInfo ReceiveInputsTarget { get; private set; }
        public static MethodInfo EnqueueTarget { get; private set; }
        public static MethodInfo DequeueTarget { get; private set; }

        private static Harmony _harmony;
        private static string _harmonyId = string.Empty;

        public static bool RegisterManual(Harmony harmony)
        {
            if (harmony == null) return false;
            if (Attempted && _harmonyId == harmony.Id) return true;
            _harmony = harmony;
            _harmonyId = harmony.Id;
            Attempted = true;
            // 默认关闭：不安装 hook、不写日志、不订阅相机。
            if (!Bug1ProbeRuntime.Enabled) return true;
            return Install();
        }

        /// <summary>运行时开启或生产登记后安装；任一 target 解析失败即 FailClosed，禁止猜 IL 偏移。</summary>
        public static bool Install()
        {
            if (Registered) return true;
            if (_harmony == null) return false;
            try
            {
                ResolveTargets();
                if (SimulateTarget == null || UpdateTarget == null
                    || ReceiveInputsTarget == null || EnqueueTarget == null || DequeueTarget == null)
                {
                    FailClosed = true;
                    RoleLogger.Error("[Shared]", Label + " !!! ABI unresolved: simulate=" + (SimulateTarget != null)
                        + " Update=" + (UpdateTarget != null)
                        + " receiveInputs=" + (ReceiveInputsTarget != null)
                        + " enqueue=" + (EnqueueTarget != null)
                        + " dequeue=" + (DequeueTarget != null));
                    return false;
                }

                MethodInfo simBefore = Hook(nameof(Prefix_Simulate));
                MethodInfo simAfter = Hook(nameof(Postfix_Simulate));
                MethodInfo update = Hook(nameof(Prefix_Update));
                MethodInfo rxBefore = Hook(nameof(Prefix_ReceiveInputs));
                MethodInfo rxAfter = Hook(nameof(Postfix_ReceiveInputs));
                MethodInfo enqueue = Hook(nameof(Postfix_Enqueue));
                MethodInfo dequeue = Hook(nameof(Postfix_Dequeue));
                if (simBefore == null || simAfter == null || update == null
                    || rxBefore == null || rxAfter == null || enqueue == null || dequeue == null)
                {
                    FailClosed = true;
                    return false;
                }

                _harmony.Patch(SimulateTarget, prefix: new HarmonyMethod(simBefore), postfix: new HarmonyMethod(simAfter));
                _harmony.Patch(UpdateTarget, prefix: new HarmonyMethod(update));
                _harmony.Patch(ReceiveInputsTarget, prefix: new HarmonyMethod(rxBefore), postfix: new HarmonyMethod(rxAfter));
                _harmony.Patch(EnqueueTarget, postfix: new HarmonyMethod(enqueue));
                _harmony.Patch(DequeueTarget, postfix: new HarmonyMethod(dequeue));
                Registered = Verify(_harmonyId, SimulateTarget, simBefore, true)
                    && Verify(_harmonyId, SimulateTarget, simAfter, false)
                    && Verify(_harmonyId, UpdateTarget, update, true)
                    && Verify(_harmonyId, ReceiveInputsTarget, rxBefore, true)
                    && Verify(_harmonyId, ReceiveInputsTarget, rxAfter, false)
                    && Verify(_harmonyId, EnqueueTarget, enqueue, false)
                    && Verify(_harmonyId, DequeueTarget, dequeue, false);
                if (!Registered)
                {
                    FailClosed = true;
                    RoleLogger.Error("[Shared]", Label + " !!! identity verify failed (owner+original+PatchMethod)");
                    return false;
                }
                FailClosed = false;
                Bug1ProbeRuntime.BoundCamera(true);
                RoleLogger.Info("[Shared]", Label + " registered simulate(9-arg)+Update hooks owner=" + _harmonyId);
                return true;
            }
            catch (Exception ex)
            {
                FailClosed = true;
                LastFailure = ex;
                RoleLogger.Error("[Shared]", Label + " !!! register failed: " + ex);
                return false;
            }
        }

        /// <summary>
        /// t48/R8-04：把本 owner 装过的 7 条 detour 全部还原（simulate prefix/postfix、Update prefix、
        /// ReceiveInputs prefix/postfix、Queue&lt;PlayerInputPacket&gt;.Enqueue/Dequeue postfix）。
        /// 幂等：未装过、重复调用、target 已空都返回 true 且不抛；只摘本 owner 的方法，
        /// 同 target 上其它插件的补丁必须保留。关闭后再开启要能重新 Install 成功。
        /// </summary>
        public static bool Uninstall()
        {
            bool ok = true;
            if (_harmony != null)
            {
                ok &= UnpatchOne(SimulateTarget, nameof(Prefix_Simulate));
                ok &= UnpatchOne(SimulateTarget, nameof(Postfix_Simulate));
                ok &= UnpatchOne(UpdateTarget, nameof(Prefix_Update));
                ok &= UnpatchOne(ReceiveInputsTarget, nameof(Prefix_ReceiveInputs));
                ok &= UnpatchOne(ReceiveInputsTarget, nameof(Postfix_ReceiveInputs));
                ok &= UnpatchOne(EnqueueTarget, nameof(Postfix_Enqueue));
                ok &= UnpatchOne(DequeueTarget, nameof(Postfix_Dequeue));
            }
            Registered = false;
            if (ok) RoleLogger.Info("[Shared]", Label + " uninstalled owner=" + _harmonyId);
            return ok;
        }

        private static bool UnpatchOne(MethodInfo original, string patchName)
        {
            if (original == null) return true;
            MethodInfo patchMethod = Hook(patchName);
            if (patchMethod == null) return true;
            // 幂等：本 owner 没装过这一条时直接算成功（Harmony.Unpatch 对未装过的 target 会抛）。
            if (!IsPatched(original, patchMethod)) return true;
            try
            {
                _harmony.Unpatch(original, patchMethod);
                return true;
            }
            catch (Exception ex)
            {
                LastFailure = ex;
                RoleLogger.Error("[Shared]", Label + " !!! unpatch failed " + patchName + ": " + ex.Message);
                return false;
            }
        }

        /// <summary>本 owner 是否在该 original 上装了指定 patch 方法（prefix/postfix 都查）。</summary>
        private static bool IsPatched(MethodInfo original, MethodInfo patchMethod)
        {
            HarmonyLib.Patches info = Harmony.GetPatchInfo(original);
            if (info == null) return false;
            foreach (Patch patch in info.Prefixes)
                if (patch.owner == _harmonyId && patch.PatchMethod == patchMethod) return true;
            foreach (Patch patch in info.Postfixes)
                if (patch.owner == _harmonyId && patch.PatchMethod == patchMethod) return true;
            return false;
        }

        /// <summary>
        /// 解析全部原生锚点；只做反射，不安装 hook，测试可直接调用核对签名/唯一性。
        /// ReceiveInputs 是 `in ServerInvocationContext`，故参数类型为 byref。
        /// </summary>
        internal static bool ResolveTargets()
        {
            SimulateTarget = AccessTools.Method(typeof(PlayerMovement), "simulate",
                new[]
                {
                    typeof(uint), typeof(int), typeof(int), typeof(int), typeof(float),
                    typeof(float), typeof(bool), typeof(bool), typeof(float),
                });
            UpdateTarget = AccessTools.Method(typeof(PlayerMovement), "Update", Type.EmptyTypes);
            ReceiveInputsTarget = AccessTools.Method(typeof(PlayerInput), "ReceiveInputs",
                new[] { typeof(ServerInvocationContext).MakeByRefType() });
            EnqueueTarget = AccessTools.Method(typeof(System.Collections.Generic.Queue<PlayerInputPacket>), "Enqueue");
            DequeueTarget = AccessTools.Method(typeof(System.Collections.Generic.Queue<PlayerInputPacket>), "Dequeue");
            return SimulateTarget != null && UpdateTarget != null
                && ReceiveInputsTarget != null && EnqueueTarget != null && DequeueTarget != null;
        }

        public static Exception LastFailure { get; private set; }

        private static MethodInfo Hook(string name)
            => typeof(Bug1ProbePatch).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic);

        internal static bool Verify(string ownerId, MethodInfo original, MethodInfo patchMethod, bool prefix)
        {
            HarmonyLib.Patches info = Harmony.GetPatchInfo(original);
            if (info == null) return false;
            System.Collections.ICollection patches = prefix ? (System.Collections.ICollection)info.Prefixes : info.Postfixes;
            foreach (Patch patch in patches)
                if (patch.owner == ownerId && patch.PatchMethod == patchMethod) return true;
            return false;
        }

        private static void Prefix_Simulate(PlayerMovement __instance)
        {
            if (!Bug1ProbeRuntime.Enabled) return;
            Bug1ProbeRuntime.OnSimBefore(__instance);
        }

        private static void Postfix_Simulate(PlayerMovement __instance)
        {
            if (!Bug1ProbeRuntime.Enabled) return;
            Bug1ProbeRuntime.OnSimAfter(__instance);
        }

        private static void Prefix_Update(PlayerMovement __instance)
        {
            if (!Bug1ProbeRuntime.Enabled) return;
            Bug1ProbeRuntime.OnBeforeUpdate(__instance);
        }

        private static void Prefix_ReceiveInputs(PlayerInput __instance)
        {
            if (!Bug1ProbeRuntime.Enabled) return;
            Bug1ProbeRuntime.OnReceiveInputsBefore(__instance);
        }

        private static void Postfix_ReceiveInputs(PlayerInput __instance)
        {
            if (!Bug1ProbeRuntime.Enabled) return;
            Bug1ProbeRuntime.OnReceiveInputsAfter(__instance);
        }

        private static void Postfix_Enqueue(System.Collections.Generic.Queue<PlayerInputPacket> __instance, PlayerInputPacket item)
        {
            if (!Bug1ProbeRuntime.Enabled) return;
            Bug1ProbeRuntime.OnEnqueue(__instance, item);
        }

        private static void Postfix_Dequeue(System.Collections.Generic.Queue<PlayerInputPacket> __instance, PlayerInputPacket __result)
        {
            if (!Bug1ProbeRuntime.Enabled) return;
            Bug1ProbeRuntime.OnDequeue(__instance, __result);
        }
    }
}
