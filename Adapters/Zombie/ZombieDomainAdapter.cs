using SteamP2PFriends.MultiObserver;
using SteamP2PFriends.MultiObserver.SPI;
using System;

namespace SteamP2PFriends.Adapters.Zombie
{
    /// <summary>
    /// 僵尸领域适配器统一 SPI 实现 (ZombieDomainAdapter)
    /// 封装 M3 区域生命周期按需激活/滞回释放与 M4 全量快照/增量同步。
    /// </summary>
    public sealed class ZombieDomainAdapter : ILifecycleDomainAdapter, IStateReplicationAdapter
    {
        public string DomainName => "Zombie";
        public string Capability => "NativeDemand+HysteresisRelease+GenerationGuard+ReliableEnqueueBaseline";

        public void OnSessionBegin(uint sessionEpoch)
        {
            ZombieRegionLifecycleAdapter.SetRegistrationReady(true);
            ZombieSnapshotAdapter.ResetSession(sessionEpoch);
        }

        public void OnSessionEnd()
        {
            ZombieRegionLifecycleAdapter.SetRegistrationReady(false);
            ZombieSnapshotAdapter.ResetSession(1);
        }

        public void OnAcquire(LeaseTicket ticket)
        {
            // 权威需求激活
        }

        public void OnRelease(LeaseTicket ticket)
        {
            // 滞回释放提交
        }

        public void OnTick(float deltaTime)
        {
            ZombieRegionLifecycleAdapter.Tick();
        }

        public void OnObserverDisconnect(ulong observerId, ulong connectionToken)
        {
            // 观察者断线由全局统一处理
        }

        public void OnObserverEntered(ulong observerId, ulong connectionToken, int regionKey)
        {
            // 空间观察者进入该 Bound
        }

        public void OnObserverExited(ulong observerId, ulong connectionToken, int regionKey)
        {
            // 空间观察者离开该 Bound
        }

        public void OnReplicationTick(float deltaTime)
        {
            // 增量状态下发心跳
        }

        public void ResetReplication(uint sessionEpoch)
        {
            ZombieSnapshotAdapter.ResetSession(sessionEpoch);
        }
    }
}
