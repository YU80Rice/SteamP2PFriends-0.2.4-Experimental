using System;
using System.Collections.Generic;
using SteamP2PFriends.Core.Identity;

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

        private readonly Dictionary<ulong, Dictionary<BarricadeKey, ObserverRegionState>> _observerStates =
            new Dictionary<ulong, Dictionary<BarricadeKey, ObserverRegionState>>();
        private readonly Dictionary<BarricadeKey, uint> _regionGenerations = new Dictionary<BarricadeKey, uint>();
        private readonly Dictionary<BarricadeKey, uint> _regionDeltaSequences = new Dictionary<BarricadeKey, uint>();

        public void UpdateRegionGeneration(BarricadeKey regionKey, uint generation)
        {
            _regionGenerations[regionKey] = generation;
        }

        public void AdvanceDeltaSequence(BarricadeKey regionKey)
        {
            if (!_regionDeltaSequences.TryGetValue(regionKey, out uint seq))
            {
                seq = 0;
            }
            _regionDeltaSequences[regionKey] = seq + 1;
        }

        public uint GetDeltaSequence(BarricadeKey regionKey)
        {
            return _regionDeltaSequences.TryGetValue(regionKey, out uint seq) ? seq : 0;
        }

        public bool ShouldReplicateSnapshot(ulong observerId, ulong connectionToken, BarricadeKey regionKey, uint currentGeneration)
        {
            if (!_observerStates.TryGetValue(observerId, out var regions))
            {
                regions = new Dictionary<BarricadeKey, ObserverRegionState>();
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

        public static void UpdateRegionGeneration(BarricadeKey regionKey, uint generation) =>
            Ledger.UpdateRegionGeneration(regionKey, generation);

        public static void AdvanceDeltaSequence(BarricadeKey regionKey) =>
            Ledger.AdvanceDeltaSequence(regionKey);

        public static bool ShouldReplicateSnapshot(ulong observerId, ulong connectionToken, BarricadeKey regionKey, uint currentGen) =>
            Ledger.ShouldReplicateSnapshot(observerId, connectionToken, regionKey, currentGen);

        public static void RemoveObserver(ulong observerId) =>
            Ledger.RemoveObserver(observerId);

        public static void Reset() =>
            Ledger.Reset();

        public static BarricadeSnapshotReplicationLedger CreateLedgerForTests() =>
            new BarricadeSnapshotReplicationLedger();
    }
}
