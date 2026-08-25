using System;
using System.Collections.Generic;

namespace SteamP2PFriends.Adapters.Structure
{
    /// <summary>
    /// 玩家防御工事快照复制账本 (BarricadeSnapshotReplicationLedger)
    /// </summary>
    public sealed class BarricadeSnapshotReplicationLedger
    {
        private sealed class ObserverRegionState
        {
            public uint AcknowledgedGeneration;
            public uint AcknowledgedDeltaSequence;
            public ulong ConnectionToken;
        }

        private readonly Dictionary<ulong, Dictionary<int, ObserverRegionState>> _observerStates =
            new Dictionary<ulong, Dictionary<int, ObserverRegionState>>();
        private readonly Dictionary<int, uint> _regionGenerations = new Dictionary<int, uint>();
        private readonly Dictionary<int, uint> _regionDeltaSequences = new Dictionary<int, uint>();

        public void UpdateRegionGeneration(int regionKey, uint generation)
        {
            _regionGenerations[regionKey] = generation;
        }

        public void AdvanceDeltaSequence(int regionKey)
        {
            if (!_regionDeltaSequences.TryGetValue(regionKey, out uint seq))
            {
                seq = 0;
            }
            _regionDeltaSequences[regionKey] = seq + 1;
        }

        public uint GetDeltaSequence(int regionKey)
        {
            return _regionDeltaSequences.TryGetValue(regionKey, out uint seq) ? seq : 0;
        }

        public bool ShouldReplicateSnapshot(ulong observerId, ulong connectionToken, int regionKey, uint currentGeneration)
        {
            if (!_observerStates.TryGetValue(observerId, out var regions))
            {
                regions = new Dictionary<int, ObserverRegionState>();
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
    /// 玩家防御工事快照复制统一适配器 (BarricadeSnapshotAdapter)
    /// </summary>
    public static class BarricadeSnapshotAdapter
    {
        private static readonly BarricadeSnapshotReplicationLedger Ledger = new BarricadeSnapshotReplicationLedger();

        public static void UpdateRegionGeneration(int regionKey, uint generation) =>
            Ledger.UpdateRegionGeneration(regionKey, generation);

        public static void AdvanceDeltaSequence(int regionKey) =>
            Ledger.AdvanceDeltaSequence(regionKey);

        public static bool ShouldReplicateSnapshot(ulong observerId, ulong connectionToken, int regionKey, uint currentGen) =>
            Ledger.ShouldReplicateSnapshot(observerId, connectionToken, regionKey, currentGen);

        public static void RemoveObserver(ulong observerId) =>
            Ledger.RemoveObserver(observerId);

        public static void Reset() =>
            Ledger.Reset();

        public static BarricadeSnapshotReplicationLedger CreateLedgerForTests() =>
            new BarricadeSnapshotReplicationLedger();
    }
}
