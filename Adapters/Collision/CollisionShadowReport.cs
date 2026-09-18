using System;
using System.Collections.Generic;
using SteamP2PFriends.Core.Identity;

namespace SteamP2PFriends.Adapters.Collision
{
    /// <summary>差异分类。前六个是预期差异，其余是禁止差异。</summary>
    public enum ECollisionShadowDifferenceKind
    {
        /// <summary>预期：只有新侧覆盖，且由 Host（本地玩家）贡献——旧 Writer 按设计排除 Host。</summary>
        HostAddedCoverage = 1,

        /// <summary>
        /// 预期：只有旧侧覆盖，且没有任何「在场且合格」的认领者仍需要它——这类覆盖随旧 Writer
        /// 一起退出 Collision 操作集合（旧 Writer 的区域覆盖是区域粒度的，它的资源侧刷新与这份
        /// 覆盖同属一条路径；本类不宣称每个区域都实际发生过树/矿写入）。
        /// 具体原因由 <see cref="CollisionShadowDifference.Reason"/> 区分：
        /// `pending-guest`（在场但未获玩法资格）/ `deferred-sample`（本拍样本不可用）/
        /// `departed-observer`（已不在 canonical 事实里）。
        /// </summary>
        ResourceCoupledCoverageExit = 2,

        /// <summary>预期：只有新侧覆盖，且由在场且合格的 Guest 贡献——canonical 生命周期比旧扫描更稳定。</summary>
        CanonicalLifecycleStability = 3,

        /// <summary>禁止：只有旧侧覆盖，而一个在场且合格的 Guest 仍需要该区——新路径会误放它。</summary>
        AuthorizedGuestMissingRegion = 4,

        /// <summary>禁止：观察者事实没变，区域却在两拍之间进出——静止时的无原因抖动。</summary>
        StaticDemandChurn = 5,

        /// <summary>禁止：区域退出时，另一个事实未变且仍在场的合格认领者仍覆盖它——一个人的离开放掉了另一个人的需求。</summary>
        CrossObserverRelease = 6,

        /// <summary>禁止：新侧有区域但没有任何认领者——投影凭空造出需求。</summary>
        UnattributedDemand = 7,

        /// <summary>禁止：Region Key 越出世界边界。</summary>
        OutOfBoundsRegionKey = 8,

        /// <summary>禁止：新侧区域集合不是 Collision 政策算出的（例如 Resource 需求被当成 Collision 需求）。</summary>
        ForeignDomain = 9,

        /// <summary>
        /// 预期：只有新侧覆盖，且只被当前不合格的认领者覆盖——那是资格不合格时被保留的既有贡献
        /// （Deferred Observer Demand），不是新造出来的需求。
        /// </summary>
        DeferredContributionRetained = 10,

        /// <summary>
        /// 禁止：旧侧覆盖无法归因于旧 Writer 自己的任何认领者——两份旧状态自相矛盾，差异不可解释。
        /// 失败闭合，不当作预期差异放过去。
        /// </summary>
        UnexplainedLegacyCoverage = 12
    }

    /// <summary>差异处置：预期差异需被解释，禁止差异必须为零。</summary>
    public enum ECollisionShadowDisposition : byte
    {
        Expected = 1,
        Forbidden = 2
    }

    /// <summary>
    /// 一条已分类的差异。聚合键是 Domain Id + Region Key；归因带观察者身份与连接代次
    /// （旧侧没有连接代次，其归因代次为 0）。
    /// </summary>
    public sealed class CollisionShadowDifference
    {
        internal CollisionShadowDifference(
            DomainId domain,
            RegionKey regionKey,
            ECollisionShadowDifferenceKind kind,
            ECollisionShadowDisposition disposition,
            ulong[] observerIds,
            ulong[] connectionTokens,
            string reason)
        {
            Domain = domain;
            RegionKey = regionKey;
            Kind = kind;
            Disposition = disposition;
            ObserverIds = observerIds ?? Array.Empty<ulong>();
            ConnectionTokens = connectionTokens ?? Array.Empty<ulong>();
            Reason = string.IsNullOrEmpty(reason) ? "none" : reason;
        }

        public DomainId Domain { get; }
        public RegionKey RegionKey { get; }
        public ECollisionShadowDifferenceKind Kind { get; }
        public ECollisionShadowDisposition Disposition { get; }
        public ulong[] ObserverIds { get; }
        public ulong[] ConnectionTokens { get; }

        /// <summary>
        /// 该差异的原因词（同一分类下的可解释细节，例如 `pending-guest` / `deferred-sample` /
        /// `departed-observer` / `host-local-player` 等）；无更细原因时为 `none`。
        /// </summary>
        public string Reason { get; }

        public bool IsForbidden => Disposition == ECollisionShadowDisposition.Forbidden;

        public override string ToString() =>
            $"{Domain}@{RegionKey} {Kind}/{Disposition} reason={Reason} observers={ObserverIds.Length}";
    }

    /// <summary>一拍对照的结论：全部差异都已分类；禁止差异计数必须为零才算对照干净。</summary>
    public sealed class CollisionShadowReport
    {
        internal CollisionShadowReport(
            IReadOnlyList<CollisionShadowDifference> differences,
            int inBothCount,
            int expectedCount,
            int forbiddenCount)
        {
            Differences = differences ?? Array.Empty<CollisionShadowDifference>();
            InBothCount = inBothCount;
            ExpectedCount = expectedCount;
            ForbiddenCount = forbiddenCount;
        }

        public IReadOnlyList<CollisionShadowDifference> Differences { get; }

        /// <summary>两侧都有的区域数（不是「相等」的证明，只是对照口径的一个计数）。</summary>
        public int InBothCount { get; }

        public int ExpectedCount { get; }
        public int ForbiddenCount { get; }

        public bool HasForbiddenDifferences => ForbiddenCount > 0;

        public override string ToString() =>
            $"inBoth={InBothCount} expected={ExpectedCount} forbidden={ForbiddenCount} " +
            $"differences={Differences.Count}";
    }
}
