using SDG.NetTransport;
using SDG.Unturned;
using SteamP2PFriends.Host;
using SteamP2PFriends.Adapters.Item.Patches;
using SteamP2PFriends.Shared;
using System;
using System.Collections.Generic;

namespace SteamP2PFriends.Adapters.Item
{
    internal enum ItemBaselineCapability : byte
    {
        ReliableEnqueueBaseline = 1
    }

    internal enum ItemBaselineBeginResult : byte
    {
        Begin = 0,
        AlreadyCommitted = 1,
        Rejected = 2
    }

    internal readonly struct ItemReplicationRegion : IEquatable<ItemReplicationRegion>
    {
        internal ItemReplicationRegion(byte x, byte y)
        {
            X = x;
            Y = y;
        }

        internal byte X { get; }
        internal byte Y { get; }

        public bool Equals(ItemReplicationRegion other) => X == other.X && Y == other.Y;
        public override bool Equals(object obj) => obj is ItemReplicationRegion other && Equals(other);
        public override int GetHashCode() => (X << 8) | Y;
    }

    internal readonly struct ItemBaselineGeneration : IEquatable<ItemBaselineGeneration>
    {
        internal ItemBaselineGeneration(int sessionEpoch, int authoritativeGeneration)
        {
            SessionEpoch = sessionEpoch;
            AuthoritativeGeneration = authoritativeGeneration;
        }

        internal int SessionEpoch { get; }
        internal int AuthoritativeGeneration { get; }

        public bool Equals(ItemBaselineGeneration other) =>
            SessionEpoch == other.SessionEpoch
            && AuthoritativeGeneration == other.AuthoritativeGeneration;

        public override bool Equals(object obj) => obj is ItemBaselineGeneration other && Equals(other);
        public override int GetHashCode() => (SessionEpoch * 397) ^ AuthoritativeGeneration;
    }

    internal readonly struct ItemBaselineToken
    {
        internal ItemBaselineToken(
            ulong observerId,
            ulong connectionToken,
            uint connectionGeneration,
            ItemReplicationRegion region,
            ItemBaselineGeneration generation,
            bool ownsTransaction)
        {
            ObserverId = observerId;
            ConnectionToken = connectionToken;
            ConnectionGeneration = connectionGeneration;
            Region = region;
            Generation = generation;
            OwnsTransaction = ownsTransaction;
        }

        internal ulong ObserverId { get; }
        internal ulong ConnectionToken { get; }
        internal uint ConnectionGeneration { get; }
        internal ItemReplicationRegion Region { get; }
        internal ItemBaselineGeneration Generation { get; }
        internal bool OwnsTransaction { get; }
    }

    internal sealed class ItemObserverReplicationLedger
    {
        private sealed class ObserverState
        {
            internal ulong ConnectionToken;
            internal uint ConnectionGeneration;
            internal readonly HashSet<ItemReplicationRegion> Relevant = new HashSet<ItemReplicationRegion>();
            internal readonly Dictionary<ItemReplicationRegion, ItemBaselineGeneration> Committed =
                new Dictionary<ItemReplicationRegion, ItemBaselineGeneration>();
            internal readonly Dictionary<ItemReplicationRegion, ItemBaselineGeneration> Preparing =
                new Dictionary<ItemReplicationRegion, ItemBaselineGeneration>();
        }

        private readonly Dictionary<ulong, ObserverState> _observers =
            new Dictionary<ulong, ObserverState>();
        private uint _nextConnectionGeneration;

        internal uint ObserveConnection(ulong observerId, ulong connectionToken, out bool changed)
        {
            changed = false;
            if (observerId == 0UL || connectionToken == 0UL) return 0U;

            if (!_observers.TryGetValue(observerId, out ObserverState state))
            {
                state = new ObserverState
                {
                    ConnectionToken = connectionToken,
                    ConnectionGeneration = NextConnectionGeneration()
                };
                _observers.Add(observerId, state);
                changed = true;
                return state.ConnectionGeneration;
            }

            if (state.ConnectionToken != connectionToken)
            {
                state.ConnectionToken = connectionToken;
                state.ConnectionGeneration = NextConnectionGeneration();
                state.Relevant.Clear();
                state.Committed.Clear();
                state.Preparing.Clear();
                changed = true;
            }

            return state.ConnectionGeneration;
        }

