using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SteamP2PFriends.Platform.Diagnostics.Patches
{
    /// <summary>
    /// §3.2 一次接收/消费的关联值。Has* 表示该原生值是否真的取到；
    /// 缺项一律保持 false，绝不内插、不用最近一包顶替。
    /// </summary>
    internal struct Bug1ConsumeRecord
    {
        public ulong RxId;
        public bool HasRxId;
        public uint InputSeq;
        public bool HasInputSeq;
        public int QueueDepthAfter;
        public Bug1PacketType PacketType;
    }

    /// <summary>
    /// §3.2 三个接收/消费相位（RX/AFTER_ENQUEUE、RX_REJECT、CONSUME/AFTER_DEQUEUE）的字段片段。
    /// 纯字符串构造，不碰 Unity 对象，可离线用例覆盖。
    /// </summary>
    internal static class Bug1PhaseJson
    {
        private static string Str(ulong value) => "\"" + value.ToString(CultureInfo.InvariantCulture) + "\"";

        private static string Num(uint value) => value.ToString(CultureInfo.InvariantCulture);

        private static string Num(int value) => value.ToString(CultureInfo.InvariantCulture);

        /// <summary>spec §3.2：packet_type 的枚举字面量为 WALK/DRIVE/UNKNOWN。</summary>
        private static string Token(Bug1PacketType packetType)
        {
            switch (packetType)
            {
                case Bug1PacketType.Walk: return "WALK";
                case Bug1PacketType.Drive: return "DRIVE";
                default: return "UNKNOWN";
            }
        }

        /// <summary>
        /// spec §3.1：unavailable 是 map&lt;string,string&gt;（字段→原因），不是字段名数组；
        /// 与 manifest 同名字段保持同一形状。只写一次，无缺项时不出现。
        /// </summary>
        private static void AppendUnavailable(StringBuilder sb, params string[] entries)
        {
            int count = 0;
            for (int i = 0; i < entries.Length; i++) if (!string.IsNullOrEmpty(entries[i])) count++;
            if (count == 0) return;
            sb.Append(",\"unavailable\":{");
            bool first = true;
            for (int i = 0; i < entries.Length; i++)
            {
                if (string.IsNullOrEmpty(entries[i])) continue;
                if (!first) sb.Append(',');
                sb.Append(entries[i]);
                first = false;
            }
            sb.Append('}');
        }

        /// <summary>unavailable 的一个条目：字段名 → 可复核的缺失原因（不是占位符）。</summary>
        private static string Missing(string field, string reason)
            => "\"" + field + "\":\"" + reason + "\"";

        /// <summary>RX/AFTER_ENQUEUE：rx_id 与 inputseq 各自独立可缺，不互相冒充。</summary>
        public static string Enqueue(ulong rxId, bool hasRxId, uint inputSeq, bool hasInputSeq,
            Bug1PacketType packetType, int? depthBefore, int depthAfter, bool seqReuse)
        {
            var sb = new StringBuilder(224);
            sb.Append("\"rx_id\":").Append(hasRxId ? Str(rxId) : "null");
            sb.Append(",\"inputseq\":").Append(hasInputSeq ? Num(inputSeq) : "null");
            sb.Append(",\"packet_type\":\"").Append(Token(packetType)).Append('"');
            sb.Append(",\"queue_depth_before\":").Append(depthBefore.HasValue ? Num(depthBefore.Value) : "null");
            sb.Append(",\"queue_depth_after\":").Append(Num(depthAfter));
            // F5：同一 inputseq 再次出现时映射被新 rx 覆盖，必须可观测，不许静默顶掉旧包。
            if (seqReuse) sb.Append(",\"rx_seq_reuse\":true");
            AppendUnavailable(sb,
                hasRxId ? null : Missing("rx_id", "inputseq_unavailable"),
                hasInputSeq ? null : Missing("inputseq", "client_simulation_frame_number_unreadable"),
                depthBefore.HasValue ? null : Missing("queue_depth_before", "no_receive_bracket_observed"));
            return sb.ToString();
        }

        /// <summary>
        /// RX/ENQUEUE_GUARD_REJECTED：真实 Enqueue 已发生，但前后深度不构成一次干净入队
        /// （例如同一次调用入队多包）。这既不是"成功入队"，也不能记成"拒绝"：
        /// 产出 invalid 记录让下游读成 UNKNOWN，并计入 GuardRejected，绝不静默丢样本。
        /// </summary>
        public static string EnqueueGuardRejected(uint? inputSeq, Bug1PacketType packetType,
            int? depthBefore, int depthAfter)
        {
            bool hasSeq = inputSeq.HasValue;
            var sb = new StringBuilder(256);
            sb.Append("\"guard\":\"enqueue_depth_discontinuity\"");
            sb.Append(",\"rx_id\":null");
            sb.Append(",\"inputseq\":").Append(hasSeq ? Num(inputSeq.Value) : "null");
            sb.Append(",\"packet_type\":\"").Append(Token(packetType)).Append('"');
            sb.Append(",\"queue_depth_before\":").Append(depthBefore.HasValue ? Num(depthBefore.Value) : "null");
            sb.Append(",\"queue_depth_after\":").Append(Num(depthAfter));
            AppendUnavailable(sb,
                hasSeq ? null : Missing("inputseq", "client_simulation_frame_number_unreadable"),
                Missing("rx_id", "enqueue_guard_rejected"),
                depthBefore.HasValue ? null : Missing("queue_depth_before", "no_receive_bracket_observed"));
            return sb.ToString();
        }

        /// <summary>
        /// RX_REJECT：本次调用没有发生真实入队。rx_id/inputseq 结构上不可得（写 null 并列入 unavailable），
        /// 拒绝原因只在原生可直接判定时给值，否则 null + unavailable，不补读 reader。
        /// </summary>
        public static string Reject(string rejectReason, int? depthBefore, int? depthAfter)
        {
            bool hasReason = !string.IsNullOrEmpty(rejectReason);
            var sb = new StringBuilder(192);
            sb.Append("\"reject_reason\":").Append(hasReason ? "\"" + Escape(rejectReason) + "\"" : "null");
            sb.Append(",\"rx_id\":null");
            sb.Append(",\"inputseq\":null");
            sb.Append(",\"packet_type\":\"UNKNOWN\"");
            sb.Append(",\"queue_depth_before\":").Append(depthBefore.HasValue ? Num(depthBefore.Value) : "null");
            sb.Append(",\"queue_depth_after\":").Append(depthAfter.HasValue ? Num(depthAfter.Value) : "null");
            AppendUnavailable(sb,
                hasReason ? null : Missing("reject_reason", "native_reject_branch_unresolved"),
                Missing("rx_id", "no_enqueue_observed"),
                Missing("inputseq", "no_enqueue_observed"),
                depthBefore.HasValue ? null : Missing("queue_depth_before", "receive_prefix_depth_unavailable"),
                depthAfter.HasValue ? null : Missing("queue_depth_after", "receive_postfix_depth_unavailable"));
            return sb.ToString();
        }

        /// <summary>
        /// CONSUME/AFTER_DEQUEUE：rx_id 只能来自本次真实出队包的 enqueue 记录，
        /// 查不到写 null + unavailable，不用最近收到的包顶替。
        /// 出队前深度没有独立接缝可读，写 null + unavailable，不用 after+1 反推。
        /// F1：出队时无法确定"消费本包的那次 simulate"（它随后才发生），
        /// sim_call_id 一律写 null + unavailable，绝不用上一次 simulate 顶替。
        /// </summary>
        public static string Dequeue(string consumeId, ulong rxId, bool hasRxId,
            uint inputSeq, bool hasInputSeq, Bug1PacketType packetType, int depthAfter)
        {
            var sb = new StringBuilder(256);
            sb.Append("\"consume_id\":\"").Append(Escape(consumeId)).Append('"');
            sb.Append(",\"sim_call_id\":null");
            sb.Append(",\"rx_id\":").Append(hasRxId ? Str(rxId) : "null");
            sb.Append(",\"inputseq\":").Append(hasInputSeq ? Num(inputSeq) : "null");
            sb.Append(",\"packet_type\":\"").Append(Token(packetType)).Append('"');
            sb.Append(",\"queue_depth_before\":null");
            sb.Append(",\"queue_depth_after\":").Append(Num(depthAfter));
            AppendUnavailable(sb,
                Missing("sim_call_id", "consumer_simulate_not_determinable_at_dequeue"),
                hasRxId ? null : Missing("rx_id", "rx_link_not_found_for_this_inputseq"),
                hasInputSeq ? null : Missing("inputseq", "client_simulation_frame_number_unreadable"),
                Missing("queue_depth_before", "no_pre_dequeue_seam"));
            return sb.ToString();
        }

        internal static string Escape(string value)
            => string.IsNullOrEmpty(value) ? string.Empty : value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    /// <summary>
    /// §3.2 的接收/消费相位状态机：rx_id 是由原生 inputseq 分配的本地接收号，
    /// 用有界 FIFO side table 关联原包，不保留整包历史、不再次解析 reader。
    /// 纯托管状态，不引用 Unity 类型，便于离线用例与变异验证。
    /// </summary>
    internal sealed class Bug1RxPhaseLedger
    {
        private readonly Dictionary<uint, ulong> _rxByInputSeq = new Dictionary<uint, ulong>();
        // 队列项必须带 rx_id：同一 inputseq 被复用后，旧项不得删掉新映射。
        private readonly Queue<KeyValuePair<uint, ulong>> _rxOrder = new Queue<KeyValuePair<uint, ulong>>();
        private readonly int _capacity;
        private ulong _nextRxId;
        private long _consumeSeq;
        private long _guardRejected;
        private long _evicted;
        private long _superseded;
        private bool _receiveActive;
        private int _receiveDepthBefore = -1;
        private bool _receiveEnqueued;
        private Bug1ConsumeRecord _pending;
        private bool _hasPending;

        public Bug1RxPhaseLedger() : this(Bug1ProbeConstants.MaxRxTableEntries) { }

        internal Bug1RxPhaseLedger(int capacity)
        {
            _capacity = capacity > 0 ? capacity : 1;
        }

        public Bug1ConsumeRecord Last { get; private set; }

        public bool HasLast { get; private set; }

        public bool HasPending => _hasPending;

        public Bug1ConsumeRecord Pending => _pending;

        /// <summary>深度守卫拒绝次数：仪表故障可观测，不静默丢样本（RUN_END 暴露）。</summary>
        public long GuardRejected => _guardRejected;

        /// <summary>FIFO 容量淘汰次数：用于区分"被容量挤掉"与"探针晚启动/包从未见过"。</summary>
        public long EvictedRxEntries => _evicted;

        /// <summary>同一 inputseq 重复出现并覆盖旧 rx 映射的次数（不静默）。</summary>
        public long SupersededRxEntries => _superseded;

        public void Reset()
        {
            _guardRejected = 0L;
            _evicted = 0L;
            _superseded = 0L;
            _rxByInputSeq.Clear();
            _rxOrder.Clear();
            _nextRxId = 0UL;
            _consumeSeq = 0L;
            _receiveActive = false;
            _receiveDepthBefore = -1;
            _receiveEnqueued = false;
            _pending = default(Bug1ConsumeRecord);
            _hasPending = false;
            Last = default(Bug1ConsumeRecord);
            HasLast = false;
        }

        /// <summary>RX 入口前缀已进入：只记队列深度，不读 NetPakReader。</summary>
        public void BeginReceive(int depthBefore)
        {
            _receiveActive = true;
            _receiveEnqueued = false;
            _receiveDepthBefore = depthBefore;
        }

        /// <summary>本次 ReceiveInputs 已观察到的入队前深度；没观察到返回 null，绝不按 after-1 反推。</summary>
        public int? ObservedDepthBefore
            => _receiveActive && _receiveDepthBefore >= 0 ? (int?)_receiveDepthBefore : null;

        /// <summary>
        /// 入口后缀结束：本次调用发生过真实 Enqueue 就返回 null（与 AFTER_ENQUEUE 互斥，
        /// 规格禁止用入口 Postfix 冒充入队）。没入队才产出 RX_REJECT。
        /// </summary>
        public string CompleteReceive(string rejectReason, int depthAfter)
        {
            bool active = _receiveActive;
            bool enqueued = _receiveEnqueued;
            _receiveActive = false;
            _receiveEnqueued = false;
            if (!active || enqueued) return null;
            int? before = _receiveDepthBefore >= 0 ? (int?)_receiveDepthBefore : null;
            int? after = depthAfter >= 0 ? (int?)depthAfter : null;
            return Bug1PhaseJson.Reject(rejectReason, before, after);
        }

        /// <summary>
        /// RX/AFTER_ENQUEUE：只有前后深度恰好差 1（一次干净入队）才产出成功记录。
        /// 守卫不通过时产出 invalid 的 ENQUEUE_GUARD_REJECTED 记录并计数，绝不静默丢弃；
        /// 且"本次调用出现过入队"先于守卫置位——真实入队但守卫未过不得再被记成 RX_REJECT。
        /// </summary>
        public string OnEnqueue(uint? inputSeq, Bug1PacketType packetType, int? observedDepthBefore, int depthAfter,
            out bool guardRejected)
        {
            // 语义：本次 ReceiveInputs 内出现过真实 Enqueue 尝试（原生已入队才会走到这里）。
            _receiveEnqueued = true;
            guardRejected = observedDepthBefore.HasValue && depthAfter != observedDepthBefore.Value + 1;
            if (guardRejected)
            {
                _guardRejected++;
                return Bug1PhaseJson.EnqueueGuardRejected(inputSeq, packetType, observedDepthBefore, depthAfter);
            }
            bool hasSeq = inputSeq.HasValue;
            bool reused = false;
            ulong rxId = hasSeq ? Allocate(inputSeq.Value, out reused) : 0UL;
            return Bug1PhaseJson.Enqueue(rxId, hasSeq, inputSeq ?? 0u, hasSeq, packetType,
                observedDepthBefore, depthAfter, reused);
        }

        /// <summary>
        /// CONSUME/AFTER_DEQUEUE：用本次真实出队包的 inputseq 查 rx 表；查不到写 null，
        /// 不拿最近收到的包顶替。返回的片段同时写入 Last/Pending 供 SIM 与 FRAME 关联。
        /// </summary>
        public string OnDequeue(uint? inputSeq, Bug1PacketType packetType, int depthAfter)
        {
            bool hasSeq = inputSeq.HasValue;
            ulong rxId = 0UL;
            bool hasRx = hasSeq && _rxByInputSeq.TryGetValue(inputSeq.Value, out rxId);
            if (!hasRx) rxId = 0UL;
            var record = new Bug1ConsumeRecord
            {
                RxId = rxId,
                HasRxId = hasRx,
                InputSeq = inputSeq ?? 0u,
                HasInputSeq = hasSeq,
                QueueDepthAfter = depthAfter,
                PacketType = packetType,
            };
            _pending = record;
            _hasPending = true;
            Last = record;
            HasLast = true;
            string consumeId = "consume-" + (++_consumeSeq).ToString(CultureInfo.InvariantCulture);
            return Bug1PhaseJson.Dequeue(consumeId, rxId, hasRx,
                inputSeq ?? 0u, hasSeq, packetType, depthAfter);
        }

        /// <summary>SIM 入口把本次消费关联一次性取走，同一消费不会被两次 simulate 复用。</summary>
        public bool TakePending(out Bug1ConsumeRecord record)
        {
            record = _pending;
            bool has = _hasPending;
            _hasPending = false;
            return has;
        }

        /// <summary>
        /// 本地接收号：按 inputseq 分配，FIFO 有界淘汰，不保留整包历史。
        /// 同一 inputseq 再次出现：映射指向新 rx 并计数（reused），旧队列项不会误删新映射。
        /// </summary>
        private ulong Allocate(uint inputSeq, out bool reused)
        {
            ulong rx = ++_nextRxId;
            reused = _rxByInputSeq.ContainsKey(inputSeq);
            if (reused) _superseded++;
            _rxByInputSeq[inputSeq] = rx;
            _rxOrder.Enqueue(new KeyValuePair<uint, ulong>(inputSeq, rx));
            while (_rxOrder.Count > _capacity)
            {
                KeyValuePair<uint, ulong> oldest = _rxOrder.Dequeue();
                ulong current;
                if (_rxByInputSeq.TryGetValue(oldest.Key, out current) && current == oldest.Value)
                {
                    _rxByInputSeq.Remove(oldest.Key);
                    _evicted++;
                }
            }
            return rx;
        }
    }
}
