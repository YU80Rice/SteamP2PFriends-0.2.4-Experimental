using SteamP2PFriends.Core.Identity;
using SteamP2PFriends.MultiObserver.Demand;
using System;
using System.Collections.Generic;

namespace SteamP2PFriends.WhitelistTests
{
    /// <summary>
    /// 票 02 的共享投影接缝契约（PureMemory，最高接缝 = Control Plane 投影引擎）：
    /// 唯一的 World Presence Observer 空间事实、按声明式 Demand Policy 的枚举/裁剪/
    /// 去重/计数/进入退出差异，以及 typed Domain Demand 的 Domain Id + Region Key 身份。
    /// 断言对外行为与结构形状，不断言共享引擎的内部字段布局。
    /// </summary>
    internal static class DemandProjectionEngineTests
    {
        private const string RadiusSource = "test-radius-source";

        internal static DemandPolicy ResourcePolicy(byte radius, byte worldSize = 64)
        {
            return new DemandPolicy(DomainIds.Resource, radius, worldSize,
                EDemandRegionShape.ChebyshevSquare2D, RadiusSource, presence => presence.GameplayAuthorized);
        }

        internal static DemandPolicy CollisionPolicy(byte radius, byte worldSize = 64)
        {
            return new DemandPolicy(DomainIds.Collision, radius, worldSize,
                EDemandRegionShape.ChebyshevSquare2D, "LevelGround.OBJECT_REGIONS",
                presence => presence.GameplayAuthorized);
        }

        internal static ObserverPresence Presence(
            ulong observerId, ulong connectionToken, byte centerX, byte centerY, bool authorized = true)
        {
            return new ObserverPresence(observerId, connectionToken, new RegionKey(centerX, centerY), authorized);
        }

        internal static int CountRegions(RegionKey[] regions, RegionKey expected)
        {
            int count = 0;
            foreach (RegionKey region in regions)
            {
                if (region == expected) count++;
            }
            return count;
        }

        internal static bool ContainsRegion(RegionKey[] regions, byte x, byte y)
        {
            return CountRegions(regions, new RegionKey(x, y)) == 1;
        }

        /// <summary>
        /// 唯一空间事实：两个领域从同一份事实投影，各按自己的半径得到不同区域集合，
        /// 重复提交同一事实是幂等的；一个领域的需求不授予另一个领域。
        /// </summary>
        internal static bool Test_DPE01_SharedFactIsSingleAndIdempotent()
        {
            var authority = new ObserverSpatialAuthority();
            var engine = new DemandProjectionEngine(authority);
            DemandPolicy resource = ResourcePolicy(1);
            DemandPolicy collision = CollisionPolicy(3);
            engine.Register(resource);
            engine.Register(collision);
            ObserverPresence presence = Presence(100UL, 1001UL, 10, 10);

            DomainDemandProjection resourceProjection = engine.Observe(resource, presence);
            DomainDemandProjection collisionProjection = engine.Observe(collision, presence);
            DomainDemandProjection repeat = engine.Observe(resource, presence);

            bool singleFact = authority.Count == 1
                && authority.TryGet(100UL, out ObserverPresence stored)
                && stored.Center == new RegionKey(10, 10)
                && stored.ConnectionToken == 1001UL;
            bool perDomainRadius = resourceProjection.EnteredRegions.Length == 9
                && collisionProjection.EnteredRegions.Length == 49;
            bool idempotent = !repeat.HasChanges
                && repeat.EnteredRegions.Length == 0
                && repeat.ExitedRegions.Length == 0;
            bool demandsAreDomainBound = engine.TryGetDemand(resource, new RegionKey(10, 10),
                    out DomainDemand resourceDemand)
                && resourceDemand.Domain == DomainIds.Resource
                && resourceDemand.RegionKey == new RegionKey(10, 10)
                && resourceDemand.ObserverCount == 1
                && engine.TryGetDemand(collision, new RegionKey(12, 10),
                    out DomainDemand collisionDemand)
                && collisionDemand.Domain == DomainIds.Collision
                && collisionDemand.ObserverCount == 1;

            return singleFact && perDomainRadius && idempotent && demandsAreDomainBound;
        }

