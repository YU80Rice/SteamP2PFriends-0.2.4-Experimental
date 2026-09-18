using System;
using System.Collections.Generic;
using SteamP2PFriends.Core.Identity;
using SteamP2PFriends.MultiObserver.Demand;

namespace SteamP2PFriends.Adapters.Collision
{
    /// <summary>
    /// Collision 只读影子对照分类器：把「共享投影算出的 Collision demand」与「旧 RemoteCoverage
    /// Writer 当前覆盖」按观察者贡献与聚合 Domain Id + Region Key 逐条分类。
    ///
    /// 它是纯函数：不读原生状态、不写任何东西、不持有跨拍状态（上一拍由调用方随帧传入）。
    /// 它只把旧覆盖当作对照物而不是金标——两边相等不是通过条件，所有差异可分类、可解释才是；
    /// 分类不出来的差异失败闭合为禁止差异。
    /// </summary>
    public static class CollisionShadowComparator
    {
        /// <summary>
        /// 比较一拍。政策必须属于 Collision 且帧内区域集合也必须来自该政策，
        /// 否则整拍失败闭合为禁止差异（ForeignDomain），不静默按相等处理。
        /// </summary>
        public static CollisionShadowReport Compare(DemandPolicy policy, CollisionShadowFrame frame)
        {
            if (policy == null) throw new ArgumentNullException(nameof(policy));
            if (frame == null) throw new ArgumentNullException(nameof(frame));

            if (policy.Domain != DomainIds.Collision || frame.Domain != policy.Domain)
            {
                // 异域需求：整帧不可信，区域键在这里没有意义，只报一条禁止差异。
                var foreign = new List<CollisionShadowDifference>
                {
                    new CollisionShadowDifference(
                        frame.Domain,
                        default(RegionKey),
                        ECollisionShadowDifferenceKind.ForeignDomain,
                        ECollisionShadowDisposition.Forbidden,
                        Array.Empty<ulong>(),
                        Array.Empty<ulong>(),
                        "foreign-domain")
                };
                return new CollisionShadowReport(foreign, 0, 0, 1);
            }

            var differences = new List<CollisionShadowDifference>();
            var newRegions = new HashSet<RegionKey>(frame.NewRegions);
            var legacyRegions = new HashSet<RegionKey>(frame.LegacyRegions);
            var previousRegions = new HashSet<RegionKey>(frame.PreviousNewRegions);

            AddOutOfBounds(policy, frame, newRegions, legacyRegions, previousRegions, differences);

            int inBoth = 0;
            foreach (RegionKey region in newRegions)
            {
                if (IsOutOfBounds(region, frame.WorldSize)) continue;
                if (legacyRegions.Contains(region))
                {
                    inBoth++;
                    continue;
                }
                differences.Add(ClassifyNewOnly(policy, frame, region));
            }

            foreach (RegionKey region in legacyRegions)
            {
                if (IsOutOfBounds(region, frame.WorldSize)) continue;
                if (newRegions.Contains(region)) continue;
                differences.Add(ClassifyLegacyOnly(policy, frame, region));
            }

            foreach (RegionKey region in SymmetricDifference(newRegions, previousRegions))
            {
                if (IsOutOfBounds(region, frame.WorldSize)) continue;
                CollisionShadowDifference temporal =
                    ClassifyTemporal(policy, frame, region, newRegions, previousRegions);
                if (temporal != null) differences.Add(temporal);
            }

            // 集合遍历顺序不稳定，差异逐条按 Region Key + 分类排序，使同输入产生同输出。
            differences.Sort(CompareDifferences);
            int expected = 0;
            int forbidden = 0;
            foreach (CollisionShadowDifference difference in differences)
            {
                if (difference.IsForbidden) forbidden++;
                else expected++;
            }
            return new CollisionShadowReport(differences, inBoth, expected, forbidden);
        }

