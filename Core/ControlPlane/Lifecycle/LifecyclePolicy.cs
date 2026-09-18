using System;

namespace SteamP2PFriends.MultiObserver.Lifecycle
{
    /// <summary>
    /// 领域声明的一条重试节奏：起步间隔、逐次倍增后的上限、以及达到静默稳态的尝试次数。
    /// 静默稳态只降低日志量，不改变重试资格或清除语义。
    /// </summary>
    public readonly struct RetrySchedule
    {
        public RetrySchedule(float baseIntervalSeconds, float capSeconds, int quietAttempts)
        {
            if (baseIntervalSeconds <= 0f)
                throw new ArgumentOutOfRangeException(nameof(baseIntervalSeconds), "重试起步间隔必须为正");
            if (capSeconds < baseIntervalSeconds)
                throw new ArgumentOutOfRangeException(nameof(capSeconds), "重试上限不得低于起步间隔");
            if (quietAttempts < 1)
                throw new ArgumentOutOfRangeException(nameof(quietAttempts), "静默阈值至少为一次尝试");

            BaseIntervalSeconds = baseIntervalSeconds;
            CapSeconds = capSeconds;
            QuietAttempts = quietAttempts;
        }

        public float BaseIntervalSeconds { get; }
        public float CapSeconds { get; }
        public int QuietAttempts { get; }

        /// <summary>
        /// 第 attempts 次失败后的等待间隔：起步间隔按 2 的幂倍增，受各自上限约束。
        /// attempts 从 1 开始计数。
        /// </summary>
        public float IntervalFor(int attempts)
        {
            if (attempts < 1) attempts = 1;
            return Math.Min(BaseIntervalSeconds * (1 << Math.Min(attempts - 1, 10)), CapSeconds);
        }
    }

    /// <summary>
    /// 领域声明的 Lifecycle Policy：滞回窗口与两类重试节奏。
    ///
    /// 它是声明而不是执行器——不存储区域状态、不调度重试、不做身份判断；
    /// 何时进入滞回、何时重试、重试身份如何校验全部由共享编排引擎拥有。
    /// 不存在跨域共享的默认滞回或默认 retry 节奏：每条政策各自携带自己的值。
    /// </summary>
    public sealed class LifecyclePolicy
    {
        public LifecyclePolicy(
            float hysteresisSeconds,
            RetrySchedule deferredRetry,
            RetrySchedule failedRetry,
            HeartbeatPolicy heartbeat,
            string source)
        {
            if (hysteresisSeconds < 0f)
                throw new ArgumentOutOfRangeException(nameof(hysteresisSeconds), "滞回窗口不得为负");
            if (string.IsNullOrWhiteSpace(source))
                throw new ArgumentException("Lifecycle Policy 必须声明来源", nameof(source));

            HysteresisSeconds = hysteresisSeconds;
            DeferredRetry = deferredRetry;
            FailedRetry = failedRetry;
            Heartbeat = heartbeat;
            Source = source;
        }

        /// <summary>最后一名观察者离开后，租约保留多久再释放。0 表示立即释放。</summary>
        public float HysteresisSeconds { get; }

        /// <summary>暂缓类失败的 retry 节奏（原生数据未就绪）。</summary>
        public RetrySchedule DeferredRetry { get; }

        /// <summary>一般失败的 retry 节奏。</summary>
        public RetrySchedule FailedRetry { get; }

        /// <summary>
        /// 持续状态（样本暂缓、领域熔断、写入挂起）的诊断心跳节奏。心跳有界：既有间隔下限，
        /// 又有重复上限，且在恢复时必留闭环记录。
        /// </summary>
        public HeartbeatPolicy Heartbeat { get; }

        /// <summary>政策来源标识（例如领域常量名），供诊断与取证引用。</summary>
        public string Source { get; }

        public override string ToString() =>
            $"hysteresis={HysteresisSeconds:0.###}s source={Source}";
    }
}