        internal bool UpdateRelevance(
            ulong observerId,
            ulong connectionToken,
            byte centerX,
            byte centerY,
            int worldSize,
            int radius,
            out bool connectionChanged)
        {
            if (worldSize <= 0 || worldSize > byte.MaxValue + 1 || radius < 0)
                throw new ArgumentOutOfRangeException();

            uint generation = ObserveConnection(observerId, connectionToken, out connectionChanged);
            if (generation == 0U) return false;

            ObserverState state = _observers[observerId];
            var desired = new HashSet<ItemReplicationRegion>();
            int minX = Math.Max(0, centerX - radius);
            int maxX = Math.Min(worldSize - 1, centerX + radius);
            int minY = Math.Max(0, centerY - radius);
            int maxY = Math.Min(worldSize - 1, centerY + radius);
            for (int x = minX; x <= maxX; x++)
            {
                for (int y = minY; y <= maxY; y++)
                {
                    desired.Add(new ItemReplicationRegion((byte)x, (byte)y));
                }
            }

            var released = new List<ItemReplicationRegion>();
            foreach (ItemReplicationRegion region in state.Relevant)
            {
                if (!desired.Contains(region)) released.Add(region);
            }
            foreach (ItemReplicationRegion region in released)
            {
                state.Committed.Remove(region);
                state.Preparing.Remove(region);
            }

            state.Relevant.Clear();
            foreach (ItemReplicationRegion region in desired) state.Relevant.Add(region);
            return true;
        }

        internal ItemBaselineBeginResult TryBegin(
            ulong observerId,
            ulong connectionToken,
            ItemReplicationRegion region,
            ItemBaselineGeneration generation,
            out ItemBaselineToken token)
        {
            token = default;
            uint connectionGeneration = ObserveConnection(observerId, connectionToken, out _);
            if (connectionGeneration == 0U || !_observers.TryGetValue(observerId, out ObserverState state))
                return ItemBaselineBeginResult.Rejected;
            if (!state.Relevant.Contains(region))
                return ItemBaselineBeginResult.Rejected;

            if (state.Committed.TryGetValue(region, out ItemBaselineGeneration committed)
                && committed.Equals(generation))
            {
                return ItemBaselineBeginResult.AlreadyCommitted;
            }

            if (state.Preparing.ContainsKey(region))
                return ItemBaselineBeginResult.Rejected;

            state.Preparing[region] = generation;
            token = new ItemBaselineToken(
                observerId,
                connectionToken,
                connectionGeneration,
                region,
                generation,
                true);
            return ItemBaselineBeginResult.Begin;
        }

        internal bool Commit(in ItemBaselineToken token)
        {
            if (!IsCurrentOwner(token, out ObserverState state)) return false;
            state.Preparing.Remove(token.Region);
            state.Committed[token.Region] = token.Generation;
            return true;
        }

        internal bool Abort(in ItemBaselineToken token)
        {
            if (!IsCurrentOwner(token, out ObserverState state)) return false;
            state.Preparing.Remove(token.Region);
            return true;
        }

        internal bool IsCommitted(
            ulong observerId,
            ulong connectionToken,
            ItemReplicationRegion region,
            ItemBaselineGeneration generation)
        {
            return _observers.TryGetValue(observerId, out ObserverState state)
                && state.ConnectionToken == connectionToken
                && state.Committed.TryGetValue(region, out ItemBaselineGeneration committed)
                && committed.Equals(generation);
        }

        internal bool RemoveObserver(ulong observerId) => _observers.Remove(observerId);

