using System;
using System.Collections.Generic;

namespace SteamP2PFriends.MultiObserver.Demand
{
    /// <summary>
    /// Control Plane 唯一的 World Presence Observer 空间事实存储（Observer Spatial Authority）。
    ///
    /// 领域不得各自持有观察者位置或自建索引：Demand Projection Engine 只从这里读事实，
    /// 领域接缝只提交观测值。同一事实的重复提交是幂等的——第二个领域提交同一份样本不会
    /// 改变事实，也不会产生第二次空间迁移语义。
    ///
    /// 线程约束与既有 Control Plane 一致：调用方在游戏线程串行驱动；锁只保证单次调用的
    /// 内部一致性，不承诺跨对象的原子事务。
    /// </summary>
    public sealed class ObserverSpatialAuthority
    {
        private readonly object _sync = new object();
        private readonly Dictionary<ulong, ObserverPresence> _facts =
            new Dictionary<ulong, ObserverPresence>();

        public int Count
        {
            get { lock (_sync) return _facts.Count; }
        }

        /// <summary>提交或刷新一条观察者事实。相同事实重复提交为幂等空操作。</summary>
        public void Observe(ObserverPresence presence)
        {
            lock (_sync) _facts[presence.ObserverId] = presence;
        }

        public bool TryGet(ulong observerId, out ObserverPresence presence)
        {
            lock (_sync) return _facts.TryGetValue(observerId, out presence);
        }

        /// <summary>移除观察者事实；不存在时为幂等空操作。</summary>
        public bool Remove(ulong observerId)
        {
            lock (_sync) return _facts.Remove(observerId);
        }

        public void Clear()
        {
            lock (_sync) _facts.Clear();
        }
    }
}
