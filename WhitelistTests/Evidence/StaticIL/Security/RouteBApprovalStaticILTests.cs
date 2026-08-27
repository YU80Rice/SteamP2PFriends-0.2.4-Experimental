using SteamP2PFriends.Core.Patches;
using SteamP2PFriends.Security.Patches;
using System;
using System.Collections.Generic;

namespace SteamP2PFriends.WhitelistTests
{
    internal static class RouteBApprovalStaticILTests
    {
        internal static bool Test_B11_PendingActionAndCommandGatesAreAuthoritative()
        {
            bool actionBlocked = P2PQuarantineActionGatePatch.ShouldBlockForTest(true, true, false, true);
            bool approvedAllowed = !P2PQuarantineActionGatePatch.ShouldBlockForTest(true, true, false, false);
            bool pendingAdminCommandBlocked = P2PListenHostCommandPermissionPatch.ShouldBlock(
                true, true, false, true, true, true, "/god");
            bool approvedAdminCommandAllowed = !P2PListenHostCommandPermissionPatch.ShouldBlock(
                true, true, false, false, true, true, "/god");
            int discovered = 0;
            Type contextRef = typeof(SDG.Unturned.ServerInvocationContext).MakeByRefType();
            var requiredContext = new[]
            {
                ResolveU3Method("SDG.Unturned.BarricadeDrop", "ReceiveSalvageRequest", contextRef),
                ResolveU3Method("SDG.Unturned.StructureDrop", "ReceiveSalvageRequest", contextRef),
                ResolveU3Method("SDG.Unturned.ResourceManager", "ReceiveForageRequest", contextRef, typeof(byte), typeof(byte), typeof(ushort)),
                ResolveU3Method("SDG.Unturned.InteractableFarm", "ReceiveHarvestRequest", contextRef),
                ResolveU3Method("SDG.Unturned.InteractableDoor", "ReceiveToggleRequest", contextRef, typeof(bool)),
                ResolveU3Method("SDG.Unturned.ItemManager", "ReceiveTakeItemRequest", contextRef, typeof(byte), typeof(byte), typeof(uint), typeof(byte), typeof(byte), typeof(byte), typeof(byte)),
                ResolveU3Method("SDG.Unturned.VehicleManager", "ReceiveEnterVehicleRequest", contextRef, typeof(uint), typeof(byte[]), typeof(byte[]), typeof(byte))
            };
            var requiredOwner = new[]
            {
                ResolveU3Method("SDG.Unturned.PlayerInventory", "ReceiveDragItem", typeof(byte), typeof(byte), typeof(byte), typeof(byte), typeof(byte), typeof(byte), typeof(byte)),
                ResolveU3Method("SDG.Unturned.PlayerInventory", "ReceiveSwapItem", typeof(byte), typeof(byte), typeof(byte), typeof(byte), typeof(byte), typeof(byte), typeof(byte), typeof(byte)),
                ResolveU3Method("SDG.Unturned.PlayerInventory", "ReceiveDropItem", typeof(byte), typeof(byte), typeof(byte)),
                ResolveU3Method("SDG.Unturned.PlayerEquipment", "ReceiveEquipRequest", typeof(byte), typeof(byte), typeof(byte)),
                ResolveU3Method("SDG.Unturned.PlayerEquipment", "ReceiveToggleVisionRequest")
            };
            Type[] assemblyTypes;
            try { assemblyTypes = typeof(SDG.Unturned.Player).Assembly.GetTypes(); }
            catch (System.Reflection.ReflectionTypeLoadException ex) { assemblyTypes = ex.Types; }
            var typesToCheck = new HashSet<Type>(assemblyTypes);
            foreach (Type type in typesToCheck)
            {
                if (type == null) continue;
                foreach (System.Reflection.MethodInfo method in type.GetMethods(
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.DeclaredOnly))
                {
                    if (P2PQuarantineActionGatePatch.IsBlockedContextTarget(method)) discovered++;
                }
            }
            bool exactContextCoverage = Array.TrueForAll(requiredContext,
                method => method != null && P2PQuarantineActionGatePatch.IsBlockedContextTarget(method));
            bool storageMetadataCoverage = CecilHasSteamCallMethod(
                "SDG.Unturned.InteractableStorage", "ReceiveInteractRequest",
                (int)SDG.Unturned.ESteamCallValidation.SERVERSIDE,
                "SDG.Unturned.ServerInvocationContext&", "System.Boolean");
            bool exactOwnerCoverage = Array.TrueForAll(requiredOwner,
                method => method != null && P2PQuarantineActionGatePatch.IsBlockedOwnerTarget(method));
            System.Reflection.MethodInfo input = null;
            foreach (System.Reflection.MethodInfo candidate in typeof(SDG.Unturned.PlayerInput).GetMethods(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
            {
                if (candidate.Name != "ReceiveInputs") continue;
                System.Reflection.ParameterInfo[] parameters = candidate.GetParameters();
                if (parameters.Length > 0 && parameters[0].ParameterType ==
                    typeof(SDG.Unturned.ServerInvocationContext).MakeByRefType())
                {
                    input = candidate;
                    break;
                }
            }
            bool inputConsumed = !P2PQuarantineActionGatePatch.IsBlockedContextTarget(input) &&
                !P2PQuarantineActionGatePatch.IsBlockedOwnerTarget(input);
            bool coverage = discovered >= P2PQuarantineActionGatePatch.MinimumExpectedContextTargetCount &&
                exactContextCoverage && storageMetadataCoverage && exactOwnerCoverage && inputConsumed;
            if (!coverage) Console.WriteLine("    FAIL: generated gameplay RPC coverage incomplete");
            return actionBlocked && approvedAllowed && pendingAdminCommandBlocked &&
                   approvedAdminCommandAllowed && coverage;
        }

        private static System.Reflection.MethodInfo ResolveU3Method(string typeName, string methodName,
            params Type[] parameterTypes)
        {
            try
            {
                Type type = typeof(SDG.Unturned.Player).Assembly.GetType(typeName, false);
                return type?.GetMethod(methodName,
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance,
                    null, parameterTypes, null);
            }
            catch (TypeLoadException ex)
            {
                Console.WriteLine("    U3 metadata load failed: " + typeName + "." + methodName + " (" + ex.Message + ")");
                return null;
            }
        }

        private static bool CecilHasSteamCallMethod(string typeName, string methodName,
            int expectedValidation, params string[] parameterTypeNames)
        {
            using (Mono.Cecil.ModuleDefinition module = Mono.Cecil.ModuleDefinition.ReadModule(
                typeof(SDG.Unturned.Player).Assembly.Location,
                new Mono.Cecil.ReaderParameters { ReadingMode = Mono.Cecil.ReadingMode.Deferred }))
            {
                Mono.Cecil.TypeDefinition type = module.GetType(typeName);
                if (type == null) return false;
                foreach (Mono.Cecil.MethodDefinition method in type.Methods)
                {
                    if (method.Name != methodName || method.Parameters.Count != parameterTypeNames.Length) continue;
                    bool parametersMatch = true;
                    for (int index = 0; index < parameterTypeNames.Length; index++)
                        parametersMatch &= method.Parameters[index].ParameterType.FullName == parameterTypeNames[index];
                    if (!parametersMatch) continue;
                    foreach (Mono.Cecil.CustomAttribute attribute in method.CustomAttributes)
                    {
                        if (attribute.AttributeType.FullName == "SDG.Unturned.SteamCall" &&
                            attribute.ConstructorArguments.Count == 1 &&
                            Convert.ToInt32(attribute.ConstructorArguments[0].Value) == expectedValidation)
                            return true;
                    }
                }
                return false;
            }
        }
    }
}
