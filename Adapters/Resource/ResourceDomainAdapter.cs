using SteamP2PFriends.MultiObserver;
using SteamP2PFriends.MultiObserver.SPI;
using SteamP2PFriends.Core.Identity;
using SteamP2PFriends.Shared;
using System;
using RegionKey = SteamP2PFriends.Core.Identity.RegionKey;

namespace SteamP2PFriends.Adapters.Resource
{
    /// <summary>
    /// 资源领域适配器统一 SPI 实现 (ResourceDomainAdapter)
    /// 统一管理树木、矿石物理碰撞与采伐状态同步。
    /// </summary>
    public sealed class ResourceDomainAdapter : ILifecycleDomainAdapter, IStateReplicationAdapter,
        IReversibleObserverDisconnectAdapter, IReversibleRegionLifecycleAdapter,
        IReversibleObserverReplicationAdapter
    {
        public DomainId DomainId => DomainIds.Resource;
        public string DisplayName => "Resource";
        public string Capability => "TreeOreCollision+HarvestReplication+HysteresisRelease";

        public void OnSessionBegin(uint sessionEpoch)
        {
            ResourceObservability.ResetSession();
            ResourceRegionLifecycleAdapter.SetRegistrationReady(true);
            ResourceRegionLifecycleAdapter.BeginSession(sessionEpoch);
            ResourceSnapshotAdapter.ResetSession(sessionEpoch);
            ResourceObservability.Info("[Host]", "SessionBegin", "-", sessionEpoch, 0UL, 0U,
                "SPI", true, "success", "authority=ResourceProductionControlSeam");
        }

        public void OnSessionEnd()
        {
            ulong sessionEpoch = ResourceRegionLifecycleAdapter.CurrentSessionEpoch;
            Exception cleanupFailure = null;
            try
            {
                ResourceRegionLifecycleAdapter.EndSession();
            }
            catch (Exception ex)
            {
                cleanupFailure = ex;
                ResourceObservability.Error("[Host]", "SessionEnd", "-", sessionEpoch, 0UL, 0U,
                    "Fallback", true, "failed", "reason=resource-lifecycle-end-failed exception=" + ex.GetType().Name);
            }

            try
            {
                ResourceRegionLifecycleAdapter.SetRegistrationReady(false);
            }
            catch (Exception ex)
            {
                if (cleanupFailure == null) cleanupFailure = ex;
                ResourceObservability.Error("[Host]", "SessionEnd", "-", sessionEpoch, 0UL, 0U,
                    "Fallback", true, "failed", "reason=registration-close-failed exception=" + ex.GetType().Name);
            }

            try
            {
                ResourceSnapshotAdapter.ResetSession(sessionEpoch);
            }
            catch (Exception ex)
            {
                if (cleanupFailure == null) cleanupFailure = ex;
                ResourceObservability.Error("[Host]", "SessionEnd", "-", sessionEpoch, 0UL, 0U,
                    "Fallback", true, "failed", "reason=snapshot-reset-failed exception=" + ex.GetType().Name);
            }

            if (cleanupFailure == null)
            {
                ResourceObservability.Info("[Host]", "SessionEnd", "-", sessionEpoch, 0UL, 0U,
                    "SPI", true, "success", "cleanup=complete");
            }
            ResourceObservability.ResetSession();
            if (cleanupFailure != null) throw cleanupFailure;
        }

        public void OnAcquire(LeaseTicket ticket)
        {
            if (!ticket.Valid)
            {
                ResourceObservability.Warn("[Host]", "LeaseAcquire", ticket.RegionKey.ToString(),
                    ticket.SessionEpoch.Value, 0UL, ticket.RegionGeneration.Value, "Fallback", false,
                    "rejected", "reason=invalid-ticket");
                throw new InvalidOperationException("Resource acquire ticket is invalid.");
            }

            try
            {
                if (!ResourceRegionLifecycleAdapter.TryCommitAcquire(
                    ticket.RegionKey,
                    ticket.SessionEpoch.Value,
                    ticket.RegionGeneration.Value,
                    out uint generation,
                    out string rejectionReason))
                {
                    ResourceObservability.Warn("[Host]", "LeaseAcquire", ticket.RegionKey.ToString(),
                        ticket.SessionEpoch.Value, 0UL, ticket.RegionGeneration.Value, "Fallback", true,
                        "rejected", "reason=" + rejectionReason);
                    throw new ResourceAcquireRejectedException(rejectionReason);
                }
                ResourceObservability.Info("[Host]", "LeaseAcquire", ticket.RegionKey.ToString(),
                    ticket.SessionEpoch.Value, 0UL, generation, "SPI", true, "success",
                    "demand=" + ticket.ActiveDemandCount);
            }
            catch (Exception ex)
            {
                ResourceObservability.Error("[Host]", "LeaseAcquire", ticket.RegionKey.ToString(),
                    ticket.SessionEpoch.Value, 0UL, ticket.RegionGeneration.Value, "Fallback", true,
                    "failed", "exception=" + ex.GetType().Name);
                throw;
            }
        }

        public void OnRelease(LeaseTicket ticket)
        {
            if (!ticket.Valid)
            {
                ResourceObservability.Warn("[Host]", "LeaseRelease", ticket.RegionKey.ToString(),
                    ticket.SessionEpoch.Value, 0UL, ticket.RegionGeneration.Value, "Fallback", false,
                    "rejected", "reason=invalid-ticket");
                throw new ResourceReleaseRejectedException("invalid-ticket");
            }

            try
            {
                bool committed = ResourceRegionLifecycleAdapter.CommitRelease(
                    ticket.RegionKey,
                    ticket.SessionEpoch.Value,
                    ticket.RegionGeneration.Value,
                    out _,
                    out string rejectionReason);
                ResourceObservability.Info("[Host]", "LeaseRelease", ticket.RegionKey.ToString(),
                    ticket.SessionEpoch.Value, 0UL, ticket.RegionGeneration.Value, "SPI", true,
                    committed ? "success" : "rejected", "committed=" + committed +
                    " reason=" + rejectionReason);
                if (!committed)
                    throw new ResourceReleaseRejectedException(rejectionReason);
            }
            catch (Exception ex)
            {
                ResourceObservability.Error("[Host]", "LeaseRelease", ticket.RegionKey.ToString(),
                    ticket.SessionEpoch.Value, 0UL, ticket.RegionGeneration.Value, "Fallback", true,
                    "failed", "reason=" + (ex is ResourceReleaseRejectedException rejected
                        ? rejected.Reason : "external-release-failed") +
                    " exception=" + ex.GetType().Name);
                throw;
            }
        }

        public void OnTick(float deltaTime)
        {
            ResourceRegionLifecycleAdapter.Tick();
            ResourceObservability.Info("[Host]", "LifecycleTick", "-", 0UL, 0UL, 0U,
                "SPI", true, "success");
        }

        public void OnObserverDisconnect(ulong observerId, ulong connectionToken)
        {
            bool removed = ResourceSnapshotAdapter.OnObserverDisconnect(observerId, connectionToken);
            ResourceObservability.Info("[Host]", "ObserverDisconnect", "-", 0UL, connectionToken, 0U,
                "SPI", true, removed ? "success" : "rejected", "observer=" + observerId +
                " reason=" + (removed ? "none" : "stale-or-already-cleared"));
        }

        public void OnObserverEntered(ulong observerId, ulong connectionToken, RegionKey regionKey)
        {
            uint gen = ResourceRegionLifecycleAdapter.GetGeneration(regionKey);
            bool queued = ResourceSnapshotAdapter.EnqueueInitialSnapshot(observerId, connectionToken, regionKey, gen);
            ResourceObservability.Info("[Host]", "SnapshotEnqueue", regionKey.ToString(),
                ResourceRegionLifecycleAdapter.CurrentSessionEpoch, connectionToken, gen, "SPI", true,
                queued ? "success" : "rejected", "observer=" + observerId);
            if (!queued)
                throw new InvalidOperationException("Resource snapshot enqueue rejected by generation gate.");
        }

        public void OnObserverExited(ulong observerId, ulong connectionToken, RegionKey regionKey)
        {
            bool removed = ResourceSnapshotAdapter.RemoveSnapshot(observerId, connectionToken, regionKey);
            // 过时移除容忍(R1):观察者重连后,旧 connection token 的移除被 generation gate
            // 拒绝属预期场景,只记录日志不抛出——抛出会回滚 ObserverUpdate 事务并触发 M0 会话重建。
            ResourceObservability.Info("[Host]", "SnapshotRemove", regionKey.ToString(),
                ResourceRegionLifecycleAdapter.CurrentSessionEpoch, connectionToken,
                ResourceRegionLifecycleAdapter.GetGeneration(regionKey), "SPI", true,
                removed ? "success" : "rejected", "observer=" + observerId +
                " reason=" + (removed ? "none" : "stale-removal-tolerated"));
        }

        public void OnReplicationTick(float deltaTime)
        {
            try
            {
                ResourceSnapshotAdapter.OnReplicationTick(deltaTime);
                ResourceObservability.Info("[Host]", "ReplicationTick", "-",
                    ResourceRegionLifecycleAdapter.CurrentSessionEpoch, 0UL, 0U, "SPI", true,
                    "observed", "implementation=ledger-only nativeTransport=ResourceManager");
            }
            catch (Exception ex)
            {
                ResourceObservability.Error("[Host]", "ReplicationTick", "-",
                    ResourceRegionLifecycleAdapter.CurrentSessionEpoch, 0UL, 0U, "Fallback", true,
                    "failed", "exception=" + ex.GetType().Name);
                throw;
            }
        }

        public void ResetReplication(uint sessionEpoch)
        {
            ResourceSnapshotAdapter.ResetSession(sessionEpoch);
        }

        public object CaptureObserverDisconnectState(ulong observerId, ulong connectionToken)
        {
            return ResourceSnapshotAdapter.CaptureObserverState(observerId);
        }

        public void RestoreObserverDisconnectState(
            ulong observerId,
            ulong connectionToken,
            object state)
        {
            ResourceSnapshotAdapter.RestoreObserverState(observerId, state);
        }

        public object CaptureRegionState(RegionKey regionKey)
        {
            return ResourceRegionLifecycleAdapter.CaptureRegionState(regionKey);
        }

        public void RestoreRegionState(RegionKey regionKey, object state)
        {
            ResourceRegionLifecycleAdapter.RestoreRegionState(
                regionKey, state as ResourceRegionLifecycleState
                    ?? throw new ArgumentException("Invalid Resource region lifecycle state.", nameof(state)));
        }

        public object CaptureObserverReplicationState(ulong observerId, ulong connectionToken)
        {
            return ResourceSnapshotAdapter.CaptureObserverState(observerId);
        }

        public void RestoreObserverReplicationState(
            ulong observerId,
            ulong connectionToken,
            object state)
        {
            ResourceSnapshotAdapter.RestoreObserverState(observerId, state);
        }
    }
}
