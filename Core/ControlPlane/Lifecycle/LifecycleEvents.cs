namespace SteamP2PFriends.MultiObserver.Lifecycle
{
    /// <summary>
    /// 共享编排引擎的转换事件名。事件名是取证词汇，不是控制流开关：引擎按状态机决定
    /// 何时转换，这里只给转换起名字。
    /// </summary>
    public static class LifecycleEvents
    {
        public const string SessionBegin = "SessionBegin";
        public const string SessionBeginCleanup = "SessionBeginCleanup";
        public const string SessionEnd = "SessionEnd";
        public const string SessionState = "LifecycleSession";

        public const string ObserverUpdate = "ObserverUpdate";
        public const string ObserverRemove = "ObserverRemove";
        public const string ObserverCompensation = "ObserverCompensation";

        /// <summary>样本暂缓（Deferred Observer Demand）与它的有界心跳/闭环记录共用的事件名。</summary>
        public const string ObserverDeferred = "ObserverDeferred";

        /// <summary>会话级写入闸门：挂起、持续挂起的心跳与恢复闭环。</summary>
        public const string SessionSuspended = "LifecycleWriteGate";

        /// <summary>领域熔断及其有界心跳（被推进路径跳过时的持续异常记录）。</summary>
        public const string DomainFault = "LifecycleDomainFault";

        public const string RegionAcquire = "LeaseAcquire";
        public const string RegionAcquireRetry = "LeaseAcquireRetry";
        public const string RegionReleaseScheduled = "LeaseReleaseScheduled";
        public const string RegionReentry = "LeaseReentry";
        public const string RegionRelease = "LeaseRelease";
        public const string RegionReleaseAttempt = "LeaseReleaseAttempt";
        public const string RegionReleaseDeferred = "LeaseReleaseDeferred";
        public const string RegionReleaseCompensation = "LeaseReleaseCompensation";
        public const string RegionGenerationRead = "GenerationRead";
        public const string RegionExit = "RegionExit";

        public const string ReplicationEnqueue = "SnapshotEnqueue";
        public const string ReplicationRemove = "SnapshotRemove";

        public const string LifecycleTick = "LifecycleTick";
        public const string ReplicationTick = "ReplicationTick";
    }
}
