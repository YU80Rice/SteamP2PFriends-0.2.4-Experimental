using System.Collections.Generic;

namespace SteamP2PFriends.MultiObserver.Lifecycle
{
    public sealed class FailedRetryController
    {
        private readonly RetrySchedule _schedule;
        private readonly Dictionary<string, Chain> _chains = new Dictionary<string, Chain>();

        public FailedRetryController(RetrySchedule schedule) { _schedule = schedule; }

        public void ReportActualFault(string workKey, int attemptId, float now)
        {
            if (string.IsNullOrEmpty(workKey)) return;
            if (!_chains.TryGetValue(workKey, out Chain chain))
            {
                _chains[workKey] = new Chain
                {
                    FirstFaultAt = now,
                    NextAt = now + _schedule.IntervalFor(1),
                    LastAttemptId = attemptId
                };
                return;
            }
            if (chain.LastAttemptId == attemptId) return;
            chain.LastAttemptId = attemptId;
        }

        public bool TryClaimAttempt(string workKey, float now)
        {
            if (!_chains.TryGetValue(workKey, out Chain chain) || chain.Exhausted || chain.InFlight)
                return false;
            if (now < chain.NextAt) return false;
            if (DeadlineReached(chain, now) || RetryLimitReached(chain))
            {
                chain.Exhausted = true;
                return false;
            }
            chain.RetriesStarted++;
            chain.InFlight = true;
            return true;
        }

        public void RetryFailed(string workKey, float now)
        {
            if (!_chains.TryGetValue(workKey, out Chain chain) || !chain.InFlight) return;
            chain.InFlight = false;
            if (DeadlineReached(chain, now) || RetryLimitReached(chain))
            {
                chain.Exhausted = true;
                return;
            }
            chain.NextAt = now + _schedule.IntervalFor(chain.RetriesStarted + 1);
        }

        public bool IsExhausted(string workKey) =>
            _chains.TryGetValue(workKey, out Chain chain) && chain.Exhausted;

        public bool RequestManual(string workKey, float now)
        {
            if (!_chains.TryGetValue(workKey, out Chain chain) || !chain.Exhausted) return false;
            chain.FirstFaultAt = now;
            chain.RetriesStarted = 0;
            chain.NextAt = now;
            chain.Exhausted = false;
            chain.InFlight = false;
            return true;
        }

        public void Complete(string workKey) => _chains.Remove(workKey);
        public void Revoke(string workKey) => _chains.Remove(workKey);

        private bool DeadlineReached(Chain chain, float now) =>
            _schedule.MaxElapsedSeconds > 0f
            && now - chain.FirstFaultAt >= _schedule.MaxElapsedSeconds;

        private bool RetryLimitReached(Chain chain) =>
            _schedule.MaxRetries > 0 && chain.RetriesStarted >= _schedule.MaxRetries;

        private sealed class Chain
        {
            internal float FirstFaultAt;
            internal float NextAt;
            internal int RetriesStarted;
            internal int LastAttemptId;
            internal bool Exhausted;
            internal bool InFlight;
        }
    }
}
