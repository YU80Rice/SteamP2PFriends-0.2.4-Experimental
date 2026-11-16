using SDG.Unturned;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace SteamP2PFriends.Platform.Diagnostics.Patches
{
    internal enum Bug1TargetSelectionStatus
    {
        Selected = 0,
        NotFound = 1,
        Ambiguous = 2,
    }

    /// <summary>
    /// R8-01：单个 client 的纯数据快照。不持有任何 Unity/SDK 对象引用，
    /// 因此目标选择可以在控制台进程里离线跑用例，不需要造 SteamPlayer/Player。
    /// </summary>
    internal sealed class Bug1ClientSnapshot
    {
        public bool IsNull;
        public bool HasPlayerId;
        public bool HasPlayer;
        public string PlayerName;
        public ulong SteamId;
        public int SourceIndex;
    }

    internal sealed class Bug1TargetSelection
    {
        public Bug1TargetSelectionStatus Status;
        public int SelectedIndex = -1;
        public ulong SteamId;
        public string PlayerName;
        public string ErrorCode;
        public string Reason = string.Empty;
        public int CandidateCount;
        public readonly List<string> Skipped = new List<string>();
    }

    /// <summary>
    /// t48 / R8-01：目标选择的唯一实现点。
    /// 背景（上一轮 Bug3 崩溃同坑）：SDG.Unturned.SteamPlayerID 重载了 == 与 !=
    /// （.scratch/bug2-sdk-longpaths-20261030/.../Unturned/Player/SteamPlayerID.cs:136-144），
    /// 实现直接取两侧 .steamID，**任一侧为 null 都抛 NullReferenceException**（连 null == null 也抛）。
    /// 所以对 playerID 用 == / != 判空在真实主机上必崩；本类只用 ReferenceEquals，
    /// 且 Control.ResolveTarget / Control.FindPlayer / Runtime.TryBindTarget 三处都必须经本类取快照。
    /// </summary>
    internal static class Bug1ProbeTargetSelector
    {
        public const string SkipNullClient = "null-client";
        public const string SkipNullPlayerId = "playerid-null";
        public const string SkipSelf = "self";
        public const string SkipSelectorMismatch = "selector-mismatch";

        /// <summary>
        /// 唯一判空点：ReferenceEquals 而非 ==（见类注释）。client 为 null 时返回 IsNull 快照，
        /// playerID 为 null 时返回 HasPlayerId=false 快照，均不抛异常。
        /// </summary>
        public static Bug1ClientSnapshot Snapshot(SteamPlayer client, int index)
        {
            var snapshot = new Bug1ClientSnapshot { SourceIndex = index };
            if (ReferenceEquals(client, null)) { snapshot.IsNull = true; return snapshot; }
            SteamPlayerID id = client.playerID;
            if (ReferenceEquals(id, null)) return snapshot;
            snapshot.HasPlayerId = true;
            snapshot.PlayerName = id.playerName;
            snapshot.SteamId = id.steamID.m_SteamID;
            snapshot.HasPlayer = !ReferenceEquals(client.player, null);
            return snapshot;
        }

        public static List<Bug1ClientSnapshot> SnapshotAll(List<SteamPlayer> clients)
        {
            var snapshots = new List<Bug1ClientSnapshot>();
            if (clients == null) return snapshots;
            for (int i = 0; i < clients.Count; i++) snapshots.Add(Snapshot(clients[i], i));
            return snapshots;
        }

        /// <summary>
        /// Host 视角选择被观察客机：排除 Host 自己（localSteamId）、空选择器只在唯一客机时选中。
        /// 输出 Selected/NotFound/Ambiguous + 错误码 + 每个被跳过 client 的原因，便于日志与用例断言。
        /// </summary>
        public static Bug1TargetSelection Select(IList<Bug1ClientSnapshot> clients, string selector, ulong localSteamId)
        {
            string needle = (selector ?? string.Empty).Trim();
            var selection = new Bug1TargetSelection();
            int matches = 0;
            int firstMatch = -1;
            for (int i = 0; clients != null && i < clients.Count; i++)
            {
                Bug1ClientSnapshot client = clients[i];
                if (client == null || client.IsNull) { selection.Skipped.Add(SkipNullClient); continue; }
                if (!client.HasPlayerId) { selection.Skipped.Add(SkipNullPlayerId); continue; }
                if (localSteamId != 0UL && client.SteamId == localSteamId) { selection.Skipped.Add(SkipSelf); continue; }
                if (needle.Length > 0 && !Bug1ProbeEntryLogic.MatchesTarget(needle, client.PlayerName, client.SteamId))
                {
                    selection.Skipped.Add(SkipSelectorMismatch);
                    continue;
                }
                matches++;
                if (firstMatch < 0) firstMatch = i;
                selection.SteamId = client.SteamId;
                selection.PlayerName = client.PlayerName;
            }
            selection.CandidateCount = matches;
            if (matches == 0)
            {
                selection.Status = Bug1TargetSelectionStatus.NotFound;
                selection.ErrorCode = needle.Length > 0
                    ? Bug1ProbeEntryLogic.ErrorTargetNotFound
                    : Bug1ProbeEntryLogic.ErrorTargetMissing;
                selection.Reason = needle.Length > 0 ? "selector-matched-nothing" : "no-remote-client";
                return selection;
            }
            if (matches > 1)
            {
                selection.Status = Bug1TargetSelectionStatus.Ambiguous;
                selection.ErrorCode = Bug1ProbeEntryLogic.ErrorTargetAmbiguous;
                selection.Reason = "multiple-clients-matched";
                return selection;
            }
            selection.Status = Bug1TargetSelectionStatus.Selected;
            selection.SelectedIndex = firstMatch;
            selection.Reason = "unique-match";
            return selection;
        }

        /// <summary>
        /// 按已冻结的 SteamID64 绑定唯一 client（Runtime.TryBindTarget 用）。
        /// localSteamId 传 0 表示不做自我排除——目标 ID 在开启时已排除过 Host 自己。
        /// </summary>
        public static Bug1TargetSelection SelectBySteamId(IList<Bug1ClientSnapshot> clients, ulong steamId)
        {
            if (steamId == 0UL)
            {
                var missing = new Bug1TargetSelection
                {
                    Status = Bug1TargetSelectionStatus.NotFound,
                    ErrorCode = Bug1ProbeEntryLogic.ErrorTargetMissing,
                    Reason = "target-steam-id-missing",
                };
                return missing;
            }
            return Select(clients, steamId.ToString(CultureInfo.InvariantCulture), 0UL);
        }
    }
}
