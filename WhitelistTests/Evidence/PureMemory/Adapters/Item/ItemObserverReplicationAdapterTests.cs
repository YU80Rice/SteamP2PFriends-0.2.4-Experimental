using SteamP2PFriends.Adapters.Item;
using System;
using System.Collections.Generic;

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

        internal static bool Test_IT146_LateDisconnectKeepsReconnectedBinding()
        {
            const ulong observer = 146UL;
            ItemObserverReplicationAdapter.ResetForSession();
            object oldOwner = new object();
            object currentOwner = new object();
            object oldTransport = new object();
            object currentTransport = new object();
            ItemObserverReplicationAdapter.EnsureConnectionToken(observer, oldOwner, oldTransport);
            ulong current = ItemObserverReplicationAdapter.EnsureConnectionToken(observer, currentOwner, currentTransport);
            bool staleRemoved = ItemObserverReplicationAdapter.RemoveObserver(observer, oldOwner, oldTransport);
            ulong afterLateDisconnect = ItemObserverReplicationAdapter.EnsureConnectionToken(observer, currentOwner, currentTransport);
            bool currentRemoved = ItemObserverReplicationAdapter.RemoveObserver(observer, currentOwner, currentTransport);
            ItemObserverReplicationAdapter.ResetForSession();
            return current != 0UL && !staleRemoved && afterLateDisconnect == current && currentRemoved;
        }

        internal static bool Test_IT147_DisconnectRequiresExactCoreOwnerAndTransport()
        {
            object oldOwner = new object(), currentOwner = new object();
            object oldTransport = new object(), currentTransport = new object();
            var identity = new SteamP2PFriends.MultiObserver.MultiObserverShadowCoordinator.ConnectionIdentity
            {
                SteamPlayerReference = currentOwner,
                TransportReference = currentTransport,
                Token = 147UL
            };
            bool oldOwnerMatched = SteamP2PFriends.MultiObserver.MultiObserverShadowCoordinator
                .TryGetDisconnectedConnectionToken(identity, oldOwner, oldTransport, out _);
            bool mixedTransportMatched = SteamP2PFriends.MultiObserver.MultiObserverShadowCoordinator
                .TryGetDisconnectedConnectionToken(identity, currentOwner, oldTransport, out _);
            bool currentMatched = SteamP2PFriends.MultiObserver.MultiObserverShadowCoordinator
                .TryGetDisconnectedConnectionToken(identity, currentOwner, currentTransport, out ulong token);
            return !oldOwnerMatched && !mixedTransportMatched && currentMatched && token == 147UL;
        }

        internal static bool Test_IT149_ProductionDisconnectKeepsReconnectedOwner()
        {
            const ulong observer = 149UL;
            object oldOwner = new object(), currentOwner = new object();
            object oldTransport = new object(), currentTransport = new object();
            var identitiesField = typeof(SteamP2PFriends.MultiObserver.MultiObserverShadowCoordinator)
                .GetField("Connections", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            var identities = (Dictionary<ulong,
                SteamP2PFriends.MultiObserver.MultiObserverShadowCoordinator.ConnectionIdentity>)identitiesField.GetValue(null);
            identities.Clear();
            identities[observer] = new SteamP2PFriends.MultiObserver.MultiObserverShadowCoordinator.ConnectionIdentity
            {
                SteamPlayerReference = currentOwner, TransportReference = currentTransport, Token = 149UL
            };
            ItemObserverReplicationAdapter.ResetForSession();
            ItemObserverReplicationAdapter.EnsureConnectionToken(observer, currentOwner, currentTransport);
            bool staleResult = SteamP2PFriends.MultiObserver.MultiObserverShadowCoordinator
                .OnItemObserverDisconnected(observer, oldOwner, oldTransport);
            bool staleKept = identities.ContainsKey(observer)
                && ItemObserverReplicationAdapter.EnsureConnectionToken(observer, currentOwner, currentTransport) != 0UL;
            bool currentResult = SteamP2PFriends.MultiObserver.MultiObserverShadowCoordinator
                .OnItemObserverDisconnected(observer, currentOwner, currentTransport);
            bool currentRemoved = !identities.ContainsKey(observer);
            bool repeatedResult = SteamP2PFriends.MultiObserver.MultiObserverShadowCoordinator
                .OnItemObserverDisconnected(observer, currentOwner, currentTransport);
            identities.Clear();
            ItemObserverReplicationAdapter.ResetForSession();
            return !staleResult && staleKept && currentResult && currentRemoved && !repeatedResult;
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
