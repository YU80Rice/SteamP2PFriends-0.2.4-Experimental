using SteamP2PFriends.MultiObserver;
using SteamP2PFriends.Core.Identity;
using System;
using System.Collections.Generic;

namespace SteamP2PFriends.WhitelistTests
{
    internal static class MultiObserverShadowTests
    {
        internal static bool Test_M01_PendingAuthorizationDoesNotRemoveWorldPresence()
        {
            var ledger = NewSession();
            ledger.Reconcile(new[] { Sample(10, 100, 5, 5, 2, false) }, 64, 1);
            if (ledger.ObserverCount != 1 || ledger.GetZombieDemand(BoundKey.FromNative(2)) != 1) return false;

            IReadOnlyList<ShadowTransition> transitions = ledger.Reconcile(
                new[] { Sample(10, 100, 5, 5, 2, true) }, 64, 1);

            return ledger.ObserverCount == 1
                && ledger.GetZombieDemand(BoundKey.FromNative(2)) == 1
                && Contains(transitions, ShadowTransitionKind.AuthorizationChanged);
        }

        internal static bool Test_M02_OverlappingDemandUsesReferenceCounts()
        {
            var ledger = NewSession();
            ledger.Reconcile(new[]
            {
                Sample(10, 100, 5, 5, 2),
                Sample(20, 200, 5, 5, 2)
            }, 64, 1);

            if (ledger.GetItemDemand(new RegionKey(5, 5)) != 2 || ledger.GetZombieDemand(BoundKey.FromNative(2)) != 2)
                return false;

            ledger.Reconcile(new[] { Sample(20, 200, 5, 5, 2) }, 64, 1);
            return ledger.GetItemDemand(new RegionKey(5, 5)) == 1
                && ledger.GetZombieDemand(BoundKey.FromNative(2)) == 1
                && ledger.ObserverCount == 1;
        }

        internal static bool Test_M03_ConnectionGenerationIsSessionMonotonic()
        {
            var ledger = NewSession();
            ledger.Reconcile(new[]
            {
                Sample(10, 100, 5, 5, 2),
                Sample(20, 200, 6, 6, 3)
            }, 64, 1);
            if (!ledger.TryGetObserver(10, out ObserverShadowSnapshot firstBefore)
                || !ledger.TryGetObserver(20, out ObserverShadowSnapshot secondBefore)) return false;
            uint firstGeneration = firstBefore.ConnectionGeneration;
            uint secondGeneration = secondBefore.ConnectionGeneration;

            ledger.Reconcile(new[]
            {
                Sample(10, 101, 5, 5, 2),
                Sample(20, 200, 6, 6, 3)
            }, 64, 1);

            return ledger.TryGetObserver(10, out ObserverShadowSnapshot firstAfter)
                && ledger.TryGetObserver(20, out ObserverShadowSnapshot secondAfter)
                && firstAfter.ConnectionGeneration > firstGeneration
                && firstAfter.ConnectionGeneration > secondGeneration
                && secondAfter.ConnectionGeneration == secondGeneration;
        }

        internal static bool Test_M04_ObserverDisconnectDoesNotAdvanceSessionEpoch()
        {
            var ledger = NewSession();
            ulong sessionEpoch = ledger.SessionEpoch;
            ledger.Reconcile(new[]
            {
                Sample(10, 100, 5, 5, 2),
                Sample(20, 200, 6, 6, 3)
            }, 64, 1);
            ledger.Reconcile(new[] { Sample(20, 200, 6, 6, 3) }, 64, 1);
            if (ledger.SessionEpoch != sessionEpoch || ledger.ObserverCount != 1) return false;

            if (!ledger.EndSession()) return false;
            return !ledger.IsSessionActive && ledger.SessionEpoch != sessionEpoch && ledger.ObserverCount == 0;
        }

        internal static bool Test_M05_EdgeRelevanceIsBounded()
        {
            var ledger = NewSession();
            ledger.Reconcile(new[] { Sample(10, 100, 0, 0, 2) }, 64, 1);
            if (!ledger.TryGetObserver(10, out ObserverShadowSnapshot state)) return false;
            return state.ItemRegionCount == 4
                && ledger.GetItemDemand(new RegionKey(0, 0)) == 1
                && ledger.GetItemDemand(new RegionKey(1, 1)) == 1
                && ledger.GetItemDemand(new RegionKey(2, 2)) == 0;
        }

