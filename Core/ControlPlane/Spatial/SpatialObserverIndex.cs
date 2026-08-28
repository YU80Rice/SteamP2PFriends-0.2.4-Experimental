using System;
using System.Collections.Generic;
using SteamP2PFriends.Core.Identity;

namespace SteamP2PFriends.MultiObserver.Spatial
{
    /// <summary>
    /// 空间差异事件。二维 Region 与一维导航 Bound 使用独立集合，避免拓扑身份混用。
    /// </summary>
    public readonly struct SpatialRelevanceDiff
    {
        public readonly ulong ObserverId;
        public readonly ulong ConnectionToken;
        public readonly RegionKey[] EnteredRegions;
        public readonly RegionKey[] ExitedRegions;
        public readonly BoundKey[] EnteredBounds;
        public readonly BoundKey[] ExitedBounds;
        public readonly bool HasChanges;

        public SpatialRelevanceDiff(
            ulong observerId, ulong connectionToken,
            RegionKey[] enteredRegions, RegionKey[] exitedRegions,
            BoundKey[] enteredBounds = null, BoundKey[] exitedBounds = null)
        {
            ObserverId = observerId;
            ConnectionToken = connectionToken;
            EnteredRegions = enteredRegions ?? Array.Empty<RegionKey>();
            ExitedRegions = exitedRegions ?? Array.Empty<RegionKey>();
            EnteredBounds = enteredBounds ?? Array.Empty<BoundKey>();
            ExitedBounds = exitedBounds ?? Array.Empty<BoundKey>();
            HasChanges = EnteredRegions.Length > 0 || ExitedRegions.Length > 0
                || EnteredBounds.Length > 0 || ExitedBounds.Length > 0;
        }
    }

    /// <summary>
    /// 纯内存空间观察者索引深模块：统一身份编码、连接代次失效和差异计算。
    /// </summary>
    public sealed class SpatialObserverIndex
    {
        private sealed class ObserverSpatialState
        {
            public ulong ConnectionToken;
            public readonly HashSet<RegionKey> ActiveRegions = new HashSet<RegionKey>();
            public readonly HashSet<BoundKey> ActiveBounds = new HashSet<BoundKey>();
        }

        private readonly object _sync = new object();
        private readonly Dictionary<ulong, ObserverSpatialState> _observers =
            new Dictionary<ulong, ObserverSpatialState>();

        public void Clear()
        {
            lock (_sync) _observers.Clear();
        }

        public SpatialRelevanceDiff RemoveObserver(ulong observerId)
        {
            lock (_sync)
            {
                if (!_observers.TryGetValue(observerId, out ObserverSpatialState state))
                    return new SpatialRelevanceDiff(observerId, 0UL,
                        Array.Empty<RegionKey>(), Array.Empty<RegionKey>());

                RegionKey[] regions = new RegionKey[state.ActiveRegions.Count];
                state.ActiveRegions.CopyTo(regions);
                BoundKey[] bounds = new BoundKey[state.ActiveBounds.Count];
                state.ActiveBounds.CopyTo(bounds);
                _observers.Remove(observerId);
                return new SpatialRelevanceDiff(observerId, state.ConnectionToken,
                    Array.Empty<RegionKey>(), regions,
                    Array.Empty<BoundKey>(), bounds);
            }
        }

        public SpatialRelevanceDiff UpdateGrid2D(
            ulong observerId, ulong connectionToken,
            byte centerX, byte centerY, byte radius, byte maxDimension)
        {
            return ApplyTargetRegions(observerId, connectionToken,
                CalculateGrid2D(centerX, centerY, radius, maxDimension));
        }

        public SpatialRelevanceDiff UpdateBound1D(
            ulong observerId, ulong connectionToken, byte bound)
        {
            var target = new HashSet<BoundKey>();
            BoundKey key = BoundKey.FromNative(bound);
            if (!key.IsNone) target.Add(key);
            return ApplyTargetBounds(observerId, connectionToken, target);
        }

        public HashSet<RegionKey> GetActiveRegions(ulong observerId)
        {
            lock (_sync)
            {
                return _observers.TryGetValue(observerId, out ObserverSpatialState state)
                    ? new HashSet<RegionKey>(state.ActiveRegions)
                    : new HashSet<RegionKey>();
            }
        }

