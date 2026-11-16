using SDG.Unturned;
using SteamP2PFriends.Platform.Diagnostics.Patches;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace SteamP2PFriends.WhitelistTests
{
    /// <summary>
    /// t65：窗口边界归因（F1）、订阅行为（F2）、越界计数生产者（F3）。
    /// 只测测量与判定，不产任何 Bug1 根因结论。
    /// </summary>
    internal static class Bug1ProbeT65Tests
    {
        private const long Frequency = 10000000L;

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Bug1ProbeT65: " + message);
        }

        private static Bug1FrameSample Frame(int frame, bool before, bool render, double x = 0.0)
        {
            return new Bug1FrameSample
            {
                Frame = frame,
                FixedTickId = frame,
                MonoTicks = frame * Frequency / 75,
                MonoFrequency = Frequency,
                HasBeforeUpdate = before,
                HasRenderReady = render,
                CameraIsMain = true,
                ThirdCarrierVerified = true,
                RendererVerified = true,
                ModelEndpointResolved = true,
                ABeforeUpdate = new Bug1Vec3(x, 0, 0),
                APos = new Bug1Vec3(x, 0, 0),
                VPos = new Bug1Vec3(x, 0, 0),
                HasLastInputSeq = true,
                LastInputSeq = (uint)(frame + 1),
                HasLastRxId = true,
                LastRxId = (ulong)(frame + 1),
                HasQueueDepth = true,
                QueueDepth = 1,
            };
        }

        private static Bug1StepWindow NewWindow(params Bug1FrameSample[] frames)
        {
            var w = new Bug1StepWindow
            {
                StepId = "t65-step",
                NativeSpeedCapUS = 4.0,
                SpeedCapKnown = true,
                FixedConditionsValid = true,
            };
            for (int i = 0; i < frames.Length; i++) w.Frames.Add(frames[i]);
            if (frames.Length > 0)
            {
                w.StepFirstFrame = frames[0].Frame;
                w.StepLastFrame = frames[frames.Length - 1].Frame;
            }
            return w;
        }

        private static Bug1WindowBoundaryContext Ctx(string reason, bool targetAlive, bool thirdAlive,
            bool rendererAlive, bool hookFailClosed)
        {
            return new Bug1WindowBoundaryContext
            {
                EndReason = reason,
                TargetAlive = targetAlive,
                ThirdAlive = thirdAlive,
                RendererAlive = rendererAlive,
                HookFailClosed = hookFailClosed,
            };
        }

        private static Bug1WindowBounds Seal(Bug1StepWindow w, Bug1WindowBoundaryContext ctx)
        {
            Bug1WindowBounds b = Bug1ProbeWindowBoundary.Finalize(w, ctx);
            w.WindowHeadExcluded = b.HeadExcluded;
            w.WindowTailExcluded = b.TailExcluded;
            w.WindowInteriorPartial = b.InteriorPartial;
            w.WindowExcludedReason = b.ExcludedReason;
            w.WindowTailBoundaryKind = b.TailBoundaryKind;
            w.WindowHeadBoundaryKind = b.HeadBoundaryKind;
            w.WindowTailUnexplained = b.TailUnexplained;
            w.WindowTailRenderOnly = b.TailRenderOnly;
            w.WindowTailFirstFrame = b.TailFirstFrame;
            w.WindowTailLastFrame = b.TailLastFrame;
            w.CaptureState = "{\"end_reason\":\"" + ctx.EndReason + "\"}";
            return b;
        }

        private static bool HasField(List<string> fields, string prefix)
        {
            for (int i = 0; i < fields.Count; i++)
                if (fields[i] != null && fields[i].StartsWith(prefix, StringComparison.Ordinal)) return true;
            return false;
        }

        // Bug1-62：session_reset 只能关闭窗口，绝不能解释采集中途停止。
        public static void Test_T65_SessionResetNeverExplainsTail()
        {
            var w = NewWindow(
                Frame(100, true, true), Frame(101, true, true),
                Frame(102, false, false), Frame(103, false, false), Frame(104, false, false));
            Bug1WindowBounds b = Seal(w, Ctx("session_reset", false, false, false, false));
            Assert(b.TailExcluded == 0, "session_reset 不得裁剪尾部，实际 tail_excluded=" + b.TailExcluded);
            Assert(b.TailUnexplained, "尾部必须标记为未被解释");
            Assert(b.TailBoundaryKind == Bug1WindowBounds.BoundaryCaptureFailure,
                "session_reset 应归因为 capture failure，实际 " + b.TailBoundaryKind);

            Bug1Verdict v = Bug1ProbeJudge.Judge(w);
            Assert(v.Label == Bug1Label.Unknown, "应判 UNKNOWN，实际 " + v.Label);
            Assert(v.Reason == "CAPTURE_FAILURE_TAIL", "原因应为 CAPTURE_FAILURE_TAIL，实际 " + v.Reason);
            Assert(HasField(v.MissingFields, "capture_tail@102"), "必须写明尾部首帧：" + string.Join(",", v.MissingFields));
            Assert(HasField(v.MissingFields, "window_tail"), "必须带 window_tail 字段");
        }

        // Bug1-62b：即使采集面看起来健康，session_reset 也不进合法集合。
        public static void Test_T65_SessionResetStaysIllegalEvenWhenCaptureHealthy()
        {
            var w = NewWindow(
                Frame(200, true, true), Frame(201, true, true),
                Frame(202, false, false), Frame(203, false, false));
            Bug1WindowBounds b = Seal(w, Ctx("session_reset", true, true, true, false));
            Assert(b.TailExcluded == 0, "采集面健康也不能让 session_reset 变成合法边界");
            Bug1Verdict v = Bug1ProbeJudge.Judge(w);
            Assert(v.Label == Bug1Label.Unknown, "应判 UNKNOWN，实际 " + v.Label);
            Assert(v.Reason == "CAPTURE_FAILURE_TAIL", "原因应为 CAPTURE_FAILURE_TAIL，实际 " + v.Reason);
        }

        // Bug1-63：target_left 且目标确实已销毁、detour 健康——这才是合法边界。
        public static void Test_T65_TargetLeftWithHealthyCaptureTrimsTail()
        {
            var w = NewWindow(
                Frame(300, true, true), Frame(301, true, true),
                Frame(302, false, false), Frame(303, false, false), Frame(304, false, false));
            Bug1WindowBounds b = Seal(w, Ctx("target_left", false, false, false, false));
            Assert(b.TailExcluded == 3, "合法边界应裁掉 3 个尾帧，实际 " + b.TailExcluded);
            Assert(b.TailBoundaryKind == Bug1WindowBounds.BoundaryPlayerDestroyed,
                "应归因为 boundary_player_destroyed，实际 " + b.TailBoundaryKind);
            Assert(!b.TailUnexplained, "合法边界不应标记为未解释");

            Bug1Verdict v = Bug1ProbeJudge.Judge(w);
            Assert(v.Label == Bug1Label.NoThresholdEvent, "裁剪后应正常判定，实际 " + v.Label);
            Assert(v.PhaseMissingGenuine == 0, "真缺必须为 0，实际 " + v.PhaseMissingGenuine);
            Assert(v.PhaseMissingOutOfWindow == b.HeadExcluded + b.TailExcluded,
                "out_of_window 必须等于 head+tail，实际 " + v.PhaseMissingOutOfWindow);
        }

        // Bug1-69：detour 已 fail-closed 时，即使 end_reason 是 target_left，采集面也不可信，尾部不得被解释掉。
        public static void Test_T65_FailClosedHookNeverExplainsTail()
        {
            var w = NewWindow(
                Frame(800, true, true), Frame(801, true, true),
                Frame(802, false, false), Frame(803, false, false));
            Bug1WindowBounds b = Seal(w, Ctx("target_left", false, true, true, true));
            Assert(b.TailBoundaryKind == Bug1WindowBounds.BoundaryCaptureFailure,
                "fail-closed 时边界必须归为 capture_failure，实际 " + b.TailBoundaryKind);
            Assert(b.TailExcluded == 0, "fail-closed 时不得裁剪尾部，实际 " + b.TailExcluded);
            Assert(b.TailUnexplained, "fail-closed 时尾部必须标为不可解释");
            Bug1Verdict v = Bug1ProbeJudge.Judge(w);
            Assert(v.Label == Bug1Label.Unknown, "应判 UNKNOWN，实际 " + v.Label);
            Assert(v.Reason == "CAPTURE_FAILURE_TAIL", "原因应为 CAPTURE_FAILURE_TAIL，实际 " + v.Reason);
        }

        // Bug1-64：config_changed 等其它原因不解释尾部，一律 UNKNOWN。
        public static void Test_T65_ConfigChangedDoesNotExplainTail()
        {
            var w = NewWindow(
                Frame(400, true, true), Frame(401, true, true),
                Frame(402, false, false), Frame(403, false, false));
            Bug1WindowBounds b = Seal(w, Ctx("config_changed", true, true, true, false));
            Assert(b.TailExcluded == 0, "config_changed 不得裁剪尾部");
            Bug1Verdict v = Bug1ProbeJudge.Judge(w);
            Assert(v.Label == Bug1Label.Unknown, "应判 UNKNOWN，实际 " + v.Label);
            Assert(v.Reason == "CAPTURE_FAILURE_TAIL", "原因应为 CAPTURE_FAILURE_TAIL，实际 " + v.Reason);
        }

        // Bug1-68：render-only 尾部（有 RENDER_READY 无 BEFORE_UPDATE）单独识别，绝不裁成干净窗口。
        public static void Test_T65_RenderOnlyTailIsStoppedMidWindow()
        {
            var w = NewWindow(
                Frame(500, true, true), Frame(501, true, true),
                Frame(502, false, true), Frame(503, false, true), Frame(504, false, true));
            Bug1WindowBounds b = Seal(w, Ctx("session_reset", false, false, false, false));
            Assert(b.TailRenderOnly == 3, "必须识别出 3 个 render-only 尾帧，实际 " + b.TailRenderOnly);
            Assert(b.TailExcluded == 0, "render-only 尾部不得被裁掉，实际 " + b.TailExcluded);
            Bug1Verdict v = Bug1ProbeJudge.Judge(w);
            Assert(v.Label == Bug1Label.Unknown, "应判 UNKNOWN，实际 " + v.Label);
            Assert(v.Reason == "PHASE_STOPPED_MID_WINDOW", "原因应为 PHASE_STOPPED_MID_WINDOW，实际 " + v.Reason);
            Assert(v.WindowTailRenderOnly == 3, "verdict 必须带出 render-only 计数");

            // 合法边界下同一形状允许裁（边界本身解释了为什么 detour 停了）。
            var legal = NewWindow(
                Frame(600, true, true), Frame(601, true, true),
                Frame(602, false, true), Frame(603, false, true));
            Bug1WindowBounds lb = Seal(legal, Ctx("target_left", false, false, false, false));
            Assert(lb.TailExcluded == 2, "合法边界应裁掉 render-only 尾部，实际 " + lb.TailExcluded);
            Bug1Verdict lv = Bug1ProbeJudge.Judge(legal);
            Assert(lv.Reason != "PHASE_STOPPED_MID_WINDOW", "合法边界下不得再报 PHASE_STOPPED_MID_WINDOW");
        }

        // Bug1-66：head/tail/interior/remaining 恒等式，且与 genuine 不交叠。
        public static void Test_T65_WindowAccountingIdentity()
        {
            var w = NewWindow(
                Frame(700, false, true), Frame(701, false, true),      // head 2
                Frame(702, true, true),                                 // complete
                Frame(703, false, true),                                // interior partial 1
                Frame(704, true, true), Frame(705, true, true),          // complete
                Frame(706, false, false), Frame(707, false, false), Frame(708, false, false)); // tail 3
            const int original = 9;
            Bug1WindowBounds b = Seal(w, Ctx("target_left", false, false, false, false));
            Assert(b.HeadExcluded == 2, "head 应为 2，实际 " + b.HeadExcluded);
            Assert(b.TailExcluded == 3, "tail 应为 3，实际 " + b.TailExcluded);
            Assert(b.InteriorPartial == 1, "interior 应为 1，实际 " + b.InteriorPartial);
            // 边界帧只被裁掉一次：head + tail + 判定窗内剩余帧 == 原帧数；interior 半帧仍在判定窗内，不重复计入。
            Assert(b.HeadExcluded + b.TailExcluded + b.RemainingFrames == original,
                "head(" + b.HeadExcluded + ")+tail(" + b.TailExcluded + ")+remaining(" + b.RemainingFrames
                + ") 必须等于原帧数 " + original);
            Assert(b.RemainingFrames == 4, "判定窗内应剩 4 帧（2 齐全 + 1 内部半帧 + 1 齐全），实际 " + b.RemainingFrames);
            Assert(b.TailCandidateCount == 3, "候选尾帧数应为 3，实际 " + b.TailCandidateCount);

            Bug1Verdict v = Bug1ProbeJudge.Judge(w);
            Assert(v.PhaseMissingGenuine == 1, "真缺只能是内部那 1 帧，实际 " + v.PhaseMissingGenuine);
            Assert(v.PhaseMissingOutOfWindow == 5, "越界计数应为 5，实际 " + v.PhaseMissingOutOfWindow);
            Assert(v.PhaseMissingGenuine + v.PhaseMissingOutOfWindow <= original, "两类缺相位不得重叠计数");
        }

        // Bug1-65：三条边界的行为级用例——回调必须真的收口，且 end_reason 正确。
        public static void Test_T65_BoundaryEventsCloseWindow()
        {
            string root = Path.Combine(Path.GetTempPath(), "spf-t65-" + Guid.NewGuid().ToString("N"));
            try
            {
                Bug1ProbeRuntime.__TestOffline(true);
                Bug1ProbeRuntime.__TestInstallSeam(() => true);
                Bug1ProbeRuntime.__TestClearCaptureState();

                MethodInfo destroyed = typeof(Bug1ProbeRuntime).GetMethod(
                    "OnPlayerDestroyedBoundary", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                MethodInfo levelLoaded = typeof(Bug1ProbeRuntime).GetMethod(
                    "OnLevelLoadedBoundary", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                MethodInfo sessionReset = typeof(Bug1ProbeRuntime).GetMethod(
                    "OnSessionReset", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                Assert(destroyed != null, "必须存在 OnPlayerDestroyedBoundary");
                Assert(levelLoaded != null, "必须存在 OnLevelLoadedBoundary");
                Assert(sessionReset != null, "必须存在 OnSessionReset");

                // 离线构造一个 Player 实例（不跑构造/静态构造），只为让 destroy 回调的引用比较成立。
                object fake = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(Player));
                Bug1ProbeRuntime.__TestSetCaptureState(false, false, false, false, null);

                CloseBy(root, "target_left", delegate
                {
                    Bug1ProbeRuntime.__TestBindTarget((Player)fake);
                    destroyed.Invoke(null, new object[] { fake });
                });
                CloseBy(root, "level_changed", delegate { levelLoaded.Invoke(null, new object[] { 1 }); });
                CloseBy(root, "session_reset", delegate { sessionReset.Invoke(null, null); });
            }
            finally
            {
                Bug1ProbeRuntime.__TestDisable();
                Bug1ProbeRuntime.__TestInstallSeam(null);
                Bug1ProbeRuntime.__TestOffline(false);
                Bug1ProbeRuntime.__TestClearCaptureState();
                try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
            }
        }

        private static void CloseBy(string root, string expectedReason, System.Action trigger)
        {
            var config = new Bug1ProbeConfig
            {
                RunId = "t65-run",
                StepId = "t65-" + expectedReason,
                TargetSteamId = 1UL,
                OutputRoot = root,
                NativeSpeedCapUS = 4.0,
                SpeedCapKnown = true,
                SpeedTier = Bug1ProbeNativeSpeed.TierWalk,
            };
            Assert(Bug1ProbeRuntime.Enable(config), "离线 Enable 应成功：" + Bug1ProbeRuntime.LastError);
            Bug1ProbeRuntime.__TestAddSmoothFrames(3);
            trigger();
            Assert(!Bug1ProbeRuntime.Enabled, expectedReason + " 触发后必须关闭窗口（Enabled=false）");
            string[] lines = File.ReadAllLines(Path.Combine(root, "trace.jsonl"), Encoding.UTF8);
            string runEnd = FindLine(lines, "\"phase\":\"RUN_END\"");
            Assert(runEnd.Contains("\"end_reason\":\"" + expectedReason + "\""),
                "RUN_END 必须记录 end_reason=" + expectedReason + "：" + runEnd);
            Assert(runEnd.Contains("\"capture_state\":{"), "RUN_END 必须带 capture_state 五要素：" + runEnd);
        }

        private static string FindLine(string[] lines, string marker)
        {
            for (int i = 0; i < lines.Length; i++)
                if (lines[i].Contains(marker)) return lines[i];
            throw new InvalidOperationException("Bug1ProbeT65: 未找到包含 " + marker + " 的行");
        }

        // Bug1-67：订阅语句必须真的在（离线进程里 SDK 静态事件不可靠，用 IL 钉）。
        public static void Test_T65_BoundaryHookSubscriptionsExist()
        {
            MethodInfo hook = typeof(Bug1ProbeRuntime).GetMethod(
                "HookBoundaryEvents", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            Assert(hook != null, "必须存在 HookBoundaryEvents");
            int destroyed = IlContractProbe.CountFieldLoads(hook, "SDG.Unturned.Player", "onPlayerDestroyed");
            int levelLoaded = IlContractProbe.CountFieldLoads(hook, "SDG.Unturned.Level", "onLevelLoaded");
            Assert(destroyed >= 1, "HookBoundaryEvents 必须订阅 Player.onPlayerDestroyed，实际=" + destroyed);
            Assert(levelLoaded >= 1, "HookBoundaryEvents 必须订阅 Level.onLevelLoaded，实际=" + levelLoaded);
        }
    }
}
