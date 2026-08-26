using System;
using System.Collections.Generic;
using SteamP2PFriends.Core.Identity;

namespace SteamP2PFriends.Adapters.Structure
{
    /// <summary>
    /// 玩家防御工事（Barricade）生命周期账本 (BarricadeRegionLifecycleLedger)
    /// 纯内存空间租约管理，支持 2D 网格区域与载具植入体（Plant）代次追踪与状态更新增量。
    /// </summary>
    public sealed class BarricadeRegionLifecycleLedger
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

        private readonly Dictionary<BarricadeKey, RegionEntry> _regions = new Dictionary<BarricadeKey, RegionEntry>();
        private readonly HashSet<BarricadeKey> _activeKeys = new HashSet<BarricadeKey>();
        private readonly float _hysteresisSeconds;
        private uint _sessionEpoch;
        private bool _isSessionActive;

        public BarricadeRegionLifecycleLedger(float hysteresisSeconds = DefaultHysteresisSeconds)
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

        public uint AcquireObserver(BarricadeKey regionKey)
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
            entry.ReleaseDeadline = float.MinValue; // 取消可能存在的滞后释放
            _activeKeys.Add(regionKey);
            return entry.Generation;
        }

        public bool ScheduleRelease(BarricadeKey regionKey, float currentTime)
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

        public bool CommitRelease(BarricadeKey regionKey, float currentTime, uint expectedSessionEpoch, uint expectedGeneration)
        {
            if (!_isSessionActive || expectedSessionEpoch != _sessionEpoch) return false;
            if (!_regions.TryGetValue(regionKey, out RegionEntry entry)) return false;
            if (entry.Generation != expectedGeneration) return false;
            if (entry.ObserverCount > 0) return false; // 重新被占用

            if (currentTime >= entry.ReleaseDeadline && entry.ReleaseDeadline > float.MinValue)
            {
                entry.Generation++;
                entry.ReleaseDeadline = float.MinValue;
                _activeKeys.Remove(regionKey);
                return true;
            }

            return false;
        }

        public uint RecordBarricadeChange(byte x, byte y, ushort plant, ushort index)
        {
            if (!_isSessionActive) return 0;
            BarricadeKey key = BarricadeKey.FromNative(x, y, plant);

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

        public uint GetGeneration(BarricadeKey regionKey)
        {
            return _regions.TryGetValue(regionKey, out RegionEntry entry) ? entry.Generation : 0;
        }

        public uint GetDeltaSequence(BarricadeKey regionKey)
        {
            return _regions.TryGetValue(regionKey, out RegionEntry entry) ? entry.DeltaSequence : 0;
        }

        public bool IsRegionActive(BarricadeKey regionKey)
        {
            return _activeKeys.Contains(regionKey);
        }

        public IReadOnlyCollection<ushort> GetModifiedIndices(BarricadeKey regionKey)
        {
            if (_regions.TryGetValue(regionKey, out RegionEntry entry))
            {
                return new List<ushort>(entry.ModifiedIndices);
            }
            return new List<ushort>();
        }

        public void ClearModifiedIndices(BarricadeKey regionKey)
        {
            if (_regions.TryGetValue(regionKey, out RegionEntry entry))
            {
                entry.ModifiedIndices.Clear();
            }
        }
    }

    /// <summary>
    /// 玩家防御工事生命周期统一适配器 (BarricadeRegionLifecycleAdapter)
    /// </summary>
    public static class BarricadeRegionLifecycleAdapter
    {
        private static readonly BarricadeRegionLifecycleLedger Ledger = new BarricadeRegionLifecycleLedger();
        private static bool _registrationReady;

        public static bool IsRegistrationReady => _registrationReady;
        public static uint CurrentSessionEpoch => Ledger.SessionEpoch;

        public static void SetRegistrationReady(bool ready) => _registrationReady = ready;
        public static void BeginSession(uint epoch) => Ledger.BeginSession(epoch);
        public static void EndSession() => Ledger.EndSession();

        public static uint OnObserverAcquire(BarricadeKey regionKey) => Ledger.AcquireObserver(regionKey);
        public static bool OnObserverRelease(BarricadeKey regionKey, float currentTime) => Ledger.ScheduleRelease(regionKey, currentTime);
        public static bool CommitRelease(BarricadeKey regionKey, float currentTime, uint expectedEpoch, uint expectedGen) =>
            Ledger.CommitRelease(regionKey, currentTime, expectedEpoch, expectedGen);

        public static uint RecordBarricadePlaced(byte x, byte y, ushort plant, ushort index) =>
            Ledger.RecordBarricadeChange(x, y, plant, index);

        public static uint RecordBarricadeDamaged(byte x, byte y, ushort plant, ushort index) =>
            Ledger.RecordBarricadeChange(x, y, plant, index);

        public static uint RecordBarricadeStateUpdated(byte x, byte y, ushort plant, ushort index) =>
            Ledger.RecordBarricadeChange(x, y, plant, index);

        public static uint RecordBarricadeSalvaged(byte x, byte y, ushort plant, ushort index) =>
            Ledger.RecordBarricadeChange(x, y, plant, index);

        public static uint GetGeneration(BarricadeKey regionKey) => Ledger.GetGeneration(regionKey);
        public static uint GetDeltaSequence(BarricadeKey regionKey) => Ledger.GetDeltaSequence(regionKey);
        public static bool IsRegionActive(byte x, byte y, ushort plant = 0) =>
            Ledger.IsRegionActive(BarricadeKey.FromNative(x, y, plant));

        public static BarricadeRegionLifecycleLedger CreateLedgerForTests(float hysteresis = 2.0f) =>
            new BarricadeRegionLifecycleLedger(hysteresis);
    }
}
