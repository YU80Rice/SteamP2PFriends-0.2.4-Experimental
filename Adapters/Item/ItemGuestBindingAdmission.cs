using System;

namespace SteamP2PFriends.Adapters.Item
{
    internal enum ItemGuestAdmission : byte
    {
        Accepted = 0,
        Waiting = 1,
        Rejected = 2
    }

    internal readonly struct ItemGuestBindingRequest
    {
        internal ItemGuestBindingRequest(ulong epoch, ulong observer, ulong token, byte x, byte y)
        { Epoch = epoch; Observer = observer; Token = token; X = x; Y = y; }
        internal ulong Epoch { get; }
        internal ulong Observer { get; }
        internal ulong Token { get; }
        internal byte X { get; }
        internal byte Y { get; }
    }

    internal readonly struct ItemCommittedBinding
    {
        internal ItemCommittedBinding(bool sessionActive, bool found, ulong epoch,
            ulong token, object owner, object transport)
        {
            SessionActive = sessionActive; Found = found; Epoch = epoch; Token = token;
            Owner = owner; Transport = transport;
        }
        internal bool SessionActive { get; }
        internal bool Found { get; }
        internal ulong Epoch { get; }
        internal ulong Token { get; }
        internal object Owner { get; }
        internal object Transport { get; }
    }

    internal sealed class ItemGuestBindingAdmissionException : InvalidOperationException
    {
        internal ItemGuestBindingAdmissionException(ItemGuestAdmission admission, string reason)
            : base("Guest baseline admission " + admission + ": " + reason)
        { Admission = admission; Reason = reason; }
        internal ItemGuestAdmission Admission { get; }
        internal string Reason { get; }
    }

    internal static class ItemGuestBindingAdmission
    {
        internal static ItemGuestAdmission Evaluate(ItemGuestBindingRequest request,
            object actualOwner, object actualTransport, ItemCommittedBinding committed)
        {
            if (request.Epoch == 0UL || request.Observer == 0UL || request.Token == 0UL
                || !committed.SessionActive || committed.Epoch == 0UL
                || request.Epoch != committed.Epoch)
                return ItemGuestAdmission.Rejected;
            if (committed.Found && committed.Token != request.Token)
                return ItemGuestAdmission.Rejected;
            if (committed.Found && actualOwner != null && committed.Owner != null
                && !object.ReferenceEquals(actualOwner, committed.Owner))
                return ItemGuestAdmission.Rejected;
            if (committed.Found && actualTransport != null && committed.Transport != null
                && !object.ReferenceEquals(actualTransport, committed.Transport))
                return ItemGuestAdmission.Rejected;
            if (!committed.Found || committed.Token == 0UL || committed.Owner == null
                || committed.Transport == null || actualOwner == null || actualTransport == null)
                return ItemGuestAdmission.Waiting;
            return object.ReferenceEquals(actualOwner, committed.Owner)
                && object.ReferenceEquals(actualTransport, committed.Transport)
                ? ItemGuestAdmission.Accepted
                : ItemGuestAdmission.Rejected;
        }
    }
}
