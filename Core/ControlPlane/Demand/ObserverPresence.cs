using System;
using SteamP2PFriends.Core.Identity;

namespace SteamP2PFriends.MultiObserver.Demand
{
    /// <summary>
    /// 唯一的 World Presence Observer 空间事实：观察者身份、连接代次、二维区域中心与玩法资格。
    ///
    /// 它只描述「谁在世界里、位于哪个 Region Key、是否按玩法资格参与」。区域集合不是事实，
    /// 而是 Demand Projection Engine 按各领域 Demand Policy 算出的投影结果，因此不在此类型内。
    /// 中心以 Region Key 表达：跨层身份不使用裸整数编码。
    /// </summary>
    public readonly struct ObserverPresence : IEquatable<ObserverPresence>
    {
        public ObserverPresence(
            ulong observerId,
            ulong connectionToken,
            RegionKey center,
            bool gameplayAuthorized)
        {
            if (observerId == 0UL)
                throw new ArgumentOutOfRangeException(nameof(observerId), "观察者身份不得为零");
            if (connectionToken == 0UL)
                throw new ArgumentOutOfRangeException(nameof(connectionToken), "连接代次不得为零");

            ObserverId = observerId;
            ConnectionToken = connectionToken;
            Center = center;
            GameplayAuthorized = gameplayAuthorized;
        }

        public ulong ObserverId { get; }
        public ulong ConnectionToken { get; }
        public RegionKey Center { get; }
        public bool GameplayAuthorized { get; }

        public bool Equals(ObserverPresence other) =>
            ObserverId == other.ObserverId
            && ConnectionToken == other.ConnectionToken
            && Center == other.Center
            && GameplayAuthorized == other.GameplayAuthorized;

        public override bool Equals(object obj) => obj is ObserverPresence other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = ObserverId.GetHashCode();
                hash = (hash * 397) ^ ConnectionToken.GetHashCode();
                hash = (hash * 397) ^ Center.GetHashCode();
                hash = (hash * 397) ^ (GameplayAuthorized ? 1 : 0);
                return hash;
            }
        }

        public override string ToString() =>
            $"observer={ObserverId} connection={ConnectionToken} center={Center} " +
            $"authorized={GameplayAuthorized}";
    }
}
