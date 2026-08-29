using SteamP2PFriends.Adapters.Resource.Patches;
using HarmonyLib;
using SDG.Unturned;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace SteamP2PFriends.WhitelistTests
{
    internal static class ResourceHarvestRegistrationStaticILContractTests
    {
        internal static bool Test_All()
        {
            Type patchType = typeof(ResourceManagerHarvestReplicationPatch);
            return patchType.GetNestedType("IHarvestPatchBackend", BindingFlags.NonPublic) != null
                && patchType.GetNestedType("HookRegistrationState", BindingFlags.NonPublic) != null
                && patchType.GetMethod("RegisterManualForTesting", BindingFlags.Static | BindingFlags.NonPublic) != null
                && patchType.GetProperty("HookStates", BindingFlags.Static | BindingFlags.NonPublic) != null
                && patchType.GetMethod("RegisterManual", BindingFlags.Static | BindingFlags.Public) != null;
        }

        internal static bool Test_FourHookRegistrationStateIsExplicit()
        {
            string[] expectedProperties =
            {
                "ServerSetResourceDeadPostfixRegistered",
                "ServerSetResourceAlivePostfixRegistered",
                "ReceiveResourceDeadPostfixRegistered",
                "ReceiveResourceAlivePostfixRegistered"
            };

            foreach (string name in expectedProperties)
            {
                if (typeof(ResourceManagerHarvestReplicationPatch).GetProperty(
                    name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic) == null)
                    return false;
            }

            return true;
        }

        internal static bool Test_RollbackResidualKnowledgeIsExplicit()
        {
            return typeof(ResourceManagerHarvestReplicationPatch.HookRegistrationState)
                .GetProperty("RollbackResidualKnown", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) != null;
        }

        internal static bool Test_BlackBoxRegistrationStateContainsIdentity()
        {
            var harmony = new Harmony(SteamP2PFriendsPlugin.HARMONY_ID);
            try
            {
                bool registered = ResourceManagerHarvestReplicationPatch.RegisterManual(harmony);
                if (!registered || !ResourceManagerHarvestReplicationPatch.AllHookRegistrationsSucceeded)
                    return false;

                return HasPatch(typeof(ResourceManager), "ServerSetResourceDead",
                           "ServerSetResourceDead_Postfix")
                    && HasPatch(typeof(ResourceManager), "ServerSetResourceAlive",
                        "ServerSetResourceAlive_Postfix")
                    && HasPatch(typeof(ResourceManager), "ReceiveResourceDead",
                        "ReceiveResourceDead_Postfix")
                    && HasPatch(typeof(ResourceManager), "ReceiveResourceAlive",
                        "ReceiveResourceAlive_Postfix");
            }
            finally
            {
                Unpatch(typeof(ResourceManager), "ServerSetResourceDead", "ServerSetResourceDead_Postfix", harmony);
                Unpatch(typeof(ResourceManager), "ServerSetResourceAlive", "ServerSetResourceAlive_Postfix", harmony);
                Unpatch(typeof(ResourceManager), "ReceiveResourceDead", "ReceiveResourceDead_Postfix", harmony);
                Unpatch(typeof(ResourceManager), "ReceiveResourceAlive", "ReceiveResourceAlive_Postfix", harmony);
            }
        }

        internal static bool Test_BlackBoxPartialFailureRollsBackAppliedAndFailedHook()
        {
            var backend = new FailureInjectingBackend(failPatchNumber: 2, throwOnRollbackVerification: true);
            bool registered = ResourceManagerHarvestReplicationPatch.RegisterManualForTesting(backend);
            return !registered
                && !ResourceManagerHarvestReplicationPatch.RegistrationSucceeded
                && !ResourceManagerHarvestReplicationPatch.AllHookRegistrationsSucceeded
                && ResourceManagerHarvestReplicationPatch.HookStates != null
                && ResourceManagerHarvestReplicationPatch.HookStates.Count == 4
                && AllHooksFailClosed();
        }

        internal static bool Test_BlackBoxRegistrationIsIdempotentAndUnique()
        {
            var harmony = new Harmony(SteamP2PFriendsPlugin.HARMONY_ID);
            try
            {
                if (!ResourceManagerHarvestReplicationPatch.RegisterManual(harmony)) return false;
                if (!ResourceManagerHarvestReplicationPatch.RegisterManual(harmony)) return false;

                return CountOwnedMatchingPatches(typeof(ResourceManager), "ServerSetResourceDead",
                           "ServerSetResourceDead_Postfix") == 1
                    && CountOwnedMatchingPatches(typeof(ResourceManager), "ServerSetResourceAlive",
                        "ServerSetResourceAlive_Postfix") == 1
                    && CountOwnedMatchingPatches(typeof(ResourceManager), "ReceiveResourceDead",
                        "ReceiveResourceDead_Postfix") == 1
                    && CountOwnedMatchingPatches(typeof(ResourceManager), "ReceiveResourceAlive",
                        "ReceiveResourceAlive_Postfix") == 1;
            }
            catch
            {
                return false;
            }
            finally
            {
                Unpatch(typeof(ResourceManager), "ServerSetResourceDead", "ServerSetResourceDead_Postfix", harmony);
                Unpatch(typeof(ResourceManager), "ServerSetResourceAlive", "ServerSetResourceAlive_Postfix", harmony);
                Unpatch(typeof(ResourceManager), "ReceiveResourceDead", "ReceiveResourceDead_Postfix", harmony);
                Unpatch(typeof(ResourceManager), "ReceiveResourceAlive", "ReceiveResourceAlive_Postfix", harmony);
            }
        }

        private static bool AllHooksFailClosed()
        {
            foreach (ResourceManagerHarvestReplicationPatch.HookRegistrationState state
                in ResourceManagerHarvestReplicationPatch.HookStates)
            {
                if (state.Registered || state.RollbackResidual) return false;
                if (state.RegistrationAttempted && state.RollbackStatus == "not-run") return false;
                if (state.RegistrationAttempted && state.RollbackVerificationCompleted
                    && !state.RollbackResidualKnown) return false;
            }
            return true;
        }

        internal static bool Test_UnpatchFailureIsNotReportedAsVerifiedClean()
        {
            var backend = new FailureInjectingBackend(
                failPatchNumber: 2,
                throwOnRollbackVerification: false,
                throwOnUnpatch: true);
            bool registered = ResourceManagerHarvestReplicationPatch.RegisterManualForTesting(backend);
            if (registered || ResourceManagerHarvestReplicationPatch.RegistrationSucceeded) return false;

            bool sawResidual = false;
            foreach (ResourceManagerHarvestReplicationPatch.HookRegistrationState state
                in ResourceManagerHarvestReplicationPatch.HookStates)
            {
                if (!state.RegistrationAttempted) continue;
                if (state.RollbackStatus.IndexOf("unpatch-failed", StringComparison.Ordinal) < 0)
                    return false;
                if (!state.RollbackResidualKnown || !state.RollbackVerificationCompleted)
                    return false;
                if (state.RollbackResidual) sawResidual = true;
            }
            return sawResidual;
        }

        private sealed class FailureInjectingBackend : ResourceManagerHarvestReplicationPatch.IHarvestPatchBackend
        {
            private readonly int _failPatchNumber;
            private readonly bool _throwOnRollbackVerification;
            private readonly bool _throwOnUnpatch;
            private readonly HashSet<MethodInfo> _patched = new HashSet<MethodInfo>();
            private int _patchCount;
            private int _unpatchCount;

            internal FailureInjectingBackend(
                int failPatchNumber,
                bool throwOnRollbackVerification,
                bool throwOnUnpatch = false)
            {
                _failPatchNumber = failPatchNumber;
                _throwOnRollbackVerification = throwOnRollbackVerification;
                _throwOnUnpatch = throwOnUnpatch;
            }

            public void Patch(MethodInfo original, MethodInfo patchMethod)
            {
                _patchCount++;
                if (_patchCount == _failPatchNumber)
                    throw new InvalidOperationException("injected patch failure");
                _patched.Add(patchMethod);
            }

            public void Unpatch(MethodInfo original, MethodInfo patchMethod)
            {
                _unpatchCount++;
                if (_throwOnUnpatch) throw new InvalidOperationException("injected unpatch failure");
                _patched.Remove(patchMethod);
            }

            public bool HasPatch(MethodInfo original, MethodInfo patchMethod, out string summary)
            {
                if (_throwOnRollbackVerification && _unpatchCount > 0)
                    throw new InvalidOperationException("injected rollback verification failure");
                bool present = _patched.Contains(patchMethod);
                summary = "fake-present=" + present;
                return present;
            }
        }

        private static bool HasPatch(Type targetType, string targetName, string patchName)
        {
            MethodInfo target = AccessTools.Method(targetType, targetName);
            MethodInfo patch = AccessTools.Method(typeof(ResourceManagerHarvestReplicationPatch), patchName);
            HarmonyLib.Patches info = Harmony.GetPatchInfo(target);
            if (info?.Postfixes == null) return false;
            foreach (HarmonyLib.Patch item in info.Postfixes)
            {
                if (item.owner == SteamP2PFriendsPlugin.HARMONY_ID && item.PatchMethod == patch)
                    return true;
            }
            return false;
        }

        private static int CountOwnedMatchingPatches(Type targetType, string targetName, string patchName)
        {
            MethodInfo target = AccessTools.Method(targetType, targetName);
            MethodInfo patch = AccessTools.Method(typeof(ResourceManagerHarvestReplicationPatch), patchName);
            HarmonyLib.Patches info = Harmony.GetPatchInfo(target);
            if (info?.Postfixes == null) return 0;

            int count = 0;
            foreach (HarmonyLib.Patch item in info.Postfixes)
            {
                if (item.owner == SteamP2PFriendsPlugin.HARMONY_ID && item.PatchMethod == patch) count++;
            }
            return count;
        }

        private static void Unpatch(Type targetType, string targetName, string patchName, Harmony harmony)
        {
            MethodInfo target = AccessTools.Method(targetType, targetName);
            MethodInfo patch = AccessTools.Method(typeof(ResourceManagerHarvestReplicationPatch), patchName);
            if (target != null && patch != null) harmony.Unpatch(target, patch);
        }

        private static bool ContainsCall(MethodInfo[] methods, string name)
        {
            foreach (MethodInfo method in methods)
            {
                if (CallsNamed(method, name)) return true;
            }

            return false;
        }

        private static bool CallsNamed(MethodInfo method, string name)
        {
            byte[] il = method.GetMethodBody()?.GetILAsByteArray();
            if (il == null) return false;

            for (int offset = 0; offset + 4 < il.Length; offset++)
            {
                int token = BitConverter.ToInt32(il, offset);
                try
                {
                    MethodBase called = method.Module.ResolveMethod(token);
                    if (called != null && string.Equals(called.Name, name, StringComparison.Ordinal)) return true;
                }
                catch { }
            }

            return false;
        }
    }
}
