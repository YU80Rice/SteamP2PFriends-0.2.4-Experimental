using HarmonyLib;
using SDG.Unturned;
using SteamP2PFriends.Adapters.Item;
using SteamP2PFriends.Adapters.Item.Patches;
using System;
using System.Collections.Generic;

namespace SteamP2PFriends.WhitelistTests
{
    internal static class ItemObserverReplicationStaticILTests
    {
        internal static bool Test_M2I09_CurrentU3IlUsesM2ReplicationGate()
        {
            Type[] parameters =
            {
                typeof(Player),
                typeof(byte), typeof(byte), typeof(byte), typeof(byte), typeof(byte),
                typeof(bool).MakeByRefType()
            };
            System.Reflection.MethodInfo original = AccessTools.Method(typeof(ItemManager), "onRegionUpdated", parameters);
            System.Reflection.MethodInfo expected = AccessTools.Method(
                typeof(ItemObserverReplicationAdapter),
                nameof(ItemObserverReplicationAdapter.ShouldReplicateForObserver),
                new Type[] { typeof(Player) });
            if (original == null || expected == null) return false;
            List<CodeInstruction> source = PatchProcessor.GetCurrentInstructions(
                original, out _, maxTranspilers: 0);
            int calls = 0;
            foreach (CodeInstruction instruction in
                ItemManagerRegionSyncPatch.OnRegionUpdated_Transpiler(source, null))
            {
                if (instruction.Calls(expected)) calls++;
            }
            return calls == 1 && ItemManagerRegionSyncPatch.ReplacementCount == 1;
        }
    }
}
