using SteamP2PFriends.Core.Identity;
using SteamP2PFriends.MultiObserver.Lifecycle;
using System;
using System.Collections.Generic;

namespace SteamP2PFriends.Adapters.Item
{
    public enum EItemRecoveryRequestResult : byte
    {
        Started = 0,
        NotEligible = 1,
        StaleConnection = 2,
        RateLimited = 3
    }

    /// <summary>
    /// Item 领域结算异常的有限预算。它只管理异常作业的时间/次数与受影响观察者资格，
    /// 不重建仓库，也不触碰原生 packet 状态。
    /// </summary>
    public sealed class ItemFailureRecovery
    {
        private static readonly RetrySchedule FailureSchedule = ItemLifecyclePolicy.Create().FailedRetry;
        public static int MaxRetries => FailureSchedule.MaxRetries;
        public static float MaxElapsedSeconds => FailureSchedule.MaxElapsedSeconds;
        public const float ManualCooldownSeconds = 10.0f;

        private readonly Dictionary<RegionKey, Entry> _entries = new Dictionary<RegionKey, Entry>();

        public bool IsIsolated(RegionKey region) => _entries.ContainsKey(region);

        public bool IsExhausted(RegionKey region) =>
            _entries.TryGetValue(region, out Entry entry) && entry.Exhausted;

        public bool RecordFailure(RegionKey region, float now, out bool exhaustedNow)
        {
            if (!_entries.TryGetValue(region, out Entry entry))
                entry = new Entry(now);
            entry.FailureCount++;
            float elapsed = now - entry.StartedAt;
            entry.Exhausted = entry.FailureCount > MaxRetries
                || elapsed >= MaxElapsedSeconds;
            entry.NextRetryAt = entry.Exhausted
                ? float.PositiveInfinity
                : now + IntervalFor(entry.FailureCount);
            exhaustedNow = entry.Exhausted && !entry.ExhaustionNotified;
            if (exhaustedNow) entry.ExhaustionNotified = true;
            _entries[region] = entry;
            return !entry.Exhausted;
        }

        public bool TryBeginAutomatic(RegionKey region, float now, out bool exhaustedNow)
        {
            exhaustedNow = false;
            if (!_entries.TryGetValue(region, out Entry entry) || entry.Exhausted) return false;
            if (now - entry.StartedAt >= MaxElapsedSeconds)
            {
                entry.Exhausted = true;
                exhaustedNow = !entry.ExhaustionNotified;
                entry.ExhaustionNotified = true;
                _entries[region] = entry;
                return false;
            }
            if (now < entry.NextRetryAt) return false;
            entry.NextRetryAt = float.PositiveInfinity;
            _entries[region] = entry;
            return true;
        }

        public void HoldUnconfirmed(RegionKey region, float now)
        {
            if (!_entries.TryGetValue(region, out Entry entry)) entry = new Entry(now);
            entry.Exhausted = true;
            entry.NextRetryAt = float.PositiveInfinity;
            entry.ExhaustionNotified = true;
            _entries[region] = entry;
        }

        public void Complete(RegionKey region) => _entries.Remove(region);

        public void Invalidate(RegionKey region) => _entries.Remove(region);

        public IReadOnlyList<RegionKey> SnapshotRegions() => new List<RegionKey>(_entries.Keys);

        public EItemRecoveryRequestResult RequestManual(
            RegionKey region, ulong observerId, ulong connectionToken, float now,
            Func<ulong, ulong, bool> connectionCurrent)
        {
            if (!_entries.TryGetValue(region, out Entry entry) || !entry.Exhausted)
                return EItemRecoveryRequestResult.NotEligible;
            if (!entry.Waiters.Contains(new Waiter(observerId, connectionToken)))
                return EItemRecoveryRequestResult.NotEligible;
            if (connectionCurrent != null && !connectionCurrent(observerId, connectionToken))
                return EItemRecoveryRequestResult.StaleConnection;
            if (entry.LastManualAt.TryGetValue(
                    new Waiter(observerId, connectionToken), out float lastManual)
                && now - lastManual < ManualCooldownSeconds)
                return EItemRecoveryRequestResult.RateLimited;
            entry.FailureCount = 0;
            entry.StartedAt = now;
            entry.NextRetryAt = now;
            entry.LastManualAt[new Waiter(observerId, connectionToken)] = now;
            entry.Exhausted = false;
            entry.ExhaustionNotified = false;
            _entries[region] = entry;
            return EItemRecoveryRequestResult.Started;
        }

