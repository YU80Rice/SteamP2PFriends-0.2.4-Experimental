using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using SDG.Unturned;
using SteamP2PFriends.Platform.Diagnostics.Patches;

namespace SteamP2PFriends.WhitelistTests
{
    /// <summary>
    /// t60：窗口收口 / 缺相位归因 / 输入关联可解释 / 远端插值观测。
    /// 全部用例只依赖纯函数与离线测试缝，不需要 Unity 运行时。
    /// </summary>
    internal static class Bug1ProbeT60Tests
    {
        private const long Frequency = 10000000L;

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Bug1ProbeT60: " + message);
        }

        private static Bug1FrameSample Frame(int frame, bool before, bool render, double x = 0.0)
        {
            return new Bug1FrameSample
            {
                Frame = frame,
                MonoTicks = (long)frame * (Frequency / 75),
                MonoFrequency = Frequency,
                HasBeforeUpdate = before,
                HasRenderReady = render,
                CameraIsMain = true,
                ThirdCarrierVerified = true,
                RendererVerified = true,
                ModelEndpointResolved = true,
                APos = new Bug1Vec3(x, 0.0, 0.0),
                VPos = new Bug1Vec3(x, 0.0, 0.0),
                AYaw = 0.0,
                VYaw = 0.0,
            };
        }

        private static Bug1StepWindow Window(params Bug1FrameSample[] frames)
        {
            var w = new Bug1StepWindow
            {
                StepId = "t60-step",
                NativeSpeedCapUS = 4.0,
                SpeedCapKnown = true,
                FixedConditionsValid = true,
            };
            foreach (Bug1FrameSample f in frames) w.Frames.Add(f);
            if (frames.Length > 0)
            {
                w.StepFirstFrame = frames[0].Frame;
                w.StepLastFrame = frames[frames.Length - 1].Frame;
            }
            return w;
        }

        // T79：三处导出（RUN_END / manifest.json / result.json）必须同字段集，缺一即假绿。
        private static readonly string[] T79AggregationFields =
        {
            "event_aggregation_revision",
            "selected_event_complete",
            "event_scan_complete",
            "strict_m1_events_is_lower_bound",
            "right_censored_segments",
            "right_censored_ranges",
            "closed_events",
        };

        // t83：A/V 分离且关联字段齐全的帧，用来构造「早段闭合 M1 + 完整静止尾段」。
        private static Bug1FrameSample Divergent(int frame, double ax, double vx)
        {
            Bug1FrameSample sample = Frame(frame, true, true);
            sample.APos = new Bug1Vec3(ax, 0.0, 0.0);
            sample.ABeforeUpdate = new Bug1Vec3(ax, 0.0, 0.0);
            sample.VPos = new Bug1Vec3(vx, 0.0, 0.0);
            sample.HasLastInputSeq = true;
            sample.LastInputSeq = (uint)(frame + 1);
            sample.HasLastRxId = true;
            sample.LastRxId = (ulong)(frame + 1);
            sample.HasQueueDepth = true;
            sample.QueueDepth = 1;
            return sample;
        }

        private static bool ExtractBool(string text, string marker)
        {
            int i = text.IndexOf(marker, StringComparison.Ordinal);
            if (i < 0) throw new InvalidOperationException("Bug1ProbeT60: 未找到 " + marker);
            i += marker.Length;
            while (i < text.Length && (text[i] == ' ' || text[i] == '"')) i++;
            return string.CompareOrdinal(text, i, "true", 0, 4) == 0;
        }

