using SteamP2PFriends.MultiObserver.Lifecycle;

namespace SteamP2PFriends.Adapters.Resource
{
    /// <summary>
    /// Resource 域的 Lifecycle Policy 声明：滞回窗口与两类 acquire retry 节奏。
    ///
    /// 这里只做声明——不等价于执行器：何时进入滞回、何时重试、重试身份如何校验都由共享编排
    /// 引擎拥有。资源半径与滞回值都取自资源域自己的常量，不得升格为跨域的共享默认值。
    /// </summary>
    public static class ResourceLifecyclePolicy
    {
        /// <summary>政策来源标识：滞回窗口取自资源区域生命周期适配器的默认值。</summary>
        public const string Source = "ResourceRegionLifecycleAdapter.DefaultHysteresisSeconds";

        /// <summary>暂缓类重试的起步间隔（秒）：原生 foliage 尚未烘焙，短暂等待后即可成功。</summary>
        public const float DeferredAcquireRetryIntervalSeconds = 2.0f;

        /// <summary>暂缓类重试的间隔上限（秒）。</summary>
        public const float DeferredAcquireRetryCapSeconds = 32f;

        /// <summary>暂缓连续重试达到该次数后降级静默稳态：仅降低日志量，重试与清除语义不变。</summary>
        public const int DeferredAcquireQuietAttempts = 5;

        /// <summary>一般 acquire 失败的重试起步间隔（秒）。</summary>
        public const float FailedAcquireRetryIntervalSeconds = 10.0f;

        /// <summary>一般 acquire 失败的重试间隔上限（秒）。</summary>
        public const float FailedAcquireRetryCapSeconds = 60f;

        /// <summary>持续状态（样本暂缓、领域熔断、写入挂起）的心跳间隔（秒）：不逐帧淹没日志。</summary>
        public const float PersistentStateHeartbeatSeconds = 5f;

        /// <summary>持续状态心跳的重复上限：用尽后写出显式终止记录，等待状态变化重新起搏。</summary>
        public const int PersistentStateHeartbeatRepeats = 6;

        /// <summary>
        /// 声明 Resource 的 Lifecycle Policy。滞回窗口由调用方从资源域自己的常量读入后传入
        /// （生产接线读 <see cref="ResourceRegionLifecycleAdapter.DefaultHysteresisSeconds"/>），
        /// 资源域不得借用共享引擎里的默认窗口。
        /// </summary>
        public static LifecyclePolicy Create(float hysteresisSeconds)
        {
            return new LifecyclePolicy(
                hysteresisSeconds,
                new RetrySchedule(
                    DeferredAcquireRetryIntervalSeconds,
                    DeferredAcquireRetryCapSeconds,
                    DeferredAcquireQuietAttempts),
                new RetrySchedule(
                    FailedAcquireRetryIntervalSeconds,
                    FailedAcquireRetryCapSeconds,
                    int.MaxValue),
                new HeartbeatPolicy(
                    PersistentStateHeartbeatSeconds,
                    PersistentStateHeartbeatRepeats),
                Source);
        }
    }
}
