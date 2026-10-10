using SDG.Unturned;
using SteamP2PFriends.Core.Identity;
using System;
using System.Collections.Generic;

namespace SteamP2PFriends.Adapters.Item
{
    public sealed class ItemReturnSettlement
    {
        public const int ActionsPerStep = 1;

        private readonly INativeItemWarehouseBridge _bridge;
        private readonly ItemMetadataStore _store;
        private readonly Func<RegionKey, int> _demand;
        private readonly Func<float> _now;
        private readonly Func<byte> _worldSize;
        private readonly Func<RegionKey, bool> _activated;
        private readonly Func<RegionKey, uint> _generation;
        private readonly Func<ulong> _session;
        private readonly HashSet<RegionKey> _departed = new HashSet<RegionKey>();
        private readonly HashSet<RegionKey> _isolated = new HashSet<RegionKey>();
        private readonly Dictionary<RegionKey, Job> _jobs = new Dictionary<RegionKey, Job>();
        private readonly List<RegionKey> _order = new List<RegionKey>();
        private int _cursor;

        public ItemReturnSettlement(
            INativeItemWarehouseBridge bridge,
            ItemMetadataStore store,
            Func<RegionKey, int> demand,
            Func<float> now,
            Func<byte> worldSize,
            Func<RegionKey, bool> activated,
            Func<RegionKey, uint> generation,
            Func<ulong> session)
        {
            _bridge = bridge;
            _store = store;
            _demand = demand;
            _now = now;
            _worldSize = worldSize;
            _activated = activated;
            _generation = generation;
            _session = session;
        }

        public bool IsBusy(RegionKey region) => _jobs.ContainsKey(region);
        public bool BlocksConsumption(RegionKey region) => IsBusy(region) || _isolated.Contains(region);
        public bool BlocksWarehouse(RegionKey region) => IsBusy(region) || _isolated.Contains(region);
        public bool IsIsolated(RegionKey region) => _isolated.Contains(region);
        public int ActiveJobCount => _jobs.Count;
        public Action<ulong, ulong, RegionKey, bool> Deliver { get; set; }
        public Func<ulong, ulong, bool> ConnectionCurrent { get; set; }

        public bool Hold(RegionKey region, ulong observer, ulong token, bool host)
        {
            if (_isolated.Contains(region)) return true;
            if (_activated(region) && _departed.Contains(region) && !_jobs.ContainsKey(region))
            {
                _jobs[region] = new Job { Session = _session(), Generation = _generation(region) };
                _order.Add(region);
            }
            if (!_jobs.TryGetValue(region, out Job job)) return false;
            for (int i = 0; i < job.Waiters.Count; i++)
            {
                if (job.Waiters[i].Observer != observer) continue;
                if (job.Waiters[i].Token == token) return true;
                job.Waiters.RemoveAt(i);
                break;
            }
            job.Waiters.Add(new Waiter { Observer = observer, Token = token, Host = host });
            return true;
        }

        public void ReleaseObserver(ulong observer, ulong token)
        {
            for (int i = 0; i < _order.Count; i++)
                if (_jobs.TryGetValue(_order[i], out Job job))
                    job.Waiters.RemoveAll(waiter => waiter.Observer == observer && waiter.Token == token);
        }

        public void ResetIdentity()
        {
            _jobs.Clear();
            _order.Clear();
            _departed.Clear();
            _isolated.Clear();
        }

        public void ReleaseWaiter(RegionKey region, ulong observer)
        {
            if (_jobs.TryGetValue(region, out Job job))
                job.Waiters.RemoveAll(waiter => waiter.Observer == observer);
        }

        public void Refresh()
        {
            byte size = _worldSize();
            for (byte x = 0; x < size; x++)
            {
                for (byte y = 0; y < size; y++)
                {
                    var region = new RegionKey(x, y);
                    if (!_activated(region) && !IsBusy(region)) continue;
                    Note(region, _demand(region));
                }
            }
        }

        public void Note(RegionKey region, int demand)
        {
            if (!_activated(region)) return;
            if (_isolated.Contains(region))
            {
                if (demand <= 0) _departed.Add(region);
                return;
            }
            if (demand <= 0)
            {
                _departed.Add(region);
                return;
            }
            if (!_departed.Contains(region) || _jobs.ContainsKey(region)) return;
            _jobs[region] = new Job { Session = _session(), Generation = _generation(region) };
            _order.Add(region);
        }

