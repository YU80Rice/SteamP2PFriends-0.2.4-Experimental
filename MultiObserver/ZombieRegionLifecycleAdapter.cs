using SDG.Unturned;
using SteamP2PFriends.Host;
using SteamP2PFriends.Patches.P0EZombieLifecycle;
using SteamP2PFriends.Shared;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SteamP2PFriends.MultiObserver
{
    internal enum ZombieLifecycleAction : byte
    {
        None = 0,
        Acquire = 1,
        Hold = 2,
        ScheduleRelease = 3,
        CancelRelease = 4,
        CommitRelease = 5,
        QuarantineMismatch = 6
    }

    internal readonly struct ZombieReleaseLease
    {
        internal ZombieReleaseLease(ulong sessionEpoch, byte bound, uint regionGeneration, float deadline)
        {
            SessionEpoch = sessionEpoch;
            Bound = bound;
            RegionGeneration = regionGeneration;
            Deadline = deadline;
        }

        internal ulong SessionEpoch { get; }
        internal byte Bound { get; }
        internal uint RegionGeneration { get; }
        internal float Deadline { get; }
    }

    internal sealed class ZombieRegionLifecycleLedger
    {
        private readonly Dictionary<byte, uint> _generations = new Dictionary<byte, uint>();
        private readonly Dictionary<byte, ZombieReleaseLease> _releases = new Dictionary<byte, ZombieReleaseLease>();
        private readonly HashSet<byte> _quarantined = new HashSet<byte>();

        internal ulong SessionEpoch { get; private set; }
        internal int PendingReleaseCount => _releases.Count;
        internal int QuarantinedBoundCount => _quarantined.Count;

        internal void BeginSession(ulong epoch)
        {
            if (epoch == 0UL) throw new ArgumentOutOfRangeException(nameof(epoch));
            if (SessionEpoch == epoch) return;
            SessionEpoch = epoch;
            _generations.Clear();
            _releases.Clear();
            _quarantined.Clear();
        }

        internal uint GetGeneration(byte bound) =>
            _generations.TryGetValue(bound, out uint generation) ? generation : 0U;

        internal uint CommitAcquire(byte bound)
        {
            CancelRelease(bound);
            uint next = GetGeneration(bound);
            next = next == uint.MaxValue ? 1U : next + 1U;
            if (next == 0U) next = 1U;
            _generations[bound] = next;
            return next;
        }

        internal ZombieLifecycleAction CompareDemand(byte bound, int nativeDemand, int observerDemand)
        {
            if (nativeDemand < 0 || observerDemand < 0)
                throw new ArgumentOutOfRangeException("Demand cannot be negative.");
            if (nativeDemand == observerDemand)
            {
                _quarantined.Remove(bound);
                return ZombieLifecycleAction.None;
            }
            _quarantined.Add(bound);
            return ZombieLifecycleAction.QuarantineMismatch;
        }

        internal bool IsQuarantined(byte bound) => _quarantined.Contains(bound);

        internal ZombieReleaseLease ScheduleRelease(byte bound, float now, float hysteresisSeconds)
        {
            var lease = new ZombieReleaseLease(
                SessionEpoch,
                bound,
                GetGeneration(bound),
                now + Math.Max(0f, hysteresisSeconds));
            _releases[bound] = lease;
            return lease;
        }

        internal bool CancelRelease(byte bound) => _releases.Remove(bound);

        internal bool TryGetRelease(byte bound, out ZombieReleaseLease lease) =>
            _releases.TryGetValue(bound, out lease);

        internal bool CanCommitRelease(in ZombieReleaseLease lease, float now, int nativeDemand)
        {
            return lease.SessionEpoch == SessionEpoch
                && lease.RegionGeneration == GetGeneration(lease.Bound)
                && !IsQuarantined(lease.Bound)
                && now >= lease.Deadline
                && nativeDemand == 0;
        }

        internal bool CommitRelease(in ZombieReleaseLease lease)
        {
            if (!_releases.TryGetValue(lease.Bound, out ZombieReleaseLease current)) return false;
            if (current.SessionEpoch != lease.SessionEpoch
                || current.RegionGeneration != lease.RegionGeneration
                || current.Deadline != lease.Deadline) return false;
            return _releases.Remove(lease.Bound);
        }

        internal IReadOnlyList<ZombieReleaseLease> SnapshotReleases() =>
            new List<ZombieReleaseLease>(_releases.Values);
    }

    /// <summary>
    /// M3 owner for listen-host zombie region generation and final release. Vanilla
    /// PlayerCountInRegion remains the primary demand signal. M0 observer demand is a
    /// diagnostic cross-check and never rewrites the native counter in the same frame.
    /// </summary>
    internal static class ZombieRegionLifecycleAdapter
    {
        private const float ReleaseHysteresisSeconds = 2f;
        private const int LogLimit = 48;
        private static readonly ZombieRegionLifecycleLedger Ledger = new ZombieRegionLifecycleLedger();
        private static readonly Dictionary<byte, ZombieRegion> RegionIdentity = new Dictionary<byte, ZombieRegion>();
        private static bool _registrationReady;
        private static int _logCount;
        private static float _nextReconcileAt;

        internal static bool IsReady => _registrationReady;
        internal static int PendingReleaseCount => Ledger.PendingReleaseCount;

        internal static void SetRegistrationReady(bool ready)
        {
            _registrationReady = ready;
            if (!ready) ResetForSession();
        }

        internal static void ResetForSession()
        {
            RegionIdentity.Clear();
            _logCount = 0;
            _nextReconcileAt = 0f;
        }

        internal static void Tick()
        {
            ThreadUtil.assertIsGameThread();
            if (!_registrationReady || !HostManager.ShouldProcessClientHostListen()) return;
            EnsureSession();

            float now = Time.realtimeSinceStartup;
            if (now >= _nextReconcileAt)
            {
                _nextReconcileAt = now + 1f;
                ReconcileNativeRegions(now);
            }
            foreach (ZombieReleaseLease lease in Ledger.SnapshotReleases())
            {
                if (Ledger.IsQuarantined(lease.Bound))
                {
                    Ledger.CancelRelease(lease.Bound);
                    SafeWarn($"release-cancel bound={lease.Bound} reason=demand-quarantined");
                    continue;
                }
                if (!TryGetRegion(lease.Bound, out ZombieRegion region)
                    || !RegionIdentity.TryGetValue(lease.Bound, out ZombieRegion expected)
                    || !ReferenceEquals(region, expected))
                {
                    Ledger.CancelRelease(lease.Bound);
                    SafeWarn($"release-cancel bound={lease.Bound} reason=region-identity-changed");
                    continue;
                }

                int nativeDemand = Math.Max(0, region.PlayerCountInRegion);
                if (nativeDemand > 0)
                {
                    Ledger.CancelRelease(lease.Bound);
                    SafeInfo($"release-cancel bound={lease.Bound} generation={lease.RegionGeneration} demand={nativeDemand}");
                    continue;
                }
                if (!Ledger.CanCommitRelease(lease, now, nativeDemand)) continue;

                region.destroy();
                region.isNetworked = false;
                Ledger.CommitRelease(lease);
                SafeInfo($"release-commit bound={lease.Bound} generation={lease.RegionGeneration} demand=0 hysteresis={ReleaseHysteresisSeconds:0.0}s");
            }
        }

        internal static bool TryAcquireForRemote(Player player, byte oldBound, byte newBound)
        {
            ThreadUtil.assertIsGameThread();
            if (!_registrationReady || !IsListenHostRemote(player)) return false;
            EnsureSession();
            if (!TryGetRegion(newBound, out ZombieRegion region)) return false;

            ObserveDemand(newBound, region.PlayerCountInRegion);
            Ledger.CancelRelease(newBound);
            if (region.isNetworked) return false;
            if (player?.movement?.loadedBounds == null
                || newBound >= player.movement.loadedBounds.Length
                || player.movement.loadedBounds[newBound] == null
                || player.movement.loadedBounds[newBound].isZombiesLoaded) return false;
            if (ZombieManager.instance == null) return false;

            int before = region.zombies?.Count ?? -1;
            ZombieManager.instance.generateZombies(newBound);
            if (!TryGetRegion(newBound, out ZombieRegion verified) || !ReferenceEquals(region, verified))
                throw new InvalidOperationException("Zombie region changed during acquire.");
            region.isNetworked = true;
            uint generation = Ledger.CommitAcquire(newBound);
            RegionIdentity[newBound] = region;
            SafeInfo($"acquire-commit bound={newBound} generation={generation} source=remote-0to1 zombies={before}->{region.zombies?.Count ?? -1} oldBound={oldBound}");
            return true;
        }

        internal static void BeginLocalTransition(Player player, byte oldBound, byte newBound, ref ZombieLifecycleState state)
        {
            ThreadUtil.assertIsGameThread();
            state = default;
            if (!_registrationReady || !IsListenHostPlayer(player)) return;
            EnsureSession();
            state.sessionEpoch = Ledger.SessionEpoch;

            if (TryGetRegion(oldBound, out ZombieRegion oldRegion) && oldRegion.isNetworked)
            {
                int nativeDemand = Math.Max(0, oldRegion.PlayerCountInRegion);
                ObserveDemand(oldBound, nativeDemand);
                state.oldBound = oldBound;
                state.oldOriginalIsNetworked = true;
                state.oldRegionGeneration = Ledger.GetGeneration(oldBound);
                state.oldRegionIdentity = oldRegion;
                state.oldWasTracked = true;
                if (player.channel.IsLocalPlayer)
                {
                    oldRegion.isNetworked = false;
                    state.oldWasModified = true;
                }
            }

            if (player.channel.IsLocalPlayer && TryGetRegion(newBound, out ZombieRegion newRegion))
            {
                Ledger.CancelRelease(newBound);
                LoadedBound[] loaded = player.movement?.loadedBounds;
                if (newRegion.isNetworked && loaded != null && newBound < loaded.Length
                    && loaded[newBound] != null && !loaded[newBound].isZombiesLoaded)
                {
                    state.newBound = newBound;
                    state.newOriginalIsZombiesLoaded = false;
                    state.newRegionGeneration = Ledger.GetGeneration(newBound);
                    state.newRegionIdentity = newRegion;
                    loaded[newBound].isZombiesLoaded = true;
                    state.newWasModified = true;
                }
            }
        }

        internal static void CompleteLocalTransition(Player player, byte newBound, ref ZombieLifecycleState state, Exception exception)
        {
            ThreadUtil.assertIsGameThread();
            if (state.oldWasTracked) RestoreOldAndPlanRelease(ref state);
            if (exception != null && state.newWasModified) RollbackNewLoaded(player, ref state);

            if (exception == null && TryGetRegion(newBound, out ZombieRegion newRegion) && newRegion.isNetworked)
            {
                if (!RegionIdentity.TryGetValue(newBound, out ZombieRegion known) || !ReferenceEquals(known, newRegion))
                {
                    uint generation = Ledger.CommitAcquire(newBound);
                    RegionIdentity[newBound] = newRegion;
                    SafeInfo($"acquire-observed bound={newBound} generation={generation} source=local-vanilla");
                }
                ObserveDemand(newBound, newRegion.PlayerCountInRegion);
            }
        }

        private static void RestoreOldAndPlanRelease(ref ZombieLifecycleState state)
        {
            if (state.sessionEpoch != Ledger.SessionEpoch) return;
            if (!TryGetRegion(state.oldBound, out ZombieRegion region)
                || !ReferenceEquals(region, state.oldRegionIdentity)
                || Ledger.GetGeneration(state.oldBound) != state.oldRegionGeneration) return;

            if (state.oldWasModified)
                region.isNetworked = state.oldOriginalIsNetworked;
            int nativeDemand = Math.Max(0, region.PlayerCountInRegion);
            ObserveDemand(state.oldBound, nativeDemand);
            if (nativeDemand == 0)
            {
                RegionIdentity[state.oldBound] = region;
                ZombieReleaseLease lease = Ledger.ScheduleRelease(state.oldBound, Time.realtimeSinceStartup, ReleaseHysteresisSeconds);
                SafeInfo($"release-scheduled bound={state.oldBound} generation={lease.RegionGeneration} deadline={lease.Deadline:0.000}");
            }
        }

        private static void RollbackNewLoaded(Player player, ref ZombieLifecycleState state)
        {
            LoadedBound[] loaded = player?.movement?.loadedBounds;
            if (loaded == null || state.newBound >= loaded.Length || loaded[state.newBound] == null) return;
            if (state.sessionEpoch != Ledger.SessionEpoch) return;
            if (!TryGetRegion(state.newBound, out ZombieRegion region)
                || !ReferenceEquals(region, state.newRegionIdentity)
                || Ledger.GetGeneration(state.newBound) != state.newRegionGeneration) return;
            loaded[state.newBound].isZombiesLoaded = state.newOriginalIsZombiesLoaded;
        }

        private static void ObserveDemand(byte bound, int nativeDemand)
        {
            int observerDemand = MultiObserverShadowCoordinator.GetZombieDemandCount(bound);
            if (Ledger.CompareDemand(bound, Math.Max(0, nativeDemand), Math.Max(0, observerDemand))
                == ZombieLifecycleAction.QuarantineMismatch)
            {
                SafeWarn($"demand-mismatch bound={bound} native={nativeDemand} observer={observerDemand} action=quarantine-no-counter-rewrite");
            }
        }

        private static void EnsureSession()
        {
            ulong epoch = MultiObserverShadowCoordinator.SessionEpoch;
            if (epoch == 0UL) epoch = 1UL;
            if (Ledger.SessionEpoch != epoch)
            {
                Ledger.BeginSession(epoch);
                RegionIdentity.Clear();
                SafeInfo($"session-begin epoch={epoch}");
            }
        }

        private static void ReconcileNativeRegions(float now)
        {
            ZombieRegion[] regions = ZombieManager.regions;
            if (regions == null) return;
            for (int index = 0; index < regions.Length; index++)
            {
                ZombieRegion region = regions[index];
                if (region == null) continue;
                byte bound = (byte)index;
                int nativeDemand = Math.Max(0, region.PlayerCountInRegion);
                ObserveDemand(bound, nativeDemand);
                if (Ledger.IsQuarantined(bound))
                {
                    Ledger.CancelRelease(bound);
                    continue;
                }
                if (nativeDemand > 0)
                {
                    Ledger.CancelRelease(bound);
                    if (!region.isNetworked)
                    {
                        if (ZombieManager.instance == null
                            || !RecoverNativeAcquire(bound, region, nativeDemand))
                        {
                            Ledger.CompareDemand(bound, nativeDemand, 0);
                            continue;
                        }
                    }
                    if (!RegionIdentity.TryGetValue(bound, out ZombieRegion known) || !ReferenceEquals(known, region))
                    {
                        Ledger.CommitAcquire(bound);
                        RegionIdentity[bound] = region;
                    }
                    continue;
                }

                if (!region.isNetworked)
                {
                    Ledger.CancelRelease(bound);
                    RegionIdentity.Remove(bound);
                    continue;
                }
                if (!RegionIdentity.TryGetValue(bound, out ZombieRegion expected) || !ReferenceEquals(expected, region))
                {
                    Ledger.CommitAcquire(bound);
                    RegionIdentity[bound] = region;
                }
                if (!Ledger.TryGetRelease(bound, out _))
                {
                    ZombieReleaseLease lease = Ledger.ScheduleRelease(bound, now, ReleaseHysteresisSeconds);
                    SafeInfo($"release-scheduled bound={bound} generation={lease.RegionGeneration} source=native-reconcile");
                }
            }
        }

        private static bool RecoverNativeAcquire(byte bound, ZombieRegion region, int nativeDemand)
        {
            try
            {
                int before = region.zombies?.Count ?? -1;
                ZombieManager.instance.generateZombies(bound);
                ZombieRegion[] regions = ZombieManager.regions;
                if (regions == null || bound >= regions.Length || !ReferenceEquals(region, regions[bound]))
                    throw new InvalidOperationException("Native-demand acquire changed region identity.");
                region.isNetworked = true;
                uint generation = Ledger.CommitAcquire(bound);
                RegionIdentity[bound] = region;
                SafeInfo($"acquire-commit bound={bound} generation={generation} source=native-demand demand={nativeDemand} zombies={before}->{region.zombies?.Count ?? -1}");
                return true;
            }
            catch (Exception ex)
            {
                SafeWarn($"acquire-failed bound={bound} demand={nativeDemand} error={ex.GetType().Name}");
                return false;
            }
        }

        private static bool TryGetRegion(byte bound, out ZombieRegion region)
        {
            region = null;
            if (!LevelNavigation.checkSafe(bound)) return false;
            ZombieRegion[] regions = ZombieManager.regions;
            if (regions == null || bound >= regions.Length) return false;
            region = regions[bound];
            return region != null;
        }

        private static bool IsListenHostRemote(Player player) =>
            player?.channel != null && !player.channel.IsLocalPlayer
            && Provider.isServer && HostManager.ShouldProcessClientHostListen();

        private static bool IsListenHostPlayer(Player player) =>
            player?.channel != null
            && player.movement?.loadedBounds != null
            && Provider.isServer && HostManager.ShouldProcessClientHostListen();

        private static void SafeInfo(string message)
        {
            if (_logCount++ >= LogLimit) return;
            try { RoleLogger.Info("[Host]", "[MultiObserver/M3-Zombie] " + message); } catch { }
        }

        private static void SafeWarn(string message)
        {
            if (_logCount++ >= LogLimit) return;
            try { RoleLogger.Warn("[Shared]", "[MultiObserver/M3-Zombie] " + message); } catch { }
        }

        internal static ZombieRegionLifecycleLedger CreateLedgerForTests() => new ZombieRegionLifecycleLedger();
    }
}
