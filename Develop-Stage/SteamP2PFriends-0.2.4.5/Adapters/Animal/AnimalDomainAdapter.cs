using SteamP2PFriends.MultiObserver;
using SteamP2PFriends.MultiObserver.SPI;
using System;

namespace SteamP2PFriends.Adapters.Animal
{
    /// <summary>
    /// 动物领域适配器统一 SPI 实现 (AnimalDomainAdapter)
    /// 封装 M5 野生动物生命周期按需激活/滞回释放与全量快照/增量同步。
    /// </summary>
    public sealed class AnimalDomainAdapter : ILifecycleDomainAdapter, IStateReplicationAdapter
    {
        public string DomainName => "Animal";
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
            if (ticket.Valid)
            {
                AnimalRegionLifecycleAdapter.OnObserverAcquire((byte)ticket.RegionKey);
            }
        }

        public void OnRelease(LeaseTicket ticket)
        {
            if (ticket.Valid)
            {
                AnimalRegionLifecycleAdapter.OnObserverRelease((byte)ticket.RegionKey, 0f);
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

        public void OnObserverEntered(ulong observerId, ulong connectionToken, int regionKey)
        {
            uint gen = AnimalRegionLifecycleAdapter.GetGeneration((byte)regionKey);
            AnimalSnapshotAdapter.EnqueueInitialSnapshot(observerId, connectionToken, (byte)regionKey, gen);
        }

        public void OnObserverExited(ulong observerId, ulong connectionToken, int regionKey)
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