        internal void Reset()
        {
            _observers.Clear();
            _nextConnectionGeneration = 0U;
        }

        private bool IsCurrentOwner(in ItemBaselineToken token, out ObserverState state)
        {
            return _observers.TryGetValue(token.ObserverId, out state)
                && token.OwnsTransaction
                && state.ConnectionToken == token.ConnectionToken
                && state.ConnectionGeneration == token.ConnectionGeneration
                && state.Relevant.Contains(token.Region)
                && state.Preparing.TryGetValue(token.Region, out ItemBaselineGeneration preparing)
                && preparing.Equals(token.Generation);
        }

        private uint NextConnectionGeneration()
        {
            _nextConnectionGeneration = _nextConnectionGeneration == uint.MaxValue
                ? 1U
                : _nextConnectionGeneration + 1U;
            if (_nextConnectionGeneration == 0U) _nextConnectionGeneration = 1U;
            return _nextConnectionGeneration;
        }
    }

    /// <summary>
    /// M2 owner for per-observer map-item baselines. It supervises the native reliable
    /// askItems RPC and does not own pickup removals or player/zombie drop incrementals.
    /// </summary>
    internal static class ItemObserverReplicationAdapter
    {
        internal const ItemBaselineCapability Capability = ItemBaselineCapability.ReliableEnqueueBaseline;
        private const int LogLimit = 32;

        private sealed class ConnectionBinding
        {
            internal object Connection;
            internal ulong Token;
        }

        internal sealed class BaselineCallState
        {
            internal ItemBaselineToken Token;
            internal bool Managed;
            internal Action<bool> LoadedProjectionWriter;

            internal bool TryRollbackLoadedProjection(ulong currentConnectionToken)
            {
                if (!Token.OwnsTransaction
                    || currentConnectionToken == 0UL
                    || currentConnectionToken != Token.ConnectionToken
                    || LoadedProjectionWriter == null)
                    return false;

                LoadedProjectionWriter(false);
                return true;
            }
        }

        private static readonly ItemObserverReplicationLedger Ledger = new ItemObserverReplicationLedger();
        private static readonly Dictionary<ulong, ConnectionBinding> Connections =
            new Dictionary<ulong, ConnectionBinding>();
        private static ulong _nextConnectionToken;
        private static bool _registrationReady;
        private static int _commitLogs;
        private static int _skipLogs;
        private static int _rejectLogs;

        internal static bool IsReady => _registrationReady;

        internal static void SetRegistrationReady(bool ready)
        {
            _registrationReady = ready;
            if (!ready) ResetForSession();
        }

        public static bool ShouldReplicateForObserver(Player player)
        {
            ThreadUtil.assertIsGameThread();
            if (Dedicator.IsDedicatedServer) return true;
            if (!_registrationReady) return false;
            if (!ListenRegionSyncEligibility.IsDedicatedOrP2PRemoteRecipient(player)) return false;

            return TryResolve(player, out ulong observerId, out _, out ulong connectionToken)
                && Ledger.ObserveConnection(observerId, connectionToken, out _) != 0U;
        }

        internal static void ObserveRelevance(Player player, byte newX, byte newY)
        {
            if (!_registrationReady || Dedicator.IsDedicatedServer) return;
            ThreadUtil.assertIsGameThread();
            if (!ListenRegionSyncEligibility.IsDedicatedOrP2PRemoteRecipient(player)) return;
            if (!TryResolve(player, out ulong observerId, out _, out ulong connectionToken)) return;

            if (!Ledger.UpdateRelevance(
                    observerId,
                    connectionToken,
                    newX,
                    newY,
                    Regions.WORLD_SIZE,
                    ItemManager.ITEM_REGIONS,
                    out bool connectionChanged))
                return;

            if (connectionChanged && player?.movement?.loadedRegions != null)
            {
                for (int x = 0; x < Regions.WORLD_SIZE; x++)
                {
                    for (int y = 0; y < Regions.WORLD_SIZE; y++)
                    {
                        player.movement.loadedRegions[x, y].isItemsLoaded = false;
                    }
                }
            }
        }

