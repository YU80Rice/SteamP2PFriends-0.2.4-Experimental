using SteamP2PFriends.Core.Ownership;
using System;
using System.Linq;
using System.Reflection;

namespace SteamP2PFriends.WhitelistTests
{
    /// <summary>
    /// Ticket 04 的静态结构门禁：验证唯一归属目录、受控跨领域登记、注册权威入口
    /// 和 Registration Trace 覆盖。它不把真实游戏运行误报为 Runtime 证据。
    /// </summary>
    internal static class ModuleOwnershipStaticILContractTests
    {
        internal static bool Test_All()
        {
            return ModuleOwnershipCatalog.HasUniqueModuleIds()
                && ModuleOwnershipCatalog.HasSingleRegistrationAuthority()
                && ModuleOwnershipCatalog.HasRegistrationTraceCoverage()
                && Test_RegistrationTypesHaveSingleCompiledEntry()
                && Test_SecurityTypesHaveSingleCompiledEntry();
        }

        private static bool Test_RegistrationTypesHaveSingleCompiledEntry()
        {
            Assembly assembly = typeof(SteamP2PFriendsPlugin).Assembly;
            return assembly.GetTypes().Count(type => type.FullName ==
                       "SteamP2PFriends.Core.Registration.PatchRegistrationOrchestrator") == 1
                && assembly.GetTypes().Count(type => type.FullName ==
                       "SteamP2PFriends.Core.Registration.RegistrationClosure") == 1;
        }

        private static bool Test_SecurityTypesHaveSingleCompiledEntry()
        {
            Assembly assembly = typeof(SteamP2PFriendsPlugin).Assembly;
            return assembly.GetTypes().Count(type => type.FullName ==
                       "SteamP2PFriends.Security.P2PApprovalManager") == 1
                && assembly.GetTypes().Count(type => type.FullName ==
                       "SteamP2PFriends.Security.P2PWhitelistService") == 1
                && assembly.GetTypes().Any(type => type.FullName ==
                       "SteamP2PFriends.Security.Patches.P2PQuarantineActionGatePatch");
        }
    }
}
