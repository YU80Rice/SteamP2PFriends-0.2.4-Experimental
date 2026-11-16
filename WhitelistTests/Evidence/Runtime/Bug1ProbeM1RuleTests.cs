using SteamP2PFriends.Platform.Diagnostics.Patches;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace SteamP2PFriends.WhitelistTests
{
    /// <summary>
    /// T71 / M1-XZ-RELATIVE-v2 规则用例 R01–R11（architecture-decision-t71-m1-relative-motion.md §5）。
    /// 每条用例都直接调用生产判定入口 <see cref="Bug1ProbeJudge.Judge"/>，不另写一份判定器；
    /// 断言只覆盖判定器的分类与计数，不构成 Bug1 根因结论。
    /// </summary>
    internal static class Bug1ProbeM1RuleTests
    {
        private const long Frequency = 10000000L; // 10MHz，dt=1/75 时每帧 133333 tick
        private const double SpeedCap = 5.0;

        /// <summary>相位齐全、组态合法、V 稳定的基准窗口；各规则用例只覆盖差异部分。</summary>
        private static Bug1StepWindow Window(int frameCount, double speedCap = SpeedCap)
            => WindowFrom(0, frameCount, speedCap);

        /// <summary>T79 帧级夹具：窗口帧号可从任意起点开始（裁决书例子用 100..110）。</summary>
        private static Bug1StepWindow WindowFrom(int firstFrame, int frameCount, double speedCap = SpeedCap)
        {
            var window = new Bug1StepWindow
            {
                StepId = "step-r",
                NativeSpeedCapUS = speedCap,
                SpeedCapKnown = true,
                FixedConditionsValid = true,
                StepFirstFrame = firstFrame,
                StepLastFrame = firstFrame + frameCount - 1,
            };
            for (int i = 0; i < frameCount; i++)
            {
                window.Frames.Add(new Bug1FrameSample
                {
                    Frame = firstFrame + i,
                    FixedTickId = i,
                    MonoTicks = (long)Math.Round(i * (double)Frequency / 75.0),
                    MonoFrequency = Frequency,
                    HasBeforeUpdate = true,
                    HasRenderReady = true,
                    CameraIsMain = true,
                    ThirdCarrierVerified = true,
                    RendererVerified = true,
                    ModelEndpointResolved = true,
                    ABeforeUpdate = new Bug1Vec3(0, 0, 0),
                    APos = new Bug1Vec3(0, 0, 0),
                    VPos = new Bug1Vec3(0, 0, 0),
                    AYaw = 0,
                    VYaw = 0,
                    HasLastInputSeq = true,
                    LastInputSeq = (uint)(i + 1),
                    HasLastRxId = true,
                    LastRxId = (ulong)(i + 1),
                    HasQueueDepth = true,
                    QueueDepth = 1,
                });
            }
            return window;
        }

        private static void SetV(Bug1StepWindow window, int frame, double x, double z = 0.0)
        {
            window.Frames[frame].VPos = new Bug1Vec3(x, 0, z);
        }

        private static void SetA(Bug1StepWindow window, int frame, double x, double y = 0.0, double z = 0.0)
        {
            window.Frames[frame].APos = new Bug1Vec3(x, y, z);
            window.Frames[frame].ABeforeUpdate = new Bug1Vec3(x, y, z);
        }

        private static void AddSim(Bug1StepWindow window, int frame, string id, Bug1Vec3 before, Bug1Vec3 after)
        {
            window.Sims.Add(new Bug1SimSample
            {
                SimCallId = id,
                Frame = frame,
                HasBefore = true,
                HasAfter = true,
                ABefore = before,
                AAfter = after,
                HasInputSeq = true,
                InputSeq = (uint)(frame + 1),
                HasRxId = true,
                RxId = (ulong)(frame + 1),
                HasQueueDepth = true,
                QueueDepth = 1,
            });
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Bug1ProbeM1: " + message);
        }

        private static string Dump(Bug1Verdict v)
        {
            return "label=" + v.Label + " reason=" + v.Reason + " valid=" + v.Valid
                + " m1=" + v.StrictM1Events + " auth=" + v.AuthSegments + " nonm1=" + v.NonM1Segments
                + " stall=" + v.HorizontalStallSegments + " cand=" + v.HorizontalCandidates
                + " A=" + v.CandidateA + " B=" + v.CandidateB + " R=" + v.ReleaseFrame
                + " reject=[" + string.Join(",", v.CandidateRejections) + "]";
        }

        /// <summary>R01：A 逐步推进、V 停 4 帧后一次释放并回到 A → M1（落后见证 + 回补收敛）。</summary>
        public static void Test_R01_M1RequiresLagAndConvergence()
        {
            var w = Window(5);
            for (int i = 0; i < 4; i++) SetV(w, i, 0);
            SetV(w, 4, 0.08);
            for (int i = 0; i < 5; i++) SetA(w, i, 0.02 * i);

            var v = Bug1ProbeJudge.Judge(w);

            Assert(v.Label == Bug1Label.M1StallCatchup, "R01 应判 M1，" + Dump(v));
            Assert(v.StrictM1Events == 1, "R01 事件数应为 1，" + Dump(v));
            Assert(v.LagWitnessFrame == 1, "R01 落后见证应在停段首帧，实际=" + v.LagWitnessFrame);
            Assert(v.LagBeforeReleaseU > v.LagAfterReleaseU, "R01 释放后相对差距必须收敛");
            Assert(v.CandidateA == 0 && v.CandidateB == 3 && v.ReleaseFrame == 4, "R01 区间应为 [0,3]/4，" + Dump(v));
            Assert(v.M1RuleRevision == "M1-XZ-RELATIVE-v2" && v.MotionAxes == "XZ", "R01 必须带规则修订号与运动轴");

            // T79/CLOSED_EVENT_PRIORITY-v1：同一窗口再接一段静止尾部（无未来释放）——
            // 右截断只局部生效，已闭合的 M1 与候选三元组不得被推翻，事件计数降为下界。
            var withTail = Window(9);
            for (int i = 0; i < 4; i++) SetV(withTail, i, 0);
            SetV(withTail, 4, 0.08);
            for (int i = 0; i < 5; i++) SetA(withTail, i, 0.02 * i);
            for (int i = 5; i < 9; i++) { SetV(withTail, i, 0.08); SetA(withTail, i, 0.08); }

            var t = Bug1ProbeJudge.Judge(withTail);
            Assert(t.Label == Bug1Label.M1StallCatchup, "T79 已闭合 M1 不得被正常尾部右截断推翻，" + Dump(t));
            Assert(t.Valid, "T79 有闭合事件时 valid 必须为 true，" + Dump(t));
            Assert(t.StrictM1Events == 1, "T79 闭合 M1 计数应为 1，" + Dump(t));
            Assert(t.CandidateA == 0 && t.CandidateB == 3 && t.ReleaseFrame == 4,
                "T79 候选三元组必须仍是闭合事件真实帧号，不得被尾段覆盖，" + Dump(t));
            Assert(t.SelectedEventComplete, "T79 代表事件证明域应完整，" + Dump(t));
            Assert(!t.EventScanComplete, "T79 存在未判定右截断段时 event_scan_complete 必须 false，" + Dump(t));
            Assert(t.StrictM1EventsIsLowerBound, "T79 有右截断时事件计数是下界，" + Dump(t));
            Assert(t.RightCensoredSegments == 1 && t.RightCensoredRanges.Count == 1,
                "T79 尾部应记 1 段局部右截断，" + Dump(t));
            Assert(t.RightCensoredRanges[0].StallStartFrame == 4 && t.RightCensoredRanges[0].StallEndFrame == 8
                && t.RightCensoredRanges[0].ReleaseFrame == -1 && t.RightCensoredRanges[0].Reason == "RIGHT_CENSORED",
                "T79 右截断范围应为 [4,8]（起点为释放帧 4，与裁决书 [104,110] 同构）且 release=-1，不得扩成整窗，" + Dump(t));
            Assert(t.ClosedEvents.Count == 1 && t.ClosedEvents[0].Kind == "M1"
                && t.ClosedEvents[0].StallStartFrame == 0 && t.ClosedEvents[0].StallEndFrame == 3
                && t.ClosedEvents[0].ReleaseFrame == 4 && t.ClosedEvents[0].EvidenceComplete,
                "T79 closed_events 应恰有 M1(0,3,4) 且证据完整，" + Dump(t));
            Assert(t.EventAggregationRevision == "CLOSED_EVENT_PRIORITY-v1", "T79 必须带聚合口径修订号");

            // T79 裁决书帧级例子（帧 100..110、dt=1/75、cap=5、Y/Z=0）：
            // A.x = 0,.02,.04,.06 然后 .08；V.x = 0,0,0,0 然后 .08。
            // 前段 [100,103] 停住、104 释放、之后同点静止 → 尾段 [104,110] 无未来释放。
            var spec = WindowFrom(100, 11);
            for (int i = 0; i < 4; i++) { SetV(spec, i, 0); SetA(spec, i, 0.02 * i); }
            for (int i = 4; i < 11; i++) { SetV(spec, i, 0.08); SetA(spec, i, 0.08); }

            var s = Bug1ProbeJudge.Judge(spec);
            Assert(s.Label == Bug1Label.M1StallCatchup && s.Reason == "M1_STALL_CATCHUP",
                "T79 规格例应判 M1_STALL_CATCHUP，" + Dump(s));
            Assert(s.Valid, "T79 规格例 valid 必须为 true，" + Dump(s));
            Assert(s.StrictM1Events == 1, "T79 规格例 strict_m1_events 应为 1，" + Dump(s));
            Assert(s.CandidateA == 100 && s.CandidateB == 103 && s.ReleaseFrame == 104,
                "T79 规格例候选三元组应为 100/103/104，" + Dump(s));
            Assert(s.SelectedEventComplete, "T79 规格例 selected_event_complete 应为 true，" + Dump(s));
            Assert(!s.EventScanComplete, "T79 规格例 event_scan_complete 应为 false，" + Dump(s));
            Assert(s.StrictM1EventsIsLowerBound, "T79 规格例事件计数是下界，" + Dump(s));
            Assert(s.RightCensoredSegments == 1 && s.RightCensoredRanges.Count == 1
                && s.RightCensoredRanges[0].StallStartFrame == 104 && s.RightCensoredRanges[0].StallEndFrame == 110
                && s.RightCensoredRanges[0].ReleaseFrame == -1,
                "T79 规格例右截断范围必须是 [104,110]、release=-1，不得改成 [100,110]，" + Dump(s));
            Assert(s.ClosedEvents.Count == 1 && s.ClosedEvents[0].Kind == "M1"
                && s.ClosedEvents[0].StallStartFrame == 100 && s.ClosedEvents[0].StallEndFrame == 103
                && s.ClosedEvents[0].ReleaseFrame == 104,
                "T79 规格例 closed_events 应恰有 M1(100,103,104)，" + Dump(s));

            // 两件闭合 M1 + 尾段：全部合格件都计数，代表按「最长→最早 a→最早 release」稳定选择。
            var two = Window(14);
            for (int i = 0; i < 4; i++) { SetV(two, i, 0); SetA(two, i, 0.02 * i); }
            SetV(two, 4, 0.08);
            SetA(two, 4, 0.08);
            for (int i = 5; i < 8; i++) { SetV(two, i, 0.08); SetA(two, i, 0.02 * i); }
            SetV(two, 8, 0.16);
            SetA(two, 8, 0.16);
            for (int i = 9; i < 14; i++) { SetV(two, i, 0.16); SetA(two, i, 0.16); }

            var d = Bug1ProbeJudge.Judge(two);
            Assert(d.Label == Bug1Label.M1StallCatchup, "T79 两件闭合 M1 应判 M1，" + Dump(d));
            Assert(d.StrictM1Events == 2, "T79 两件闭合 M1 必须都计数，" + Dump(d));
            Assert(d.ClosedEvents.Count == 2 && d.ClosedEvents[0].Kind == "M1" && d.ClosedEvents[1].Kind == "M1",
                "T79 closed_events 应含两件 M1，" + Dump(d));
            Assert(d.CandidateA == 0 && d.CandidateB == 3 && d.ReleaseFrame == 4,
                "T79 等长 M1 代表应取最早 a，" + Dump(d));
            Assert(d.RightCensoredSegments == 1 && d.RightCensoredRanges[0].StallStartFrame == 8
                && d.RightCensoredRanges[0].StallEndFrame == 13,
                "T79 尾部应记 [8,13] 右截断（起点为释放帧 8），" + Dump(d));
        }

        /// <summary>R02：A 与 V 同步步进（停段根未推进、释放同拍）→ AUTH_STEP_LOCKED，且不得判 M1。</summary>
        public static void Test_R02_AuthStepLockedWhenRootAndCarrierMoveTogether()
        {
            var w = Window(5);
            for (int i = 0; i < 4; i++) { SetV(w, i, 0); SetA(w, i, 0); }
            SetV(w, 4, 0.08);
            SetA(w, 4, 0.08);

            var v = Bug1ProbeJudge.Judge(w);

            Assert(v.Label == Bug1Label.AuthStepLocked, "R02 应判 AUTH，" + Dump(v));
            Assert(v.Reason == "AUTH_STEP_LOCKED", "R02 原因应为 AUTH_STEP_LOCKED，实际=" + v.Reason);
            Assert(v.StrictM1Events == 0, "R02 不得计入 M1，" + Dump(v));
            Assert(v.AuthSegments == 1, "R02 AUTH 段计数应为 1，" + Dump(v));
            Assert(v.CandidateA == 0 && v.CandidateB == 3 && v.ReleaseFrame == 4, "R02 区间应为 [0,3]/4，" + Dump(v));

            // T74-F2：对称抖动（每帧与段首差 0.001U ≤ε，但段内成对跨度 0.002U > ε）不是「离散步进」，不得判 AUTH。
            // 取值受 q=0.001 网格约束：必须量化后仍 ≤ε 才可能与「距段首」口径区分开。
            // 改前实现用「距段首」口径 → 本例会被误判 AUTH；改为 §3.4 的段内最大成对距离后落 NON_M1。
            var jitter = Window(5);
            SetA(jitter, 0, 0);
            SetA(jitter, 1, 0.001);
            SetA(jitter, 2, -0.001);
            SetA(jitter, 3, 0);
            for (int i = 0; i < 4; i++) SetV(jitter, i, 0);
            SetV(jitter, 4, 0.08);
            SetA(jitter, 4, 0.08);

            var j = Bug1ProbeJudge.Judge(jitter);

            Assert(j.Label == Bug1Label.NoThresholdEvent, "R02 对称抖动不得判 AUTH/M1，应落 NO_THRESHOLD_EVENT，" + Dump(j));
            Assert(j.AuthSegments == 0, "R02 对称抖动不得计入 AUTH 段，" + Dump(j));
            Assert(j.NonM1Segments == 1, "R02 对称抖动应计入 NON_M1 段，" + Dump(j));
            Assert(j.StrictM1Events == 0, "R02 对称抖动不得计入 M1，" + Dump(j));
            Assert(j.CandidateRejections.Contains("LAG_WITNESS_MISSING@4"),
                "R02 对称抖动应因缺落后见证被拒，实际=[" + string.Join(",", j.CandidateRejections.ToArray()) + "]");

            // T79：闭合 AUTH 后接静止尾段 → 仍报 AUTH 与真实三元组，M1 确认数 0，事件扫描不穷尽。
            var authTail = Window(9);
            for (int i = 0; i < 4; i++) { SetV(authTail, i, 0); SetA(authTail, i, 0); }
            SetV(authTail, 4, 0.08);
            SetA(authTail, 4, 0.08);
            for (int i = 5; i < 9; i++) { SetV(authTail, i, 0.08); SetA(authTail, i, 0.08); }

            var at = Bug1ProbeJudge.Judge(authTail);
            Assert(at.Label == Bug1Label.AuthStepLocked, "T79 闭合 AUTH 不得被尾段推翻，" + Dump(at));
            Assert(at.Valid && at.SelectedEventComplete, "T79 AUTH 代表事件应完整，" + Dump(at));
            Assert(!at.EventScanComplete && at.StrictM1EventsIsLowerBound, "T79 AUTH 后尾段令扫描不穷尽，" + Dump(at));
            Assert(at.StrictM1Events == 0, "T79 AUTH 不得计入 M1，" + Dump(at));
            Assert(at.CandidateA == 0 && at.CandidateB == 3 && at.ReleaseFrame == 4,
                "T79 AUTH 三元组应为 [0,3]/4，" + Dump(at));
            Assert(at.ClosedEvents.Count == 1 && at.ClosedEvents[0].Kind == "AUTH",
                "T79 closed_events 应为 AUTH 一件，" + Dump(at));
        }

        /// <summary>R03：本次真实 trace 的候选段回放（frame 11562–12216）→ AUTH_STEP_LOCKED，且不改写原始 result.json。</summary>
        public static void Test_R03_RealTraceCandidateIsAuthNotM1()
        {
            string dir = ResolveTraceDirectory();
            if (dir == null)
            {
                // T75-F2：找不到输入就**不许**算 PASS。本套件没有 SKIP 计数维度（Program.cs 不在本票 inScope），
                // 所以这里直接失败并说明缺什么——「没跑」不能记成「跑过」。
                throw new InvalidOperationException(
                    "Bug1ProbeM1: R03 需要真实 trace 输入但未找到：请设置 SPF_BUG1_TRACE_JSONL 指向 trace.jsonl，" +
                    "或把 bug1-20261107T111121Z/HOST/trace.jsonl 放在仓库父目录的 启动器/UnturnedModManager/publish/UMM-v2.2.1-win-x64 下");
            }

            var window = ReplayCandidateSegment(Path.Combine(dir, "trace.jsonl"), 11562, 12215, 12216);
            Assert(window != null, "R03 无法从 trace 重建候选窗口");

            var v = Bug1ProbeJudge.Judge(window);

            Assert(v.Label == Bug1Label.AuthStepLocked, "R03 原候选应归 AUTH，实际 " + Dump(v));
            Assert(v.StrictM1Events == 0, "R03 不得再判 M1，" + Dump(v));
            Assert(v.AuthSegments == 1, "R03 应有一个 AUTH 段，" + Dump(v));
            Assert(v.CandidateA == 11562 && v.CandidateB == 12215 && v.ReleaseFrame == 12216,
                "R03 候选区间应为 11562–12215/12216，" + Dump(v));
            Assert(v.RenderRootSpanXZ <= v.EpsilonU, "R03 停段权威水平跨度应 ≈0，实际=" + v.RenderRootSpanXZ);

            string resultPath = Path.Combine(dir, "result.json");
            if (File.Exists(resultPath))
            {
                string original = File.ReadAllText(resultPath);
                Assert(original.Contains("M1StallCatchup"),
                    "R03 原始 result.json 必须保留历史误报供审计，不得被本次重判覆盖");
            }
        }

        /// <summary>
        /// R04：垂直运动不参与水平候选。纯垂直位移在水平投影里没有「释放」，边界未闭合 → UNKNOWN(RIGHT_CENSORED)；
        /// 「水平 0.03U + 垂直 0.08U」的混合释放，水平仍低于帧界 → 不得形成候选（三维距离 0.085U 会骗过三维口径）。
        /// </summary>
        public static void Test_R04_VerticalOnlyMotionHasNoHorizontalCandidate()
        {
            foreach (double release in new[] { 0.01, 0.08 })
            {
                var w = Window(5);
                for (int i = 0; i < 4; i++) { SetV(w, i, 0); SetA(w, i, 0); }
                w.Frames[4].VPos = new Bug1Vec3(0, release, 0);
                w.Frames[4].APos = new Bug1Vec3(0, release, 0);
                w.Frames[4].ABeforeUpdate = new Bug1Vec3(0, release, 0);
                AddSim(w, 0, "sim-v", new Bug1Vec3(0, 0, 0), new Bug1Vec3(0, release, 0));

                var v = Bug1ProbeJudge.Judge(w);

                Assert(v.Label != Bug1Label.M1StallCatchup, "R04 垂直释放不得判 M1（release=" + release + "），" + Dump(v));
                Assert(v.StrictM1Events == 0, "R04 垂直释放不得计入 M1（release=" + release + "），" + Dump(v));
                Assert(v.HorizontalCandidates == 0, "R04 水平候选必须为 0（release=" + release + "），" + Dump(v));
                Assert(v.Label == Bug1Label.Unknown && v.Reason == "RIGHT_CENSORED",
                    "R04 水平边界未闭合必须 UNKNOWN(RIGHT_CENSORED)（release=" + release + "），" + Dump(v));
            }

            // 混合释放：水平 0.03U 低于 cap5×dt(1/75)+ε=0.0668U，垂直 0.08U 只增大三维距离。
            var mixed = Window(6);
            for (int i = 0; i < 4; i++) { SetV(mixed, i, 0); SetA(mixed, i, 0); }
            SetV(mixed, 4, 0.03); SetA(mixed, 4, 0.03);
            mixed.Frames[4].VPos = new Bug1Vec3(0.03, 0.08, 0);
            mixed.Frames[4].APos = new Bug1Vec3(0.03, 0.08, 0);
            SetV(mixed, 5, 0.03); SetA(mixed, 5, 0.03);
            mixed.Frames[5].VPos = new Bug1Vec3(0.03, 0.08, 0);
            mixed.Frames[5].APos = new Bug1Vec3(0.03, 0.08, 0);

            var mv = Bug1ProbeJudge.Judge(mixed);

            Assert(mv.Label != Bug1Label.M1StallCatchup, "R04 混合释放不得判 M1，" + Dump(mv));
            Assert(mv.HorizontalCandidates == 0, "R04 混合释放水平未越界，不得形成候选，" + Dump(mv));
            Assert(mv.CandidateRejections.Contains("RELEASE_NOT_EXCEEDED@4"),
                "R04 混合释放必须记录水平未越界，" + Dump(mv));
            Assert(mv.Label == Bug1Label.NoThresholdEvent, "R04 混合释放应落无阈值事件，" + Dump(mv));
        }

        /// <summary>R05-a：载体始终在权威前方（释放才回到权威）→ 无落后见证，不得判 M1。</summary>
        public static void Test_R05a_CarrierAheadOfAuthorityIsNotM1()
        {
            var w = Window(5);
            for (int i = 0; i < 4; i++) SetV(w, i, 1.0);
            SetV(w, 4, 0.08);
            for (int i = 0; i < 5; i++) SetA(w, i, 0.02 * i);

            var v = Bug1ProbeJudge.Judge(w);

            Assert(v.Label != Bug1Label.M1StallCatchup, "R05-a 不得判 M1，" + Dump(v));
            Assert(v.StrictM1Events == 0, "R05-a 事件数应为 0，" + Dump(v));
            Assert(v.LagWitnessFrame == -1, "R05-a 不应存在落后见证，实际=" + v.LagWitnessFrame);
            Assert(v.CandidateRejections.Contains("LAG_WITNESS_MISSING@4"),
                "R05-a 必须记录缺少落后见证，" + Dump(v));
        }

        /// <summary>R05-b：载体确实落后但释放向外突跳（不是回补）→ 不得判 M1。</summary>
        public static void Test_R05b_ReleaseAwayFromAuthorityIsNotM1()
        {
            var w = Window(5);
            for (int i = 0; i < 4; i++) SetV(w, i, 0);
            SetV(w, 4, -0.08);
            for (int i = 0; i < 5; i++) SetA(w, i, 0.02 * i);

            var v = Bug1ProbeJudge.Judge(w);

            Assert(v.Label != Bug1Label.M1StallCatchup, "R05-b 不得判 M1，" + Dump(v));
            Assert(v.StrictM1Events == 0, "R05-b 事件数应为 0，" + Dump(v));
            // 落后见证只在代表事件上导出；此处由拒绝原因证明见证存在但收敛失败。
            Assert(v.CandidateRejections.Contains("ERROR_NOT_CONVERGED@4"),
                "R05-b 必须记录回补不收敛，" + Dump(v));
        }

        /// <summary>R06：2cm 绝对门按 1mm 网格整数比较，含边界（19 格拒、20/21 格过）。</summary>
        public static void Test_R06_TwoCentimetreGateIsGridBased()
        {
            foreach (double release in new[] { 0.019, 0.020, 0.021 })
            {
                var w = Window(5, 0.1); // cap 调低，保证速度门 B 低于 19mm，单独考察 2cm 门
                for (int i = 0; i < 4; i++) SetV(w, i, 0);
                SetV(w, 4, release);
                for (int i = 0; i < 4; i++) SetA(w, i, 0.004 * i);
                SetA(w, 4, release);

                var v = Bug1ProbeJudge.Judge(w);

                bool below = v.CandidateRejections.Contains("RELEASE_BELOW_2CM@4");
                if (release < 0.020)
                {
                    Assert(below, "R06 " + release.ToString(CultureInfo.InvariantCulture) + "U 应被 2cm 门拒绝，" + Dump(v));
                    Assert(v.HorizontalCandidates == 0, "R06 19 格不得形成候选，" + Dump(v));
                }
                else
                {
                    Assert(!below, "R06 " + release.ToString(CultureInfo.InvariantCulture) + "U 含边界，不得被 2cm 门拒绝，" + Dump(v));
                    Assert(v.HorizontalCandidates == 1, "R06 " + release.ToString(CultureInfo.InvariantCulture) + "U 应形成候选，" + Dump(v));
                }
            }
        }

        /// <summary>R07：A/V 逐帧同步微动（≤eps）后同步大步 → 不得判 M1（无落后见证）。</summary>
        /// <summary>
        /// R07：同步微动（逐帧 ≤ε、累计 > 3ε）不得判 M1。权威与载体每帧同步挪 0.0008U，
        /// 8 帧累计 0.0056U（> 3ε=0.0052U），释放 0.08U —— 累计跨度大但逐帧无落后，M1 的落后见证必须为空。
        /// </summary>
        public static void Test_R07_SynchronizedMicroMotionIsNotM1()
        {
            var w = Window(9);
            for (int i = 0; i < 8; i++)
            {
                double x = 0.0008 * i;
                SetV(w, i, x);
                SetA(w, i, x);
            }
            SetV(w, 8, 0.08);
            SetA(w, 8, 0.08);

            var v = Bug1ProbeJudge.Judge(w);

            Assert(v.Label != Bug1Label.M1StallCatchup, "R07 同步微动不得判 M1，" + Dump(v));
            Assert(v.StrictM1Events == 0, "R07 事件数应为 0，" + Dump(v));
            Assert(v.LagWitnessFrame == -1, "R07 不应存在落后见证，实际=" + v.LagWitnessFrame);
            Assert(v.HorizontalStallSegments == 1, "R07 应恰好有 1 个水平停段，" + Dump(v));
            Assert(v.HorizontalCandidates == 1, "R07 应形成 1 个水平候选，" + Dump(v));
            Assert(v.CandidateRejections.Contains("LAG_WITNESS_MISSING@8"),
                "R07 必须因缺落后见证被拒（LAG_WITNESS_MISSING@8），实际=[" + string.Join(",", v.CandidateRejections.ToArray()) + "]");
            Assert(v.RenderRootSpanXZ == 0d,
                "R07 落 NON_M1 段：代表段只为 M1/AUTH 段计算，故 RenderRootSpanXZ 应为 0（设计如此），实际=" + v.RenderRootSpanXZ.ToString("R"));
            // 「累计跨度大」是夹具自身的性质，不能靠 NON_M1 段不导出的 RenderRootSpanXZ 证明，这里直接按夹具算术断言。
            double cumulative = 0.0008 * 7;
            Assert(cumulative > 3 * v.EpsilonU,
                "R07 夹具累计跨度必须 > 3ε，否则证不了「累计大 ≠ 落后」，实际=" + cumulative.ToString("R"));
            // 代表段只赋给 M1 / AUTH 段；NON_M1 段无代表，这里断言「无代表」而不是为断言改生产语义。
            Assert(v.CandidateA == -1, "R07 落 NON_M1 段，不应有代表段，" + Dump(v));
            Assert(v.Label == Bug1Label.NoThresholdEvent, "R07 应落 NO_THRESHOLD_EVENT，" + Dump(v));
        }

        /// <summary>R08：较长的 AUTH 段在前、较短的真 M1 段在后 → 真 M1 不得被最长段遮蔽。</summary>
        public static void Test_R08_ShorterTrueM1IsNotMaskedByLongerSegment()
        {
            var w = Window(13);
            for (int i = 0; i < 8; i++) { SetV(w, i, 0); SetA(w, i, 0); }
            SetV(w, 8, 0.08);
            SetA(w, 8, 0.08);
            SetV(w, 9, 0.08);
            SetV(w, 10, 0.08);
            SetV(w, 11, 0.08);
            SetV(w, 12, 0.16);
            SetA(w, 9, 0.10);
            SetA(w, 10, 0.12);
            SetA(w, 11, 0.14);
            SetA(w, 12, 0.16);

            var v = Bug1ProbeJudge.Judge(w);

            Assert(v.Label == Bug1Label.M1StallCatchup, "R08 较短的 M1 段必须仍被找到，" + Dump(v));
            Assert(v.StrictM1Events == 1, "R08 M1 事件计数应为 1，" + Dump(v));
            Assert(v.AuthSegments == 1, "R08 较长的 AUTH 段也应计数，" + Dump(v));
            Assert(v.HorizontalStallSegments == 2, "R08 必须遍历到两个停段，" + Dump(v));
            Assert(v.CandidateA == 8 && v.CandidateB == 11 && v.ReleaseFrame == 12,
                "R08 代表事件应为真 M1 段 [8,11]/12，而不是更长的 AUTH 段，" + Dump(v));

            // T79：R08 再接静止尾段 → 代表仍是真 M1 段 [8,11]/12，较早的 AUTH 件仍保留在 closed_events。
            var tailed = Window(17);
            for (int i = 0; i < 8; i++) { SetV(tailed, i, 0); SetA(tailed, i, 0); }
            SetV(tailed, 8, 0.08);
            SetA(tailed, 8, 0.08);
            SetV(tailed, 9, 0.08);
            SetV(tailed, 10, 0.08);
            SetV(tailed, 11, 0.08);
            SetV(tailed, 12, 0.16);
            SetA(tailed, 9, 0.10);
            SetA(tailed, 10, 0.12);
            SetA(tailed, 11, 0.14);
            SetA(tailed, 12, 0.16);
            for (int i = 13; i < 17; i++) { SetV(tailed, i, 0.16); SetA(tailed, i, 0.16); }

            var rt = Bug1ProbeJudge.Judge(tailed);
            Assert(rt.Label == Bug1Label.M1StallCatchup && rt.StrictM1Events == 1,
                "T79 R08 尾段不得改变 M1 结论，" + Dump(rt));
            Assert(rt.CandidateA == 8 && rt.CandidateB == 11 && rt.ReleaseFrame == 12,
                "T79 R08 代表仍为真 M1 段 [8,11]/12，" + Dump(rt));
            Assert(rt.ClosedEvents.Count == 2 && rt.RightCensoredSegments == 1,
                "T79 R08 应保留 AUTH+M1 两件闭合事件与一段尾截断，" + Dump(rt));
        }

        /// <summary>
        /// R09：渲染相位 A 与 V 始终同步（无落后），只有 SIM 相位在水平移动 → 不得判 M1；
        /// SIM 三维跨度只作为旁证导出（&gt;ε），不得成为 M1 的布尔开关。
        /// </summary>
        public static void Test_R09_SimOnlyMotionCannotFormM1()
        {
            var w = Window(5);
            for (int i = 0; i < 4; i++)
            {
                SetV(w, i, 0);
                SetA(w, i, 0);
                AddSim(w, i, "sim-" + i, new Bug1Vec3(0.1 * i, 0, 0), new Bug1Vec3(0.1 * (i + 1), 0, 0));
            }
            SetV(w, 4, 0.08);
            SetA(w, 4, 0.08);
            AddSim(w, 4, "sim-4", new Bug1Vec3(0.4, 0, 0), new Bug1Vec3(0.5, 0, 0));

            var v = Bug1ProbeJudge.Judge(w);

            Assert(v.Label != Bug1Label.M1StallCatchup, "R09 仅 SIM 运动不得判 M1，" + Dump(v));
            Assert(v.StrictM1Events == 0, "R09 事件数应为 0，" + Dump(v));
            Assert(v.LagWitnessFrame == -1, "R09 渲染相位无落后见证，实际=" + v.LagWitnessFrame);
            Assert(v.SimRootSpan3D > v.EpsilonU,
                "R09 SIM 三维跨度应作为旁证导出（>ε），实际=" + v.SimRootSpan3D);
        }

        /// <summary>R10：缺相位/相机重复/暂停/截断 → UNKNOWN 先行，不得变成 NO_THRESHOLD_EVENT 凑绿。</summary>
        public static void Test_R10_UnknownPrecedenceOverNoThreshold()
        {
            var duplicate = Window(5);
            duplicate.Frames[2].CameraDuplicate = true;
            var v1 = Bug1ProbeJudge.Judge(duplicate);
            Assert(v1.Label == Bug1Label.Unknown, "R10 相机重复必须 UNKNOWN，" + Dump(v1));
            Assert(v1.Reason == "MULTI_CAMERA_SAMPLE", "R10 相机重复原因应为 MULTI_CAMERA_SAMPLE，实际=" + v1.Reason);

            var paused = Window(5);
            paused.PauseCrossed = true;
            var v2 = Bug1ProbeJudge.Judge(paused);
            Assert(v2.Label == Bug1Label.Unknown, "R10 暂停跨越必须 UNKNOWN，" + Dump(v2));
            Assert(v2.Reason == "PAUSE_CROSSED", "R10 暂停原因应为 PAUSE_CROSSED，实际=" + v2.Reason);

            var missing = Window(5);
            missing.Frames[3].HasRenderReady = false;
            var v3 = Bug1ProbeJudge.Judge(missing);
            Assert(v3.Label == Bug1Label.Unknown, "R10 缺渲染相位必须 UNKNOWN，" + Dump(v3));
            Assert(v3.Reason == "MISSING_PHASE", "R10 缺相位原因应为 MISSING_PHASE，实际=" + v3.Reason);

            var censored = Window(4);
            for (int i = 0; i < 4; i++) { SetV(censored, i, 0); SetA(censored, i, 0); }
            var v4 = Bug1ProbeJudge.Judge(censored);
            Assert(v4.Label == Bug1Label.Unknown, "R10 看不到释放必须 UNKNOWN(RIGHT_CENSORED)，" + Dump(v4));
            Assert(v4.Reason == "RIGHT_CENSORED", "R10 截断原因应为 RIGHT_CENSORED，实际=" + v4.Reason);
            Assert(!v4.Valid, "R10 截断必须 valid=false");
            // T79：纯右截断（无任何闭合事件）→ strict=0、三元组全 -1、保留截断范围、扫描不穷尽。
            Assert(v4.StrictM1Events == 0, "R10 纯截断不得计入 M1，" + Dump(v4));
            Assert(v4.CandidateA == -1 && v4.CandidateB == -1 && v4.ReleaseFrame == -1,
                "R10 纯截断不得虚报候选三元组，" + Dump(v4));
            Assert(v4.RightCensoredSegments == 1 && v4.RightCensoredRanges.Count == 1
                && v4.RightCensoredRanges[0].StallStartFrame == 0 && v4.RightCensoredRanges[0].StallEndFrame == 3
                && v4.RightCensoredRanges[0].ReleaseFrame == -1,
                "R10 纯截断应记 [0,3]、release=-1 的局部范围，" + Dump(v4));
            Assert(!v4.EventScanComplete && v4.StrictM1EventsIsLowerBound,
                "R10 纯截断的扫描必须不穷尽且计数为下界，" + Dump(v4));
            Assert(!v4.SelectedEventComplete, "R10 纯截断没有代表事件，" + Dump(v4));

            // T74-F3：关联失败（缺 inputseq）与真实端点未解析，都必须先判 UNKNOWN，不得凑成 NO_THRESHOLD_EVENT。
            var noSeq = Window(5);
            for (int i = 0; i < 4; i++) { SetV(noSeq, i, 0); SetA(noSeq, i, 0); }
            SetV(noSeq, 4, 0.08);
            SetA(noSeq, 4, 0.08);
            noSeq.Frames[2].HasLastInputSeq = false;
            var v5 = Bug1ProbeJudge.Judge(noSeq);
            Assert(v5.Label == Bug1Label.Unknown, "R10 缺 inputseq 必须 UNKNOWN，" + Dump(v5));
            Assert(v5.Reason == "INPUT_SEQ_MISSING", "R10 缺 inputseq 原因应为 INPUT_SEQ_MISSING，实际=" + v5.Reason);

            var noEndpoint = Window(5);
            for (int i = 0; i < 4; i++) { SetV(noEndpoint, i, 0); SetA(noEndpoint, i, 0); }
            SetV(noEndpoint, 4, 0.08);
            SetA(noEndpoint, 4, 0.08);
            noEndpoint.Frames[2].ModelEndpointResolved = false;
            var v6 = Bug1ProbeJudge.Judge(noEndpoint);
            Assert(v6.Label == Bug1Label.Unknown, "R10 端点未解析必须 UNKNOWN，" + Dump(v6));
            Assert(v6.Reason == "MODEL_ENDPOINT_UNRESOLVED",
                "R10 端点未解析原因应为 MODEL_ENDPOINT_UNRESOLVED，实际=" + v6.Reason);

            // T79 负控：早段已有闭合 M1，但尾部真实缺相位（不是「尚无未来 release」）→ 仍 UNKNOWN，不得豁免。
            var earlyM1BadTail = Window(9);
            for (int i = 0; i < 5; i++) { SetV(earlyM1BadTail, i, i < 4 ? 0 : 0.08); SetA(earlyM1BadTail, i, 0.02 * i); }
            for (int i = 5; i < 9; i++) { SetV(earlyM1BadTail, i, 0.08); SetA(earlyM1BadTail, i, 0.08); }
            earlyM1BadTail.Frames[7].HasBeforeUpdate = false;
            var v7 = Bug1ProbeJudge.Judge(earlyM1BadTail);
            Assert(v7.Label == Bug1Label.Unknown,
                "T79 尾部真实缺相位必须 UNKNOWN，不得因早段闭合 M1 豁免，" + Dump(v7));
            Assert(v7.Reason == "MISSING_PHASE", "T79 缺相位原因应为 MISSING_PHASE，实际=" + v7.Reason);
            Assert(!v7.Valid && v7.StrictM1Events == 0,
                "T79 完整性失败时不得报闭合事件计数，" + Dump(v7));
        }

        /// <summary>R11：XZ 裁决不替代既有门——yaw 跨零仍按原判据、垂直落地不参与、非有限 yaw 仍 UNKNOWN。</summary>
        public static void Test_R11_NonHorizontalGatesStillApply()
        {
            var wrap = Window(5);
            for (int i = 0; i < 4; i++) { SetV(wrap, i, 0); SetA(wrap, i, 0); }
            SetV(wrap, 4, 0.08);
            SetA(wrap, 4, 0.08);
            for (int i = 0; i < 5; i++)
            {
                wrap.Frames[i].AYaw = i < 4 ? 359.0 : 0.5;
                wrap.Frames[i].VYaw = i < 4 ? 359.0 : 0.5;
            }
            var v1 = Bug1ProbeJudge.Judge(wrap);
            Assert(v1.Label == Bug1Label.AuthStepLocked, "R11 yaw 跨 359→0 不应把同步步进改成别的分类，" + Dump(v1));

            var landing = Window(5);
            for (int i = 0; i < 4; i++) { SetV(landing, i, 0); SetA(landing, i, 0); }
            landing.Frames[3].VPos = new Bug1Vec3(0, -0.5, 0);
            landing.Frames[3].APos = new Bug1Vec3(0, -0.5, 0);
            SetV(landing, 4, 0.08);
            SetA(landing, 4, 0.08);
            var v2 = Bug1ProbeJudge.Judge(landing);
            Assert(v2.Label == Bug1Label.AuthStepLocked, "R11 落地垂直位移不得影响 XZ 分类，" + Dump(v2));

            var nonFinite = Window(5);
            for (int i = 0; i < 4; i++) { SetV(nonFinite, i, 0); SetA(nonFinite, i, 0); }
            SetV(nonFinite, 4, 0.08);
            SetA(nonFinite, 4, 0.08);
            nonFinite.Frames[2].AYaw = double.NaN;
            var v3 = Bug1ProbeJudge.Judge(nonFinite);
            Assert(v3.Label == Bug1Label.Unknown, "R11 非有限 yaw 必须 UNKNOWN，" + Dump(v3));
            Assert(v3.Reason == "NON_FINITE", "R11 非有限原因应为 NON_FINITE，实际=" + v3.Reason);
        }

        // ---------- 真实 trace 回放助手（R03） ----------

        private static string ResolveTraceDirectory()
        {
            string env = Environment.GetEnvironmentVariable("SPF_BUG1_TRACE_JSONL");
            if (!string.IsNullOrEmpty(env) && File.Exists(env)) return Path.GetDirectoryName(env);
            string root = AppDomain.CurrentDomain.BaseDirectory;
            for (int i = 0; i < 8 && !string.IsNullOrEmpty(root); i++)
            {
                string candidate = Path.Combine(root, "启动器", "UnturnedModManager", "publish",
                    "UMM-v2.2.1-win-x64", "bug1-20261107T111121Z", "HOST");
                if (File.Exists(Path.Combine(candidate, "trace.jsonl"))) return candidate;
                DirectoryInfo parent = Directory.GetParent(root);
                root = parent == null ? null : parent.FullName;
            }
            return null;
        }

        /// <summary>只回放被审计的那一段（停段 + 释放帧），字段全部来自 trace 原始记录，不做任何硬编码填充。</summary>
        private static Bug1StepWindow ReplayCandidateSegment(string tracePath, int stallStart, int stallEnd, int releaseFrame)
        {
            var render = new Dictionary<int, string>();
            var before = new Dictionary<int, string>();
            string runBegin = null;
            foreach (string line in File.ReadLines(tracePath))
            {
                if (runBegin == null && line.Contains("\"phase\":\"RUN_BEGIN\"")) runBegin = line;
                if (!line.Contains("\"kind\":\"FRAME\"")) continue;
                int frame = (int)NumberAfter(line, "\"frame_count\":");
                if (frame < stallStart || frame > releaseFrame) continue;
                if (line.Contains("\"phase\":\"RENDER_READY\"")) render[frame] = line;
                else if (line.Contains("\"phase\":\"BEFORE_UPDATE\"")) before[frame] = line;
            }
            // 速度上限同样取自 trace 的 RUN_BEGIN，不在用例里写死 4.5。
            if (runBegin == null) return null;
            double speedCap = NumberAfter(runBegin, "\"native_speed_cap_u_s\":");
            if (!(speedCap > 0d)) return null;

            var window = new Bug1StepWindow
            {
                StepId = "step-r03",
                NativeSpeedCapUS = speedCap,
                SpeedCapKnown = true,
                FixedConditionsValid = true,
                StepFirstFrame = stallStart,
                StepLastFrame = releaseFrame,
            };
            for (int f = stallStart; f <= releaseFrame; f++)
            {
                string rr;
                if (!render.TryGetValue(f, out rr)) return null;
                string bu;
                bool hasBu = before.TryGetValue(f, out bu);
                var sample = new Bug1FrameSample
                {
                    Frame = f,
                    FixedTickId = f,
                    MonoTicks = (long)NumberAfter(rr, "\"mono_ticks\":\""),
                    // 时钟频率取自该帧 trace 记录（不再写死 10MHz）。
                    MonoFrequency = (long)NumberAfter(rr, "\"mono_frequency\":\"", 1),
                    HasBeforeUpdate = hasBu,
                    HasRenderReady = true,
                    CameraIsMain = BoolAfter(rr, "camera_main", true),
                    CameraDuplicate = BoolAfter(rr, "camera_duplicate", false),
                    ThirdCarrierVerified = BoolAfter(rr, "third_carrier_verified", true),
                    RendererVerified = BoolAfter(rr, "renderer_chain_verified", true),
                    ModelEndpointResolved = BoolAfter(rr, "model_endpoint_resolved", true),
                    APos = new Bug1Vec3(
                        NumberAfter(rr, "\"a_pos\":{\"x\":"),
                        NumberAfter(rr, "\"a_pos\":{\"x\":", 2),
                        NumberAfter(rr, "\"a_pos\":{\"x\":", 3)),
                    VPos = new Bug1Vec3(
                        NumberAfter(rr, "\"v_pos\":{\"x\":"),
                        NumberAfter(rr, "\"v_pos\":{\"x\":", 2),
                        NumberAfter(rr, "\"v_pos\":{\"x\":", 3)),
                    AYaw = NumberAfter(rr, "\"a_yaw_deg\":"),
                    VYaw = NumberAfter(rr, "\"v_yaw_deg\":"),
                };
                sample.ABeforeUpdate = hasBu
                    ? new Bug1Vec3(
                        NumberAfter(bu, "\"a_pos_before_update\":{\"x\":"),
                        NumberAfter(bu, "\"a_pos_before_update\":{\"x\":", 2),
                        NumberAfter(bu, "\"a_pos_before_update\":{\"x\":", 3))
                    : sample.APos;

                double seq = NumberAfter(rr, "\"inputseq\":");
                double rx = NumberAfter(rr, "\"rx_id\":");
                double depth = NumberAfter(rr, "\"queue_depth\":");
                sample.HasLastInputSeq = !double.IsNaN(seq);
                sample.LastInputSeq = double.IsNaN(seq) ? 0u : (uint)seq;
                sample.HasLastRxId = !double.IsNaN(rx);
                sample.LastRxId = double.IsNaN(rx) ? 0UL : (ulong)rx;
                sample.HasQueueDepth = !double.IsNaN(depth);
                sample.QueueDepth = double.IsNaN(depth) ? 0 : (int)depth;
                window.Frames.Add(sample);
            }

            var simBefore = new Dictionary<string, string>(StringComparer.Ordinal);
            var simAfter = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string line in File.ReadLines(tracePath))
            {
                if (!line.Contains("\"kind\":\"SIM\"")) continue;
                int frame = (int)NumberAfter(line, "\"frame_count\":");
                if (frame < stallStart || frame > releaseFrame) continue;
                string id = StringAfter(line, "\"sim_call_id\":");
                if (string.IsNullOrEmpty(id)) continue;
                if (line.Contains("\"phase\":\"BEFORE\"")) simBefore[id] = line;
                else if (line.Contains("\"phase\":\"AFTER\"")) simAfter[id] = line;
            }
            foreach (var pair in simAfter)
            {
                string beforeLine;
                if (!simBefore.TryGetValue(pair.Key, out beforeLine)) continue;
                string afterLine = pair.Value;
                double seq = NumberAfter(afterLine, "\"inputseq\":");
                double rx = NumberAfter(afterLine, "\"rx_id\":");
                double depth = NumberAfter(afterLine, "\"queue_depth\":");
                window.Sims.Add(new Bug1SimSample
                {
                    SimCallId = pair.Key,
                    Frame = (int)NumberAfter(afterLine, "\"frame_count\":"),
                    HasBefore = true,
                    HasAfter = true,
                    ABefore = VecAfter(beforeLine, "\"a_pos\":{\"x\":"),
                    AAfter = VecAfter(afterLine, "\"a_pos\":{\"x\":"),
                    HasInputSeq = !double.IsNaN(seq),
                    InputSeq = double.IsNaN(seq) ? 0u : (uint)seq,
                    HasRxId = !double.IsNaN(rx),
                    RxId = double.IsNaN(rx) ? 0UL : (ulong)rx,
                    HasQueueDepth = !double.IsNaN(depth),
                    QueueDepth = double.IsNaN(depth) ? 0 : (int)depth,
                });
            }
            return window;
        }

        private static Bug1Vec3 VecAfter(string line, string key)
        {
            return new Bug1Vec3(NumberAfter(line, key), NumberAfter(line, key, 2), NumberAfter(line, key, 3));
        }

        private static string StringAfter(string line, string key)
        {
            int index = line.IndexOf(key, StringComparison.Ordinal);
            if (index < 0) return null;
            index += key.Length;
            while (index < line.Length && (line[index] == '"' || line[index] == ' ')) index++;
            int end = line.IndexOf('"', index);
            return end < 0 ? null : line.Substring(index, end - index);
        }

        private static double NumberAfter(string line, string key, int component = 1)
        {
            int index = line.IndexOf(key, StringComparison.Ordinal);
            if (index < 0) return double.NaN;
            index += key.Length;
            // trace 里 rx_id / mono_ticks 是字符串编码（"123"），跳过引号再解析数字。
            while (index < line.Length && (line[index] == '"' || line[index] == ' ')) index++;
            for (int c = 1; c < component; c++)
            {
                index = line.IndexOf(":", index, StringComparison.Ordinal);
                if (index < 0) return double.NaN;
                index++;
                while (index < line.Length && (line[index] == '"' || line[index] == ' ')) index++;
            }
            int end = index;
            while (end < line.Length && (char.IsDigit(line[end]) || line[end] == '-' || line[end] == '+'
                || line[end] == '.' || line[end] == 'e' || line[end] == 'E')) end++;
            double value;
            return double.TryParse(line.Substring(index, end - index), NumberStyles.Float,
                CultureInfo.InvariantCulture, out value) ? value : double.NaN;
        }

        private static bool BoolAfter(string line, string name, bool fallback)
        {
            if (line.Contains("\"" + name + "\":true")) return true;
            if (line.Contains("\"" + name + "\":false")) return false;
            return fallback;
        }
    }
}
