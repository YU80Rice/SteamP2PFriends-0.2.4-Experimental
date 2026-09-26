using SteamP2PFriends.MultiObserver;
using SteamP2PFriends.MultiObserver.SPI;
using SteamP2PFriends.Core.Identity;
using SteamP2PFriends.Shared;
using SDG.Unturned;
using UnityEngine;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace SteamP2PFriends.Adapters.Collision
{
    public readonly struct CollisionReleaseLease
    {
        public CollisionReleaseLease(ulong sessionEpoch, RegionKey regionKey, uint regionGeneration, float deadline)
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

    public sealed class LevelObjectCollisionLedger
    {
        private readonly Dictionary<RegionKey, uint> _generations = new Dictionary<RegionKey, uint>();
        private readonly Dictionary<RegionKey, CollisionReleaseLease> _releases = new Dictionary<RegionKey, CollisionReleaseLease>();
        private readonly HashSet<RegionKey> _activeRegions = new HashSet<RegionKey>();

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
        }

        public void EndSession()
        {
            _generations.Clear();
            _releases.Clear();
            _activeRegions.Clear();
        }

        public void RestoreRegionState(RegionKey regionKey, bool active, uint generation)
        {
            _releases.Remove(regionKey);
            if (active) _activeRegions.Add(regionKey);
            else _activeRegions.Remove(regionKey);
            if (generation == 0U) _generations.Remove(regionKey);
            else _generations[regionKey] = generation;
        }

        public uint GetGeneration(RegionKey regionKey) =>
            _generations.TryGetValue(regionKey, out uint generation) ? generation : 0U;

        public bool IsRegionActive(RegionKey regionKey) => _activeRegions.Contains(regionKey);

        public bool IsRegionActive(byte x, byte y) => IsRegionActive(new RegionKey(x, y));

        public uint CommitAcquire(RegionKey regionKey)
        {
            CancelRelease(regionKey);
            _activeRegions.Add(regionKey);
            uint next = GetGeneration(regionKey);
            next = next == uint.MaxValue ? 1U : next + 1U;
            if (next == 0U) next = 1U;
            _generations[regionKey] = next;
            return next;
        }

        public CollisionReleaseLease ScheduleRelease(RegionKey regionKey, float now, float hysteresisSeconds)
        {
            var lease = new CollisionReleaseLease(
                SessionEpoch,
                regionKey,
                GetGeneration(regionKey),
                now + Math.Max(0f, hysteresisSeconds));
            _releases[regionKey] = lease;
            return lease;
        }

        public bool CancelRelease(RegionKey regionKey) => _releases.Remove(regionKey);

        public bool TryGetRelease(RegionKey regionKey, out CollisionReleaseLease lease) =>
            _releases.TryGetValue(regionKey, out lease);

        public bool TryCommitRelease(RegionKey regionKey, ulong expectedEpoch, uint expectedGeneration, out uint committedGeneration)
        {
            committedGeneration = 0U;
            if (!_releases.TryGetValue(regionKey, out CollisionReleaseLease lease))
                return false;
            if (lease.SessionEpoch != expectedEpoch)
                return false;
            if (lease.RegionGeneration != expectedGeneration)
                return false;
            if (GetGeneration(regionKey) != expectedGeneration)
                return false;

            _releases.Remove(regionKey);
            _activeRegions.Remove(regionKey);
            uint next = expectedGeneration == uint.MaxValue ? 1U : expectedGeneration + 1U;
            if (next == 0U) next = 1U;
            _generations[regionKey] = next;
            committedGeneration = next;
            return true;
        }

        public void CleanObserverDisconnect(RegionKey regionKey)
        {
            _releases.Remove(regionKey);
            _activeRegions.Remove(regionKey);
        }
    }

    /// <summary>
    /// 静态场景物件与权限门物理碰撞适配器 (LevelObjectCollisionAdapter)
    /// 统一管理远区静态物件、刷卡门/钥匙门物理碰撞的按需激活与滞回释放。
    /// </summary>
    public sealed class LevelObjectCollisionAdapter : ILifecycleDomainAdapter, ICollisionOverrideStore
    {
        private sealed class RegionState
        {
            internal readonly bool Active;
            internal readonly uint Generation;
            internal readonly Dictionary<Animation, AnimationOwnership> Animations;
            internal readonly Dictionary<LevelObject, ulong> Objects;

            internal RegionState(bool active, uint generation,
                Dictionary<Animation, AnimationOwnership> animations,
                Dictionary<LevelObject, ulong> objects)
            {
                Active = active;
                Generation = generation;
                Animations = animations;
                Objects = objects;
            }
        }

        private sealed class AnimationOwnership
        {
            internal readonly LevelObject Owner;
            internal readonly RegionKey Region;
            internal readonly AnimationCullingType Original;
            internal ulong AcquireGeneration;

            internal AnimationOwnership(LevelObject owner, RegionKey region,
                AnimationCullingType original, ulong acquireGeneration)
            {
                Owner = owner;
                Region = region;
                Original = original;
                AcquireGeneration = acquireGeneration;
            }
        }

        private sealed class ObjectOwnership
        {
            internal readonly RegionKey Region;
            internal ulong AcquireGeneration;

            internal ObjectOwnership(RegionKey region, ulong acquireGeneration)
            {
                Region = region;
                AcquireGeneration = acquireGeneration;
            }
        }

        private static readonly LevelObjectCollisionLedger Ledger = new LevelObjectCollisionLedger();
        private static readonly MethodInfo VanillaRefreshMethod =
            typeof(LevelObject).GetMethod("UpdateActiveAndRenderersEnabled",
                BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly object SyncLock = new object();
        private static readonly Dictionary<Animation, AnimationOwnership> OwnedAnimations =
            new Dictionary<Animation, AnimationOwnership>();
        private static readonly Dictionary<LevelObject, ObjectOwnership> OwnedObjects =
            new Dictionary<LevelObject, ObjectOwnership>();
        private static bool _registrationReady;
        private static ulong _currentSessionEpoch = 1UL;
        public const float DefaultHysteresisSeconds = 2.0f;

        public DomainId DomainId => DomainIds.Collision;
        public string DisplayName => "Collision";
        public string Capability => "StaticRootReactivation+DynamicAnimationCulling+HysteresisRelease";

        public static bool RegistrationReady => _registrationReady;
        public static ulong CurrentSessionEpoch => _currentSessionEpoch;

        public static bool IsRemoteCollisionRequired(byte x, byte y)
        {
            lock (SyncLock)
            {
                return Ledger.IsRegionActive(x, y);
            }
        }

        public static uint GetGeneration(RegionKey regionKey)
        {
            lock (SyncLock)
            {
                return Ledger.GetGeneration(regionKey);
            }
        }

        public bool IsIdentityCertain => _registrationReady;

        public IReadOnlyList<CollisionOverride> Acquire(CollisionExecutionIdentity identity)
        {
            if (identity.DomainId != DomainIds.Collision) return Array.Empty<CollisionOverride>();
            lock (SyncLock)
            {
                uint generation = Ledger.CommitAcquire(identity.RegionKey);
                if (generation == 0U)
                    throw new InvalidOperationException("Collision region generation is undefined after acquire.");
                var overrides = new List<CollisionOverride>();
                overrides.Add(new CollisionOverride(CollisionOverrideKind.RegionLeaseMarker,
                    identity.RegionKey, identity.AcquireGeneration, true));
                overrides.AddRange(AcquireNativeOverrides(identity));
                return overrides;
            }
        }

        private IReadOnlyList<CollisionOverride> AcquireNativeOverrides(
            CollisionExecutionIdentity identity)
        {
            var overrides = new List<CollisionOverride>();
            if (LevelObjects.objects == null) return overrides;

            List<LevelObject> objects = LevelObjects.objects[
                identity.RegionKey.X, identity.RegionKey.Y];
            if (objects == null) return overrides;

            for (int index = 0; index < objects.Count; index++)
            {
                LevelObject levelObject = objects[index];
                if (levelObject == null) continue;
                AcquireLevelObject(levelObject, identity, overrides);
            }
            return overrides;
        }

        private static void AcquireLevelObject(
            LevelObject levelObject, CollisionExecutionIdentity identity,
            List<CollisionOverride> overrides)
        {
            Transform transform = levelObject.transform;
            if (!levelObject.canDamageRubble || transform == null
                || (levelObject.asset != null && levelObject.asset.type == EObjectType.NPC)
                || transform.Find("Decal") != null
                || transform.gameObject == null
                || transform.GetComponentInChildren<Collider>(true) == null)
                return;

            ObjectOwnership objectOwnership;
            if (OwnedObjects.TryGetValue(levelObject, out objectOwnership))
            {
                if (objectOwnership.Region != identity.RegionKey) return;
                overrides.Add(new CollisionOverride(CollisionOverrideKind.LevelObject,
                    identity.RegionKey, identity.AcquireGeneration, true, levelObject));
            }
            else if (!transform.gameObject.activeSelf)
            {
                transform.gameObject.SetActive(true);
                OwnedObjects[levelObject] = new ObjectOwnership(
                    identity.RegionKey, identity.AcquireGeneration);
                overrides.Add(new CollisionOverride(CollisionOverrideKind.LevelObject,
                    identity.RegionKey, identity.AcquireGeneration, true, levelObject));
            }
            AcquireDoorAnimations(levelObject, transform, identity, overrides);
        }

        private static void AcquireDoorAnimations(
            LevelObject levelObject, Transform transform,
            CollisionExecutionIdentity identity, List<CollisionOverride> overrides)
        {
            if (!(levelObject.interactable is InteractableObjectBinaryState)) return;
            Animation[] animations = transform.GetComponentsInChildren<Animation>(true);
            for (int index = 0; index < animations.Length; index++)
            {
                Animation animation = animations[index];
                if (animation == null) continue;
                if (!OwnedAnimations.ContainsKey(animation))
                    OwnedAnimations.Add(animation, new AnimationOwnership(
                        levelObject, identity.RegionKey, animation.cullingType,
                        identity.AcquireGeneration));
                AnimationOwnership animationOwnership;
                if (OwnedAnimations.TryGetValue(animation, out animationOwnership)
                    && animationOwnership.Region == identity.RegionKey)
                {
                    overrides.Add(new CollisionOverride(CollisionOverrideKind.DoorAnimation,
                        identity.RegionKey, identity.AcquireGeneration, true, animation));
                }
                if (animation.cullingType != AnimationCullingType.AlwaysAnimate)
                {
                    animation.cullingType = AnimationCullingType.AlwaysAnimate;
                    bool alreadyIncluded = false;
                    for (int existing = 0; existing < overrides.Count; existing++)
                        if (ReferenceEquals(overrides[existing].NativeTarget, animation))
                            alreadyIncluded = true;
                    if (!alreadyIncluded)
                        overrides.Add(new CollisionOverride(CollisionOverrideKind.DoorAnimation,
                            identity.RegionKey, identity.AcquireGeneration, true, animation));
                }
            }
        }

        public bool IsOwned(CollisionOverride item)
        {
            if (!item.PluginOwned || !Ledger.IsRegionActive(item.RegionKey)) return false;
            if (item.NativeTarget is Animation animation)
            {
                AnimationOwnership ownership;
                return OwnedAnimations.TryGetValue(animation, out ownership)
                    && ownership.Region == item.RegionKey;
            }
            if (item.NativeTarget is LevelObject levelObject)
                return OwnedObjects.TryGetValue(levelObject, out ObjectOwnership ownership)
                    && ownership.Region == item.RegionKey;
            return item.Kind == CollisionOverrideKind.RegionLeaseMarker
                && item.NativeTarget == null;
        }

        public bool TryRevokeOwnedAtomically(IReadOnlyList<CollisionOverride> overrides)
        {
            if (overrides == null) return false;
            lock (SyncLock)
            {
                for (int index = 0; index < overrides.Count; index++)
                    if (!IsOwned(overrides[index])) return false;
                for (int index = 0; index < overrides.Count; index++)
                    RevokeNativeOverride(overrides[index]);
                if (overrides.Count > 0)
                    Ledger.CleanObserverDisconnect(overrides[0].RegionKey);
                return true;
            }
        }

        private static void RestoreOwnedNativeState()
        {
            foreach (KeyValuePair<Animation, AnimationOwnership> pair in OwnedAnimations)
            {
                if (pair.Key != null) pair.Key.cullingType = pair.Value.Original;
            }
            foreach (LevelObject levelObject in OwnedObjects.Keys)
            {
                if (levelObject != null && VanillaRefreshMethod != null)
                    VanillaRefreshMethod.Invoke(levelObject, null);
            }
            OwnedAnimations.Clear();
            OwnedObjects.Clear();
        }
        private static void RestoreRegionNativeState(RegionKey regionKey)
        {
            foreach (KeyValuePair<Animation, AnimationOwnership> pair in OwnedAnimations)
                if (pair.Value.Region == regionKey && pair.Key != null)
                    pair.Key.cullingType = pair.Value.Original;
            foreach (KeyValuePair<LevelObject, ObjectOwnership> pair in OwnedObjects)
                if (pair.Value.Region == regionKey && pair.Key != null
                    && VanillaRefreshMethod != null)
                    VanillaRefreshMethod.Invoke(pair.Key, null);
            foreach (Animation animation in new List<Animation>(OwnedAnimations.Keys))
            {
                AnimationOwnership ownership;
                if (OwnedAnimations.TryGetValue(animation, out ownership)
                    && ownership.Region == regionKey)
                    OwnedAnimations.Remove(animation);
            }
            foreach (LevelObject levelObject in new List<LevelObject>(OwnedObjects.Keys))
            {
                ObjectOwnership ownership;
                if (OwnedObjects.TryGetValue(levelObject, out ownership)
                    && ownership.Region == regionKey)
                    OwnedObjects.Remove(levelObject);
            }
        }

        private static void RevokeNativeOverride(CollisionOverride item)
        {
            if (item.NativeTarget is Animation animation)
            {
                AnimationOwnership ownership;
                if (OwnedAnimations.TryGetValue(animation, out ownership))
                {
                    animation.cullingType = ownership.Original;
                    OwnedAnimations.Remove(animation);
                }
                return;
            }

            if (item.NativeTarget is LevelObject levelObject)
            {
                OwnedObjects.Remove(levelObject);
                if (VanillaRefreshMethod != null)
                    VanillaRefreshMethod.Invoke(levelObject, null);
            }
        }

        public object CaptureRegionState(RegionKey regionKey)
        {
            lock (SyncLock)
            {
                var animations = new Dictionary<Animation, AnimationOwnership>();
                foreach (KeyValuePair<Animation, AnimationOwnership> pair in OwnedAnimations)
                    if (pair.Value.Region == regionKey) animations[pair.Key] = pair.Value;
                var objects = new Dictionary<LevelObject, ulong>();
                foreach (KeyValuePair<LevelObject, ObjectOwnership> pair in OwnedObjects)
                    if (pair.Value.Region == regionKey) objects[pair.Key] = pair.Value.AcquireGeneration;
                return new RegionState(Ledger.IsRegionActive(regionKey), Ledger.GetGeneration(regionKey),
                    animations, objects);
            }
        }

        public void RestoreRegionState(RegionKey regionKey, object state)
        {
            var snapshot = state as RegionState;
            if (snapshot == null) return;
            lock (SyncLock)
            {
                RestoreRegionNativeState(regionKey);
                foreach (KeyValuePair<Animation, AnimationOwnership> pair in snapshot.Animations)
                {
                    OwnedAnimations[pair.Key] = pair.Value;
                    if (pair.Key != null)
                        pair.Key.cullingType = snapshot.Active
                            ? AnimationCullingType.AlwaysAnimate
                            : pair.Value.Original;
                }
                foreach (KeyValuePair<LevelObject, ulong> pair in snapshot.Objects)
                {
                    OwnedObjects[pair.Key] = new ObjectOwnership(regionKey, pair.Value);
                    if (pair.Key != null && pair.Key.transform != null
                        && pair.Key.transform.gameObject != null)
                        pair.Key.transform.gameObject.SetActive(true);
                }
                Ledger.RestoreRegionState(regionKey, snapshot.Active, snapshot.Generation);
            }
        }

        public void OnSessionBegin(uint sessionEpoch)
        {
            lock (SyncLock)
            {
                _registrationReady = true;
                _currentSessionEpoch = sessionEpoch == 0U ? 1UL : sessionEpoch;
                RestoreOwnedNativeState();
                Ledger.BeginSession(_currentSessionEpoch);
            }
        }

        public void OnSessionEnd()
        {
            lock (SyncLock)
            {
                _registrationReady = false;
                _currentSessionEpoch = 0UL;
                RestoreOwnedNativeState();
                Ledger.EndSession();
                OwnedAnimations.Clear();
                OwnedObjects.Clear();
            }
        }

        public void OnAcquire(LeaseTicket ticket)
        {
            if (ticket.Valid)
            {
                lock (SyncLock)
                {
                    Ledger.CommitAcquire(ticket.RegionKey);
                }
            }
        }

        public void OnRelease(LeaseTicket ticket)
        {
            if (ticket.Valid)
            {
                lock (SyncLock)
                {
                    Ledger.ScheduleRelease(ticket.RegionKey, 0f, DefaultHysteresisSeconds);
                }
            }
        }

        public void OnTick(float deltaTime)
        {
            // 碰撞生命周期驱动
        }

        public void OnObserverDisconnect(ulong observerId, ulong connectionToken)
        {
            // 观察者注销
        }
    }
}
