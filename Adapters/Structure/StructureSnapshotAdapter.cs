using System;
using System.Collections.Generic;
using SteamP2PFriends.Core.Identity;

namespace SteamP2PFriends.Adapters.Structure
{
    /// <summary>
    /// 玩家建筑结构快照复制账本 (StructureSnapshotReplicationLedger)
    /// </summary>
    public sealed class StructureSnapshotReplicationLedger
    {
        private sealed class ObserverRegionState
        {
            public uint AcknowledgedGeneration;
            public uint AcknowledgedDeltaSequence;
            public ulong ConnectionToken;
        }

        private readonly Dictionary<ulong, Dictionary<RegionKey, ObserverRegionState>> _observerStates =
            new Dictionary<ulong, Dictionary<RegionKey, ObserverRegionState>>();
        private readonly Dictionary<RegionKey, uint> _regionGenerations = new Dictionary<RegionKey, uint>();
        private readonly Dictionary<RegionKey, uint> _regionDeltaSequences = new Dictionary<RegionKey, uint>();

        public void UpdateRegionGeneration(RegionKey regionKey, uint generation)
        {
            _regionGenerations[regionKey] = generation;
        }

        public void AdvanceDeltaSequence(RegionKey regionKey)
        {
            if (!_regionDeltaSequences.TryGetValue(regionKey, out uint seq))
            {
                seq = 0;
            }
            _regionDeltaSequences[regionKey] = seq + 1;
        }

        public uint GetDeltaSequence(RegionKey regionKey)
        {
            return _regionDeltaSequences.TryGetValue(regionKey, out uint seq) ? seq : 0;
        }

        public bool ShouldReplicateSnapshot(ulong observerId, ulong connectionToken, RegionKey regionKey, uint currentGeneration)
        {
            if (!_observerStates.TryGetValue(observerId, out var regions))
            {
                regions = new Dictionary<RegionKey, ObserverRegionState>();
                _observerStates[observerId] = regions;
            }

            if (!regions.TryGetValue(regionKey, out var state) || state.ConnectionToken != connectionToken)
            {
                regions[regionKey] = new ObserverRegionState
                {
                    AcknowledgedGeneration = currentGeneration,
                    AcknowledgedDeltaSequence = GetDeltaSequence(regionKey),
                    ConnectionToken = connectionToken
                };
                return true;
            }

            if (state.AcknowledgedGeneration != currentGeneration)
            {
                state.AcknowledgedGeneration = currentGeneration;
                state.AcknowledgedDeltaSequence = GetDeltaSequence(regionKey);
                return true;
            }

            return false;
        }

        public void RemoveObserver(ulong observerId)
        {
            _observerStates.Remove(observerId);
        }

        public void Reset()
        {
            _observerStates.Clear();
            _regionGenerations.Clear();
            _regionDeltaSequences.Clear();
        }
    }

    /// <summary>
    /// 玩家建筑结构快照复制统一适配器 (StructureSnapshotAdapter)
    /// </summary>
    public static class StructureSnapshotAdapter
    {
        private static readonly StructureSnapshotReplicationLedger Ledger = new StructureSnapshotReplicationLedger();

        public static void UpdateRegionGeneration(RegionKey regionKey, uint generation) =>
            Ledger.UpdateRegionGeneration(regionKey, generation);

        public static void AdvanceDeltaSequence(RegionKey regionKey) =>
            Ledger.AdvanceDeltaSequence(regionKey);

        public static bool ShouldReplicateSnapshot(ulong observerId, ulong connectionToken, RegionKey regionKey, uint currentGen) =>
            Ledger.ShouldReplicateSnapshot(observerId, connectionToken, regionKey, currentGen);

        public static void RemoveObserver(ulong observerId) =>
            Ledger.RemoveObserver(observerId);

        public static void Reset() =>
            Ledger.Reset();

        public static StructureSnapshotReplicationLedger CreateLedgerForTests() =>
            new StructureSnapshotReplicationLedger();
    }
}
