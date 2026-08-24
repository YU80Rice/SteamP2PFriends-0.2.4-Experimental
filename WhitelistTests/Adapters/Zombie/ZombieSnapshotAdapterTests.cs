using SteamP2PFriends.MultiObserver;
using System;

namespace SteamP2PFriends.WhitelistTests
{
    internal static class ZombieSnapshotAdapterTests
    {
        internal static bool Test_M4Z01_InitialSnapshotEnqueued()
        {
            ZombieSnapshotReplicationLedger ledger = Ready(1, 101, 7, 1);
            if (ledger.TryBegin(1, 101, 7, 1, 1, out ZombieSnapshotToken token)
                != ZombieSnapshotBeginResult.Begin) return false;
            if (!token.Valid || token.SnapshotSequence == 0) return false;
            if (!ledger.Commit(token)) return false;
            return ledger.TryBegin(1, 101, 7, 1, 1, out _)
                == ZombieSnapshotBeginResult.AlreadyCommitted;
        }

        internal static bool Test_M4Z02_StaleGenerationForcesResync()
        {
            ZombieSnapshotReplicationLedger ledger = Ready(1, 101, 7, 1);
            if (!Commit(ledger, 1, 101, 7, 1, 1)) return false;
            // Region reconstructed -> Generation bumped from 1 to 2
            return ledger.TryBegin(1, 101, 7, 1, 2, out ZombieSnapshotToken token)
                == ZombieSnapshotBeginResult.Begin
                && token.RegionGeneration == 2;
        }

        internal static bool Test_M4Z03_ExceptionAbortAllowsRetry()
        {
            ZombieSnapshotReplicationLedger ledger = Ready(1, 101, 7, 1);
            if (ledger.TryBegin(1, 101, 7, 1, 1, out ZombieSnapshotToken token)
                != ZombieSnapshotBeginResult.Begin) return false;
            if (!ledger.Abort(token)) return false;
            return ledger.TryBegin(1, 101, 7, 1, 1, out _)
                == ZombieSnapshotBeginResult.Begin;
        }

        internal static bool Test_M4Z04_RelevanceExitAndReentry()
        {
            ZombieSnapshotReplicationLedger ledger = Ready(1, 101, 7, 1);
            if (!Commit(ledger, 1, 101, 7, 1, 1)) return false;
            // Observer moves to bound 10
            ledger.UpdateRelevance(1, 101, 10, 1, out _);
            if (ledger.TryBegin(1, 101, 7, 1, 1, out _)
                != ZombieSnapshotBeginResult.Rejected) return false;
            // Observer returns to bound 7
            ledger.UpdateRelevance(1, 101, 7, 1, out _);
            return ledger.TryBegin(1, 101, 7, 1, 1, out _)
                == ZombieSnapshotBeginResult.AlreadyCommitted;
        }

        internal static bool Test_M4Z05_ReconnectInvalidatesOldSnapshotToken()
        {
            ZombieSnapshotReplicationLedger ledger = Ready(1, 101, 7, 1);
            if (ledger.TryBegin(1, 101, 7, 1, 1, out ZombieSnapshotToken stale)
                != ZombieSnapshotBeginResult.Begin) return false;
            // Reconnect -> connection token 101 -> 202
            ledger.UpdateRelevance(1, 202, 7, 1, out bool changed);
            if (!changed || ledger.Commit(stale)) return false;
            return ledger.TryBegin(1, 202, 7, 1, 1, out _)
                == ZombieSnapshotBeginResult.Begin;
        }

        internal static bool Test_M4Z06_OverlappingObserversHaveIndependentSnapshots()
        {
            ZombieSnapshotReplicationLedger ledger = Ready(1, 101, 7, 1);
            ledger.UpdateRelevance(2, 202, 7, 1, out _);
            if (!Commit(ledger, 1, 101, 7, 1, 1)) return false;
            return ledger.TryBegin(2, 202, 7, 1, 1, out ZombieSnapshotToken token2)
                == ZombieSnapshotBeginResult.Begin
                && token2.ObserverId == 2;
        }

        internal static bool Test_M4Z07_DifferentBoundsDoNotCrossCommit()
        {
            ZombieSnapshotReplicationLedger ledger = Ready(1, 101, 7, 1);
            ledger.UpdateRelevance(2, 202, 10, 1, out _);
            if (!Commit(ledger, 1, 101, 7, 1, 1)) return false;
            if (!Commit(ledger, 2, 202, 10, 1, 1)) return false;
            return ledger.IsCommitted(1, 101, 7, 1, 1)
                && !ledger.IsCommitted(1, 101, 10, 1, 1)
                && ledger.IsCommitted(2, 202, 10, 1, 1);
        }

        internal static bool Test_M4Z08_ExplicitCapabilityIsReliableEnqueue()
        {
            return ZombieSnapshotAdapter.Capability
                == ZombieSnapshotCapability.ReliableEnqueueBaseline;
        }

        internal static bool Test_M4Z09_DeltaSequenceMonotonic()
        {
            ZombieSnapshotReplicationLedger ledger = Ready(1, 101, 7, 1);
            uint s1 = ledger.NextDeltaSequence(1, 7, 1);
            uint s2 = ledger.NextDeltaSequence(1, 7, 1);
            uint s3 = ledger.NextDeltaSequence(1, 7, 1);
            return s1 == 1 && s2 == 2 && s3 == 3;
        }

        internal static bool Test_M4Z10_ExactDisconnectCleansObserver()
        {
            ZombieSnapshotReplicationLedger ledger = Ready(1, 101, 7, 1);
            if (!Commit(ledger, 1, 101, 7, 1, 1)) return false;
            if (!ledger.RemoveObserver(1)) return false;
            ledger.UpdateRelevance(1, 101, 7, 1, out bool changed);
            return changed
                && ledger.TryBegin(1, 101, 7, 1, 1, out _)
                    == ZombieSnapshotBeginResult.Begin;
        }

        private static ZombieSnapshotReplicationLedger Ready(
            ulong observerId,
            ulong connectionToken,
            byte bound,
            uint sessionEpoch)
        {
            ZombieSnapshotReplicationLedger ledger = ZombieSnapshotAdapter.CreateLedgerForTests();
            ledger.UpdateRelevance(observerId, connectionToken, bound, sessionEpoch, out _);
            return ledger;
        }

        private static bool Commit(
            ZombieSnapshotReplicationLedger ledger,
            ulong observerId,
            ulong connectionToken,
            byte bound,
            uint sessionEpoch,
            uint regionGeneration)
        {
            return ledger.TryBegin(observerId, connectionToken, bound, sessionEpoch, regionGeneration, out ZombieSnapshotToken token)
                    == ZombieSnapshotBeginResult.Begin
                && ledger.Commit(token);
        }
    }
}
