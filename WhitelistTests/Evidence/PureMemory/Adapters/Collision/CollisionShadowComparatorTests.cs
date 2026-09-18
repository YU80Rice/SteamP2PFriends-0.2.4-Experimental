using SteamP2PFriends.Adapters.Collision;
using SteamP2PFriends.Core.Identity;
using SteamP2PFriends.MultiObserver.Demand;
using System;
using System.Collections.Generic;

namespace SteamP2PFriends.WhitelistTests
{
    /// <summary>
    /// 票 05 的只读影子对照契约（PureMemory，挂在本票新增的**只读对照接缝**上——
    /// 它不是行为接缝：唯一对外行为接缝仍是 Lifecycle Orchestration Engine，本票既没有
    /// Collision Domain Execution Port，也没有第二套生命周期状态机，比较器只做只读分类）。
    /// 新旧投影按观察者贡献与聚合 Domain Id + Region Key 逐条分类，所有差异必须可解释；
    /// 预期差异（Host 新增覆盖、旧侧陈旧条目退出、canonical 生命周期更稳定、Pending Guest
    /// 覆盖退出、暂缓贡献保留）与禁止差异（授权 Guest 缺区、需求抖动、错误释放、凭空需求、
    /// 越界 Region Key、异域需求、无法归因的旧覆盖）各自归类。比较器只读、无副作用、不持有跨拍状态。
    /// </summary>
    internal static class CollisionShadowComparatorTests
    {
        private const byte WorldSize = 64;
        private const byte Radius = 2;

        internal static DemandPolicy Policy()
        {
            return CollisionDemandPolicy.Create(Radius, WorldSize);
        }

        internal static CollisionShadowClaim Claim(
            ulong observerId, ulong connectionToken, byte x, byte y, bool host = false, bool authorized = true)
        {
            return new CollisionShadowClaim(
                new ObserverPresence(observerId, connectionToken, new RegionKey(x, y), authorized), host);
        }

        internal static CollisionShadowLegacyClaim LegacyClaim(
            ulong observerId, byte x, byte y,
            ECollisionShadowLegacyState state = ECollisionShadowLegacyState.Eligible)
        {
            return new CollisionShadowLegacyClaim(observerId, new RegionKey(x, y), state);
        }

        internal static CollisionShadowFrame Frame(
            RegionKey[] newRegions,
            RegionKey[] legacyRegions,
            CollisionShadowClaim[] claims,
            CollisionShadowClaim[] previousClaims = null,
            RegionKey[] previousNewRegions = null,
            CollisionShadowLegacyClaim[] legacyClaims = null,
            DomainId domain = default(DomainId),
            byte worldSize = WorldSize)
        {
            return new CollisionShadowFrame(
                domain.IsDefined ? domain : DomainIds.Collision,
                worldSize,
                newRegions,
                previousNewRegions ?? newRegions,
                legacyRegions,
                claims,
                previousClaims ?? claims,
                legacyClaims ?? new CollisionShadowLegacyClaim[0]);
        }

        /// <summary>按切比雪夫方形并按世界边界裁剪展开一个观察者的区域集合（对照侧同样口径）。</summary>
        internal static RegionKey[] Square(byte centerX, byte centerY, byte radius = Radius)
        {
            HashSet<RegionKey> regions = SpatialIndexOf(centerX, centerY, radius);
            var result = new RegionKey[regions.Count];
            regions.CopyTo(result);
            Array.Sort(result, CompareRegions);
            return result;
        }

        internal static RegionKey[] Union(params RegionKey[][] sets)
        {
            var merged = new HashSet<RegionKey>();
            foreach (RegionKey[] set in sets)
            {
                foreach (RegionKey region in set) merged.Add(region);
            }
            var result = new RegionKey[merged.Count];
            merged.CopyTo(result);
            Array.Sort(result, CompareRegions);
            return result;
        }

