using SDG.Unturned;
using Steamworks;

namespace SteamP2PFriends.Shared
{
    /// <summary>
    /// Join Routing 票 04：会话密码策略缝。
    /// 房主侧：开房菜单密码只写入当前会话的 Provider.serverPassword（空串 = 无密码房间），
    /// 返回菜单、结束会话或销毁房间时经 ClearRuntime 清空；不持久化、不进审计与普通诊断。
    /// 客机侧：SteamID 路线把原版直连页密码框文本传入 ServerConnectParameters。
    /// 隐私铁律：日志与断言只允许经 HasPassword 输出布尔，禁止明文、长度与参数转储。
    /// </summary>
    internal static class SessionPassword
    {
        /// <summary>开房菜单密码输入 -> 当前会话 Provider.serverPassword。不 trim；空串 = 无密码房间。</summary>
        internal static string ResolveSessionServerPassword(string rawMenuInput)
        {
            return rawMenuInput ?? string.Empty;
        }

        /// <summary>原版直连页密码框 -> 客机 SteamID 路线连接参数。不 trim；空串 = 无密码。</summary>
        internal static ServerConnectParameters BuildSteamP2PConnectParameters(ulong hostSteamId, string rawPasswordFieldText)
        {
            return new ServerConnectParameters(new CSteamID(hostSteamId), rawPasswordFieldText ?? string.Empty);
        }

        /// <summary>唯一允许的密码可观测性谓词：只输出布尔，不输出明文。</summary>
        internal static bool HasPassword(string sessionPassword)
        {
            return !string.IsNullOrEmpty(sessionPassword);
        }

        /// <summary>连接参数 hasPassword 谓词（测试与诊断同样只允许布尔出口）。</summary>
        internal static bool HasPassword(ServerConnectParameters parameters)
        {
            return parameters != null && !string.IsNullOrEmpty(parameters.password);
        }

        /// <summary>会话结束（返回菜单、结束会话、销毁房间）清空运行时密码。</summary>
        internal static void ClearRuntime()
        {
            Provider.serverPassword = string.Empty;
        }
    }
}
