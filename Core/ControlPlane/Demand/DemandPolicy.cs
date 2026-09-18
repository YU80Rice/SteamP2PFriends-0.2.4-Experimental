using System;
using SteamP2PFriends.Core.Identity;

namespace SteamP2PFriends.MultiObserver.Demand
{
    /// <summary>
    /// 领域声明的空间形状。当前只实现二维切比雪夫方形——它按世界边界裁剪后仍是方形，
    /// 与既有的原版区域半径语义一致。未声明的形状必须失败闭合，不得退回「默认形状」。
    /// </summary>
    public enum EDemandRegionShape
    {
        /// <summary>切比雪夫方形：|dx| &lt;= radius 且 |dy| &lt;= radius，再按世界边界裁剪。</summary>
        ChebyshevSquare2D = 1
    }

    /// <summary>
    /// 领域声明的 Demand Policy：资格、空间形状、半径与半径来源、世界尺寸、Domain Id。
    ///
    /// 它是声明而不是投影器——不扫描客户端、不存储观察者位置、不含区域集合；
    /// 区域枚举、边界裁剪、去重、计数与进入/退出差异全部由 Demand Projection Engine 执行。
    /// 不存在跨域共享的默认半径或需求掩码：每条政策各自携带自己的半径与来源。
    /// </summary>
    public sealed class DemandPolicy
    {
        public DemandPolicy(
            DomainId domainId,
            byte radius,
            byte worldSize,
            EDemandRegionShape shape,
            string radiusSource,
            Func<ObserverPresence, bool> eligibility)
        {
            if (!domainId.IsDefined)
                throw new ArgumentException("Demand Policy 必须携带已定义的 Domain Id", nameof(domainId));
            if (worldSize == 0)
                throw new ArgumentOutOfRangeException(nameof(worldSize), "世界尺寸不得为零");
            if (shape != EDemandRegionShape.ChebyshevSquare2D)
                throw new ArgumentOutOfRangeException(nameof(shape), "未实现的领域空间形状必须失败闭合");
            if (string.IsNullOrWhiteSpace(radiusSource))
                throw new ArgumentException("Demand Policy 必须声明半径来源", nameof(radiusSource));

            Domain = domainId;
            Radius = radius;
            WorldSize = worldSize;
            Shape = shape;
            RadiusSource = radiusSource;
            Eligibility = eligibility ?? throw new ArgumentNullException(nameof(eligibility));
        }

        public DomainId Domain { get; }

        /// <summary>领域自己的区域半径。0 表示单区域（只投影观察者所在区域）。</summary>
        public byte Radius { get; }

        public byte WorldSize { get; }

        public EDemandRegionShape Shape { get; }

        /// <summary>半径声明的来源标识（例如原版物件/资源区域常量的名称），供诊断与取证。</summary>
        public string RadiusSource { get; }

        private Func<ObserverPresence, bool> Eligibility { get; }

        /// <summary>该观察者是否为本领域贡献需求。资格由领域声明，引擎只执行。</summary>
        public bool IsEligible(ObserverPresence presence) => Eligibility(presence);

        public override string ToString() =>
            $"domain={Domain} shape={Shape} radius={Radius} radiusSource={RadiusSource}";
    }
}
