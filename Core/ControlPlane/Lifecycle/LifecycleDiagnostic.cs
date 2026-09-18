using System;
using SteamP2PFriends.Core.Identity;

namespace SteamP2PFriends.MultiObserver.Lifecycle
{
    /// <summary>
    /// 一次生命周期转换的结果分类。这是引擎的内部词汇，领域诊断出口再把它映射成
    /// 自己的受控值集合；引擎不解析异常文本，也不认识任何领域类型。
    /// </summary>
    public enum ELifecycleOutcome
    {
        Success = 0,
        Failed = 1,
        Skipped = 2,
        Rejected = 3,
        Deferred = 4,
        Attempt = 5,
        Scheduled = 6
    }

    /// <summary>
    /// 观测路径：本次转换走的是插件共享面还是失败回退。只描述取证语义，不参与控制流判断。
    /// 领域自己的原生/旧路径日志不经过这个出口（那是领域数据面自己的取证），因此这里不为其
    /// 预留取值——未产出的枚举成员只会变成无人维护的死分支。
    /// </summary>
    public enum ELifecyclePath
    {
        Spi = 0,
        Fallback = 1
    }

    /// <summary>
    /// 诊断级别。级别不是结果的同义反复——同一结果类别可能按转换点不同而落在不同级别
    /// （例如「暂缓重试」是常规信息，「跳过」在 acquire 路径是失败、在过时退出路径是常规信息），
    /// 因此由发出转换的一方显式声明。
    /// </summary>
    public enum ELifecycleSeverity
    {
        Info = 0,
        Warn = 1,
        Error = 2
    }

    /// <summary>
    /// 引擎产出的统一转换诊断记录。字段集覆盖规格要求的最小关联键：
    /// 领域、区域、转换、会话代次、区域代次、尝试次数、结果与原因；观察者贡献类转换
    /// 另带观察者与连接代次。
    /// </summary>
    public sealed class LifecycleDiagnostic
    {
        /// <param name="domain">
        /// 转换所属领域。会话边界这类跨域转换使用未定义（default）的 Domain Id，
        /// 表示它是引擎级而非领域级转换。
        /// </param>
        public LifecycleDiagnostic(DomainId domain, string eventName, ELifecycleOutcome outcome)
        {
            if (string.IsNullOrWhiteSpace(eventName))
                throw new ArgumentException("生命周期诊断必须携带事件名", nameof(eventName));

            Domain = domain;
            EventName = eventName;
            Outcome = outcome;
            Role = "Host";
            Path = ELifecyclePath.Spi;
            SpiActive = true;
            Severity = DeriveSeverity(outcome);
        }

        /// <summary>该路径是否表示共享面仍在正常服务（区分 spiActive 取证字段）。</summary>
        public static bool IsSharedPath(ELifecyclePath path) => path == ELifecyclePath.Spi;

        /// <summary>结果类别的默认级别：失败即错误，拒绝即警告，其余为常规信息。</summary>
        public static ELifecycleSeverity DeriveSeverity(ELifecycleOutcome outcome)
        {
            switch (outcome)
            {
                case ELifecycleOutcome.Failed: return ELifecycleSeverity.Error;
                case ELifecycleOutcome.Rejected: return ELifecycleSeverity.Warn;
                default: return ELifecycleSeverity.Info;
            }
        }

        public DomainId Domain { get; }
        public string EventName { get; }
        public ELifecycleOutcome Outcome { get; }

        /// <summary>日志角色（Host / Shared）。</summary>
        public string Role { get; set; }

        public ELifecyclePath Path { get; set; }
        public bool SpiActive { get; set; }
        public ELifecycleSeverity Severity { get; set; }

        public RegionKey Region { get; set; }
        public bool HasRegion { get; set; }
        public ulong SessionEpoch { get; set; }
        public ulong ConnectionGeneration { get; set; }
        public uint RegionGeneration { get; set; }
        public ulong ObserverId { get; set; }
        public int Attempt { get; set; }
        public string Detail { get; set; }
    }
}
