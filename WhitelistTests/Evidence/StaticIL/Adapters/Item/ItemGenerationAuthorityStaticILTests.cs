using HarmonyLib;
using SDG.Unturned;
using SteamP2PFriends.Adapters.Item.Patches;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace SteamP2PFriends.WhitelistTests
{
    internal static class ItemGenerationAuthorityStaticILTests
    {
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
                original, out _, maxTranspilers: 0);
            if (source == null || source.Count == 0) return false;
            foreach (CodeInstruction _ in ItemManagerRegionSyncPatch.OnRegionUpdated_Transpiler(source, null)) { }
            return ItemManagerRegionSyncPatch.ReplacementCount == 1 &&
                ItemManagerRegionSyncPatch.GenerationGateReplacementCount == 1;
        }
    }
}
