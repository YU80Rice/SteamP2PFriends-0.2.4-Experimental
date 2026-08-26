using SteamP2PFriends.MultiObserver;
using SteamP2PFriends.MultiObserver.SPI;
using SteamP2PFriends.Core.Identity;
using System;
using RegionKey = SteamP2PFriends.Core.Identity.RegionKey;

namespace SteamP2PFriends.Adapters.Structure
{
    /// <summary>
    /// 玩家建筑与防御工事领域统一 SPI 实现 (BuildingDomainAdapter)
    /// 统一管理防御工事 (Barricade) 与建筑结构 (Structure) 的生命周期与状态复制。
    /// </summary>
    public sealed class BuildingDomainAdapter : ILifecycleDomainAdapter, IStateReplicationAdapter
    {
        public DomainId DomainId => DomainIds.Building;
        public string DisplayName => "Building";
        public string Capability => "BarricadeStructureLifecycle+StateReplication+HysteresisRelease";

        public void OnSessionBegin(uint sessionEpoch)
        {
            BarricadeRegionLifecycleAdapter.SetRegistrationReady(true);
            BarricadeRegionLifecycleAdapter.BeginSession(sessionEpoch);
            BarricadeSnapshotAdapter.Reset();

            StructureRegionLifecycleAdapter.SetRegistrationReady(true);
            StructureRegionLifecycleAdapter.BeginSession(sessionEpoch);
            StructureSnapshotAdapter.Reset();
        }

        public void OnSessionEnd()
        {
            BarricadeRegionLifecycleAdapter.SetRegistrationReady(false);
            BarricadeRegionLifecycleAdapter.EndSession();
            BarricadeSnapshotAdapter.Reset();

            StructureRegionLifecycleAdapter.SetRegistrationReady(false);
            StructureRegionLifecycleAdapter.EndSession();
            StructureSnapshotAdapter.Reset();
        }

        public void OnAcquire(LeaseTicket ticket)
        {
            if (ticket.Valid)
            {
                BarricadeRegionLifecycleAdapter.OnObserverAcquire(BarricadeKey.FromRegion(ticket.RegionKey, 0));
                StructureRegionLifecycleAdapter.OnObserverAcquire(ticket.RegionKey);
            }
        }

        public void OnRelease(LeaseTicket ticket)
        {
            if (ticket.Valid)
            {
                BarricadeRegionLifecycleAdapter.OnObserverRelease(BarricadeKey.FromRegion(ticket.RegionKey, 0), 0f);
                StructureRegionLifecycleAdapter.OnObserverRelease(ticket.RegionKey, 0f);
            }
        }

        public void OnTick(float deltaTime)
        {
        }

        public void OnObserverDisconnect(ulong observerId, ulong connectionToken)
        {
            BarricadeSnapshotAdapter.RemoveObserver(observerId);
            StructureSnapshotAdapter.RemoveObserver(observerId);
        }

        public void OnObserverEntered(ulong observerId, ulong connectionToken, RegionKey regionKey)
        {
            BarricadeKey barricadeKey = BarricadeKey.FromRegion(regionKey, 0);
            uint bGen = BarricadeRegionLifecycleAdapter.GetGeneration(barricadeKey);
            BarricadeSnapshotAdapter.ShouldReplicateSnapshot(observerId, connectionToken, barricadeKey, bGen);

            uint sGen = StructureRegionLifecycleAdapter.GetGeneration(regionKey);
            StructureSnapshotAdapter.ShouldReplicateSnapshot(observerId, connectionToken, regionKey, sGen);
        }

        public void OnObserverExited(ulong observerId, ulong connectionToken, RegionKey regionKey)
        {
        }

        public void OnReplicationTick(float deltaTime)
        {
        }

        public void ResetReplication(uint sessionEpoch)
        {
            BarricadeSnapshotAdapter.Reset();
            StructureSnapshotAdapter.Reset();
        }
    }
}
