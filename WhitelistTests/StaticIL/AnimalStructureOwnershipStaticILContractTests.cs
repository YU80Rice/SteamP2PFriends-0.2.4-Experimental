using SteamP2PFriends.Core.Ownership;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace SteamP2PFriends.WhitelistTests
{
    /// <summary>
    /// Ticket 07 的结构接缝：验证 Animal、Structure、Barricade 的领域入口唯一，
    /// 并把 Vehicle 与未能证明单一归属的跨领域补丁留在 Pending 记录中。
    ///
    /// registration evidence 是静态审计目录，不执行 Harmony 注册，也不把编译产物
    /// 结构升级为 Runtime 证据。每一行绑定到实际编译类型、方法、HarmonyPatch
    /// target、patch type、owner、priority 和 Registration Trace order。
    /// </summary>
    internal static class AnimalStructureOwnershipStaticILContractTests
    {
        private const string ExpectedOwner = SteamP2PFriendsPlugin.HARMONY_ID;
        private const string HarmonyPatchAttributeName = "HarmonyLib.HarmonyPatch";

        private sealed class RegistrationEvidence
        {
            internal RegistrationEvidence(int order, string traceId, string domain,
                string patchTypeFullName, string patchMethodName, string targetTypeFullName,
                string targetMethodName, string patchKind, string ownerTypeFullName,
                string ownerFieldName, string priority)
            {
                Order = order;
                TraceId = traceId;
                Domain = domain;
                PatchTypeFullName = patchTypeFullName;
                PatchMethodName = patchMethodName;
                TargetTypeFullName = targetTypeFullName;
                TargetMethodName = targetMethodName;
                PatchKind = patchKind;
                OwnerTypeFullName = ownerTypeFullName;
                OwnerFieldName = ownerFieldName;
                Priority = priority;
            }

            internal int Order { get; }
            internal string TraceId { get; }
            internal string Domain { get; }
            internal string PatchTypeFullName { get; }
            internal string PatchMethodName { get; }
            internal string TargetTypeFullName { get; }
            internal string TargetMethodName { get; }
            internal string PatchKind { get; }
            internal string OwnerTypeFullName { get; }
            internal string OwnerFieldName { get; }
            internal string Priority { get; }
        }

        // 来源：docs/architecture/registration-trace.md §1.1 与 §6.2。
        // order 是 U3-SDK/Registration Trace 的静态调用顺序，不是 Harmony Runtime 排序。
        private static readonly IReadOnlyList<RegistrationEvidence> Evidence =
            new List<RegistrationEvidence>
            {
                new RegistrationEvidence(2, "U3-REG-05-WorldSyncAndAdapters", "Barricade",
                    "SteamP2PFriends.Adapters.Structure.Patches.BarricadeManagerRegionSyncPatch",
                    "OnRegionUpdated_Transpiler", "SDG.Unturned.BarricadeManager", "onRegionUpdated",
                    "Transpiler", "SteamP2PFriends.SteamP2PFriendsPlugin", "HARMONY_ID", "default"),
                new RegistrationEvidence(2, "U3-REG-05-WorldSyncAndAdapters", "Barricade",
                    "SteamP2PFriends.Adapters.Structure.Patches.BarricadeManagerRegionSyncPatch",
                    "SendRegion_Prefix", "SDG.Unturned.BarricadeManager", "SendRegion",
                    "Prefix", "SteamP2PFriends.SteamP2PFriendsPlugin", "HARMONY_ID", "default"),
                new RegistrationEvidence(3, "U3-REG-05-WorldSyncAndAdapters", "Structure",
                    "SteamP2PFriends.Adapters.Structure.Patches.StructureManagerRegionSyncPatch",
                    "OnRegionUpdated_Transpiler", "SDG.Unturned.StructureManager", "onRegionUpdated",
                    "Transpiler", "SteamP2PFriends.SteamP2PFriendsPlugin", "HARMONY_ID", "default"),
                new RegistrationEvidence(3, "U3-REG-05-WorldSyncAndAdapters", "Structure",
                    "SteamP2PFriends.Adapters.Structure.Patches.StructureManagerRegionSyncPatch",
                    "AskStructures_Prefix", "SDG.Unturned.StructureManager", "askStructures",
                    "Prefix", "SteamP2PFriends.SteamP2PFriendsPlugin", "HARMONY_ID", "default"),
                new RegistrationEvidence(45, "U3-REG-02-InternalDiagnostics", "Animal",
                    "SteamP2PFriends.Adapters.Animal.Patches.AnimalManagerWorldSyncDiagnosticPatch",
                    "Update_Prefix", "SDG.Unturned.AnimalManager", "Update", "Prefix",
                    "SteamP2PFriends.SteamP2PFriendsPlugin", "HARMONY_ID", "default"),
                new RegistrationEvidence(45, "U3-REG-02-InternalDiagnostics", "Animal",
                    "SteamP2PFriends.Adapters.Animal.Patches.AnimalManagerWorldSyncDiagnosticPatch",
                    "SendAnimalStates_Prefix", "SDG.Unturned.AnimalManager", "sendAnimalStates", "Prefix",
                    "SteamP2PFriends.SteamP2PFriendsPlugin", "HARMONY_ID", "default"),
                new RegistrationEvidence(45, "U3-REG-02-InternalDiagnostics", "Animal",
                    "SteamP2PFriends.Adapters.Animal.Patches.AnimalManagerWorldSyncDiagnosticPatch",
                    "SpawnAnimal_Prefix", "SDG.Unturned.AnimalManager", "spawnAnimal", "Prefix",
                    "SteamP2PFriends.SteamP2PFriendsPlugin", "HARMONY_ID", "default"),
                new RegistrationEvidence(45, "U3-REG-02-InternalDiagnostics", "Animal",
                    "SteamP2PFriends.Adapters.Animal.Patches.AnimalManagerWorldSyncDiagnosticPatch",
                    "ReceiveMultipleAnimals_Prefix", "SDG.Unturned.AnimalManager", "ReceiveMultipleAnimals", "Prefix",
                    "SteamP2PFriends.SteamP2PFriendsPlugin", "HARMONY_ID", "default"),
                new RegistrationEvidence(45, "U3-REG-02-InternalDiagnostics", "Animal",
                    "SteamP2PFriends.Adapters.Animal.Patches.AnimalManagerWorldSyncDiagnosticPatch",
                    "ReceiveAnimalStates_Prefix", "SDG.Unturned.AnimalManager", "ReceiveAnimalStates", "Prefix",
                    "SteamP2PFriends.SteamP2PFriendsPlugin", "HARMONY_ID", "default"),
                new RegistrationEvidence(51, "U3-REG-05-WorldSyncAndAdapters", "Animal",
                    "SteamP2PFriends.Adapters.Animal.Patches.AnimalManagerP0C2SendAnimalStatesPatch",
                    "Update_Transpiler", "SDG.Unturned.AnimalManager", "Update", "Transpiler",
                    "SteamP2PFriends.SteamP2PFriendsPlugin", "HARMONY_ID", "default"),
                new RegistrationEvidence(65, "U3-REG-05-WorldSyncAndAdapters", "Barricade",
                    "SteamP2PFriends.Adapters.Structure.Patches.P0EBarricadeLifecycle.BarricadeLifecycleTranspiler",
                    "Equip_Transpiler", "SDG.Unturned.UseableBarricade", "equip", "Transpiler",
                    "SteamP2PFriends.SteamP2PFriendsPlugin", "HARMONY_ID", "Priority.Normal"),
                new RegistrationEvidence(65, "U3-REG-05-WorldSyncAndAdapters", "Barricade",
                    "SteamP2PFriends.Adapters.Structure.Patches.P0EBarricadeLifecycle.BarricadeLifecycleTranspiler",
                    "CheckClaims_Transpiler", "SDG.Unturned.UseableBarricade", "checkClaims", "Transpiler",
                    "SteamP2PFriends.SteamP2PFriendsPlugin", "HARMONY_ID", "Priority.Normal")
            };

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
                && Test_RegistrationEvidence(assembly)
                && Test_RegistrationPostVerificationShape(assembly);
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

        private static bool Test_RegistrationEvidence(Assembly assembly)
        {
            if (Evidence.Count != 12 || Evidence.Any(item => string.IsNullOrEmpty(item.TraceId))) return false;
            if (!HasActualHarmonyOwnerConstruction(assembly)) return false;
            if (Evidence.Any(item => item.Domain != "Animal" && item.Domain != "Structure" && item.Domain != "Barricade")) return false;
            if (!Evidence.Select(item => item.Order).Distinct().OrderBy(order => order)
                .SequenceEqual(new[] { 2, 3, 45, 51, 65 })) return false;
            if (Evidence.Any(item => item.Order != ExpectedOrder(item))) return false;

            foreach (RegistrationEvidence evidence in Evidence)
            {
                Type patchType = assembly.GetType(evidence.PatchTypeFullName, false);
                Type ownerType = assembly.GetType(evidence.OwnerTypeFullName, false);
                if (patchType == null || ownerType == null) return false;

                MethodInfo patchMethod = patchType.GetMethod(evidence.PatchMethodName,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (patchMethod == null || !HasPatchKind(patchMethod, evidence.PatchKind)) return false;
                if (!HasHarmonyTarget(patchMethod, evidence.TargetTypeFullName, evidence.TargetMethodName)) return false;

                FieldInfo ownerField = ownerType.GetField(evidence.OwnerFieldName,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (ownerField == null) return false;
                object ownerValue = ownerField.IsLiteral
                    ? ownerField.GetRawConstantValue()
                    : ownerField.GetValue(null);
                if (!string.Equals(ownerValue as string, ExpectedOwner, StringComparison.Ordinal)) return false;

                if (evidence.Priority == "default")
                {
                    if (patchMethod.GetCustomAttributes(false).Any(attribute =>
                        attribute.GetType().FullName == "HarmonyLib.HarmonyPriority")) return false;
                }
                else if (evidence.Priority == "Priority.Normal")
                {
                    Type registration = assembly.GetType(
                        "SteamP2PFriends.Adapters.Structure.Patches.P0EBarricadeLifecycle.BarricadeLifecycleRegistration", false);
                    FieldInfo priority = registration?.GetField("RegisteredTranspilerPriority",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    object priorityValue = priority?.GetValue(null);
                    if (priority == null || (int)priorityValue != 400) return false;
                }
                else return false;
            }

            bool callEvidence = Test_RegistrationCallIL(assembly);
            return callEvidence;
        }

        private static bool HasActualHarmonyOwnerConstruction(Assembly assembly)
        {
            Type plugin = assembly.GetType("SteamP2PFriends.SteamP2PFriendsPlugin", false);
            MethodInfo awake = plugin?.GetMethod("Awake", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (awake == null) return false;
            IlEvidence evidence = ReadIlEvidence(awake);
            return evidence.Strings.Contains(ExpectedOwner)
                && evidence.CalledMethods.Any(called =>
                    called.DeclaringType?.FullName == "HarmonyLib.Harmony" && called.Name == ".ctor");
        }

        private static int ExpectedOrder(RegistrationEvidence evidence)
        {
            if (evidence.PatchMethodName == "OnRegionUpdated_Transpiler" && evidence.Domain == "Barricade") return 2;
            if (evidence.PatchMethodName == "SendRegion_Prefix") return 2;
            if (evidence.PatchMethodName == "OnRegionUpdated_Transpiler" && evidence.Domain == "Structure") return 3;
            if (evidence.PatchMethodName == "AskStructures_Prefix") return 3;
            if (evidence.PatchMethodName == "Equip_Transpiler" || evidence.PatchMethodName == "CheckClaims_Transpiler") return 65;
            if (evidence.Domain == "Animal" && evidence.PatchMethodName == "Update_Transpiler") return 51;
            if (evidence.Domain == "Animal") return 45;
            return -1;
        }

        private static bool Test_RegistrationPostVerificationShape(Assembly assembly)
        {
            string[] manualTypes =
            {
                "SteamP2PFriends.Adapters.Animal.Patches.AnimalManagerP0C2SendAnimalStatesPatch",
                "SteamP2PFriends.Adapters.Animal.Patches.AnimalManagerWorldSyncDiagnosticPatch",
                "SteamP2PFriends.Adapters.Structure.Patches.BarricadeManagerRegionSyncPatch",
                "SteamP2PFriends.Adapters.Structure.Patches.StructureManagerRegionSyncPatch"
            };

            foreach (string fullName in manualTypes)
            {
                Type type = assembly.GetType(fullName, false);
                if (type?.GetMethod("RegisterManual", BindingFlags.Public | BindingFlags.Static) == null)
                    return false;
                if (type.GetProperty("AllRegistrationsSucceeded", BindingFlags.Public | BindingFlags.Static) == null)
                    return false;
            }

            Type animalDiagnostic = assembly.GetType(
                "SteamP2PFriends.Adapters.Animal.Patches.AnimalManagerWorldSyncDiagnosticPatch", false);
            Type lifecycle = assembly.GetType(
                "SteamP2PFriends.Adapters.Structure.Patches.P0EBarricadeLifecycle.BarricadeLifecycleRegistration", false);
            return animalDiagnostic?.GetMethod("VerifyRegistration", BindingFlags.Public | BindingFlags.Static) != null
                && lifecycle?.GetMethod("RegisterAtomically", BindingFlags.Public | BindingFlags.Static) != null
                && lifecycle.GetMethod("VerifyAll", BindingFlags.NonPublic | BindingFlags.Static) != null
                && lifecycle.GetMethod("RollbackBoth", BindingFlags.NonPublic | BindingFlags.Static) != null;
        }

        private static bool Test_RegistrationCallIL(Assembly assembly)
        {
            bool[] checks =
            {
                ContainsRegistrationEvidence(assembly,
                    "SteamP2PFriends.Adapters.Animal.Patches.AnimalManagerP0C2SendAnimalStatesPatch",
                    "RegisterTranspiler", "SDG.Unturned.AnimalManager", "Update",
                    "SteamP2PFriends.Adapters.Animal.Patches.AnimalManagerP0C2SendAnimalStatesPatch",
                    "Update_Transpiler", true),
                ContainsIdentityRegistrationEvidence(assembly, "Update", "Update_Prefix", "Animal.Update.Pre"),
                ContainsIdentityRegistrationEvidence(assembly, "sendAnimalStates", "SendAnimalStates_Prefix", "Animal.sendAnimalStates.Pre"),
                ContainsIdentityRegistrationEvidence(assembly, "spawnAnimal", "SpawnAnimal_Prefix", "Animal.spawnAnimal.Pre"),
                ContainsIdentityRegistrationEvidence(assembly, "ReceiveMultipleAnimals", "ReceiveMultipleAnimals_Prefix", "Animal.ReceiveMultipleAnimals.Pre"),
                ContainsIdentityRegistrationEvidence(assembly, "ReceiveAnimalStates", "ReceiveAnimalStates_Prefix", "Animal.ReceiveAnimalStates.Pre"),
                ContainsRegistrationEvidence(assembly,
                    "SteamP2PFriends.Adapters.Structure.Patches.BarricadeManagerRegionSyncPatch",
                    "RegisterTranspiler", "SDG.Unturned.BarricadeManager", "onRegionUpdated",
                    "SteamP2PFriends.Adapters.Structure.Patches.BarricadeManagerRegionSyncPatch",
                    "OnRegionUpdated_Transpiler", true),
                ContainsRegistrationEvidence(assembly,
                    "SteamP2PFriends.Adapters.Structure.Patches.BarricadeManagerRegionSyncPatch",
                    "RegisterSendRegionPrefix", "SDG.Unturned.BarricadeManager", "SendRegion",
                    "SteamP2PFriends.Adapters.Structure.Patches.BarricadeManagerRegionSyncPatch",
                    "SendRegion_Prefix", true),
                ContainsRegistrationEvidence(assembly,
                    "SteamP2PFriends.Adapters.Structure.Patches.StructureManagerRegionSyncPatch",
                    "RegisterTranspiler", "SDG.Unturned.StructureManager", "onRegionUpdated",
                    "SteamP2PFriends.Adapters.Structure.Patches.StructureManagerRegionSyncPatch",
                    "OnRegionUpdated_Transpiler", true),
                ContainsRegistrationEvidence(assembly,
                    "SteamP2PFriends.Adapters.Structure.Patches.StructureManagerRegionSyncPatch",
                    "RegisterAskStructuresPrefix", "SDG.Unturned.StructureManager", "askStructures",
                    "SteamP2PFriends.Adapters.Structure.Patches.StructureManagerRegionSyncPatch",
                    "AskStructures_Prefix", true),
                ContainsCachedTargetEvidence(assembly, "equip", "Equip_Transpiler"),
                ContainsCachedTargetEvidence(assembly, "checkClaims", "CheckClaims_Transpiler"),
                ContainsHarmonyPatchCall(assembly,
                    "SteamP2PFriends.Adapters.Structure.Patches.P0EBarricadeLifecycle.BarricadeLifecycleRegistration",
                    "RegisterAtomically", "RegisteredTranspilerPriority")
            };
            return checks.All(check => check);
        }

        private static bool ContainsRegistrationEvidence(Assembly assembly, string typeName,
            string methodName, string targetTypeName, string targetMethodName,
            string patchTypeName, string patchMethodName, bool requireHarmonyPatchCall)
        {
            Type type = assembly.GetType(typeName, false);
            MethodInfo method = type?.GetMethod(methodName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (method == null) return false;

            List<IlInstruction> instructions = ReadInstructions(method);
            return TryFindHarmonyPatchCall(instructions, targetTypeName, targetMethodName,
                patchTypeName, patchMethodName, out _);
        }

        private static bool ContainsCachedTargetEvidence(Assembly assembly,
            string targetMethodName, string patchMethodName)
        {
            Type type = assembly.GetType(
                "SteamP2PFriends.Adapters.Structure.Patches.P0EBarricadeLifecycle.BarricadeLifecycleRegistration", false);
            MethodInfo method = type?.GetMethod("CacheAllMethodInfos",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (method == null) return false;

            List<IlInstruction> instructions = ReadInstructions(method);
            List<int> accessToolsCalls = new List<int>();
            bool targetEvidence = false;
            bool patchEvidence = false;
            for (int index = 0; index < instructions.Count; index++)
            {
                MethodBase called = instructions[index].Operand as MethodBase;
                if (called?.DeclaringType?.FullName == "HarmonyLib.AccessTools" && called.Name == "Method")
                    accessToolsCalls.Add(index);
            }

            for (int i = 0; i < accessToolsCalls.Count; i++)
            {
                int previous = i == 0 ? -1 : accessToolsCalls[i - 1];
                int next = i + 1 < accessToolsCalls.Count ? accessToolsCalls[i + 1] : instructions.Count;
                IEnumerable<object> callOperands = instructions.Skip(previous + 1)
                    .Take(next - previous - 1).Select(item => item.Operand).ToList();
                bool targetOk = callOperands.OfType<Type>().Any(item =>
                        item.FullName == "SDG.Unturned.UseableBarricade")
                    && callOperands.OfType<string>().Contains(targetMethodName)
                    && callOperands.OfType<FieldInfo>().Any(item => item.Name ==
                        (targetMethodName == "equip" ? "_equipMethod" : "_checkClaimsMethod"));
                bool patchOk = callOperands.OfType<Type>().Any(item => item.FullName ==
                        "SteamP2PFriends.Adapters.Structure.Patches.P0EBarricadeLifecycle.BarricadeLifecycleTranspiler")
                    && callOperands.OfType<string>().Contains(patchMethodName)
                    && callOperands.OfType<FieldInfo>().Any(item => item.Name ==
                        (patchMethodName == "Equip_Transpiler" ? "_equipTranspiler" : "_checkClaimsTranspiler"));
                if (targetOk) targetEvidence = true;
                if (patchOk) patchEvidence = true;
            }
            return targetEvidence && patchEvidence;
        }

        private static bool TryFindHarmonyPatchCall(List<IlInstruction> instructions,
            string targetTypeName, string targetMethodName, string patchTypeName, string patchMethodName,
            out IlCallSite matched)
        {
            matched = null;
            string expectedTarget = "method:" + targetTypeName + "|" + targetMethodName;
            string expectedPatch = "method:" + patchTypeName + "|" + patchMethodName;
            foreach (IlCallSite site in ReadCallSites(instructions))
            {
                if (site.Method?.DeclaringType?.FullName != "HarmonyLib.Harmony" || site.Method.Name != "Patch") continue;
                if (site.Arguments.Count == 0 || site.Arguments[0] == null) continue;

                IlValue patch = site.Arguments.FirstOrDefault(value => value?.Kind == "HarmonyMethod"
                    && value.Children.Count > 0
                    && string.Equals(value.Children[0].Value as string, expectedPatch, StringComparison.Ordinal));
                if (site.Arguments[0].Kind == "Method"
                    && string.Equals(site.Arguments[0].Value as string, expectedTarget, StringComparison.Ordinal)
                    && patch != null)
                {
                    matched = site;
                    return true;
                }
            }
            return false;
        }

        private static Dictionary<int, IlValue> BuildAccessToolsLocalValues(List<IlInstruction> instructions)
        {
            var values = new Dictionary<int, IlValue>();
            var accessCalls = new List<int>();
            for (int index = 0; index < instructions.Count; index++)
            {
                MethodBase called = instructions[index].Operand as MethodBase;
                if (called?.DeclaringType?.FullName == "HarmonyLib.AccessTools" && called.Name == "Method")
                    accessCalls.Add(index);
            }

            for (int i = 0; i < accessCalls.Count; i++)
            {
                int call = accessCalls[i];
                int next = i + 1 < accessCalls.Count ? accessCalls[i + 1] : instructions.Count;
                string typeName = instructions.Skip(i == 0 ? 0 : accessCalls[i - 1] + 1)
                    .Take(call - (i == 0 ? 0 : accessCalls[i - 1] + 1) + 1)
                    .Select(item => item.Operand).OfType<Type>()
                    .Select(item => item.FullName).FirstOrDefault();
                string methodName = instructions.Skip(i == 0 ? 0 : accessCalls[i - 1] + 1)
                    .Take(call - (i == 0 ? 0 : accessCalls[i - 1] + 1) + 1)
                    .Select(item => item.Operand).OfType<string>().LastOrDefault();
                int? local = null;
                for (int cursor = call + 1; cursor < next; cursor++)
                {
                    int? stored = GetStoredLocalIndex(instructions[cursor]);
                    if (stored.HasValue) { local = stored; break; }
                }
                if (local.HasValue && !string.IsNullOrEmpty(typeName) && !string.IsNullOrEmpty(methodName))
                {
                    values[local.Value] = new IlValue
                    {
                        Kind = "Method",
                        Value = "method:" + typeName + "|" + methodName
                    };
                }
            }
            return values;
        }

        private static IEnumerable<IlCallSite> ReadCallSites(List<IlInstruction> instructions)
        {
            Dictionary<int, IlValue> localValues = BuildAccessToolsLocalValues(instructions);
            for (int index = 0; index < instructions.Count; index++)
            {
                MethodBase called = instructions[index].Operand as MethodBase;
                if (called?.DeclaringType?.FullName != "HarmonyLib.Harmony" || called.Name != "Patch") continue;
                if (TrySimulateHarmonyPatchCall(instructions, index, localValues, out IlCallSite site))
                    yield return site;
            }
        }

        private static bool TrySimulateHarmonyPatchCall(List<IlInstruction> instructions, int patchIndex,
            Dictionary<int, IlValue> localValues, out IlCallSite site)
        {
            site = null;
            int start = -1;
            for (int index = patchIndex - 1; index >= 0; index--)
            {
                if (instructions[index].OpCode.Name == "ldarg.0") { start = index; break; }
            }
            if (start < 0 || patchIndex - start > 40) return false;

            var stack = new List<IlValue>();
            for (int index = start; index <= patchIndex; index++)
            {
                IlInstruction instruction = instructions[index];
                string op = instruction.OpCode.Name;
                if (op == "ldarg.0")
                {
                    stack.Add(new IlValue { Kind = "HarmonyInstance" });
                    continue;
                }
                int? loadedLocal = GetLoadedLocalIndex(instruction);
                if (loadedLocal.HasValue)
                {
                    stack.Add(localValues.TryGetValue(loadedLocal.Value, out IlValue value)
                        ? value : new IlValue { Kind = "UnknownLocal", Value = loadedLocal.Value });
                    continue;
                }
                if (op == "ldnull") { stack.Add(new IlValue { Kind = "Null" }); continue; }
                if (instruction.Operand is FieldInfo field && op.StartsWith("ldsfld", StringComparison.Ordinal))
                {
                    stack.Add(new IlValue { Kind = "Field", Value = field.Name });
                    continue;
                }

                MethodBase called = instruction.Operand as MethodBase;
                if (called?.DeclaringType?.FullName == "HarmonyLib.HarmonyMethod" && called.Name == ".ctor")
                {
                    int count = called.GetParameters().Length;
                    if (stack.Count < count) return false;
                    var args = stack.Skip(stack.Count - count).Take(count).ToList();
                    stack.RemoveRange(stack.Count - count, count);
                    var harmonyMethod = new IlValue { Kind = "HarmonyMethod" };
                    if (args.Count > 0) harmonyMethod.Children.Add(args[0]);
                    stack.Add(harmonyMethod);
                    continue;
                }
                if (called?.DeclaringType?.FullName == "HarmonyLib.Harmony" && called.Name == "Patch")
                {
                    int count = called.GetParameters().Length;
                    if (stack.Count < count + 1) return false;
                    var args = stack.Skip(stack.Count - count).Take(count).ToList();
                    stack.RemoveRange(stack.Count - count, count);
                    IlValue instance = stack[stack.Count - 1];
                    stack.RemoveAt(stack.Count - 1);
                    if (instance.Kind != "HarmonyInstance") return false;
                    site = new IlCallSite { Index = index, Method = called };
                    site.Arguments.AddRange(args);
                    return true;
                }
            }
            return false;
        }

        private static bool ContainsIdentityRegistrationEvidence(Assembly assembly,
            string targetMethodName, string patchMethodName, string registrationLabel)
        {
            Type type = assembly.GetType(
                "SteamP2PFriends.Adapters.Animal.Patches.AnimalManagerWorldSyncDiagnosticPatch", false);
            MethodInfo method = type?.GetMethod("RegisterManual",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (method == null) return false;

            List<IlInstruction> instructions = ReadInstructions(method);
            Dictionary<int, string> typeLocals = BuildTypeLocalValues(instructions);
            int previousRegistrationCall = -1;
            for (int index = 0; index < instructions.Count; index++)
            {
                MethodBase called = instructions[index].Operand as MethodBase;
                if (called?.DeclaringType?.FullName != "SteamP2PFriends.Core.Patches.WorldSyncDiagnosticCore"
                    || called.Name != "RegisterIdentityPatch") continue;

                int start = previousRegistrationCall + 1;
                previousRegistrationCall = index;
                List<IlInstruction> block = instructions.Skip(start).Take(index - start + 1).ToList();
                int targetTypeIndex = block.FindIndex(instruction =>
                    (instruction.Operand as Type)?.FullName == "SDG.Unturned.AnimalManager");
                int targetNameIndex = FindStringAfter(block, targetMethodName, targetTypeIndex + 1);
                int patchNameIndex = FindStringAfter(block, patchMethodName, targetNameIndex + 1);
                int resolverIndex = block.FindIndex(patchNameIndex + 1, instruction =>
                    (instruction.Operand as MethodBase)?.DeclaringType?.FullName == "HarmonyLib.AccessTools"
                    && (instruction.Operand as MethodBase)?.Name == "Method");
                int labelIndex = FindStringAfter(block, registrationLabel, resolverIndex + 1);
                bool hasPatchTypeLocal = block.Select((instruction, offset) => new { instruction, offset })
                    .Where(item => item.offset < patchNameIndex)
                    .Any(item => GetLoadedLocalIndex(item.instruction) is int local
                        && typeLocals.TryGetValue(local, out string typeName)
                        && typeName == "SteamP2PFriends.Adapters.Animal.Patches.AnimalManagerWorldSyncDiagnosticPatch");
                bool ordered = targetTypeIndex >= 0 && targetNameIndex >= 0 && patchNameIndex >= 0
                    && resolverIndex >= 0 && labelIndex >= 0;
                if (ordered && hasPatchTypeLocal && HasHarmonyArgument(block.Take(targetTypeIndex))) return true;
            }
            return false;
        }

        private static int FindStringAfter(List<IlInstruction> instructions, string expected, int start)
        {
            if (start < 0) return -1;
            for (int index = start; index < instructions.Count; index++)
            {
                if (string.Equals(instructions[index].Operand as string, expected, StringComparison.Ordinal))
                    return index;
            }
            return -1;
        }

        private static Dictionary<int, string> BuildTypeLocalValues(List<IlInstruction> instructions)
        {
            var values = new Dictionary<int, string>();
            for (int index = 0; index + 2 < instructions.Count; index++)
            {
                Type type = instructions[index].Operand as Type;
                MethodBase resolver = instructions[index + 1].Operand as MethodBase;
                if (type == null || resolver?.Name != "GetTypeFromHandle") continue;
                for (int cursor = index + 2; cursor < Math.Min(index + 5, instructions.Count); cursor++)
                {
                    int? local = GetStoredLocalIndex(instructions[cursor]);
                    if (local.HasValue) { values[local.Value] = type.FullName; break; }
                }
            }
            return values;
        }

        private static bool HasHarmonyArgument(IEnumerable<IlInstruction> instructions)
        {
            return instructions.Any(instruction => instruction.OpCode.Name == "ldarg.0"
                || (instruction.OpCode.Name == "ldarg" && (instruction.Operand as int?) == 0));
        }

        private static int? GetLoadedLocalIndex(IlInstruction instruction)
        {
            string op = instruction.OpCode.Name;
            if (op == "ldloc.0") return 0;
            if (op == "ldloc.1") return 1;
            if (op == "ldloc.2") return 2;
            if (op == "ldloc.3") return 3;
            if (op == "ldloc" && instruction.Operand is int index) return index;
            return null;
        }

        private static int? GetStoredLocalIndex(IlInstruction instruction)
        {
            string op = instruction.OpCode.Name;
            if (op == "stloc.0") return 0;
            if (op == "stloc.1") return 1;
            if (op == "stloc.2") return 2;
            if (op == "stloc.3") return 3;
            if (op == "stloc" && instruction.Operand is int index) return index;
            return null;
        }

        private static bool ContainsHarmonyPatchCall(Assembly assembly, string typeName,
            string methodName, string requiredFieldName)
        {
            Type type = assembly.GetType(typeName, false);
            MethodInfo method = type?.GetMethod(methodName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (method == null) return false;
            List<IlInstruction> instructions = ReadInstructions(method);
            bool equipBound = TryFindHarmonyPatchFieldCall(instructions, "_equipMethod", "_equipTranspiler");
            bool claimsBound = TryFindHarmonyPatchFieldCall(instructions, "_checkClaimsMethod", "_checkClaimsTranspiler");
            IlEvidence evidence = ReadIlEvidence(method);
            return equipBound && claimsBound
                && evidence.FieldNames.Contains(requiredFieldName)
                && HasRegistrationFailurePath(instructions);
        }

        private static bool HasRegistrationFailurePath(List<IlInstruction> instructions)
        {
            List<int> patchCalls = FindCallIndices(instructions,
                called => called.DeclaringType?.FullName == "HarmonyLib.Harmony" && called.Name == "Patch");
            List<int> rollbackCalls = FindCallIndices(instructions,
                called => called.DeclaringType?.FullName ==
                    "SteamP2PFriends.Adapters.Structure.Patches.P0EBarricadeLifecycle.BarricadeLifecycleRegistration"
                    && called.Name == "RollbackBoth");
            List<int> verifyCalls = FindCallIndices(instructions,
                called => called.DeclaringType?.FullName ==
                    "SteamP2PFriends.Adapters.Structure.Patches.P0EBarricadeLifecycle.BarricadeLifecycleRegistration"
                    && called.Name == "VerifyAll");
            if (patchCalls.Count != 2 || rollbackCalls.Count < 3 || verifyCalls.Count != 1) return false;
            if (patchCalls.Any(patch => !rollbackCalls.Any(rollback => rollback > patch))) return false;

            int verify = verifyCalls[0];
            bool verifyBranches = instructions.Skip(verify + 1).Take(6)
                .Any(instruction => instruction.OpCode.Name == "brtrue"
                    || instruction.OpCode.Name == "brtrue.s"
                    || instruction.OpCode.Name == "brfalse"
                    || instruction.OpCode.Name == "brfalse.s");
            return verifyBranches && rollbackCalls.Any(rollback => rollback > verify);
        }

        private static List<int> FindCallIndices(List<IlInstruction> instructions,
            Func<MethodBase, bool> predicate)
        {
            var indices = new List<int>();
            for (int index = 0; index < instructions.Count; index++)
            {
                if (instructions[index].Operand is MethodBase called && predicate(called))
                    indices.Add(index);
            }
            return indices;
        }

        private static bool TryFindHarmonyPatchFieldCall(List<IlInstruction> instructions,
            string targetFieldName, string patchFieldName)
        {
            for (int index = 0; index < instructions.Count; index++)
            {
                MethodBase called = instructions[index].Operand as MethodBase;
                if (called?.DeclaringType?.FullName != "HarmonyLib.Harmony" || called.Name != "Patch") continue;
                if (!TrySimulateHarmonyPatchCall(instructions, index,
                    new Dictionary<int, IlValue>(), out IlCallSite site)) continue;
                if (site.Arguments.Count < 4) continue;
                IlValue original = site.Arguments[0];
                IlValue transpiler = site.Arguments[3];
                if (original?.Kind == "Field"
                    && string.Equals(original.Value as string, targetFieldName, StringComparison.Ordinal)
                    && transpiler?.Kind == "HarmonyMethod"
                    && transpiler.Children.Count > 0
                    && transpiler.Children[0].Kind == "Field"
                    && string.Equals(transpiler.Children[0].Value as string,
                        patchFieldName, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private sealed class IlEvidence
        {
            internal readonly HashSet<string> TypeNames = new HashSet<string>(StringComparer.Ordinal);
            internal readonly HashSet<string> Strings = new HashSet<string>(StringComparer.Ordinal);
            internal readonly HashSet<string> FieldNames = new HashSet<string>(StringComparer.Ordinal);
            internal readonly List<MethodBase> CalledMethods = new List<MethodBase>();
        }

        private sealed class IlInstruction
        {
            internal OpCode OpCode { get; set; }
            internal object Operand { get; set; }
        }

        private sealed class IlValue
        {
            internal string Kind { get; set; }
            internal object Value { get; set; }
            internal List<IlValue> Children { get; } = new List<IlValue>();
        }

        private sealed class IlCallSite
        {
            internal int Index { get; set; }
            internal MethodBase Method { get; set; }
            internal List<IlValue> Arguments { get; } = new List<IlValue>();
        }

        private static readonly Dictionary<ushort, OpCode> OpCodeMap = CreateOpCodeMap();

        private static Dictionary<ushort, OpCode> CreateOpCodeMap()
        {
            var map = new Dictionary<ushort, OpCode>();
            foreach (FieldInfo field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (field.FieldType == typeof(OpCode))
                {
                    OpCode code = (OpCode)field.GetValue(null);
                    map[(ushort)code.Value] = code;
                }
            }
            return map;
        }

        private static IlEvidence ReadIlEvidence(MethodInfo method)
        {
            var evidence = new IlEvidence();
            byte[] il = method.GetMethodBody()?.GetILAsByteArray();
            if (il == null) return evidence;

            int offset = 0;
            while (offset < il.Length)
            {
                ushort value = il[offset++];
                if (value == 0xfe && offset < il.Length) value = (ushort)(0xfe00 | il[offset++]);
                if (!OpCodeMap.TryGetValue(value, out OpCode code)) break;

                int operandOffset = offset;
                int token;
                switch (code.OperandType)
                {
                    case OperandType.InlineString:
                        token = BitConverter.ToInt32(il, offset);
                        offset += 4;
                        try { evidence.Strings.Add(method.Module.ResolveString(token)); } catch { }
                        break;
                    case OperandType.InlineMethod:
                        token = BitConverter.ToInt32(il, offset);
                        offset += 4;
                        try { evidence.CalledMethods.Add(method.Module.ResolveMethod(token)); } catch { }
                        break;
                    case OperandType.InlineType:
                    case OperandType.InlineTok:
                        token = BitConverter.ToInt32(il, offset);
                        offset += 4;
                        try
                        {
                            MemberInfo member = method.Module.ResolveMember(token);
                            Type resolvedType = member as Type ?? (member as MethodBase)?.DeclaringType
                                ?? (member as FieldInfo)?.DeclaringType;
                            if (resolvedType != null) evidence.TypeNames.Add(resolvedType.FullName);
                            if (member is FieldInfo field) evidence.FieldNames.Add(field.Name);
                        }
                        catch { }
                        break;
                    case OperandType.InlineField:
                        token = BitConverter.ToInt32(il, offset);
                        offset += 4;
                        try { evidence.FieldNames.Add(method.Module.ResolveField(token).Name); } catch { }
                        break;
                    case OperandType.ShortInlineBrTarget:
                    case OperandType.ShortInlineI:
                        offset += 1;
                        break;
                    case OperandType.ShortInlineVar:
                        offset += 1;
                        break;
                    case OperandType.InlineBrTarget:
                    case OperandType.InlineI:
                    case OperandType.InlineI8:
                    case OperandType.InlineR:
                        offset += code.OperandType == OperandType.InlineI8 || code.OperandType == OperandType.InlineR ? 8 : 4;
                        break;
                    case OperandType.InlineSwitch:
                        if (offset + 4 > il.Length) return evidence;
                        int switchCount = BitConverter.ToInt32(il, offset);
                        if (switchCount < 0 || switchCount > (il.Length - offset - 4) / 4) return evidence;
                        offset += 4 + (switchCount * 4);
                        break;
                    case OperandType.InlineVar:
                        offset += 2;
                        break;
                }

                if (offset < operandOffset || offset > il.Length) break;
            }
            return evidence;
        }

        private static List<IlInstruction> ReadInstructions(MethodInfo method)
        {
            var instructions = new List<IlInstruction>();
            byte[] il = method.GetMethodBody()?.GetILAsByteArray();
            if (il == null) return instructions;

            int offset = 0;
            while (offset < il.Length)
            {
                ushort value = il[offset++];
                if (value == 0xfe && offset < il.Length) value = (ushort)(0xfe00 | il[offset++]);
                if (!OpCodeMap.TryGetValue(value, out OpCode code)) break;

                var instruction = new IlInstruction { OpCode = code };
                switch (code.OperandType)
                {
                    case OperandType.InlineString:
                        try { instruction.Operand = method.Module.ResolveString(BitConverter.ToInt32(il, offset)); } catch { }
                        offset += 4;
                        break;
                    case OperandType.InlineMethod:
                        try { instruction.Operand = method.Module.ResolveMethod(BitConverter.ToInt32(il, offset)); } catch { }
                        offset += 4;
                        break;
                    case OperandType.InlineType:
                        try { instruction.Operand = method.Module.ResolveType(BitConverter.ToInt32(il, offset)); } catch { }
                        offset += 4;
                        break;
                    case OperandType.InlineField:
                        try { instruction.Operand = method.Module.ResolveField(BitConverter.ToInt32(il, offset)); } catch { }
                        offset += 4;
                        break;
                    case OperandType.InlineTok:
                        try { instruction.Operand = method.Module.ResolveMember(BitConverter.ToInt32(il, offset)); } catch { }
                        offset += 4;
                        break;
                    case OperandType.ShortInlineBrTarget:
                    case OperandType.ShortInlineI:
                        offset += 1;
                        break;
                    case OperandType.ShortInlineVar:
                        instruction.Operand = (int)il[offset];
                        offset += 1;
                        break;
                    case OperandType.InlineBrTarget:
                    case OperandType.InlineI:
                        offset += 4;
                        break;
                    case OperandType.InlineI8:
                    case OperandType.InlineR:
                        offset += 8;
                        break;
                    case OperandType.InlineVar:
                        instruction.Operand = (int)BitConverter.ToUInt16(il, offset);
                        offset += 2;
                        break;
                    case OperandType.InlineSwitch:
                        int count = BitConverter.ToInt32(il, offset);
                        offset += 4 + (count * 4);
                        break;
                }
                instructions.Add(instruction);
            }
            return instructions;
        }

        private static bool HasPatchKind(MethodInfo method, string patchKind)
        {
            string expected = "HarmonyLib.Harmony" + patchKind;
            return method.GetCustomAttributes(false).Any(attribute => attribute.GetType().FullName == expected);
        }

        private static bool HasHarmonyTarget(MethodInfo method, string expectedTypeName, string expectedMethodName)
        {
            foreach (object attribute in method.GetCustomAttributes(false))
            {
                if (attribute.GetType().FullName != HarmonyPatchAttributeName) continue;
                FieldInfo infoField = attribute.GetType().GetField("info",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                object info = infoField?.GetValue(attribute);
                Type declaringType = info?.GetType().GetField("declaringType",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(info) as Type;
                string methodName = info?.GetType().GetField("methodName",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(info) as string;
                bool uniqueTarget = declaringType != null && HasUniqueTargetSignature(declaringType, expectedMethodName);
                if (declaringType?.FullName == expectedTypeName && methodName == expectedMethodName && uniqueTarget)
                    return true;
            }
            return false;
        }

        private static bool HasUniqueTargetSignature(Type declaringType, string methodName)
        {
            MethodInfo[] methods = declaringType.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                .Where(candidate => candidate.Name == methodName).ToArray();
            if (methods.Length == 1) return true;
            if (methodName != "askStructures") return false;

            string[] expected =
            {
                "SDG.NetTransport.ITransportConnection",
                "System.Byte",
                "System.Byte",
                "System.Single"
            };
            bool matched = methods.Count(candidate => candidate.GetParameters().Select(parameter =>
                parameter.ParameterType.FullName).SequenceEqual(expected)) == 1;
            return matched;
        }

        private static ModuleOwnershipRecord Find(string moduleId)
        {
            return ModuleOwnershipCatalog.Snapshot.FirstOrDefault(record => record.ModuleId == moduleId);
        }
    }
}
