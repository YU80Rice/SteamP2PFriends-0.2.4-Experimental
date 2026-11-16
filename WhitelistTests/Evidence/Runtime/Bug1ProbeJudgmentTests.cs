using SteamP2PFriends.Platform.Diagnostics.Patches;
using System;
using System.Collections.Generic;

namespace SteamP2PFriends.WhitelistTests
{
    /// <summary>
    /// T7 / Bug1 仪表判定器自动化用例：纯函数输入输出，覆盖 M1、AUTH_STEP_LOCKED、
    /// UNKNOWN（缺样/截断/相位错误）与 NO_THRESHOLD_EVENT，并验证互斥性。
    /// 本套用例只证明"判定能力"，不构成 Bug1 根因结论。
    /// </summary>
    internal static class Bug1ProbeJudgmentTests
    {
        private const long Frequency = 10000000L; // 10MHz，dt=1/75 时每帧 133333 tick
        private const double SpeedCap = 5.0;

        /// <summary>构造一个相位完整、组态合法、V 稳定的窗口；各用例再覆盖差异部分。</summary>
        private static Bug1StepWindow BaseWindow(int frameCount)
        {
            var window = new Bug1StepWindow
            {
                StepId = "step-test",
                NativeSpeedCapUS = SpeedCap,
                SpeedCapKnown = true,
                FixedConditionsValid = true,
                StepFirstFrame = 0,
                StepLastFrame = frameCount - 1,
            };
            for (int i = 0; i < frameCount; i++)
            {
                var frame = new Bug1FrameSample
                {
                    Frame = i,
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
                    HasLastInputSeq = true,
                    LastInputSeq = (uint)(i + 1),
                    HasLastRxId = true,
                    LastRxId = (ulong)(i + 1),
                    HasQueueDepth = true,
                    QueueDepth = 1,
                };
                window.Frames.Add(frame);
            }
            return window;
        }

        private static void SetV(Bug1StepWindow window, int frame, double x)
        {
            var sample = window.Frames[frame];
            sample.VPos = new Bug1Vec3(x, 0, 0);
        }

        private static void SetA(Bug1StepWindow window, int frame, double x)
        {
            var sample = window.Frames[frame];
            sample.APos = new Bug1Vec3(x, 0, 0);
            sample.ABeforeUpdate = new Bug1Vec3(x, 0, 0);
        }

