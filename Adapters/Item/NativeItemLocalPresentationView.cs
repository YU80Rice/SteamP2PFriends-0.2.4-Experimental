using SDG.Unturned;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SteamP2PFriends.Adapters.Item
{
    public sealed class NativeItemLocalPresentationView : IItemLocalPresentationView
    {
        private readonly Player _player;

        public NativeItemLocalPresentationView(Player player = null)
        {
            _player = player;
        }

        public bool RegionReady(byte x, byte y)
        {
            ItemRegion[,] regions = ReadRegions();
            return regions != null && x < regions.GetLength(0) && y < regions.GetLength(1) && regions[x, y] != null;
        }

        public IReadOnlyList<ItemView> ReadCurrentItems(byte x, byte y)
        {
            var list = new List<ItemView>();
            ItemRegion region = Region(x, y);
            if (region == null) throw new System.InvalidOperationException("Item region missing for presentation snapshot.");
            if (region.items == null) throw new System.InvalidOperationException("Item region items list missing.");
            for (int i = 0; i < region.items.Count; i++)
            {
                ItemData data = region.items[i];
                if (data?.item == null) continue;
                list.Add(new ItemView
                {
                    InstanceId = data.instanceID,
                    AssetId = data.item.id,
                    Amount = data.item.amount,
                    Quality = data.item.quality,
                    State = data.item.state,
                    Point = data.point
                });
            }
            return list;
        }

        private IList _pending;
        public IList Pending => _pending ?? (_pending = new NativePendingList());

        public IList Models(byte x, byte y) => new NativeModelList(Region(x, y));

        public bool GetNetworked(byte x, byte y) => Region(x, y)?.isNetworked == true;

        public void SetNetworked(byte x, byte y, bool value)
        {
            ItemRegion region = Region(x, y);
            if (region != null) region.isNetworked = value;
        }

        public bool ProjectionAvailable(byte x, byte y)
        {
            LoadedRegion[,] loaded = _player?.movement?.loadedRegions;
            return loaded != null && x < loaded.GetLength(0) && y < loaded.GetLength(1) && loaded[x, y] != null;
        }

        public void SetLoaded(byte x, byte y, bool value)
        {
            LoadedRegion[,] loaded = _player?.movement?.loadedRegions;
            if (loaded == null || x >= loaded.GetLength(0) || y >= loaded.GetLength(1) || loaded[x, y] == null)
                throw new System.InvalidOperationException("Host loaded projection missing.");
            loaded[x, y].isItemsLoaded = value;
        }

        public float ItemSortOrder(ItemView item)
        {
            Vector3 camera = CameraPosition(out bool hasCamera);
            return hasCamera ? (camera - item.Point).sqrMagnitude : 0f;
        }

        public void DestroyUnity(object modelHandle)
        {
            GameObject gameObject = modelHandle as GameObject;
            if (gameObject == null) throw new System.InvalidOperationException("Item model GameObject missing.");
            UnityEngine.Object.Destroy(gameObject);
        }

        public void NotifyRemoved(ModelView model)
        {
            ItemDrop drop = model?.NativeDrop as ItemDrop;
            if (drop == null) throw new System.InvalidOperationException("Item drop association missing.");
            ItemManager.onItemDropRemoved?.Invoke(drop.model, drop.interactableItem);
        }

        public void PlayRemovalEffect(ModelView model)
        {
            ItemDrop drop = model?.NativeDrop as ItemDrop;
            if (drop?.interactableItem?.asset == null || drop.model == null) return;
            var play = typeof(ItemManager).GetMethod("PlayInventoryAudio",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            play?.Invoke(null, new object[] { drop.interactableItem.asset, drop.model.position });
        }

        public void FinishDeferredDestroy(byte x, byte y)
        {
            ItemRegion region = Region(x, y);
            if (region == null) return;
            var flag = typeof(ItemRegion).GetField("isPendingDestroy",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (flag == null || !(bool)flag.GetValue(region)) return;
            flag.SetValue(region, false);
            var listField = typeof(ItemManager).GetField("regionsPendingDestroy",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            var list = listField?.GetValue(null) as List<ItemRegion>;
            list?.RemoveFast(region);
        }

        private static ItemRegion[,] ReadRegions()
        {
            try { return ItemManager.regions; }
            catch (System.Exception ex)
            {
                throw new System.InvalidOperationException("ItemManager.regions unavailable.", ex);
            }
        }

        private static ItemRegion Region(byte x, byte y)
        {
            ItemRegion[,] regions = ReadRegions();
            if (regions == null || x >= regions.GetLength(0) || y >= regions.GetLength(1)) return null;
            return regions[x, y];
        }

        private static Vector3 CameraPosition(out bool hasCamera)
        {
            try
            {
                UnityEngine.Camera camera = MainCamera.instance;
                if (camera != null && camera.transform != null)
                {
                    hasCamera = true;
                    return camera.transform.position;
                }
            }
            catch { }
            hasCamera = false;
            return Vector3.zero;
        }
    }
}
