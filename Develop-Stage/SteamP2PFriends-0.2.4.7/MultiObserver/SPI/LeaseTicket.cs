using System;

namespace SteamP2PFriends.MultiObserver.SPI
{
    /// <summary>
    /// 强类型区域功能性租约票据 (Lease Ticket)
    /// 封装特定领域、区域坐标/索引、会话代、权威区域代以及当前活跃需求计数。
    /// </summary>
    public readonly struct LeaseTicket : IEquatable<LeaseTicket>
    {
        public readonly string DomainName;
        public readonly int RegionKey;
        public readonly uint SessionEpoch;
        public readonly uint RegionGeneration;
        public readonly int ActiveDemandCount;
        public readonly bool Valid;

        public LeaseTicket(
            string domainName,
            int regionKey,
            uint sessionEpoch,
            uint regionGeneration,
            int activeDemandCount,
            bool valid)
        {
            DomainName = domainName ?? string.Empty;
            RegionKey = regionKey;
            SessionEpoch = sessionEpoch;
            RegionGeneration = regionGeneration;
            ActiveDemandCount = activeDemandCount;
            Valid = valid;
        }

        public bool Equals(LeaseTicket other)
        {
            return string.Equals(DomainName, other.DomainName, StringComparison.Ordinal)
                && RegionKey == other.RegionKey
                && SessionEpoch == other.SessionEpoch
                && RegionGeneration == other.RegionGeneration
                && ActiveDemandCount == other.ActiveDemandCount
                && Valid == other.Valid;
        }

        public override bool Equals(object obj) => obj is LeaseTicket other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = DomainName != null ? DomainName.GetHashCode() : 0;
                hash = (hash * 397) ^ RegionKey;
                hash = (hash * 397) ^ (int)SessionEpoch;
                hash = (hash * 397) ^ (int)RegionGeneration;
                hash = (hash * 397) ^ ActiveDemandCount;
                hash = (hash * 397) ^ Valid.GetHashCode();
                return hash;
            }
        }

        public override string ToString()
        {
            return $"LeaseTicket[{DomainName} key={RegionKey} epoch={SessionEpoch} gen={RegionGeneration} demand={ActiveDemandCount}]";
        }
    }
}