        /// <summary>
        /// 区域枚举形状 = 切比雪夫方形并按世界边界裁剪；半径来自政策而不是共享默认值。
        /// </summary>
        internal static bool Test_DPE02_EnumerationIsChebyshevAndWorldClipped()
        {
            var engine = new DemandProjectionEngine(new ObserverSpatialAuthority());
            DemandPolicy policy = ResourcePolicy(3);
            engine.Register(policy);

            DomainDemandProjection interior = engine.Observe(policy, Presence(100UL, 1001UL, 30, 30));
            bool square = interior.EnteredRegions.Length == 49
                && ContainsRegion(interior.EnteredRegions, 27, 27)
                && ContainsRegion(interior.EnteredRegions, 33, 33)
                && !ContainsRegion(interior.EnteredRegions, 34, 30)
                && !ContainsRegion(interior.EnteredRegions, 30, 34);

            DomainDemandProjection lowCorner = engine.Observe(policy, Presence(200UL, 2001UL, 0, 0));
            bool clippedLow = lowCorner.EnteredRegions.Length == 16
                && ContainsRegion(lowCorner.EnteredRegions, 0, 0)
                && ContainsRegion(lowCorner.EnteredRegions, 3, 3);

            DomainDemandProjection edge = engine.Observe(policy, Presence(300UL, 3001UL, 0, 30));
            bool clippedEdge = edge.EnteredRegions.Length == 28
                && ContainsRegion(edge.EnteredRegions, 0, 30);

            DemandPolicy radiusZero = ResourcePolicy(0);
            var zeroEngine = new DemandProjectionEngine(new ObserverSpatialAuthority());
            zeroEngine.Register(radiusZero);
            bool singleRegion = zeroEngine.Observe(radiusZero, Presence(400UL, 4001UL, 10, 10))
                .EnteredRegions.Length == 1;

            return square && clippedLow && clippedEdge && singleRegion;
        }

        /// <summary>
        /// 去重与计数：同区观察者共享同一区域集合但计数累加；一人移出后计数回落，
        /// 交集区域仍保留两人计数。
        /// </summary>
        internal static bool Test_DPE03_OverlappingObserversDedupAndCount()
        {
            var engine = new DemandProjectionEngine(new ObserverSpatialAuthority());
            DemandPolicy policy = ResourcePolicy(1);
            engine.Register(policy);

            DomainDemandProjection first = engine.Observe(policy, Presence(100UL, 1001UL, 10, 10));
            DomainDemandProjection second = engine.Observe(policy, Presence(200UL, 2001UL, 10, 10));
            int unionRegions = engine.GetDemandRegionCount(policy);
            bool deduplicated = first.EnteredRegions.Length == 9
                && second.EnteredRegions.Length == 9
                && unionRegions == 9
                && engine.TryGetDemand(policy, new RegionKey(10, 10), out DomainDemand both)
                && both.ObserverCount == 2;

            // 100 右移一格：左列只余 200，右列只余 100，中列仍是两人交集。
            DomainDemandProjection moved = engine.Observe(policy, Presence(100UL, 1001UL, 11, 10));
            bool countsFollowMovement = moved.EnteredRegions.Length == 3
                && moved.ExitedRegions.Length == 3
                && ContainsRegion(moved.EnteredRegions, 12, 10)
                && ContainsRegion(moved.ExitedRegions, 9, 10)
                && engine.TryGetDemand(policy, new RegionKey(9, 10), out DomainDemand leftOnly)
                && leftOnly.ObserverCount == 1
                && engine.TryGetDemand(policy, new RegionKey(12, 10), out DomainDemand rightOnly)
                && rightOnly.ObserverCount == 1
                && engine.TryGetDemand(policy, new RegionKey(11, 10), out DomainDemand overlap)
                && overlap.ObserverCount == 2
                && engine.GetDemandRegionCount(policy) == 12;

            return deduplicated && countsFollowMovement;
        }

        /// <summary>
        /// 进入/退出差异随移动产生，且差异携带领域身份、观察者与连接代次；
        /// 同位置重复观测不产生差异。
        /// </summary>
        internal static bool Test_DPE04_EnterExitDiffFollowsMovement()
        {
            var engine = new DemandProjectionEngine(new ObserverSpatialAuthority());
            DemandPolicy policy = ResourcePolicy(1);
            engine.Register(policy);

            DomainDemandProjection entered = engine.Observe(policy, Presence(100UL, 1001UL, 10, 10));
            DomainDemandProjection moved = engine.Observe(policy, Presence(100UL, 1001UL, 10, 11));
            DomainDemandProjection steady = engine.Observe(policy, Presence(100UL, 1001UL, 10, 11));

            bool identity = entered.Domain == DomainIds.Resource
                && entered.ObserverId == 100UL
                && entered.ConnectionToken == 1001UL;
            bool diffShape = entered.EnteredRegions.Length == 9
                && entered.ExitedRegions.Length == 0
                && moved.EnteredRegions.Length == 3
                && moved.ExitedRegions.Length == 3
                && ContainsRegion(moved.EnteredRegions, 9, 12)
                && ContainsRegion(moved.ExitedRegions, 9, 9)
                && !ContainsRegion(moved.ExitedRegions, 10, 10);
            bool steadyState = !steady.HasChanges;

            return identity && diffShape && steadyState;
        }