        /// <summary>新侧独有：Host 贡献 → 预期；在场合格 Guest 贡献 → 预期（canonical 生命周期更稳）；
        /// 只有暂缓贡献覆盖 → 预期；没有任何认领者 → 禁止。</summary>
        private static CollisionShadowDifference ClassifyNewOnly(
            DemandPolicy policy, CollisionShadowFrame frame, RegionKey region)
        {
            bool hostCovers = false;
            bool eligibleCovers = false;
            bool deferredCovers = false;
            bool anyCovers = false;
            var ids = new List<ulong>();
            var tokens = new List<ulong>();
            foreach (CollisionShadowClaim claim in frame.CurrentClaims)
            {
                if (!Covers(claim.Presence.Center, region, policy.Radius)) continue;
                anyCovers = true;
                if (claim.IsHost) hostCovers = true;
                if (claim.IsDeferred) deferredCovers = true;
                if (policy.IsEligible(claim.Presence) && !claim.IsDeferred)
                {
                    eligibleCovers = true;
                    AddClaimant(ids, tokens, claim.Presence.ObserverId, claim.Presence.ConnectionToken);
                }
                else if (!hostCovers)
                {
                    AddClaimant(ids, tokens, claim.Presence.ObserverId, claim.Presence.ConnectionToken);
                }
            }

            if (hostCovers)
                return Difference(policy, region, ECollisionShadowDifferenceKind.HostAddedCoverage, ids, tokens,
                    "host-local-player");
            if (eligibleCovers)
                return Difference(policy, region, ECollisionShadowDifferenceKind.CanonicalLifecycleStability,
                    ids, tokens, "canonical-fact-claimant");
            if (deferredCovers)
                return Difference(policy, region, ECollisionShadowDifferenceKind.DeferredContributionRetained,
                    ids, tokens, "deferred-contribution");
            if (anyCovers)
                return Difference(policy, region, ECollisionShadowDifferenceKind.DeferredContributionRetained,
                    ids, tokens, "deferred-contribution");
            return Difference(policy, region, ECollisionShadowDifferenceKind.UnattributedDemand,
                ids, tokens, "no-claimant");
        }

        /// <summary>旧侧独有：被在场且合格的认领者（Guest 或 Host）仍需要 → 禁止；
        /// 只被暂缓/未合格/已离开的认领者覆盖 → 预期（旧 Writer 与其资源侧写入一起退出，
        /// 原因词区分 pending-guest / deferred-sample / departed-observer）；
        /// 无法归因于旧 Writer 自己的任何认领者 → 禁止（两份旧状态自相矛盾，差异不可解释）。</summary>
        private static CollisionShadowDifference ClassifyLegacyOnly(
            DemandPolicy policy, CollisionShadowFrame frame, RegionKey region)
        {
            var ids = new List<ulong>();
            var tokens = new List<ulong>();
            bool stillNeeded = false;
            foreach (CollisionShadowClaim claim in frame.CurrentClaims)
            {
                if (!policy.IsEligible(claim.Presence)) continue;
                if (!Covers(claim.Presence.Center, region, policy.Radius)) continue;
                stillNeeded = true;
                AddClaimant(ids, tokens, claim.Presence.ObserverId, claim.Presence.ConnectionToken);
            }

            var legacyIds = new List<ulong>();
            var legacyTokens = new List<ulong>();
            string expectedReason = null;
            foreach (CollisionShadowLegacyClaim claim in frame.LegacyClaims)
            {
                if (!Covers(claim.Center, region, policy.Radius)) continue;
                AddClaimant(legacyIds, legacyTokens, claim.ObserverId, 0UL);
                if (claim.State == ECollisionShadowLegacyState.Eligible)
                {
                    stillNeeded = true;
                    AddClaimant(ids, tokens, claim.ObserverId, 0UL);
                }
                else if (expectedReason == null || claim.State == ECollisionShadowLegacyState.Pending)
                {
                    // 原因优先级：Pending（资格问题最需要被看见）> Deferred > Absent。
                    expectedReason = ReasonOf(claim.State);
                }
            }

            if (stillNeeded)
                return Difference(policy, region, ECollisionShadowDifferenceKind.AuthorizedGuestMissingRegion,
                    ids, tokens, ReasonOf(ECollisionShadowLegacyState.Eligible));
            if (expectedReason != null)
                return Difference(policy, region, ECollisionShadowDifferenceKind.ResourceCoupledCoverageExit,
                    legacyIds, legacyTokens, expectedReason);
            return Difference(policy, region, ECollisionShadowDifferenceKind.UnexplainedLegacyCoverage,
                legacyIds, legacyTokens, "no-legacy-claimant");
        }

