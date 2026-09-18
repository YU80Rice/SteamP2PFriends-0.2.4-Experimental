using System;
using SteamP2PFriends.Core.Identity;

namespace SteamP2PFriends.MultiObserver.Demand
{
    /// <summary>
    /// 一次领域投影的结果：该领域的 Domain Id、观察者与连接代次，以及本次观测引起的
    /// 区域进入/退出差异。差异按 Region Key 表达——跨层身份不使用裸整数编码。
    /// 无变化时两个数组均为空且 <see cref="HasChanges"/> 为 false。
    /// </summary>
    public readonly struct DomainDemandProjection
    {
        public DomainDemandProjection(
            DomainId domain,
            ulong observerId,
            ulong connectionToken,
            RegionKey[] enteredRegions,
            RegionKey[] exitedRegions)
        {
            if (!domain.IsDefined)
                throw new ArgumentException("投影结果必须携带已定义的 Domain Id", nameof(domain));

            Domain = domain;
            ObserverId = observerId;
            ConnectionToken = connectionToken;
            EnteredRegions = enteredRegions ?? Array.Empty<RegionKey>();
            ExitedRegions = exitedRegions ?? Array.Empty<RegionKey>();
        }

        public DomainId Domain { get; }
        public ulong ObserverId { get; }
        public ulong ConnectionToken { get; }
        public RegionKey[] EnteredRegions { get; }
        public RegionKey[] ExitedRegions { get; }

        public bool HasChanges => EnteredRegions.Length > 0 || ExitedRegions.Length > 0;

        public override string ToString() =>
            $"{Domain} observer={ObserverId} entered={EnteredRegions.Length} exited={ExitedRegions.Length}";
    }
}
