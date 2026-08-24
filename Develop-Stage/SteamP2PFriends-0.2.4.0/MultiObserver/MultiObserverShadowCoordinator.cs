using SDG.Unturned;
using SteamP2PFriends.Host;
using SteamP2PFriends.Patches;
using SteamP2PFriends.Shared;
using Steamworks;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SteamP2PFriends.MultiObserver
{
    /// <summary>
    /// U3 read-only adapter for the M0 shadow ledger. No game state, loaded flag, RPC, or
    /// authorization decision is mutated here.
    /// </summary>
    internal static class MultiObserverShadowCoordinator
    {
        private const float ReconcileIntervalSeconds = 1f;
        private const float SummaryIntervalSeconds = 30f;
        private const int SessionEventLogLimit = 160;
        private const int SessionSummaryLogLimit = 120;
        private const int SessionFaultLogLimit = 16;

        private sealed class ConnectionIdentity
        {
            internal object SteamPlayerReference;
            internal object TransportReference;
            internal ulong Token;
        }

        private sealed class CaptureResult
        {
            internal readonly List<ObserverShadowSample> Samples = new List<ObserverShadowSample>();
            internal Dictionary<ulong, ConnectionIdentity> StagedConnections;
            internal ulong StagedNextConnectionToken;
            internal bool IsComplete = true;
        }

        private sealed class ValidatedObserver
        {
            internal SteamPlayer SteamPlayer;
            internal Player Player;
            internal PlayerMovement Movement;
            internal ulong ObserverId;
        }

        private static readonly MultiObserverShadowLedger Ledger = new MultiObserverShadowLedger();
        private static readonly Dictionary<ulong, ConnectionIdentity> Connections =
            new Dictionary<ulong, ConnectionIdentity>();
        private static readonly Dictionary<string, string> LastMismatch =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly ShadowShutdownGate ShutdownGate = new ShadowShutdownGate();
        private static readonly ShadowFaultBackoff FaultBackoff = new ShadowFaultBackoff();

        private static ulong _nextConnectionToken;
        private static float _nextReconcileAt;
        private static float _nextSummaryAt;
        private static int _eventLogCount;
        private static int _summaryLogCount;
        private static int _faultLogCount;
        private static string _hostSessionId;

        internal static ulong SessionEpoch => Ledger.SessionEpoch;
        internal static int ObserverCount => Ledger.ObserverCount;

        internal static void Initialize()
        {
            ShutdownGate.PrepareForInitialize();
        }

        internal static void Tick(bool enabled)
        {
            ThreadUtil.assertIsGameThread();

            if (ShutdownGate.Consume())
            {
                EndSessionIfNeeded("deferred-plugin-shutdown");
                ResetManagedState(resetLogQuotas: true);
                _hostSessionId = null;
                return;
            }
            if (ShutdownGate.IsLatched) return;

            float now = Time.realtimeSinceStartup;
            if (FaultBackoff.IsFaulted)
            {
                if (!FaultBackoff.TryBeginRecovery(now)) return;
                EndSessionIfNeeded("fault-recovery");
                ResetManagedState(resetLogQuotas: false);
                SafeInfo($"fault-recovery attempt={FaultBackoff.Attempt}");
            }

            bool active = enabled && HostManager.ShouldProcessClientHostListen();
            if (!active)
            {
                EndSessionIfNeeded(enabled ? "listen-host-inactive" : "disabled");
                ResetManagedState(resetLogQuotas: true);
                return;
            }

            if (now < _nextReconcileAt) return;
            _nextReconcileAt = now + ReconcileIntervalSeconds;

            string currentSessionId = HostManager.CurrentSessionId;
            if (string.IsNullOrEmpty(currentSessionId)
                || string.IsNullOrEmpty(_hostSessionId)
                || !string.Equals(_hostSessionId, currentSessionId, StringComparison.Ordinal))
            {
                SafeMismatch(
                    "host-session-identity",
                    $"host session identity unavailable or mismatched; notified={MaskSession(_hostSessionId)} " +
                    $"current={MaskSession(currentSessionId)}; shadow reconcile suppressed");
                return;
            }
            ClearMismatch("host-session-identity");

            string worldIdentity = BuildWorldIdentity(currentSessionId);
            if (Ledger.BeginSession(worldIdentity))
            {
                Connections.Clear();
                LastMismatch.Clear();
                _eventLogCount = 0;
                _summaryLogCount = 0;
                _faultLogCount = 0;
                _nextSummaryAt = now;
                SafeInfo($"session-begin epoch={Ledger.SessionEpoch} world={worldIdentity}");
            }

            CaptureResult capture = CaptureSamples();
            if (!capture.IsComplete)
            {
                SafeMismatch("capture-incomplete", "capture-incomplete; entire shadow reconcile suppressed");
                return;
            }
            CommitConnectionIdentities(capture);
            ClearMismatch("capture-incomplete");

            IReadOnlyList<ShadowTransition> transitions =
                Ledger.Reconcile(
                    capture.Samples,
                    Regions.WORLD_SIZE,
                    ItemManager.ITEM_REGIONS);
            foreach (ShadowTransition transition in transitions)
            {
                SafeEvent(
                    $"transition={transition.Kind} observer={Mask(transition.ObserverId)} {transition.Detail}");
            }

            AuditNativeState();
            if (now >= _nextSummaryAt)
            {
                _nextSummaryAt = now + SummaryIntervalSeconds;
                int pendingCount = 0;
                IReadOnlyList<ObserverShadowSnapshot> observers = Ledger.SnapshotObservers();
                foreach (ObserverShadowSnapshot observer in observers)
                {
                    if (!observer.GameplayAuthorized) pendingCount++;
                }

                if (_summaryLogCount < SessionSummaryLogLimit)
                {
                    _summaryLogCount++;
                    SafeInfo(
                        $"summary={_summaryLogCount}/{SessionSummaryLogLimit} epoch={Ledger.SessionEpoch} " +
                        $"observers={Ledger.ObserverCount} pendingObservers={pendingCount} " +
                        $"itemDemandRegions={Ledger.ItemDemandRegionCount} " +
                        $"zombieDemandBounds={Ledger.ZombieDemandBoundCount} shadowOnly=true");
                }
            }

            FaultBackoff.MarkSuccess();
        }

        internal static void NotifyHostSessionStarted(string sessionId)
        {
            ThreadUtil.assertIsGameThread();
            if (string.IsNullOrEmpty(sessionId))
                throw new ArgumentException("Host session identity is required.", nameof(sessionId));
            if (!string.Equals(_hostSessionId, sessionId, StringComparison.Ordinal))
            {
                EndSessionIfNeeded("host-session-replaced");
                ResetManagedState(resetLogQuotas: true);
                _hostSessionId = sessionId;
            }
        }

        internal static void NotifyHostSessionEnded(string sessionId, string reason)
        {
            ThreadUtil.assertIsGameThread();
            if (!string.IsNullOrEmpty(_hostSessionId)
                && !string.IsNullOrEmpty(sessionId)
                && !string.Equals(_hostSessionId, sessionId, StringComparison.Ordinal))
            {
                SafeEvent("ignored stale session-end notification");
                return;
            }

            EndSessionIfNeeded("host-session-ended:" + (reason ?? "unknown"));
            ResetManagedState(resetLogQuotas: true);
            _hostSessionId = null;
        }

        internal static void HandleTickFailure(Exception exception)
        {
            try
            {
                float now = Time.realtimeSinceStartup;
                if (!FaultBackoff.RecordFailure(now)) return;
                if (_faultLogCount < SessionFaultLogLimit)
                {
                    _faultLogCount++;
                    SafeWarn($"fault={_faultLogCount}/{SessionFaultLogLimit} attempt={FaultBackoff.Attempt} " +
                        $"retryAt={FaultBackoff.NextRecoveryAt:F1} " +
                        $"type={exception?.GetType().Name ?? "unknown"}; legacy writers unchanged");
                }
            }
            catch { }
        }

        internal static void Shutdown()
        {
            ShutdownGate.Request();
            try
            {
                ThreadUtil.assertIsGameThread();
            }
            catch
            {
                // Never mutate dictionaries off-thread. A future game-thread Tick drains this
                // request; process teardown otherwise reclaims managed state.
                return;
            }

            if (ShutdownGate.Consume())
            {
                EndSessionIfNeeded("plugin-shutdown");
                ResetManagedState(resetLogQuotas: true);
                _hostSessionId = null;
            }
        }

        private static CaptureResult CaptureSamples()
        {
            var result = new CaptureResult();
            var clients = Provider.clients;
            if (clients == null)
            {
                result.IsComplete = false;
                return result;
            }

            SteamPlayer[] snapshot;
            try
            {
                int count = clients.Count;
                if (count > 64) return Incomplete(result, "observer-capacity-exceeded");
                snapshot = new SteamPlayer[count];
                for (int index = 0; index < count; index++) snapshot[index] = clients[index];
                if (clients.Count != count) return Incomplete(result, "client-list-changed-during-copy");
            }
            catch (Exception ex)
            {
                return Incomplete(result, "client-snapshot-error:" + ex.GetType().Name);
            }

            var validated = new List<ValidatedObserver>(snapshot.Length);
            var activeObservers = new HashSet<ulong>();
            for (int index = 0; index < snapshot.Length; index++)
            {
                SteamPlayer steamPlayer = snapshot[index];
                Player player = steamPlayer?.player;
                PlayerMovement movement = player?.movement;
                object transport = steamPlayer?.transportConnection;
                ulong observerId = steamPlayer?.playerID?.steamID.m_SteamID ?? 0UL;
                if (observerId == 0UL
                    || player == null
                    || movement == null
                    || transport == null
                    || movement.loadedRegions == null
                    || !activeObservers.Add(observerId))
                {
                    return Incomplete(result, "invalid-observer-record:index=" + index);
                }
                validated.Add(new ValidatedObserver
                {
                    SteamPlayer = steamPlayer,
                    Player = player,
                    Movement = movement,
                    ObserverId = observerId
                });
            }

            var stagedConnections = new Dictionary<ulong, ConnectionIdentity>();
            ulong stagedNextToken = _nextConnectionToken;
            try
            {
                foreach (ValidatedObserver observer in validated)
                {
                    ulong connectionToken = GetStagedConnectionToken(
                        observer.ObserverId,
                        observer.SteamPlayer,
                        stagedConnections,
                        ref stagedNextToken);
                    CountNativeLoadedItemRegions(
                        observer.Movement,
                        out int nativeLoadedItems,
                        out int nativeStaleLoadedItems);
                    int committedItems = CountCommittedItemRegions(
                        observer.Movement.region_x,
                        observer.Movement.region_y);
                    // Mirror ZombieManager.onBoundUpdated: a player outside all navigation bounds
                    // remains a world observer, but has no functional zombie region demand.
                    bool hasFunctionalZombieBound = LevelNavigation.checkSafe(observer.Movement.bound);
                    bool local = observer.Player.channel != null && observer.Player.channel.IsLocalPlayer;
                    bool authorized = local || !P2PApprovalManager.IsPending(new CSteamID(observer.ObserverId));

                    result.Samples.Add(new ObserverShadowSample(
                        observer.ObserverId,
                        connectionToken,
                        observer.Movement.region_x,
                        observer.Movement.region_y,
                        observer.Movement.bound,
                        hasFunctionalZombieBound,
                        local,
                        authorized,
                        nativeLoadedItems,
                        nativeStaleLoadedItems,
                        committedItems));
                }
            }
            catch (Exception ex)
            {
                return Incomplete(result, "capture-build-error:" + ex.GetType().Name);
            }

            result.StagedConnections = stagedConnections;
            result.StagedNextConnectionToken = stagedNextToken;
            return result;
        }

        private static CaptureResult Incomplete(CaptureResult result, string reason)
        {
            result.IsComplete = false;
            result.Samples.Clear();
            result.StagedConnections = null;
            SafeEvent("capture-rejected reason=" + reason);
            return result;
        }

        private static void AuditNativeState()
        {
            IReadOnlyList<ObserverShadowSnapshot> observers = Ledger.SnapshotObservers();
            foreach (ObserverShadowSnapshot observer in observers)
            {
                int desiredCount = observer.ItemRegionCount;
                if (!observer.IsLocalPlayer && observer.NativeLoadedItemRegions != desiredCount)
                {
                    SafeMismatch(
                        $"item-loaded:{observer.ObserverId}",
                        $"observer={Mask(observer.ObserverId)} desired={desiredCount} " +
                        $"nativeLoaded={observer.NativeLoadedItemRegions} committed={observer.CommittedItemRegions}");
                }
                else
                {
                    ClearMismatch($"item-loaded:{observer.ObserverId}");
                }

                if (observer.NativeStaleLoadedItemRegions > 0)
                {
                    SafeMismatch(
                        $"item-stale:{observer.ObserverId}",
                        $"observer={Mask(observer.ObserverId)} staleLoadedOutsideDesired=" +
                        observer.NativeStaleLoadedItemRegions);
                }
                else
                {
                    ClearMismatch($"item-stale:{observer.ObserverId}");
                }

                if (observer.CommittedItemRegions != desiredCount)
                {
                    SafeMismatch(
                        $"item-authority:{observer.ObserverId}",
                        $"observer={Mask(observer.ObserverId)} desired={desiredCount} " +
                        $"authorityCommitted={observer.CommittedItemRegions}");
                }
                else
                {
                    ClearMismatch($"item-authority:{observer.ObserverId}");
                }
            }

            IReadOnlyDictionary<byte, int> demand = Ledger.SnapshotZombieDemand();
            var activeZombieMismatchKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (KeyValuePair<byte, int> pair in demand)
            {
                string mismatchKey = $"zombie-demand:{pair.Key}";
                activeZombieMismatchKeys.Add(mismatchKey);
                int nativeCount = ReadNativeZombiePlayerCount(pair.Key);
                if (nativeCount != pair.Value)
                {
                    SafeMismatch(
                        mismatchKey,
                        $"bound={pair.Key} observerDemand={pair.Value} nativePlayerCount={nativeCount}");
                }
                else
                {
                    ClearMismatch(mismatchKey);
                }
            }

            var staleZombieKeys = new List<string>();
            foreach (string key in LastMismatch.Keys)
            {
                if (key.StartsWith("zombie-demand:", StringComparison.Ordinal)
                    && !activeZombieMismatchKeys.Contains(key))
                {
                    staleZombieKeys.Add(key);
                }
            }
            foreach (string key in staleZombieKeys) ClearMismatch(key);
        }

        private static ulong GetStagedConnectionToken(
            ulong observerId,
            SteamPlayer steamPlayer,
            Dictionary<ulong, ConnectionIdentity> stagedConnections,
            ref ulong stagedNextToken)
        {
            object transport = steamPlayer.transportConnection;
            if (Connections.TryGetValue(observerId, out ConnectionIdentity identity)
                && ReferenceEquals(identity.SteamPlayerReference, steamPlayer)
                && ReferenceEquals(identity.TransportReference, transport))
            {
                stagedConnections[observerId] = identity;
                return identity.Token;
            }

            stagedNextToken = stagedNextToken == ulong.MaxValue ? 1UL : stagedNextToken + 1UL;
            if (stagedNextToken == 0UL) stagedNextToken = 1UL;
            identity = new ConnectionIdentity
            {
                SteamPlayerReference = steamPlayer,
                TransportReference = transport,
                Token = stagedNextToken
            };
            stagedConnections[observerId] = identity;
            return identity.Token;
        }

        private static void CountNativeLoadedItemRegions(
            PlayerMovement movement,
            out int desiredLoaded,
            out int staleLoaded)
        {
            LoadedRegion[,] loadedRegions = movement.loadedRegions;
            if (loadedRegions == null) throw new InvalidOperationException("loadedRegions unavailable");

            desiredLoaded = 0;
            staleLoaded = 0;
            int radius = ItemManager.ITEM_REGIONS;
            int maxX = Math.Min(loadedRegions.GetLength(0), Regions.WORLD_SIZE);
            int maxY = Math.Min(loadedRegions.GetLength(1), Regions.WORLD_SIZE);
            for (int x = 0; x < maxX; x++)
            {
                for (int y = 0; y < maxY; y++)
                {
                    LoadedRegion region = loadedRegions[x, y];
                    if (region == null || !region.isItemsLoaded) continue;
                    bool desired = Math.Abs(x - movement.region_x) <= radius
                        && Math.Abs(y - movement.region_y) <= radius;
                    if (desired) desiredLoaded++;
                    else staleLoaded++;
                }
            }
        }

        private static int CountCommittedItemRegions(byte centerX, byte centerY)
        {
            int count = 0;
            int radius = ItemManager.ITEM_REGIONS;
            for (int x = centerX - radius; x <= centerX + radius; x++)
            {
                for (int y = centerY - radius; y <= centerY + radius; y++)
                {
                    if (!Regions.checkSafe(x, y)) continue;
                    if (AuthoritativeItemGenerationGatePatch.GetStateForShadow((byte)x, (byte)y)
                        == RegionGenerationState.Committed)
                    {
                        count++;
                    }
                }
            }
            return count;
        }

        private static int ReadNativeZombiePlayerCount(byte bound)
        {
            ZombieRegion[] regions = ZombieManager.regions;
            if (regions == null || bound >= regions.Length || regions[bound] == null) return 0;
            return regions[bound].PlayerCountInRegion;
        }

        private static string BuildWorldIdentity(string sessionId)
        {
            string serverId = Provider.serverID ?? string.Empty;
            string map = Provider.map ?? string.Empty;
            return sessionId + "|" + serverId + "|" + map;
        }

        private static void CommitConnectionIdentities(CaptureResult capture)
        {
            var removed = new List<ulong>();
            foreach (ulong observerId in Connections.Keys)
            {
                if (!capture.StagedConnections.ContainsKey(observerId)) removed.Add(observerId);
            }
            Connections.Clear();
            foreach (KeyValuePair<ulong, ConnectionIdentity> pair in capture.StagedConnections)
                Connections.Add(pair.Key, pair.Value);
            _nextConnectionToken = capture.StagedNextConnectionToken;
            foreach (ulong observerId in removed)
                RemoveObserverMismatch(observerId);
        }

        private static void EndSessionIfNeeded(string reason)
        {
            if (!Ledger.EndSession()) return;
            SafeInfo($"session-end nextEpoch={Ledger.SessionEpoch} reason={reason}");
        }

        private static void ResetManagedState(bool resetLogQuotas)
        {
            Connections.Clear();
            LastMismatch.Clear();
            if (resetLogQuotas)
            {
                _eventLogCount = 0;
                _summaryLogCount = 0;
                _faultLogCount = 0;
            }
            _nextReconcileAt = 0f;
            _nextSummaryAt = 0f;
        }

        private static void SafeMismatch(string key, string detail)
        {
            if (LastMismatch.TryGetValue(key, out string previous)
                && string.Equals(previous, detail, StringComparison.Ordinal))
            {
                return;
            }
            LastMismatch[key] = detail;
            SafeEvent("mismatch " + detail);
        }

        private static void ClearMismatch(string key)
        {
            if (LastMismatch.Remove(key)) SafeEvent("resolved key=" + key);
        }

        private static void RemoveObserverMismatch(ulong observerId)
        {
            string suffix = ":" + observerId;
            var removed = new List<string>();
            foreach (string key in LastMismatch.Keys)
            {
                if (key.EndsWith(suffix, StringComparison.Ordinal)) removed.Add(key);
            }
            foreach (string key in removed) LastMismatch.Remove(key);
        }

        private static void SafeEvent(string message)
        {
            if (_eventLogCount >= SessionEventLogLimit) return;
            _eventLogCount++;
            SafeInfo($"event={_eventLogCount}/{SessionEventLogLimit} {message}");
        }

        private static void SafeInfo(string message)
        {
            try { RoleLogger.Info("[Host]", "[MultiObserver/M0] " + message); }
            catch { }
        }

        private static void SafeWarn(string message)
        {
            try { RoleLogger.Warn("[Shared]", "[MultiObserver/M0] " + message); }
            catch { }
        }

        private static string Mask(ulong observerId)
        {
            try { return DiagnosticMaskUtil.MaskSteamId(observerId); }
            catch { return "mask-error"; }
        }

        private static string MaskSession(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId)) return "missing";
            return sessionId.Length <= 8 ? sessionId : sessionId.Substring(0, 8);
        }
    }
}
