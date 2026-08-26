using SteamP2PFriends.Adapters.Item;
using SteamP2PFriends.Core.Patches;
using HarmonyLib;
using SDG.Unturned;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace SteamP2PFriends.WhitelistTests
{
    internal static class ItemObserverReplicationAdapterTests
    {
        private static readonly ItemReplicationRegion RegionA = new ItemReplicationRegion(10, 10);
        private static readonly ItemReplicationRegion RegionB = new ItemReplicationRegion(20, 20);
        private static readonly ItemBaselineGeneration Generation1 = new ItemBaselineGeneration(1, 1);
        private static readonly ItemBaselineGeneration Generation2 = new ItemBaselineGeneration(2, 2);

        internal static bool Test_M2I01_ReliableReturnCommitsExactlyOnce()
        {
            ItemObserverReplicationLedger ledger = Ready(1, 101, 10, 10);
            if (ledger.TryBegin(1, 101, RegionA, Generation1, out ItemBaselineToken token)
                != ItemBaselineBeginResult.Begin) return false;
            if (!ledger.Commit(token)) return false;
            return ledger.TryBegin(1, 101, RegionA, Generation1, out _)
                == ItemBaselineBeginResult.AlreadyCommitted;
        }

        internal static bool Test_M2I02_ExceptionAbortAllowsRetry()
        {
            ItemObserverReplicationLedger ledger = Ready(1, 101, 10, 10);
            if (ledger.TryBegin(1, 101, RegionA, Generation1, out ItemBaselineToken token)
                != ItemBaselineBeginResult.Begin) return false;
            if (!ledger.Abort(token)) return false;
            return ledger.TryBegin(1, 101, RegionA, Generation1, out _)
                == ItemBaselineBeginResult.Begin;
        }

        internal static bool Test_M2I03_RelevanceExitReentryResendsSameGeneration()
        {
            ItemObserverReplicationLedger ledger = Ready(1, 101, 10, 10);
            if (!Commit(ledger, 1, 101, RegionA, Generation1)) return false;
            ledger.UpdateRelevance(1, 101, 20, 20, 64, 1, out _);
            if (ledger.TryBegin(1, 101, RegionA, Generation1, out _)
                != ItemBaselineBeginResult.Rejected) return false;
            ledger.UpdateRelevance(1, 101, 10, 10, 64, 1, out _);
            return ledger.TryBegin(1, 101, RegionA, Generation1, out _)
                == ItemBaselineBeginResult.Begin;
        }

        internal static bool Test_M2I04_ReconnectInvalidatesOldBaselineAndToken()
        {
            ItemObserverReplicationLedger ledger = Ready(1, 101, 10, 10);
            if (ledger.TryBegin(1, 101, RegionA, Generation1, out ItemBaselineToken stale)
                != ItemBaselineBeginResult.Begin) return false;
            ledger.UpdateRelevance(1, 202, 10, 10, 64, 1, out bool changed);
            if (!changed || ledger.Commit(stale)) return false;
            return ledger.TryBegin(1, 202, RegionA, Generation1, out _)
                == ItemBaselineBeginResult.Begin;
        }

        internal static bool Test_M2I05_OverlappingObserversHaveIndependentBaselines()
        {
            ItemObserverReplicationLedger ledger = Ready(1, 101, 10, 10);
            ledger.UpdateRelevance(2, 202, 10, 10, 64, 1, out _);
            if (!Commit(ledger, 1, 101, RegionA, Generation1)) return false;
            return ledger.TryBegin(2, 202, RegionA, Generation1, out _)
                == ItemBaselineBeginResult.Begin;
        }

        internal static bool Test_M2I06_DifferentRegionsDoNotCrossCommit()
        {
            ItemObserverReplicationLedger ledger = Ready(1, 101, 10, 10);
            ledger.UpdateRelevance(2, 202, 20, 20, 64, 1, out _);
            if (!Commit(ledger, 1, 101, RegionA, Generation1)) return false;
            if (!Commit(ledger, 2, 202, RegionB, Generation1)) return false;
            return ledger.IsCommitted(1, 101, RegionA, Generation1)
                && !ledger.IsCommitted(1, 101, RegionB, Generation1)
                && ledger.IsCommitted(2, 202, RegionB, Generation1);
        }

        internal static bool Test_M2I07_NewWorldGenerationResendsWithoutRelevanceExit()
        {
            ItemObserverReplicationLedger ledger = Ready(1, 101, 10, 10);
            if (!Commit(ledger, 1, 101, RegionA, Generation1)) return false;
            return ledger.TryBegin(1, 101, RegionA, Generation2, out _)
                == ItemBaselineBeginResult.Begin;
        }

        internal static bool Test_M2I08_CapabilityIsExplicitReliableEnqueue()
        {
            return ItemObserverReplicationAdapter.Capability
                == ItemBaselineCapability.ReliableEnqueueBaseline;
        }

        internal static bool Test_M2I09_CurrentU3IlUsesM2ReplicationGate()
        {
            Type[] parameters =
            {
                typeof(Player),
                typeof(byte), typeof(byte), typeof(byte), typeof(byte), typeof(byte),
                typeof(bool).MakeByRefType()
            };
            MethodInfo original = AccessTools.Method(typeof(ItemManager), "onRegionUpdated", parameters);
            MethodInfo expected = AccessTools.Method(
                typeof(ItemObserverReplicationAdapter),
                nameof(ItemObserverReplicationAdapter.ShouldReplicateForObserver),
                new Type[] { typeof(Player) });
            if (original == null || expected == null) return false;

            List<CodeInstruction> source = PatchProcessor.GetCurrentInstructions(
                original,
                out _,
                maxTranspilers: 0);
            int calls = 0;
            foreach (CodeInstruction instruction in
                ItemManagerRegionSyncPatch.OnRegionUpdated_Transpiler(source, null))
            {
                if (instruction.Calls(expected)) calls++;
            }
            return calls == 1 && ItemManagerRegionSyncPatch.ReplacementCount == 1;
        }

        internal static bool Test_M2I10_LoadedProjectionRollbackRequiresCurrentConnection()
        {
            bool loaded = true;
            var state = new ItemObserverReplicationAdapter.BaselineCallState
            {
                Token = new ItemBaselineToken(1, 101, 1, RegionA, Generation1, true),
                Managed = true,
                LoadedProjectionWriter = value => loaded = value
            };

            if (state.TryRollbackLoadedProjection(202) || !loaded) return false;
            return state.TryRollbackLoadedProjection(101) && !loaded;
        }

        internal static bool Test_M2I11_ExactDisconnectInvalidatesEvenReusedConnectionToken()
        {
            ItemObserverReplicationLedger ledger = Ready(1, 101, 10, 10);
            if (!Commit(ledger, 1, 101, RegionA, Generation1)) return false;
            if (!ledger.RemoveObserver(1)) return false;
            ledger.UpdateRelevance(1, 101, 10, 10, 64, 1, out bool changed);
            return changed
                && ledger.TryBegin(1, 101, RegionA, Generation1, out _)
                    == ItemBaselineBeginResult.Begin;
        }

        private static ItemObserverReplicationLedger Ready(
            ulong observerId,
            ulong connectionToken,
            byte centerX,
            byte centerY)
        {
            ItemObserverReplicationLedger ledger =
                ItemObserverReplicationAdapter.CreateLedgerForTests();
            ledger.UpdateRelevance(observerId, connectionToken, centerX, centerY, 64, 1, out _);
            return ledger;
        }

        private static bool Commit(
            ItemObserverReplicationLedger ledger,
            ulong observerId,
            ulong connectionToken,
            ItemReplicationRegion region,
            ItemBaselineGeneration generation)
        {
            return ledger.TryBegin(observerId, connectionToken, region, generation, out ItemBaselineToken token)
                    == ItemBaselineBeginResult.Begin
                && ledger.Commit(token);
        }
    }
}
