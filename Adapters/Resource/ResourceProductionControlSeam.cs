using SteamP2PFriends.Core.Identity;
using SteamP2PFriends.MultiObserver.Demand;
using SteamP2PFriends.MultiObserver.Lifecycle;
using SteamP2PFriends.MultiObserver.SPI;
using SteamP2PFriends.Shared;
using System;
using System.Collections.Generic;

namespace SteamP2PFriends.Adapters.Resource
{
    /// <summary>
    /// Resource 的唯一生产控制接缝——现在只是一层薄门面。
    ///
    /// 观察者空间事实、typed Resource Demand、需求聚合、0 -&gt; 1 Acquire、N -&gt; 0 滞回 Release、
    /// 身份校验、retry、补偿调度与局部故障隔离全部归 Control Plane：接缝把原生操作接成
    /// <see cref="ResourceExecutionPort"/>，注册到共享 <see cref="LifecycleOrchestrationEngine"/>，
    /// 再把调用转发过去。它不再持有任何编排状态（区域集合、租约、滞回登记、retry 登记、
    /// 观察者集合一个都不在），也不再是第二套通用生命周期状态机。
    /// </summary>
    public sealed class ResourceProductionControlSeam
    {
        private readonly LifecycleOrchestrationEngine _engine;
        private readonly DemandProjectionEngine _projection;
        private readonly DemandPolicy _policy;

        public ResourceProductionControlSeam(
            ILifecycleDomainAdapter lifecycle,
            IStateReplicationAdapter replication,
            Func<RegionKey, uint> generationReader,
            DemandProjectionEngine projection,
            DemandPolicy resourceDemandPolicy,
            float hysteresisSeconds)
        {
            if (lifecycle == null) throw new ArgumentNullException(nameof(lifecycle));
            if (replication == null) throw new ArgumentNullException(nameof(replication));
            if (generationReader == null) throw new ArgumentNullException(nameof(generationReader));
            _projection = projection ?? throw new ArgumentNullException(nameof(projection));
            if (resourceDemandPolicy == null)
                throw new ArgumentNullException(nameof(resourceDemandPolicy));
            if (resourceDemandPolicy.Domain != DomainIds.Resource)
                throw new ArgumentException(
                    "Resource 生产接缝只消费 Resource 身份的 Demand Policy。",
                    nameof(resourceDemandPolicy));
            if (hysteresisSeconds < 0f) throw new ArgumentOutOfRangeException(nameof(hysteresisSeconds));
            _policy = resourceDemandPolicy;

            LifecyclePolicy lifecyclePolicy = ResourceLifecyclePolicy.Create(hysteresisSeconds);
            var port = new ResourceExecutionPort(
                lifecycle, replication, generationReader, lifecyclePolicy);
            _engine = new LifecycleOrchestrationEngine(projection, ResourceLifecycleDiagnostics.Instance);
            _engine.Register(_policy, port);
        }

        /// <summary>本接缝消费的 Demand Policy（声明属于 Resource 域）。</summary>
        public DemandPolicy DemandPolicy => _policy;

        /// <summary>共享投影引擎当前为该领域算出的需求区域数（诊断用，不是租约数）。</summary>
        public int ProjectedDemandRegionCount => _projection.GetDemandRegionCount(_policy);

        public bool IsSessionActive => _engine.IsSessionActive;
        public SessionEpoch SessionEpoch => _engine.SessionEpoch;
        public int ObserverCount => _engine.ObserverCount(DomainIds.Resource);
        public int ActiveLeaseCount => _engine.ActiveLeaseCount(DomainIds.Resource);
        public int PendingReleaseCount => _engine.PendingReleaseCount(DomainIds.Resource);
        public int DemandRegionCount => _engine.DemandRegionCount(DomainIds.Resource);
        public int ReentryCount => _engine.ReentryCount(DomainIds.Resource);
        public bool RepairRequired => _engine.IsRepairRequired(DomainIds.Resource);
        public int PendingAcquireRetryCount => _engine.PendingAcquireRetryCount(DomainIds.Resource);

        public bool TryGetConnectionGeneration(ulong observerId, out ulong connectionGeneration)
        {
            return _engine.TryGetConnectionGeneration(DomainIds.Resource, observerId, out connectionGeneration);
        }

        public void BeginSession(SessionEpoch sessionEpoch) => _engine.BeginSession(sessionEpoch);
        public void EndSession() => _engine.EndSession();
        public void AdvanceTime(float deltaTime) => _engine.AdvanceTime(deltaTime);
        public void Flush(float deltaTime) => _engine.Flush(deltaTime);
        public void Tick(float deltaTime) => _engine.Tick(deltaTime);

        public DomainDemandProjection UpdateObserver(
            ulong observerId,
            ulong connectionToken,
            byte centerX,
            byte centerY)
        {
            return UpdateObserver(observerId, connectionToken, centerX, centerY, gameplayAuthorized: true);
        }

        public DomainDemandProjection UpdateObserver(
            ulong observerId,
            ulong connectionToken,
            byte centerX,
            byte centerY,
            bool gameplayAuthorized)
        {
            return _engine.Observe(DomainIds.Resource, observerId, connectionToken,
                centerX, centerY, gameplayAuthorized);
        }

        public DomainDemandProjection RemoveObserver(ulong observerId)
        {
            return _engine.RemoveObserver(DomainIds.Resource, observerId);
        }

        /// <summary>
        /// 登记暂缓观察者：本拍样本不可用但无法证明离开。既有贡献原样保留，不触发破坏性释放；
        /// 持续暂缓与恢复闭环由共享引擎的有界心跳承载。
        /// </summary>
        public void DeferObserver(ulong observerId, string reason)
        {
            _engine.DeferObserver(DomainIds.Resource, observerId, reason);
        }

        /// <summary>挂起本域写入（会话身份不确定或共享面故障恢复期间）：保留租约与需求。</summary>
        public void SuspendWrites(string reason) => _engine.SuspendWrites(reason);

        public void ResumeWrites(string reason) => _engine.ResumeWrites(reason);

        public bool IsWriteSuspended => _engine.IsWriteSuspended;

        public int DeferredObserverCount => _engine.DeferredObserverCount(DomainIds.Resource);

        public int GetDemand(RegionKey regionKey) =>
            _engine.GetDemand(DomainIds.Resource, regionKey);

        public bool IsLeased(RegionKey regionKey) =>
            _engine.IsLeased(DomainIds.Resource, regionKey);

        public bool TryGetLease(RegionKey regionKey, out ResourceProductionLease lease)
        {
            RegionGeneration generation;
            int activeDemandCount;
            if (_engine.TryGetLease(DomainIds.Resource, regionKey, out generation, out activeDemandCount))
            {
                lease = new ResourceProductionLease(
                    _engine.SessionEpoch, regionKey, generation, activeDemandCount);
                return true;
            }

            lease = default;
            return false;
        }
    }

    public readonly struct ResourceProductionLease
    {
        public ResourceProductionLease(
            SessionEpoch sessionEpoch,
            RegionKey regionKey,
            RegionGeneration regionGeneration,
            int activeDemandCount)
        {
            SessionEpoch = sessionEpoch;
            RegionKey = regionKey;
            RegionGeneration = regionGeneration;
            ActiveDemandCount = activeDemandCount;
        }

        public SessionEpoch SessionEpoch { get; }
        public RegionKey RegionKey { get; }
        public RegionGeneration RegionGeneration { get; }
        public int ActiveDemandCount { get; }
    }
}