        /// <summary>
        /// 连接代次失效：重连（新连接代次）先退出旧事实的全部区域，再进入新位置区域；
        /// 旧区域不再有需求。
        /// </summary>
        internal static bool Test_DPE05_ConnectionGenerationInvalidatesOldRegions()
        {
            var engine = new DemandProjectionEngine(new ObserverSpatialAuthority());
            DemandPolicy policy = ResourcePolicy(1);
            engine.Register(policy);

            engine.Observe(policy, Presence(100UL, 1001UL, 10, 10));
            DomainDemandProjection reconnected = engine.Observe(policy, Presence(100UL, 2002UL, 20, 20));

            bool oldRegionsExited = reconnected.ExitedRegions.Length == 9
                && ContainsRegion(reconnected.ExitedRegions, 9, 9)
                && ContainsRegion(reconnected.ExitedRegions, 11, 11);
            bool newRegionsEntered = reconnected.EnteredRegions.Length == 9
                && ContainsRegion(reconnected.EnteredRegions, 19, 19)
                && ContainsRegion(reconnected.EnteredRegions, 21, 21)
                && reconnected.ConnectionToken == 2002UL;
            bool oldDemandGone = !engine.TryGetDemand(policy, new RegionKey(10, 10), out _)
                && engine.TryGetDemand(policy, new RegionKey(20, 20), out DomainDemand current)
                && current.ObserverCount == 1
                && engine.GetDemandRegionCount(policy) == 9;

            return oldRegionsExited && newRegionsEntered && oldDemandGone;
        }

        /// <summary>
        /// 领域隔离：一个领域的需求不授予另一个领域的执行；政策必须按 Domain Id 唯一注册，
        /// 未注册或同域不同实例的政策都被拒绝。
        /// </summary>
        internal static bool Test_DPE06_DomainDemandIsNotGrantedAcrossDomains()
        {
            var engine = new DemandProjectionEngine(new ObserverSpatialAuthority());
            DemandPolicy resource = ResourcePolicy(1);
            DemandPolicy collision = CollisionPolicy(1);
            engine.Register(resource);

            bool unregisteredRejected = Throws(() =>
                engine.Observe(collision, Presence(100UL, 1001UL, 10, 10)));
            bool demandNotGranted = !engine.IsRegistered(collision.Domain)
                && !Throws(() => engine.Register(collision));

            engine.Observe(resource, Presence(100UL, 1001UL, 10, 10));
            bool independentDomains = engine.TryGetDemand(resource, new RegionKey(10, 10), out DomainDemand own)
                && own.Domain == DomainIds.Resource
                && !engine.TryGetDemand(collision, new RegionKey(10, 10), out _);

            DemandPolicy duplicate = ResourcePolicy(1);
            bool duplicateRejected = Throws(() => engine.Register(duplicate))
                && Throws(() => engine.Observe(duplicate, Presence(200UL, 2001UL, 10, 10)));

            return unregisteredRejected && demandNotGranted && independentDomains && duplicateRejected;
        }

