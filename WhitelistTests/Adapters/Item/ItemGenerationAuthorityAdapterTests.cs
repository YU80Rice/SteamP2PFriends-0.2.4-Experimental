using HarmonyLib;
using SDG.Unturned;
using SteamP2PFriends.MultiObserver;
using SteamP2PFriends.Patches;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace SteamP2PFriends.WhitelistTests
{
    internal static class ItemGenerationAuthorityAdapterTests
    {
        internal static bool Test_M1I01_LocalObserverPreservesVanillaWhenAdapterNotReady()
        {
            return ItemGenerationAuthorityAdapter.Evaluate(true, false, false, false);
        }

        internal static bool Test_M1I02_DedicatedRemotePreservesFullMapAuthority()
        {
            return !ItemGenerationAuthorityAdapter.Evaluate(false, true, true, true);
        }

        internal static bool Test_M1I03_ListenRemoteRequiresRegistration()
        {
            return !ItemGenerationAuthorityAdapter.Evaluate(false, false, false, true)
                && ItemGenerationAuthorityAdapter.Evaluate(false, false, true, true);
        }

        internal static bool Test_M1I04_UnrelatedRemoteCannotGenerate()
        {
            return !ItemGenerationAuthorityAdapter.Evaluate(false, false, true, false);
        }

        internal static bool Test_M1I05_AuthorizationIsNotPartOfWorldPresenceDecision()
        {
            // There is intentionally no approval argument: a created remote Player is a world
            // observer while pending, and must receive the same generated world snapshot.
            return ItemGenerationAuthorityAdapter.Evaluate(false, false, true, true);
        }

        internal static bool Test_M1I06_CurrentU3IlMatchesExactlyOneGenerationGate()
        {
            Type[] parameters =
            {
                typeof(Player),
                typeof(byte), typeof(byte), typeof(byte), typeof(byte), typeof(byte),
                typeof(bool).MakeByRefType()
            };
            MethodInfo original = AccessTools.Method(typeof(ItemManager), "onRegionUpdated", parameters);
            if (original == null) return false;

            List<CodeInstruction> source = PatchProcessor.GetCurrentInstructions(
                original,
                out _,
                maxTranspilers: 0);
            if (source == null || source.Count == 0) return false;

            foreach (CodeInstruction _ in ItemManagerRegionSyncPatch.OnRegionUpdated_Transpiler(source, null))
            {
            }

            return ItemManagerRegionSyncPatch.ReplacementCount == 1
                && ItemManagerRegionSyncPatch.GenerationGateReplacementCount == 1;
        }
    }
}
