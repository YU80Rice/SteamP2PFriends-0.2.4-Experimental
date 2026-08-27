using SteamP2PFriends.Core.Ownership;
using System;
using System.Linq;
using System.Reflection;

namespace SteamP2PFriends.WhitelistTests
{
    /// <summary>
    /// Ticket 06 的结构接缝：Item/Zombie 的适配器、补丁和生命周期辅助类型
    /// 必须各自落在领域命名空间；旧 Core.Patches 入口不得留下编译产物。
    /// 该契约只验证编译产物结构，不把 Runtime 行为误报为通过。
    /// </summary>
    internal static class ItemZombieOwnershipStaticILContractTests
    {
        internal static bool Test_All()
        {
            Assembly assembly = typeof(SteamP2PFriendsPlugin).Assembly;
            return Test_ItemTypesHaveItemOwnership(assembly)
                && Test_ZombieTypesHaveZombieOwnership(assembly)
                && Test_LegacyItemZombieAuthoritiesAreAbsent(assembly)
                && ModuleOwnershipCatalog.HasItemZombieOwnership();
        }

        private static bool Test_ItemTypesHaveItemOwnership(Assembly assembly)
        {
            string[] expected =
            {
                "SteamP2PFriends.Adapters.Item.ItemDomainAdapter",
                "SteamP2PFriends.Adapters.Item.ItemGenerationAuthorityAdapter",
                "SteamP2PFriends.Adapters.Item.ItemObserverReplicationAdapter",
                "SteamP2PFriends.Adapters.Item.Patches.ItemManagerRegionSyncPatch",
                "SteamP2PFriends.Adapters.Item.Patches.ItemManagerWorldSyncDiagnosticPatch",
                "SteamP2PFriends.Adapters.Item.Patches.AuthoritativeItemGenerationGatePatch",
                "SteamP2PFriends.Adapters.Item.Patches.ItemManagerP0B3PreGeneratePatch",
                "SteamP2PFriends.Adapters.Item.Patches.ItemManagerP0B6RegenerateOnLevelLoadedPatch"
            };

            return expected.All(fullName => assembly.GetTypes().Count(type => type.FullName == fullName) == 1);
        }

        private static bool Test_ZombieTypesHaveZombieOwnership(Assembly assembly)
        {
            string[] expected =
            {
                "SteamP2PFriends.Adapters.Zombie.ZombieDomainAdapter",
                "SteamP2PFriends.Adapters.Zombie.ZombieRegionLifecycleAdapter",
                "SteamP2PFriends.Adapters.Zombie.ZombieSnapshotAdapter",
                "SteamP2PFriends.Adapters.Zombie.Patches.ZombieManagerWorldSyncDiagnosticPatch",
                "SteamP2PFriends.Adapters.Zombie.Patches.ZombieManagerP0C1SendZombieStatesPatch",
                "SteamP2PFriends.Adapters.Zombie.Patches.ZombieManagerP0DGenerateZombiesPatch",
                "SteamP2PFriends.Adapters.Zombie.Patches.ZombieLifecyclePatch",
                "SteamP2PFriends.Adapters.Zombie.Patches.ZombieLifecycleState",
                "SteamP2PFriends.Adapters.Zombie.Patches.ZombieLifecycleOwnerVerify",
                "SteamP2PFriends.Adapters.Zombie.Patches.ZombieEntityMappingDiagnosticPatch"
            };

            return expected.All(fullName => assembly.GetTypes().Count(type => type.FullName == fullName) == 1);
        }

        private static bool Test_LegacyItemZombieAuthoritiesAreAbsent(Assembly assembly)
        {
            string[] legacyNames =
            {
                "SteamP2PFriends.Core.Patches.ItemManagerWorldSyncDiagnosticPatch",
                "SteamP2PFriends.Core.Patches.AuthoritativeItemGenerationGatePatch",
                "SteamP2PFriends.Core.Patches.ItemManagerP0B3PreGeneratePatch",
                "SteamP2PFriends.Core.Patches.ItemManagerP0B6RegenerateOnLevelLoadedPatch",
                "SteamP2PFriends.Core.Patches.ZombieManagerWorldSyncDiagnosticPatch",
                "SteamP2PFriends.Core.Patches.ZombieManagerP0C1SendZombieStatesPatch",
                "SteamP2PFriends.Core.Patches.ZombieManagerP0DGenerateZombiesPatch",
                "SteamP2PFriends.Core.Patches.P0EZombieLifecycle.ZombieLifecyclePatch",
                "SteamP2PFriends.Core.Patches.P0EZombieLifecycle.ZombieLifecycleState",
                "SteamP2PFriends.Core.Patches.P0EZombieLifecycle.ZombieLifecycleOwnerVerify",
                "SteamP2PFriends.Core.Patches.P0EDiagnostic.ZombieEntityMappingDiagnosticPatch"
            };

            return legacyNames.All(fullName => assembly.GetTypes().All(type => type.FullName != fullName));
        }
    }
}