        /// <summary>
        /// 领域声明的资格由引擎执行，且资格不合格不等于确认离开：既有贡献进入
        /// Deferred Observer Demand——不发退出差异、计数保留（不视为零需求）、事实保留；
        /// 重新合格恢复原贡献，确认离开（RemoveObserver）才清理。
        /// </summary>
        internal static bool Test_DPE07_IneligibleObserverDefersInsteadOfReleasing()
        {
            var authority = new ObserverSpatialAuthority();
            var engine = new DemandProjectionEngine(authority);
            DemandPolicy policy = ResourcePolicy(0);
            engine.Register(policy);
            RegionKey regionKey = new RegionKey(10, 10);

            DomainDemandProjection eligible = engine.Observe(policy, Presence(100UL, 1001UL, 10, 10));
            bool enteredWhenEligible = eligible.EnteredRegions.Length == 1
                && engine.TryGetDemand(policy, regionKey, out _);

            DomainDemandProjection ineligible =
                engine.Observe(policy, Presence(100UL, 1001UL, 10, 10, authorized: false));
            bool deferredNotReleased = !ineligible.HasChanges
                && ineligible.ExitedRegions.Length == 0
                && ineligible.EnteredRegions.Length == 0
                && engine.TryGetDemand(policy, regionKey, out DomainDemand held)
                && held.ObserverCount == 1
                && engine.GetDemandRegionCount(policy) == 1
                && authority.TryGet(100UL, out ObserverPresence deferredFact)
                && !deferredFact.GameplayAuthorized;

            DomainDemandProjection reeligible = engine.Observe(policy, Presence(100UL, 1001UL, 10, 10));
            bool unchangedOnRecovery = !reeligible.HasChanges
                && engine.TryGetDemand(policy, regionKey, out _);

            // 新观察者一开始就不合格：不产生需求，也不产生退出差异。
            DomainDemandProjection neverEligible =
                engine.Observe(policy, Presence(200UL, 2001UL, 30, 30, authorized: false));
            bool ineligibleNewcomerProjectsNothing = !neverEligible.HasChanges
                && engine.GetDemandRegionCount(policy) == 1;

            DomainDemandProjection removed = engine.RemoveObserver(policy, 100UL);
            bool confirmedLeaveReleases = removed.HasChanges
                && removed.ExitedRegions.Length == 1
                && engine.GetDemandRegionCount(policy) == 0;

            return enteredWhenEligible && deferredNotReleased && unchangedOnRecovery
                && ineligibleNewcomerProjectsNothing && confirmedLeaveReleases;
        }
        /// <summary>
        /// 声明面失败闭合：未实现的形状、缺失 Domain Id、零世界尺寸、空半径来源、
        /// 零观察者身份都不得被接受为「默认值」。
        /// </summary>
        internal static bool Test_DPE08_DeclarationsFailClosed()
        {
            bool undefinedShape = Throws(() => new DemandPolicy(
                DomainIds.Resource, 1, 64, (EDemandRegionShape)99, RadiusSource, presence => true));
            bool missingDomain = Throws(() => new DemandPolicy(
                default(DomainId), 1, 64, EDemandRegionShape.ChebyshevSquare2D, RadiusSource,
                presence => true));
            bool zeroWorld = Throws(() => new DemandPolicy(
                DomainIds.Resource, 1, 0, EDemandRegionShape.ChebyshevSquare2D, RadiusSource,
                presence => true));
            bool emptySource = Throws(() => new DemandPolicy(
                DomainIds.Resource, 1, 64, EDemandRegionShape.ChebyshevSquare2D, "  ",
                presence => true));
            bool zeroObserver = Throws(() => new ObserverPresence(
                0UL, 1001UL, new RegionKey(1, 1), true));
            bool zeroConnection = Throws(() => new ObserverPresence(
                100UL, 0UL, new RegionKey(1, 1), true));
            bool undefinedDemand = Throws(() => new DomainDemand(default(DomainId), new RegionKey(1, 1), 1));
            bool negativeCount = Throws(() => new DomainDemand(DomainIds.Resource, new RegionKey(1, 1), -1));

            bool validConstructs = !Throws(() => ResourcePolicy(3).ToString());
            DemandPolicy valid = ResourcePolicy(3);
            bool declaredFieldsHonored = valid.Domain == DomainIds.Resource
                && valid.Radius == 3
                && valid.WorldSize == 64
                && valid.Shape == EDemandRegionShape.ChebyshevSquare2D
                && valid.RadiusSource == RadiusSource
                && valid.IsEligible(Presence(1UL, 1UL, 1, 1))
                && !valid.IsEligible(Presence(1UL, 1UL, 1, 1, authorized: false));

            return undefinedShape && missingDomain && zeroWorld && emptySource && zeroObserver
                && zeroConnection && undefinedDemand && negativeCount
                && validConstructs && declaredFieldsHonored;
        }

