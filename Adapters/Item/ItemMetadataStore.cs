using SteamP2PFriends.Core.Identity;
using System;
using System.Collections.Generic;

namespace SteamP2PFriends.Adapters.Item
{
    public sealed class ItemMetadataRecord
    {
        public uint InstanceId { get; }
        public RegionKey RegionKey { get; }
        public ItemOriginKind Origin { get; }
        public float CreationTime { get; }
        public float TimingBenchmark { get; set; }

        public ItemMetadataRecord(
            uint instanceId,
            RegionKey regionKey,
            ItemOriginKind origin,
            float creationTime,
            float timingBenchmark)
        {
            InstanceId = instanceId;
            RegionKey = regionKey;
            Origin = origin;
            CreationTime = creationTime;
            TimingBenchmark = timingBenchmark;
        }

        public bool IsExpired(float currentTime, float despawnNaturalTime, float despawnDroppedTime)
        {
            if (Origin == ItemOriginKind.Natural)
            {
                return (currentTime - TimingBenchmark) > despawnNaturalTime;
            }
            if (Origin == ItemOriginKind.Dropped)
            {
                return (currentTime - CreationTime) > despawnDroppedTime;
            }
            return false;
        }
    }

    public sealed class ItemMetadataStore
    {
        private readonly Dictionary<uint, ItemMetadataRecord> _records =
            new Dictionary<uint, ItemMetadataRecord>();
        private readonly Dictionary<RegionKey, List<uint>> _regionIndex =
            new Dictionary<RegionKey, List<uint>>();

        public int Count => _records.Count;

        public void Clear()
        {
            _records.Clear();
            _regionIndex.Clear();
        }

        public void RegisterItem(
            uint instanceId,
            RegionKey regionKey,
            ItemOriginKind origin,
            float creationTime,
            float timingBenchmark = 0f)
        {
            if (_records.TryGetValue(instanceId, out ItemMetadataRecord oldRecord))
            {
                if (!oldRecord.RegionKey.Equals(regionKey)
                    && _regionIndex.TryGetValue(oldRecord.RegionKey, out List<uint> oldList))
                {
                    oldList.Remove(instanceId);
                }
            }

            float initialBenchmark = origin == ItemOriginKind.Natural ? timingBenchmark : creationTime;
            var record = new ItemMetadataRecord(
                instanceId,
                regionKey,
                origin,
                creationTime,
                initialBenchmark);
            _records[instanceId] = record;

            if (!_regionIndex.TryGetValue(regionKey, out List<uint> list))
            {
                list = new List<uint>();
                _regionIndex[regionKey] = list;
            }
            if (!list.Contains(instanceId))
            {
                list.Add(instanceId);
            }
        }

        public bool RemoveItem(uint instanceId)
        {
            if (_records.TryGetValue(instanceId, out ItemMetadataRecord record))
            {
                _records.Remove(instanceId);
                if (_regionIndex.TryGetValue(record.RegionKey, out List<uint> list))
                {
                    list.Remove(instanceId);
                }
                return true;
            }
            return false;
        }

        public bool TryGetMetadata(uint instanceId, out ItemMetadataRecord record) =>
            _records.TryGetValue(instanceId, out record);

        public void ActivateRegionNaturalTiming(RegionKey regionKey, float activationTime)
        {
            if (!_regionIndex.TryGetValue(regionKey, out List<uint> list)) return;
            for (int i = 0; i < list.Count; i++)
            {
                uint id = list[i];
                if (_records.TryGetValue(id, out ItemMetadataRecord rec))
                {
                    if (rec.Origin == ItemOriginKind.Natural)
                    {
                        rec.TimingBenchmark = activationTime;
                    }
                }
            }
        }

        public IReadOnlyList<uint> GetItemIdsInRegion(RegionKey regionKey)
        {
            return _regionIndex.TryGetValue(regionKey, out List<uint> list)
                ? (IReadOnlyList<uint>)list
                : Array.Empty<uint>();
        }
    }
}
