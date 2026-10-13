using SDG.Unturned;
using SteamP2PFriends.Core.Identity;
using SteamP2PFriends.Host;
using SteamP2PFriends.MultiObserver;
using SteamP2PFriends.Security;
using System;

namespace SteamP2PFriends.Adapters.Item.Patches
{
    internal delegate void ItemRecoveryPermissionHandler(
        SteamPlayer player, string text, ref bool shouldExecute, ref bool shouldList);

    internal interface IItemRecoveryCommandEventSource
    {
        void Subscribe(ItemRecoveryPermissionHandler handler);
        void Unsubscribe(ItemRecoveryPermissionHandler handler);
    }

    internal readonly struct ItemRecoveryCommandSessionContext
    {
        internal ItemRecoveryCommandSessionContext(
            string sessionId, ulong sessionEpoch, ulong itemSessionEpoch,
            bool listenHostActive, bool sessionActive, bool identityReady, bool candidateMode)
        {
            CurrentSessionId = sessionId ?? string.Empty;
            SessionEpoch = sessionEpoch;
            ItemSessionEpoch = itemSessionEpoch;
            ListenHostActive = listenHostActive;
            SessionActive = sessionActive;
            IdentityReady = identityReady;
            CandidateMode = candidateMode;
        }

        internal string CurrentSessionId { get; }
        internal ulong SessionEpoch { get; }
        internal ulong ItemSessionEpoch { get; }
        internal bool ListenHostActive { get; }
        internal bool SessionActive { get; }
        internal bool IdentityReady { get; }
        internal bool CandidateMode { get; }

        internal static ItemRecoveryCommandSessionContext Valid(string id, ulong epoch) =>
            new ItemRecoveryCommandSessionContext(id, epoch, epoch, true, true, true, true);

        internal static ItemRecoveryCommandSessionContext Invalid(string id, ulong epoch) =>
            new ItemRecoveryCommandSessionContext(id, epoch, epoch, false, false, false, false);

        internal bool IsValidFor(string registeredSessionId)
        {
            return ListenHostActive && SessionActive && IdentityReady && CandidateMode
                && !string.IsNullOrEmpty(registeredSessionId)
                && string.Equals(CurrentSessionId, registeredSessionId, StringComparison.Ordinal)
                && SessionEpoch != 0UL
                && SessionEpoch == ItemSessionEpoch;
        }
    }

    internal sealed class ItemRecoveryCommandSubscription
    {
        private readonly IItemRecoveryCommandEventSource _source;
        private readonly Func<ItemRecoveryCommandSessionContext> _context;
        private readonly ItemRecoveryPermissionHandler _handlerAction;
        private ItemRecoveryPermissionHandler _registeredHandler;
        private string _registeredSessionId;

        internal ItemRecoveryCommandSubscription(
            IItemRecoveryCommandEventSource source,
            Func<ItemRecoveryCommandSessionContext> context,
            ItemRecoveryPermissionHandler handlerAction)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _handlerAction = handlerAction ?? throw new ArgumentNullException(nameof(handlerAction));
        }

        internal void Register(string sessionId)
        {
            if (_registeredHandler != null) return;
            _registeredSessionId = sessionId ?? string.Empty;
            _registeredHandler = CreateHandler(_registeredSessionId);
            _source.Subscribe(_registeredHandler);
        }

        internal bool IsRegistered => _registeredHandler != null;

        internal void Unregister()
        {
            if (_registeredHandler == null) return;
            _source.Unsubscribe(_registeredHandler);
            _registeredHandler = null;
            _registeredSessionId = null;
        }

        private ItemRecoveryPermissionHandler CreateHandler(string sessionId)
        {
            return (SteamPlayer player, string text, ref bool shouldExecute, ref bool shouldList) =>
                Handle(sessionId, player, text, ref shouldExecute, ref shouldList);
        }

