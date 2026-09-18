using System;
using SteamP2PFriends.Core.Identity;
using SteamP2PFriends.MultiObserver.Demand;

namespace SteamP2PFriends.Adapters.Collision
{
    /// <summary>
    /// 影子对照新侧的认领者：一条 canonical World Presence Observer 事实（含 Host——本地玩家）
    /// 加它是否本地玩家。资格不在这里判定，由 Collision 的 Demand Policy 对这条事实判定——
    /// 影子对照不复制第二套资格规则。
    /// </summary>
    public readonly struct CollisionShadowClaim
    {
        public CollisionShadowClaim(ObserverPresence presence, bool isHost, bool isDeferred = false)
        {
            Presence = presence;
            IsHost = isHost;
            IsDeferred = isDeferred;
        }

        public ObserverPresence Presence { get; }

        /// <summary>本地玩家＝听主机 Host；旧 Writer 按设计不覆盖 Host，因此它只可能出现在新侧。</summary>
        public bool IsHost { get; }

        /// <summary>
        /// 这条认领来自暂缓观察者的**最后已知事实**（本拍样本不可用）：它的区域是保留的
        /// Deferred Observer Demand，不是新算出来的需求，也不是「本拍在场」的样本。
        /// </summary>
        public bool IsDeferred { get; }

        public override string ToString() =>
            $"observer={Presence.ObserverId} center={Presence.Center} host={IsHost} deferred={IsDeferred}";
    }

    /// <summary>
    /// 旧侧认领者当前处于哪一态。四态必须分开，不能压成一个 bool：
    /// 「在场且合格」「在场但不合格（Pending Guest）」「本拍样本不可用（暂缓）」「已不在事实里（离开）」
    /// 对应的差异处置与原因词都不同——把暂缓者当成离开，就会把准入故障误报成预期差异。
    /// </summary>
    public enum ECollisionShadowLegacyState : byte
    {
        /// <summary>在场且按 Collision Demand Policy 合格。</summary>
        Eligible = 1,

        /// <summary>在场但未获玩法资格（Pending Guest）。</summary>
        Pending = 2,

        /// <summary>本拍样本不可用（Deferred Observer Demand）：既有贡献保留，不计为离开。</summary>
        Deferred = 3,

        /// <summary>已不在 canonical 事实里：确认离开，或旧 Writer 自己那份跟踪的陈旧条目。</summary>
        Absent = 4
    }

    /// <summary>
    /// 影子对照旧侧的认领者：旧 RemoteCoverage Writer 自己跟踪的远端玩家中心，外加它当前所处的态。
    /// 旧侧没有连接代次（它按对象引用判定重连），因此只带身份与中心。
    /// </summary>
    public readonly struct CollisionShadowLegacyClaim
    {
        public CollisionShadowLegacyClaim(ulong observerId, RegionKey center, ECollisionShadowLegacyState state)
        {
            if (observerId == 0UL)
                throw new ArgumentOutOfRangeException(nameof(observerId), "旧侧认领者身份不得为零");
            if (state != ECollisionShadowLegacyState.Eligible
                && state != ECollisionShadowLegacyState.Pending
                && state != ECollisionShadowLegacyState.Deferred
                && state != ECollisionShadowLegacyState.Absent)
            {
                throw new ArgumentOutOfRangeException(nameof(state), "旧侧认领者状态必须是四态之一");
            }

            ObserverId = observerId;
            Center = center;
            State = state;
        }

        public ulong ObserverId { get; }
        public RegionKey Center { get; }

        public ECollisionShadowLegacyState State { get; }

        public override string ToString() =>
            $"legacyObserver={ObserverId} center={Center} state={State}";
    }

    /// <summary>
    /// 影子对照的一拍输入：新侧（共享投影）当前与上一拍的区域集合、旧侧（旧 Writer）当前覆盖，
    /// 以及两侧的认领者。它只装事实，不做分类；分类由 CollisionShadowComparator 完成。
    ///
    /// <see cref="Domain"/> 是新侧区域集合来源的 Domain Id——它必须等于 Collision 政策的
    /// Domain Id，否则视为「Resource 需求被当成 Collision 需求」的接线错误，失败闭合。
    /// </summary>
    public sealed class CollisionShadowFrame
    {
        public CollisionShadowFrame(
            DomainId domain,
            byte worldSize,
            RegionKey[] newRegions,
            RegionKey[] previousNewRegions,
            RegionKey[] legacyRegions,
            CollisionShadowClaim[] currentClaims,
            CollisionShadowClaim[] previousClaims,
            CollisionShadowLegacyClaim[] legacyClaims)
        {
            if (worldSize == 0)
                throw new ArgumentOutOfRangeException(nameof(worldSize), "世界尺寸不得为零");

            Domain = domain;
            WorldSize = worldSize;
            NewRegions = newRegions ?? Array.Empty<RegionKey>();
            PreviousNewRegions = previousNewRegions ?? Array.Empty<RegionKey>();
            LegacyRegions = legacyRegions ?? Array.Empty<RegionKey>();
            CurrentClaims = currentClaims ?? Array.Empty<CollisionShadowClaim>();
            PreviousClaims = previousClaims ?? Array.Empty<CollisionShadowClaim>();
            LegacyClaims = legacyClaims ?? Array.Empty<CollisionShadowLegacyClaim>();
        }

        public DomainId Domain { get; }
        public byte WorldSize { get; }

        /// <summary>新侧当前有需求的区域（共享投影引擎算出，含 Host 贡献）。</summary>
        public RegionKey[] NewRegions { get; }

        /// <summary>新侧上一拍的区域集合（抖动判定用；首拍为空）。</summary>
        public RegionKey[] PreviousNewRegions { get; }

        /// <summary>旧侧当前覆盖的区域（旧 RemoteCoverage Writer 的真实状态，只读抄出）。</summary>
        public RegionKey[] LegacyRegions { get; }

        public CollisionShadowClaim[] CurrentClaims { get; }
        public CollisionShadowClaim[] PreviousClaims { get; }

        /// <summary>旧侧自己的远端玩家中心（Host 不在其中——旧 Writer 按设计只看远端玩家）。</summary>
        public CollisionShadowLegacyClaim[] LegacyClaims { get; }
    }
}
