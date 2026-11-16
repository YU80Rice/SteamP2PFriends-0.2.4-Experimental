using SDG.Unturned;
using System;
using System.Globalization;
using System.Reflection;

namespace SteamP2PFriends.Platform.Diagnostics.Patches
{
    /// <summary>
    /// t45：原生速度上限解析。取值必须来自原生实现，不许猜：
    ///   PlayerMovement.SPEED_SPRINT（private static readonly float，Assembly-CSharp）
    ///   × (1 + 0.25 × PlayerSkills.mastery(OFFENSE, EXERCISE))   —— 与 PlayerMovement.speed 的 sprint 分支同式
    ///   × PlayerMovement.totalSpeedMultiplier（plugin × clothing × equipment）
    /// 任一环取不到即返回 false，入口据此拒绝开启（NATIVE_SPEED_CAP_UNRESOLVED），
    /// 绝不用历史值/观测值/常数默认值凑过速度上限门。
    /// </summary>
    internal static class Bug1ProbeNativeSpeed
    {
        private const BindingFlags StaticNonPublic = BindingFlags.Static | BindingFlags.NonPublic;

        /// <summary>t48/F4：spec §3.4 的走/跑两档与「按目标姿态自动选档」。</summary>
        public const string TierAuto = "auto";
        public const string TierWalk = "walk";
        public const string TierSprint = "sprint";

        /// <summary>纯算术核心（可离线用例）：三个因子任一非法即失败，并给出可读证据。</summary>
        internal static bool Compute(double sprintConstant, double exerciseMultiplier, double totalSpeedMultiplier,
            out double capUS, out string evidence)
        {
            capUS = 0d;
            if (!IsPositiveFinite(sprintConstant)) { evidence = "sprint_constant_invalid"; return false; }
            if (!IsPositiveFinite(exerciseMultiplier)) { evidence = "exercise_multiplier_invalid"; return false; }
            if (!IsPositiveFinite(totalSpeedMultiplier)) { evidence = "total_speed_multiplier_invalid"; return false; }
            double value = sprintConstant * exerciseMultiplier * totalSpeedMultiplier;
            if (!IsPositiveFinite(value) || value > 100d) { evidence = "speed_cap_out_of_range"; return false; }
            capUS = value;
            evidence = string.Format(CultureInfo.InvariantCulture,
                "SPEED_SPRINT={0:R};exercise=1+0.25*mastery(EXERCISE)={1:R};totalSpeedMultiplier={2:R};cap={3:R}",
                sprintConstant, exerciseMultiplier, totalSpeedMultiplier, value);
            return true;
        }

        /// <summary>Unity 侧解析：从目标 PlayerMovement 读原生常量与当前倍率；异常一律视为取不到。</summary>
        internal static bool TryResolve(Player player, out double walkUS, out double sprintUS, out bool sprinting,
            out string evidence)
        {
            walkUS = 0d;
            sprintUS = 0d;
            sprinting = false;
            evidence = null;
            try
            {
                if (player == null || player.movement == null || player.skills == null)
                {
                    evidence = "player_or_movement_missing";
                    return false;
                }
                FieldInfo standField = typeof(PlayerMovement).GetField("SPEED_STAND", StaticNonPublic);
                if (standField == null || standField.FieldType != typeof(float))
                {
                    evidence = "SPEED_STAND_member_not_found";
                    return false;
                }
                FieldInfo sprintField = typeof(PlayerMovement).GetField("SPEED_SPRINT", StaticNonPublic);
                if (sprintField == null || sprintField.FieldType != typeof(float))
                {
                    evidence = "SPEED_SPRINT_member_not_found";
                    return false;
                }
                double standConstant = (float)standField.GetValue(null);
                double sprintConstant = (float)sprintField.GetValue(null);
                double mastery = player.skills.mastery((int)EPlayerSpeciality.OFFENSE, (int)EPlayerOffense.EXERCISE);
                double exercise = 1d + 0.25d * mastery;
                double totalMultiplier = player.movement.totalSpeedMultiplier;
                if (!ComputeTiers(standConstant, sprintConstant, exercise, totalMultiplier,
                        out walkUS, out sprintUS, out evidence))
                    return false;
                EPlayerStance stance = player.stance.stance;
                sprinting = stance == EPlayerStance.SPRINT;
                evidence = evidence
                    + ";src=Assembly-CSharp!PlayerMovement.SPEED_STAND+SPEED_SPRINT+totalSpeedMultiplier,PlayerSkills.mastery"
                    + ";stance=" + stance;
                return true;
            }
            catch (Exception ex)
            {
                evidence = "resolve_failed:" + ex.GetType().Name;
                return false;
            }
        }

        /// <summary>
        /// t48/F4：spec §3.4「走/跑各有上限」——同时冻结两档，绝不恒取 sprint。
        /// 原生依据（.scratch/bug2-sdk-longpaths-20261030/.../Unturned/Player/PlayerMovement.cs:92-93）：
        ///   SPEED_STAND = 4.5f（walk 档）、SPEED_SPRINT = 7f（sprint 档），
        /// 两档都乘同一个 exercise 与 totalSpeedMultiplier（:303/:311 同式）。
        /// </summary>
        internal static bool ComputeTiers(double standConstant, double sprintConstant, double exerciseMultiplier,
            double totalSpeedMultiplier, out double walkUS, out double sprintUS, out string evidence)
        {
            walkUS = 0d;
            sprintUS = 0d;
            if (!IsPositiveFinite(standConstant)) { evidence = "stand_constant_invalid"; return false; }
            double walkCap;
            string walkEvidence;
            if (!Compute(standConstant, exerciseMultiplier, totalSpeedMultiplier, out walkCap, out walkEvidence))
            {
                evidence = "walk:" + walkEvidence;
                return false;
            }
            double sprintCap;
            string sprintEvidence;
            if (!Compute(sprintConstant, exerciseMultiplier, totalSpeedMultiplier, out sprintCap, out sprintEvidence))
            {
                evidence = "sprint:" + sprintEvidence;
                return false;
            }
            if (sprintCap < walkCap) { evidence = "sprint_cap_below_walk_cap"; return false; }
            walkUS = walkCap;
            sprintUS = sprintCap;
            evidence = string.Format(CultureInfo.InvariantCulture,
                "SPEED_STAND={0:R};SPEED_SPRINT={1:R};exercise=1+0.25*mastery(EXERCISE)={2:R};"
                + "totalSpeedMultiplier={3:R};walk_cap={4:R};sprint_cap={5:R}",
                standConstant, sprintConstant, exerciseMultiplier, totalSpeedMultiplier, walkCap, sprintCap);
            return true;
        }

        /// <summary>
        /// 本次窗口用哪一档：auto/空 → 按目标当前姿态（SPRINT 才算跑档）；walk/sprint → 显式指定；
        /// 其它取值返回 null，由入口按 NATIVE_SPEED_CAP_UNRESOLVED 拒绝（不许静默回落）。
        /// </summary>
        internal static string ResolveTier(string configured, bool sprinting)
        {
            string tier = (configured ?? string.Empty).Trim().ToLowerInvariant();
            if (tier.Length == 0 || tier == TierAuto) return sprinting ? TierSprint : TierWalk;
            if (tier == TierWalk) return TierWalk;
            if (tier == TierSprint) return TierSprint;
            return null;
        }

        private static bool IsPositiveFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value) && value > 0d;
        }
    }
}
