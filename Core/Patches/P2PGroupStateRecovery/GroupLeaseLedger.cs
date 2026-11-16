using System;

namespace SteamP2PFriends.Core.Patches.P2PGroupStateRecovery
{
    /// <summary>单个恢复租约（epoch + 地图 + 玩家实例 token）的生命周期状态。</summary>
    internal enum GroupLeaseState
    {
        None = 0,
        Pending = 1,
        Claimed = 2,
        LoadSucceeded = 3,
        NotificationsAttempted = 4,
        Completed = 5,
        NotificationFailed = 6,
        LoadFailed = 7,
        Abandoned = 8
    }

    /// <summary>
    /// T14/E4 账本：一次租约一本账，按 epoch + 地图 + token 记账。
    /// 不变式：0 ≤ NotifyAttempts ≤ RealLoadSuccess ≤ 1；任何违反都记入 InvariantViolations，
    /// 供反例变异用例变红。RealLoadSuccess 只在原生 load 正常返回后递增，
    /// 不得用 wasLoadCalled（原生首行就置 true）冒充成功。
    /// </summary>
    internal sealed class GroupLeaseLedger
    {
        internal long Epoch { get; private set; }
        internal string MapName { get; private set; } = string.Empty;
        internal long Token { get; private set; }
        internal GroupLeaseState State { get; private set; } = GroupLeaseState.None;
        internal int RealLoadEntry { get; private set; }
        internal int RealLoadSuccess { get; private set; }
        internal int NotifyAttempts { get; private set; }
        internal int NotifySuccess { get; private set; }
        internal int NotifyFailed { get; private set; }
        internal bool SkippedEmpty { get; private set; }
        internal bool SkippedWatchdog { get; private set; }
        internal bool Degraded { get; private set; }
        internal string Reason { get; private set; } = string.Empty;

        // T18/F8：不变式违反独立成账本字段，不与 Abandoned 状态位混用。
        internal string InvariantViolation { get; private set; } = string.Empty;
        // T18/F8：pending 已空时仍请求放弃的次数（旧代码静默返回 false）。
        internal int AbandonNoops { get; private set; }

        internal void Reset()
        {
            Epoch = 0L;
            MapName = string.Empty;
            Token = 0L;
            State = GroupLeaseState.None;
            RealLoadEntry = 0;
            RealLoadSuccess = 0;
            NotifyAttempts = 0;
            NotifySuccess = 0;
            NotifyFailed = 0;
            SkippedEmpty = false;
            SkippedWatchdog = false;
            Degraded = false;
            Reason = string.Empty;
            InvariantViolation = string.Empty;
            AbandonNoops = 0;
        }

        internal void Begin(long epoch, string mapName, long token)
        {
            Reset();
            Epoch = epoch;
            MapName = mapName ?? string.Empty;
            Token = token;
            State = GroupLeaseState.Pending;
        }

        internal bool SameLease(long epoch, string mapName, long token)
        {
            return State != GroupLeaseState.None
                && Epoch == epoch
                && Token == token
                && string.Equals(MapName, mapName ?? string.Empty, StringComparison.Ordinal);
        }

        internal void MarkClaimed()
        {
            State = GroupLeaseState.Claimed;
        }

        internal void MarkLoadEntered()
        {
            RealLoadEntry++;
        }

        internal void MarkLoadSucceeded()
        {
            RealLoadSuccess++;
            State = GroupLeaseState.LoadSucceeded;
        }

        internal void MarkLoadFailed(string reason)
        {
            State = GroupLeaseState.LoadFailed;
            Reason = reason ?? string.Empty;
        }

        internal bool CanNotify()
        {
            return State == GroupLeaseState.LoadSucceeded && RealLoadSuccess == 1 && NotifyAttempts == 0;
        }

        internal void MarkNotifyAttempted()
        {
            NotifyAttempts++;
            State = GroupLeaseState.NotificationsAttempted;
        }

        internal void MarkNotifySucceeded()
        {
            NotifySuccess++;
            State = GroupLeaseState.Completed;
        }

        internal void MarkNotifyFailed(string reason)
        {
            NotifyFailed++;
            State = GroupLeaseState.NotificationFailed;
            Reason = reason ?? string.Empty;
        }

        internal void MarkSkippedEmpty()
        {
            SkippedEmpty = true;
            State = GroupLeaseState.Completed;
        }

        internal void MarkSkippedWatchdog(string reason)
        {
            SkippedWatchdog = true;
            Degraded = true;
            Reason = reason ?? string.Empty;
            // 只终止"尚未进入真实 load"的租约；已 LoadFailed/LoadSucceeded 的状态不被兜底改写。
            if (State == GroupLeaseState.Pending || State == GroupLeaseState.Claimed)
                State = GroupLeaseState.Abandoned;
        }

        internal void MarkAbandoned(string reason)
        {
            if (State == GroupLeaseState.None)
            {
                // T18/F8：账本已空时仍被要求放弃，必须留痕而不是静默。
                AbandonNoops++;
                return;
            }
            Degraded = true;
            State = GroupLeaseState.Abandoned;
            Reason = reason ?? string.Empty;
        }

        /// <summary>T18/F8：不变式违反只写独立字段，不改状态机（原实现误记成 Abandoned）。</summary>
        internal void MarkInvariantViolation(string detail)
        {
            InvariantViolation = detail ?? string.Empty;
        }

        /// <summary>T18/F1：pending 在 save 前缀被强制兑现（有界寿命），仅标记降级不改状态机。</summary>
        internal void MarkDegraded(string reason)
        {
            Degraded = true;
            Reason = reason ?? string.Empty;
        }
    }
}
