using System;

namespace SteamP2PFriends.Adapters.Item
{
    public readonly struct ItemDeletionRequest
    {
        public ItemDeletionRequest(byte x, byte y, uint instanceId, bool networked)
        {
            X = x;
            Y = y;
            InstanceId = instanceId;
            Networked = networked;
        }

        public byte X { get; }
        public byte Y { get; }
        public uint InstanceId { get; }
        public bool Networked { get; }
    }

    public sealed class ItemDeletionDispatch
    {
        public bool Remove(
            ItemDeletionRequest request,
            Func<bool> authorizeIncrement,
            Action cancelPending,
            Func<bool> removeAuthoritative,
            Func<bool> broadcastDestroy)
        {
            if (authorizeIncrement == null) throw new ArgumentNullException(nameof(authorizeIncrement));
            if (cancelPending == null) throw new ArgumentNullException(nameof(cancelPending));
            if (removeAuthoritative == null) throw new ArgumentNullException(nameof(removeAuthoritative));
            if (broadcastDestroy == null) throw new ArgumentNullException(nameof(broadcastDestroy));
            if (request.Networked) authorizeIncrement();
            if (!removeAuthoritative()) return false;
            cancelPending();
            return broadcastDestroy();
        }
    }
}