        internal static bool Test_M06_DuplicateObserverIsIgnoredWithoutDoubleDemand()
        {
            var ledger = NewSession();
            IReadOnlyList<ShadowTransition> transitions = ledger.Reconcile(new[]
            {
                Sample(10, 100, 5, 5, 2),
                Sample(10, 999, 7, 7, 4)
            }, 64, 1);

            return ledger.ObserverCount == 1
                && ledger.GetZombieDemand(BoundKey.FromNative(2)) == 1
                && ledger.GetZombieDemand(BoundKey.FromNative(4)) == 0
                && Contains(transitions, ShadowTransitionKind.DuplicateObserverIgnored);
        }

        internal static bool Test_M07_MovementTransfersDemandAtomically()
        {
            var ledger = NewSession();
            ledger.Reconcile(new[] { Sample(10, 100, 5, 5, 2) }, 64, 1);
            ledger.Reconcile(new[] { Sample(10, 100, 10, 10, 4) }, 64, 1);

            return ledger.GetItemDemand(new RegionKey(5, 5)) == 0
                && ledger.GetItemDemand(new RegionKey(10, 10)) == 1
                && ledger.GetZombieDemand(BoundKey.FromNative(2)) == 0
                && ledger.GetZombieDemand(BoundKey.FromNative(4)) == 1;
        }

        internal static bool Test_M08_UniqueHostSessionReplacesSameMapWithoutIntermediateTick()
        {
            var ledger = new MultiObserverShadowLedger();
            if (!ledger.BeginSession("server|map|session-a")) return false;
            ulong firstEpoch = ledger.SessionEpoch;
            ledger.Reconcile(new[] { Sample(10, 100, 5, 5, 2) }, 64, 1);

            if (!ledger.BeginSession("server|map|session-b")) return false;
            return ledger.SessionEpoch != firstEpoch
                && ledger.ObserverCount == 0
                && ledger.ItemDemandRegionCount == 0
                && ledger.ZombieDemandBoundCount == 0;
        }

        internal static bool Test_M09_IncompleteCaptureCannotRemoveObserver()
        {
            var ledger = NewSession();
            ledger.Reconcile(new[]
            {
                Sample(10, 100, 5, 5, 2),
                Sample(20, 200, 6, 6, 3)
            }, 64, 1);

            ledger.Reconcile(
                new[] { Sample(20, 200, 6, 6, 3) },
                64,
                1,
                allowAbsenceRemoval: false);

            return ledger.ObserverCount == 2
                && ledger.GetZombieDemand(BoundKey.FromNative(2)) == 1
                && ledger.GetZombieDemand(BoundKey.FromNative(3)) == 1;
        }

        internal static bool Test_M10_LifecycleSequenceIsMonotonicAcrossRemoveAndReadd()
        {
            var ledger = NewSession();
            ledger.Reconcile(new[] { Sample(10, 100, 5, 5, 2) }, 64, 1);
            if (!ledger.TryGetObserver(10, out ObserverShadowSnapshot first)) return false;

            ledger.Reconcile(new[] { Sample(10, 101, 5, 5, 2) }, 64, 1);
            if (!ledger.TryGetObserver(10, out ObserverShadowSnapshot changed)
                || changed.LifecycleSequence <= first.LifecycleSequence) return false;

            ledger.Reconcile(Array.Empty<ObserverShadowSample>(), 64, 1);
            ledger.Reconcile(new[] { Sample(10, 102, 5, 5, 2) }, 64, 1);
            return ledger.TryGetObserver(10, out ObserverShadowSnapshot readded)
                && readded.LifecycleSequence > changed.LifecycleSequence
                && readded.ConnectionGeneration > changed.ConnectionGeneration;
        }

