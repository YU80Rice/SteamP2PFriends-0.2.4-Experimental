using SteamP2PFriends.Core.Identity;
using SteamP2PFriends.MultiObserver.SPI;

namespace SteamP2PFriends.MultiObserver.Lifecycle
{
    /// <summary>
    /// 领域把原生失败翻译成的引擎可执行结果。引擎不认识任何领域异常类型，
    /// 也不解析异常文本——分类由领域在端口上完成。
    /// </summary>
    public enum EDomainFailureKind
    {
        /// <summary>一般失败：区域级隔离，较长间隔重试。</summary>
        Failed = 0,

        /// <summary>暂缓失败：原生数据尚未就绪，短间隔重试、不计 fault。</summary>
        Deferred = 1,

        /// <summary>业务拒绝：状态保持不变、不执行补偿（例如票据代次不匹配）。</summary>
        Rejected = 2
    }

    /// <summary>
    /// Domain Execution Port：领域在共享编排引擎上的唯一接口。
    ///
    /// 领域在这里声明自己的 Lifecycle Policy，并实现原生操作与可逆边界；它不扫描观察者、
    /// 不重算需求、不持有编排状态机——需求聚合、Acquire/Release、滞回、身份校验、retry、
    /// 补偿调度与故障隔离都由引擎拥有。接入新领域只需注册 Demand Policy、Lifecycle Policy
    /// 与本端口，不在引擎里增加领域分支。
    /// </summary>
    public interface IDomainExecutionPort
    {
        /// <summary>领域不可变机器身份。</summary>
        DomainId DomainId { get; }

        /// <summary>面向日志与诊断的显示名称；不参与注册或协议判断。</summary>
        string DisplayName { get; }

        /// <summary>领域声明的生命周期政策（滞回窗口、retry 节奏、静默阈值）。</summary>
        LifecyclePolicy LifecyclePolicy { get; }

        void OnSessionBegin(uint sessionEpoch);
        void OnSessionEnd();

        /// <summary>重置该领域的逐观察者复制状态（会话边界与失败收尾共用）。</summary>
        void ResetReplication(uint sessionEpoch);

        /// <summary>0 -&gt; 1 需求激活：取得原生区域权威。</summary>
        void OnAcquire(LeaseTicket ticket);

        /// <summary>N -&gt; 0 滞回释放：把最终状态交还原生权威。</summary>
        void OnRelease(LeaseTicket ticket);

        /// <summary>领域生命周期帧心跳。</summary>
        void OnLifecycleTick(float deltaTime);

        /// <summary>领域复制帧心跳。</summary>
        void OnReplicationTick(float deltaTime);

        void OnObserverReplicationEntered(ulong observerId, ulong connectionToken, RegionKey regionKey);
        void OnObserverReplicationExited(ulong observerId, ulong connectionToken, RegionKey regionKey);
        void OnObserverDisconnected(ulong observerId, ulong connectionToken);

        /// <summary>读取原生区域代次。引擎消费该轴，不重新生成它。</summary>
        RegionGeneration ReadRegionGeneration(RegionKey regionKey);

        /// <summary>把区域原生失败翻译成引擎可执行的结果。</summary>
        EDomainFailureKind ClassifyRegionFailure(System.Exception exception);

        /// <summary>把释放阶段的原生失败翻译成引擎可执行的结果。</summary>
        EDomainFailureKind ClassifyReleaseFailure(System.Exception exception);

        object CaptureRegionState(RegionKey regionKey);
        void RestoreRegionState(RegionKey regionKey, object state);
        object CaptureObserverReplicationState(ulong observerId, ulong connectionToken);
        void RestoreObserverReplicationState(ulong observerId, ulong connectionToken, object state);
        object CaptureObserverDisconnectState(ulong observerId, ulong connectionToken);
        void RestoreObserverDisconnectState(ulong observerId, ulong connectionToken, object state);
    }
}
