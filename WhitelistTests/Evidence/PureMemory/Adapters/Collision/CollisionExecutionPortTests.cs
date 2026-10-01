using SteamP2PFriends.Adapters.Collision;
using SteamP2PFriends.Core.Identity;
using SteamP2PFriends.MultiObserver.Demand;
using SteamP2PFriends.MultiObserver.Lifecycle;
using SteamP2PFriends.MultiObserver.SPI;
using System;
using System.Collections.Generic;

namespace SteamP2PFriends.WhitelistTests
{
    internal static class CollisionExecutionPortTests
    {
        private static LeaseTicket Ticket(
            RegionKey region, ulong epoch = 41UL, uint generation = 7U, int demand = 0)
        {
            return new LeaseTicket(
                DomainIds.Collision, region, new SessionEpoch(epoch),
                new RegionGeneration(generation), demand, true);
        }

        private static CollisionExecutionPort CreatePort(
            FakeCollisionOverrideStore store, Func<RegionKey, uint> generations = null)
        {
            return new CollisionExecutionPort(
                store, generations ?? (region => 7U), CollisionLifecyclePolicy.Create(2.0f));
        }

        private static bool Expect(bool condition, string detail)
        {
            if (!condition) throw new InvalidOperationException(detail);
            return true;
        }

        internal static bool Test_CEP01_ExecutionPortSeam()
        {
            var store = new FakeCollisionOverrideStore();
            CollisionExecutionPort port = CreatePort(store);
            var projection = new DemandProjectionEngine(new ObserverSpatialAuthority());
            DemandPolicy policy = CollisionDemandPolicy.Create(0, 64);
            projection.Register(policy);
            var engine = new LifecycleOrchestrationEngine(projection);
            engine.Register(policy, port);
            engine.BeginSession(new SessionEpoch(41UL));
            engine.Observe(DomainIds.Collision, 900UL, 9001UL, 3, 4, true);
            return Expect(store.Acquired.Count == 1 && port.ReceiptCount == 1
                && port.LastAcquireDetail.Contains("outcome=success"),
                "typed Collision demand must reach the execution port through the shared engine");
        }

        internal static bool Test_CEP02_ReceiptIdentity()
        {
            var store = new FakeCollisionOverrideStore();
            CollisionExecutionPort port = CreatePort(store);
            port.OnSessionBegin(41U);
            CollisionAcquisitionReceipt receipt;
            port.TryAcquire(Ticket(new RegionKey(3, 4)), out receipt);
            return Expect(receipt.DomainId == DomainIds.Collision
                && receipt.RegionKey == new RegionKey(3, 4)
                && receipt.SessionEpoch == new SessionEpoch(41UL)
                && receipt.RegionGeneration == new RegionGeneration(7U)
                && receipt.AcquireGeneration != 0UL
                && receipt.Overrides.Count == 1, "receipt identity is incomplete");
        }

        internal static bool Test_CEP03_NewAcquireInvalidatesOldReceipt()
        {
            var store = new FakeCollisionOverrideStore();
            CollisionExecutionPort port = CreatePort(store);
            port.OnSessionBegin(41U);
            RegionKey region = new RegionKey(3, 4);
            CollisionAcquisitionReceipt first;
            CollisionAcquisitionReceipt second;
            port.TryAcquire(Ticket(region), out first);
            port.TryAcquire(Ticket(region), out second);
            return Expect(second.AcquireGeneration > first.AcquireGeneration
                && port.IsCurrentReceipt(second)
                && !port.IsCurrentReceipt(first),
                "new acquire must replace the previous receipt");
        }

        internal static bool Test_CEP04_ReleaseOnlyRevokesOwnedReceiptOverrides()
        {
            var store = new FakeCollisionOverrideStore();
            CollisionExecutionPort port = CreatePort(store);
            port.OnSessionBegin(41U);
            RegionKey region = new RegionKey(3, 4);
            CollisionAcquisitionReceipt receipt;
            port.TryAcquire(Ticket(region), out receipt);
            bool released = port.TryRelease(Ticket(region));
            return Expect(released && store.Revoked.Count == receipt.Overrides.Count
                && port.ReceiptCount == 0, "release must revoke only receipt-owned overrides");
        }

