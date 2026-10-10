using SteamP2PFriends.Core.Identity;
using SteamP2PFriends.MultiObserver.Demand;

namespace SteamP2PFriends.Adapters.Item
{
    /// <summary>
    /// Item 域的 Demand Policy 声明：Domain Id = Item，空间形状 = 二维切比雪夫方形，
    /// 半径来源 = 原版物品区域常量，资格 = 具备玩法资格的 World Presence Observer（Host 亦在其列）。
    /// </summary>
    public static class ItemDemandPolicy
    {
        public const string RadiusSource = "ItemManager.ITEM_REGIONS";

        public static DemandPolicy Create(byte radius, byte worldSize)
        {
            return new DemandPolicy(
                DomainIds.Item,
                radius,
                worldSize,
                EDemandRegionShape.ChebyshevSquare2D,
                RadiusSource,
                presence => presence.GameplayAuthorized);
        }
    }
}
