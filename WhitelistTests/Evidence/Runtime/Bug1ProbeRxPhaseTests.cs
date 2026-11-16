using SteamP2PFriends.Platform.Diagnostics.Patches;
using SDG.Unturned;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace SteamP2PFriends.WhitelistTests
{
    /// <summary>
    /// T35 / Bug1 仪表：三个接收/消费相位（RX/AFTER_ENQUEUE、RX_REJECT、CONSUME/AFTER_DEQUEUE）
    /// 与 input_seq / rx_id / queue_depth 关联字段的自动化用例。
    /// 只证明"测量能力存在且缺样时如实 UNKNOWN"，不构成 Bug1 根因结论。
    /// </summary>
    internal static class Bug1ProbeRxPhaseTests
    {
        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Bug1Probe: " + message);
        }

        /// <summary>AFTER_ENQUEUE 必须带原生 inputseq、本地 rx_id 与真实前后深度。</summary>
        public static void Test_PhaseEnqueue_RecordsNativeSeqAndDepth()
        {
            var ledger = new Bug1RxPhaseLedger();
            ledger.BeginReceive(2);
            bool guard;
            string json = ledger.OnEnqueue(42u, Bug1PacketType.Walk, ledger.ObservedDepthBefore, 3, out guard);

            Assert(!guard, "干净入队不得被深度守卫判为异常");
            Assert(json != null, "干净入队必须产出 RX/AFTER_ENQUEUE");
            Assert(json.Contains("\"rx_id\":\"1\""), "rx_id 应为本地接收号 1，实际=" + json);
            Assert(json.Contains("\"inputseq\":42"), "inputseq 必须取原生 clientSimulationFrameNumber，实际=" + json);
            Assert(json.Contains("\"packet_type\":\"WALK\""), "packet_type 应按原生包类型判定，实际=" + json);
            Assert(json.Contains("\"queue_depth_before\":2"), "入队前深度必须来自真实队列计数，实际=" + json);
            Assert(json.Contains("\"queue_depth_after\":3"), "入队后深度必须来自真实队列计数，实际=" + json);
            Assert(!json.Contains("\"unavailable\""), "字段全可得时不得出现 unavailable，实际=" + json);
        }

        /// <summary>
        /// 前后深度对不上就不是一次干净入队：不得产出可用的 AFTER_ENQUEUE 样本，
        /// 但也不许静默——必须留下可搜的守卫标记与计数，且同一次调用不得再被记成 RX_REJECT。
        /// </summary>
        public static void Test_PhaseEnqueue_UncleanDepthProducesNothing()
        {
            var ledger = new Bug1RxPhaseLedger();
            ledger.BeginReceive(2);
            bool guard;
            string json = ledger.OnEnqueue(7u, Bug1PacketType.Walk, ledger.ObservedDepthBefore, 6, out guard);

            Assert(guard, "深度不连续必须被守卫识别");
            Assert(ledger.GuardRejected == 1, "守卫拒绝必须计数，实际=" + ledger.GuardRejected);
            Assert(json.Contains("\"guard\":\"enqueue_depth_discontinuity\""), "必须留下可检索的守卫标记，实际=" + json);
            Assert(!json.Contains("\"rx_id\":\"1\""), "守卫拒绝的记录不得冒充已分配 rx_id，实际=" + json);
            Assert(!ledger.HasLast, "未干净入队不得产生消费关联");
            string reject = ledger.CompleteReceive(null, 6);
            Assert(reject == null, "真实入队已发生（只是不干净）时不得再记成 RX_REJECT，实际=" + reject);
        }

        /// <summary>取不到原生序号时写 null + unavailable，绝不编造 rx_id 或内插序号。</summary>
        public static void Test_PhaseEnqueue_MissingSeqStaysNull()
        {
            var ledger = new Bug1RxPhaseLedger();
            ledger.BeginReceive(0);
            bool guard;
            string json = ledger.OnEnqueue(null, Bug1PacketType.Unknown, ledger.ObservedDepthBefore, 1, out guard);

            Assert(!guard, "序号缺失不是深度不连续，不得误判为守卫拒绝");
            Assert(json != null, "入队本身成立时仍应产出相位记录（序号缺失只是字段不可得）");
            Assert(json.Contains("\"inputseq\":null"), "缺序号必须写 null，实际=" + json);
            Assert(json.Contains("\"rx_id\":null"), "没有序号就不许编一个 rx_id，实际=" + json);
            Assert(json.Contains("\"packet_type\":\"UNKNOWN\""), "未识别包类型应为 UNKNOWN，实际=" + json);
            Assert(json.Contains("\"unavailable\":{\"rx_id\":\"inputseq_unavailable\",\"inputseq\":\"client_simulation_frame_number_unreadable\"}"),
                "unavailable 必须是 字段→原因 的 map 且点名 rx_id/inputseq，实际=" + json);
            Assert(json.IndexOf("\"unavailable\"", StringComparison.Ordinal)
                == json.LastIndexOf("\"unavailable\"", StringComparison.Ordinal),
                "unavailable 键只能出现一次，实际=" + json);
        }

        /// <summary>RX_REJECT 只在本调用没有任何真实入队时产出，与 AFTER_ENQUEUE 结构互斥。</summary>
        public static void Test_PhaseReject_OnlyWhenNoEnqueue()
        {
            var ledger = new Bug1RxPhaseLedger();
            ledger.BeginReceive(4);
            string reject = ledger.CompleteReceive("dismissed", 4);

            Assert(reject != null, "未入队的 ReceiveInputs 调用必须产出 RX_REJECT");
            Assert(reject.Contains("\"reject_reason\":\"dismissed\""), "原生可判定的拒绝原因应原样记录，实际=" + reject);
            Assert(reject.Contains("\"rx_id\":null"), "拒绝不得带 rx_id，实际=" + reject);
            Assert(reject.Contains("\"inputseq\":null"), "拒绝不得带 inputseq，实际=" + reject);
            Assert(reject.Contains("\"queue_depth_before\":4") && reject.Contains("\"queue_depth_after\":4"),
                "拒绝也必须记录原生队列深度，实际=" + reject);

            ledger.BeginReceive(1);
            bool guard;
            ledger.OnEnqueue(5u, Bug1PacketType.Walk, ledger.ObservedDepthBefore, 2, out guard);
            Assert(!guard, "该调用应是一次干净入队");
            string none = ledger.CompleteReceive(null, 2);
            Assert(none == null, "已真实入队的调用不得再产出 RX_REJECT（两相位互斥），实际=" + none);

            string stray = ledger.CompleteReceive(null, 3);
            Assert(stray == null, "没有入口前缀观察时不得凭空产出 RX_REJECT，实际=" + stray);

            var ledger2 = new Bug1RxPhaseLedger();
            ledger2.BeginReceive(0);
            string noReason = ledger2.CompleteReceive(null, 0);
            Assert(noReason != null && noReason.Contains("\"reject_reason\":null"),
                "原因不可得时写 null 而不是编造，实际=" + noReason);
            Assert(noReason.Contains("\"reject_reason\"") && noReason.Contains("\"rx_id\""),
                "拒绝记录仍需列出字段结构，实际=" + noReason);
        }

        /// <summary>CONSUME 的 rx_id 必须来自本次真实出队包在校验表中登记的那一条。</summary>
        public static void Test_PhaseConsume_LinksRxFromDequeuedPacket()
        {
            var ledger = new Bug1RxPhaseLedger();
            ledger.BeginReceive(0);
            bool guard;
            ledger.OnEnqueue(9u, Bug1PacketType.Drive, ledger.ObservedDepthBefore, 1, out guard);
            Assert(!guard, "该入队应干净通过守卫");
            string json = ledger.OnDequeue(9u, Bug1PacketType.Drive, 0);

            Assert(json.Contains("\"rx_id\":\"1\""), "rx_id 必须与 enqueue 时分配的一致，实际=" + json);
            Assert(json.Contains("\"inputseq\":9"), "inputseq 必须取自被出队的那个包，实际=" + json);
            Assert(json.Contains("\"consume_id\":\"consume-1\""), "每次消费必须有唯一 consume_id，实际=" + json);
            Assert(json.Contains("\"sim_call_id\":null"),
                "出队时无法确定随后那次 simulate，必须 null 而不是填上一次 simulate，实际=" + json);
            Assert(json.Contains("\"unavailable\":{\"sim_call_id\":\"consumer_simulate_not_determinable_at_dequeue\""),
                "sim_call_id 为 null 必须附 unavailable 原因，实际=" + json);
            Assert(json.Contains("\"queue_depth_after\":0"), "出队后深度必须来自真实队列计数，实际=" + json);
            Assert(json.Contains("\"queue_depth_before\":null"),
                "出队前深度没有独立接缝，必须 null 而不是 after+1 反推，实际=" + json);
            Assert(ledger.HasLast && ledger.Last.HasRxId && ledger.Last.RxId == 1UL,
                "消费关联必须同时进入 Last 供 FRAME 采样使用");

            Bug1ConsumeRecord taken;
            Assert(ledger.TakePending(out taken) && taken.HasRxId && taken.RxId == 1UL,
                "SIM 入口应能取到本次消费关联");
            Assert(!ledger.TakePending(out taken), "同一消费关联只能被一次 simulate 取走");
        }

        /// <summary>出队包从未登记过序号时，不得借用最近一包的 rx_id 顶替。</summary>
        public static void Test_PhaseConsume_UnknownSeqNoSubstitution()
        {
            var ledger = new Bug1RxPhaseLedger();
            ledger.BeginReceive(0);
            bool guard;
            ledger.OnEnqueue(9u, Bug1PacketType.Walk, ledger.ObservedDepthBefore, 1, out guard);
            string json = ledger.OnDequeue(99u, Bug1PacketType.Walk, 0);

            Assert(json.Contains("\"rx_id\":null"), "未登记过的序号不得借用最近一包的 rx_id，实际=" + json);
            Assert(json.Contains("\"inputseq\":99"), "序号本身仍应如实记录，实际=" + json);
            Assert(json.Contains("\"rx_id\":\"rx_link_not_found_for_this_inputseq\""),
                "unavailable 必须点名 rx_id 及其原因，实际=" + json);
            Assert(json.Contains("\"queue_depth_before\":\"no_pre_dequeue_seam\""),
                "unavailable 必须点名 queue_depth_before 及其原因，实际=" + json);
            Assert(!ledger.Last.HasRxId, "关联失败必须体现为 HasRxId=false，不能被默认值掩盖");
        }

        /// <summary>
        /// 生产接线契约（R6-01）：六个 Harmony hook 必须真的调进 Runtime 的相位入口。
        /// 历史缺陷是删掉 Postfix_Enqueue / Postfix_Dequeue 里的两处调用后全量仍全绿，
        /// 这条用例就是用来在那种情况下变红的。
        /// </summary>
        public static void Test_ProductionWiring_FourPhaseEdgesAreCalled()
        {
            string runtimeName = typeof(Bug1ProbeRuntime).FullName;
            string[] hooks = { "Prefix_ReceiveInputs", "Postfix_ReceiveInputs", "Postfix_Enqueue", "Postfix_Dequeue" };
            string[] entries = { "OnReceiveInputsBefore", "OnReceiveInputsAfter", "OnEnqueue", "OnDequeue" };
            var flags = BindingFlags.Static | BindingFlags.NonPublic;
            for (int i = 0; i < hooks.Length; i++)
            {
                MethodInfo hook = typeof(Bug1ProbePatch).GetMethod(hooks[i], flags);
                Assert(hook != null, "缺少 Harmony hook: " + hooks[i]);
                int edges = IlContractProbe.CountMethodCalls(hook, runtimeName, entries[i]);
                Assert(edges >= 1, hooks[i] + " 未调用 Bug1ProbeRuntime." + entries[i] + "，实际边数=" + edges);
            }

            Assert(Bug1ProbePatch.ResolveTargets(), "六个原生目标必须在本机可解析");
            Assert(Bug1ProbePatch.EnqueueTarget != null
                && Bug1ProbePatch.EnqueueTarget.DeclaringType == typeof(Queue<PlayerInputPacket>),
                "EnqueueTarget 必须落在 Queue<PlayerInputPacket>，实际=" + Describe(Bug1ProbePatch.EnqueueTarget));
            Assert(Bug1ProbePatch.DequeueTarget != null
                && Bug1ProbePatch.DequeueTarget.DeclaringType == typeof(Queue<PlayerInputPacket>),
                "DequeueTarget 必须落在 Queue<PlayerInputPacket>，实际=" + Describe(Bug1ProbePatch.DequeueTarget));
        }

        /// <summary>
        /// 穿过生产入口的用例：真实 Queue 与真实 PlayerInputPacket，只赋引用、不构造 Unity 对象。
        /// 断言目标队列两相位都产出，且非目标队列即使 Enqueue 也不产样。
        /// </summary>
        public static void Test_RuntimeWiring_TargetQueueProducesBothPhases()
        {
            var queue = new Queue<PlayerInputPacket>();
            var foreign = new Queue<PlayerInputPacket>();
            var packet = new PlayerInputPacket();
            FieldInfo seq = typeof(PlayerInputPacket).GetField("clientSimulationFrameNumber",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert(seq != null, "原生序号字段 clientSimulationFrameNumber 必须存在");
            seq.SetValue(packet, 77u);
            var config = new Bug1ProbeConfig
            {
                RunId = "t42-run",
                StepId = "t42-step",
                TargetSteamId = 1UL,
                OutputRoot = null,
                NativeSpeedCapUS = 5.0,
                SpeedCapKnown = true,
            };
            try
            {
                Bug1ProbeRuntime.__TestEnable(null, queue, seq, new Bug1StepWindow(), config);
                queue.Enqueue(packet);
                Bug1ProbeRuntime.OnEnqueue(queue, packet);
                foreign.Enqueue(packet);
                Bug1ProbeRuntime.OnEnqueue(foreign, packet);
                PlayerInputPacket dequeued = queue.Dequeue();
                Bug1ProbeRuntime.OnDequeue(queue, dequeued);
                // R7-03 出队侧负控：非目标队列（foreign）的真实出队也必须被过滤，与入队侧对称。
                PlayerInputPacket foreignDequeued = foreign.Dequeue();
                Bug1ProbeRuntime.OnDequeue(foreign, foreignDequeued);

                string[] trace = Bug1ProbeRuntime.__TestTrace();
                Assert(trace.Length == 2, "目标队列应恰好产出 2 条相位记录（非目标队列必须被过滤），实际=" + trace.Length
                    + " enabled=" + Bug1ProbeRuntime.Enabled + " last=" + Bug1ProbeRuntime.LastError);
                Assert(trace[0].Contains("\"phase\":\"AFTER_ENQUEUE\"") && trace[0].Contains("\"inputseq\":77"),
                    "第一条必须是 RX/AFTER_ENQUEUE 且带原生序号，实际=" + trace[0]);
                Assert(trace[1].Contains("\"phase\":\"AFTER_DEQUEUE\"") && trace[1].Contains("\"rx_id\":\"1\""),
                    "第二条必须是 CONSUME/AFTER_DEQUEUE 且关联入队时的 rx_id，实际=" + trace[1]);
                // R7-03 三行断言：出队侧负控不得追加任何记录，也不得改变运行时状态。
                Assert(Array.FindAll(trace, line => line.Contains("AFTER_DEQUEUE")).Length == 1,
                    "非目标队列出队不得追加 CONSUME 记录，实际 AFTER_DEQUEUE 行数=" +
                    Array.FindAll(trace, line => line.Contains("AFTER_DEQUEUE")).Length);
                Assert(trace.Length == 2, "出队负控后总记录数必须仍是 2，实际=" + trace.Length);
                Assert(Bug1ProbeRuntime.Enabled, "过滤不得影响运行时状态，enabled 应仍为 true");
            }
            finally { Bug1ProbeRuntime.__TestDisable(); }
        }

        /// <summary>R7-03：出队侧非目标队列必须被过滤——即使发生真实出队，也不得产出 CONSUME 记录。</summary>
        public static void Test_RuntimeWiring_ForeignDequeueProducesNothing()
        {
            var target = new Queue<PlayerInputPacket>();
            var foreign = new Queue<PlayerInputPacket>();
            var packet = new PlayerInputPacket();
            FieldInfo seq = typeof(PlayerInputPacket).GetField("clientSimulationFrameNumber",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert(seq != null, "原生序号字段 clientSimulationFrameNumber 必须存在");
            seq.SetValue(packet, 11u);
            var config = new Bug1ProbeConfig
            {
                RunId = "r7-run",
                StepId = "r7-step",
                TargetSteamId = 1UL,
                OutputRoot = null,
                NativeSpeedCapUS = 5.0,
                SpeedCapKnown = true,
            };
            try
            {
                Bug1ProbeRuntime.__TestEnable(null, target, seq, new Bug1StepWindow(), config);
                foreign.Enqueue(packet);
                PlayerInputPacket got = foreign.Dequeue();
                Bug1ProbeRuntime.OnDequeue(foreign, got);

                string[] trace = Bug1ProbeRuntime.__TestTrace();
                Assert(trace.Length == 0, "非目标队列出队不得产出任何记录，实际=" + trace.Length);
            }
            finally { Bug1ProbeRuntime.__TestDisable(); }
        }

        /// <summary>有界 rx 表（512）淘汰必须计数，被淘汰的序号不得再关联到 rx_id。</summary>
        public static void Test_Ledger_CapacityEvictionIsCounted()
        {
            var ledger = new Bug1RxPhaseLedger(2);
            bool guard;
            ledger.BeginReceive(0);
            ledger.OnEnqueue(1u, Bug1PacketType.Walk, ledger.ObservedDepthBefore, 1, out guard);
            ledger.BeginReceive(1);
            ledger.OnEnqueue(2u, Bug1PacketType.Walk, ledger.ObservedDepthBefore, 2, out guard);
            ledger.BeginReceive(2);
            ledger.OnEnqueue(3u, Bug1PacketType.Walk, ledger.ObservedDepthBefore, 3, out guard);

            Assert(ledger.EvictedRxEntries == 1, "容量 2 时第三次入队必须淘汰 1 条，实际=" + ledger.EvictedRxEntries);
            string old = ledger.OnDequeue(1u, Bug1PacketType.Walk, 2);
            Assert(old.Contains("\"rx_id\":null"), "已淘汰的 inputseq 不得再关联 rx_id，实际=" + old);
        }

        /// <summary>同一 inputseq 重复出现时映射被覆盖必须可观测（F5），不许静默顶掉旧包。</summary>
        public static void Test_Ledger_DuplicateInputSeqIsObservable()
        {
            var ledger = new Bug1RxPhaseLedger();
            bool guard;
            ledger.BeginReceive(0);
            ledger.OnEnqueue(5u, Bug1PacketType.Walk, ledger.ObservedDepthBefore, 1, out guard);
            ledger.BeginReceive(1);
            string second = ledger.OnEnqueue(5u, Bug1PacketType.Walk, ledger.ObservedDepthBefore, 2, out guard);

            Assert(ledger.SupersededRxEntries == 1, "重复 inputseq 必须计入 superseded，实际=" + ledger.SupersededRxEntries);
            Assert(second.Contains("\"rx_seq_reuse\":true"), "重复序号必须在记录上显式标记，实际=" + second);
        }

        private static string Describe(MethodInfo method)
            => method == null ? "null" : method.DeclaringType.FullName + "." + method.Name;

        /// <summary>
        /// manifest 的 game_mvid / spf_assembly_sha256 必须是运行时真值：
        /// 前者等于游戏程序集 ManifestModule.ModuleVersionId，后者等于 SPF 自身 DLL 的现算 SHA256。
        /// 任何一项取不到允许为 null，但必须有 unavailable 原因，绝不允许占位字符串。
        /// </summary>
        public static void Test_Manifest_RuntimeIdentityIsReal()
        {
            string json = Bug1ProbeRuntime.BuildEnvironmentManifestFields();

            Assert(json != null && json.Length > 0, "manifest 字段不得为空");
            Assert(!json.Contains("UNRESOLVED"), "manifest 不得残留 UNRESOLVED 占位，实际=" + json);

            string mvid = ExtractString(json, "game_mvid");
            if (mvid == null)
            {
                Assert(json.Contains("\"game_mvid\":\"game-assembly-unavailable")
                    || json.Contains("\"game_mvid\":\"module-version-id-empty\""),
                    "game_mvid 取不到时必须写明 unavailable 原因，实际=" + json);
            }
            else
            {
                Guid parsed;
                Assert(Guid.TryParse(mvid, out parsed) && parsed != Guid.Empty,
                    "game_mvid 必须是有效 MVID，实际=" + mvid);
                string expected = typeof(SDG.Unturned.PlayerInput).Assembly.ManifestModule.ModuleVersionId.ToString("D");
                Assert(mvid == expected, "game_mvid 必须等于游戏程序集 MVID，实际=" + mvid + " 期望=" + expected);
            }

            string sha = ExtractString(json, "spf_assembly_sha256");
            if (sha == null)
            {
                Assert(json.Contains("\"spf_assembly_sha256\":\"assembly-location-empty")
                    || json.Contains("\"spf_assembly_sha256\":\"assembly-hash-failed"),
                    "spf_assembly_sha256 取不到时必须写明 unavailable 原因，实际=" + json);
            }
            else
            {
                Assert(sha.Length == 64, "SHA256 必须是 64 位十六进制，实际=" + sha);
                for (int i = 0; i < sha.Length; i++)
                {
                    char c = sha[i];
                    Assert((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'), "SHA256 必须小写十六进制，实际=" + sha);
                }
                string actual = HashFile(typeof(Bug1ProbeRuntime).Assembly.Location);
                Assert(sha == actual, "spf_assembly_sha256 必须等于运行时程序集真实哈希，实际=" + sha + " 现算=" + actual);
            }
        }

        private static string ExtractString(string json, string key)
        {
            string marker = "\"" + key + "\":\"";
            int start = json.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0) return null;
            start += marker.Length;
            int end = json.IndexOf('"', start);
            if (end < 0) return null;
            return json.Substring(start, end - start);
        }

        private static string HashFile(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
            {
                byte[] hash = sha.ComputeHash(stream);
                var sb = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }
    }
}