        private static HashSet<RegionKey> SpatialIndexOf(byte centerX, byte centerY, byte radius)
        {
            var regions = new HashSet<RegionKey>();
            for (int x = Math.Max(0, centerX - radius); x <= Math.Min(WorldSize - 1, centerX + radius); x++)
            {
                for (int y = Math.Max(0, centerY - radius); y <= Math.Min(WorldSize - 1, centerY + radius); y++)
                {
                    regions.Add(new RegionKey((byte)x, (byte)y));
                }
            }
            return regions;
        }

        private static int CompareRegions(RegionKey left, RegionKey right)
        {
            return left.Packed.CompareTo(right.Packed);
        }

        internal static bool HasDifference(
            CollisionShadowReport report, ECollisionShadowDifferenceKind kind, byte x, byte y)
        {
            return CountDifferences(report, kind, new RegionKey(x, y)) == 1;
        }

        internal static int CountDifferences(
            CollisionShadowReport report, ECollisionShadowDifferenceKind kind, RegionKey region)
        {
            int count = 0;
            foreach (CollisionShadowDifference difference in report.Differences)
            {
                if (difference.Kind == kind && difference.RegionKey == region) count++;
            }
            return count;
        }

        internal static int CountKind(CollisionShadowReport report, ECollisionShadowDifferenceKind kind)
        {
            int count = 0;
            foreach (CollisionShadowDifference difference in report.Differences)
            {
                if (difference.Kind == kind) count++;
            }
            return count;
        }

        internal static bool AllDifferencesAre(
            CollisionShadowReport report, ECollisionShadowDifferenceKind kind, ECollisionShadowDisposition disposition)
        {
            if (report.Differences.Count == 0) return false;
            foreach (CollisionShadowDifference difference in report.Differences)
            {
                if (difference.Kind != kind || difference.Disposition != disposition) return false;
            }
            return true;
        }

        internal static CollisionShadowDifference Find(
            CollisionShadowReport report, ECollisionShadowDifferenceKind kind, byte x, byte y)
        {
            foreach (CollisionShadowDifference difference in report.Differences)
            {
                if (difference.Kind == kind && difference.RegionKey == new RegionKey(x, y)) return difference;
            }
            return null;
        }

