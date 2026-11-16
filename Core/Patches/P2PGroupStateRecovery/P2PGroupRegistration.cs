using System;

namespace SteamP2PFriends.Core.Patches.P2PGroupStateRecovery
{
    /// <summary>T14/E5：生产登记的可观测终态（不再"只打一行日志就静默降级"）。</summary>
    internal enum P2PGroupRegistrationState
    {
        Unregistered = 0,
        Registering = 1,
        Ready = 2,
        Failed = 3
    }

    /// <summary>T14/E5：登记失败的稳定原因枚举，供父入口与测试断言。</summary>
    internal enum P2PGroupRegistrationFailureReason
    {
        None = 0,
        TargetResolveFailed = 1,
        PatchVerifyFailed = 2,
        LifecycleSubscribeFailed = 3,
        StaleOwnerOrHotReload = 4,
        RollbackFailed = 5
    }

    /// <summary>T14/E5：失败态快照；日志抛异常也不得抹掉。</summary>
    internal struct P2PGroupRegistrationFailure
    {
        internal string Feature;
        internal int RegistrationAttempt;
        internal string OwnerId;
        internal string ModuleMvid;
        internal string Original;
        internal string PatchMethod;
        internal string Stage;
        internal P2PGroupRegistrationFailureReason Reason;
        internal string RollbackStatus;
        internal bool RestartRequired;
    }

    /// <summary>
    /// T14/E5：登记失败必须抛给父入口（PatchRegistrationAuditModules 的 catch 会置
    /// _registrationStageFailed=true），不得只记 Error 后静默返回。
    /// </summary>
    internal sealed class P2PGroupRegistrationException : Exception
    {
        internal P2PGroupRegistrationException(P2PGroupRegistrationFailure failure, string message, Exception inner)
            : base(message, inner)
        {
            Failure = failure;
        }

        internal P2PGroupRegistrationFailure Failure { get; }
    }
}
