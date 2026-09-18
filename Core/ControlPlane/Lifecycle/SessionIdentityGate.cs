using System;

namespace SteamP2PFriends.MultiObserver.Lifecycle
{
    /// <summary>
    /// 会话身份的就绪状态。身份不确定时既不当作正常（旧代次无写入资格），也不当作终止
    /// （不得清空其它领域）：先在有限窗口内恢复，恢复不了就显式熔断并继续有界心跳。
    /// </summary>
    public enum ESessionIdentityState
    {
        /// <summary>身份已确认且与本会话一致：可写入。</summary>
        Ready = 0,

        /// <summary>身份缺失或与已确认身份不一致：恢复窗口内，写入被拒绝且不做破坏性释放。</summary>
        Recovering = 1,

        /// <summary>恢复窗口耗尽：显式熔断，保持阻断直到身份重新可用并重建会话。</summary>
        CircuitBroken = 2
    }

    /// <summary>一次身份观测的判定结果。它只描述状态与应写出的记录，不自己结束任何会话。</summary>
    public readonly struct SessionIdentityDecision
    {
        internal SessionIdentityDecision(
            ESessionIdentityState state,
            bool changed,
            bool shouldEmit,
            EBoundedHeartbeat heartbeat,
            bool requiresResynchronization)
        {
            State = state;
            Changed = changed;
            ShouldEmit = shouldEmit;
            Heartbeat = heartbeat;
            RequiresResynchronization = requiresResynchronization;
        }

        public ESessionIdentityState State { get; }

        /// <summary>本次观测是否发生了状态转换（恢复与熔断的闭环点）。</summary>
        public bool Changed { get; }

        /// <summary>本次是否应写出一条记录（首条、重复或终止；受有界心跳约束）。</summary>
        public bool ShouldEmit { get; }

        public EBoundedHeartbeat Heartbeat { get; }

        /// <summary>
        /// 熔断后的恢复标记：会话可能在失明期间换了世界，旧 Session Epoch 与旧代次不得再写入，
        /// 调用方必须重建会话后才能继续。
        /// </summary>
        public bool RequiresResynchronization { get; }
    }

    /// <summary>
    /// 会话身份门：把「宿主会话身份缺失/不一致」从「只打一条去重日志然后整局失明」变成
    /// 有界恢复或显式熔断。它只读身份与时间，产出决策；会话的建立与结束仍由调用方执行。
    ///
    /// 状态机：Ready --身份不可用--> Recovering（有限窗口 + 有界心跳）
    ///        Recovering --窗口耗尽--> CircuitBroken（显式熔断 + 自己的有界心跳）
    ///        任一异常态 --身份重新可用--> Ready（闭环记录；熔断后的恢复要求重建会话）
    /// </summary>
    public sealed class SessionIdentityGate
    {
        private readonly HeartbeatPolicy _heartbeatPolicy;
        private readonly float _recoveryWindowSeconds;
        private string _acknowledgedIdentity = string.Empty;
        private BoundedHeartbeat _heartbeat;
        private float _recoveringSince;
        private bool _heartbeatRunning;

        public SessionIdentityGate(HeartbeatPolicy heartbeatPolicy, float recoveryWindowSeconds)
        {
            if (recoveryWindowSeconds <= 0f)
                throw new ArgumentOutOfRangeException(
                    nameof(recoveryWindowSeconds), "身份恢复窗口必须为正");
            _heartbeatPolicy = heartbeatPolicy;
            _recoveryWindowSeconds = recoveryWindowSeconds;
            State = ESessionIdentityState.Recovering;
        }

        public ESessionIdentityState State { get; private set; }

        public bool IsReady => State == ESessionIdentityState.Ready;

        public bool IsCircuitBroken => State == ESessionIdentityState.CircuitBroken;

        /// <summary>已确认的会话身份；尚未确认时为空串。</summary>
        public string AcknowledgedIdentity => _acknowledgedIdentity ?? string.Empty;