        internal static bool BeginBaseline(
            ITransportConnection connection,
            byte x,
            byte y,
            out BaselineCallState state)
        {
            state = new BaselineCallState();
            if (Dedicator.IsDedicatedServer) return true;
            // The internal overload is normally reached only from the dedicated/listen
            // region loop, but explicit pass-through preserves any SP/non-P2P caller.
            if (!HostManager.IsP2PHostMode || !Provider.isServer) return true;
            if (!_registrationReady) return false;
            ThreadUtil.assertIsGameThread();

            if (!TryResolve(connection, out SteamPlayer steamPlayer, out ulong observerId, out ulong connectionToken))
            {
                LogReject($"region=({x},{y}) reason=observer-resolution-failed");
                return false;
            }

            state.Managed = true;
            state.LoadedProjectionWriter = value =>
                ProjectLoadedFlag(steamPlayer?.player, x, y, value);
            if (!AuthoritativeItemGenerationGatePatch.TryGetCommittedGeneration(
                    x,
                    y,
                    out int sessionEpoch,
                    out int authoritativeGeneration))
            {
                ProjectLoadedFlag(steamPlayer?.player, x, y, false);
                LogReject($"observer={Mask(observerId)} region=({x},{y}) reason=authority-not-committed");
                return false;
            }

            var region = new ItemReplicationRegion(x, y);
            var generation = new ItemBaselineGeneration(sessionEpoch, authoritativeGeneration);
            ItemBaselineBeginResult result = Ledger.TryBegin(
                observerId,
                connectionToken,
                region,
                generation,
                out ItemBaselineToken token);

            if (result == ItemBaselineBeginResult.Begin)
            {
                state.Token = token;
                return true;
            }

            if (result == ItemBaselineBeginResult.AlreadyCommitted)
            {
                ProjectLoadedFlag(steamPlayer?.player, x, y, true);
                LogSkip($"observer={Mask(observerId)} region=({x},{y}) reason=baseline-already-committed");
                return false;
            }

            ProjectLoadedFlag(steamPlayer?.player, x, y, false);
            LogReject($"observer={Mask(observerId)} region=({x},{y}) reason=ledger-rejected");
            return false;
        }

        internal static void CompleteBaseline(BaselineCallState state, bool originalRan)
        {
            if (state == null || !state.Managed || !state.Token.OwnsTransaction) return;
            ThreadUtil.assertIsGameThread();

            if (!originalRan)
            {
                Ledger.Abort(state.Token);
                RollbackLoadedProjectionIfCurrent(state);
                return;
            }

            if (!Ledger.Commit(state.Token))
            {
                RollbackLoadedProjectionIfCurrent(state);
                LogReject($"observer={Mask(state.Token.ObserverId)} region=({state.Token.Region.X},{state.Token.Region.Y}) reason=commit-rejected");
                return;
            }

            if (_commitLogs < LogLimit)
            {
                _commitLogs++;
                SafeInfo($"commit={_commitLogs}/{LogLimit} capability={Capability} observer={Mask(state.Token.ObserverId)} " +
                    $"connectionGeneration={state.Token.ConnectionGeneration} region=({state.Token.Region.X},{state.Token.Region.Y}) " +
                    $"sessionEpoch={state.Token.Generation.SessionEpoch} worldGeneration={state.Token.Generation.AuthoritativeGeneration}");
            }
        }

        internal static void AbortBaseline(BaselineCallState state, Exception exception)
        {
            if (state == null || !state.Managed || !state.Token.OwnsTransaction) return;
            Ledger.Abort(state.Token);
            RollbackLoadedProjectionIfCurrent(state);
            LogReject($"observer={Mask(state.Token.ObserverId)} region=({state.Token.Region.X},{state.Token.Region.Y}) " +
                $"reason=askItems-exception type={exception?.GetType().Name ?? "unknown"}");
        }