        public void RememberProgress(
            RegionKey region, bool clearing, int budget, int used, bool succeeded)
        {
            if (!_entries.TryGetValue(region, out Entry entry)) return;
            entry.HasProgress = true;
            entry.Clearing = clearing;
            entry.RestockBudget = budget;
            entry.RestockUsed = used;
            entry.RestockSucceeded = succeeded;
        }

        public bool TryReadProgress(
            RegionKey region, out bool clearing, out int budget, out int used, out bool succeeded)
        {
            clearing = true;
            budget = 0;
            used = 0;
            succeeded = false;
            if (!_entries.TryGetValue(region, out Entry entry) || !entry.HasProgress) return false;
            clearing = entry.Clearing;
            budget = entry.RestockBudget;
            used = entry.RestockUsed;
            succeeded = entry.RestockSucceeded;
            return true;
        }

        public void SetWaiters(RegionKey region, IEnumerable<Waiter> waiters)
        {
            if (!_entries.TryGetValue(region, out Entry entry)) return;
            entry.Waiters.Clear();
            if (waiters != null) entry.Waiters.AddRange(waiters);
            _entries[region] = entry;
        }

        public void RemoveWaiter(ulong observerId, ulong connectionToken)
        {
            foreach (RegionKey region in SnapshotRegions())
            {
                if (!_entries.TryGetValue(region, out Entry entry)) continue;
                entry.Waiters.RemoveAll(waiter => waiter.ObserverId == observerId
                    && waiter.ConnectionToken == connectionToken);
                if (entry.Waiters.Count == 0) _entries.Remove(region);
                else _entries[region] = entry;
            }
        }

        public void RemoveWaiter(RegionKey region, ulong observerId, ulong connectionToken)
        {
            if (!_entries.TryGetValue(region, out Entry entry)) return;
            entry.Waiters.RemoveAll(waiter => waiter.ObserverId == observerId
                && waiter.ConnectionToken == connectionToken);
            if (entry.Waiters.Count == 0) _entries.Remove(region);
            else _entries[region] = entry;
        }

        public bool TryGetWaiters(RegionKey region, out IReadOnlyList<Waiter> waiters)
        {
            if (!_entries.TryGetValue(region, out Entry entry))
            {
                waiters = null;
                return false;
            }
            waiters = entry.Waiters;
            return true;
        }

        public void Clear() => _entries.Clear();

        private static float IntervalFor(int failureCount) => FailureSchedule.IntervalFor(failureCount);

        public struct Waiter : IEquatable<Waiter>
        {
            public Waiter(ulong observerId, ulong connectionToken, bool host = false)
            {
                ObserverId = observerId;
                ConnectionToken = connectionToken;
                Host = host;
            }
            public ulong ObserverId { get; }
            public ulong ConnectionToken { get; }
            public bool Host { get; }
            public bool Equals(Waiter other) => ObserverId == other.ObserverId
                && ConnectionToken == other.ConnectionToken;
            public override bool Equals(object obj) => obj is Waiter other && Equals(other);
            public override int GetHashCode() => ObserverId.GetHashCode() ^ ConnectionToken.GetHashCode();
        }

        private sealed class Entry
        {
            internal Entry(float startedAt)
            {
                StartedAt = startedAt;
            }
            internal float StartedAt;
            internal float NextRetryAt;
            internal int FailureCount;
            internal bool Exhausted;
            internal bool ExhaustionNotified;
            internal bool HasProgress;
            internal bool Clearing = true;
            internal int RestockBudget;
            internal int RestockUsed;
            internal bool RestockSucceeded;
            internal readonly Dictionary<Waiter, float> LastManualAt =
                new Dictionary<Waiter, float>();
            internal readonly List<Waiter> Waiters = new List<Waiter>();
        }
    }
}
