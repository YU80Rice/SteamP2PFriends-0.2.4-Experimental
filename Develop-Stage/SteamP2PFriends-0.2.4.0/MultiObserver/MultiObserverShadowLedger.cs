using System;
using System.Collections.Generic;
using System.Threading;

namespace SteamP2PFriends.MultiObserver
{
    internal sealed class ShadowShutdownGate
    {
        private int _requested;
        private int _latched;

        internal void Request()
        {
            Interlocked.Exchange(ref _latched, 1);
            Interlocked.Exchange(ref _requested, 1);
        }

        internal void PrepareForInitialize()
        {
            Interlocked.Exchange(ref _latched, 0);
            Interlocked.Exchange(ref _requested, 1);
        }

        internal bool Consume() => Interlocked.Exchange(ref _requested, 0) != 0;
        internal bool IsRequested => Volatile.Read(ref _requested) != 0;
        internal bool IsLatched => Volatile.Read(ref _latched) != 0;
    }

    internal sealed class ShadowFaultBackoff
    {
        internal bool IsFaulted { get; private set; }
        internal int Attempt { get; private set; }
        internal float NextRecoveryAt { get; private set; }

        internal bool RecordFailure(float now)
        {
            if (IsFaulted) return false;
            Attempt = Attempt == int.MaxValue ? 1 : Attempt + 1;
            int exponent = Math.Min(Attempt - 1, 5);
            float delay = Math.Min(60f, 1 << exponent);
            NextRecoveryAt = now + delay;
            IsFaulted = true;
            return true;
        }

        internal bool TryBeginRecovery(float now)
        {
            if (!IsFaulted || now < NextRecoveryAt) return false;
            IsFaulted = false;
            return true;
        }

        internal void MarkSuccess()
        {
            IsFaulted = false;
            Attempt = 0;
            NextRecoveryAt = 0f;
        }
    }

    internal readonly struct RegionKey : IEquatable<RegionKey>
    {
        internal RegionKey(byte x, byte y)
        {
            X = x;
            Y = y;
        }

        internal byte X { get; }
        internal byte Y { get; }

        public bool Equals(RegionKey other) => X == other.X && Y == other.Y;
        public override bool Equals(object obj) => obj is RegionKey other && Equals(other);
        public override int GetHashCode() => (X << 8) | Y;
        public override string ToString() => $"({X},{Y})";
    }

    internal readonly struct ObserverShadowSample
    {
        internal ObserverShadowSample(
            ulong observerId,
            ulong connectionToken,
            byte itemRegionX,
            byte itemRegionY,
            byte zombieBound,
            bool hasFunctionalZombieBound,
            bool isLocalPlayer,
            bool gameplayAuthorized,
            int nativeLoadedItemRegions,
            int nativeStaleLoadedItemRegions,
            int committedItemRegions)
        {
            ObserverId = observerId;
            ConnectionToken = connectionToken;
            ItemRegionX = itemRegionX;
            ItemRegionY = itemRegionY;
            ZombieBound = zombieBound;
            HasFunctionalZombieBound = hasFunctionalZombieBound;
            IsLocalPlayer = isLocalPlayer;
            GameplayAuthorized = gameplayAuthorized;
            NativeLoadedItemRegions = nativeLoadedItemRegions;
            NativeStaleLoadedItemRegions = nativeStaleLoadedItemRegions;
            CommittedItemRegions = committedItemRegions;
        }

        internal ulong ObserverId { get; }
        internal ulong ConnectionToken { get; }
        internal byte ItemRegionX { get; }
        internal byte ItemRegionY { get; }
        internal byte ZombieBound { get; }
        internal bool HasFunctionalZombieBound { get; }
        internal bool IsLocalPlayer { get; }
        internal bool GameplayAuthorized { get; }
        internal int NativeLoadedItemRegions { get; }
        internal int NativeStaleLoadedItemRegions { get; }
        internal int CommittedItemRegions { get; }
    }

    internal enum ShadowTransitionKind : byte
    {
        ObserverAdded,
        ObserverRemoved,
        ConnectionChanged,
        RelevanceChanged,
        AuthorizationChanged,
        DuplicateObserverIgnored
    }

    internal readonly struct ShadowTransition
    {
        internal ShadowTransition(ShadowTransitionKind kind, ulong observerId, string detail)
        {
            Kind = kind;
            ObserverId = observerId;
            Detail = detail ?? string.Empty;
        }

        internal ShadowTransitionKind Kind { get; }
        internal ulong ObserverId { get; }
        internal string Detail { get; }
    }