        internal static void RemoveObserver(ulong observerId)
        {
            if (observerId == 0UL) return;
            Connections.Remove(observerId);
            Ledger.RemoveObserver(observerId);
        }

        internal static void RemoveObserversExcept(ISet<ulong> activeObserverIds)
        {
            var removed = new List<ulong>();
            foreach (ulong observerId in Connections.Keys)
            {
                if (activeObserverIds == null || !activeObserverIds.Contains(observerId)) removed.Add(observerId);
            }

            foreach (ulong observerId in removed)
            {
                Connections.Remove(observerId);
                Ledger.RemoveObserver(observerId);
            }
        }

        internal static void ResetForSession()
        {
            Ledger.Reset();
            Connections.Clear();
            _nextConnectionToken = 0UL;
            _commitLogs = 0;
            _skipLogs = 0;
            _rejectLogs = 0;
        }

        internal static ItemObserverReplicationLedger CreateLedgerForTests() =>
            new ItemObserverReplicationLedger();

        private static bool TryResolve(
            Player player,
            out ulong observerId,
            out ITransportConnection connection,
            out ulong connectionToken)
        {
            observerId = player?.channel?.owner?.playerID?.steamID.m_SteamID ?? 0UL;
            connection = player?.channel?.owner?.transportConnection;
            connectionToken = EnsureConnectionToken(observerId, connection);
            return observerId != 0UL && connection != null && connectionToken != 0UL;
        }

        private static bool TryResolve(
            ITransportConnection connection,
            out SteamPlayer steamPlayer,
            out ulong observerId,
            out ulong connectionToken)
        {
            steamPlayer = null;
            observerId = 0UL;
            connectionToken = 0UL;
            if (connection == null || Provider.clients == null) return false;

            foreach (SteamPlayer candidate in Provider.clients)
            {
                if (candidate == null || !ReferenceEquals(candidate.transportConnection, connection)) continue;
                steamPlayer = candidate;
                observerId = candidate.playerID?.steamID.m_SteamID ?? 0UL;
                connectionToken = EnsureConnectionToken(observerId, connection);
                return observerId != 0UL && connectionToken != 0UL;
            }
            return false;
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

        private static void RollbackLoadedProjectionIfCurrent(BaselineCallState state)
        {
            if (state == null || !state.Token.OwnsTransaction) return;
            ulong currentConnectionToken = 0UL;
            if (Connections.TryGetValue(state.Token.ObserverId, out ConnectionBinding binding))
                currentConnectionToken = binding.Token;
            state.TryRollbackLoadedProjection(currentConnectionToken);
        }

        private static void ProjectLoadedFlag(Player player, byte x, byte y, bool value)
        {
            try
            {
                if (player?.movement?.loadedRegions == null) return;
                player.movement.loadedRegions[x, y].isItemsLoaded = value;
            }
            catch (Exception ex)
            {
                LogReject($"region=({x},{y}) reason=loaded-projection-failed type={ex.GetType().Name}");
            }
        }

        private static void LogSkip(string message)
        {
            if (_skipLogs >= LogLimit) return;
            _skipLogs++;
            SafeInfo($"skip={_skipLogs}/{LogLimit} {message}");
        }

        private static void LogReject(string message)
        {
            if (_rejectLogs >= LogLimit) return;
            _rejectLogs++;
            SafeError($"reject={_rejectLogs}/{LogLimit} {message}");
        }

        private static string Mask(ulong observerId) =>
            observerId == 0UL ? "unknown" : DiagnosticMaskUtil.MaskSteamId(observerId);

        private static void SafeInfo(string message)
        {
            try { RoleLogger.Info("[Host]", "[MultiObserver/M2-Item] " + message); } catch { }
        }

        private static void SafeError(string message)
        {
            try { RoleLogger.Error("[Shared]", "[MultiObserver/M2-Item] " + message); } catch { }
        }
    }
}
