using SteamP2PFriends.Core.Identity;
using System;

namespace SteamP2PFriends.Adapters.Item
{
    /// <summary>
    /// Item 域区域激活凭证：绑定会话、区域、代次与首次启计时间基准。
    /// </summary>
    public readonly struct ItemAcquisitionReceipt : IEquatable<ItemAcquisitionReceipt>
    {
        public DomainId DomainId { get; }
        public RegionKey RegionKey { get; }
        public SessionEpoch SessionEpoch { get; }
        public RegionGeneration RegionGeneration { get; }
        public ulong AcquireGeneration { get; }
        public float ActivationTime { get; }
        public bool Valid { get; }

        public ItemAcquisitionReceipt(
            DomainId domainId,
            RegionKey regionKey,
            SessionEpoch sessionEpoch,
            RegionGeneration regionGeneration,
            ulong acquireGeneration,
            float activationTime)
        {
            DomainId = domainId;
            RegionKey = regionKey;
            SessionEpoch = sessionEpoch;
            RegionGeneration = regionGeneration;
            AcquireGeneration = acquireGeneration;
            ActivationTime = activationTime;
            Valid = domainId == DomainIds.Item
                && sessionEpoch.Value != 0UL
                && regionGeneration.IsDefined
                && acquireGeneration != 0UL;
        }

        public bool Equals(ItemAcquisitionReceipt other) =>
            Valid == other.Valid
            && DomainId == other.DomainId
            && RegionKey.Equals(other.RegionKey)
            && SessionEpoch.Equals(other.SessionEpoch)
            && RegionGeneration.Equals(other.RegionGeneration)
            && AcquireGeneration == other.AcquireGeneration
            && Math.Abs(ActivationTime - other.ActivationTime) < 0.0001f;

        public override bool Equals(object obj) =>
            obj is ItemAcquisitionReceipt other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + DomainId.GetHashCode();
                hash = hash * 31 + RegionKey.GetHashCode();
                hash = hash * 31 + SessionEpoch.GetHashCode();
                hash = hash * 31 + RegionGeneration.GetHashCode();
                hash = hash * 31 + AcquireGeneration.GetHashCode();
                return hash;
            }
        }

        public static bool operator ==(ItemAcquisitionReceipt left, ItemAcquisitionReceipt right) => left.Equals(right);
        public static bool operator !=(ItemAcquisitionReceipt left, ItemAcquisitionReceipt right) => !left.Equals(right);
    }
}
