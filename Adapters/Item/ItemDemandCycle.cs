using SDG.Unturned;
using SteamP2PFriends.Core.Identity;
using System;
using System.Collections.Generic;

namespace SteamP2PFriends.Adapters.Item
{
    public static class ItemLegacyCycleGate
    {
        public static bool SuppressFullMapWriter { get; set; }
        public static bool InOwnerStep { get; set; }
        public static bool ShouldSkipLegacy => SuppressFullMapWriter && !InOwnerStep;
    }

    public sealed class ItemDemandCycle
    {
        public const int RegionsPerStep = 1;

        private readonly INativeItemWarehouseBridge _bridge;
        private readonly ItemMetadataStore _store;
        private readonly Func<RegionKey, int> _demand;
        private readonly Func<float> _now;
        private readonly Func<byte> _worldSize;
        private readonly Func<RegionKey, bool> _activated;
        private readonly Func<RegionKey, bool> _busy;
        private readonly Func<RegionKey, bool> _isolated;
        private int _naturalCursor;
        private int _droppedCursor;
        private int _respawnCursor;

        public int NaturalSteps { get; private set; }
        public int NaturalRemovals { get; private set; }
        public int DroppedSteps { get; private set; }
        public int DroppedRemovals { get; private set; }

        public ItemDemandCycle(
            INativeItemWarehouseBridge bridge,
            ItemMetadataStore store,
            Func<RegionKey, int> demand,
            Func<float> now,
            Func<byte> worldSize,
            Func<RegionKey, bool> activated,
            Func<RegionKey, bool> busy = null,
            Func<RegionKey, bool> isolated = null)
        {
            _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _demand = demand ?? throw new ArgumentNullException(nameof(demand));
            _now = now ?? throw new ArgumentNullException(nameof(now));
            _worldSize = worldSize ?? throw new ArgumentNullException(nameof(worldSize));
            _activated = activated ?? throw new ArgumentNullException(nameof(activated));
            _busy = busy ?? (_ => false);
            _isolated = isolated ?? (_ => false);
        }

        public void Step(bool naturalAndRespawn = true, bool droppedExpiry = true)
        {
            ItemCycleThresholds thresholds = ItemCycleThresholds.Resolve();
            if (thresholds == null || !thresholds.NativeCyclePermitted) return;
            byte size = _worldSize();
            if (size == 0) return;
            if (droppedExpiry) StepDropped(thresholds, size);
            if (!naturalAndRespawn) return;
            StepNatural(thresholds, size);
            StepRespawn(thresholds, size);
        }

        private void StepNatural(ItemCycleThresholds thresholds, byte size)
        {
            int span = size * size;
            int examined = 0;
            int withItems = 0;
            float now = _now();
            while (examined < span && withItems < RegionsPerStep)
            {
                int index = _naturalCursor % span;
                _naturalCursor = index + 1;
                examined++;
                byte x = (byte)(index / size);
                byte y = (byte)(index % size);
                var region = new RegionKey(x, y);
                if (_demand(region) <= 0 || !_activated(region) || _busy(region)) continue;
                IReadOnlyList<ItemData> items = _bridge.GetRegionItems(x, y);
                if (items == null || items.Count == 0) continue;
                withItems++;
                NaturalSteps++;
                RemoveFirstExpiredNatural(x, y, items, now, thresholds.DespawnNaturalTime);
            }
        }

        private void RemoveFirstExpiredNatural(
            byte x, byte y, IReadOnlyList<ItemData> items, float now, float limit)
        {
            for (int i = 0; i < items.Count; i++)
            {
                ItemData item = items[i];
                if (item == null || item.instanceID == 0U) continue;
                Adopt(new RegionKey(x, y), item);
                if (!_store.TryGetMetadata(item.instanceID, out ItemMetadataRecord record)
                    || record.Origin != ItemOriginKind.Natural) continue;
                if (now - item.lastDropped <= limit) continue;
                if (!_bridge.TryRemoveItem(x, y, item.instanceID)) continue;
                if (StillPresent(_bridge.GetRegionItems(x, y), item.instanceID)) continue;
                _store.RemoveItem(item.instanceID);
                NaturalRemovals++;
                return;
            }
        }

        private void StepDropped(ItemCycleThresholds thresholds, byte size)
        {
            int span = size * size;
            int examined = 0;
            int withItems = 0;
            float now = _now();
            while (examined < span && withItems < RegionsPerStep)
            {
                int index = _droppedCursor % span;
                _droppedCursor = index + 1;
                examined++;
                byte x = (byte)(index / size);
                byte y = (byte)(index % size);
                IReadOnlyList<ItemData> items = _bridge.GetRegionItems(x, y);
                if (_isolated(new RegionKey(x, y)) || items == null || items.Count == 0) continue;
                withItems++;
                DroppedSteps++;
                AdoptAndRemoveFirstExpiredDrop(x, y, items, now, thresholds.DespawnDroppedTime);
            }
        }