        /// <summary>
        /// 确认一个新身份（会话建立或身份重新可用）。确认即回到 Ready 并停掉旧心跳——
        /// 新会话与新身份是新的起点，不继承旧状态的熔断标记。
        /// </summary>
        public void Bind(string identity)
        {
            if (string.IsNullOrEmpty(identity))
                throw new ArgumentException("会话身份不得为空", nameof(identity));
            _acknowledgedIdentity = identity;
            _heartbeat.Stop();
            _heartbeatRunning = false;
            State = ESessionIdentityState.Ready;
        }

        /// <summary>
        /// 观测本拍的实际身份。身份与本门已确认值一致即就绪；缺失或不一致即进入恢复窗口；
        /// 窗口耗尽转为显式熔断。恢复与熔断都带自己的有界心跳，恢复必留闭环记录。
        /// </summary>
        public SessionIdentityDecision Observe(string observedIdentity, float now)
        {
            if (IsAcknowledged(observedIdentity))
            {
                if (State == ESessionIdentityState.Ready)
                    return Decision(ESessionIdentityState.Ready, false, EBoundedHeartbeat.Suppressed, false);
                bool requiresResynchronization = State != ESessionIdentityState.Recovering;
                _heartbeatRunning = false;
                State = ESessionIdentityState.Ready;
                return Decision(ESessionIdentityState.Ready, true, EBoundedHeartbeat.First, requiresResynchronization);
            }

            if (State == ESessionIdentityState.Ready)
            {
                State = ESessionIdentityState.Recovering;
                _recoveringSince = now;
                RestartHeartbeat(now);
                // 状态转换记录与心跳首条是同一条：先起搏再取心跳判定，避免出现「记了一条 First
                // 但心跳的 First 又在下一次观测才发出」的双计数。
                return Decision(ESessionIdentityState.Recovering, true, NextHeartbeat(now), false);
            }

            if (State == ESessionIdentityState.Recovering
                && now - _recoveringSince >= _recoveryWindowSeconds)
            {
                // 恢复窗口耗尽：显式熔断。熔断状态有自己的有界心跳，因此不是「一条日志后永久静默」。
                State = ESessionIdentityState.CircuitBroken;
                RestartHeartbeat(now);
                return Decision(ESessionIdentityState.CircuitBroken, true, NextHeartbeat(now), false);
            }

            EBoundedHeartbeat kind = NextHeartbeat(now);
            return Decision(State, false, kind, false);
        }

        /// <summary>会话结束或插件关闭：清空身份与心跳，回到未确认状态。</summary>
        public void Reset()
        {
            _acknowledgedIdentity = string.Empty;
            _heartbeat.Stop();
            _heartbeatRunning = false;
            State = ESessionIdentityState.Recovering;
        }

        private bool IsAcknowledged(string observedIdentity) =>
            !string.IsNullOrEmpty(observedIdentity)
            && !string.IsNullOrEmpty(_acknowledgedIdentity)
            && string.Equals(_acknowledgedIdentity, observedIdentity, StringComparison.Ordinal);

        private void RestartHeartbeat(float now)
        {
            _heartbeat = new BoundedHeartbeat(_heartbeatPolicy, now);
            _heartbeatRunning = true;
        }

        private EBoundedHeartbeat NextHeartbeat(float now)
        {
            if (!_heartbeatRunning) return EBoundedHeartbeat.Suppressed;
            EBoundedHeartbeat kind = _heartbeat.Next(now);
            if (kind == EBoundedHeartbeat.Exhausted) _heartbeatRunning = false;
            return kind;
        }

        private static SessionIdentityDecision Decision(
            ESessionIdentityState state,
            bool changed,
            EBoundedHeartbeat kind,
            bool requiresResynchronization)
        {
            return new SessionIdentityDecision(
                state, changed, kind != EBoundedHeartbeat.Suppressed, kind, requiresResynchronization);
        }
    }
}