        internal static bool Test_CEP05_StaleOrUncertainReleaseDoesNotDestructivelyDisable()
        {
            var store = new FakeCollisionOverrideStore();
            CollisionExecutionPort port = CreatePort(store);
            port.OnSessionBegin(41U);
            RegionKey region = new RegionKey(3, 4);
            CollisionAcquisitionReceipt receipt;
            port.TryAcquire(Ticket(region), out receipt);
            store.IdentityCertain = false;
            bool uncertainReleased = port.TryRelease(Ticket(region));
            store.IdentityCertain = true;
            bool staleReleased = port.TryRelease(Ticket(region, epoch: 42UL));
            bool staleGenerationReleased = port.TryRelease(Ticket(region, generation: 8U));
            return Expect(!uncertainReleased && !staleReleased && !staleGenerationReleased
                && store.Revoked.Count == 0 && port.ReceiptCount == 1,
                "uncertain or stale identity must retain receipt and avoid destructive release");
        }

        internal static bool Test_CEP06_ReleaseIsIdempotent()
        {
            var store = new FakeCollisionOverrideStore();
            CollisionExecutionPort port = CreatePort(store);
            port.OnSessionBegin(41U);
            RegionKey region = new RegionKey(3, 4);
            CollisionAcquisitionReceipt receipt;
            port.TryAcquire(Ticket(region), out receipt);
            bool first = port.TryRelease(Ticket(region));
            bool second = port.TryRelease(Ticket(region));
            return Expect(first && !second && store.Revoked.Count == 1,
                "release must be idempotent after receipt removal");
        }


        internal static bool Test_CEP07_HostDemandBlocksFinalRelease()
        {
            var store = new FakeCollisionOverrideStore();
            CollisionExecutionPort port = CreatePort(store);
            var projection = new DemandProjectionEngine(new ObserverSpatialAuthority());
            DemandPolicy policy = CollisionDemandPolicy.Create(0, 64);
            projection.Register(policy);
            var engine = new LifecycleOrchestrationEngine(projection);
            engine.Register(policy, port);
            engine.BeginSession(new SessionEpoch(41UL));
            engine.Observe(DomainIds.Collision, 900UL, 9001UL, 3, 4, true);
            engine.Observe(DomainIds.Collision, 100UL, 1001UL, 3, 4, true);
            engine.RemoveObserver(DomainIds.Collision, 100UL);
            engine.AdvanceTime(2.0f);
            engine.Flush(0f);
            return Expect(store.Revoked.Count == 0 && port.ReceiptCount == 1,
                "Host demand must prevent the shared engine from committing final Release");
        }

        internal static bool Test_CEP09_StaleReceiptCannotReleaseNewAcquire()
        {
            var store = new FakeCollisionOverrideStore();
            CollisionExecutionPort port = CreatePort(store);
            port.OnSessionBegin(41U);
            RegionKey region = new RegionKey(3, 4);
            CollisionAcquisitionReceipt first;
            CollisionAcquisitionReceipt second;
            port.TryAcquire(Ticket(region), out first);
            port.TryAcquire(Ticket(region), out second);
            bool staleReleased = port.TryRelease(Ticket(region), first);
            return Expect(!staleReleased && store.Revoked.Count == 0 && port.ReceiptCount == 1,
                "stale receipt must not release the replacement acquisition");
        }

        internal static bool Test_CEP08_OverrideSetExcludesResourceAndTrees()
        {
            var store = new FakeCollisionOverrideStore();
            CollisionExecutionPort port = CreatePort(store);
            port.OnSessionBegin(41U);
            CollisionAcquisitionReceipt receipt;
            port.TryAcquire(Ticket(new RegionKey(3, 4)), out receipt);
            foreach (CollisionOverride item in receipt.Overrides)
            {
                if (item.Kind == CollisionOverrideKind.ColliderCulling
                    || item.Kind == CollisionOverrideKind.DoorAnimation
                    || item.Kind == CollisionOverrideKind.LevelObject) continue;
                return false;
            }
            return Expect(receipt.Overrides.Count == 1, "receipt must contain only Collision overrides");
        }

        internal static bool Test_CEP14_ReceiptUsesActualStoreGeneration()
        {
            var store = new GenerationAdvancingCollisionOverrideStore();
            CollisionExecutionPort port = new CollisionExecutionPort(
                store, region => store.Generation, CollisionLifecyclePolicy.Create(2.0f));
            port.OnSessionBegin(41U);
            CollisionAcquisitionReceipt receipt;
            bool acquired = port.TryAcquire(Ticket(new RegionKey(3, 4), generation: 7U), out receipt);
            return Expect(acquired && receipt.RegionGeneration.Value == store.Generation,
                "receipt must bind the generation returned by the execution store");
        }

