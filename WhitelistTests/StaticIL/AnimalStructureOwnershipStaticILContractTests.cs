using System;
using System.Linq;
using System.Reflection;
using SteamP2PFriends.Core.Ownership;

namespace SteamP2PFriends.WhitelistTests
{
    /// <summary>
    /// Ticket 07 的结构接缝：验证 Animal、Structure、Barricade 的领域入口唯一，
    /// 并把 Vehicle 与未能证明单一归属的跨领域补丁留在 Pending 记录中。
    /// 断言只针对编译产物结构，不宣称 Harmony Runtime 行为通过。
    /// </summary>
    internal static class AnimalStructureOwnershipStaticILContractTests
    {
        internal static bool Test_All()
        {
            Assembly assembly = typeof(SteamP2PFriendsPlugin).Assembly;
            return Test_AnimalAuthority(assembly)
                && Test_StructureAuthority(assembly)
                && Test_LegacyStructureAuthoritiesAbsent(assembly)
                && Test_LegacyAnimalAuthoritiesAbsent(assembly)
                && Test_VehicleRemainsPending(assembly)
                && Test_OtherDomainStatusesRecorded()
                && ModuleOwnershipCatalog.HasRegistrationTraceCoverage()
                && Test_RegistrationContractShape(assembly);
        }

        private static bool Test_AnimalAuthority(Assembly assembly)
        {
            string[] expected =
            {
                "SteamP2PFriends.Adapters.Animal.AnimalDomainAdapter",
                "SteamP2PFriends.Adapters.Animal.Patches.AnimalManagerP0C2SendAnimalStatesPatch",
                "SteamP2PFriends.Adapters.Animal.Patches.AnimalManagerWorldSyncDiagnosticPatch"
            };

            return expected.All(fullName => assembly.GetTypes().Count(type => type.FullName == fullName) == 1);
        }

        private static bool Test_StructureAuthority(Assembly assembly)
        {
            string[] expected =
            {
                "SteamP2PFriends.Adapters.Structure.BuildingDomainAdapter",
                "SteamP2PFriends.Adapters.Structure.Patches.BarricadeManagerRegionSyncPatch",
                "SteamP2PFriends.Adapters.Structure.Patches.StructureManagerRegionSyncPatch",
                "SteamP2PFriends.Adapters.Structure.Patches.P0EBarricadeLifecycle.BarricadeLifecycleRegistration"
            };

            return expected.All(fullName => assembly.GetTypes().Count(type => type.FullName == fullName) == 1);
        }

        private static bool Test_LegacyStructureAuthoritiesAbsent(Assembly assembly)
        {
            string[] legacyNames =
            {
                "SteamP2PFriends.Core.Patches.BarricadeManagerRegionSyncPatch",
                "SteamP2PFriends.Core.Patches.StructureManagerRegionSyncPatch",
                "SteamP2PFriends.Core.Patches.P0EBarricadeLifecycle.BarricadeLifecycleDiagQuota",
                "SteamP2PFriends.Core.Patches.P0EBarricadeLifecycle.BarricadeLifecycleHelper",
                "SteamP2PFriends.Core.Patches.P0EBarricadeLifecycle.BarricadeLifecycleILMatcher",
                "SteamP2PFriends.Core.Patches.P0EBarricadeLifecycle.BarricadeLifecycleOwnerVerify",
                "SteamP2PFriends.Core.Patches.P0EBarricadeLifecycle.BarricadeLifecycleRegistration",
                "SteamP2PFriends.Core.Patches.P0EBarricadeLifecycle.BarricadeLifecycleTranspiler"
            };

            return legacyNames.All(fullName => assembly.GetTypes().All(type => type.FullName != fullName));
        }

        private static bool Test_LegacyAnimalAuthoritiesAbsent(Assembly assembly)
        {
            string[] legacyNames =
            {
                "SteamP2PFriends.Core.Patches.AnimalManagerP0C2SendAnimalStatesPatch",
                "SteamP2PFriends.Core.Patches.AnimalManagerWorldSyncDiagnosticPatch"
            };

            return legacyNames.All(fullName => assembly.GetTypes().All(type => type.FullName != fullName));
        }