        private void AdoptAndRemoveFirstExpiredDrop(
            byte x, byte y, IReadOnlyList<ItemData> items, float now, float limit)
        {
            var region = new RegionKey(x, y);
            for (int i = 0; i < items.Count; i++)
            {
                ItemData item = items[i];
                if (item == null || item.instanceID == 0U) continue;
                Adopt(region, item);
                if (!_store.TryGetMetadata(item.instanceID, out ItemMetadataRecord record)
                    || record.Origin != ItemOriginKind.Dropped) continue;
                if (now - record.CreationTime <= limit) continue;
                if (!_bridge.TryRemoveItem(x, y, item.instanceID)) continue;
                if (StillPresent(_bridge.GetRegionItems(x, y), item.instanceID)) continue;
                _store.RemoveItem(item.instanceID);
                DroppedRemovals++;
                return;
            }
        }

        private void StepRespawn(ItemCycleThresholds thresholds, byte size)
        {
            int span = size * size;
            int examined = 0;
            float now = _now();
            while (examined < span)
            {
                int index = _respawnCursor % span;
                _respawnCursor = index + 1;
                examined++;
                byte x = (byte)(index / size);
                byte y = (byte)(index % size);
                var region = new RegionKey(x, y);
                if (_demand(region) <= 0 || !_activated(region) || _busy(region)) continue;
                if (now - _bridge.ReadLastRespawn(x, y) <= thresholds.RespawnTime) continue;
                int points = _bridge.CountSpawnPoints(x, y);
                if (points <= 0) continue;
                IReadOnlyList<ItemData> items = _bridge.GetRegionItems(x, y);
                int current = items == null ? 0 : items.Count;
                int desired = (int)(points * thresholds.SpawnChance);
                if (current >= desired) continue;
                var before = Snapshot(items);
                _bridge.TryApplyNativeRespawn(x, y, now);
                if (!AdoptNewItems(x, y, before, now)) continue;
                if (!_bridge.CommitRespawnTime(x, y, now)) continue;
                if (Math.Abs(_bridge.ReadLastRespawn(x, y) - now) > 0.001f) continue;
                return;
            }
        }

        private bool AdoptNewItems(byte x, byte y, HashSet<uint> before, float now)
        {
            IReadOnlyList<ItemData> items = _bridge.GetRegionItems(x, y);
            if (items == null) return false;
            var region = new RegionKey(x, y);
            bool created = false;
            for (int i = 0; i < items.Count; i++)
            {
                ItemData item = items[i];
                if (item == null || item.instanceID == 0U || before.Contains(item.instanceID)) continue;
                if (ItemOriginTrackingSeam.IsTrackedDropped(item.instanceID))
                    _store.RegisterItem(item.instanceID, region, ItemOriginKind.Dropped, item.lastDropped, item.lastDropped);
                else
                {
                    ItemOriginTrackingSeam.NotifyNaturalItem(item.instanceID);
                    _store.RegisterItem(item.instanceID, region, ItemOriginKind.Natural, now, now);
                }
                created = true;
            }
            return created;
        }

        private static HashSet<uint> Snapshot(IReadOnlyList<ItemData> items)
        {
            var ids = new HashSet<uint>();
            if (items == null) return ids;
            for (int i = 0; i < items.Count; i++)
                if (items[i] != null && items[i].instanceID != 0U) ids.Add(items[i].instanceID);
            return ids;
        }

        private void Adopt(RegionKey region, ItemData item)
        {
            if (_store.TryGetMetadata(item.instanceID, out _)) return;
            if (ItemOriginTrackingSeam.IsTrackedDropped(item.instanceID))
            {
                _store.RegisterItem(item.instanceID, region, ItemOriginKind.Dropped, item.lastDropped, item.lastDropped);
                return;
            }
            if (ItemOriginTrackingSeam.IsTrackedNatural(item.instanceID))
            {
                _store.RegisterItem(item.instanceID, region, ItemOriginKind.Natural, item.lastDropped, 0f);
                return;
            }
            _store.RegisterItem(item.instanceID, region, ItemOriginKind.Unknown, item.lastDropped, item.lastDropped);
        }

        private static bool StillPresent(IReadOnlyList<ItemData> items, uint instanceId)
        {
            if (items == null) return false;
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i] != null && items[i].instanceID == instanceId) return true;
            }
            return false;
        }
    }
}
