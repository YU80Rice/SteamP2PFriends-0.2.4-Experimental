using SteamP2PFriends.Core.Identity;
using SteamP2PFriends.MultiObserver.SPI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace SteamP2PFriends.Adapters.Collision
{
    public enum CollisionOverrideKind
    {
        LevelObject = 0,
        DoorAnimation = 1,
        ColliderCulling = 2
    }

    public readonly struct CollisionOverride : IEquatable<CollisionOverride>
    {
        public CollisionOverride(
            CollisionOverrideKind kind, RegionKey regionKey, ulong acquireGeneration, bool pluginOwned)
        {
            Kind = kind;
            RegionKey = regionKey;
            AcquireGeneration = acquireGeneration;
            PluginOwned = pluginOwned;
        }

        public CollisionOverrideKind Kind { get; }
        public RegionKey RegionKey { get; }
        public ulong AcquireGeneration { get; }
        public bool PluginOwned { get; }

        public bool Equals(CollisionOverride other) => Kind == other.Kind
            && RegionKey == other.RegionKey
            && AcquireGeneration == other.AcquireGeneration
            && PluginOwned == other.PluginOwned;
        public override bool Equals(object obj) => obj is CollisionOverride other && Equals(other);
        public override int GetHashCode() => (((int)Kind * 397) ^ RegionKey.GetHashCode())
            * 397 ^ AcquireGeneration.GetHashCode();
    }

    public readonly struct CollisionExecutionIdentity
    {
        public CollisionExecutionIdentity(LeaseTicket ticket, ulong acquireGeneration)
        {
            DomainId = ticket.DomainId;
            RegionKey = ticket.RegionKey;
            SessionEpoch = ticket.SessionEpoch;
            RegionGeneration = ticket.RegionGeneration;
            AcquireGeneration = acquireGeneration;
        }

        public DomainId DomainId { get; }
        public RegionKey RegionKey { get; }
        public SessionEpoch SessionEpoch { get; }
        public RegionGeneration RegionGeneration { get; }
        public ulong AcquireGeneration { get; }
    }

    public readonly struct CollisionAcquisitionReceipt : IEquatable<CollisionAcquisitionReceipt>
    {
        private readonly IReadOnlyList<CollisionOverride> _overrides;

        public CollisionAcquisitionReceipt(
            DomainId domainId, RegionKey regionKey, SessionEpoch sessionEpoch,
            RegionGeneration regionGeneration, ulong acquireGeneration,
            IReadOnlyList<CollisionOverride> overrides)
        {
            DomainId = domainId;
            RegionKey = regionKey;
            SessionEpoch = sessionEpoch;
            RegionGeneration = regionGeneration;
            AcquireGeneration = acquireGeneration;
            _overrides = new ReadOnlyCollection<CollisionOverride>(Copy(overrides));
            Valid = domainId == DomainIds.Collision
                && sessionEpoch.Value != 0UL
                && regionGeneration.IsDefined
                && acquireGeneration != 0UL;
        }

        public DomainId DomainId { get; }
        public RegionKey RegionKey { get; }
        public SessionEpoch SessionEpoch { get; }
        public RegionGeneration RegionGeneration { get; }
        public ulong AcquireGeneration { get; }
        public bool Valid { get; }
        public IReadOnlyList<CollisionOverride> Overrides => _overrides ??
            new ReadOnlyCollection<CollisionOverride>(new CollisionOverride[0]);

        public bool Equals(CollisionAcquisitionReceipt other)
        {
            if (!(DomainId == other.DomainId && RegionKey == other.RegionKey
                && SessionEpoch == other.SessionEpoch
                && RegionGeneration == other.RegionGeneration
                && AcquireGeneration == other.AcquireGeneration
                && Valid == other.Valid
                && Overrides.Count == other.Overrides.Count)) return false;
            for (int i = 0; i < Overrides.Count; i++)
            {
                if (!Overrides[i].Equals(other.Overrides[i])) return false;
            }
            return true;
        }
        public override bool Equals(object obj) => obj is CollisionAcquisitionReceipt other && Equals(other);
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (RegionKey.GetHashCode() * 397) ^ AcquireGeneration.GetHashCode();
                for (int i = 0; i < Overrides.Count; i++) hash = (hash * 397) ^ Overrides[i].GetHashCode();
                return hash;
            }
        }

        private static CollisionOverride[] Copy(IReadOnlyList<CollisionOverride> source)
        {
            if (source == null || source.Count == 0) return new CollisionOverride[0];
            var copy = new CollisionOverride[source.Count];
            for (int i = 0; i < source.Count; i++) copy[i] = source[i];
            return copy;
        }
    }
}
