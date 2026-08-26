using System;
using System.Linq;
using SteamP2PFriends.Core.Ownership;
using System.Reflection;

namespace SteamP2PFriends.WhitelistTests
{
    /// <summary>
    /// Ticket 05 的静态结构接缝：验证 Resource/Collision 补丁的物理领域归属
    /// 已反映到编译命名空间，且不存在旧 Core.Patches 的重复权威类型。
    /// 这组断言只验证编译产物结构，不宣称 Harmony Runtime 行为通过。
    /// </summary>
    internal static class ResourceCollisionOwnershipStaticILContractTests
    {
        internal static bool Test_All()
        {
            Assembly assembly = typeof(SteamP2PFriendsPlugin).Assembly;
            return Test_DomainPatchTypesHaveExpectedFullNames(assembly)
                && Test_ResourcePatchTypesHaveResourceOwnership(assembly)
                && Test_CollisionPatchTypesHaveCollisionOwnership(assembly)
                && Test_LegacyPatchNamespacesHaveNoCompiledAuthority(assembly)
                && ModuleOwnershipCatalog.HasResourceCollisionOwnership();
        }

        private static bool Test_DomainPatchTypesHaveExpectedFullNames(Assembly assembly)
        {
            string[] expectedFullNames =
            {
                "SteamP2PFriends.Adapters.Resource.Patches.ResourceManagerWorldSyncDiagnosticPatch",
                "SteamP2PFriends.Adapters.Resource.Patches.ResourceManagerRegionSyncPatch",
                "SteamP2PFriends.Adapters.Resource.Patches.ResourceManagerHarvestReplicationPatch",
                "SteamP2PFriends.Adapters.Resource.Patches.LevelGroundRemoteTreeCollisionPatch",
                "SteamP2PFriends.Adapters.Collision.Patches.LevelObjectRemoteCollisionPatch"
            };

            return expectedFullNames.All(fullName => assembly.GetTypes().Count(type => type.FullName == fullName) == 1)
                && assembly.GetTypes().Where(type => expectedFullNames.Contains(
                    type.FullName ?? string.Empty, StringComparer.Ordinal)).Count() == expectedFullNames.Length;
        }

        private static bool Test_ResourcePatchTypesHaveResourceOwnership(Assembly assembly)
        {
            string[] expected =
            {
                "SteamP2PFriends.Adapters.Resource.Patches.ResourceManagerWorldSyncDiagnosticPatch",
                "SteamP2PFriends.Adapters.Resource.Patches.ResourceManagerRegionSyncPatch",
                "SteamP2PFriends.Adapters.Resource.Patches.ResourceManagerHarvestReplicationPatch",
                "SteamP2PFriends.Adapters.Resource.Patches.LevelGroundRemoteTreeCollisionPatch"
            };

            return expected.All(fullName => assembly.GetTypes().Count(type => type.FullName == fullName) == 1);
        }

        private static bool Test_CollisionPatchTypesHaveCollisionOwnership(Assembly assembly)
        {
            return assembly.GetTypes().Count(type =>
                       type.FullName ==
                       "SteamP2PFriends.Adapters.Collision.Patches.LevelObjectRemoteCollisionPatch") == 1;
        }

        private static bool Test_LegacyPatchNamespacesHaveNoCompiledAuthority(Assembly assembly)
        {
            string[] legacyNames =
            {
                "SteamP2PFriends.Core.Patches.ResourceManagerWorldSyncDiagnosticPatch",
                "SteamP2PFriends.Core.Patches.ResourceManagerRegionSyncPatch",
                "SteamP2PFriends.Core.Patches.ResourceManagerHarvestReplicationPatch",
                "SteamP2PFriends.Core.Patches.LevelGroundRemoteTreeCollisionPatch",
                "SteamP2PFriends.Core.Patches.LevelObjectRemoteCollisionPatch"
            };

            return legacyNames.All(fullName => assembly.GetTypes().All(type => type.FullName != fullName));
        }

    }
}