        // Bug1-55：两端半帧必须被裁掉，且各自计数；内部半帧不算越界。
        public static void Test_T60_WindowBoundaryTrimsHeadAndTail()
        {
            Bug1StepWindow w = Window(
                Frame(100, false, true),
                Frame(101, false, true),
                Frame(102, true, true),
                Frame(103, true, true),
                Frame(104, true, true),
                Frame(105, true, false),
                Frame(106, true, false),
                Frame(107, true, false));
            // t65：裁剪只在「合法边界」下发生；这里显式给 target_left + 目标已销毁。
            var ctx = new Bug1WindowBoundaryContext
            {
                EndReason = "target_left",
                TargetAlive = false,
                HookFailClosed = false,
            };
            Bug1WindowBounds bounds = Bug1ProbeWindowBoundary.Finalize(w, ctx);
            Assert(bounds.HeadExcluded == 2, "head excluded 应为 2，实际 " + bounds.HeadExcluded);
            Assert(bounds.TailExcluded == 3, "tail excluded 应为 3，实际 " + bounds.TailExcluded);
            Assert(bounds.TailCandidateCount == 3, "尾部候选数应为 3，实际 " + bounds.TailCandidateCount);
            Assert(w.Frames.Count == 3, "收口后应剩 3 帧，实际 " + w.Frames.Count);
            Assert(w.Frames[0].Frame == 102 && w.Frames[2].Frame == 104, "收口后帧区间应为 102..104");
            Assert(bounds.InteriorPartial == 0, "内部半帧应为 0，实际 " + bounds.InteriorPartial);
            Assert(bounds.ExcludedReason.Contains(Bug1WindowBounds.ReasonHeadPartial), "原因应含 HEAD_PARTIAL：" + bounds.ExcludedReason);
            Assert(bounds.ExcludedReason.Contains(Bug1WindowBounds.ReasonTailPartial), "原因应含 TAIL_PARTIAL：" + bounds.ExcludedReason);
            Assert(w.StepFirstFrame == 102 && w.StepLastFrame == 104, "StepFirst/LastFrame 必须同步为裁剪后区间");
        }

        // Bug1-56：整窗没有一帧相位齐全时禁止裁剪——否则会把「detour 全灭」洗成干净窗口。
        public static void Test_T60_WindowBoundaryDoesNotTrimWhenNoCompleteFrame()
        {
            Bug1StepWindow w = Window(
                Frame(200, false, true),
                Frame(201, false, true),
                Frame(202, false, false));
            Bug1WindowBounds bounds = Bug1ProbeWindowBoundary.Finalize(w);
            Assert(bounds.HeadExcluded == 0 && bounds.TailExcluded == 0, "无齐全帧时不得裁剪");
            Assert(w.Frames.Count == 3, "无齐全帧时帧数应保持不变，实际 " + w.Frames.Count);
            Assert(bounds.ExcludedReason == Bug1WindowBounds.ReasonNone, "原因应为 NONE，实际 " + bounds.ExcludedReason);

            Bug1Verdict verdict = Bug1ProbeJudge.Judge(w);
            Assert(verdict.Label == Bug1Label.Unknown, "无齐全帧必须判 UNKNOWN");
            Assert(verdict.Reason == "MISSING_PHASE", "原因应为 MISSING_PHASE，实际 " + verdict.Reason);
            // 3 帧各缺 BEFORE_UPDATE + 末帧另缺 RENDER_READY = 4 条真缺，必须精确计数（少一条即说明计数被拆掉）。
            Assert(verdict.PhaseMissingGenuine == 4, "真缺计数应为 4，实际 " + verdict.PhaseMissingGenuine);
        }

        // Bug1-57：内部半帧不是越界——保留并计入 PhaseMissingGenuine，越界计数恒 0。
        public static void Test_T60_InteriorPartialStaysGenuine()
        {
            Bug1StepWindow w = Window(
                Frame(300, true, true, 0.0),
                Frame(301, false, true, 0.1),
                Frame(302, true, false, 0.2),
                Frame(303, true, true, 0.3));
            Bug1WindowBounds bounds = Bug1ProbeWindowBoundary.Finalize(w);
            Assert(bounds.HeadExcluded == 0 && bounds.TailExcluded == 0, "内部半帧不得被当越界裁掉");
            Assert(bounds.InteriorPartial == 2, "内部半帧应为 2，实际 " + bounds.InteriorPartial);
            w.WindowHeadExcluded = bounds.HeadExcluded;
            w.WindowTailExcluded = bounds.TailExcluded;
            w.WindowInteriorPartial = bounds.InteriorPartial;
            w.WindowExcludedReason = bounds.ExcludedReason;
            Bug1Verdict verdict = Bug1ProbeJudge.Judge(w);
            Assert(verdict.Label == Bug1Label.Unknown, "内部缺相位必须判 UNKNOWN");
            Assert(verdict.Reason == "MISSING_PHASE", "原因应为 MISSING_PHASE，实际 " + verdict.Reason);
            Assert(verdict.PhaseMissingGenuine == 2, "真缺计数应为 2，实际 " + verdict.PhaseMissingGenuine);
            Assert(verdict.PhaseMissingOutOfWindow == 0, "越界计数必须恒 0，实际 " + verdict.PhaseMissingOutOfWindow);
            Assert(verdict.WindowInteriorPartial == 2, "verdict 必须带出内部半帧计数");
        }

