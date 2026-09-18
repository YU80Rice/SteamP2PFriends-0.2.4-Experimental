using System;
using SteamP2PFriends.Core.Identity;

namespace SteamP2PFriends.MultiObserver.Demand
{
    /// <summary>
    /// 由 Demand Projection Engine 算出的 typed Domain Demand。
    /// 需求身份至少是 Domain Id + Region Key：没有 Domain Id 的需求不存在，
    /// 一个领域的需求也永远不能作为另一个领域的执行依据。
    /// </summary>
    public readonly struct DomainDemand : IEquatable<DomainDemand>
    {
        public DomainDemand(DomainId domain, RegionKey regionKey, int observerCount)
        {
            if (!domain.IsDefined)
                throw new ArgumentException("Domain Demand 必须携带已定义的 Domain Id", nameof(domain));
            if (observerCount < 0)
                throw new ArgumentOutOfRangeException(nameof(observerCount), "需求计数不得为负");

            Domain = domain;
            RegionKey = regionKey;
            ObserverCount = observerCount;
        }

        public DomainId Domain { get; }
        public RegionKey RegionKey { get; }

        /// <summary>当前为该区域贡献需求的合格观察者数（计数值，不是观察者名单）。</summary>
        public int ObserverCount { get; }

        public bool Equals(DomainDemand other) =>
            Domain == other.Domain
            && RegionKey == other.RegionKey
            && ObserverCount == other.ObserverCount;

        public override bool Equals(object obj) => obj is DomainDemand other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Domain.GetHashCode();
                hash = (hash * 397) ^ RegionKey.GetHashCode();
                hash = (hash * 397) ^ ObserverCount;
                return hash;
            }
        }

        public override string ToString() => $"{Domain}@{RegionKey} demand={ObserverCount}";

        public static bool operator ==(DomainDemand left, DomainDemand right) => left.Equals(right);
        public static bool operator !=(DomainDemand left, DomainDemand right) => !left.Equals(right);
    }
}
