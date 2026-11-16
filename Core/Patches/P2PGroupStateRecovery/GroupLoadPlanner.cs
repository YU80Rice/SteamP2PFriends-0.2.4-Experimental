using System;

namespace SteamP2PFriends.Core.Patches.P2PGroupStateRecovery
{
    /// <summary>
    /// T14/E2 可注入纯函数 seam：只根据"资格 + 地图 + 实例 token + 核心账"给出三分支决策。
    /// Prefix_Load 只负责取字段并调用本方法，测试直接覆盖 defer / pass-through / forced 三分支，
    /// 从而任何让 Prefix 直接 return true 的变异都会让用例变红。
    /// </summary>
    internal static class GroupLoadPlanner
    {
        internal static GroupLoadDecision Decide(
            GroupRecoveryEligibility eligibility,
            string mapName,
            long playerToken,
            GroupStateRecoveryCore core)
        {
            if (core == null)
                throw new ArgumentNullException(nameof(core));

            // Guest / 普通单机 / dedicated / 非唯一真实本地 Host：一律按原生顺序放行。
            if (!eligibility.IsEligible)
                return GroupLoadDecision.PassThrough;

            // 非 SURVIVAL 或拿不到实例 token：不干预。
            if (string.IsNullOrEmpty(mapName) || playerToken <= 0L)
            {
                core.NoteUnnamedPassThrough();
                return GroupLoadDecision.PassThrough;
            }

            return core.OnLoadRequested(playerToken, mapName);
        }
    }
}
