using SteamP2PFriends.MultiObserver;
using SteamP2PFriends.MultiObserver.SPI;
using SteamP2PFriends.Core.Identity;
using System;
using RegionKey = SteamP2PFriends.Core.Identity.RegionKey;

namespace SteamP2PFriends.Adapters.Item
{
    /// <summary>
    /// 物品领域适配器统一 SPI 实现 (ItemDomainAdapter)
    /// 封装 M1 自然生成权威判定与 M2 快照复制/掉落增量同步。
    /// </summary>
    public sealed class ItemDomainAdapter : ILifecycleDomainAdapter, IStateReplicationAdapter
    {
        public DomainId DomainId => DomainIds.Item;
        public string DisplayName => "Item";
        public string Capability => "ItemGenerationAuthority+ItemObserverReplication";

        public void OnSessionBegin(uint sessionEpoch)
        {
            ItemObserverReplicationAdapter.SetRegistrationReady(true);
        }

        public void OnSessionEnd()
        {
            ItemObserverReplicationAdapter.SetRegistrationReady(false);
        }

        public void OnAcquire(LeaseTicket ticket)
        {
            // 权威生成激活
        }

        public void OnRelease(LeaseTicket ticket)
        {
            // 物品区域释放
        }

        public void OnTick(float deltaTime)
        {
            // 帧驱动
        }

        public void OnObserverDisconnect(ulong observerId, ulong connectionToken)
        {
            // 观察者断线
        }

        public void OnObserverEntered(ulong observerId, ulong connectionToken, RegionKey regionKey)
        {
            // 空间观察者进入该 Region
        }

        public void OnObserverExited(ulong observerId, ulong connectionToken, RegionKey regionKey)
        {
            // 空间观察者离开该 Region
        }

        public void OnReplicationTick(float deltaTime)
        {
            // 增量状态下发
        }

        public void ResetReplication(uint sessionEpoch)
        {
            ItemObserverReplicationAdapter.SetRegistrationReady(true);
        }
    }
}
