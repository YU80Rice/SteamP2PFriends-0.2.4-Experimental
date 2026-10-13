using SteamP2PFriends.Adapters.Item.Patches;
using SteamP2PFriends.Adapters.Item;
using SteamP2PFriends.Core.Identity;
using SteamP2PFriends.MultiObserver.Lifecycle;
using System;
using SDG.Unturned;

namespace SteamP2PFriends.WhitelistTests
{
    internal static class ItemRecoveryCommandTests
    {
        internal static bool Test_IT63_RecoveryCommandParsesAndFailsClosed()
        {
            bool parsed = ItemRecoveryCommandPatch.TryParse(
                "/spfitemretry 7 9", out RegionKey region);
            bool exactRegion = parsed && region == new RegionKey(7, 9);
            bool rejectsMalformed = !ItemRecoveryCommandPatch.TryParse(
                "/spfitemretry 7", out _)
                && !ItemRecoveryCommandPatch.TryParse("/spfitemretry 7 999", out _)
                && !ItemRecoveryCommandPatch.TryParse("/spfitemretryx 7 9", out _)
                && !ItemRecoveryCommandPatch.TryParse("/other 7 9", out _);
            bool approvedGuestOnly = ItemRecoveryCommandPatch.CanHandle(
                p2pHost: true, server: true, localHost: false,
                pending: false, approved: true);
            bool rejectsUnapproved = !ItemRecoveryCommandPatch.CanHandle(
                p2pHost: true, server: true, localHost: false,
                pending: true, approved: false);
            bool localHostAllowed = ItemRecoveryCommandPatch.CanHandle(
                p2pHost: true, server: true, localHost: true,
                pending: false, approved: true);
            return exactRegion && rejectsMalformed && approvedGuestOnly
                && rejectsUnapproved && localHostAllowed;
        }

        internal static bool Test_IT64_ApprovedRecoveryCommandPassesRemoteGate()
        {
            bool recoveryAllowed = !Security.Patches.P2PListenHostCommandPermissionPatch.ShouldBlock(
                true, true, false, false, false, false, "/spfitemretry 7 9");
            bool otherCommandBlocked = Security.Patches.P2PListenHostCommandPermissionPatch.ShouldBlock(
                true, true, false, false, false, false, "/vanilla-command");
            return recoveryAllowed && otherCommandBlocked;
        }

        internal static bool Test_IT65_HostSessionCommandSubscriptionIsEpochBound()
        {
            var source = new FakeItemRecoveryCommandEventSource();
            ItemRecoveryCommandSessionContext context =
                ItemRecoveryCommandSessionContext.Valid("session-a", 7UL);
            _acceptedCount = 0;
            var subscription = new ItemRecoveryCommandSubscription(
                source, () => context, CountAccepted);

            subscription.Register("session-a");
            source.InvokeActive("/spfitemretry 7 9");
            subscription.Register("session-a");
            bool firstSessionOnce = source.SubscribeCount == 1 && _acceptedCount == 1;

            subscription.Unregister();
            context = ItemRecoveryCommandSessionContext.Invalid("session-a", 7UL);
            source.InvokeHistory(0, "/spfitemretry 7 9");
            bool endedDoesNotRecover = _acceptedCount == 1;

            context = ItemRecoveryCommandSessionContext.Valid("session-b", 8UL);
            subscription.Register("session-b");
            source.InvokeHistory(0, "/spfitemretry 7 9");
            source.InvokeActive("/spfitemretry 7 9");
            bool staleAndCurrentAreIsolated = _acceptedCount == 2;

            context = new ItemRecoveryCommandSessionContext(
                "session-b", 9UL, 8UL, true, true, true, true);
            source.InvokeActive("/spfitemretry 7 9");
            bool epochMismatchDoesNotRecover = _acceptedCount == 2;
            context = ItemRecoveryCommandSessionContext.Invalid("session-b", 8UL);
            source.InvokeActive("/spfitemretry 7 9");
            bool identityFailureDoesNotRecover = _acceptedCount == 2;
            subscription.Unregister();
            return firstSessionOnce && endedDoesNotRecover
                && staleAndCurrentAreIsolated && epochMismatchDoesNotRecover
                && identityFailureDoesNotRecover
                && source.SubscribeCount == 2 && source.UnsubscribeCount == 2;
        }