        internal static bool AttributedTo(CollisionShadowDifference difference, ulong observerId, ulong token)
        {
            if (difference == null) return false;
            for (int index = 0; index < difference.ObserverIds.Length; index++)
            {
                if (difference.ObserverIds[index] == observerId
                    && index < difference.ConnectionTokens.Length
                    && difference.ConnectionTokens[index] == token)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Host 只出现在新侧 = 预期差异：旧 Writer 的 remote-only 语义按设计不覆盖本地玩家，
        /// 新政策必须包含 Host；该差异归因到 Host 的观察者与连接代次并标为预期。
        /// </summary>
        internal static bool Test_CSC02_HostAddedCoverageIsExpected()
        {
            DemandPolicy policy = Policy();
            RegionKey[] hostSquare = Square(10, 10);
            RegionKey[] guestSquare = Square(30, 30);
            CollisionShadowClaim[] claims =
            {
                Claim(900UL, 9001UL, 10, 10, host: true),
                Claim(100UL, 1001UL, 30, 30)
            };

            CollisionShadowReport report = CollisionShadowComparator.Compare(policy, Frame(
                newRegions: Union(hostSquare, guestSquare),
                legacyRegions: guestSquare,
                claims: claims,
                legacyClaims: new[] { LegacyClaim(100UL, 30, 30) }));

            return report.ForbiddenCount == 0
                && !report.HasForbiddenDifferences
                && CountKind(report, ECollisionShadowDifferenceKind.HostAddedCoverage) == hostSquare.Length
                && AllDifferencesAre(report, ECollisionShadowDifferenceKind.HostAddedCoverage,
                    ECollisionShadowDisposition.Expected)
                && AttributedTo(Find(report, ECollisionShadowDifferenceKind.HostAddedCoverage, 10, 10),
                    900UL, 9001UL)
                && CountDifferences(report, ECollisionShadowDifferenceKind.HostAddedCoverage,
                    new RegionKey(30, 30)) == 0
                && report.InBothCount == guestSquare.Length;
        }

        /// <summary>
        /// 授权 Guest 缺区 = 禁止差异：旧侧覆盖、新侧缺失，而该区仍被一个在场且合格的 Guest
        /// 认领——新路径会误放掉它。整区逐条列出，归因到该 Guest。
        /// </summary>
        internal static bool Test_CSC03_AuthorizedGuestMissingRegionIsForbidden()
        {
            DemandPolicy policy = Policy();
            RegionKey[] guestSquare = Square(10, 10);
            RegionKey[] otherSquare = Square(30, 30);
            CollisionShadowClaim[] claims = { Claim(100UL, 1001UL, 10, 10), Claim(200UL, 2001UL, 30, 30) };

            CollisionShadowReport report = CollisionShadowComparator.Compare(policy, Frame(
                newRegions: otherSquare,
                legacyRegions: Union(guestSquare, otherSquare),
                claims: claims,
                legacyClaims: new[] { LegacyClaim(100UL, 10, 10), LegacyClaim(200UL, 30, 30) }));

            return report.HasForbiddenDifferences
                && report.ForbiddenCount == guestSquare.Length
                && report.ExpectedCount == 0
                && AllDifferencesAre(report, ECollisionShadowDifferenceKind.AuthorizedGuestMissingRegion,
                    ECollisionShadowDisposition.Forbidden)
                && AttributedTo(Find(report, ECollisionShadowDifferenceKind.AuthorizedGuestMissingRegion, 10, 10),
                    100UL, 1001UL);
        }

        /// <summary>
        /// 旧侧独有且只被「在场但未获玩法资格」的观察者（Pending Guest）认领 = 预期差异
        /// （原因词 pending-guest）：Pending Guest 是否投影世界需求沿用既有 World Presence Observer
        /// 定义，本规格不改写；这类差异逐区具名并单独计数，供票 07 判读。
        /// </summary>
        internal static bool Test_CSC04_PendingGuestCoverageExitIsExpected()
        {
            DemandPolicy policy = Policy();
            RegionKey[] pendingSquare = Square(20, 20);
            CollisionShadowClaim[] claims = { Claim(700UL, 7001UL, 20, 20, authorized: false) };

            CollisionShadowReport report = CollisionShadowComparator.Compare(policy, Frame(
                newRegions: new RegionKey[0],
                legacyRegions: pendingSquare,
                claims: claims,
                legacyClaims: new[] { LegacyClaim(700UL, 20, 20, ECollisionShadowLegacyState.Pending) }));

            CollisionShadowDifference difference =
                Find(report, ECollisionShadowDifferenceKind.ResourceCoupledCoverageExit, 20, 20);
            return report.ForbiddenCount == 0
                && !report.HasForbiddenDifferences
                && AllDifferencesAre(report, ECollisionShadowDifferenceKind.ResourceCoupledCoverageExit,
                    ECollisionShadowDisposition.Expected)
                && CountDifferences(report, ECollisionShadowDifferenceKind.ResourceCoupledCoverageExit,
                    new RegionKey(20, 20)) == 1
                && difference != null
                && difference.Reason == "pending-guest"
                && AttributedTo(difference, 700UL, 0UL)
                && report.ExpectedCount == pendingSquare.Length;
        }

        /// <summary>
        /// 新侧独有且由在场合格 Guest 贡献 = 预期差异（canonical 生命周期更稳定）：
        /// 旧扫描在记录短暂不可用时会把该 Guest 整个漏掉，新投影从 canonical 事实重建它。
        /// </summary>
        internal static bool Test_CSC05_CanonicalLifecycleGapClosedIsExpected()
        {
            DemandPolicy policy = Policy();
            RegionKey[] guestSquare = Square(10, 10);
            RegionKey[] otherSquare = Square(30, 30);
            CollisionShadowClaim[] claims = { Claim(100UL, 1001UL, 10, 10), Claim(200UL, 2001UL, 30, 30) };

            CollisionShadowReport report = CollisionShadowComparator.Compare(policy, Frame(
                newRegions: Union(guestSquare, otherSquare),
                legacyRegions: otherSquare,
                claims: claims,
                legacyClaims: new[] { LegacyClaim(200UL, 30, 30) }));

            return report.ForbiddenCount == 0
                && !report.HasForbiddenDifferences
                && AllDifferencesAre(report, ECollisionShadowDifferenceKind.CanonicalLifecycleStability,
                    ECollisionShadowDisposition.Expected)
                && AttributedTo(Find(report, ECollisionShadowDifferenceKind.CanonicalLifecycleStability, 10, 10),
                    100UL, 1001UL)
                && report.InBothCount == otherSquare.Length;
        }

        /// <summary>
        /// 两侧区域集合相同时不产生任何差异：对照口径既不是「相等即通过」也不是「必须不等」，
        /// 而是 InBoth 计数 + 零差异；上一拍与当前完全相同也不触发抖动判定。
        /// </summary>
        internal static bool Test_CSC01_IdenticalProjectionsAreInBoth()
        {
            DemandPolicy policy = Policy();
            RegionKey[] shared = Square(10, 10);
            CollisionShadowClaim[] claims = { Claim(100UL, 1001UL, 10, 10) };

            CollisionShadowReport report = CollisionShadowComparator.Compare(policy, Frame(
                newRegions: shared,
                legacyRegions: shared,
                claims: claims,
                legacyClaims: new[] { LegacyClaim(100UL, 10, 10) }));

            return report.Differences.Count == 0
                && report.InBothCount == shared.Length
                && report.ExpectedCount == 0
                && report.ForbiddenCount == 0
                && !report.HasForbiddenDifferences;
        }

        /// <summary>
        /// 需求抖动 = 禁止差异：观察者事实两拍之间完全没变（同身份、同中心、同资格），
        /// 区域却从新侧消失——静止时的无原因抖动，唯一可能来自新路径自身不稳定。
        /// </summary>
        internal static bool Test_CSC06_StaticDemandChurnIsForbidden()
        {
            DemandPolicy policy = Policy();
            RegionKey[] keptSquare = Square(10, 10);
            RegionKey[] churningSquare = Square(30, 30);
            CollisionShadowClaim[] claims = { Claim(100UL, 1001UL, 10, 10), Claim(200UL, 2001UL, 30, 30) };

            CollisionShadowReport report = CollisionShadowComparator.Compare(policy, Frame(
                newRegions: keptSquare,
                legacyRegions: keptSquare,
                claims: claims,
                previousClaims: claims,
                previousNewRegions: Union(keptSquare, churningSquare)));

            return report.HasForbiddenDifferences
                && report.ExpectedCount == 0
                && AllDifferencesAre(report, ECollisionShadowDifferenceKind.StaticDemandChurn,
                    ECollisionShadowDisposition.Forbidden)
                && CountKind(report, ECollisionShadowDifferenceKind.StaticDemandChurn) == churningSquare.Length
                && AttributedTo(Find(report, ECollisionShadowDifferenceKind.StaticDemandChurn, 30, 30),
                    200UL, 2001UL);
        }

        /// <summary>
        /// 错误释放 = 禁止差异：一个 Guest 离开时把另一个仍在场且事实未变的 Guest 仍需要的区域
        /// 一起放掉。只有「离开者独有」的区域退出才算合法，两者必须在同一拍里区分开。
        /// </summary>
        internal static bool Test_CSC07_CrossObserverReleaseIsForbidden()
        {
            DemandPolicy policy = Policy();
            RegionKey[] survivorSquare = Square(10, 10);
            RegionKey[] leaverSquare = Square(11, 10);
            RegionKey[] survivorOnly = { new RegionKey(8, 8), new RegionKey(8, 9), new RegionKey(8, 10),
                new RegionKey(8, 11), new RegionKey(8, 12) };
            CollisionShadowClaim[] currentClaims = { Claim(100UL, 1001UL, 10, 10) };
            CollisionShadowClaim[] previousClaims = { Claim(100UL, 1001UL, 10, 10), Claim(200UL, 2001UL, 11, 10) };

            CollisionShadowReport report = CollisionShadowComparator.Compare(policy, Frame(
                newRegions: survivorOnly,
                legacyRegions: survivorOnly,
                claims: currentClaims,
                previousClaims: previousClaims,
                previousNewRegions: Union(survivorSquare, leaverSquare)));

            return report.HasForbiddenDifferences
                && AllDifferencesAre(report, ECollisionShadowDifferenceKind.CrossObserverRelease,
                    ECollisionShadowDisposition.Forbidden)
                && CountKind(report, ECollisionShadowDifferenceKind.CrossObserverRelease) == 20
                && AttributedTo(Find(report, ECollisionShadowDifferenceKind.CrossObserverRelease, 11, 10),
                    100UL, 1001UL)
                && CountDifferences(report, ECollisionShadowDifferenceKind.CrossObserverRelease,
                    new RegionKey(13, 10)) == 0
                && CountDifferences(report, ECollisionShadowDifferenceKind.StaticDemandChurn,
                    new RegionKey(13, 10)) == 0;
        }

        /// <summary>
        /// 凭空需求 = 禁止差异：新侧有区域，但没有任何认领者覆盖它——投影必须能被现有事实解释，
        /// 解释不出来的差异失败闭合，不当成预期差异放过去。
        /// </summary>
        internal static bool Test_CSC08_UnattributedDemandIsForbidden()
        {
            DemandPolicy policy = Policy();
            RegionKey[] invented = { new RegionKey(5, 5) };
            CollisionShadowClaim[] claims = { Claim(100UL, 1001UL, 10, 10) };

            CollisionShadowReport report = CollisionShadowComparator.Compare(policy, Frame(
                newRegions: invented,
                legacyRegions: new RegionKey[0],
                claims: claims));

            CollisionShadowDifference difference =
                Find(report, ECollisionShadowDifferenceKind.UnattributedDemand, 5, 5);
            return report.HasForbiddenDifferences
                && AllDifferencesAre(report, ECollisionShadowDifferenceKind.UnattributedDemand,
                    ECollisionShadowDisposition.Forbidden)
                && difference != null
                && difference.Reason == "no-claimant"
                && difference.ObserverIds.Length == 0;
        }

        /// <summary>
        /// 越界 Region Key = 禁止差异：世界尺寸之外的区域既不能出现在新侧也不能出现在旧侧，
        /// 出现即说明有人用了错误的编码或世界尺寸。
        /// </summary>
        internal static bool Test_CSC09_OutOfBoundsRegionKeyIsForbidden()
        {
            DemandPolicy policy = Policy();
            RegionKey[] outside = { new RegionKey(10, 3), new RegionKey(3, 10) };

            CollisionShadowReport report = CollisionShadowComparator.Compare(policy, Frame(
                newRegions: outside,
                legacyRegions: new RegionKey[0],
                claims: new[] { Claim(100UL, 1001UL, 3, 3) },
                worldSize: (byte)8));

            return report.HasForbiddenDifferences
                && report.ForbiddenCount == outside.Length
                && AllDifferencesAre(report, ECollisionShadowDifferenceKind.OutOfBoundsRegionKey,
                    ECollisionShadowDisposition.Forbidden)
                && HasDifference(report, ECollisionShadowDifferenceKind.OutOfBoundsRegionKey, 10, 3);
        }

        /// <summary>
        /// 异域需求 = 禁止差异：帧里的新侧区域集合必须来自 Collision 政策，来自别的领域
        /// （例如 Resource 需求被当成 Collision 需求）时整帧不可信，失败闭合而不是静默按相等处理。
        /// </summary>
        internal static bool Test_CSC10_ForeignDomainFailsClosed()
        {
            DemandPolicy policy = Policy();
            RegionKey[] shared = Square(10, 10);

            CollisionShadowReport report = CollisionShadowComparator.Compare(policy, Frame(
                newRegions: shared,
                legacyRegions: shared,
                claims: new[] { Claim(100UL, 1001UL, 10, 10) },
                domain: DomainIds.Resource));

            return report.HasForbiddenDifferences
                && report.ForbiddenCount == 1
                && AllDifferencesAre(report, ECollisionShadowDifferenceKind.ForeignDomain,
                    ECollisionShadowDisposition.Forbidden);
        }

        /// <summary>
        /// 差异的聚合键是 Domain Id + Region Key，归因带观察者身份与连接代次：
        /// 新侧差异带 canonical 事实的观察者与连接代次，旧侧差异只带旧 Writer 跟踪到的观察者
        /// （旧侧没有连接代次，其代次为 0，不得借用新侧代次冒充）。
        /// </summary>
        internal static bool Test_CSC11_DifferencesCarryAggregateKeyAndAttribution()
        {
            DemandPolicy policy = Policy();
            RegionKey[] hostSquare = Square(10, 10);
            RegionKey[] legacyOnly = Square(20, 20);
            CollisionShadowClaim[] claims = { Claim(900UL, 9001UL, 10, 10, host: true) };

            CollisionShadowReport report = CollisionShadowComparator.Compare(policy, Frame(
                newRegions: hostSquare,
                legacyRegions: legacyOnly,
                claims: claims,
                legacyClaims: new[] { LegacyClaim(700UL, 20, 20, ECollisionShadowLegacyState.Pending) }));

            CollisionShadowDifference hostAdded =
                Find(report, ECollisionShadowDifferenceKind.HostAddedCoverage, 10, 10);
            CollisionShadowDifference legacyExit =
                Find(report, ECollisionShadowDifferenceKind.ResourceCoupledCoverageExit, 20, 20);

            return hostAdded != null
                && hostAdded.Domain == DomainIds.Collision
                && hostAdded.RegionKey == new RegionKey(10, 10)
                && hostAdded.ObserverIds.Length == 1
                && hostAdded.ObserverIds[0] == 900UL
                && hostAdded.ConnectionTokens[0] == 9001UL
                && AttributedTo(hostAdded, 900UL, 9001UL)
                && AttributedTo(legacyExit, 700UL, 0UL)
                && legacyExit.Domain == DomainIds.Collision
                && report.ExpectedCount == hostSquare.Length + legacyOnly.Length;
        }

        /// <summary>
        /// Collision 政策从 canonical 观察者事实投影：合格观察者（Host 亦在其列）贡献需求，
        /// 未获玩法资格的 Pending Guest 不贡献也不被当成离开（事实保留在控制面），
        /// 半径来源声明为原版物件区域常量而不升格为共享默认半径。
        /// </summary>
        internal static bool Test_CSC12_CollisionPolicyIncludesHostAndDefersIneligibleGuest()
        {
            var authority = new ObserverSpatialAuthority();
            var engine = new DemandProjectionEngine(authority);
            DemandPolicy policy = CollisionDemandPolicy.Create(3, WorldSize);
            engine.Register(policy);

            DomainDemandProjection host = engine.Observe(policy,
                new ObserverPresence(900UL, 9001UL, new RegionKey(10, 10), gameplayAuthorized: true));
            bool hostContributes = host.EnteredRegions.Length == 49
                && host.Domain == DomainIds.Collision
                && engine.TryGetDemand(policy, new RegionKey(10, 10), out DomainDemand hostDemand)
                && hostDemand.Domain == DomainIds.Collision
                && hostDemand.ObserverCount == 1;

            DomainDemandProjection pending = engine.Observe(policy,
                new ObserverPresence(700UL, 7001UL, new RegionKey(30, 30), gameplayAuthorized: false));
            bool pendingDefers = !pending.HasChanges
                && engine.GetDemandRegionCount(policy) == 49
                && !engine.TryGetDemand(policy, new RegionKey(30, 30), out DomainDemand _)
                && authority.TryGet(700UL, out ObserverPresence stored)
                && !stored.GameplayAuthorized;

            bool declaration = policy.Domain == DomainIds.Collision
                && policy.Shape == EDemandRegionShape.ChebyshevSquare2D
                && policy.RadiusSource == CollisionDemandPolicy.RadiusSource
                && policy.RadiusSource == "LevelObjects.OBJECT_REGIONS"
                && policy.IsEligible(new ObserverPresence(1UL, 1UL, new RegionKey(1, 1), true))
                && !policy.IsEligible(new ObserverPresence(1UL, 1UL, new RegionKey(1, 1), false));

            return hostContributes && pendingDefers && declaration;
        }

        /// <summary>
        /// 旧侧独有且只被「已不在 canonical 事实里」的旧条目认领 = 预期差异（原因词 departed-observer）：
        /// 那是旧 Writer 自己那份远端跟踪的陈旧条目，随旧 Writer 与其资源侧写入一起退出。
        /// </summary>
        internal static bool Test_CSC13_DepartedObserverCoverageExitIsExpected()
        {
            DemandPolicy policy = Policy();
            RegionKey[] departedSquare = Square(20, 20);
            CollisionShadowClaim[] claims = { Claim(100UL, 1001UL, 10, 10) };

            CollisionShadowReport report = CollisionShadowComparator.Compare(policy, Frame(
                newRegions: new RegionKey[0],
                legacyRegions: departedSquare,
                claims: claims,
                legacyClaims: new[] { LegacyClaim(700UL, 20, 20, ECollisionShadowLegacyState.Absent) }));

            CollisionShadowDifference difference =
                Find(report, ECollisionShadowDifferenceKind.ResourceCoupledCoverageExit, 20, 20);
            return report.ForbiddenCount == 0
                && !report.HasForbiddenDifferences
                && AllDifferencesAre(report, ECollisionShadowDifferenceKind.ResourceCoupledCoverageExit,
                    ECollisionShadowDisposition.Expected)
                && difference != null
                && difference.Reason == "departed-observer"
                && AttributedTo(difference, 700UL, 0UL)
                && report.ExpectedCount == departedSquare.Length;
        }

        /// <summary>
        /// 暂缓者的保留贡献必须落在 InBoth：只要协调器把它的投影区域算进新侧（本票已如此），
        /// 该区域两侧都有、零差异——「保留的 Deferred Demand」不得被伪造成「新侧缺区」。
        /// </summary>
        internal static bool Test_CSC16_DeferredRetentionStaysInBoth()
        {
            DemandPolicy policy = Policy();
            RegionKey[] retained = Square(20, 20);

            CollisionShadowReport report = CollisionShadowComparator.Compare(policy, Frame(
                newRegions: retained,
                legacyRegions: retained,
                claims: new[] { Claim(100UL, 1001UL, 10, 10) },
                legacyClaims: new[] { LegacyClaim(700UL, 20, 20, ECollisionShadowLegacyState.Deferred) }));

            return report.Differences.Count == 0
                && report.InBothCount == retained.Length
                && report.ExpectedCount == 0
                && report.ForbiddenCount == 0;
        }

        /// <summary>
        /// 本拍样本不可用（暂缓）不等于离开：只被暂缓态认领者覆盖的旧侧区域是预期差异
        /// （原因词 deferred-sample），不是 departed-observer，也不是禁止差异——
        /// 准入故障不得被误报成「已离开」。
        /// </summary>
        internal static bool Test_CSC15_DeferredSampleIsNotDeparture()
        {
            DemandPolicy policy = Policy();
            RegionKey[] deferredSquare = Square(20, 20);

            CollisionShadowReport report = CollisionShadowComparator.Compare(policy, Frame(
                newRegions: new RegionKey[0],
                legacyRegions: deferredSquare,
                claims: new[] { Claim(100UL, 1001UL, 10, 10) },
                legacyClaims: new[] { LegacyClaim(700UL, 20, 20, ECollisionShadowLegacyState.Deferred) }));

            CollisionShadowDifference difference =
                Find(report, ECollisionShadowDifferenceKind.ResourceCoupledCoverageExit, 20, 20);
            return report.ForbiddenCount == 0
                && !report.HasForbiddenDifferences
                && difference != null
                && difference.Reason == "deferred-sample"
                && CountKind(report, ECollisionShadowDifferenceKind.ResourceCoupledCoverageExit)
                    == deferredSquare.Length;
        }

        /// <summary>
        /// 暂缓观察者的保留区域必须能被事实解释：它带着暂缓认领（最后已知事实）出现在帧里，
        /// 只在新侧时归类为保留的 Deferred Demand（预期），而不是「凭空需求」这种禁止差异——
        /// 准入故障不得被误报成投影造假。
        /// </summary>
        internal static bool Test_CSC17_DeferredClaimantExplainsRetainedRegion()
        {
            DemandPolicy policy = Policy();
            RegionKey[] retained = Square(20, 20);
            var deferredClaim = new CollisionShadowClaim(
                new ObserverPresence(700UL, 7001UL, new RegionKey(20, 20), gameplayAuthorized: true),
                isHost: false, isDeferred: true);

            CollisionShadowReport report = CollisionShadowComparator.Compare(policy, Frame(
                newRegions: retained,
                legacyRegions: new RegionKey[0],
                claims: new[] { deferredClaim }));

            CollisionShadowDifference difference =
                Find(report, ECollisionShadowDifferenceKind.DeferredContributionRetained, 20, 20);
            return report.ForbiddenCount == 0
                && !report.HasForbiddenDifferences
                && difference != null
                && difference.Reason == "deferred-contribution"
                && AttributedTo(difference, 700UL, 7001UL)
                && report.ExpectedCount == retained.Length;
        }

        /// <summary>
        /// 旧侧覆盖无法归因于旧 Writer 自己的任何认领者 = 禁止差异：两份旧状态自相矛盾
        /// （旧覆盖只能由旧 Writer 自己的远端跟踪派生），差异不可解释，失败闭合而不是放行。
        /// </summary>
        internal static bool Test_CSC14_UnexplainedLegacyCoverageIsForbidden()
        {
            DemandPolicy policy = Policy();
            RegionKey[] orphaned = { new RegionKey(50, 50), new RegionKey(51, 51) };

            CollisionShadowReport report = CollisionShadowComparator.Compare(policy, Frame(
                newRegions: new RegionKey[0],
                legacyRegions: orphaned,
                claims: new[] { Claim(100UL, 1001UL, 10, 10) }));

            CollisionShadowDifference difference =
                Find(report, ECollisionShadowDifferenceKind.UnexplainedLegacyCoverage, 50, 50);
            return report.HasForbiddenDifferences
                && report.ForbiddenCount == orphaned.Length
                && AllDifferencesAre(report, ECollisionShadowDifferenceKind.UnexplainedLegacyCoverage,
                    ECollisionShadowDisposition.Forbidden)
                && difference != null
                && difference.Reason == "no-legacy-claimant"
                && difference.ObserverIds.Length == 0;
        }
    }
}