        internal static bool Test_CEP15_ProductionStoreFollowsSessionBoundary()
        {
            var store = new SessionBoundaryStore();
            CollisionExecutionPort port = new CollisionExecutionPort(
                store, region => 7U, CollisionLifecyclePolicy.Create(2.0f));
            port.OnSessionBegin(42U);
            port.OnSessionEnd();
            return Expect(store.Begun == 1 && store.Ended == 1,
                "Collision Port must forward both session boundaries to its Store");
        }
        internal static bool Test_CEP17_PortForwardsLifecycleTickToStore()
        {
            var store = new SessionBoundaryStore();
            CollisionExecutionPort port = new CollisionExecutionPort(
                store, region => 7U, CollisionLifecyclePolicy.Create(2.0f));
            port.OnSessionBegin(42U);
            port.OnLifecycleTick(0.016f);
            port.OnLifecycleTick(0.032f);
            return Expect(store.Ticks == 2 && store.LastDelta == 0.032f,
                "Collision Port must forward lifecycle maintenance ticks to its Store");
        }

        /// <summary>
        /// 票 10 回归锁（生产接线自举路径）：引擎 Acquire 路径以
        /// beforeAcquire = port.ReadRegionGeneration 开票，而生产端口代次源接的是
        /// LevelObjectCollisionAdapter.GetGeneration（读静态 Ledger）。从未 Acquire 的区域在
        /// Ledger 无条目，代次源必须读出已定义初始代次，否则 IsDefined 门恒拒、首次 Acquire
        /// 永不成立（实机 S3 轮 4293 条 acquire-identity-rejected regionGeneration=0 死锁签名）。
        /// 既有 CEP 系测试均以显式 generation:7 开票，未覆盖本自举路径。
        /// Store 用可证身份的 Fake：纯内存宿主无法执行真实 Store 的 OnSessionBegin
        /// （RestoreOwnedNativeState 含 Unity extern 调用点，JIT 编译即抛 ECall），
        /// 而本票缺陷在代次源与身份门，不在 Store。
        /// </summary>
        internal static bool Test_CEP18_NeverAcquiredRegionFirstAcquireSucceeds()
        {
            var store = new FakeCollisionOverrideStore();
            CollisionExecutionPort port = new CollisionExecutionPort(
                store, LevelObjectCollisionAdapter.GetGeneration, CollisionLifecyclePolicy.Create(2.0f));
            port.OnSessionBegin(41U);
            RegionKey region = new RegionKey(21, 37);
            RegionGeneration beforeAcquire = port.ReadRegionGeneration(region);
            if (!beforeAcquire.IsDefined)
                throw new InvalidOperationException(
                    "never-acquired region must read a defined initial generation");
            var ticket = new LeaseTicket(
                DomainIds.Collision, region, new SessionEpoch(41UL), beforeAcquire, 1, true);
            CollisionAcquisitionReceipt receipt;
            bool acquired = port.TryAcquire(ticket, out receipt);
            return Expect(acquired && receipt.Valid
                && receipt.RegionGeneration.IsDefined
                && receipt.RegionGeneration.Value == beforeAcquire.Value
                && port.ReceiptCount == 1
                && port.LastAcquireDetail.Contains("outcome=success"),
                "never-acquired region must succeed on its first acquire");
        }

        /// <summary>
        /// 票 10 回归锁（代次源自举与推进）：真实 LevelObjectCollisionAdapter 的代次源对从未
        /// Acquire 的区域必须读出已定义初始代次（恰好等于 InitialRegionGeneration）；经生产
        /// 提交入口 ICollisionOverrideStore.Acquire（执行端口过身份门后调用的正是它）首次提交
        /// 即落在该初始代次（Ledger 0+1），再次提交照常 +1 推进——自举不得把代次楔死。
        /// Acquire→AcquireNativeOverrides 链不含 Unity extern 调用点（LevelObjects.objects
        /// 为托管属性，宿主中为 null 提前返回），可纯内存执行；用独立区域键与 CEP18 隔离。
        /// </summary>
        internal static bool Test_CEP19_GenerationSourceBootstrapsAndAdvances()
        {
            RegionKey region = new RegionKey(22, 38);
            var adapter = new LevelObjectCollisionAdapter();
            // 钉死字面量 1（= 生产侧 InitialRegionGeneration）：契约级精确值，
            // 生产侧若改动自举初始代次必须同步更新本测试。
            uint initial = LevelObjectCollisionAdapter.GetGeneration(region);
            if (initial != 1U)
                throw new InvalidOperationException(
                    "never-acquired region must read the defined initial generation 1");
            var bootstrapTicket = new LeaseTicket(
                DomainIds.Collision, region, new SessionEpoch(41UL),
                new RegionGeneration(initial), 1, true);
            adapter.Acquire(new CollisionExecutionIdentity(bootstrapTicket, 1UL));
            uint firstCommitted = LevelObjectCollisionAdapter.GetGeneration(region);
            adapter.Acquire(new CollisionExecutionIdentity(bootstrapTicket, 2UL));
            uint secondCommitted = LevelObjectCollisionAdapter.GetGeneration(region);
            return Expect(firstCommitted == 1U && secondCommitted == firstCommitted + 1U,
                "first commit must establish the bootstrap generation and later commits must advance it");
        }