        internal static bool Test_M11_ShutdownGateAndFaultBackoffAreBounded()
        {
            var gate = new ShadowShutdownGate();
            gate.Request();
            gate.Request();
            if (!gate.IsRequested || !gate.IsLatched || !gate.Consume() || gate.Consume()) return false;
            if (!gate.IsLatched) return false;
            gate.PrepareForInitialize();
            if (gate.IsLatched || !gate.Consume()) return false;

            var backoff = new ShadowFaultBackoff();
            if (!backoff.RecordFailure(10f) || backoff.RecordFailure(10f)) return false;
            if (!backoff.IsFaulted || backoff.TryBeginRecovery(10.5f)) return false;
            if (!backoff.TryBeginRecovery(11f)) return false;
            if (!backoff.RecordFailure(12f) || backoff.Attempt != 2 || backoff.NextRecoveryAt < 14f)
                return false;
            backoff.MarkSuccess();
            return !backoff.IsFaulted && backoff.Attempt == 0;
        }

        internal static bool Test_M12_StaleLoadedCountIsPreserved()
        {
            var ledger = NewSession();
            ledger.Reconcile(new[] { Sample(10, 100, 5, 5, 2, true, 4) }, 64, 1);
            return ledger.TryGetObserver(10, out ObserverShadowSnapshot snapshot)
                && snapshot.NativeLoadedItemRegions == 9
                && snapshot.NativeStaleLoadedItemRegions == 4;
        }

        internal static bool Test_M13_ObserverCapacityFailsClosed()
        {
            var ledger = NewSession();
            var samples = new ObserverShadowSample[65];
            for (int index = 0; index < samples.Length; index++)
                samples[index] = Sample((ulong)index + 1UL, (ulong)index + 100UL, 5, 5, 2);
            try
            {
                ledger.Reconcile(samples, 64, 1);
                return false;
            }
            catch (InvalidOperationException)
            {
                return ledger.ObserverCount == 0;
            }
        }

        internal static bool Test_M14_InvalidZombieBoundDoesNotCreateFunctionalDemand()
        {
            var ledger = NewSession();
            ledger.Reconcile(new[] { Sample(10, 100, 5, 5, byte.MaxValue, true, 0, false) }, 64, 1);
            if (!ledger.TryGetObserver(10, out ObserverShadowSnapshot invalid)
                || invalid.HasFunctionalZombieBound
                || ledger.ObserverCount != 1
                || ledger.GetItemDemand(new RegionKey(5, 5)) != 1
                || ledger.GetZombieDemand(BoundKey.None) != 0
                || ledger.ZombieDemandBoundCount != 0)
            {
                return false;
            }

            ledger.Reconcile(new[] { Sample(10, 100, 5, 5, 2, true, 0, true) }, 64, 1);
            if (ledger.GetZombieDemand(BoundKey.FromNative(2)) != 1 || ledger.ZombieDemandBoundCount != 1) return false;

            ledger.Reconcile(new[] { Sample(10, 100, 5, 5, byte.MaxValue, true, 0, false) }, 64, 1);
            return ledger.TryGetObserver(10, out ObserverShadowSnapshot after)
                && !after.HasFunctionalZombieBound
                && ledger.ObserverCount == 1
                && ledger.GetItemDemand(new RegionKey(5, 5)) == 1
                && ledger.GetZombieDemand(BoundKey.FromNative(2)) == 0
                && ledger.ZombieDemandBoundCount == 0;
        }

        private static MultiObserverShadowLedger NewSession()
        {
            var ledger = new MultiObserverShadowLedger();
            if (!ledger.BeginSession("test-world")) throw new InvalidOperationException("session did not begin");
            return ledger;
        }

        private static ObserverShadowSample Sample(
            ulong id,
            ulong connection,
            byte x,
            byte y,
            byte bound,
            bool authorized = true,
            int staleLoaded = 0,
            bool hasFunctionalZombieBound = true)
        {
            return new ObserverShadowSample(
                id, connection, x, y,
                hasFunctionalZombieBound ? BoundKey.FromNative(bound) : BoundKey.None,
                hasFunctionalZombieBound, false, authorized, 9, staleLoaded, 9);
        }

        private static bool Contains(IReadOnlyList<ShadowTransition> transitions, ShadowTransitionKind kind)
        {
            foreach (ShadowTransition transition in transitions)
            {
                if (transition.Kind == kind) return true;
            }
            return false;
        }
    }
}