        internal static bool Test_IT66_ManualRateLimitCannotFallThrough()
        {
            bool rateLimitedStopsSettlement = !ItemProductionControlSeam
                .ShouldTrySettlementManualRecovery(EAcquireRecoveryRequestResult.RateLimited);
            bool staleStopsSettlement = !ItemProductionControlSeam
                .ShouldTrySettlementManualRecovery(EAcquireRecoveryRequestResult.StaleConnection);
            bool independentSettlementMayStart = ItemProductionControlSeam
                .ShouldTrySettlementManualRecovery(EAcquireRecoveryRequestResult.NotEligible);
            return rateLimitedStopsSettlement && staleStopsSettlement
                && independentSettlementMayStart;
        }

        internal static bool Test_IT68_ManualRecoveryNormalizesCurrentConnectionToken()
        {
            bool valid = ItemReturnSettlement.TryValidateManualRecoveryToken(
                201UL, token => token == 201UL || token == 501UL, out ulong normalized);
            bool oldRejected = !ItemReturnSettlement.TryValidateManualRecoveryToken(
                199UL, token => token == 201UL || token == 501UL, out _);
            return valid && normalized == 201UL && oldRejected;
        }

        internal static bool Test_IT69_HalfPacketGateIsObserverAndRegionScoped()
        {
            var gate = new ItemObserverRecoveryGate();
            var region = new RegionKey(7, 9);
            gate.Block(1001UL, 201UL, region);
            bool affectedBlocked = gate.IsBlocked(1001UL, 201UL, region);
            bool otherObserverOpen = !gate.IsBlocked(1002UL, 202UL, region);
            bool neighborOpen = !gate.IsBlocked(1001UL, 201UL, new RegionKey(7, 10));
            gate.ReleaseRegion(1001UL, 201UL, new RegionKey(7, 10));
            bool adjacentExitDoesNotReopen = gate.IsBlocked(1001UL, 201UL, region);
            gate.ReleaseObserver(1001UL, 201UL);
            bool fullExitReopens = !gate.IsBlocked(1001UL, 201UL, region);
            gate.Block(1001UL, 201UL, region);
            int blockedProjection = 0;
            int normalProjection = 0;
            var admission = new ItemStepFiveAdmission(
                _ => { }, (_, __) => { },
                (request, itemRegion) => gate.IsBlocked(
                    request.ObserverId, request.ConnectionToken, itemRegion),
                (_, __) => false);
            admission.Admit(new ItemStepFiveRequest(1001UL, 201UL, region, 0, 16, false),
                new ItemStepFiveLoadedProjection(_ => blockedProjection++));
            admission.Admit(new ItemStepFiveRequest(1002UL, 202UL, region, 0, 16, false),
                new ItemStepFiveLoadedProjection(_ => normalProjection++));
            return affectedBlocked && otherObserverOpen && neighborOpen
                && adjacentExitDoesNotReopen && fullExitReopens
                && blockedProjection == 1 && normalProjection == 0;
        }

        internal static bool Test_IT70_InvalidRecoveryCommandFailsClosed()
        {
            var source = new FakeItemRecoveryCommandEventSource();
            ItemRecoveryCommandSessionContext context =
                ItemRecoveryCommandSessionContext.Invalid("session-a", 7UL);
            int handlerCalls = 0;
            var subscription = new ItemRecoveryCommandSubscription(
                source, () => context,
                (SteamPlayer player, string text, ref bool execute, ref bool list) => handlerCalls++);
            subscription.Register("session-a");
            source.InvokeActive("/spfitemretry 7 9");
            bool targetHidden = !source.LastShouldExecute && !source.LastShouldList
                && handlerCalls == 0;
            source.InvokeActive("/ordinary-chat");
            bool ordinaryPreserved = source.LastShouldExecute && source.LastShouldList
                && handlerCalls == 0;
            subscription.Unregister();
            return targetHidden && ordinaryPreserved;
        }

        internal static bool Test_IT71_RecoveryCallbackExceptionFailsClosed()
        {
            var source = new FakeItemRecoveryCommandEventSource();
            _acceptedCount = 0;
            var subscription = new ItemRecoveryCommandSubscription(
                source, ThrowSessionContext, CountAccepted);
            subscription.Register("session-a");
            source.InvokeActive("/spfitemretry 7 9");
            subscription.Unregister();
            return !source.LastShouldExecute && !source.LastShouldList
                && _acceptedCount == 0;
        }

