using SDG.NetTransport;
using SDG.Unturned;
using SteamP2PFriends.Shared;
using SteamP2PFriends.Core.Identity;
using System;
using System.Collections.Generic;

namespace SteamP2PFriends.Adapters.Zombie
{
    public enum ZombieSnapshotCapability
    {
        ReliableEnqueueBaseline = 1
    }

    public enum ZombieSnapshotBeginResult
    {
        Begin = 1,
        AlreadyCommitted = 2,
        Rejected = 3
    }

    public readonly struct ZombieSnapshotToken : IEquatable<ZombieSnapshotToken>
    {
        public readonly ulong ObserverId;
        public readonly ulong ConnectionToken;
        public readonly uint SessionEpoch;
        public readonly BoundKey Bound;
        public readonly uint RegionGeneration;
        public readonly uint SnapshotSequence;
        public readonly bool Valid;

        public ZombieSnapshotToken(
            ulong observerId,
            ulong connectionToken,
            uint sessionEpoch,
            BoundKey bound,
            uint regionGeneration,
            uint snapshotSequence,
            bool valid)
        {
            ObserverId = observerId;
            ConnectionToken = connectionToken;
            SessionEpoch = sessionEpoch;
            Bound = bound;
            RegionGeneration = regionGeneration;
            SnapshotSequence = snapshotSequence;
            Valid = valid;
        }

        public bool Equals(ZombieSnapshotToken other)
        {
            return ObserverId == other.ObserverId
                && ConnectionToken == other.ConnectionToken
                && SessionEpoch == other.SessionEpoch
                && Bound == other.Bound
                && RegionGeneration == other.RegionGeneration
                && SnapshotSequence == other.SnapshotSequence
                && Valid == other.Valid;
        }

        public override bool Equals(object obj) => obj is ZombieSnapshotToken token && Equals(token);
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = ObserverId.GetHashCode();
                hash = (hash * 397) ^ ConnectionToken.GetHashCode();
                hash = (hash * 397) ^ (int)SessionEpoch;
                hash = (hash * 397) ^ Bound.GetHashCode();
                hash = (hash * 397) ^ (int)RegionGeneration;
                hash = (hash * 397) ^ (int)SnapshotSequence;
                hash = (hash * 397) ^ Valid.GetHashCode();
                return hash;
            }
        }
    }

    public sealed class ZombieSnapshotReplicationLedger
    {
        private sealed class ObserverZombieReplicationState
        {
            public ulong ConnectionToken;
            public BoundKey ActiveBound = BoundKey.None;
            public uint SessionEpoch;
            public uint LastSnapshotSequence;
            public uint LastDeltaSequence;
            public readonly Dictionary<BoundKey, uint> CommittedGenerations = new Dictionary<BoundKey, uint>();
            public readonly HashSet<BoundKey> ClothesCommitted = new HashSet<BoundKey>();
            public readonly HashSet<BoundKey> PreparingBounds = new HashSet<BoundKey>();
        }

        private readonly object _sync = new object();
        private readonly Dictionary<ulong, ObserverZombieReplicationState> _observers =
            new Dictionary<ulong, ObserverZombieReplicationState>();
        private uint _currentEpoch = 1;
        private uint _globalSequence = 0;

        public void Reset(uint nextEpoch)
        {
            lock (_sync)
            {
                _observers.Clear();
                _currentEpoch = nextEpoch == 0 ? 1 : nextEpoch;
                _globalSequence = 0;
            }
        }

        public bool UpdateRelevance(
            ulong observerId,
            ulong connectionToken,
            BoundKey bound,
            uint sessionEpoch,
            out bool changed)
        {
            lock (_sync)
            {
                changed = false;
                if (sessionEpoch != _currentEpoch)
                {
                    return false;
                }

                if (!_observers.TryGetValue(observerId, out ObserverZombieReplicationState state))
                {
                    state = new ObserverZombieReplicationState
                    {
                        ConnectionToken = connectionToken,
                        ActiveBound = bound,
                        SessionEpoch = sessionEpoch
                    };
                    _observers[observerId] = state;
                    changed = true;
                    return true;
                }

                if (state.ConnectionToken != connectionToken || state.SessionEpoch != sessionEpoch)
                {
                    state.ConnectionToken = connectionToken;
                    state.SessionEpoch = sessionEpoch;
                    state.ActiveBound = bound;
                    state.CommittedGenerations.Clear();
                    state.ClothesCommitted.Clear();
                    state.PreparingBounds.Clear();
                    state.LastSnapshotSequence = 0;
                    state.LastDeltaSequence = 0;
                    changed = true;
                    return true;
                }

                if (state.ActiveBound != bound)
                {
                    state.ActiveBound = bound;
                    changed = true;
                }

                return true;
            }
        }

        public bool RemoveObserver(ulong observerId)
        {
            lock (_sync)
            {
                return _observers.Remove(observerId);
            }
        }

        public ZombieSnapshotBeginResult TryBegin(
            ulong observerId,
            ulong connectionToken,
            BoundKey bound,
            uint sessionEpoch,
            uint regionGeneration,
            out ZombieSnapshotToken token)
        {
            lock (_sync)
            {
                token = default;
                if (sessionEpoch != _currentEpoch)
                {
                    return ZombieSnapshotBeginResult.Rejected;
                }

                if (!_observers.TryGetValue(observerId, out ObserverZombieReplicationState state)
                    || state.ConnectionToken != connectionToken
                    || state.SessionEpoch != sessionEpoch)
                {
                    return ZombieSnapshotBeginResult.Rejected;
                }

                if (state.ActiveBound != bound && !bound.IsNone)
                {
                    return ZombieSnapshotBeginResult.Rejected;
                }

                if (state.CommittedGenerations.TryGetValue(bound, out uint committedGen)
                    && committedGen == regionGeneration)
                {
                    return ZombieSnapshotBeginResult.AlreadyCommitted;
                }

                if (state.PreparingBounds.Contains(bound))
                {
                    return ZombieSnapshotBeginResult.Rejected;
                }

                state.PreparingBounds.Add(bound);
                _globalSequence++;
                state.LastSnapshotSequence = _globalSequence;

                token = new ZombieSnapshotToken(
                    observerId,
                    connectionToken,
                    sessionEpoch,
                    bound,
                    regionGeneration,
                    state.LastSnapshotSequence,
                    true);

                return ZombieSnapshotBeginResult.Begin;
            }
        }

        public bool Commit(ZombieSnapshotToken token)
        {
            if (!token.Valid) return false;

            lock (_sync)
            {
                if (token.SessionEpoch != _currentEpoch) return false;

                if (!_observers.TryGetValue(token.ObserverId, out ObserverZombieReplicationState state)
                    || state.ConnectionToken != token.ConnectionToken
                    || state.SessionEpoch != token.SessionEpoch)
                {
                    return false;
                }

                if (!state.PreparingBounds.Remove(token.Bound))
                {
                    return false;
                }

                state.CommittedGenerations[token.Bound] = token.RegionGeneration;
                state.ClothesCommitted.Add(token.Bound);
                return true;
            }
        }

        public bool Abort(ZombieSnapshotToken token)
        {
            if (!token.Valid) return false;

            lock (_sync)
            {
                if (!_observers.TryGetValue(token.ObserverId, out ObserverZombieReplicationState state)
                    || state.ConnectionToken != token.ConnectionToken
                    || state.SessionEpoch != token.SessionEpoch)
                {
                    return false;
                }

                return state.PreparingBounds.Remove(token.Bound);
            }
        }

        public bool IsCommitted(
            ulong observerId,
            ulong connectionToken,
            BoundKey bound,
            uint sessionEpoch,
            uint regionGeneration)
        {
            lock (_sync)
            {
                if (sessionEpoch != _currentEpoch) return false;

                if (!_observers.TryGetValue(observerId, out ObserverZombieReplicationState state)
                    || state.ConnectionToken != connectionToken
                    || state.SessionEpoch != sessionEpoch)
                {
                    return false;
                }

                return state.CommittedGenerations.TryGetValue(bound, out uint committedGen)
                    && committedGen == regionGeneration;
            }
        }

        public uint NextDeltaSequence(ulong observerId, BoundKey bound, uint sessionEpoch)
        {
            lock (_sync)
            {
                if (sessionEpoch != _currentEpoch) return 0;

                if (!_observers.TryGetValue(observerId, out ObserverZombieReplicationState state)
                    || state.SessionEpoch != sessionEpoch)
                {
                    return 0;
                }

                state.LastDeltaSequence++;
                return state.LastDeltaSequence;
            }
        }
    }

    public static class ZombieSnapshotAdapter
    {
        public static ZombieSnapshotCapability Capability => ZombieSnapshotCapability.ReliableEnqueueBaseline;

        private sealed class ConnectionBinding
        {
            internal object Connection;
            internal ulong Token;
        }

        private static readonly ZombieSnapshotReplicationLedger Ledger = new ZombieSnapshotReplicationLedger();
        private static readonly Dictionary<ulong, ConnectionBinding> Connections =
            new Dictionary<ulong, ConnectionBinding>();
        private static ulong _nextConnectionToken;
        private static uint _currentEpoch = 1;

        public static void ResetSession(uint nextEpoch)
        {
            _currentEpoch = nextEpoch == 0 ? 1 : nextEpoch;
            Ledger.Reset(_currentEpoch);
            Connections.Clear();
            RoleLogger.Info("[Host]", $"[MultiObserver/M4-ZombieSnapshot] session-begin epoch={_currentEpoch}");
        }

        public static ZombieSnapshotReplicationLedger CreateLedgerForTests()
        {
            return new ZombieSnapshotReplicationLedger();
        }

        public static bool ShouldReplicateForObserver(Player player, BoundKey bound)
        {
            if (player == null || player.channel == null) return false;
            if (Dedicator.IsDedicatedServer) return true;
            if (player.channel.IsLocalPlayer) return false;

            return ListenRegionSyncEligibility.IsDedicatedOrP2PHost();
        }

        public static ZombieSnapshotBeginResult TryBeginSnapshot(
            Player player,
            BoundKey bound,
            uint regionGeneration,
            out ZombieSnapshotToken token)
        {
            token = default;
            if (!TryResolve(player, out ulong observerId, out ulong connectionToken))
                return ZombieSnapshotBeginResult.Rejected;

            Ledger.UpdateRelevance(observerId, connectionToken, bound, _currentEpoch, out _);

            return Ledger.TryBegin(
                observerId,
                connectionToken,
                bound,
                _currentEpoch,
                regionGeneration,
                out token);
        }

        public static bool CommitSnapshot(ZombieSnapshotToken token)
        {
            return Ledger.Commit(token);
        }

        public static bool AbortSnapshot(ZombieSnapshotToken token)
        {
            return Ledger.Abort(token);
        }

        public static bool IsSnapshotCommitted(Player player, BoundKey bound, uint regionGeneration)
        {
            if (!TryResolve(player, out ulong observerId, out ulong connectionToken))
                return false;

            return Ledger.IsCommitted(observerId, connectionToken, bound, _currentEpoch, regionGeneration);
        }

        private static ulong EnsureConnectionToken(ulong observerId, object connection)
        {
            if (observerId == 0UL || connection == null) return 0UL;
            if (Connections.TryGetValue(observerId, out ConnectionBinding binding)
                && ReferenceEquals(binding.Connection, connection))
                return binding.Token;

            _nextConnectionToken = _nextConnectionToken == ulong.MaxValue ? 1UL : _nextConnectionToken + 1UL;
            if (_nextConnectionToken == 0UL) _nextConnectionToken = 1UL;
            Connections[observerId] = new ConnectionBinding
            {
                Connection = connection,
                Token = _nextConnectionToken
            };
            return _nextConnectionToken;
        }

        private static bool TryResolve(
            Player player,
            out ulong observerId,
            out ulong connectionToken)
        {
            observerId = 0UL;
            connectionToken = 0UL;
            if (player?.channel?.owner?.playerID == null) return false;

            observerId = player.channel.owner.playerID.steamID.m_SteamID;
            object connection = player.channel.owner.transportConnection ?? (object)player.channel;
            connectionToken = EnsureConnectionToken(observerId, connection);
            return observerId != 0UL && connectionToken != 0UL;
        }
    }
}