        private static void AddSim(Bug1StepWindow window, int frame, string id, double beforeX, double afterX)
        {
            window.Sims.Add(new Bug1SimSample
            {
                SimCallId = id,
                Frame = frame,
                HasBefore = true,
                HasAfter = true,
                ABefore = new Bug1Vec3(beforeX, 0, 0),
                AAfter = new Bug1Vec3(afterX, 0, 0),
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
            if (!condition) throw new InvalidOperationException("Bug1Probe: " + message);
        }

        /// <summary>规格 §5.3 主例：V 停 4 帧后 0.08 释放，A 同步前进 → M1_STALL_CATCHUP。</summary>
        public static void Test_M1StallCatchup_SyntheticCanonical()
        {
            var window = BaseWindow(5);
            for (int i = 0; i < 4; i++) SetV(window, i, 0);
            SetV(window, 4, 0.08);
            for (int i = 0; i < 5; i++) SetA(window, i, 0.02 * i);
            AddSim(window, 0, "sim-1", 0.0, 0.02);

            var verdict = Bug1ProbeJudge.Judge(window);

            Assert(verdict.Label == Bug1Label.M1StallCatchup, "canonical 例应判 M1，实际=" + verdict.Label + " reason=" + verdict.Reason + " candA=" + verdict.CandidateA + " candB=" + verdict.CandidateB + " spanXZ=" + verdict.RenderRootSpanXZ);
            Assert(verdict.Valid, "canonical 例应 valid");
            Assert(verdict.CandidateA == 0 && verdict.CandidateB == 3, "候选区间应为 [0,3]，实际=[" + verdict.CandidateA + "," + verdict.CandidateB + "]");
            Assert(verdict.ReleaseFrame == 4, "释放帧应为 4，实际=" + verdict.ReleaseFrame);
            // T71/M1-XZ-RELATIVE-v2：M1 由同相位落后见证 + 回补收敛决定，水平渲染跨度只是旁证。
            Assert(verdict.LagWitnessFrame == 1, "落后见证应落在停段内首帧 1，实际=" + verdict.LagWitnessFrame);
            Assert(verdict.RenderRootSpanXZ > verdict.EpsilonU, "水平渲染跨度应 > ε，实际=" + verdict.RenderRootSpanXZ);
            Assert(verdict.LagBeforeReleaseU > verdict.LagAfterReleaseU, "释放后相对差距必须收敛");
            Assert(verdict.StrictM1Events == 1, "M1 事件计数应为 1，实际=" + verdict.StrictM1Events);
            Assert(verdict.M1RuleRevision == "M1-XZ-RELATIVE-v2" && verdict.MotionAxes == "XZ", "必须带规则修订号与运动轴");

            // T79/CLOSED_EVENT_PRIORITY-v1：同一闭合 M1 之后再接静止尾段（无未来释放）→ M1 与三元组保持不变，
            // 只把尾段记成局部右截断，并声明事件扫描不穷尽。
            var tailed = BaseWindow(9);
            for (int i = 0; i < 5; i++) { SetV(tailed, i, i < 4 ? 0 : 0.08); SetA(tailed, i, 0.02 * i); }
            for (int i = 5; i < 9; i++) { SetV(tailed, i, 0.08); SetA(tailed, i, 0.08); }
            AddSim(tailed, 0, "sim-1", 0.0, 0.02);

            var tailedVerdict = Bug1ProbeJudge.Judge(tailed);
            Assert(tailedVerdict.Label == Bug1Label.M1StallCatchup,
                "T79 闭合 M1 不得被尾段推翻，实际=" + tailedVerdict.Label + " reason=" + tailedVerdict.Reason);
            Assert(tailedVerdict.Valid, "T79 闭合 M1 的 valid 应为 true");
            Assert(tailedVerdict.StrictM1Events == 1, "T79 闭合 M1 计数应仍为 1，实际=" + tailedVerdict.StrictM1Events);
            Assert(tailedVerdict.CandidateA == 0 && tailedVerdict.CandidateB == 3 && tailedVerdict.ReleaseFrame == 4,
                "T79 候选三元组应保持 0/3/4，实际=" + tailedVerdict.CandidateA + "/" + tailedVerdict.CandidateB + "/" + tailedVerdict.ReleaseFrame);
            Assert(tailedVerdict.SelectedEventComplete, "T79 代表事件应完整");
            Assert(!tailedVerdict.EventScanComplete && tailedVerdict.StrictM1EventsIsLowerBound, "T79 尾段令扫描不穷尽");
            Assert(tailedVerdict.RightCensoredSegments == 1 && tailedVerdict.RightCensoredRanges[0].StallStartFrame == 4,
                "T79 尾段应记 [4,8] 右截断，实际段数=" + tailedVerdict.RightCensoredSegments);
        }

        /// <summary>
        /// T71/M1-XZ-RELATIVE-v2 R02：A 与 V 同步步进（停段根也没推进，释放同拍）→ AUTH_STEP_LOCKED，且与 M1 互斥。
        /// 旧夹具（A 不动、V 单跳）在新规则下既不是 M1 也不是 AUTH（增量不匹配），已改为本同步例。
        /// </summary>
        public static void Test_AuthStepLocked_SameStallDifferentAuthority()
        {
            var window = BaseWindow(5);
            for (int i = 0; i < 4; i++) { SetV(window, i, 0); SetA(window, i, 0); }
            SetV(window, 4, 0.08);
            SetA(window, 4, 0.08);
            AddSim(window, 2, "sim-2", 0.0, 0.0);

            var verdict = Bug1ProbeJudge.Judge(window);

            Assert(verdict.Label == Bug1Label.AuthStepLocked, "A/V 同拍步进应判 AUTH_STEP_LOCKED，实际=" + verdict.Label + " reason=" + verdict.Reason);
            Assert(verdict.RenderRootSpanXZ <= verdict.EpsilonU, "停段根未推进，水平跨度应 ≤ ε，实际=" + verdict.RenderRootSpanXZ);
            Assert(verdict.StrictM1Events == 0, "AUTH 不得同时计入 M1，实际=" + verdict.StrictM1Events);
            Assert(verdict.AuthSegments >= 1, "必须记到 AUTH 段，实际=" + verdict.AuthSegments);
            Assert(verdict.Label != Bug1Label.M1StallCatchup, "同一候选必须互斥");
        }

        /// <summary>无停帧候选的完整平滑段 → NO_THRESHOLD_EVENT，不得当成因果证据。</summary>
        public static void Test_SmoothSegment_NoThresholdEvent()
        {
            var window = BaseWindow(6);
            for (int i = 0; i < 6; i++)
            {
                SetV(window, i, 0.02 * i);
                SetA(window, i, 0.02 * i);
            }

            var verdict = Bug1ProbeJudge.Judge(window);

            Assert(verdict.Label == Bug1Label.NoThresholdEvent, "平滑段应判 NO_THRESHOLD_EVENT，实际=" + verdict.Label + " reason=" + verdict.Reason);
            Assert(verdict.Valid, "完整平滑段应 valid（只是无事件）");
            Assert(verdict.CandidateA == -1 && verdict.CandidateB == -1, "不得虚报候选区间");
            Assert(verdict.RenderRootSpanXZ == 0.0, "平滑段水平渲染跨度应为 0，实际=" + verdict.RenderRootSpanXZ);
        }

        /// <summary>帧缺失 → UNKNOWN，并列出缺失字段/帧，绝不允许内插补齐。</summary>
        public static void Test_MissingFrame_UnknownWithFieldNames()
        {
            var window = BaseWindow(5);
            for (int i = 0; i < 4; i++) SetV(window, i, 0);
            SetV(window, 4, 0.08);
            window.Frames.RemoveAt(2);

            var verdict = Bug1ProbeJudge.Judge(window);

            Assert(verdict.Label == Bug1Label.Unknown, "缺帧应判 UNKNOWN，实际=" + verdict.Label);
            Assert(verdict.MissingFields.Count > 0, "UNKNOWN 必须写明缺失字段名");
            Assert(verdict.MissingFrames.Contains(2), "缺失帧列表应含 2");
            Assert(!verdict.Valid, "缺帧窗口不得标记 valid");
        }

        /// <summary>simulate 只采到 before 未采到 after（相位错误）→ UNKNOWN。</summary>
        public static void Test_UnpairedSim_Unknown()
        {
            var window = BaseWindow(5);
            for (int i = 0; i < 4; i++) SetV(window, i, 0);
            SetV(window, 4, 0.08);
            window.Sims.Add(new Bug1SimSample
            {
                SimCallId = "sim-open",
                Frame = 3,
                HasBefore = true,
                HasAfter = false,
                ABefore = new Bug1Vec3(0, 0, 0),
            });

            var verdict = Bug1ProbeJudge.Judge(window);

            Assert(verdict.Label == Bug1Label.Unknown, "未配对 sim 应判 UNKNOWN，实际=" + verdict.Label);
            Assert(verdict.UnpairedSimIds.Contains("sim-open"), "必须列出未配对的 sim_call_id");
        }

        /// <summary>停帧段延续到窗口末尾（右截断）→ UNKNOWN，不得猜一个释放点。</summary>
        public static void Test_RightCensored_Unknown()
        {
            var window = BaseWindow(6);
            for (int i = 0; i < 6; i++) SetV(window, i, 0);

            var verdict = Bug1ProbeJudge.Judge(window);

            Assert(verdict.Label == Bug1Label.Unknown, "右截断应判 UNKNOWN，实际=" + verdict.Label);
            Assert(verdict.Reason.Contains("right_censored") || verdict.Reason.Contains("RIGHT_CENSORED"), "reason 应标记右截断，实际=" + verdict.Reason);

            // T79：纯右截断没有闭合事件 → strict=0、三元组 -1、保留范围、扫描不穷尽。
            Assert(verdict.StrictM1Events == 0, "右截断不得计入 M1，实际=" + verdict.StrictM1Events);
            Assert(verdict.CandidateA == -1 && verdict.CandidateB == -1 && verdict.ReleaseFrame == -1,
                "右截断不得虚报候选三元组");
            Assert(verdict.RightCensoredSegments == 1 && verdict.RightCensoredRanges[0].ReleaseFrame == -1,
                "右截断应记局部范围且 release=-1");
            Assert(!verdict.Valid && !verdict.EventScanComplete && verdict.StrictM1EventsIsLowerBound,
                "右截断必须 valid=false 且扫描不穷尽");

            // T79：外部 RightCensored=true 但窗口内定位不到任何未闭合尾段 → 含义不明，维持 UNKNOWN。
            var smooth = BaseWindow(5);
            for (int i = 0; i < 5; i++) SetV(smooth, i, 0.02 * i);
            smooth.RightCensored = true;
            var orphan = Bug1ProbeJudge.Judge(smooth);
            Assert(orphan.Label == Bug1Label.Unknown && orphan.Reason == "RIGHT_CENSORED",
                "外部截断标志无法定位时必须 UNKNOWN(RIGHT_CENSORED)，实际=" + orphan.Label + "/" + orphan.Reason);
            // R14-02：定位不到尾段时 ranges 为空，扫描其实已穷尽；导出不得谎称「未穷尽」。
            Assert(orphan.EventScanComplete, "定位不到尾段时 event_scan_complete 必须为 true");
            Assert(!orphan.StrictM1EventsIsLowerBound, "定位不到尾段时计数不是下界，is_lower_bound 必须为 false");

            // T79：同样的外部标志，但确实存在可定位的尾段 → 早段闭合 M1 仍成立。
            var located = BaseWindow(9);
            for (int i = 0; i < 5; i++) { SetV(located, i, i < 4 ? 0 : 0.08); SetA(located, i, 0.02 * i); }
            for (int i = 5; i < 9; i++) { SetV(located, i, 0.08); SetA(located, i, 0.08); }
            located.RightCensored = true;
            var locatedVerdict = Bug1ProbeJudge.Judge(located);
            Assert(locatedVerdict.Label == Bug1Label.M1StallCatchup,
                "外部截断标志可定位时闭合 M1 仍成立，实际=" + locatedVerdict.Label + "/" + locatedVerdict.Reason);
        }

        /// <summary>同帧重复相机采样（相位错误）→ UNKNOWN。</summary>
        public static void Test_DuplicateCameraSample_Unknown()
        {
            var window = BaseWindow(5);
            for (int i = 0; i < 4; i++) SetV(window, i, 0);
            SetV(window, 4, 0.08);
            window.Frames[3].CameraDuplicate = true;

            var verdict = Bug1ProbeJudge.Judge(window);

            Assert(verdict.Label == Bug1Label.Unknown, "重复相机采样应判 UNKNOWN，实际=" + verdict.Label);
            Assert(verdict.MissingFields.Count > 0, "必须写明缺失/无效字段名");
        }

        /// <summary>非有限坐标是采集中断而非"没动"→ UNKNOWN，不得按 0 参与距离。</summary>
        public static void Test_NonFiniteSample_Unknown()
        {
            var window = BaseWindow(5);
            for (int i = 0; i < 4; i++) SetV(window, i, 0);
            window.Frames[2].VPos = new Bug1Vec3(double.NaN, 0, 0);
            SetV(window, 4, 0.08);

            var verdict = Bug1ProbeJudge.Judge(window);

            Assert(verdict.Label == Bug1Label.Unknown, "NaN 样本应判 UNKNOWN，实际=" + verdict.Label);
            Assert(verdict.MissingFields.Count > 0, "必须写明非有限字段");

            // T81-F1：时钟门必须有可红用例，不能只有代码没有断言。
            var badFrequency = BaseWindow(5);
            for (int i = 0; i < 5; i++) SetV(badFrequency, i, 0.02 * i);
            badFrequency.Frames[2].MonoFrequency = 0;
            var clockInvalid = Bug1ProbeJudge.Judge(badFrequency);
            Assert(clockInvalid.Label == Bug1Label.Unknown && clockInvalid.Reason == "CLOCK_INVALID",
                "mono_frequency<=0 必须 UNKNOWN(CLOCK_INVALID)，实际=" + clockInvalid.Label + "/" + clockInvalid.Reason);

            var nonMonotonic = BaseWindow(5);
            for (int i = 0; i < 5; i++) SetV(nonMonotonic, i, 0.02 * i);
            nonMonotonic.Frames[3].MonoTicks = nonMonotonic.Frames[2].MonoTicks;
            var clockBackwards = Bug1ProbeJudge.Judge(nonMonotonic);
            Assert(clockBackwards.Label == Bug1Label.Unknown && clockBackwards.Reason == "CLOCK_NOT_MONOTONIC",
                "时钟回退必须 UNKNOWN(CLOCK_NOT_MONOTONIC)，实际=" + clockBackwards.Label + "/" + clockBackwards.Reason);
        }

        /// <summary>释放位移恰好等于阈值 B 时不触发（必须严格大于）。</summary>
        public static void Test_ReleaseExactlyAtBoundary_NoThresholdEvent()
        {
            var window = BaseWindow(5);
            for (int i = 0; i < 4; i++) SetV(window, i, 0);
            double dt = 1.0 / 75.0;
            double boundary = Bug1ProbeJudge.SpeedBoundary(SpeedCap, dt);
            SetV(window, 4, boundary);

            var verdict = Bug1ProbeJudge.Judge(window);

            Assert(verdict.Label != Bug1Label.M1StallCatchup && verdict.Label != Bug1Label.AuthStepLocked,
                "恰好等于 B 不得当成候选，实际=" + verdict.Label);
            Assert(verdict.Reason.Contains("RELEASE_NOT_EXCEEDED") || verdict.Label == Bug1Label.NoThresholdEvent,
                "应按未超过释放门处理，实际 reason=" + verdict.Reason);
        }

        /// <summary>两次微小位移不构成停帧段，也不得升级为 M1。</summary>
        public static void Test_TwoSmallDisplacements_NoStallCandidate()
        {
            var window = BaseWindow(5);
            SetV(window, 0, 0);
            SetV(window, 1, 0.0005);
            SetV(window, 2, 0);
            SetV(window, 3, 0.0005);
            SetV(window, 4, 0.0015);
            for (int i = 0; i < 5; i++) SetA(window, i, 0.05 * i);

            var verdict = Bug1ProbeJudge.Judge(window);

            Assert(verdict.Label != Bug1Label.M1StallCatchup, "微小抖动不得判 M1，实际=" + verdict.Label);
            Assert(verdict.RenderRootSpanXZ <= verdict.EpsilonU || verdict.Label == Bug1Label.Unknown || verdict.Label == Bug1Label.NoThresholdEvent,
                "微抖动场景只允许无事件或 UNKNOWN");
        }

        /// <summary>
        /// T71/M1-XZ-RELATIVE-v2 修订：权威中途推进又回到原点时，释放后相对差距反而变大（不收敛），
        /// 因此不再是 M1 —— 旧 T4 三维 root_span 口径会把「回位」当成 M1 证据，已被本修订取代。
        /// 该几何段记 NON_M1（ERROR_NOT_CONVERGED），不是 AUTH（停段根推进过）也不是 M1。
        /// </summary>
        public static void Test_AuthorityMovesAndReturns_IsNotM1()
        {
            var window = BaseWindow(5);
            for (int i = 0; i < 4; i++) SetV(window, i, 0);
            SetV(window, 4, 0.08);
            window.Frames[0].APos = new Bug1Vec3(0, 0, 0);
            window.Frames[1].APos = new Bug1Vec3(0.06, 0, 0);
            window.Frames[2].APos = new Bug1Vec3(0, 0, 0);
            window.Frames[3].APos = new Bug1Vec3(0, 0, 0);
            window.Frames[4].APos = new Bug1Vec3(0, 0, 0);

            var verdict = Bug1ProbeJudge.Judge(window);

            Assert(verdict.Label != Bug1Label.M1StallCatchup, "回位不构成回补收敛，不得判 M1，实际=" + verdict.Label);
            Assert(verdict.StrictM1Events == 0, "不得计入 M1 事件，实际=" + verdict.StrictM1Events);
            Assert(verdict.Label == Bug1Label.NoThresholdEvent, "应落无阈值事件，实际=" + verdict.Label + " reason=" + verdict.Reason);
            Assert(verdict.CandidateRejections.Contains("ERROR_NOT_CONVERGED@4"),
                "必须记录回补不收敛的拒绝原因，实际=[" + string.Join(",", verdict.CandidateRejections) + "]");
        }

        /// <summary>§5.4 yaw 最短差：359°→1° 应得 +2°，而非 358°。</summary>
        public static void Test_YawShortestDelta_WrapsAcrossZero()
        {
            double delta = Bug1ProbeJudge.ShortestYawDelta(359.0, 1.0);
            Assert(Math.Abs(delta - 2.0) < 1e-9, "359→1 的最短差应为 +2，实际=" + delta);
            double reverse = Bug1ProbeJudge.ShortestYawDelta(1.0, 359.0);
            Assert(Math.Abs(reverse + 2.0) < 1e-9, "1→359 的最短差应为 -2，实际=" + reverse);
            double boundary = Bug1ProbeJudge.ShortestYawDelta(0.0, 180.0);
            Assert(Math.Abs(Math.Abs(boundary) - 180.0) < 1e-9, "0→180 应为 ±180，实际=" + boundary);
        }

        /// <summary>默认关闭：生产登记入口必须静默返回，不解析 target、不安装 hook、不留日志。</summary>
        public static void Test_DefaultOff_RegisterManualInstallsNothing()
        {
            Assert(!Bug1ProbeRuntime.Enabled, "默认状态必须是关闭");
            var harmony = new HarmonyLib.Harmony("spf.t7.default-off." + Guid.NewGuid().ToString("N"));
            bool ok = Bug1ProbePatch.RegisterManual(harmony);
            Assert(ok, "默认关闭时 RegisterManual 应返回 true（视为已处理）");
            Assert(!Bug1ProbePatch.Registered, "默认关闭不得安装任何 hook");
            Assert(Bug1ProbePatch.SimulateTarget == null && Bug1ProbePatch.UpdateTarget == null,
                "默认关闭不得解析原生 target");
            Assert(!Bug1ProbeRuntime.Bound, "默认关闭不得绑定远端玩家");
            harmony.UnpatchSelf();
        }
    }
}