        private static string ReasonOf(ECollisionShadowLegacyState state)
        {
            switch (state)
            {
                case ECollisionShadowLegacyState.Eligible: return "live-authorized-claimant";
                case ECollisionShadowLegacyState.Pending: return "pending-guest";
                case ECollisionShadowLegacyState.Deferred: return "deferred-sample";
                default: return "departed-observer";
            }
        }

        /// <summary>
        /// 时间轴对照：观察者事实没变而区域进出＝抖动；区域退出但事实未变且有仍覆盖它的
        /// 幸存认领者＝错误释放。事实确实变了（移动、离开、资格变化）的进出都是合法差异。
        /// </summary>
        private static CollisionShadowDifference ClassifyTemporal(
            DemandPolicy policy, CollisionShadowFrame frame, RegionKey region,
            HashSet<RegionKey> newRegions, HashSet<RegionKey> previousRegions)
        {
            bool entered = newRegions.Contains(region);
            if (!entered && !previousRegions.Contains(region)) return null;

            var currentTokens = new List<ulong>();
            List<ClaimIdentity> current = EligibleIdentities(policy, frame.CurrentClaims, region, currentTokens);
            var previousTokens = new List<ulong>();
            List<ClaimIdentity> previous =
                EligibleIdentities(policy, frame.PreviousClaims, region, previousTokens);

            if (current.Count == 0 && previous.Count == 0) return null;
            if (SameIdentities(current, previous))
            {
                var churnIds = new List<ulong>();
                var churnTokens = new List<ulong>();
                for (int index = 0; index < current.Count; index++)
                    AddClaimant(churnIds, churnTokens, current[index].ObserverId, currentTokens[index]);
                return Difference(policy, region, ECollisionShadowDifferenceKind.StaticDemandChurn,
                    churnIds, churnTokens, "facts-unchanged");
            }

            if (entered) return null;
            var ids = new List<ulong>();
            var tokens = new List<ulong>();
            for (int index = 0; index < current.Count; index++)
            {
                if (!Contains(previous, current[index])) continue;
                AddClaimant(ids, tokens, current[index].ObserverId, currentTokens[index]);
            }
            if (ids.Count == 0) return null;
            return Difference(policy, region, ECollisionShadowDifferenceKind.CrossObserverRelease, ids, tokens,
                "surviving-claimant-still-covers");
        }

        private static bool SameIdentities(List<ClaimIdentity> left, List<ClaimIdentity> right)
        {
            if (left.Count != right.Count) return false;
            foreach (ClaimIdentity identity in left)
            {
                if (!Contains(right, identity)) return false;
            }
            return true;
        }

        private static bool Contains(List<ClaimIdentity> identities, ClaimIdentity expected)
        {
            foreach (ClaimIdentity identity in identities)
            {
                if (identity.Equals(expected)) return true;
            }
            return false;
        }

        private static List<ClaimIdentity> EligibleIdentities(
            DemandPolicy policy, CollisionShadowClaim[] claims, RegionKey region, List<ulong> tokens)
        {
            var identities = new List<ClaimIdentity>();
            foreach (CollisionShadowClaim claim in claims)
            {
                if (!policy.IsEligible(claim.Presence)) continue;
                if (!Covers(claim.Presence.Center, region, policy.Radius)) continue;
                var identity = new ClaimIdentity(claim.Presence.ObserverId, claim.Presence.Center);
                if (Contains(identities, identity)) continue;
                identities.Add(identity);
                tokens.Add(claim.Presence.ConnectionToken);
            }
            return identities;
        }

