using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SteamP2PFriends.Adapters.Item
{
    public enum ItemStepFiveHandoff : byte
    {
        RunOriginal = 0,
        Handled = 1,
        Rejected = 3
    }

    public sealed class ItemView
    {
        public uint InstanceId;
        public ushort AssetId;
        public byte Amount;
        public byte Quality;
        public byte[] State;
        public Vector3 Point;
    }

    public sealed class PendingView
    {
        public byte RegionX;
        public byte RegionY;
        public uint InstanceId;
        public ushort AssetId;
        public byte Amount;
        public byte Quality;
        public byte[] State;
        public Vector3 Point;
        public float SortOrder;
        public bool ShouldPlayEffect;
    }

    public sealed class ModelView
    {
        public uint InstanceId;
        public object Handle;
        public object NativeDrop;
    }

    public interface IItemLocalPresentationView
    {
        bool RegionReady(byte x, byte y);
        IReadOnlyList<ItemView> ReadCurrentItems(byte x, byte y);
        IList Pending { get; }
        IList Models(byte x, byte y);
        bool GetNetworked(byte x, byte y);
        void SetNetworked(byte x, byte y, bool value);
        bool ProjectionAvailable(byte x, byte y);
        void SetLoaded(byte x, byte y, bool value);
        float ItemSortOrder(ItemView item);
        void DestroyUnity(object modelHandle);
        void NotifyRemoved(ModelView model);
        void PlayRemovalEffect(ModelView model);
        void FinishDeferredDestroy(byte x, byte y);
    }
}
