using SteamP2PFriends.MultiObserver;
using SteamP2PFriends.MultiObserver.SPI;
using SteamP2PFriends.Core.Identity;
using SteamP2PFriends.Shared;
using System;
using System.Collections.Generic;
using System.Reflection;
using SDG.Unturned;

namespace SteamP2PFriends.Adapters.Resource
{
    public sealed class ResourceReleaseRejectedException : InvalidOperationException
    {
        public ResourceReleaseRejectedException(string reason)
            : base("Resource release rejected: " + reason)
        {
            Reason = reason;
        }

        public string Reason { get; }
    }

    public sealed class ResourceAcquireRejectedException : InvalidOperationException
    {
        public ResourceAcquireRejectedException(string reason)
            : base("Resource acquire rejected: " + reason)
        {
            Reason = reason;
        }

        public string Reason { get; }
    }

    public sealed class ResourceGenerationExhaustedException : InvalidOperationException
    {
        public ResourceGenerationExhaustedException(string reason)
            : base("Resource RegionGeneration exhausted.")
        {
            Reason = reason;
        }

        public string Reason { get; }
    }

    public static class ResourceGenerationRules
    {
        public static bool TryAdvance(uint current, out uint next)
        {
            return TryAdvance(current, out next, out _);
        }

        public static bool TryAdvance(uint current, out uint next, out string reason)
        {
            if (current == uint.MaxValue)
            {
                next = current;
                reason = "generation-overflow";
                return false;
            }

            next = current + 1U;
            if (next == 0U)
            {
                reason = "generation-overflow";
                return false;
            }

            reason = "none";
            return true;
        }
    }

    public readonly struct ResourceReleaseLease
    {
        public ResourceReleaseLease(ulong sessionEpoch, RegionKey regionKey, uint regionGeneration, float deadline)
        {
            SessionEpoch = sessionEpoch;
            RegionKey = regionKey;
            RegionGeneration = regionGeneration;
            Deadline = deadline;
        }

        public ulong SessionEpoch { get; }
        public RegionKey RegionKey { get; }
        public uint RegionGeneration { get; }
        public float Deadline { get; }
    }

    /// <summary>
    /// 原生 ResourceManager/ResourceSpawnpoint 状态的最小可逆快照。
    /// 没有完整原生快照时，补偿必须失败闭合，不能用插件 ledger 猜测恢复。
    /// </summary>
    public sealed class ResourceNativeRegionState
    {
        internal ResourceNativeRegionState(
            bool isNetworked,
            ushort respawnResourceIndex,
            int resourceCount,
            HashSet<ushort> deadResourceIndices)
        {
            IsNetworked = isNetworked;
            RespawnResourceIndex = respawnResourceIndex;
            ResourceCount = resourceCount;
            DeadResourceIndices = new HashSet<ushort>(deadResourceIndices ?? new HashSet<ushort>());
        }

        public bool IsNetworked { get; }
        public ushort RespawnResourceIndex { get; }
        public int ResourceCount { get; }
        public IReadOnlyCollection<ushort> DeadResourceIndices { get; }
    }

    public sealed class ResourceRegionLifecycleState
    {
        internal ResourceRegionLifecycleState(
            bool active,
            uint generation,
            bool hasPendingRelease,
            ResourceReleaseLease pendingRelease,
            HashSet<ushort> deadResourceIndices,
            ResourceNativeRegionState nativeState)
        {
            IsActive = active;
            Generation = generation;
            HasPendingRelease = hasPendingRelease;
            PendingRelease = pendingRelease;
            DeadResourceIndices = new HashSet<ushort>(deadResourceIndices ?? new HashSet<ushort>());
            NativeState = nativeState;
        }

        public bool IsActive { get; }
        public uint Generation { get; }
        public bool HasPendingRelease { get; }
        public ResourceReleaseLease PendingRelease { get; }
        public IReadOnlyCollection<ushort> DeadResourceIndices { get; }
        public ResourceNativeRegionState NativeState { get; }
        public bool HasNativeState => NativeState != null;
    }

