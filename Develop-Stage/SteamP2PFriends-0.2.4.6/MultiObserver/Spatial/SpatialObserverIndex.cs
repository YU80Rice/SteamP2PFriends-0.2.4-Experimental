using System;
using System.Collections.Generic;

namespace SteamP2PFriends.MultiObserver.Spatial
{
    /// <summary>
    /// 空间差异事件结构 (Spatial Relevance Diff)
    /// </summary>
    public readonly struct SpatialRelevanceDiff
    {
        public readonly ulong ObserverId;
        public readonly ulong ConnectionToken;
        public readonly int[] EnteredKeys;
        public readonly int[] ExitedKeys;
        public readonly bool HasChanges;

        public SpatialRelevanceDiff(
            ulong observerId,
            ulong connectionToken,
            int[] enteredKeys,
            int[] exitedKeys)
        {
            ObserverId = observerId;
            ConnectionToken = connectionToken;
            EnteredKeys = enteredKeys ?? Array.Empty<int>();
            ExitedKeys = exitedKeys ?? Array.Empty<int>();
            HasChanges = (EnteredKeys.Length > 0) || (ExitedKeys.Length > 0);
        }
    }

    /// <summary>
    /// 统一空间观察者索引中枢 (SpatialObserverIndex)
    /// 纯内存空间映射与差异计算器（零 Unity / Unturned 依赖）。
    /// 一次性计算观察者空间足迹，并计算出进入/离开的差异集，广播给所有适配器。
    /// </summary>
    public sealed class SpatialObserverIndex
    {
        private sealed class ObserverSpatialState
        {
            public ulong ConnectionToken;
            public readonly HashSet<int> ActiveRegions = new HashSet<int>();
        }

        private readonly object _sync = new object();
        private readonly Dictionary<ulong, ObserverSpatialState> _observers =
            new Dictionary<ulong, ObserverSpatialState>();

        /// <summary>
        /// 清空所有观察者空间索引
        /// </summary>
        public void Clear()
        {
            lock (_sync)
            {
                _observers.Clear();
            }
        }

        /// <summary>
        /// 注销指定观察者并返回其释放的所有区域
        /// </summary>
        public SpatialRelevanceDiff RemoveObserver(ulong observerId)
        {
            lock (_sync)
            {
                if (!_observers.TryGetValue(observerId, out ObserverSpatialState state))
                {
                    return new SpatialRelevanceDiff(observerId, 0UL, Array.Empty<int>(), Array.Empty<int>());
                }

                int[] exited = new int[state.ActiveRegions.Count];
                state.ActiveRegions.CopyTo(exited);
                ulong token = state.ConnectionToken;

                _observers.Remove(observerId);

                return new SpatialRelevanceDiff(observerId, token, Array.Empty<int>(), exited);
            }
        }

        /// <summary>
        /// 更新 2D 矩形网格相关性（如九宫格物品/建筑/结构），并计算进出差异
        /// </summary>
        public SpatialRelevanceDiff UpdateGrid2D(
            ulong observerId,
            ulong connectionToken,
            byte centerX,
            byte centerY,
            byte radius,
            byte maxDimension)
        {
            HashSet<int> target = CalculateGrid2D(centerX, centerY, radius, maxDimension);
            return ApplyTargetRegions(observerId, connectionToken, target);
        }

        /// <summary>
        /// 更新单一 Bounds 索引（如僵尸城镇导航 Bounds），并计算进出差异
        /// </summary>
        public SpatialRelevanceDiff UpdateBound1D(
            ulong observerId,
            ulong connectionToken,
            byte bound)
        {
            HashSet<int> target = new HashSet<int>();
            if (bound != byte.MaxValue)
            {
                target.Add(bound);
            }
            return ApplyTargetRegions(observerId, connectionToken, target);
        }

        /// <summary>
        /// 获取观察者当前占用的全部空间区域集合
        /// </summary>
        public HashSet<int> GetActiveRegions(ulong observerId)
        {
            lock (_sync)
            {
                if (_observers.TryGetValue(observerId, out ObserverSpatialState state))
                {
                    return new HashSet<int>(state.ActiveRegions);
                }
                return new HashSet<int>();
            }
        }

        private SpatialRelevanceDiff ApplyTargetRegions(
            ulong observerId,
            ulong connectionToken,
            HashSet<int> targetRegions)
        {
            lock (_sync)
            {
                List<int> entered = new List<int>();
                List<int> exited = new List<int>();

                if (!_observers.TryGetValue(observerId, out ObserverSpatialState state))
                {
                    state = new ObserverSpatialState
                    {
                        ConnectionToken = connectionToken
                    };
                    _observers[observerId] = state;
                    foreach (int r in targetRegions)
                    {
                        entered.Add(r);
                        state.ActiveRegions.Add(r);
                    }
                    return new SpatialRelevanceDiff(observerId, connectionToken, entered.ToArray(), Array.Empty<int>());
                }

                if (state.ConnectionToken != connectionToken)
                {
                    // Token changed: all old active regions are exited!
                    foreach (int r in state.ActiveRegions)
                    {
                        exited.Add(r);
                    }
                    state.ActiveRegions.Clear();
                    state.ConnectionToken = connectionToken;

                    // all target regions are newly entered for this connection token
                    foreach (int r in targetRegions)
                    {
                        entered.Add(r);
                        state.ActiveRegions.Add(r);
                    }

                    return new SpatialRelevanceDiff(observerId, connectionToken, entered.ToArray(), exited.ToArray());
                }

                // Token same: normal diff
                foreach (int r in targetRegions)
                {
                    if (!state.ActiveRegions.Contains(r))
                    {
                        entered.Add(r);
                    }
                }

                foreach (int r in state.ActiveRegions)
                {
                    if (!targetRegions.Contains(r))
                    {
                        exited.Add(r);
                    }
                }

                state.ActiveRegions.Clear();
                foreach (int r in targetRegions)
                {
                    state.ActiveRegions.Add(r);
                }

                return new SpatialRelevanceDiff(
                    observerId,
                    connectionToken,
                    entered.ToArray(),
                    exited.ToArray());
            }
        }

        public static HashSet<int> CalculateGrid2D(
            byte centerX,
            byte centerY,
            byte radius,
            byte maxDimension)
        {
            var result = new HashSet<int>();
            int minX = Math.Max(0, centerX - radius);
            int maxX = Math.Min(maxDimension - 1, centerX + radius);
            int minY = Math.Max(0, centerY - radius);
            int maxY = Math.Min(maxDimension - 1, centerY + radius);

            for (int x = minX; x <= maxX; x++)
            {
                for (int y = minY; y <= maxY; y++)
                {
                    int key = (x << 8) | y;
                    result.Add(key);
                }
            }

            return result;
        }
    }
}
