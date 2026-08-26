using SteamP2PFriends.MultiObserver;
using SteamP2PFriends.MultiObserver.SPI;
using SteamP2PFriends.Core.Identity;
using System;
using RegionKey = SteamP2PFriends.Core.Identity.RegionKey;

namespace SteamP2PFriends.Adapters.Animal
{
    /// <summary>
    /// 动物领域适配器统一 SPI 实现 (AnimalDomainAdapter)
    /// 封装 M5 野生动物生命周期按需激活/滞回释放与全量快照/增量同步。
    /// </summary>
    public sealed class AnimalDomainAdapter : ILifecycleDomainAdapter, IStateReplicationAdapter, IBoundStateReplicationAdapter
    {
        public DomainId DomainId => DomainIds.Animal;
        public string DisplayName => "Animal";
        public string Capability => "NativeDemand+HysteresisRelease+GenerationGuard+ReliableEnqueueBaseline";

        public void OnSessionBegin(uint sessionEpoch)
        {
            AnimalRegionLifecycleAdapter.SetRegistrationReady(true);
            AnimalRegionLifecycleAdapter.BeginSession(sessionEpoch);
            AnimalSnapshotAdapter.ResetSession(sessionEpoch);
        }

        public void OnSessionEnd()
        {
            AnimalRegionLifecycleAdapter.SetRegistrationReady(false);
            AnimalSnapshotAdapter.ResetSession(1);
        }

        public void OnAcquire(LeaseTicket ticket)
        {
            if (ticket.Valid && !ticket.BoundKey.IsNone)
            {
                AnimalRegionLifecycleAdapter.OnObserverAcquire(ticket.BoundKey);
            }
        }

        public void OnRelease(LeaseTicket ticket)
        {
            if (ticket.Valid && !ticket.BoundKey.IsNone)
            {
                AnimalRegionLifecycleAdapter.OnObserverRelease(ticket.BoundKey, 0f);
            }
        }

        public void OnTick(float deltaTime)
        {
            AnimalRegionLifecycleAdapter.Tick();
        }

        public void OnObserverDisconnect(ulong observerId, ulong connectionToken)
        {
            AnimalSnapshotAdapter.OnObserverDisconnect(observerId);
        }

        public void OnObserverEntered(ulong observerId, ulong connectionToken, RegionKey regionKey)
        {
            // Animal 状态不消费二维 Region；Bound 入口见 OnBoundObserverEntered。
        }

        public void OnObserverExited(ulong observerId, ulong connectionToken, RegionKey regionKey)
        {
            // Animal 状态不消费二维 Region。
        }

        public void OnBoundObserverEntered(ulong observerId, ulong connectionToken, BoundKey boundKey)
        {
            if (boundKey.IsNone) return;
            uint gen = AnimalRegionLifecycleAdapter.GetGeneration(boundKey);
            AnimalSnapshotAdapter.EnqueueInitialSnapshot(observerId, connectionToken, boundKey, gen);
        }

        public void OnBoundObserverExited(ulong observerId, ulong connectionToken, BoundKey boundKey)
        {
            // 退出该 Bound
        }

        public void OnReplicationTick(float deltaTime)
        {
            // 状态同步 Tick
        }

        public void ResetReplication(uint sessionEpoch)
        {
            AnimalSnapshotAdapter.ResetSession(sessionEpoch);
        }
    }
}
