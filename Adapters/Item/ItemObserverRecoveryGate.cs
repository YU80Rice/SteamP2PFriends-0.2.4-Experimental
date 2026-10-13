using SteamP2PFriends.Core.Identity;
using System;
using System.Collections.Generic;

namespace SteamP2PFriends.Adapters.Item
{
    internal sealed class ItemObserverRecoveryGate
    {
        private readonly HashSet<Key> _blocked = new HashSet<Key>();

        internal void Block(ulong observerId, ulong connectionToken, RegionKey region) =>
            _blocked.Add(new Key(observerId, connectionToken, region));

        internal bool IsBlocked(ulong observerId, ulong connectionToken, RegionKey region) =>
            _blocked.Contains(new Key(observerId, connectionToken, region));

        internal void ReleaseRegion(ulong observerId, ulong connectionToken, RegionKey region) =>
            _blocked.Remove(new Key(observerId, connectionToken, region));

        internal void ReleaseObserver(ulong observerId, ulong connectionToken)
        {
            _blocked.RemoveWhere(key => key.Matches(observerId, connectionToken));
        }

        internal void Clear() => _blocked.Clear();

        private readonly struct Key : IEquatable<Key>
        {
            private readonly ulong _observer;
            private readonly ulong _token;
            private readonly RegionKey _region;

            internal Key(ulong observer, ulong token, RegionKey region)
            {
                _observer = observer;
                _token = token;
                _region = region;
            }

            public bool Equals(Key other) => _observer == other._observer
                && _token == other._token && _region == other._region;
            internal bool Matches(ulong observer, ulong token) =>
                _observer == observer && _token == token;
            public override bool Equals(object obj) => obj is Key && Equals((Key)obj);
            public override int GetHashCode() => _observer.GetHashCode()
                ^ _token.GetHashCode() ^ _region.GetHashCode();
        }
    }
}
