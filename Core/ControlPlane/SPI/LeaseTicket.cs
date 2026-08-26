using System;
using SteamP2PFriends.Core.Identity;

namespace SteamP2PFriends.MultiObserver.SPI
{
    /// <summary>
    /// 强类型区域功能性租约票据 (Lease Ticket)
    /// 封装特定领域、区域坐标/索引、会话代、权威区域代以及当前活跃需求计数。
    /// </summary>
    public readonly struct LeaseTicket : IEquatable<LeaseTicket>
    {
        public readonly DomainId DomainId;
        public readonly RegionKey RegionKey;
        public readonly BoundKey BoundKey;
        public readonly SessionEpoch SessionEpoch;
        public readonly RegionGeneration RegionGeneration;
        public readonly int ActiveDemandCount;
        public readonly bool Valid;

        public LeaseTicket(
            DomainId domainId,
            RegionKey regionKey,
            SessionEpoch sessionEpoch,
            RegionGeneration regionGeneration,
            int activeDemandCount,
            bool valid)
            : this(domainId, regionKey, BoundKey.None, sessionEpoch,
                regionGeneration, activeDemandCount, valid)
        {
        }

        public LeaseTicket(
            DomainId domainId,
            RegionKey regionKey,
            BoundKey boundKey,
            SessionEpoch sessionEpoch,
            RegionGeneration regionGeneration,
            int activeDemandCount,
            bool valid)
        {
            DomainId = domainId;
            RegionKey = regionKey;
            BoundKey = boundKey;
            SessionEpoch = sessionEpoch;
            RegionGeneration = regionGeneration;
            ActiveDemandCount = activeDemandCount;
            Valid = valid;
        }

        public bool Equals(LeaseTicket other)
        {
            return DomainId == other.DomainId
                && RegionKey == other.RegionKey
                && BoundKey == other.BoundKey
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
                int hash = DomainId.GetHashCode();
                hash = (hash * 397) ^ RegionKey.GetHashCode();
                hash = (hash * 397) ^ BoundKey.GetHashCode();
                hash = (hash * 397) ^ SessionEpoch.GetHashCode();
                hash = (hash * 397) ^ RegionGeneration.GetHashCode();
                hash = (hash * 397) ^ ActiveDemandCount;
                hash = (hash * 397) ^ Valid.GetHashCode();
                return hash;
            }
        }

        public override string ToString()
        {
            return $"LeaseTicket[{DomainId} key={RegionKey} bound={BoundKey} epoch={SessionEpoch} gen={RegionGeneration} demand={ActiveDemandCount}]";
        }
    }
}
