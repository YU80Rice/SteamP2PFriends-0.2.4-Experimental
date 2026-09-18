using System;
using System.Collections.Generic;
using SteamP2PFriends.Core.Identity;
using SteamP2PFriends.MultiObserver.Spatial;

namespace SteamP2PFriends.MultiObserver.Demand
{
    /// <summary>
    /// 共享 Demand Projection Engine：按各领域声明的 Demand Policy，从唯一的
    /// Observer Spatial Authority 事实算出 typed Domain Demand。
    ///
    /// 它执行区域枚举、世界边界裁剪、去重、计数与进入/退出差异；它不改原生状态、
    /// 不拥有领域执行、不持有观察者位置。每个领域一份投影状态（Domain Demand Projection
    /// State），但共同消费同一份空间事实——不存在第二个观察者索引。
    /// </summary>
    public sealed class DemandProjectionEngine
    {
        private readonly ObserverSpatialAuthority _authority;
        private readonly Dictionary<DomainId, DomainDemandProjectionState> _domains =
            new Dictionary<DomainId, DomainDemandProjectionState>();

        public DemandProjectionEngine(ObserverSpatialAuthority authority)
        {
            _authority = authority ?? throw new ArgumentNullException(nameof(authority));
        }

        /// <summary>唯一的 World Presence Observer 空间事实来源。</summary>
        public ObserverSpatialAuthority Authority => _authority;

        /// <summary>注册领域 Demand Policy。同一 Domain Id 只能注册一条政策实例。</summary>
        public void Register(DemandPolicy policy)
        {
            if (policy == null) throw new ArgumentNullException(nameof(policy));
            if (_domains.ContainsKey(policy.Domain))
                throw new InvalidOperationException(
                    "同一 Domain Id 只能注册一条 Demand Policy：" + policy.Domain);
            _domains.Add(policy.Domain, new DomainDemandProjectionState(policy));
        }

        /// <summary>
        /// 会话边界：清空唯一空间事实与全部领域投影状态。旧 Session 的观察者事实、
        /// 需求计数与投影区域必须在此清干净，新 Session 从空状态重建。
        /// 会话轴是全局的（Session Epoch 只有一个），因此这是引擎级操作而不是按域操作；
        /// 重复调用幂等。
        /// </summary>
        public void EndSession()
        {
            _authority.Clear();
            foreach (DomainDemandProjectionState state in _domains.Values) state.Clear();
        }

        /// <summary>该 Domain Id 是否已有注册的 Demand Policy。</summary>
        public bool IsRegistered(DomainId domain) => _domains.ContainsKey(domain);

        /// <summary>提交观察者事实并返回该领域的投影差异。</summary>
        public DomainDemandProjection Observe(DemandPolicy policy, ObserverPresence presence)
        {
            DomainDemandProjectionState state = EnsureRegistered(policy);
            _authority.Observe(presence);

            ObserverPresence canonical;
            if (!_authority.TryGet(presence.ObserverId, out canonical))
            {
                // 事实不存在＝该观察者不贡献需求：按退出处理，不做破坏性推断。
                SpatialRelevanceDiff removedState = state.RemoveObserver(presence.ObserverId);
                return Build(policy, presence.ObserverId, removedState.ConnectionToken, removedState);
            }

            SpatialRelevanceDiff diff = state.Apply(canonical);
            return Build(policy, canonical.ObserverId, canonical.ConnectionToken, diff);
        }

        /// <summary>移除观察者事实并返回该领域剩余的退出差异。</summary>
        public DomainDemandProjection RemoveObserver(DemandPolicy policy, ulong observerId)
        {
            DomainDemandProjectionState state = EnsureRegistered(policy);
            _authority.Remove(observerId);
            SpatialRelevanceDiff diff = state.RemoveObserver(observerId);
            return Build(policy, observerId, diff.ConnectionToken, diff);
        }

        /// <summary>
        /// 当前该区域是否已有该领域的 typed demand（计数大于零）。无需求时返回 false，
        /// 不以「零需求对象」代替不存在。
        /// </summary>
        public bool TryGetDemand(DemandPolicy policy, RegionKey regionKey, out DomainDemand demand)
        {
            DomainDemandProjectionState state = EnsureRegistered(policy);
            int observerCount = state.CountObserversInRegion(regionKey);
            if (observerCount == 0)
            {
                demand = default;
                return false;
            }

            demand = new DomainDemand(policy.Domain, regionKey, observerCount);
            return true;
        }

        /// <summary>当前存在需求的区域数（计数大于零的区域）。</summary>
        public int GetDemandRegionCount(DemandPolicy policy)
        {
            return EnsureRegistered(policy).DemandRegionCount;
        }

