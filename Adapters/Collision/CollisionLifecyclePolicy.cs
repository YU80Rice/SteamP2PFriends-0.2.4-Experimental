using SteamP2PFriends.MultiObserver.Lifecycle;

namespace SteamP2PFriends.Adapters.Collision
{
    /// <summary>Collision 自己声明的生命周期政策；不借用 Resource 的政策常量。</summary>
    public static class CollisionLifecyclePolicy
    {
        public const string Source = "CollisionLevelObjectOverrideLifecycle";
        public const float HysteresisSeconds = 2.0f;
        public const float DeferredRetryIntervalSeconds = 1.0f;
        public const float DeferredRetryCapSeconds = 16.0f;
        public const int DeferredRetryQuietAttempts = 4;
        public const float FailedRetryIntervalSeconds = 5.0f;
        public const float FailedRetryCapSeconds = 30.0f;
        public const float HeartbeatIntervalSeconds = 5.0f;
        public const int HeartbeatRepeats = 6;

        public static LifecyclePolicy Create(float hysteresisSeconds = HysteresisSeconds)
        {
            return new LifecyclePolicy(
                hysteresisSeconds,
                new RetrySchedule(DeferredRetryIntervalSeconds, DeferredRetryCapSeconds,
                    DeferredRetryQuietAttempts),
                new RetrySchedule(FailedRetryIntervalSeconds, FailedRetryCapSeconds,
                    int.MaxValue),
                new HeartbeatPolicy(HeartbeatIntervalSeconds, HeartbeatRepeats), Source);
        }
    }
}
