using System;

namespace SteamP2PFriends.MultiObserver.Lifecycle
{
    /// <summary>
    /// 领域声明的心跳政策：持续异常、恢复与熔断的诊断节奏。心跳必须有界——既有间隔下限
    /// （不逐帧淹没日志），又有重复上限（不无限刷屏），并在用尽时显式声明「已抑制、等待状态
    /// 变化」，而不是只留一条去重日志后永久静默；状态一变即重新起搏，恢复必留闭环记录。
    /// </summary>
    public readonly struct HeartbeatPolicy
    {
        public HeartbeatPolicy(float intervalSeconds, int maxRepeats)
        {
            if (intervalSeconds <= 0f)
                throw new ArgumentOutOfRangeException(nameof(intervalSeconds), "心跳间隔必须为正");
            if (maxRepeats < 1)
                throw new ArgumentOutOfRangeException(nameof(maxRepeats), "心跳重复上限至少为一次");

            IntervalSeconds = intervalSeconds;
            MaxRepeats = maxRepeats;
        }

        /// <summary>两条心跳之间的最小间隔（秒）：持续状态按此速率重复。</summary>
        public float IntervalSeconds { get; }

        /// <summary>首次记录之后允许重复的最大次数；用尽后写出一条显式终止记录。</summary>
        public int MaxRepeats { get; }

        public override string ToString() =>
            $"interval={IntervalSeconds:0.###}s maxRepeats={MaxRepeats}";
    }

    /// <summary>有界心跳的一次判定结果。</summary>
    public enum EBoundedHeartbeat
    {
        /// <summary>首次进入持续状态：立即写出一条。</summary>
        First = 0,

        /// <summary>按间隔重复：仍在重复上限内。</summary>
        Repeat = 1,

        /// <summary>重复次数用尽：写出最后一条显式终止记录，此后静默等待状态变化。</summary>
        Exhausted = 2,

        /// <summary>本次不产生记录（未到间隔、已终止或未启动）。</summary>
        Suppressed = 3
    }

    /// <summary>
    /// 有界心跳状态机：持续状态（样本暂缓、领域熔断、写入挂起）的重复诊断由它驱动。
    /// 起搏即写出首条；此后按政策间隔重复，重复次数受上限约束，用尽写出一条显式终止记录。
    /// 状态变化（恢复或重新进入）必须由调用方重新起搏，因此恢复必留闭环记录。
    /// </summary>
    public struct BoundedHeartbeat
    {
        private readonly HeartbeatPolicy _policy;
        private float _nextEmitAt;
        private int _emitted;
        private bool _running;

        public BoundedHeartbeat(HeartbeatPolicy policy, float now)
        {
            _policy = policy;
            _nextEmitAt = now;
            _emitted = 0;
            _running = true;
        }

        /// <summary>心跳是否仍在起搏（终止或状态结束后为 false）。</summary>
        public bool IsRunning => _running;

        /// <summary>迄今已写出的记录数（首条 + 重复 + 终止记录）。</summary>
        public int EmittedCount => _emitted;

        /// <summary>推进到 now 并给出本次判定。</summary>
        public EBoundedHeartbeat Next(float now)
        {
            if (!_running) return EBoundedHeartbeat.Suppressed;
            if (_emitted == 0) return Emit(now, EBoundedHeartbeat.First);
            if (now < _nextEmitAt) return EBoundedHeartbeat.Suppressed;
            if (_emitted > _policy.MaxRepeats)
            {
                // 重复上限已用尽：写出终止记录后转入静默，等待状态变化重新起搏。
                _running = false;
                return EBoundedHeartbeat.Exhausted;
            }

            return Emit(now, EBoundedHeartbeat.Repeat);
        }

        /// <summary>结束本次心跳：状态已恢复或不再持续，后续不再产出记录。</summary>
        public void Stop()
        {
            _running = false;
        }

        private EBoundedHeartbeat Emit(float now, EBoundedHeartbeat kind)
        {
            _emitted++;
            _nextEmitAt = now + _policy.IntervalSeconds;
            return kind;
        }
    }
}