        // Bug1-58：插值字段必须真算；缺字段显式 unavailable，不得填默认值冒充。
        public static void Test_T60_InterpolationComputesLerpAndUnavailable()
        {
            Bug1InterpolationSample s = Bug1InterpolationSample.Compute(
                true, 100.0, true, 100.04, true, 0.08,
                true, new Bug1Vec3(1, 2, 3), true, new Bug1Vec3(1, 2, 4), true, new Bug1Vec3(1, 2, 3.5));
            Assert(s.HasLerp, "全字段可用时必须算出 lerp");
            Assert(Math.Abs(s.Lerp - 0.5) < 1e-9, "lerp 应为 0.5，实际 " + s.Lerp);
            Assert(s.Unavailable.Count == 0, "全字段可用时 unavailable 应为空");

            Bug1InterpolationSample missing = Bug1InterpolationSample.Compute(
                false, 0.0, true, 100.04, true, 0.08,
                false, default(Bug1Vec3), true, new Bug1Vec3(0, 0, 0), true, new Bug1Vec3(0, 0, 0));
            Assert(!missing.HasTick, "缺 tick 必须标记不可用");
            Assert(!missing.HasLerp, "缺 tick 时不得算 lerp");
            Assert(!missing.HasLastUpdatePos, "缺 lastUpdatePos 必须标记不可用");
            Assert(missing.Unavailable.Contains("remote_input_tick"), "unavailable 必须含 remote_input_tick");
            Assert(missing.Unavailable.Contains("lerp_factor"), "unavailable 必须含 lerp_factor");
            Assert(missing.Unavailable.Contains("last_update_pos"), "unavailable 必须含 last_update_pos");

            Bug1InterpolationSample badRate = Bug1InterpolationSample.Compute(
                true, 100.0, true, 100.04, true, 0.0,
                true, new Bug1Vec3(0, 0, 0), true, new Bug1Vec3(0, 0, 0), true, new Bug1Vec3(0, 0, 0));
            Assert(!badRate.HasRate && badRate.Unavailable.Contains("input_rate"), "rate<=0 必须标 unavailable");
            Assert(!badRate.HasLerp, "rate 非法时不得算 lerp");
        }

        // Bug1-59：关联失配必须给原因码与候选字段，不得只写笼统串。
        public static void Test_T60_InputAssociationReasons()
        {
            Assert(Bug1InputAssociation.Classify(true, true, true, true) == null, "字段齐全时应关联成功");
            Assert(Bug1InputAssociation.Classify(false, false, false, false) == Bug1InputAssociation.ReasonNoConsumeYet, "无消费记录应报 no_consume_yet");
            Assert(Bug1InputAssociation.Classify(true, false, true, true) == Bug1InputAssociation.ReasonMissingInputSeq, "缺 inputseq 原因码不对");
            Assert(Bug1InputAssociation.Classify(true, true, false, true) == Bug1InputAssociation.ReasonMissingRxId, "缺 rx_id 原因码不对");
            Assert(Bug1InputAssociation.Classify(true, true, true, false) == Bug1InputAssociation.ReasonMissingQueueDepth, "缺 queue_depth 原因码不对");
            string json = Bug1InputAssociation.JsonFragment(Bug1InputAssociation.ReasonNoConsumeYet, false, 0UL, false, 0u);
            Assert(json.Contains("\"input_association_rule\":\"last_consume_before_render\""), "必须带关联规则：" + json);
            Assert(json.Contains("\"input_association_reason\":\"no_consume_yet\""), "必须带原因码：" + json);
            Assert(json.Contains("\"candidate_rx_ids\":[]"), "无候选时必须显式空数组：" + json);
        }

