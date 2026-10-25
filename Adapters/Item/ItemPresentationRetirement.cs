using SDG.Unturned;

namespace SteamP2PFriends.Adapters.Item
{
    /// <summary>
    /// 会话边界的 Item 本地呈现退役判定：唯一权威 Writer 已接管，或听主机以服务端身份运行。
    /// ItemOwnedPresentationRuntimePatch 与 ItemManagerRegionSyncPatch 共用此单一来源。
    /// </summary>
    internal static class ItemPresentationRetirement
    {
        internal static bool IsRetiredListenHost()
        {
            try { if (Dedicator.IsDedicatedServer) return false; }
            catch { return false; }
            try { if (!Provider.isServer) return false; }
            catch { return false; }
            if (MultiObserver.MultiObserverShadowCoordinator.ItemAuthorityWriterOwnsSession) return true;
            try { return Host.HostManager.IsP2PHostMode; }
            catch { return false; }
        }
    }
}