        /// <summary>该观察者在该领域当前被投影的区域集合（补偿/回滚用，不暴露为第二权威）。</summary>
        public HashSet<RegionKey> GetActiveRegions(DemandPolicy policy, ulong observerId)
        {
            return EnsureRegistered(policy).GetActiveRegions(observerId);
        }

        /// <summary>补偿路径：把该观察者的领域投影区域集合还原到指定快照。</summary>
        public void RestoreObserverRegions(
            DemandPolicy policy, ulong observerId, ulong connectionToken, IEnumerable<RegionKey> regions)
        {
            if (regions == null) throw new ArgumentNullException(nameof(regions));
            EnsureRegistered(policy).RestoreRegions(observerId, connectionToken, regions);
        }

        private DomainDemandProjectionState EnsureRegistered(DemandPolicy policy)
        {
            if (policy == null) throw new ArgumentNullException(nameof(policy));
            DomainDemandProjectionState state;
            if (!_domains.TryGetValue(policy.Domain, out state)
                || !ReferenceEquals(state.Policy, policy))
            {
                throw new InvalidOperationException(
                    "Demand Policy 未注册或与已注册实例不一致：" + policy.Domain);
            }
            return state;
        }

        private static DomainDemandProjection Build(
            DemandPolicy policy, ulong observerId, ulong connectionToken, SpatialRelevanceDiff diff)
        {
            return new DomainDemandProjection(
                policy.Domain, observerId, connectionToken, diff.EnteredRegions, diff.ExitedRegions);
        }

        /// <summary>
        /// 单个领域的 Domain Demand Projection State：该领域按自己的政策、从共享空间事实
        /// 派生出的观察者区域集合与需求计数。它不持有观察者位置，也不复制 Observer Spatial
        /// Authority——第二个领域增加的是它自己的一份投影状态，不是第二份空间事实。
        /// </summary>
        private sealed class DomainDemandProjectionState
        {
            private readonly SpatialObserverIndex _regions = new SpatialObserverIndex();

            internal DomainDemandProjectionState(DemandPolicy policy)
            {
                Policy = policy;
            }

            internal DemandPolicy Policy { get; }

            /// <summary>
            /// 用领域政策把该观察者事实投影为区域集合：资格由政策判定（不合格视为不贡献
            /// 需求，按退出处理），区域枚举与边界裁剪由空间索引的切比雪夫投影执行。
            /// </summary>
            internal SpatialRelevanceDiff Apply(ObserverPresence presence)
            {
                if (!Policy.IsEligible(presence))
                {
                    // 资格不合格不等于确认离开：按 Deferred Observer Demand 处理——保留既有贡献、
                    // 不发出退出差异。清理只由确认离开（RemoveObserver）、代次失效、会话重置
                    // （EndSession）或有界恢复策略触发；有界恢复属 Control-Plane Readiness Gate
                    // （票 04），不在本票。「无法证明离开」时宁可不关，不据此做破坏性释放。
                    if (_regions.TryGetConnectionToken(presence.ObserverId, out ulong storedToken)
                        && storedToken != presence.ConnectionToken)
                    {
                        // 连接代次已变（重连）：代次失效是允许的清理触发，旧代次贡献不得继续保留。
                        return _regions.RemoveObserver(presence.ObserverId);
                    }

                    return new SpatialRelevanceDiff(presence.ObserverId, presence.ConnectionToken,
                        Array.Empty<RegionKey>(), Array.Empty<RegionKey>());
                }

                return _regions.UpdateGrid2D(
                    presence.ObserverId,
                    presence.ConnectionToken,
                    presence.Center.X,
                    presence.Center.Y,
                    Policy.Radius,
                    Policy.WorldSize);
            }

            internal SpatialRelevanceDiff RemoveObserver(ulong observerId)
            {
                return _regions.RemoveObserver(observerId);
            }

            internal HashSet<RegionKey> GetActiveRegions(ulong observerId)
            {
                return _regions.GetActiveRegions(observerId);
            }

            internal void RestoreRegions(
                ulong observerId, ulong connectionToken, IEnumerable<RegionKey> regions)
            {
                _regions.RestoreRegions(observerId, connectionToken, regions);
            }

            internal void Clear()
            {
                _regions.Clear();
            }

            internal int CountObserversInRegion(RegionKey regionKey)
            {
                return _regions.CountObserversInRegion(regionKey);
            }

            internal int DemandRegionCount => _regions.CountObserversPerRegion().Count;
        }
    }
}