        internal static bool Test_CEP16_EmptyStoreReceiptReleasesRegion()
        {
            var store = new EmptyOverrideStore();
            CollisionExecutionPort port = new CollisionExecutionPort(
                store, region => 7U, CollisionLifecyclePolicy.Create(2.0f));
            port.OnSessionBegin(41U);
            CollisionAcquisitionReceipt receipt;
            bool acquired = port.TryAcquire(Ticket(new RegionKey(3, 4)), out receipt);
            bool released = port.TryRelease(Ticket(new RegionKey(3, 4)));
            return Expect(acquired && receipt.Overrides.Count == 1
                && receipt.Overrides[0].Kind == CollisionOverrideKind.RegionLeaseMarker
                && released && store.Revoked,
                "an empty native target set must use a region marker and still release its region ownership");
        }
        internal static bool Test_CEP13_DemandStillPresentRejectsRelease()
        {
            var store = new FakeCollisionOverrideStore();
            CollisionExecutionPort port = CreatePort(store);
            port.OnSessionBegin(41U);
            RegionKey region = new RegionKey(3, 4);
            CollisionAcquisitionReceipt receipt;
            port.TryAcquire(Ticket(region), out receipt);
            bool released = port.TryRelease(Ticket(region, demand: 1));
            return Expect(!released && store.Revoked.Count == 0 && port.ReceiptCount == 1
                && port.LastReleaseReceiptDetail.Contains("reason=release-identity-rejected"),
                "release must reject while Collision demand remains positive");
        }



        internal static bool Test_CEP12_ReleaseRejectionIsObservable()
        {
            var store = new FakeCollisionOverrideStore();
            CollisionExecutionPort port = CreatePort(store);
            port.OnSessionBegin(41U);
            RegionKey region = new RegionKey(3, 4);
            bool missingReleased = port.TryRelease(Ticket(region));
            bool missingObserved = port.LastReleaseReceiptDetail.Contains("reason=release-identity-rejected");
            CollisionAcquisitionReceipt receipt;
            port.TryAcquire(Ticket(region), out receipt);
            store.IdentityCertain = false;
            bool uncertainReleased = port.TryRelease(Ticket(region));
            bool uncertainObserved = port.LastReleaseReceiptDetail.Contains("reason=release-identity-rejected");
            return Expect(!missingReleased && missingObserved && !uncertainReleased && uncertainObserved
                && port.ReceiptCount == 1, "release rejection must remain observable and retain state");
        }


        internal static bool Test_CEP11_UnownedOverrideFailsClosed()
        {
            var store = new FakeCollisionOverrideStore { ForceUnowned = true };
            CollisionExecutionPort port = CreatePort(store);
            port.OnSessionBegin(41U);
            RegionKey region = new RegionKey(3, 4);
            CollisionAcquisitionReceipt receipt;
            port.TryAcquire(Ticket(region), out receipt);
            bool released = port.TryRelease(Ticket(region));
            return Expect(!released && store.Revoked.Count == 0 && port.ReceiptCount == 1
                && port.LastReleaseReceiptDetail.Contains("ownership-unproven"),
                "unproven ownership must retain receipt and fail non-destructively");
        }


        internal static bool Test_CEP10_ReleaseDiagnosticNamesReceipt()
        {
            var store = new FakeCollisionOverrideStore();
            CollisionExecutionPort port = CreatePort(store);
            port.OnSessionBegin(41U);
            RegionKey region = new RegionKey(3, 4);
            CollisionAcquisitionReceipt receipt;
            port.TryAcquire(Ticket(region), out receipt);
            bool released = port.TryRelease(Ticket(region));
            return Expect(released
                && port.LastReleaseReceiptDetail.Contains("receiptAcquireGeneration=" + receipt.AcquireGeneration),
                "release diagnostic must identify the receipt generation");
        }

    }