        private static void AddOutOfBounds(
            DemandPolicy policy, CollisionShadowFrame frame,
            HashSet<RegionKey> newRegions, HashSet<RegionKey> legacyRegions, HashSet<RegionKey> previousRegions,
            List<CollisionShadowDifference> differences)
        {
            var seen = new HashSet<RegionKey>();
            foreach (RegionKey region in Union(newRegions, Union(legacyRegions, previousRegions)))
            {
                if (!IsOutOfBounds(region, frame.WorldSize)) continue;
                if (!seen.Add(region)) continue;
                differences.Add(Difference(policy, region,
                    ECollisionShadowDifferenceKind.OutOfBoundsRegionKey,
                    new List<ulong>(), new List<ulong>(), "region-key-outside-world"));
            }
        }

        private static HashSet<RegionKey> Union(HashSet<RegionKey> left, HashSet<RegionKey> right)
        {
            var union = new HashSet<RegionKey>(left);
            union.UnionWith(right);
            return union;
        }

        private static HashSet<RegionKey> SymmetricDifference(
            HashSet<RegionKey> left, HashSet<RegionKey> right)
        {
            var difference = new HashSet<RegionKey>(left);
            difference.SymmetricExceptWith(right);
            return difference;
        }

        private static bool IsOutOfBounds(RegionKey region, byte worldSize)
        {
            return region.X >= worldSize || region.Y >= worldSize;
        }

        private static bool Covers(RegionKey center, RegionKey region, byte radius)
        {
            return Math.Abs(region.X - center.X) <= radius && Math.Abs(region.Y - center.Y) <= radius;
        }

        private static void AddClaimant(List<ulong> ids, List<ulong> tokens, ulong observerId, ulong token)
        {
            for (int index = 0; index < ids.Count; index++)
            {
                if (ids[index] == observerId) return;
            }
            ids.Add(observerId);
            tokens.Add(token);
        }

        private static CollisionShadowDifference Difference(
            DemandPolicy policy, RegionKey region, ECollisionShadowDifferenceKind kind,
            List<ulong> ids, List<ulong> tokens, string reason = "none")
        {
            return Difference(policy, region, kind, ids.ToArray(), tokens.ToArray(), reason);
        }

        private static CollisionShadowDifference Difference(
            DemandPolicy policy, RegionKey region, ECollisionShadowDifferenceKind kind,
            ulong[] ids, ulong[] tokens, string reason = "none")
        {
            return new CollisionShadowDifference(
                policy.Domain, region, kind, DispositionOf(kind), ids, tokens, reason);
        }

        private static ECollisionShadowDisposition DispositionOf(ECollisionShadowDifferenceKind kind)
        {
            switch (kind)
            {
                case ECollisionShadowDifferenceKind.HostAddedCoverage:
                case ECollisionShadowDifferenceKind.ResourceCoupledCoverageExit:
                case ECollisionShadowDifferenceKind.CanonicalLifecycleStability:
                case ECollisionShadowDifferenceKind.DeferredContributionRetained:
                    return ECollisionShadowDisposition.Expected;
                default:
                    // 分类不出来的差异失败闭合为禁止差异，不默认放行。
                    return ECollisionShadowDisposition.Forbidden;
            }
        }

        private static int CompareDifferences(CollisionShadowDifference left, CollisionShadowDifference right)
        {
            int byRegion = left.RegionKey.Packed.CompareTo(right.RegionKey.Packed);
            return byRegion != 0 ? byRegion : ((int)left.Kind).CompareTo((int)right.Kind);
        }

        /// <summary>「谁在覆盖这个区域」的身份：观察者 + 中心。移动会改变它，因此移动不算抖动。</summary>
        private readonly struct ClaimIdentity : IEquatable<ClaimIdentity>
        {
            internal ClaimIdentity(ulong observerId, RegionKey center)
            {
                ObserverId = observerId;
                Center = center;
            }

            internal ulong ObserverId { get; }
            internal RegionKey Center { get; }

            public bool Equals(ClaimIdentity other) =>
                ObserverId == other.ObserverId && Center == other.Center;

            public override bool Equals(object obj) => obj is ClaimIdentity other && Equals(other);

            public override int GetHashCode()
            {
                unchecked { return (ObserverId.GetHashCode() * 397) ^ Center.Packed; }
            }
        }
    }
}