        public int Consume(int budget)
        {
            if (budget <= 0 || _order.Count == 0) return 0;
            ItemCycleThresholds thresholds = ItemCycleThresholds.Resolve();
            if (thresholds == null || !thresholds.NativeCyclePermitted) return 0;
            int used = 0;
            int examined = 0;
            while (used < budget && examined < _order.Count)
            {
                RegionKey region = _order[_cursor % _order.Count];
                _cursor++;
                examined++;
                if (!_jobs.TryGetValue(region, out Job job)) continue;
                if (job.Session != _session() || _generation(region) != job.Generation)
                {
                    _isolated.Add(region);
                    Drop(region);
                    continue;
                }
                if (job.Clearing)
                {
                    if (RemoveOne(region, thresholds)) { used++; continue; }
                    if (HasExpiredNatural(region, thresholds)) continue;
                    job.Clearing = false;
                    job.RestockBudget = Shortage(region, thresholds);
                }
                if (job.RestockBudget <= 0 || job.RestockUsed >= job.RestockBudget || Count(region) >= Desired(region, thresholds))
                {
                    Finish(region, job, true);
                    continue;
                }
                job.RestockUsed++;
                if (_bridge.TryCreateOneNatural((byte)region.X, (byte)region.Y, _now()))
                    job.RestockSucceeded = true;
                AdoptNew(region, _now());
                used++;
            }
            return used;
        }

        private bool RemoveOne(RegionKey region, ItemCycleThresholds thresholds)
        {
            byte x = (byte)region.X;
            byte y = (byte)region.Y;
            IReadOnlyList<ItemData> items = _bridge.GetRegionItems(x, y);
            if (items == null) return false;
            float now = _now();
            for (int i = 0; i < items.Count; i++)
            {
                ItemData item = items[i];
                if (item == null || item.instanceID == 0U) continue;
                if (!_store.TryGetMetadata(item.instanceID, out ItemMetadataRecord record)
                    || record.Origin != ItemOriginKind.Natural) continue;
                if (now - item.lastDropped <= thresholds.DespawnNaturalTime) continue;
                if (!_bridge.TryRemoveItem(x, y, item.instanceID)) continue;
                _store.RemoveItem(item.instanceID);
                return true;
            }
            return false;
        }

        private bool HasExpiredNatural(RegionKey region, ItemCycleThresholds thresholds)
        {
            IReadOnlyList<ItemData> items = _bridge.GetRegionItems((byte)region.X, (byte)region.Y);
            if (items == null) return false;
            for (int i = 0; i < items.Count; i++)
            {
                ItemData item = items[i];
                if (item == null || !_store.TryGetMetadata(item.instanceID, out ItemMetadataRecord record)) continue;
                if (record.Origin == ItemOriginKind.Natural && _now() - item.lastDropped > thresholds.DespawnNaturalTime)
                    return true;
            }
            return false;
        }

        private int Shortage(RegionKey region, ItemCycleThresholds thresholds)
        {
            int desired = Desired(region, thresholds);
            int current = Count(region);
            return desired > current ? desired - current : 0;
        }

        private int Desired(RegionKey region, ItemCycleThresholds thresholds)
        {
            int points = _bridge.CountSpawnPoints((byte)region.X, (byte)region.Y);
            if (points <= 0) return 0;
            if (_now() - _bridge.ReadLastRespawn((byte)region.X, (byte)region.Y) <= thresholds.RespawnTime) return Count(region);
            return (int)(points * thresholds.SpawnChance);
        }

        private int Count(RegionKey region)
        {
            IReadOnlyList<ItemData> items = _bridge.GetRegionItems((byte)region.X, (byte)region.Y);
            return items == null ? 0 : items.Count;
        }

        private void AdoptNew(RegionKey region, float now)
        {
            IReadOnlyList<ItemData> items = _bridge.GetRegionItems((byte)region.X, (byte)region.Y);
            if (items == null) return;
            for (int i = 0; i < items.Count; i++)
            {
                ItemData item = items[i];
                if (item == null || item.instanceID == 0U) continue;
                if (_store.TryGetMetadata(item.instanceID, out _)) continue;
                if (ItemOriginTrackingSeam.IsTrackedDropped(item.instanceID))
                    _store.RegisterItem(item.instanceID, region, ItemOriginKind.Dropped, item.lastDropped, item.lastDropped);
                else
                {
                    ItemOriginTrackingSeam.NotifyNaturalItem(item.instanceID);
                    _store.RegisterItem(item.instanceID, region, ItemOriginKind.Natural, now, now);
                }
            }
        }

        private void Finish(RegionKey region, Job job, bool deliver)
        {
            if (job.RestockSucceeded)
                _bridge.CommitRespawnTime((byte)region.X, (byte)region.Y, _now());
            var waiters = job.Waiters;
            bool send = deliver && _demand(region) > 0;
            Drop(region);
            if (!send) return;
            _departed.Remove(region);
            for (int i = 0; i < waiters.Count; i++)
            {
                Waiter waiter = waiters[i];
                if (ConnectionCurrent != null && !ConnectionCurrent(waiter.Observer, waiter.Token)) continue;
                Deliver?.Invoke(waiter.Observer, waiter.Token, region, waiter.Host);
            }
        }

        private void Drop(RegionKey region)
        {
            _jobs.Remove(region);
            _order.Remove(region);
        }

        private struct Waiter
        {
            public ulong Observer;
            public ulong Token;
            public bool Host;
        }

        private sealed class Job
        {
            public ulong Session;
            public uint Generation;
            public bool Clearing = true;
            public int RestockBudget;
            public int RestockUsed;
            public bool RestockSucceeded;
            public readonly List<Waiter> Waiters = new List<Waiter>();
        }
    }
}
