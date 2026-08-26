using System;
using System.Collections.Generic;
using SteamP2PFriends.Core.Identity;

namespace SteamP2PFriends.Adapters.Structure
{
    /// <summary>
    /// 玩家建筑结构（Structure）生命周期账本 (StructureRegionLifecycleLedger)
    /// 纯内存空间租约管理，支持 2D 网格区域建筑放置、破坏、修复与代次追踪。
    /// </summary>
    public sealed class StructureRegionLifecycleLedger
    {
        private const float DefaultHysteresisSeconds = 2.0f;

        private sealed class RegionEntry
        {
            public uint Generation;
            public uint DeltaSequence;
            public int ObserverCount;
            public float ReleaseDeadline = float.MinValue;
            public readonly HashSet<ushort> ModifiedIndices = new HashSet<ushort>();
        }

        private readonly Dictionary<RegionKey, RegionEntry> _regions = new Dictionary<RegionKey, RegionEntry>();
        private readonly HashSet<RegionKey> _activeKeys = new HashSet<RegionKey>();
        private readonly float _hysteresisSeconds;
        private uint _sessionEpoch;
        private bool _isSessionActive;

        public StructureRegionLifecycleLedger(float hysteresisSeconds = DefaultHysteresisSeconds)
        {
            _hysteresisSeconds = Math.Max(0.1f, hysteresisSeconds);
        }

        public uint SessionEpoch => _sessionEpoch;
        public bool IsSessionActive => _isSessionActive;
        public int ActiveRegionCount => _activeKeys.Count;

        public void BeginSession(uint sessionEpoch)
        {
            _sessionEpoch = sessionEpoch;
            _isSessionActive = true;
            _regions.Clear();
            _activeKeys.Clear();
        }

        public void EndSession()
        {
            _isSessionActive = false;
            _regions.Clear();
            _activeKeys.Clear();
        }

        public uint AcquireObserver(RegionKey regionKey)
        {
            if (!_isSessionActive) return 0;

            if (!_regions.TryGetValue(regionKey, out RegionEntry entry))
            {
                entry = new RegionEntry { Generation = 1, ObserverCount = 1 };
                _regions[regionKey] = entry;
                _activeKeys.Add(regionKey);
                return entry.Generation;
            }

            entry.ObserverCount++;
            entry.ReleaseDeadline = float.MinValue;
            _activeKeys.Add(regionKey);
            return entry.Generation;
        }

        public bool ScheduleRelease(RegionKey regionKey, float currentTime)
        {
            if (!_isSessionActive) return false;
            if (!_regions.TryGetValue(regionKey, out RegionEntry entry)) return false;

            if (entry.ObserverCount > 0)
            {
                entry.ObserverCount--;
            }

            if (entry.ObserverCount == 0)
            {
                entry.ReleaseDeadline = currentTime + _hysteresisSeconds;
                return true;
            }

            return false;
        }

        public bool CommitRelease(RegionKey regionKey, float currentTime, uint expectedSessionEpoch, uint expectedGeneration)
        {
            if (!_isSessionActive || expectedSessionEpoch != _sessionEpoch) return false;
            if (!_regions.TryGetValue(regionKey, out RegionEntry entry)) return false;
            if (entry.Generation != expectedGeneration) return false;
            if (entry.ObserverCount > 0) return false;

            if (currentTime >= entry.ReleaseDeadline && entry.ReleaseDeadline > float.MinValue)
            {
                entry.Generation++;
                entry.ReleaseDeadline = float.MinValue;
                _activeKeys.Remove(regionKey);
                return true;
            }

            return false;
        }

        public uint RecordStructureChange(byte x, byte y, ushort index)
        {
            if (!_isSessionActive) return 0;
            RegionKey key = new RegionKey(x, y);

            if (!_regions.TryGetValue(key, out RegionEntry entry))
            {
                entry = new RegionEntry { Generation = 1, ObserverCount = 0 };
                _regions[key] = entry;
            }

            entry.Generation++;
            entry.DeltaSequence++;
            entry.ModifiedIndices.Add(index);
            return entry.Generation;
        }

        public uint GetGeneration(RegionKey regionKey)
        {
            return _regions.TryGetValue(regionKey, out RegionEntry entry) ? entry.Generation : 0;
        }

        public uint GetDeltaSequence(RegionKey regionKey)
        {
            return _regions.TryGetValue(regionKey, out RegionEntry entry) ? entry.DeltaSequence : 0;
        }

        public bool IsRegionActive(RegionKey regionKey)
        {
            return _activeKeys.Contains(regionKey);
        }

        public IReadOnlyCollection<ushort> GetModifiedIndices(RegionKey regionKey)
        {
            if (_regions.TryGetValue(regionKey, out RegionEntry entry))
            {
                return new List<ushort>(entry.ModifiedIndices);
            }
            return new List<ushort>();
        }

        public void ClearModifiedIndices(RegionKey regionKey)
        {
            if (_regions.TryGetValue(regionKey, out RegionEntry entry))
            {
                entry.ModifiedIndices.Clear();
            }
        }
    }

    /// <summary>
    /// 玩家建筑结构生命周期统一适配器 (StructureRegionLifecycleAdapter)
    /// </summary>
    public static class StructureRegionLifecycleAdapter
    {
        private static readonly StructureRegionLifecycleLedger Ledger = new StructureRegionLifecycleLedger();
        private static bool _registrationReady;

        public static bool IsRegistrationReady => _registrationReady;
        public static uint CurrentSessionEpoch => Ledger.SessionEpoch;

        public static void SetRegistrationReady(bool ready) => _registrationReady = ready;
        public static void BeginSession(uint epoch) => Ledger.BeginSession(epoch);
        public static void EndSession() => Ledger.EndSession();

        public static uint OnObserverAcquire(RegionKey regionKey) => Ledger.AcquireObserver(regionKey);
        public static bool OnObserverRelease(RegionKey regionKey, float currentTime) => Ledger.ScheduleRelease(regionKey, currentTime);
        public static bool CommitRelease(RegionKey regionKey, float currentTime, uint expectedEpoch, uint expectedGen) =>
            Ledger.CommitRelease(regionKey, currentTime, expectedEpoch, expectedGen);

        public static uint RecordStructurePlaced(byte x, byte y, ushort index) =>
            Ledger.RecordStructureChange(x, y, index);

        public static uint RecordStructureDamaged(byte x, byte y, ushort index) =>
            Ledger.RecordStructureChange(x, y, index);

        public static uint RecordStructureSalvaged(byte x, byte y, ushort index) =>
            Ledger.RecordStructureChange(x, y, index);

        public static uint GetGeneration(RegionKey regionKey) => Ledger.GetGeneration(regionKey);
        public static uint GetDeltaSequence(RegionKey regionKey) => Ledger.GetDeltaSequence(regionKey);
        public static bool IsRegionActive(byte x, byte y) =>
            Ledger.IsRegionActive(new RegionKey(x, y));

        public static StructureRegionLifecycleLedger CreateLedgerForTests(float hysteresis = 2.0f) =>
            new StructureRegionLifecycleLedger(hysteresis);
    }
}