        internal static bool Test_IT72_UnregisteredRecoveryCommandIsBlocked()
        {
            bool blockedWhenUnregistered = Security.Patches.P2PListenHostCommandPermissionPatch.ShouldBlock(
                true, true, false, false, false, false, "/spfitemretry 7 9", false);
            bool allowedWhenRegistered = !Security.Patches.P2PListenHostCommandPermissionPatch.ShouldBlock(
                true, true, false, false, false, false, "/spfitemretry 7 9", true);
            bool localAlsoBlocked = Security.Patches.P2PListenHostCommandPermissionPatch.ShouldBlock(
                true, true, true, false, false, false, "/spfitemretry 7 9", false);
            return blockedWhenUnregistered && allowedWhenRegistered && localAlsoBlocked;
        }

        internal static bool Test_IT73_IdentityInvalidationDropsRecoveryChain()
        {
            var recovery = new ItemFailureRecovery();
            var region = new RegionKey(7, 9);
            for (int i = 0; i < 4; i++) recovery.RecordFailure(region, i, out _);
            recovery.SetWaiters(region, new[] { new ItemFailureRecovery.Waiter(1001UL, 201UL) });
            if (!recovery.IsExhausted(region)) return false;
            recovery.Invalidate(region);
            return !recovery.IsIsolated(region)
                && !recovery.TryGetWaiters(region, out _);
        }

        internal static bool Test_IT74_DisconnectReleaseUsesExactToken()
        {
            return !ItemReturnSettlement.IsReleaseTokenMatch(501UL, 201UL)
                && ItemReturnSettlement.IsReleaseTokenMatch(501UL, 501UL);
        }

        internal static bool Test_IT75_CoreGenerationIsSoleCandidateIdentity()
        {
            return ItemProductionControlSeam.IsCoreConnectionGenerationCurrent(201UL, 201UL)
                && !ItemProductionControlSeam.IsCoreConnectionGenerationCurrent(201UL, 501UL);
        }

        internal static bool Test_IT76_RegionReleaseKeepsOtherRecoveryWaiter()
        {
            var recovery = new ItemFailureRecovery();
            var first = new RegionKey(7, 9);
            var second = new RegionKey(7, 10);
            for (int i = 0; i < 4; i++)
            {
                recovery.RecordFailure(first, i, out _);
                recovery.RecordFailure(second, i, out _);
            }
            var waiter = new[] { new ItemFailureRecovery.Waiter(1001UL, 201UL) };
            recovery.SetWaiters(first, waiter);
            recovery.SetWaiters(second, waiter);
            recovery.RemoveWaiter(first, 1001UL, 201UL);
            return !recovery.TryGetWaiters(first, out _)
                && recovery.TryGetWaiters(second, out _);
        }

        internal static bool Test_IT77_UnknownRegionItemsStayUnconfirmed()
        {
            bool unknown = !ItemReturnSettlement.TryCountConfirmedItems(null, out _);
            bool empty = ItemReturnSettlement.TryCountConfirmedItems(
                new ItemData[0], out int count) && count == 0;
            return unknown && empty;
        }

        private static ItemRecoveryCommandSessionContext ThrowSessionContext() =>
            throw new InvalidOperationException("test context failure");

        private static int _acceptedCount;

        private static void CountAccepted(
            SteamPlayer player, string text, ref bool shouldExecute, ref bool shouldList)
        {
            _acceptedCount++;
        }
    }

    internal sealed class FakeItemRecoveryCommandEventSource : IItemRecoveryCommandEventSource
    {
        private readonly System.Collections.Generic.List<ItemRecoveryPermissionHandler> _active =
            new System.Collections.Generic.List<ItemRecoveryPermissionHandler>();
        private readonly System.Collections.Generic.List<ItemRecoveryPermissionHandler> _history =
            new System.Collections.Generic.List<ItemRecoveryPermissionHandler>();

        internal int SubscribeCount { get; private set; }
        internal int UnsubscribeCount { get; private set; }
        internal bool LastShouldExecute { get; private set; }
        internal bool LastShouldList { get; private set; }

        public void Subscribe(ItemRecoveryPermissionHandler handler)
        {
            SubscribeCount++;
            _active.Add(handler);
            _history.Add(handler);
        }

        public void Unsubscribe(ItemRecoveryPermissionHandler handler)
        {
            UnsubscribeCount++;
            _active.Remove(handler);
        }

        internal void InvokeActive(string text)
        {
            foreach (ItemRecoveryPermissionHandler handler in _active.ToArray())
                Invoke(handler, text);
        }

        internal void InvokeHistory(int index, string text) => Invoke(_history[index], text);

        private void Invoke(ItemRecoveryPermissionHandler handler, string text)
        {
            bool shouldExecute = true;
            bool shouldList = true;
            handler(null, text, ref shouldExecute, ref shouldList);
            LastShouldExecute = shouldExecute;
            LastShouldList = shouldList;
        }
    }
}