    internal readonly struct ObserverShadowSnapshot
    {
        internal ObserverShadowSnapshot(ObserverShadowState state)
        {
            ObserverId = state.ObserverId;
            ConnectionGeneration = state.ConnectionGeneration;
            LifecycleSequence = state.LifecycleSequence;
            ItemRegionX = state.ItemRegionX;
            ItemRegionY = state.ItemRegionY;
            ZombieBound = state.ZombieBound;
            HasFunctionalZombieBound = state.HasFunctionalZombieBound;
            IsLocalPlayer = state.IsLocalPlayer;
            GameplayAuthorized = state.GameplayAuthorized;
            NativeLoadedItemRegions = state.NativeLoadedItemRegions;
            NativeStaleLoadedItemRegions = state.NativeStaleLoadedItemRegions;
            CommittedItemRegions = state.CommittedItemRegions;
            ItemRegionCount = state.ItemRegions.Count;
        }

        internal ulong ObserverId { get; }
        internal uint ConnectionGeneration { get; }
        internal uint LifecycleSequence { get; }
        internal byte ItemRegionX { get; }
        internal byte ItemRegionY { get; }
        internal byte ZombieBound { get; }
        internal bool HasFunctionalZombieBound { get; }
        internal bool IsLocalPlayer { get; }
        internal bool GameplayAuthorized { get; }
        internal int NativeLoadedItemRegions { get; }
        internal int NativeStaleLoadedItemRegions { get; }
        internal int CommittedItemRegions { get; }
        internal int ItemRegionCount { get; }
    }

    internal sealed class ObserverShadowState
    {
        internal ulong ObserverId;
        internal ulong ConnectionToken;
        internal uint ConnectionGeneration;
        internal uint LifecycleSequence;
        internal byte ItemRegionX;
        internal byte ItemRegionY;
        internal byte ZombieBound;
        internal bool HasFunctionalZombieBound;
        internal bool IsLocalPlayer;
        internal bool GameplayAuthorized;
        internal int NativeLoadedItemRegions;
        internal int NativeStaleLoadedItemRegions;
        internal int CommittedItemRegions;
        internal HashSet<RegionKey> ItemRegions = new HashSet<RegionKey>();
    }

    /// <summary>
    /// Pure M0 shadow ledger. It has no Provider, Unity, networking, or authorization dependency.
    /// Its caller owns game-thread serialization.
    /// </summary>
    internal sealed class MultiObserverShadowLedger
    {
        private readonly Dictionary<ulong, ObserverShadowState> _observers =
            new Dictionary<ulong, ObserverShadowState>();
        private readonly Dictionary<RegionKey, int> _itemDemand =
            new Dictionary<RegionKey, int>();
        private readonly Dictionary<byte, int> _zombieDemand =
            new Dictionary<byte, int>();

        private ulong _sessionEpoch;
        private uint _nextConnectionGeneration;
        private uint _nextLifecycleSequence;
        private string _worldIdentity = string.Empty;

        internal bool IsSessionActive { get; private set; }
        internal ulong SessionEpoch => _sessionEpoch;
        internal string WorldIdentity => _worldIdentity;
        internal int ObserverCount => _observers.Count;
        internal int ItemDemandRegionCount => _itemDemand.Count;
        internal int ZombieDemandBoundCount => _zombieDemand.Count;

        internal bool BeginSession(string worldIdentity)
        {
            string normalized = worldIdentity ?? string.Empty;
            if (IsSessionActive && string.Equals(_worldIdentity, normalized, StringComparison.Ordinal))
                return false;

            AdvanceEpoch();
            ClearSessionState();
            _worldIdentity = normalized;
            IsSessionActive = true;
            return true;
        }

        internal bool EndSession()
        {
            if (!IsSessionActive) return false;

            // Publish the next epoch before clearing old observer state.
            AdvanceEpoch();
            IsSessionActive = false;
            _worldIdentity = string.Empty;
            ClearSessionState();
            return true;
        }

