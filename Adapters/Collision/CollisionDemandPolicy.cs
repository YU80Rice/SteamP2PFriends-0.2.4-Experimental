using SteamP2PFriends.Core.Identity;
using SteamP2PFriends.MultiObserver.Demand;

namespace SteamP2PFriends.Adapters.Collision
{
    /// <summary>
    /// Collision 域的 Demand Policy 声明：Domain Id = Collision，空间形状 = 二维切比雪夫方形，
    /// 半径来源 = 原版物件区域常量，资格 = 具备玩法资格的 World Presence Observer。
    ///
    /// Host 必须贡献 Collision 需求（它是听主机上的本地玩家，在控制面样本里即具备玩法资格），
    /// 因此本政策不得复制旧 RemoteCoverage 的 remote-only 语义；Guest 资格沿用控制面统一定义，
    /// 不在这里自行判断授权状态。这里只做声明：不扫描客户端名册、不持有观察者位置、不计算区域集合。
    /// 半径值与世界尺寸由调用方从各自的原版常量读取后传入——物件半径不得升格为跨域的共享默认半径。
    /// </summary>
    public static class CollisionDemandPolicy
    {
        /// <summary>半径声明来源（原版物件区域常量名），供诊断与取证引用。</summary>
        public const string RadiusSource = "LevelObjects.OBJECT_REGIONS";

        public static DemandPolicy Create(byte radius, byte worldSize)
        {
            return new DemandPolicy(
                DomainIds.Collision,
                radius,
                worldSize,
                EDemandRegionShape.ChebyshevSquare2D,
                RadiusSource,
                presence => presence.GameplayAuthorized);
        }
    }
}