        // Bug1-60：生产路径——EndStep 必须在判定前收口，尾巴半帧不得进窗，且 RUN_END 与 result.json 同源。
        public static void Test_T60_EndStepSealsWindowInProductionPath()
        {
            string root = Path.Combine(Path.GetTempPath(), "spf-t60-" + Guid.NewGuid().ToString("N"));
            try
            {
                Bug1ProbeRuntime.__TestOffline(true);
                Bug1ProbeRuntime.__TestInstallSeam(() => true);
                var config = new Bug1ProbeConfig
                {
                    RunId = "t60-run",
                    StepId = "t60-step",
                    TargetSteamId = 1UL,
                    OutputRoot = root,
                    NativeSpeedCapUS = 4.0,
                    SpeedCapKnown = true,
                    SpeedTier = Bug1ProbeNativeSpeed.TierWalk,
                };
                Assert(Bug1ProbeRuntime.Enable(config), "离线 Enable 应成功：" + Bug1ProbeRuntime.LastError);
                Bug1ProbeRuntime.__TestAddSmoothFrames(4);
                Bug1ProbeRuntime.__TestAddPartialTailFrames(3);
                // t65：尾部只有被「合法边界」解释时才允许裁。这里显式声明目标已销毁、detour 健康。
                Bug1ProbeRuntime.__TestSetCaptureState(false, false, false, false, null);
                Bug1ProbeRuntime.EndStep("target_left");
                Bug1ProbeRuntime.__TestClearCaptureState();
                Bug1ProbeRuntime.__TestDisable();

                string tracePath = Path.Combine(root, "trace.jsonl");
                Assert(File.Exists(tracePath), "EndStep 必须落盘 trace.jsonl，LastError=" + Bug1ProbeRuntime.LastError);
                string[] lines = File.ReadAllLines(tracePath, Encoding.UTF8);
                string runEnd = FindLine(lines, "\"phase\":\"RUN_END\"");
                Assert(runEnd.Contains("\"window_tail_excluded\":3"), "RUN_END 必须报告裁掉 3 个尾巴帧：" + runEnd);
                Assert(runEnd.Contains("\"phase_missing_genuine\":0"), "收口后真缺必须为 0：" + runEnd);
                Assert(runEnd.Contains("\"window_tail_boundary\":\"boundary_player_destroyed\""),
                    "合法边界必须归因为 boundary_player_destroyed：" + runEnd);
                Assert(runEnd.Contains("\"phase_missing_out_of_window\":3"), "越界计数必须等于 head+tail：" + runEnd);
                string result = File.ReadAllText(Path.Combine(root, "result.json"), Encoding.UTF8);
                Assert(result.Contains("\"window_tail_excluded\":3"), "result.json 必须与 RUN_END 同源：" + result);
                Assert(result.Contains("\"phase_missing_genuine\":0"), "result.json 必须带真缺计数");
                Assert(!result.Contains("MISSING_PHASE"), "收口后不得再出现 MISSING_PHASE：" + result);

                // R14-01：manifest.json 与 result.json / RUN_END 必须同字段集；只改 manifest 漏字段时这里必须红。
                string manifest = File.ReadAllText(Path.Combine(root, "manifest.json"), Encoding.UTF8);
                foreach (string field in T79AggregationFields)
                {
                    Assert(manifest.Contains("\"" + field + "\":"), "manifest.json 缺 T79 字段 " + field);
                    Assert(result.Contains("\"" + field + "\":"), "result.json 缺 T79 字段 " + field);
                    Assert(runEnd.Contains("\"" + field + "\":"), "RUN_END 缺 T79 字段 " + field);
                }
                Assert(manifest.Contains("\"event_aggregation_revision\":\"CLOSED_EVENT_PRIORITY-v1\""),
                    "manifest 必须冻结聚合口径：" + manifest);
                Assert(ExtractBool(manifest, "\"event_scan_complete\":") == ExtractBool(runEnd, "\"event_scan_complete\":"),
                    "manifest 与 RUN_END 的 event_scan_complete 必须一致");
                Assert(ExtractInt(manifest, "\"right_censored_segments\":") == ExtractInt(result, "\"right_censored_segments\":"),
                    "manifest 与 result 的 right_censored_segments 必须一致");
                Assert(manifest.Contains("\"right_censored_ranges\":[]"), "manifest 的 ranges 必须是显式空数组：" + manifest);
                Assert(result.Contains("\"right_censored_ranges\":[]"), "result 的 ranges 必须是显式空数组：" + result);
                Assert(manifest.Contains("\"closed_events\":[]"), "manifest 的 closed_events 必须是显式空数组");

                // R14-01 关键补丁：空数组抓不到「字段缺失」，这里必须有真实截断区间。
                string root2 = Path.Combine(Path.GetTempPath(), "spf-t83-" + Guid.NewGuid().ToString("N"));
                try
                {
                    Bug1StepWindow censoredWindow = Window(
                        Divergent(0, 0.00, 0.00),
                        Divergent(1, 0.02, 0.00),
                        Divergent(2, 0.04, 0.00),
                        Divergent(3, 0.06, 0.00),
                        Divergent(4, 0.08, 0.08),
                        Divergent(5, 0.08, 0.08),
                        Divergent(6, 0.08, 0.08),
                        Divergent(7, 0.08, 0.08),
                        Divergent(8, 0.08, 0.08));
                    // F1 语义：相位计数只有静态累加器一套真值，EndStep 会用 _rxEnqueueRecords/_rxDequeueRecords
                    // 覆盖窗口上的计数。所以这里必须走真实 OnEnqueue/OnDequeue 产记录；直接改窗口字段会被
                    // EndStep 的同步覆盖成 0，从而误判 PHASE_COVERAGE_MISSING（这正是本条子例第一版失败的原因）。
                    var censoredQueue = new Queue<PlayerInputPacket>();
                    FieldInfo censoredSeq = typeof(PlayerInputPacket).GetField(
                        "clientSimulationFrameNumber",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    var censoredConfig = new Bug1ProbeConfig
                    {
                        RunId = "t83-run",
                        StepId = "t83-step",
                        TargetSteamId = 1UL,
                        OutputRoot = root2,
                        NativeSpeedCapUS = 4.0,
                        SpeedCapKnown = true,
                        SpeedTier = Bug1ProbeNativeSpeed.TierWalk,
                    };
                    Assert(censoredSeq != null, "PlayerInputPacket.clientSimulationFrameNumber 必须存在");
                    Bug1ProbeRuntime.__TestEnable(null, censoredQueue, censoredSeq, censoredWindow, censoredConfig);
                    for (uint seq = 1; seq <= 4; seq++)
                    {
                        var packet = new PlayerInputPacket();
                        censoredSeq.SetValue(packet, seq);
                        censoredQueue.Enqueue(packet);
                        Bug1ProbeRuntime.OnEnqueue(censoredQueue, packet);
                    }
                    for (int i = 0; i < 4; i++)
                    {
                        PlayerInputPacket dequeued = censoredQueue.Dequeue();
                        Bug1ProbeRuntime.OnDequeue(censoredQueue, dequeued);
                    }
                    Bug1ProbeRuntime.EndStep("config_changed");
                    Bug1ProbeRuntime.__TestDisable();

                    string censoredTrace = Path.Combine(root2, "trace.jsonl");
                    Assert(File.Exists(censoredTrace), "截断用例必须落盘 trace.jsonl，LastError=" + Bug1ProbeRuntime.LastError);
                    string censoredRunEnd = FindLine(File.ReadAllLines(censoredTrace, Encoding.UTF8), "\"phase\":\"RUN_END\"");
                    string censoredManifest = File.ReadAllText(Path.Combine(root2, "manifest.json"), Encoding.UTF8);
                    string censoredResult = File.ReadAllText(Path.Combine(root2, "result.json"), Encoding.UTF8);
                    // 尾段从释放帧 4 起就是「同点静止」的新的水平小步段，因此截断区间是 [4,8]，
                    // 与闭合 M1 的 [0,3]/4 严格分开——这正是 T79 要求的「局部化右截断」。
                    const string rangeJson = "{\"stall_start_frame\":4,\"stall_end_frame\":8,\"release_frame\":-1,\"reason\":\"RIGHT_CENSORED\"}";
                    const string eventJson = "{\"kind\":\"M1\",\"stall_start_frame\":0,\"stall_end_frame\":3,\"release_frame\":4,\"evidence_complete\":true}";
                    foreach (string doc in new[] { censoredResult, censoredManifest, censoredRunEnd })
                    {
                        Assert(doc.Contains("\"right_censored_ranges\":[" + rangeJson + "]"),
                            "截断区间必须三处同源非空：" + doc);
                        Assert(doc.Contains("\"closed_events\":[" + eventJson + "]"),
                            "闭合事件必须三处同源非空：" + doc);
                        Assert(doc.Contains("\"right_censored_segments\":1"), "截断段计数必须为 1：" + doc);
                        Assert(doc.Contains("\"event_scan_complete\":false"), "有截断时扫描不得声称穷尽：" + doc);
                        Assert(doc.Contains("\"strict_m1_events_is_lower_bound\":true"), "有截断时计数是下界：" + doc);
                    }
                    Assert(censoredRunEnd.Contains("\"label\":\"M1StallCatchup\""), "早段闭合 M1 不得被尾段推翻：" + censoredRunEnd);
                    Assert(censoredResult.Contains("\"label\":\"M1StallCatchup\""), "result 必须与 RUN_END 同结论");
                    Assert(censoredRunEnd.Contains("\"strict_m1_events\":1"), "M1 事件计数应为 1：" + censoredRunEnd);
                }
                finally
                {
                    Bug1ProbeRuntime.__TestDisable();
                    try { if (Directory.Exists(root2)) Directory.Delete(root2, true); } catch { }
                }

                System.Reflection.MethodInfo endStep = typeof(Bug1ProbeRuntime).GetMethod(
                    "EndStep", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
                int finalizeCalls = IlContractProbe.CountMethodCalls(
                    endStep, typeof(Bug1ProbeWindowBoundary).FullName, "Finalize");
                Assert(finalizeCalls >= 1, "EndStep 必须调用 Bug1ProbeWindowBoundary.Finalize，实际=" + finalizeCalls);
            }
            finally
            {
                Bug1ProbeRuntime.__TestDisable();
                Bug1ProbeRuntime.__TestInstallSeam(null);
                Bug1ProbeRuntime.__TestOffline(false);
                try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
            }
        }

        private static string FindLine(string[] lines, string marker)
        {
            for (int i = 0; i < lines.Length; i++)
                if (lines[i].Contains(marker)) return lines[i];
            throw new InvalidOperationException("Bug1ProbeT60: 未找到包含 " + marker + " 的行");
        }

        // Bug1-61：真实 trace 回放——修复前 tail 半帧被算成缺相位，收口后消失且给出非 MISSING_PHASE 的结论。
        public static void Test_T60_RealTraceReplaySealsTail()
        {
            string path = ResolveTracePath();
            if (path == null) return; // 无真实 trace 的环境跳过，绝不伪造数据

            var hasBefore = new HashSet<int>();
            var hasRender = new HashSet<int>();
            foreach (string line in File.ReadLines(path))
            {
                if (!line.Contains("\"kind\":\"FRAME\"")) continue;
                int frame = ExtractInt(line, "\"frame_count\":");
                if (frame < 0) continue;
                if (line.Contains("\"phase\":\"BEFORE_UPDATE\"")) hasBefore.Add(frame);
                else if (line.Contains("\"phase\":\"RENDER_READY\"")) hasRender.Add(frame);
            }
            Assert(hasRender.Count > 1000, "真实 trace 帧数异常：" + hasRender.Count);
            var frames = new List<int>(hasRender);
            frames.Sort();
            int first = frames[0];
            // 修复前的窗口还包含「相机继续建帧、却因目标已销毁而从未 Record」的空白尾帧：
            // trace 里看不到它们，只能由 RUN_END 的 frame_count 还原出来。
            string[] allLines = File.ReadAllLines(path, Encoding.UTF8);
            int runEndFrame = ExtractInt(FindLine(allLines, "\"phase\":\"RUN_END\""), "\"frame_count\":");
            int lastRecorded = frames[frames.Count - 1];
            Assert(runEndFrame > lastRecorded, "RUN_END 帧号应晚于最后一条记录帧");
            for (int f = lastRecorded + 1; f < runEndFrame; f++) frames.Add(f);
            int last = frames[frames.Count - 1];

            Bug1StepWindow rawWindow = ReplayWindow(frames, hasBefore, hasRender, first, last);
            Bug1Verdict raw = Bug1ProbeJudge.Judge(rawWindow);
            Assert(raw.Label == Bug1Label.Unknown, "修复前应判 UNKNOWN，实际 " + raw.Label);
            Assert(raw.Reason == "MISSING_PHASE", "修复前原因应为 MISSING_PHASE，实际 " + raw.Reason);
            Assert(CountTailPhaseMissing(raw.MissingFields, last - 200) > 0, "修复前 tail 段必须存在缺相位条目");

            // t65：session_reset 只收口、不解释——真实 trace 的尾部是「目标已销毁但当时没有 destroy 钩子」，
            // 属于 capture failure，必须保留尾部并判 UNKNOWN(CAPTURE_FAILURE_TAIL)，不得裁掉。
            Bug1StepWindow resetWindow = ReplayWindow(frames, hasBefore, hasRender, first, last);
            var resetCtx = new Bug1WindowBoundaryContext
            {
                EndReason = "session_reset",
                TargetAlive = false,
                ThirdAlive = false,
                RendererAlive = false,
                HookFailClosed = false,
            };
            Bug1WindowBounds resetBounds = Bug1ProbeWindowBoundary.Finalize(resetWindow, resetCtx);
            resetWindow.WindowHeadExcluded = resetBounds.HeadExcluded;
            resetWindow.WindowTailExcluded = resetBounds.TailExcluded;
            resetWindow.WindowInteriorPartial = resetBounds.InteriorPartial;
            resetWindow.WindowExcludedReason = resetBounds.ExcludedReason;
            resetWindow.WindowTailBoundaryKind = resetBounds.TailBoundaryKind;
            resetWindow.WindowHeadBoundaryKind = resetBounds.HeadBoundaryKind;
            resetWindow.WindowTailUnexplained = resetBounds.TailUnexplained;
            resetWindow.WindowTailRenderOnly = resetBounds.TailRenderOnly;
            resetWindow.WindowTailFirstFrame = resetBounds.TailFirstFrame;
            Bug1Verdict resetVerdict = Bug1ProbeJudge.Judge(resetWindow);
            Assert(resetBounds.TailExcluded == 0, "session_reset 不得裁剪尾部，实际 tail_excluded=" + resetBounds.TailExcluded);
            Assert(resetVerdict.Label == Bug1Label.Unknown, "session_reset 收口应判 UNKNOWN，实际 " + resetVerdict.Label);
            Assert(resetVerdict.Reason == "CAPTURE_FAILURE_TAIL", "session_reset 原因应为 CAPTURE_FAILURE_TAIL，实际 " + resetVerdict.Reason);

            // 同一份数据，换成真实合法边界（target_left 且目标确实已销毁、detour 健康）才允许裁尾。
            Bug1StepWindow sealedWindow = ReplayWindow(frames, hasBefore, hasRender, first, last);
            var sealedCtx = new Bug1WindowBoundaryContext
            {
                EndReason = "target_left",
                TargetAlive = false,
                ThirdAlive = false,
                RendererAlive = false,
                HookFailClosed = false,
            };
            Bug1WindowBounds bounds = Bug1ProbeWindowBoundary.Finalize(sealedWindow, sealedCtx);
            sealedWindow.WindowHeadExcluded = bounds.HeadExcluded;
            sealedWindow.WindowTailExcluded = bounds.TailExcluded;
            sealedWindow.WindowInteriorPartial = bounds.InteriorPartial;
            sealedWindow.WindowExcludedReason = bounds.ExcludedReason;
            sealedWindow.WindowTailBoundaryKind = bounds.TailBoundaryKind;
            sealedWindow.WindowHeadBoundaryKind = bounds.HeadBoundaryKind;
            sealedWindow.WindowTailUnexplained = bounds.TailUnexplained;
            sealedWindow.WindowTailRenderOnly = bounds.TailRenderOnly;
            sealedWindow.WindowTailFirstFrame = bounds.TailFirstFrame;
            Assert(bounds.TailExcluded > 0, "真实 trace 的 tail 必须被合法边界收口，实际 " + bounds.TailExcluded);
            Assert(bounds.TailBoundaryKind == Bug1WindowBounds.BoundaryPlayerDestroyed,
                "target_left 且目标已销毁应归因为 boundary_player_destroyed，实际 " + bounds.TailBoundaryKind);
            Bug1Verdict sealedVerdict = Bug1ProbeJudge.Judge(sealedWindow);
            Assert(CountTailPhaseMissing(sealedVerdict.MissingFields, sealedWindow.StepLastFrame) == 0,
                "收口后不得再有 tail 段缺相位条目");
            Assert(sealedVerdict.Reason != "MISSING_PHASE", "收口后不得再以 MISSING_PHASE 收场，实际 " + sealedVerdict.Reason);
            Assert(sealedVerdict.PhaseMissingGenuine == 0, "收口后真缺计数应为 0，实际 " + sealedVerdict.PhaseMissingGenuine);
            Assert(sealedVerdict.PhaseMissingOutOfWindow == bounds.HeadExcluded + bounds.TailExcluded,
                "out_of_window 必须等于 head+tail，实际 " + sealedVerdict.PhaseMissingOutOfWindow);
            Console.WriteLine("  [T60] replay trace=" + path + " recorded=" + hasRender.Count
                + " run_end_frame=" + runEndFrame + " raw=" + raw.Label + "/" + raw.Reason
                + " raw_tail_missing=" + CountTailPhaseMissing(raw.MissingFields, last - 200)
                + " reset=" + resetVerdict.Label + "/" + resetVerdict.Reason + " reset_tail_excluded=" + resetBounds.TailExcluded
                + " sealed=" + sealedVerdict.Label + "/" + sealedVerdict.Reason
                + " head_excluded=" + bounds.HeadExcluded + " tail_excluded=" + bounds.TailExcluded
                + " genuine=" + sealedVerdict.PhaseMissingGenuine);
        }

        private static Bug1StepWindow ReplayWindow(List<int> frames, HashSet<int> hasBefore, HashSet<int> hasRender, int first, int last)
        {
            var w = new Bug1StepWindow
            {
                StepId = "replay",
                NativeSpeedCapUS = 4.0,
                SpeedCapKnown = true,
                FixedConditionsValid = true,
                StepFirstFrame = first,
                StepLastFrame = last,
            };
            // t65/F2：两个相位都必须取自 trace.jsonl 的真实字段，绝不硬编码。
            // 旧实现把 HasRenderReady 一律置 true，把「从未 Record 的空白尾帧」伪造成 render-only 帧，
            // 于是「收口后没有 MISSING_PHASE」成了自证。
            for (int i = 0; i < frames.Count; i++)
                w.Frames.Add(Frame(frames[i], hasBefore.Contains(frames[i]), hasRender.Contains(frames[i]), 0.02 * i));
            return w;
        }

        private static int CountTailPhaseMissing(List<string> missing, int threshold)
        {
            int count = 0;
            for (int i = 0; i < missing.Count; i++)
            {
                string field = missing[i];
                if (field == null || !field.StartsWith("phase_BEFORE_UPDATE@", StringComparison.Ordinal)) continue;
                if (ExtractInt(field, "@") > threshold) count++;
            }
            return count;
        }

        private static int ExtractInt(string text, string marker)
        {
            int at = text.IndexOf(marker, StringComparison.Ordinal);
            if (at < 0) return -1;
            int i = at + marker.Length;
            int start = i;
            while (i < text.Length && (char.IsDigit(text[i]) || text[i] == '-')) i++;
            int value;
            return int.TryParse(text.Substring(start, i - start), NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
                ? value : -1;
        }

        private static string ResolveTracePath()
        {
            string env = Environment.GetEnvironmentVariable("SPF_BUG1_TRACE_JSONL");
            if (!string.IsNullOrEmpty(env) && File.Exists(env)) return env;
            try
            {
                string dir = AppDomain.CurrentDomain.BaseDirectory;
                for (int depth = 0; depth < 8 && !string.IsNullOrEmpty(dir); depth++)
                {
                    string baseDir = Path.Combine(dir, "启动器", "UnturnedModManager", "publish", "UMM-v2.2.1-win-x64", "spf-bug1");
                    if (Directory.Exists(baseDir))
                    {
                        string[] runs = Directory.GetDirectories(baseDir, "bug1-*");
                        Array.Sort(runs, StringComparer.Ordinal);
                        for (int i = runs.Length - 1; i >= 0; i--)
                        {
                            string candidate = Path.Combine(runs[i], "HOST", "trace.jsonl");
                            if (File.Exists(candidate)) return candidate;
                        }
                    }
                    DirectoryInfo parent = Directory.GetParent(dir);
                    dir = parent == null ? null : parent.FullName;
                }
            }
            catch { }
            return null;
        }
    }
}