        private void Handle(
            string sessionId, SteamPlayer player, string text,
            ref bool shouldExecute, ref bool shouldList)
        {
            try
            {
                if (!_context().IsValidFor(sessionId))
                {
                    if (ItemRecoveryCommandPatch.IsRecoveryCommandText(text))
                    {
                        shouldExecute = false;
                        shouldList = false;
                    }
                    return;
                }
                _handlerAction(player, text, ref shouldExecute, ref shouldList);
            }
            catch (Exception)
            {
                if (ItemRecoveryCommandPatch.IsRecoveryCommandText(text))
                {
                    shouldExecute = false;
                    shouldList = false;
                }
            }
        }
    }

    internal sealed class NativeItemRecoveryCommandEventSource : IItemRecoveryCommandEventSource
    {
        private ItemRecoveryPermissionHandler _handler;

        public void Subscribe(ItemRecoveryPermissionHandler handler)
        {
            _handler = handler;
            ChatManager.onCheckPermissions -= Forward;
            ChatManager.onCheckPermissions += Forward;
        }

        public void Unsubscribe(ItemRecoveryPermissionHandler handler)
        {
            ChatManager.onCheckPermissions -= Forward;
            if (ReferenceEquals(_handler, handler)) _handler = null;
        }

        private void Forward(SteamPlayer player, string text, ref bool shouldExecute, ref bool shouldList)
        {
            _handler?.Invoke(player, text, ref shouldExecute, ref shouldList);
        }
    }

    internal static class ItemRecoveryCommandPatch
    {
        internal const string Command = "/spfitemretry";
        internal static bool RegistrationValid { get; private set; }
        internal static Action<ulong, string> ChatSink { get; set; }
        private static readonly NativeItemRecoveryCommandEventSource EventSource =
            new NativeItemRecoveryCommandEventSource();
        private static readonly ItemRecoveryCommandSubscription Subscription =
            new ItemRecoveryCommandSubscription(
                EventSource,
                MultiObserverShadowCoordinator.GetItemRecoveryCommandSessionContext,
                OnCheckPermissions);

        internal static void RegisterManual(string sessionId)
        {
            if (RegistrationValid) return;
            Subscription.Register(sessionId);
            RegistrationValid = Subscription.IsRegistered;
        }

        internal static void UnregisterManual()
        {
            Subscription.Unregister();
            RegistrationValid = false;
        }

        internal static bool TryParse(string text, out RegionKey region)
        {
            region = default(RegionKey);
            if (string.IsNullOrWhiteSpace(text)) return false;
            string[] parts = text.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3 || !string.Equals(parts[0], Command, StringComparison.OrdinalIgnoreCase))
                return false;
            if (!byte.TryParse(parts[1], out byte x) || !byte.TryParse(parts[2], out byte y)) return false;
            region = new RegionKey(x, y);
            return true;
        }

        internal static bool IsRecoveryCommandText(string text)
        {
            string normalized = text?.TrimStart();
            return !string.IsNullOrEmpty(normalized)
                && (normalized.Equals(Command, StringComparison.OrdinalIgnoreCase)
                    || normalized.StartsWith(Command + " ", StringComparison.OrdinalIgnoreCase));
        }

    internal static bool CanHandle(
            bool p2pHost, bool server, bool localHost, bool pending, bool approved) =>
            p2pHost && server && !pending && approved;

        internal static void NotifyRecoveryExhausted(
            ulong observerId, ulong token, RegionKey region, bool possibleHalfPacket = false)
        {
            string message = "物资区域 (" + region.X + "," + region.Y + ") 暂不可用。";
            message += possibleHalfPacket
                ? "检测到可能的半包。请离开失败区域的完整ITEM_REGIONS加载覆盖范围后再返回；只跨当前脚下的区域边界可能不足。不要在区内盲目重发。"
                : "请确认仍在该区域时使用 " + Command + " " + region.X + " " + region.Y + " 重试。";
            Send(observerId, message);
        }

        internal static void NotifyRecoveryUnconfirmed(ulong observerId, ulong token, RegionKey region)
        {
            Send(observerId, "物资区域 (" + region.X + "," + region.Y
                + ") 的原生结果暂无法确认，区域仍不可用。请离开该区域的完整ITEM_REGIONS加载覆盖范围后再返回；返回只会重新核验，不保证恢复。");
        }

        private static void OnCheckPermissions(
            SteamPlayer player, string text, ref bool shouldExecute, ref bool shouldList)
        {
            try { OnCheckPermissionsCore(player, text, ref shouldExecute, ref shouldList); }
            catch (Exception)
            {
                shouldExecute = false;
                shouldList = false;
            }
        }

        private static void OnCheckPermissionsCore(
            SteamPlayer player, string text, ref bool shouldExecute, ref bool shouldList)
        {
            if (!IsRecoveryCommandText(text)) return;
            shouldExecute = false;
            shouldList = false;
            if (player == null || player.playerID == null) return;
            ulong observerId = player.playerID.steamID.m_SteamID;
            bool local = observerId == Provider.user.m_SteamID;
            bool approved = P2PApprovalManager.IsApproved(player.playerID.steamID);
            if (!CanHandle(HostManager.IsP2PHostMode, Provider.isServer, local,
                P2PApprovalManager.IsPending(player.playerID.steamID), approved)) return;
            if (!TryParse(text, out RegionKey region))
            {
                Send(observerId, "用法：" + Command + " <区域X> <区域Y>。");
                return;
            }
            ulong token = MultiObserverShadowCoordinator.GetItemConnectionGeneration(observerId);
            bool accepted = token != 0UL
                && MultiObserverShadowCoordinator.TryManualItemRecovery(observerId, token, region);
            Send(observerId, accepted ? "已接受该区域的有限恢复请求。" :
                "该区域当前没有可用的恢复资格，或请求仍在冷却中。");
        }

        private static void Send(ulong observerId, string text)
        {
            try
            {
                if (ChatSink != null) { ChatSink(observerId, text); return; }
                for (int i = 0; i < Provider.clients.Count; i++)
                {
                    SteamPlayer player = Provider.clients[i];
                    if (player?.playerID?.steamID.m_SteamID != observerId) continue;
                    ChatManager.serverSendMessage(text, Palette.SERVER, toPlayer: player,
                        mode: EChatMode.WELCOME, useRichTextFormatting: false);
                    return;
                }
            }
            catch (Exception) { }
        }
    }
}