        internal IReadOnlyList<ShadowTransition> Reconcile(
            IReadOnlyList<ObserverShadowSample> samples,
            int worldSize,
            int itemRadius,
            bool allowAbsenceRemoval = true)
        {
            if (!IsSessionActive)
                throw new InvalidOperationException("Cannot reconcile observers without an active session.");
            if (worldSize <= 0 || worldSize > byte.MaxValue + 1)
                throw new ArgumentOutOfRangeException(nameof(worldSize));
            if (itemRadius < 0 || itemRadius >= worldSize)
                throw new ArgumentOutOfRangeException(nameof(itemRadius));

            var transitions = new List<ShadowTransition>();
            var seen = new HashSet<ulong>();
            int sampleCount = samples?.Count ?? 0;
            if (sampleCount > 64)
                throw new InvalidOperationException("Observer sample capacity exceeded.");

            for (int index = 0; index < sampleCount; index++)
            {
                ObserverShadowSample sample = samples[index];
                if (sample.ObserverId == 0UL || sample.ConnectionToken == 0UL) continue;
                if (!seen.Add(sample.ObserverId))
                {
                    transitions.Add(new ShadowTransition(
                        ShadowTransitionKind.DuplicateObserverIgnored,
                        sample.ObserverId,
                        "duplicate sample ignored"));
                    continue;
                }

                Upsert(sample, worldSize, itemRadius, transitions);
            }

            if (allowAbsenceRemoval && _observers.Count > seen.Count)
            {
                var removed = new List<ulong>();
                foreach (ulong observerId in _observers.Keys)
                {
                    if (!seen.Contains(observerId)) removed.Add(observerId);
                }

                foreach (ulong observerId in removed)
                {
                    ObserverShadowState state = _observers[observerId];
                    RemoveDemand(state);
                    state.LifecycleSequence = NextLifecycleSequence();
                    _observers.Remove(observerId);
                    transitions.Add(new ShadowTransition(
                        ShadowTransitionKind.ObserverRemoved,
                        observerId,
                        $"connectionGeneration={state.ConnectionGeneration} lifecycle={state.LifecycleSequence}"));
                }
            }

            return transitions;
        }

        internal bool TryGetObserver(ulong observerId, out ObserverShadowSnapshot snapshot)
        {
            if (_observers.TryGetValue(observerId, out ObserverShadowState state))
            {
                snapshot = new ObserverShadowSnapshot(state);
                return true;
            }
            snapshot = default;
            return false;
        }

        internal int GetItemDemand(RegionKey region) =>
            _itemDemand.TryGetValue(region, out int count) ? count : 0;

        internal int GetZombieDemand(byte bound) =>
            _zombieDemand.TryGetValue(bound, out int count) ? count : 0;

        internal IReadOnlyList<ObserverShadowSnapshot> SnapshotObservers()
        {
            var snapshot = new List<ObserverShadowSnapshot>(_observers.Count);
            foreach (ObserverShadowState state in _observers.Values)
                snapshot.Add(new ObserverShadowSnapshot(state));
            return snapshot;
        }

        internal IReadOnlyDictionary<byte, int> SnapshotZombieDemand() =>
            new Dictionary<byte, int>(_zombieDemand);

        private void Upsert(
            in ObserverShadowSample sample,
            int worldSize,
            int itemRadius,
            List<ShadowTransition> transitions)
        {
            HashSet<RegionKey> desiredItemRegions = BuildRegions(
                sample.ItemRegionX, sample.ItemRegionY, worldSize, itemRadius);

            if (!_observers.TryGetValue(sample.ObserverId, out ObserverShadowState state))
            {
                uint generation = NextConnectionGeneration();
                state = new ObserverShadowState
                {
                    ObserverId = sample.ObserverId,
                    ConnectionToken = sample.ConnectionToken,
                    ConnectionGeneration = generation,
                    LifecycleSequence = NextLifecycleSequence(),
                    ItemRegionX = sample.ItemRegionX,
                    ItemRegionY = sample.ItemRegionY,
                    ZombieBound = sample.ZombieBound,
                    HasFunctionalZombieBound = sample.HasFunctionalZombieBound,
                    IsLocalPlayer = sample.IsLocalPlayer,
                    GameplayAuthorized = sample.GameplayAuthorized,
                    NativeLoadedItemRegions = sample.NativeLoadedItemRegions,
                    NativeStaleLoadedItemRegions = sample.NativeStaleLoadedItemRegions,
                    CommittedItemRegions = sample.CommittedItemRegions,
                    ItemRegions = desiredItemRegions
                };
                _observers.Add(sample.ObserverId, state);
                AddDemand(state);
                transitions.Add(new ShadowTransition(
                    ShadowTransitionKind.ObserverAdded,
                    sample.ObserverId,
                    $"connectionGeneration={generation} local={sample.IsLocalPlayer} authorized={sample.GameplayAuthorized}"));
                return;
            }

            bool connectionChanged = state.ConnectionToken != sample.ConnectionToken;
            bool relevanceChanged = state.ItemRegionX != sample.ItemRegionX
                || state.ItemRegionY != sample.ItemRegionY
                || state.ZombieBound != sample.ZombieBound
                || state.HasFunctionalZombieBound != sample.HasFunctionalZombieBound;
            bool authorizationChanged = state.GameplayAuthorized != sample.GameplayAuthorized;

            if (connectionChanged || relevanceChanged)
            {
                RemoveDemand(state);
            }

            if (connectionChanged)
            {
                state.ConnectionToken = sample.ConnectionToken;
                state.ConnectionGeneration = NextConnectionGeneration();
                state.LifecycleSequence = NextLifecycleSequence();
                transitions.Add(new ShadowTransition(
                    ShadowTransitionKind.ConnectionChanged,
                    sample.ObserverId,
                    $"connectionGeneration={state.ConnectionGeneration} lifecycle={state.LifecycleSequence}"));
            }

            state.ItemRegionX = sample.ItemRegionX;
            state.ItemRegionY = sample.ItemRegionY;
            state.ZombieBound = sample.ZombieBound;
            state.HasFunctionalZombieBound = sample.HasFunctionalZombieBound;
            state.IsLocalPlayer = sample.IsLocalPlayer;
            state.GameplayAuthorized = sample.GameplayAuthorized;
            state.NativeLoadedItemRegions = sample.NativeLoadedItemRegions;
            state.NativeStaleLoadedItemRegions = sample.NativeStaleLoadedItemRegions;
            state.CommittedItemRegions = sample.CommittedItemRegions;
            state.ItemRegions = desiredItemRegions;

            if (connectionChanged || relevanceChanged)
            {
                AddDemand(state);
            }

            if (relevanceChanged)
            {
                transitions.Add(new ShadowTransition(
                    ShadowTransitionKind.RelevanceChanged,
                    sample.ObserverId,
                    $"itemCenter=({sample.ItemRegionX},{sample.ItemRegionY}) zombieBound={sample.ZombieBound} " +
                    $"zombieRelevant={sample.HasFunctionalZombieBound}"));
            }

            if (authorizationChanged)
            {
                transitions.Add(new ShadowTransition(
                    ShadowTransitionKind.AuthorizationChanged,
                    sample.ObserverId,
                    $"authorized={sample.GameplayAuthorized}; observer presence unchanged"));
            }
        }

