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
    public sealed class ResourceDomainAdapter : ILifecycleDomainAdapter, IStateReplicationAdapter
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
            ResourceObservability.Info("[Host]", "SessionEnd", "-",
                ResourceRegionLifecycleAdapter.CurrentSessionEpoch, 0UL, 0U, "SPI", true, "success");
            ResourceRegionLifecycleAdapter.EndSession();
            ResourceRegionLifecycleAdapter.SetRegistrationReady(false);
            ResourceSnapshotAdapter.ResetSession(ResourceRegionLifecycleAdapter.CurrentSessionEpoch);
            ResourceObservability.ResetSession();
        }

        public void OnAcquire(LeaseTicket ticket)
        {
            if (!ticket.Valid)
            {
                ResourceObservability.Warn("[Host]", "LeaseAcquire", ticket.RegionKey.ToString(),
                    ticket.SessionEpoch.Value, 0UL, ticket.RegionGeneration.Value, "Fallback", false,
                    "rejected", "reason=invalid-ticket");
                return;
            }

            try
            {
                uint generation = ResourceRegionLifecycleAdapter.OnObserverAcquire(ticket.RegionKey);
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
                return;
            }

            try
            {
                bool committed = ResourceRegionLifecycleAdapter.CommitRelease(
                    ticket.RegionKey,
                    ticket.SessionEpoch.Value,
                    ticket.RegionGeneration.Value,
                    out _);
                ResourceObservability.Info("[Host]", "LeaseRelease", ticket.RegionKey.ToString(),
                    ticket.SessionEpoch.Value, 0UL, ticket.RegionGeneration.Value, "SPI", true,
                    committed ? "success" : "rejected", "committed=" + committed);
                if (!committed)
                    throw new InvalidOperationException("Resource release rejected by generation or session gate.");
            }
            catch (Exception ex)
            {
                ResourceObservability.Error("[Host]", "LeaseRelease", ticket.RegionKey.ToString(),
                    ticket.SessionEpoch.Value, 0UL, ticket.RegionGeneration.Value, "Fallback", true,
                    "failed", "exception=" + ex.GetType().Name);
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
                "SPI", true, removed ? "success" : "rejected", "observer=" + observerId);
        }

        public void OnObserverEntered(ulong observerId, ulong connectionToken, RegionKey regionKey)
        {
            uint gen = ResourceRegionLifecycleAdapter.GetGeneration(regionKey);
            bool queued = ResourceSnapshotAdapter.EnqueueInitialSnapshot(observerId, connectionToken, regionKey, gen);
            ResourceObservability.Info("[Host]", "SnapshotEnqueue", regionKey.ToString(),
                ResourceRegionLifecycleAdapter.CurrentSessionEpoch, connectionToken, gen, "SPI", true,
                queued ? "success" : "rejected", "observer=" + observerId);
        }

        public void OnObserverExited(ulong observerId, ulong connectionToken, RegionKey regionKey)
        {
            bool removed = ResourceSnapshotAdapter.RemoveSnapshot(observerId, connectionToken, regionKey);
            ResourceObservability.Info("[Host]", "SnapshotRemove", regionKey.ToString(),
                ResourceRegionLifecycleAdapter.CurrentSessionEpoch, connectionToken,
                ResourceRegionLifecycleAdapter.GetGeneration(regionKey), "SPI", true,
                removed ? "success" : "rejected", "observer=" + observerId);
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
    }
}
