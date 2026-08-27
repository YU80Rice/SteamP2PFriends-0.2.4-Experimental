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
            ResourceRegionLifecycleAdapter.SetRegistrationReady(true);
            ResourceRegionLifecycleAdapter.BeginSession(sessionEpoch);
            ResourceSnapshotAdapter.ResetSession(sessionEpoch);
            RoleLogger.Info("[Host]", $"[ResourceSPI] event=SessionBegin sessionEpoch={sessionEpoch}");
        }

        public void OnSessionEnd()
        {
            RoleLogger.Info("[Host]",
                $"[ResourceSPI] event=SessionEnd sessionEpoch={ResourceRegionLifecycleAdapter.CurrentSessionEpoch}");
            ResourceRegionLifecycleAdapter.EndSession();
            ResourceRegionLifecycleAdapter.SetRegistrationReady(false);
            ResourceSnapshotAdapter.ResetSession(ResourceRegionLifecycleAdapter.CurrentSessionEpoch);
        }

        public void OnAcquire(LeaseTicket ticket)
        {
            if (ticket.Valid)
            {
                uint generation = ResourceRegionLifecycleAdapter.OnObserverAcquire(ticket.RegionKey);
                RoleLogger.Info("[Host]",
                    $"[ResourceSPI] event=LeaseAcquire leaseAuthority=ResourceProductionControlSeam " +
                    $"region={ticket.RegionKey} sessionEpoch={ticket.SessionEpoch.Value} " +
                    $"regionGeneration={generation} demand={ticket.ActiveDemandCount}");
            }
        }

        public void OnRelease(LeaseTicket ticket)
        {
            if (ticket.Valid)
            {
                bool committed = ResourceRegionLifecycleAdapter.CommitRelease(
                    ticket.RegionKey,
                    ticket.SessionEpoch.Value,
                    ticket.RegionGeneration.Value,
                    out _);
                RoleLogger.Info("[Host]",
                    $"[ResourceSPI] event=LeaseRelease leaseAuthority=ResourceProductionControlSeam " +
                    $"region={ticket.RegionKey} sessionEpoch={ticket.SessionEpoch.Value} " +
                    $"regionGeneration={ticket.RegionGeneration.Value} committed={committed}");
            }
        }

        public void OnTick(float deltaTime)
        {
            ResourceRegionLifecycleAdapter.Tick();
        }

        public void OnObserverDisconnect(ulong observerId, ulong connectionToken)
        {
            bool removed = ResourceSnapshotAdapter.OnObserverDisconnect(observerId, connectionToken);
            RoleLogger.Info("[Host]",
                $"[ResourceSPI] event=ObserverDisconnect observer={observerId} " +
                $"connectionToken={connectionToken} removed={removed}");
        }

        public void OnObserverEntered(ulong observerId, ulong connectionToken, RegionKey regionKey)
        {
            uint gen = ResourceRegionLifecycleAdapter.GetGeneration(regionKey);
            bool queued = ResourceSnapshotAdapter.EnqueueInitialSnapshot(observerId, connectionToken, regionKey, gen);
            RoleLogger.Info("[Host]",
                $"[ResourceSPI] event=SnapshotEnqueue observer={observerId} connectionToken={connectionToken} " +
                $"region={regionKey} regionGeneration={gen} queued={queued}");
        }

        public void OnObserverExited(ulong observerId, ulong connectionToken, RegionKey regionKey)
        {
            bool removed = ResourceSnapshotAdapter.RemoveSnapshot(observerId, connectionToken, regionKey);
            RoleLogger.Info("[Host]",
                $"[ResourceSPI] event=SnapshotRemove observer={observerId} connectionToken={connectionToken} " +
                $"region={regionKey} removed={removed}");
        }

        public void OnReplicationTick(float deltaTime)
        {
            ResourceSnapshotAdapter.OnReplicationTick(deltaTime);
        }

        public void ResetReplication(uint sessionEpoch)
        {
            ResourceSnapshotAdapter.ResetSession(sessionEpoch);
        }
    }
}
