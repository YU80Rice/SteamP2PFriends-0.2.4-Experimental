using System;

namespace SteamP2PFriends.MultiObserver.SPI
{
    /// <summary>
    /// 统一状态快照与增量复制适配器接口 (IStateReplicationAdapter)
    /// 所有负责逐观察者状态初始下发、外观同步与周期性增量流复制的领域适配器均实现此接口。
    /// </summary>
    public interface IStateReplicationAdapter
    {
        /// <summary>
        /// 领域唯一名称
        /// </summary>
        string DomainName { get; }

        /// <summary>
        /// 观察者进入该领域空间区域
        /// </summary>
        void OnObserverEntered(ulong observerId, ulong connectionToken, int regionKey);

        /// <summary>
        /// 观察者离开该领域空间区域
        /// </summary>
        void OnObserverExited(ulong observerId, ulong connectionToken, int regionKey);

        /// <summary>
        /// 触发/调度周期性增量数据流广播
        /// </summary>
        void OnReplicationTick(float deltaTime);

        /// <summary>
        /// 重置所有观察者的复制状态
        /// </summary>
        void ResetReplication(uint sessionEpoch);
    }
}
