using SDG.Unturned;
using SteamP2PFriends.Shared;

namespace SteamP2PFriends.WhitelistTests
{
    /// <summary>
    /// Ticket 04（join-routing）PureMemory：会话密码参数组装、房主输入语义与运行时清空。
    /// 隐私铁律：断言只经 SessionPassword.HasPassword 输出布尔，禁止读明文或长度。
    /// </summary>
    internal static class SessionPasswordTests
    {
        private const ulong HostSteamId = 76561199000000002UL;

        internal static bool Test_JR4_SteamIdRouteCarriesDirectPagePassword()
        {
            ServerConnectParameters withoutPassword =
                SessionPassword.BuildSteamP2PConnectParameters(HostSteamId, null);
            if (SessionPassword.HasPassword(withoutPassword)) return false;
            if (withoutPassword.steamId.m_SteamID != HostSteamId) return false;

            ServerConnectParameters withPassword =
                SessionPassword.BuildSteamP2PConnectParameters(HostSteamId, "  padded  ");
            if (!SessionPassword.HasPassword(withPassword)) return false;
            return true;
        }

        internal static bool Test_JR5_HostMenuInputResolvesSessionPassword()
        {
            if (SessionPassword.HasPassword(SessionPassword.ResolveSessionServerPassword(null))) return false;
            if (!SessionPassword.HasPassword(SessionPassword.ResolveSessionServerPassword("room-password"))) return false;
            // 不 trim：纯空白输入按有密码房间处理（只有空串 = 无密码房间）。
            if (!SessionPassword.HasPassword(SessionPassword.ResolveSessionServerPassword("   "))) return false;
            return true;
        }

        internal static bool Test_JR6_RuntimePasswordClearedOnSessionEnd()
        {
            try
            {
                Provider.serverPassword = "stale-session-password";
                if (!SessionPassword.HasPassword(Provider.serverPassword)) return false;

                SessionPassword.ClearRuntime();
                if (SessionPassword.HasPassword(Provider.serverPassword)) return false;

                // 幂等：重复清空无副作用。
                SessionPassword.ClearRuntime();
                if (SessionPassword.HasPassword(Provider.serverPassword)) return false;
                return true;
            }
            finally
            {
                Provider.serverPassword = string.Empty;
            }
        }
    }
}