        private static bool Test_VehicleRemainsPending(Assembly assembly)
        {
            string[] expectedCoreTypes =
            {
                "SteamP2PFriends.Core.Patches.VehicleManagerP0C1ReplicationPatch",
                "SteamP2PFriends.Core.Patches.VehicleManagerWorldSyncDiagnosticPatch",
                "SteamP2PFriends.Core.Patches.VehicleEnterDiagnosticPatch"
            };

            return expectedCoreTypes.All(fullName => assembly.GetTypes().Count(type => type.FullName == fullName) == 1)
                && assembly.GetTypes().All(type => !(type.FullName ?? string.Empty).StartsWith(
                    "SteamP2PFriends.Adapters.Vehicle", StringComparison.Ordinal));
        }

        private static bool Test_OtherDomainStatusesRecorded()
        {
            ModuleOwnershipRecord animal = Find("Adapters.Animal");
            ModuleOwnershipRecord structure = Find("Adapters.Structure");
            ModuleOwnershipRecord vehicle = Find("Adapters.Vehicle");
            ModuleOwnershipRecord objectDomain = Find("Adapters.Object");
            ModuleOwnershipRecord barricade = Find("Adapters.Barricade");
            ModuleOwnershipRecord pending = Find("Adapters.OtherPending");

            return animal != null
                && animal.PhysicalRoot == "Adapters/Animal"
                && animal.AuthorityType == "AnimalDomainAdapter"
                && structure != null
                && structure.PhysicalRoot == "Adapters/Structure"
                && structure.AuthorityType == "BuildingDomainAdapter"
                && barricade != null
                && barricade.PhysicalRoot == "Adapters/Structure"
                && barricade.AuthorityType == "BuildingDomainAdapter"
                && vehicle != null
                && vehicle.PhysicalRoot == "Core/Patches"
                && vehicle.CrossDomainReason.IndexOf("Pending", StringComparison.Ordinal) >= 0
                && objectDomain != null
                && objectDomain.PhysicalRoot == "Core/Patches"
                && objectDomain.CrossDomainReason.IndexOf("Pending", StringComparison.Ordinal) >= 0
                && pending != null
                && pending.IsControlledCrossDomain;
        }

        private static bool Test_RegistrationContractShape(Assembly assembly)
        {
            string expectedOwner = "com.yu80rice.steamp2pfriends";
            string[] patchTypes =
            {
                "SteamP2PFriends.Adapters.Animal.Patches.AnimalManagerP0C2SendAnimalStatesPatch",
                "SteamP2PFriends.Adapters.Animal.Patches.AnimalManagerWorldSyncDiagnosticPatch",
                "SteamP2PFriends.Adapters.Structure.Patches.BarricadeManagerRegionSyncPatch",
                "SteamP2PFriends.Adapters.Structure.Patches.StructureManagerRegionSyncPatch",
                "SteamP2PFriends.Adapters.Structure.Patches.P0EBarricadeLifecycle.BarricadeLifecycleTranspiler"
            };

            foreach (string fullName in patchTypes)
            {
                Type type = assembly.GetType(fullName, false);
                if (type == null) return false;

                System.Reflection.FieldInfo owner = type.GetField(
                    "HarmonyId", BindingFlags.NonPublic | BindingFlags.Static);
                if (owner != null && !string.Equals(owner.GetValue(null) as string, expectedOwner,
                    StringComparison.Ordinal))
                    return false;

                if (!type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                    .Any(method => method.GetCustomAttributes(false).Any(attribute =>
                        attribute.GetType().FullName == "HarmonyLib.HarmonyPatch")))
                    return false;
            }

            Type lifecycle = assembly.GetType(
                "SteamP2PFriends.Adapters.Structure.Patches.P0EBarricadeLifecycle.BarricadeLifecycleRegistration",
                false);
            return lifecycle != null
                && lifecycle.GetMethod("RegisterAtomically", BindingFlags.Public | BindingFlags.Static) != null
                && lifecycle.GetMethod("VerifyAll", BindingFlags.NonPublic | BindingFlags.Static) != null;
        }

        private static ModuleOwnershipRecord Find(string moduleId)
        {
            return ModuleOwnershipCatalog.Snapshot.FirstOrDefault(record => record.ModuleId == moduleId);
        }
    }
}
