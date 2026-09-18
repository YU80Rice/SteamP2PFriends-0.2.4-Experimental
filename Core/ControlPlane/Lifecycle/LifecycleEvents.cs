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
