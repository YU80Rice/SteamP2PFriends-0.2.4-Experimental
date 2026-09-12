using SteamP2PFriends.Shared;

namespace SteamP2PFriends.WhitelistTests
{
    /// <summary>
    /// Ticket 03（join-routing）PureMemory：Classify 在判断个人 SteamID 之前
    /// 只剥合法的单个 :port。端口不进入 out steamId。
    /// </summary>
    internal static class UnifiedJoinAddressClassifierTests
    {
        private const ulong IndividualSteamId = 76561199000000001UL;
        private const string IndividualSteamIdText = "76561199000000001";

        internal static bool Test_JR1_SteamIdWithOptionalPortClassifiesAsSteamP2P()
        {
            if (!IsSteamP2P(IndividualSteamIdText, IndividualSteamId)) return false;
            if (!IsSteamP2P("  " + IndividualSteamIdText + ":27016  ", IndividualSteamId))
                return false;
            if (!IsSteamP2P(IndividualSteamIdText + ":1", IndividualSteamId)) return false;
            if (!IsSteamP2P(IndividualSteamIdText + ":65535", IndividualSteamId)) return false;
            return true;
        }

        internal static bool Test_JR2_ForbiddenSuffixesStayVanilla()
        {
            if (!IsVanilla("192.168.1.1:27016")) return false;
            if (!IsVanilla("host.example.com:27016")) return false;
            if (!IsVanilla(IndividualSteamIdText + ":0")) return false;
            if (!IsVanilla(IndividualSteamIdText + ":65536")) return false;
            if (!IsVanilla(IndividualSteamIdText + ":abc")) return false;
            if (!IsVanilla("2001:db8::1")) return false;
            if (!IsVanilla(IndividualSteamIdText + ":27016:1")) return false;
            if (!IsVanilla(":0")) return false;
            if (!IsVanilla(":65536")) return false;
            if (!IsVanilla(IndividualSteamIdText + " :27016")) return false;
            if (!IsVanilla(IndividualSteamIdText + ":4294967297")) return false;
            return true;
        }

        internal static bool Test_JR3_DirectIpAndDnsKeepOwnPortStripping()
        {
            bool ipOk = UnifiedJoinAddressClassifier.TryBuildDirectIpEndpoint(
                "192.168.1.1:27016", 27015, out _, out ushort ipQuery, out ushort ipConn);
            bool dnsOk = UnifiedJoinAddressClassifier.TryBuildExplicitDnsEndpoint(
                "host.example.com:27016", 27015, out string dnsHost, out ushort dnsPort);
            bool steamNotIp = !UnifiedJoinAddressClassifier.TryBuildDirectIpEndpoint(
                IndividualSteamIdText + ":27016", 27015, out _, out _, out _);
            bool steamNotDns = !UnifiedJoinAddressClassifier.TryBuildExplicitDnsEndpoint(
                IndividualSteamIdText + ":27016", 27015, out _, out _);
            return ipOk && ipQuery == 27016 && ipConn == 27016
                && dnsOk && dnsHost == "host.example.com" && dnsPort == 27016
                && steamNotIp && steamNotDns;
        }

        private static bool IsSteamP2P(string raw, ulong expectedId)
        {
            UnifiedJoinAddressKind kind = UnifiedJoinAddressClassifier.Classify(raw, out ulong steamId);
            return kind == UnifiedJoinAddressKind.SteamP2P && steamId == expectedId;
        }

        private static bool IsVanilla(string raw)
        {
            UnifiedJoinAddressKind kind = UnifiedJoinAddressClassifier.Classify(raw, out ulong steamId);
            return kind == UnifiedJoinAddressKind.Vanilla && steamId == 0UL;
        }
    }
}