        public void RestoreRegions(ulong observerId, ulong connectionToken, IEnumerable<RegionKey> regions)
        {
            if (regions == null) throw new ArgumentNullException(nameof(regions));
            lock (_sync)
            {
                var state = new ObserverSpatialState { ConnectionToken = connectionToken };
                foreach (RegionKey region in regions) state.ActiveRegions.Add(region);
                _observers[observerId] = state;
            }
        }

        public HashSet<BoundKey> GetActiveBounds(ulong observerId)
        {
            lock (_sync)
            {
                return _observers.TryGetValue(observerId, out ObserverSpatialState state)
                    ? new HashSet<BoundKey>(state.ActiveBounds)
                    : new HashSet<BoundKey>();
            }
        }

        public static HashSet<RegionKey> CalculateGrid2D(
            byte centerX, byte centerY, byte radius, byte maxDimension)
        {
            if (maxDimension == 0) throw new ArgumentOutOfRangeException(nameof(maxDimension));
            var result = new HashSet<RegionKey>();
            int minX = Math.Max(0, centerX - radius);
            int maxX = Math.Min(maxDimension - 1, centerX + radius);
            int minY = Math.Max(0, centerY - radius);
            int maxY = Math.Min(maxDimension - 1, centerY + radius);
            for (int x = minX; x <= maxX; x++)
            for (int y = minY; y <= maxY; y++)
                result.Add(new RegionKey((byte)x, (byte)y));
            return result;
        }

        private SpatialRelevanceDiff ApplyTargetRegions(
            ulong observerId, ulong connectionToken, HashSet<RegionKey> target)
        {
            lock (_sync)
            {
                ObserverSpatialState state = GetOrCreate(observerId, connectionToken,
                    out bool connectionChanged);
                var entered = new List<RegionKey>();
                var exited = new List<RegionKey>();
                var exitedBounds = new List<BoundKey>();
                if (connectionChanged)
                {
                    exited.AddRange(state.ActiveRegions);
                    exitedBounds.AddRange(state.ActiveBounds);
                    state.ActiveRegions.Clear();
                    state.ActiveBounds.Clear();
                }
                foreach (RegionKey region in target)
                    if (state.ActiveRegions.Add(region)) entered.Add(region);
                if (!connectionChanged)
                {
                    foreach (RegionKey region in new List<RegionKey>(state.ActiveRegions))
                        if (!target.Contains(region))
                        {
                            state.ActiveRegions.Remove(region);
                            exited.Add(region);
                        }
                }
                return new SpatialRelevanceDiff(observerId, connectionToken,
                    entered.ToArray(), exited.ToArray(),
                    Array.Empty<BoundKey>(), exitedBounds.ToArray());
            }
        }

        private SpatialRelevanceDiff ApplyTargetBounds(
            ulong observerId, ulong connectionToken, HashSet<BoundKey> target)
        {
            lock (_sync)
            {
                ObserverSpatialState state = GetOrCreate(observerId, connectionToken,
                    out bool connectionChanged);
                var entered = new List<BoundKey>();
                var exited = new List<BoundKey>();
                var exitedRegions = new List<RegionKey>();
                if (connectionChanged)
                {
                    exitedRegions.AddRange(state.ActiveRegions);
                    exited.AddRange(state.ActiveBounds);
                    state.ActiveRegions.Clear();
                    state.ActiveBounds.Clear();
                }
                foreach (BoundKey bound in target)
                    if (state.ActiveBounds.Add(bound)) entered.Add(bound);
                if (!connectionChanged)
                {
                    foreach (BoundKey bound in new List<BoundKey>(state.ActiveBounds))
                        if (!target.Contains(bound))
                        {
                            state.ActiveBounds.Remove(bound);
                            exited.Add(bound);
                        }
                }
                return new SpatialRelevanceDiff(observerId, connectionToken,
                    Array.Empty<RegionKey>(), exitedRegions.ToArray(),
                    entered.ToArray(), exited.ToArray());
            }
        }

        private ObserverSpatialState GetOrCreate(
            ulong observerId, ulong connectionToken, out bool connectionChanged)
        {
            if (!_observers.TryGetValue(observerId, out ObserverSpatialState state))
            {
                state = new ObserverSpatialState { ConnectionToken = connectionToken };
                _observers[observerId] = state;
                connectionChanged = false;
                return state;
            }
            connectionChanged = state.ConnectionToken != connectionToken;
            if (connectionChanged)
            {
                state.ConnectionToken = connectionToken;
            }
            return state;
        }
    }
}