    public sealed class ResourceRegionLifecycleLedger
    {
        private readonly Dictionary<RegionKey, uint> _generations = new Dictionary<RegionKey, uint>();
        private readonly Dictionary<RegionKey, ResourceReleaseLease> _releases = new Dictionary<RegionKey, ResourceReleaseLease>();
        private readonly HashSet<RegionKey> _activeRegions = new HashSet<RegionKey>();
        private readonly Dictionary<RegionKey, HashSet<ushort>> _deadResources = new Dictionary<RegionKey, HashSet<ushort>>();

        public ulong SessionEpoch { get; private set; } = 1UL;
        public int ActiveRegionCount => _activeRegions.Count;
        public int PendingReleaseCount => _releases.Count;

        public void BeginSession(ulong epoch)
        {
            if (epoch == 0UL) throw new ArgumentOutOfRangeException(nameof(epoch));
            if (SessionEpoch == epoch) return;
            SessionEpoch = epoch;
            _generations.Clear();
            _releases.Clear();
            _activeRegions.Clear();
            _deadResources.Clear();
        }

        public void EndSession()
        {
            _generations.Clear();
            _releases.Clear();
            _activeRegions.Clear();
            _deadResources.Clear();
        }

        public uint GetGeneration(RegionKey regionKey) =>
            _generations.TryGetValue(regionKey, out uint generation) ? generation : 0U;

        public bool IsRegionActive(RegionKey regionKey) => _activeRegions.Contains(regionKey);

        public ResourceRegionLifecycleState CaptureState(RegionKey regionKey)
        {
            bool hasRelease = _releases.TryGetValue(regionKey, out ResourceReleaseLease release);
            return new ResourceRegionLifecycleState(
                _activeRegions.Contains(regionKey),
                GetGeneration(regionKey),
                hasRelease,
                release,
                GetDeadResourceIndices(regionKey),
                null);
        }

        public void RestoreState(RegionKey regionKey, ResourceRegionLifecycleState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));

            _activeRegions.Remove(regionKey);
            _releases.Remove(regionKey);
            _generations[regionKey] = state.Generation;
            if (state.IsActive) _activeRegions.Add(regionKey);
            if (state.HasPendingRelease) _releases[regionKey] = state.PendingRelease;