        /// <summary>
        /// 移除观察者释放该领域全部区域需求（退出的连接代次取已存事实），重复移除幂等；
        /// 清域只清领域投影，空间事实仍归控制面所有。
        /// </summary>
        internal static bool Test_DPE09_RemovalReleasesDomainDemand()
        {
            var authority = new ObserverSpatialAuthority();
            var engine = new DemandProjectionEngine(authority);
            DemandPolicy policy = ResourcePolicy(1);
            engine.Register(policy);
            engine.Observe(policy, Presence(100UL, 1001UL, 10, 10));

            DomainDemandProjection removed = engine.RemoveObserver(policy, 100UL);
            DomainDemandProjection repeat = engine.RemoveObserver(policy, 100UL);

            bool releasedAllRegions = removed.HasChanges
                && removed.ExitedRegions.Length == 9
                && removed.ConnectionToken == 1001UL
                && removed.ObserverId == 100UL
                && authority.Count == 0
                && engine.GetDemandRegionCount(policy) == 0
                && !engine.TryGetDemand(policy, new RegionKey(10, 10), out _);
            bool idempotent = !repeat.HasChanges;

            engine.Observe(policy, Presence(100UL, 1002UL, 10, 10));
            engine.EndSession();
            bool sessionResetClearsBoth = engine.GetDemandRegionCount(policy) == 0
                && authority.Count == 0;

            return releasedAllRegions && idempotent && sessionResetClearsBoth;
        }

        /// <summary>
        /// 补偿路径：领域投影状态可还原到既有快照（观察者区域集合），还原后计数与
        /// 差异重新自洽——这是域内事务回滚依赖的接缝。
        /// </summary>
        internal static bool Test_DPE10_RestoreObserverRegionsRebuildsProjection()
        {
            var engine = new DemandProjectionEngine(new ObserverSpatialAuthority());
            DemandPolicy policy = ResourcePolicy(1);
            engine.Register(policy);
            engine.Observe(policy, Presence(100UL, 1001UL, 10, 10));

            HashSet<RegionKey> snapshot = engine.GetActiveRegions(policy, 100UL);
            engine.Observe(policy, Presence(100UL, 1001UL, 20, 20));
            bool movedAway = engine.GetActiveRegions(policy, 100UL).Contains(new RegionKey(20, 20))
                && !engine.TryGetDemand(policy, new RegionKey(10, 10), out _);

            engine.RestoreObserverRegions(policy, 100UL, 1001UL, snapshot);
            bool restored = engine.GetActiveRegions(policy, 100UL).Contains(new RegionKey(10, 10))
                && engine.GetActiveRegions(policy, 100UL).Count == snapshot.Count
                && engine.TryGetDemand(policy, new RegionKey(10, 10), out DomainDemand demand)
                && demand.ObserverCount == 1
                && engine.GetDemandRegionCount(policy) == snapshot.Count
                && !engine.TryGetDemand(policy, new RegionKey(20, 20), out _);

            DomainDemandProjection steady = engine.Observe(policy, Presence(100UL, 1001UL, 10, 10));
            return movedAway && restored && !steady.HasChanges;
        }

        /// <summary>
        /// 暂缓中的观察者发生连接代次变化：代次失效是允许的清理触发——同代次保留既有贡献，
        /// 新代次则清理旧代次贡献（不得永久残留），代次确定后重新合格正常重建。
        /// </summary>
        internal static bool Test_DPE11_DeferredObserverGenerationChangeReleases()
        {
            var authority = new ObserverSpatialAuthority();
            var engine = new DemandProjectionEngine(authority);
            DemandPolicy policy = ResourcePolicy(1);
            engine.Register(policy);

            engine.Observe(policy, Presence(100UL, 1001UL, 10, 10));
            DomainDemandProjection heldSameGeneration =
                engine.Observe(policy, Presence(100UL, 1001UL, 10, 10, authorized: false));
            bool deferredOnSameGeneration = !heldSameGeneration.HasChanges
                && engine.GetDemandRegionCount(policy) == 9;

            DomainDemandProjection invalidated =
                engine.Observe(policy, Presence(100UL, 2002UL, 20, 20, authorized: false));
            bool oldGenerationReleased = invalidated.ExitedRegions.Length == 9
                && ContainsRegion(invalidated.ExitedRegions, 9, 9)
                && invalidated.EnteredRegions.Length == 0
                && engine.GetDemandRegionCount(policy) == 0
                && !engine.TryGetDemand(policy, new RegionKey(10, 10), out _)
                && authority.TryGet(100UL, out ObserverPresence current)
                && current.ConnectionToken == 2002UL;

            DomainDemandProjection recovered = engine.Observe(policy, Presence(100UL, 2002UL, 20, 20));
            bool rebuiltOnRecovery = recovered.EnteredRegions.Length == 9
                && ContainsRegion(recovered.EnteredRegions, 20, 20)
                && engine.GetDemandRegionCount(policy) == 9;

            return deferredOnSameGeneration && oldGenerationReleased && rebuiltOnRecovery;
        }

        internal static bool Throws(Action action)        {
            try
            {
                action();
                return false;
            }
            catch (Exception)
            {
                return true;
            }
        }
    }
}
