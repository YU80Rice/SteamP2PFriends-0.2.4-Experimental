using SteamP2PFriends.Core.Identity;

namespace SteamP2PFriends.MultiObserver.SPI
{
    /// <summary>
    /// 一维导航 Bound 的状态复制接缝。它与二维 Region 复制接口分离。
    /// </summary>
    public interface IBoundStateReplicationAdapter
    {
        void OnBoundObserverEntered(ulong observerId, ulong connectionToken, BoundKey boundKey);
        void OnBoundObserverExited(ulong observerId, ulong connectionToken, BoundKey boundKey);
    }
}