    internal sealed class EmptyOverrideStore : ICollisionOverrideStore
    {
        internal bool Revoked;
        public bool IsIdentityCertain => true;
        public void OnSessionBegin(uint sessionEpoch) { }
        public void OnSessionEnd() { }
        public IReadOnlyList<CollisionOverride> Acquire(CollisionExecutionIdentity identity) =>
            new[] { new CollisionOverride(CollisionOverrideKind.RegionLeaseMarker,
                identity.RegionKey, identity.AcquireGeneration, true) };
        public bool IsOwned(CollisionOverride item) => item.PluginOwned;
        public bool TryRevokeOwnedAtomically(IReadOnlyList<CollisionOverride> overrides)
        {
            Revoked = true;
            return true;
        }
        public object CaptureRegionState(RegionKey regionKey) => null;
        public void RestoreRegionState(RegionKey regionKey, object state) { }
        public void OnLifecycleTick(float deltaTime) { }
    }

    internal sealed class SessionBoundaryStore : ICollisionOverrideStore
    {
        internal int Begun;
        internal int Ended;
        internal int Ticks;
        internal float LastDelta;
        public bool IsIdentityCertain => true;
        public void OnSessionBegin(uint sessionEpoch) { Begun++; }
        public void OnSessionEnd() { Ended++; }
        public IReadOnlyList<CollisionOverride> Acquire(CollisionExecutionIdentity identity) =>
            new[] { new CollisionOverride(CollisionOverrideKind.LevelObject,
                identity.RegionKey, identity.AcquireGeneration, true) };
        public bool IsOwned(CollisionOverride item) => true;
        public bool TryRevokeOwnedAtomically(IReadOnlyList<CollisionOverride> overrides) => true;
        public object CaptureRegionState(RegionKey regionKey) => null;
        public void RestoreRegionState(RegionKey regionKey, object state) { }
        public void OnLifecycleTick(float deltaTime) { Ticks++; LastDelta = deltaTime; }
    }

    internal sealed class GenerationAdvancingCollisionOverrideStore : ICollisionOverrideStore
    {
        public uint Generation = 7U;
        public bool IsIdentityCertain => true;
        public void OnSessionBegin(uint sessionEpoch) { }
        public void OnSessionEnd() { }
        public IReadOnlyList<CollisionOverride> Acquire(CollisionExecutionIdentity identity)
        {
            Generation = 8U;
            return new[] { new CollisionOverride(CollisionOverrideKind.LevelObject,
                identity.RegionKey, identity.AcquireGeneration, true) };
        }
        public bool IsOwned(CollisionOverride item) => true;
        public bool TryRevokeOwnedAtomically(IReadOnlyList<CollisionOverride> overrides) => true;
        public object CaptureRegionState(RegionKey regionKey) => null;
        public void RestoreRegionState(RegionKey regionKey, object state) { }
        public void OnLifecycleTick(float deltaTime) { }
    }

    internal sealed class FakeCollisionOverrideStore : ICollisionOverrideStore
    {
        public readonly List<CollisionOverride> Acquired = new List<CollisionOverride>();
        public readonly List<CollisionOverride> Revoked = new List<CollisionOverride>();
        public bool IdentityCertain = true;
        public bool ForceUnowned;
        public bool IsIdentityCertain => IdentityCertain;
        public void OnSessionBegin(uint sessionEpoch) { }
        public void OnSessionEnd() { }

        public IReadOnlyList<CollisionOverride> Acquire(CollisionExecutionIdentity identity)
        {
            CollisionOverride item = new CollisionOverride(
                CollisionOverrideKind.LevelObject, identity.RegionKey, identity.AcquireGeneration, true);
            Acquired.Add(item);
            return new[] { item };
        }

        public bool IsOwned(CollisionOverride item) => !ForceUnowned && !Revoked.Contains(item);
        public bool TryRevokeOwnedAtomically(IReadOnlyList<CollisionOverride> overrides)
        {
            for (int i = 0; i < overrides.Count; i++)
            {
                if (!IsOwned(overrides[i])) return false;
            }
            for (int i = 0; i < overrides.Count; i++) Revoked.Add(overrides[i]);
            return true;
        }

        public bool TryRevoke(CollisionOverride item)
        {
            if (!IsOwned(item)) return false;
            Revoked.Add(item);
            return true;
        }

        public object CaptureRegionState(RegionKey regionKey) => null;
        public void RestoreRegionState(RegionKey regionKey, object state) { }
        public void OnLifecycleTick(float deltaTime) { }
    }
}
