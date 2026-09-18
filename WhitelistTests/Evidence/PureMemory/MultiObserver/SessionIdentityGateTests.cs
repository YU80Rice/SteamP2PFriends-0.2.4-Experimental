using SteamP2PFriends.MultiObserver.Lifecycle;

namespace SteamP2PFriends.WhitelistTests
{
    /// <summary>
    /// 票 04：会话身份门的纯内存契约。它把「宿主会话身份缺失或不一致」从「一条去重日志后
    /// 永久静默」变成有限恢复或显式熔断：窗口内恢复不留重建代价；窗口耗尽转显式熔断并继续
    /// 有界心跳；熔断后的恢复要求重建会话（旧 Session Epoch 与旧代次不得再写入）。
    /// </summary>
    internal static class SessionIdentityGateTests
    {
        private static readonly HeartbeatPolicy Heartbeat = new HeartbeatPolicy(5f, 2);
        private const float Window = 15f;

        /// <summary>断言全部条件成立；失败时抛出带上下文取值的异常，免去重跑调试。</summary>
        private static bool Expect(params object[] conditionsAndContext)
        {
            for (int i = 0; i < conditionsAndContext.Length - 1; i++)
            {
                if (conditionsAndContext[i] is bool flag && !flag)
                    throw new System.InvalidOperationException(
                        "期望不成立：" + conditionsAndContext[conditionsAndContext.Length - 1]);
            }
            return true;
        }

        private static string Describe(SessionIdentityDecision decision)
        {
            return decision.State + "/changed=" + decision.Changed + "/emit=" + decision.ShouldEmit
                + "/hb=" + decision.Heartbeat + "/resync=" + decision.RequiresResynchronization;
        }

        /// <summary>窗口内恢复：失配进入 Recovering 并写出首条心跳，身份回来后回到 Ready 且不要求重建会话。</summary>
        internal static bool Test_SIG01_RecoversInsideWindow()
        {
            var gate = new SessionIdentityGate(Heartbeat, Window);
            gate.Bind("session-a");
            bool bound = gate.IsReady && gate.AcknowledgedIdentity == "session-a"
                && !gate.Observe("session-a", 0f).ShouldEmit;

            SessionIdentityDecision lost = gate.Observe("", 1f);
            SessionIdentityDecision mismatch = gate.Observe("session-b", 2f);
            bool recovering = lost.State == ESessionIdentityState.Recovering
                && lost.Changed && lost.ShouldEmit && lost.Heartbeat == EBoundedHeartbeat.First
                && !gate.IsReady && mismatch.State == ESessionIdentityState.Recovering
                && !mismatch.Changed && !mismatch.ShouldEmit
                && !mismatch.RequiresResynchronization;

            SessionIdentityDecision restored = gate.Observe("session-a", Window - 0.5f);
            return Expect(bound, recovering,
                restored.State == ESessionIdentityState.Ready, restored.Changed,
                restored.ShouldEmit, gate.IsReady, !restored.RequiresResynchronization,
                "bound=" + bound + " lost=" + Describe(lost) + " mismatch=" + Describe(mismatch)
                + " restored=" + Describe(restored) + " ready=" + gate.IsReady);
        }

        /// <summary>窗口耗尽：显式熔断（状态可区分），熔断态自带心跳且受重复上限约束（不是一条日志后静默）。</summary>
        internal static bool Test_SIG02_CircuitBreaksAfterWindowWithBoundedHeartbeat()
        {
            var gate = new SessionIdentityGate(Heartbeat, Window);
            gate.Bind("session-a");
            gate.Observe("", 0f);

            SessionIdentityDecision breaking = gate.Observe("", Window);
            bool brokeExplicitly = breaking.Changed
                && breaking.State == ESessionIdentityState.CircuitBroken
                && gate.IsCircuitBroken && !gate.IsReady
                && breaking.Heartbeat == EBoundedHeartbeat.First;

            int records = 1;
            EBoundedHeartbeat last = EBoundedHeartbeat.First;
            for (int second = 1; second <= 60; second++)
            {
                SessionIdentityDecision decision = gate.Observe("", Window + second);
                if (!decision.ShouldEmit) continue;
                records++;
                last = decision.Heartbeat;
            }

            return Expect(brokeExplicitly, records == 1 + 1 + Heartbeat.MaxRepeats,
                last == EBoundedHeartbeat.Exhausted,
                "breaking=" + Describe(breaking) + " records=" + records + " last=" + last
                + " maxRepeats=" + Heartbeat.MaxRepeats + " state=" + gate.State);
        }

        /// <summary>熔断后恢复：必须重建会话（旧 epoch/代次无写入资格）；Reset 回到未确认态并重新起搏。</summary>
        internal static bool Test_SIG03_CircuitBrokenRecoveryRequiresResynchronization()
        {
            var gate = new SessionIdentityGate(Heartbeat, Window);
            gate.Bind("session-a");
            gate.Observe("", 0f);
            gate.Observe("", Window);
            bool circuitBroken = gate.IsCircuitBroken;

            SessionIdentityDecision recovered = gate.Observe("session-a", Window + 1f);
            bool requiresRebuild = recovered.State == ESessionIdentityState.Ready
                && recovered.Changed && recovered.RequiresResynchronization && gate.IsReady;

            gate.Reset();
            bool resetIsUnacknowledged = !gate.IsReady
                && gate.AcknowledgedIdentity.Length == 0
                && gate.State == ESessionIdentityState.Recovering;

            gate.Bind("session-b");
            SessionIdentityDecision fresh = gate.Observe("session-b", Window + 2f);

            return circuitBroken && requiresRebuild && resetIsUnacknowledged
                && fresh.State == ESessionIdentityState.Ready && gate.IsReady
                && !fresh.RequiresResynchronization;
        }
    }
}