        private static HashSet<RegionKey> BuildRegions(byte centerX, byte centerY, int worldSize, int radius)
        {
            var result = new HashSet<RegionKey>();
            int minX = Math.Max(0, centerX - radius);
            int maxX = Math.Min(worldSize - 1, centerX + radius);
            int minY = Math.Max(0, centerY - radius);
            int maxY = Math.Min(worldSize - 1, centerY + radius);
            for (int x = minX; x <= maxX; x++)
            {
                for (int y = minY; y <= maxY; y++)
                {
                    result.Add(new RegionKey((byte)x, (byte)y));
                }
            }
            return result;
        }

        private uint NextConnectionGeneration()
        {
            _nextConnectionGeneration = _nextConnectionGeneration == uint.MaxValue
                ? 1U
                : _nextConnectionGeneration + 1U;
            if (_nextConnectionGeneration == 0U) _nextConnectionGeneration = 1U;
            return _nextConnectionGeneration;
        }

        private uint NextLifecycleSequence()
        {
            _nextLifecycleSequence = _nextLifecycleSequence == uint.MaxValue
                ? 1U
                : _nextLifecycleSequence + 1U;
            if (_nextLifecycleSequence == 0U) _nextLifecycleSequence = 1U;
            return _nextLifecycleSequence;
        }

        private void AddDemand(ObserverShadowState state)
        {
            foreach (RegionKey region in state.ItemRegions)
                Increment(_itemDemand, region);
            if (state.HasFunctionalZombieBound)
                Increment(_zombieDemand, state.ZombieBound);
        }

        private void RemoveDemand(ObserverShadowState state)
        {
            foreach (RegionKey region in state.ItemRegions)
                Decrement(_itemDemand, region);
            if (state.HasFunctionalZombieBound)
                Decrement(_zombieDemand, state.ZombieBound);
        }

        private static void Increment<TKey>(Dictionary<TKey, int> dictionary, TKey key)
        {
            dictionary.TryGetValue(key, out int count);
            dictionary[key] = checked(count + 1);
        }

        private static void Decrement<TKey>(Dictionary<TKey, int> dictionary, TKey key)
        {
            if (!dictionary.TryGetValue(key, out int count) || count <= 0)
                throw new InvalidOperationException("Functional demand underflow.");
            if (count == 1) dictionary.Remove(key);
            else dictionary[key] = count - 1;
        }

        private void AdvanceEpoch()
        {
            _sessionEpoch = _sessionEpoch == ulong.MaxValue ? 1UL : _sessionEpoch + 1UL;
            if (_sessionEpoch == 0UL) _sessionEpoch = 1UL;
        }

        private void ClearSessionState()
        {
            _observers.Clear();
            _nextConnectionGeneration = 0U;
            _nextLifecycleSequence = 0U;
            _itemDemand.Clear();
            _zombieDemand.Clear();
        }
    }
}
