namespace SteamP2PFriends.Adapters.Item
{
    /// <summary>
    /// 物品生成来源机器分类：
    /// Natural: 地图首次预生成或周期刷新产生；
    /// Dropped: 玩家丢弃，或已核实的僵尸/动物/资源掉落；
    /// Unknown: 未知来源，不授予自然休眠资格。
    /// </summary>
    public enum ItemOriginKind : byte
    {
        Unknown = 0,
        Natural = 1,
        Dropped = 2
    }
}