            _deadResources.Remove(regionKey);
            if (state.DeadResourceIndices.Count > 0)
                _deadResources[regionKey] = new HashSet<ushort>(state.DeadResourceIndices);
        }

        public bool IsRegionActive(byte x, byte y) => IsRegionActive(new RegionKey(x, y));

        public uint CommitAcquire(RegionKey regionKey)
        {
            uint next = NextGeneration(GetGeneration(regionKey));
            CancelRelease(regionKey);
            _activeRegions.Add(regionKey);
            _generations[regionKey] = next;
            return next;
        }

        public bool TryCommitAcquire(
            RegionKey regionKey,
            ulong expectedEpoch,
            uint expectedGeneration,
            out uint committedGeneration,
            out string reason)
        {
            committedGeneration = 0U;
            if (SessionEpoch != expectedEpoch)
            {
                reason = "session-generation-mismatch";
                return false;
            }
            if (GetGeneration(regionKey) != expectedGeneration)
            {
                reason = "current-generation-mismatch";
                return false;
            }
            if (!ResourceGenerationRules.TryAdvance(expectedGeneration, out uint next, out reason))
                return false;

            CancelRelease(regionKey);
            _activeRegions.Add(regionKey);
            _generations[regionKey] = next;
            committedGeneration = next;
            reason = "none";
            return true;
        }

        public ResourceReleaseLease ScheduleRelease(RegionKey regionKey, float now, float hysteresisSeconds)
        {
            var lease = new ResourceReleaseLease(
                SessionEpoch,
                regionKey,
                GetGeneration(regionKey),
                now + Math.Max(0f, hysteresisSeconds));
            _releases[regionKey] = lease;
            return lease;
        }

        public bool CancelRelease(RegionKey regionKey) => _releases.Remove(regionKey);

        public bool TryGetRelease(RegionKey regionKey, out ResourceReleaseLease lease) =>
            _releases.TryGetValue(regionKey, out lease);

        public bool TryCommitRelease(RegionKey regionKey, ulong expectedEpoch, uint expectedGeneration, out uint committedGeneration)
        {
            return TryCommitRelease(regionKey, expectedEpoch, expectedGeneration,
                out committedGeneration, out _);
        }

        public bool TryCommitRelease(RegionKey regionKey, ulong expectedEpoch, uint expectedGeneration,
            out uint committedGeneration, out string reason)
        {
            committedGeneration = 0U;
            if (!_releases.TryGetValue(regionKey, out ResourceReleaseLease lease))
            {
                reason = "no-pending-release";
                return false;
            }
            if (lease.SessionEpoch != expectedEpoch)
            {
                reason = "session-generation-mismatch";
                return false;
            }
            if (lease.RegionGeneration != expectedGeneration)
            {
                reason = "lease-generation-mismatch";
                return false;
            }
            if (GetGeneration(regionKey) != expectedGeneration)
            {
                reason = "current-generation-mismatch";
                return false;
            }

            if (!ResourceGenerationRules.TryAdvance(expectedGeneration, out uint next, out reason))
                return false;
            _releases.Remove(regionKey);
            _activeRegions.Remove(regionKey);
            _generations[regionKey] = next;
            committedGeneration = next;
            reason = "none";
            return true;
        }

        public bool CommitRelease(RegionKey regionKey, ulong expectedEpoch, uint expectedGeneration,
            out uint committedGeneration)
        {
            return CommitRelease(regionKey, expectedEpoch, expectedGeneration,
                out committedGeneration, out _);
        }

        public bool CommitRelease(RegionKey regionKey, ulong expectedEpoch, uint expectedGeneration,
            out uint committedGeneration, out string reason)
        {
            committedGeneration = 0U;
            if (SessionEpoch != expectedEpoch)
            {
                reason = "session-generation-mismatch";
                return false;
            }
            if (!_activeRegions.Contains(regionKey))
            {
                reason = "inactive-region";
                return false;
            }
            if (GetGeneration(regionKey) != expectedGeneration)
            {
                reason = "current-generation-mismatch";
                return false;
            }

            if (!ResourceGenerationRules.TryAdvance(expectedGeneration, out uint next, out reason))
                return false;
            _releases.Remove(regionKey);
            _activeRegions.Remove(regionKey);
            _generations[regionKey] = next;
            committedGeneration = next;
            reason = "none";
            return true;
        }

        public void CleanObserverDisconnect(RegionKey regionKey)
        {
            _releases.Remove(regionKey);
            _activeRegions.Remove(regionKey);
        }

        public uint RecordResourceDead(RegionKey regionKey, ushort index)
        {
            uint next = NextGeneration(GetGeneration(regionKey));
            if (!_deadResources.TryGetValue(regionKey, out var set))
            {
                set = new HashSet<ushort>();
                _deadResources[regionKey] = set;
            }

            set.Add(index);
            _generations[regionKey] = next;
            return next;
        }

        public uint RecordResourceAlive(RegionKey regionKey, ushort index)
        {
            uint next = NextGeneration(GetGeneration(regionKey));
            if (_deadResources.TryGetValue(regionKey, out var set))
            {
                set.Remove(index);
            }

            _generations[regionKey] = next;
            return next;
        }

        public bool IsResourceDead(RegionKey regionKey, ushort index)
        {
            return _deadResources.TryGetValue(regionKey, out var set) && set.Contains(index);
        }

        public HashSet<ushort> GetDeadResourceIndices(RegionKey regionKey)
        {
            if (_deadResources.TryGetValue(regionKey, out var set))
            {
                return new HashSet<ushort>(set);
            }
            return new HashSet<ushort>();
        }

        private static uint NextGeneration(uint current)
        {
            if (!ResourceGenerationRules.TryAdvance(current, out uint next, out string reason))
            {
                throw new ResourceGenerationExhaustedException(reason);
            }
            return next;
        }
    }

    /// <summary>
    /// 树木与矿物资源生命周期协调适配器 (ResourceRegionLifecycleAdapter)
    /// 管理 2D 矩形网格 (byte x, byte y) 资源区域的按需激活、采伐破坏状态与滞回释放。
    /// </summary>
    public static class ResourceRegionLifecycleAdapter
    {
        private static readonly ResourceRegionLifecycleLedger Ledger = new ResourceRegionLifecycleLedger();
        private static readonly object SyncLock = new object();
        private static bool _registrationReady;
        private static ulong _currentSessionEpoch = 1UL;
        public const float DefaultHysteresisSeconds = 2.0f;

        public static bool RegistrationReady => _registrationReady;
        public static ulong CurrentSessionEpoch => _currentSessionEpoch;

        public static void SetRegistrationReady(bool ready)
        {
            lock (SyncLock)
            {
                _registrationReady = ready;
                if (ready)
                {
                    Ledger.BeginSession(_currentSessionEpoch);
                }
            }
        }

        public static void BeginSession(ulong sessionEpoch)
        {
            lock (SyncLock)
            {
                _currentSessionEpoch = sessionEpoch == 0UL ? 1UL : sessionEpoch;
                Ledger.BeginSession(_currentSessionEpoch);
            }
        }

        public static void EndSession()
        {
            lock (SyncLock)
            {
                Ledger.EndSession();
            }
        }

        public static bool IsRegionActive(byte x, byte y)
        {
            lock (SyncLock)
            {
                return Ledger.IsRegionActive(x, y);
            }
        }

        public static ResourceRegionLifecycleState CaptureRegionState(RegionKey regionKey)
        {
            lock (SyncLock)
            {
                ResourceNativeRegionState nativeState = CaptureNativeRegionState(regionKey);
                ResourceRegionLifecycleState ledgerState = Ledger.CaptureState(regionKey);
                return new ResourceRegionLifecycleState(
                    ledgerState.IsActive,
                    ledgerState.Generation,
                    ledgerState.HasPendingRelease,
                    ledgerState.PendingRelease,
                    new HashSet<ushort>(ledgerState.DeadResourceIndices),
                    nativeState);
            }
        }

        public static void RestoreRegionState(RegionKey regionKey, ResourceRegionLifecycleState state)
        {
            lock (SyncLock)
            {
                if (!state.HasNativeState)
                    throw new InvalidOperationException("Resource native region state is unavailable; restore refused.");
                RestoreNativeRegionState(regionKey, state.NativeState);
                Ledger.RestoreState(regionKey, state);
            }
        }

        private static ResourceNativeRegionState CaptureNativeRegionState(RegionKey regionKey)
        {
            Array regions = GetNativeRegions();
            object region = GetNativeRegion(regions, regionKey);
            FieldInfo networkedField = GetRequiredField(region, "isNetworked");
            FieldInfo respawnIndexField = GetRequiredField(region, "respawnResourceIndex");
            List<ResourceSpawnpoint> trees = LevelGround.GetTreesOrNullInRegion(regionKey.X, regionKey.Y);
            if (trees == null)
                throw new InvalidOperationException("native-resource-trees-unavailable");

            var dead = new HashSet<ushort>();
            for (ushort index = 0; index < trees.Count; index++)
            {
                ResourceSpawnpoint tree = trees[index];
                if (tree == null)
                    throw new InvalidOperationException("native-resource-tree-null index=" + index);
                if (tree.isDead) dead.Add(index);
            }

            return new ResourceNativeRegionState(
                (bool)networkedField.GetValue(region),
                (ushort)respawnIndexField.GetValue(region),
                trees.Count,
                dead);
        }

        private static void RestoreNativeRegionState(
            RegionKey regionKey,
            ResourceNativeRegionState state)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));

            Array regions = GetNativeRegions();
            object region = GetNativeRegion(regions, regionKey);
            FieldInfo networkedField = GetRequiredField(region, "isNetworked");
            FieldInfo respawnIndexField = GetRequiredField(region, "respawnResourceIndex");
            List<ResourceSpawnpoint> trees = LevelGround.GetTreesOrNullInRegion(regionKey.X, regionKey.Y);
            if (trees == null)
                throw new InvalidOperationException("native-resource-trees-unavailable");

            ValidateNativeRegionState(region, networkedField, respawnIndexField, trees, state);
            ResourceNativeRegionState before = CaptureNativeRegionState(regionKey);
            try
            {
                ApplyNativeRegionState(region, networkedField, respawnIndexField, trees, state);
            }
            catch (Exception ex)
            {
                try
                {
                    ValidateNativeRegionState(region, networkedField, respawnIndexField, trees, before);
                    ApplyNativeRegionState(region, networkedField, respawnIndexField, trees, before);
                }
                catch (Exception rollbackEx)
                {
                    throw new InvalidOperationException(
                        "native-resource-restore-rollback-failed original=" + ex.GetType().Name +
                        " rollback=" + rollbackEx.GetType().Name, rollbackEx);
                }
                throw;
            }
        }

        private static void ValidateNativeRegionState(
            object region,
            FieldInfo networkedField,
            FieldInfo respawnIndexField,
            List<ResourceSpawnpoint> trees,
            ResourceNativeRegionState state)
        {
            if (region == null || networkedField == null || respawnIndexField == null)
                throw new InvalidOperationException("native-resource-region-fields-unavailable");
            if (trees == null)
                throw new InvalidOperationException("native-resource-trees-unavailable");
            if (trees.Count != state.ResourceCount)
                throw new InvalidOperationException("native-resource-tree-count-mismatch expected=" +
                    state.ResourceCount + " actual=" + trees.Count);
            foreach (ushort deadIndex in state.DeadResourceIndices)
            {
                if (deadIndex >= trees.Count)
                    throw new InvalidOperationException("native-resource-dead-index-out-of-range index=" + deadIndex);
            }
            for (int index = 0; index < trees.Count; index++)
            {
                if (trees[index] == null)
                    throw new InvalidOperationException("native-resource-tree-null index=" + index);
            }
        }

        private static void ApplyNativeRegionState(
            object region,
            FieldInfo networkedField,
            FieldInfo respawnIndexField,
            List<ResourceSpawnpoint> trees,
            ResourceNativeRegionState state)
        {
            networkedField.SetValue(region, state.IsNetworked);
            respawnIndexField.SetValue(region, state.RespawnResourceIndex);
            for (ushort index = 0; index < trees.Count; index++)
            {
                ResourceSpawnpoint tree = trees[index];
                bool shouldBeDead = Contains(state.DeadResourceIndices, index);
                if (tree.isDead != shouldBeDead)
                {
                    if (shouldBeDead) tree.wipe();
                    else tree.revive();
                }
            }
        }

        private static Array GetNativeRegions()
        {
            FieldInfo field = typeof(ResourceManager).GetField("regions",
                BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public);
            if (field == null)
                throw new InvalidOperationException("native-resource-regions-field-missing");
            Array regions = field.GetValue(null) as Array;
            if (regions == null || regions.Rank != 2)
                throw new InvalidOperationException("native-resource-regions-unavailable");
            return regions;
        }

        private static object GetNativeRegion(Array regions, RegionKey regionKey)
        {
            if (regionKey.X >= regions.GetLength(0) || regionKey.Y >= regions.GetLength(1))
                throw new InvalidOperationException("native-resource-region-out-of-range region=" + regionKey);
            object region = regions.GetValue(regionKey.X, regionKey.Y);
            if (region == null)
                throw new InvalidOperationException("native-resource-region-null region=" + regionKey);
            return region;
        }

        private static FieldInfo GetRequiredField(object instance, string name)
        {
            FieldInfo field = instance.GetType().GetField(name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field == null)
                throw new InvalidOperationException("native-resource-field-missing name=" + name);
            return field;
        }

        private static bool Contains(IReadOnlyCollection<ushort> values, ushort value)
        {
            foreach (ushort item in values)
                if (item == value) return true;
            return false;
        }

        public static uint OnObserverAcquire(RegionKey regionKey)
        {
            lock (SyncLock)
            {
                return Ledger.CommitAcquire(regionKey);
            }
        }

        public static bool TryCommitAcquire(
            RegionKey regionKey,
            ulong sessionEpoch,
            uint expectedGeneration,
            out uint committedGeneration,
            out string reason)
        {
            lock (SyncLock)
            {
                return Ledger.TryCommitAcquire(
                    regionKey, sessionEpoch, expectedGeneration,
                    out committedGeneration, out reason);
            }
        }

        public static bool CancelRelease(RegionKey regionKey)
        {
            lock (SyncLock)
            {
                return Ledger.CancelRelease(regionKey);
            }
        }

        public static bool TryCommitRelease(RegionKey regionKey, ulong sessionEpoch, uint generation, out uint committedGeneration)
        {
            lock (SyncLock)
            {
                return Ledger.TryCommitRelease(regionKey, sessionEpoch, generation, out committedGeneration);
            }
        }

        public static bool TryCommitRelease(RegionKey regionKey, ulong sessionEpoch, uint generation,
            out uint committedGeneration, out string reason)
        {
            lock (SyncLock)
            {
                return Ledger.TryCommitRelease(regionKey, sessionEpoch, generation,
                    out committedGeneration, out reason);
            }
        }

        public static bool CommitRelease(RegionKey regionKey, ulong sessionEpoch, uint generation,
            out uint committedGeneration)
        {
            lock (SyncLock)
            {
                return Ledger.CommitRelease(regionKey, sessionEpoch, generation, out committedGeneration);
            }
        }

        public static bool CommitRelease(RegionKey regionKey, ulong sessionEpoch, uint generation,
            out uint committedGeneration, out string reason)
        {
            lock (SyncLock)
            {
                return Ledger.CommitRelease(regionKey, sessionEpoch, generation,
                    out committedGeneration, out reason);
            }
        }

        public static uint GetGeneration(RegionKey regionKey)
        {
            lock (SyncLock)
            {
                return Ledger.GetGeneration(regionKey);
            }
        }

        public static uint RecordResourceDead(byte x, byte y, ushort index)
        {
            RegionKey regionKey = new RegionKey(x, y);
            lock (SyncLock)
            {
                return Ledger.RecordResourceDead(regionKey, index);
            }
        }

        public static uint RecordResourceAlive(byte x, byte y, ushort index)
        {
            RegionKey regionKey = new RegionKey(x, y);
            lock (SyncLock)
            {
                return Ledger.RecordResourceAlive(regionKey, index);
            }
        }

        public static bool IsResourceDead(byte x, byte y, ushort index)
        {
            RegionKey regionKey = new RegionKey(x, y);
            lock (SyncLock)
            {
                return Ledger.IsResourceDead(regionKey, index);
            }
        }

        public static HashSet<ushort> GetDeadResourceIndices(byte x, byte y)
        {
            RegionKey regionKey = new RegionKey(x, y);
            lock (SyncLock)
            {
                return Ledger.GetDeadResourceIndices(regionKey);
            }
        }

        public static void OnObserverDisconnect(RegionKey regionKey)
        {
            lock (SyncLock)
            {
                Ledger.CleanObserverDisconnect(regionKey);
            }
        }

        public static void Tick()
        {
            // 周期性生命周期维护
        }
    }
}
