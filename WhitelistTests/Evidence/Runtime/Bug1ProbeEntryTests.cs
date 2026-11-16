using SteamP2PFriends.Platform.Diagnostics.Patches;
using System;

namespace SteamP2PFriends.WhitelistTests
{
    /// <summary>
    /// t45：开启入口与相位覆盖用例。
    /// 一半是生产入口的纯决策逻辑（RunId/StepId 生成、目标匹配、前置拒绝码、原生速度上限公式），
    /// 一半是判定器的相位覆盖硬判（缺 AFTER_ENQUEUE / AFTER_DEQUEUE 必须 UNKNOWN，不得落成 NO_THRESHOLD_EVENT）。
    /// 只证明测量能力与失败形态，不构成 Bug1 根因结论。
    /// </summary>
    internal static class Bug1ProbeEntryTests
    {
        private const long Frequency = 10000000L;
        private const double SpeedCap = 5.0;

        /// <summary>平滑无事件窗口：帧相位齐全、位置匀速微动，判定器正常应给 NO_THRESHOLD_EVENT。</summary>
        private static Bug1StepWindow SmoothWindow(int frameCount)
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
                double x = 0.02 * i;
                window.Frames.Add(new Bug1FrameSample
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
                    ABeforeUpdate = new Bug1Vec3(x, 0, 0),
                    APos = new Bug1Vec3(x, 0, 0),
                    VPos = new Bug1Vec3(x, 0, 0),
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

        public static void Test_PhaseCoverage_MissingEnqueueForcesUnknown()
        {
            var window = SmoothWindow(6);
            window.RxEnqueueRecords = 0;
            window.RxDequeueRecords = 4;

            var verdict = Bug1ProbeJudge.Judge(window);

            Assert(verdict.Label == Bug1Label.Unknown,
                "缺 AFTER_ENQUEUE 必须 UNKNOWN，实际=" + verdict.Label);
            Assert(verdict.Label != Bug1Label.NoThresholdEvent, "缺相位不得落成 NO_THRESHOLD_EVENT");
            Assert(verdict.MissingFields.Contains("AFTER_ENQUEUE"),
                "missing_fields 必须点名 AFTER_ENQUEUE，实际=[" + string.Join(",", verdict.MissingFields.ToArray()) + "]");
            Assert(verdict.Reason.Contains("PHASE_COVERAGE_MISSING"), "reason 应标 PHASE_COVERAGE_MISSING，实际=" + verdict.Reason);
        }

        public static void Test_PhaseCoverage_MissingDequeueForcesUnknown()
        {
            var window = SmoothWindow(6);
            window.RxEnqueueRecords = 4;
            window.RxDequeueRecords = 0;

            var verdict = Bug1ProbeJudge.Judge(window);

            Assert(verdict.Label == Bug1Label.Unknown, "缺 AFTER_DEQUEUE 必须 UNKNOWN，实际=" + verdict.Label);
            Assert(verdict.MissingFields.Contains("AFTER_DEQUEUE"), "missing_fields 必须点名 AFTER_DEQUEUE");
            Assert(!verdict.MissingFields.Contains("AFTER_ENQUEUE"), "入队相位已覆盖，不得误报");
        }

        public static void Test_PhaseCoverage_BothPresentKeepsJudgment()
        {
            var window = SmoothWindow(6);
            window.RxEnqueueRecords = 4;
            window.RxDequeueRecords = 4;

            var verdict = Bug1ProbeJudge.Judge(window);

            Assert(verdict.Label == Bug1Label.NoThresholdEvent,
                "两相位都覆盖时不得因新门变 UNKNOWN，实际=" + verdict.Label);
            Assert(verdict.Valid, "完整平滑段仍应 valid");
        }

        public static void Test_PhaseCoverage_UnsetKeepsSyntheticSemantics()
        {
            var window = SmoothWindow(6); // RxEnqueueRecords / RxDequeueRecords 保持 null

            var verdict = Bug1ProbeJudge.Judge(window);

            Assert(verdict.Label == Bug1Label.NoThresholdEvent,
                "非生产合成窗口（计数为 null）不参与相位硬判，实际=" + verdict.Label);
        }

        public static void Test_Entry_RejectsAlreadyRunning()
        {
            string error = Bug1ProbeEntryLogic.Validate(true, "run", "step-1", 1UL, true, 5d, "dir");
            Assert(error == Bug1ProbeEntryLogic.ErrorAlreadyRunning, "运行中再次开启应 ALREADY_RUNNING，实际=" + error);
        }

        public static void Test_Entry_RejectsMissingTargetAndUnresolvedCap()
        {
            Assert(Bug1ProbeEntryLogic.Validate(false, "run", "step-1", 0UL, true, 5d, "dir")
                == Bug1ProbeEntryLogic.ErrorTargetMissing, "缺目标应 TARGET_STEAM_ID_MISSING");
            Assert(Bug1ProbeEntryLogic.Validate(false, "run", "step-1", 1UL, false, 5d, "dir")
                == Bug1ProbeEntryLogic.ErrorSpeedCapUnresolved, "速度上限未知应拒绝");
            Assert(Bug1ProbeEntryLogic.Validate(false, "run", "step-1", 1UL, true, double.NaN, "dir")
                == Bug1ProbeEntryLogic.ErrorSpeedCapUnresolved, "NaN 上限应拒绝");
            Assert(Bug1ProbeEntryLogic.Validate(false, "run", "step-1", 1UL, true, -1d, "dir")
                == Bug1ProbeEntryLogic.ErrorSpeedCapUnresolved, "负数上限应拒绝");
            Assert(Bug1ProbeEntryLogic.Validate(false, "run", "step-1", 1UL, true, 5d, "")
                == Bug1ProbeEntryLogic.ErrorOutputRootUnresolved, "输出根为空应拒绝");
            Assert(Bug1ProbeEntryLogic.Validate(false, "run", "step-1", 1UL, true, 5d, "dir") == null,
                "四项齐备时应允许开启");
        }

        public static void Test_Entry_RunIdAndStepIdAreGenerated()
        {
            var utc = new DateTime(2026, 11, 6, 19, 5, 9, DateTimeKind.Utc);
            Assert(Bug1ProbeEntryLogic.BuildRunId(utc) == "bug1-20261106T190509Z",
                "RunId 应为 UTC 时间戳，实际=" + Bug1ProbeEntryLogic.BuildRunId(utc));
            Assert(Bug1ProbeEntryLogic.BuildStepId(1) == "step-1", "StepId 应自动生成 step-1");
            Assert(Bug1ProbeEntryLogic.BuildRunId(utc.AddSeconds(1)) != Bug1ProbeEntryLogic.BuildRunId(utc),
                "不同秒不得生成同一 RunId");
        }

        public static void Test_Entry_TargetSelectorMatchesNameOrSteamId()
        {
            const ulong id = 76561198000000000UL;
            Assert(Bug1ProbeEntryLogic.MatchesTarget("Alice", "alice", id), "玩家名匹配应忽略大小写");
            Assert(Bug1ProbeEntryLogic.MatchesTarget("76561198000000000", "alice", id), "SteamID64 字面量应匹配");
            Assert(!Bug1ProbeEntryLogic.MatchesTarget("", "alice", id), "空选择器不得匹配");
            Assert(!Bug1ProbeEntryLogic.MatchesTarget("Bob", "alice", id), "不相关名字不得匹配");
        }

        public static void Test_NativeSpeedCap_SprintFormula()
        {
            double cap;
            string evidence;
            bool ok = Bug1ProbeNativeSpeed.Compute(7d, 1.25d, 1.1d, out cap, out evidence);

            Assert(ok, "7 * 1.25 * 1.1 应解析成功，evidence=" + evidence);
            Assert(Math.Abs(cap - 9.625d) < 1e-9, "cap 应为 9.625，实际=" + cap);
            Assert(evidence.Contains("SPEED_SPRINT=7"), "证据须写明原生常量来源，实际=" + evidence);
        }

        public static void Test_NativeSpeedCap_UnresolvedRefuses()
        {
            double cap;
            string evidence;
            Assert(!Bug1ProbeNativeSpeed.Compute(0d, 1d, 1d, out cap, out evidence), "常量缺失必须拒绝");
            Assert(!Bug1ProbeNativeSpeed.Compute(7d, double.NaN, 1d, out cap, out evidence), "倍率非法必须拒绝");
            Assert(!Bug1ProbeNativeSpeed.Compute(7d, 1.25d, 20d, out cap, out evidence), "越界上限必须拒绝");
            bool normalOk = Bug1ProbeNativeSpeed.Compute(7d, 1.25d, 1.1d, out cap, out evidence);
            Assert(normalOk, "正常值必须接受（反向校验），实际拒绝原因=" + evidence);
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Bug1Probe: " + message);
        }
    }
}
