using SteamP2PFriends.MultiObserver;
using SteamP2PFriends.MultiObserver.SPI;
using System;

namespace SteamP2PFriends.Adapters.Resource
{
    /// <summary>
    /// 资源领域适配器统一 SPI 实现 (ResourceDomainAdapter)
    /// 统一管理树木、矿石物理碰撞与采伐状态同步。
    /// </summary>
    public sealed class ResourceDomainAdapter : ILifecycleDomainAdapter, IStateReplicationAdapter
    {
        public string DomainName => "Resource";
        public string Capability => "TreeOreCollision+HarvestReplication+HysteresisRelease";

        public void OnSessionBegin(uint sessionEpoch)
        {
            ResourceRegionLifecycleAdapter.SetRegistrationReady(true);
            ResourceRegionLifecycleAdapter.BeginSession(sessionEpoch);
            ResourceSnapshotAdapter.ResetSession(sessionEpoch);
        }

        public void OnSessionEnd()
        {
            ResourceRegionLifecycleAdapter.SetRegistrationReady(false);
            ResourceSnapshotAdapter.ResetSession(1);
        }

        public void OnAcquire(LeaseTicket ticket)
        {
            if (ticket.Valid)
            {
                ResourceRegionLifecycleAdapter.OnObserverAcquire(ticket.RegionKey);
            }
        }

        public void OnRelease(LeaseTicket ticket)
        {
            if (ticket.Valid)
            {
                ResourceRegionLifecycleAdapter.OnObserverRelease(ticket.RegionKey, 0f);
            }
        }

        public void OnTick(float deltaTime)
        {
            ResourceRegionLifecycleAdapter.Tick();
        }

        public void OnObserverDisconnect(ulong observerId, ulong connectionToken)
        {
            ResourceSnapshotAdapter.OnObserverDisconnect(observerId);
        }

        public void OnObserverEntered(ulong observerId, ulong connectionToken, int regionKey)
        {
            uint gen = ResourceRegionLifecycleAdapter.GetGeneration(regionKey);
            ResourceSnapshotAdapter.EnqueueInitialSnapshot(observerId, connectionToken, regionKey, gen);
        }

        public void OnObserverExited(ulong observerId, ulong connectionToken, int regionKey)
        {
            // 退出该区域
        }

        public void OnReplicationTick(float deltaTime)
        {
            // 状态同步 Tick
        }

        public void ResetReplication(uint sessionEpoch)
        {
            ResourceSnapshotAdapter.ResetSession(sessionEpoch);
        }
    }
}
