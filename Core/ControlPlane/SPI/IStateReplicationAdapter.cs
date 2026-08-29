using SteamP2PFriends.Core.Identity;

namespace SteamP2PFriends.MultiObserver.SPI
{
    /// <summary>
    /// 统一状态快照与增量复制适配器接口 (IStateReplicationAdapter)
    /// 所有负责逐观察者状态初始下发、外观同步与周期性增量流复制的领域适配器均实现此接口。
    /// </summary>
    public interface IStateReplicationAdapter
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
        /// 观察者进入该领域空间区域
        /// </summary>
        void OnObserverEntered(ulong observerId, ulong connectionToken, RegionKey regionKey);

        /// <summary>
        /// 观察者离开该领域空间区域
        /// </summary>
        void OnObserverExited(ulong observerId, ulong connectionToken, RegionKey regionKey);

        /// <summary>
        /// 触发/调度周期性增量数据流广播
        /// </summary>
        void OnReplicationTick(float deltaTime);

        /// <summary>
        /// 重置所有观察者的复制状态
        /// </summary>
        void ResetReplication(uint sessionEpoch);
    }

    /// <summary>
    /// 可逆的观察者复制状态边界。
    /// 复制调用可能在抛异常前已经写入或删除部分快照，因此补偿必须恢复真实快照，
    /// 不能只调用相反方向的方法猜测状态。
    /// </summary>
    public interface IReversibleObserverReplicationAdapter
    {
        object CaptureObserverReplicationState(ulong observerId, ulong connectionToken);

        void RestoreObserverReplicationState(
            ulong observerId,
            ulong connectionToken,
            object state);
    }
}
