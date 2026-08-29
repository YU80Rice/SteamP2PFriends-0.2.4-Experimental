using SteamP2PFriends.Core.Identity;

namespace SteamP2PFriends.MultiObserver.SPI
{
    /// <summary>
    /// 统一生命周期领域适配器接口 (ILifecycleDomainAdapter)
    /// 所有负责实体与地图功能按需生成、滞回释放与故障隔离的领域适配器均实现此接口。
    /// </summary>
    public interface ILifecycleDomainAdapter
    {
        /// <summary>
        /// 领域不可变机器身份。
        /// </summary>
        DomainId DomainId { get; }

        /// <summary>
        /// 面向日志和诊断的显示名称；不参与注册或协议判断。
        /// </summary>
        string DisplayName { get; }

        /// <summary>
        /// 领域声明的标准化能力特性
        /// </summary>
        string Capability { get; }

        /// <summary>
        /// 新会话启动事件
        /// </summary>
        void OnSessionBegin(uint sessionEpoch);

        /// <summary>
        /// 当前会话终止事件
        /// </summary>
        void OnSessionEnd();

        /// <summary>
        /// 0 -> 1 需求激活：观察者进入无人区，获取权威租约并按需生成实体/初始化状态
        /// </summary>
        void OnAcquire(LeaseTicket ticket);

        /// <summary>
        /// 1 -> 0 滞回释放：最后一名观察者离开且滞回定时器到期，安全提交销毁/反注册
        /// </summary>
        void OnRelease(LeaseTicket ticket);

        /// <summary>
        /// 主线程帧心跳驱动
        /// </summary>
        void OnTick(float deltaTime);

        /// <summary>
        /// 远端观察者断开连接事件：精确注销该观察者占用的所有租约
        /// </summary>
        void OnObserverDisconnect(ulong observerId, ulong connectionToken);
    }

    /// <summary>
    /// 可逆的观察者断连边界。
    /// 当外部领域适配器可能在抛异常前已经清理部分状态时，控制面先捕获快照，
    /// 失败事务可通过该接口恢复到调用前的观察者状态。
    /// </summary>
    public interface IReversibleObserverDisconnectAdapter
    {
        object CaptureObserverDisconnectState(ulong observerId, ulong connectionToken);

        void RestoreObserverDisconnectState(
            ulong observerId,
            ulong connectionToken,
            object state);
    }

    /// <summary>
    /// 可逆的领域区域生命周期边界。
    /// 当 Acquire/Release 可能在抛异常前已经改变外部状态时，控制面使用真实快照
    /// 恢复调用前状态，不能用旧 generation 猜测补偿。
    /// </summary>
    public interface IReversibleRegionLifecycleAdapter
    {
        object CaptureRegionState(RegionKey regionKey);

        void RestoreRegionState(RegionKey regionKey, object state);
    }
}
