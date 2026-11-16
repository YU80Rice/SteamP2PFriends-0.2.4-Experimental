using SDG.Unturned;
using SteamP2PFriends.Platform.Diagnostics.Patches;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;

namespace SteamP2PFriends.WhitelistTests
{
    /// <summary>
    /// t48：Bug1 入口修复轮用例。
    /// 覆盖 R8-01（判空自爆）、F1（相位计数断链）、R8-02（fail-closed 开启）、
    /// R8-03（启动期重试判定）、R8-04（关闭卸 detour）、F3（目录时机）、F4（走/跑两档）。
    /// 只证明测量能力与失败形态，不构成 Bug1 根因结论。
    /// </summary>
    internal static class Bug1ProbeEntryRepairTests
    {
        private const long Frequency = 10000000L;
        private const double SpeedCap = 5.0;

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Bug1ProbeRepair: " + message);
        }

        /// <summary>未初始化实例：playerID/player 保持 null，用来复现真实主机上的判空路径。</summary>
        private static SteamPlayer BareClient()
        {
            return (SteamPlayer)FormatterServices.GetUninitializedObject(typeof(SteamPlayer));
        }

        private static Bug1ClientSnapshot Snap(ulong steamId, string name, int index, bool hasPlayer)
        {
            var snapshot = new Bug1ClientSnapshot();
            snapshot.SourceIndex = index;
            snapshot.HasPlayerId = true;
            snapshot.HasPlayer = hasPlayer;
            snapshot.SteamId = steamId;
            snapshot.PlayerName = name;
            return snapshot;
        }

        /// <summary>
        /// R8-01：真实主机的 SteamPlayer.playerID 可能是 null，旧写法 `client.playerID == null`
        /// 会走 SteamPlayerID 重载的 == 并抛 NRE。快照必须不抛，且该 client 被判为跳过。
        /// </summary>
        public static void Test_R8_01_NullPlayerIdClientIsSkipped()
        {
            SteamPlayer ghost = BareClient();
            Bug1ClientSnapshot snap = Bug1ProbeTargetSelector.Snapshot(ghost, 0);
            Assert(!snap.IsNull, "非 null client 不得被判成 null-client");
            Assert(!snap.HasPlayerId, "未初始化 client 的 playerID 必须判为 null");
            Assert(!snap.HasPlayer, "未初始化 client 的 player 必须判为 null");

            // 隐患证明：SDK 重载的 == 任一侧 null 都抛 NRE —— 这就是旧写法在真实主机必崩的原因。
            bool hazard = false;
            try { SteamPlayerID id = ghost.playerID; if (id == null) { } }
            catch (NullReferenceException) { hazard = true; }
            Assert(hazard, "SteamPlayerID.== 对 null 必须抛 NRE；否则 ReferenceEquals 的必要性证据失效");

            var clients = new List<Bug1ClientSnapshot>
            {
                Bug1ProbeTargetSelector.Snapshot(null, 0),
                snap,
                Snap(200UL, "Guest", 2, true),
            };
            Bug1TargetSelection selection = Bug1ProbeTargetSelector.Select(clients, string.Empty, 0UL);
            Assert(selection.Status == Bug1TargetSelectionStatus.Selected,
                "null client / null playerID 必须被跳过而不是中断选择，实际=" + selection.Status);
            Assert(selection.SelectedIndex == 2, "应选中唯一有效客机 index=2，实际=" + selection.SelectedIndex);
            Assert(selection.Skipped.Contains(Bug1ProbeTargetSelector.SkipNullClient), "skipped 必须记录 null-client");
            Assert(selection.Skipped.Contains(Bug1ProbeTargetSelector.SkipNullPlayerId), "skipped 必须记录 playerid-null");
        }

        /// <summary>R8-01：选择器纯逻辑——排除自己、歧义、名字与 SteamID64 匹配、缺失。</summary>
        public static void Test_R8_01_SelectorExcludesSelfAndDetectsAmbiguity()
        {
            var clients = new List<Bug1ClientSnapshot>
            {
                Snap(100UL, "Host", 0, true),
                Snap(200UL, "Alice", 1, true),
                Snap(300UL, "Bob", 2, true),
            };

            Bug1TargetSelection ambiguous = Bug1ProbeTargetSelector.Select(clients, string.Empty, 100UL);
            Assert(ambiguous.Status == Bug1TargetSelectionStatus.Ambiguous, "两个客机且无选择器必须歧义");
            Assert(ambiguous.ErrorCode == Bug1ProbeEntryLogic.ErrorTargetAmbiguous, "歧义码必须 TARGET_PLAYER_AMBIGUOUS");
            Assert(ambiguous.Skipped.Contains(Bug1ProbeTargetSelector.SkipSelf), "必须跳过本地主机自己");

            Bug1TargetSelection byName = Bug1ProbeTargetSelector.Select(clients, "alice", 100UL);
            Assert(byName.Status == Bug1TargetSelectionStatus.Selected && byName.SelectedIndex == 1,
                "名字必须大小写不敏感匹配 Alice，实际=" + byName.Status + "/" + byName.SelectedIndex);
            Assert(byName.SteamId == 200UL, "选中项 SteamId 必须是 200");

            Bug1TargetSelection byId = Bug1ProbeTargetSelector.Select(clients, "300", 100UL);
            Assert(byId.Status == Bug1TargetSelectionStatus.Selected && byId.SelectedIndex == 2, "SteamID64 选择器必须命中 Bob");

            Bug1TargetSelection notFound = Bug1ProbeTargetSelector.Select(clients, "carol", 100UL);
            Assert(notFound.Status == Bug1TargetSelectionStatus.NotFound, "无匹配必须 NotFound");
            Assert(notFound.ErrorCode == Bug1ProbeEntryLogic.ErrorTargetNotFound, "有选择器无匹配必须是 TARGET_PLAYER_NOT_FOUND");

            var onlySelf = new List<Bug1ClientSnapshot> { Snap(100UL, "Host", 0, true) };
            Bug1TargetSelection missing = Bug1ProbeTargetSelector.Select(onlySelf, string.Empty, 100UL);
            Assert(missing.Status == Bug1TargetSelectionStatus.NotFound, "只剩自己必须 NotFound");
            Assert(missing.ErrorCode == Bug1ProbeEntryLogic.ErrorTargetMissing, "空选择器无客机必须是 TARGET_STEAM_ID_MISSING");

            Bug1TargetSelection bySteamId = Bug1ProbeTargetSelector.SelectBySteamId(clients, 200UL);
            Assert(bySteamId.Status == Bug1TargetSelectionStatus.Selected && bySteamId.SelectedIndex == 1,
                "SelectBySteamId 必须命中 Alice");
            Assert(Bug1ProbeTargetSelector.SelectBySteamId(clients, 999UL).Status == Bug1TargetSelectionStatus.NotFound,
                "不存在的 SteamID 必须 NotFound");
            Assert(Bug1ProbeTargetSelector.SelectBySteamId(clients, 0UL).ErrorCode == Bug1ProbeEntryLogic.ErrorTargetMissing,
                "SteamID=0 必须是 TARGET_STEAM_ID_MISSING");
        }

        private static Bug1ProbeConfig NewConfig(string runId, string outputRoot)
        {
            return new Bug1ProbeConfig
            {
                RunId = runId,
                StepId = "step-t48",
                TargetSteamId = 1UL,
                OutputRoot = outputRoot,
                NativeSpeedCapUS = 4.0,
                NativeSpeedCapWalkUS = 4.0,
                NativeSpeedCapSprintUS = 7.0,
                SpeedTier = Bug1ProbeNativeSpeed.TierWalk,
                SpeedCapKnown = true,
            };
        }

        private static string FindLine(string[] trace, string marker)
        {
            for (int i = 0; i < trace.Length; i++) if (trace[i].Contains(marker)) return trace[i];
            return null;
        }

        private static void Cleanup(string root)
        {
            Bug1ProbeRuntime.__TestDisable();
            Bug1ProbeRuntime.__TestInstallSeam(null);
            Bug1ProbeRuntime.__TestOffline(false);
            try { if (!string.IsNullOrEmpty(root) && Directory.Exists(root)) Directory.Delete(root, true); } catch { }
        }

        /// <summary>
        /// F1：生产路径——Enable(config) 建窗（两计数置 0）→ 真实 OnEnqueue/OnDequeue 产记录 → EndStep。
        /// 旧代码不同步静态计数，这里必然 UNKNOWN(PHASE_COVERAGE_MISSING)；修好后 RUN_END 计数与判定同源。
        /// </summary>
        public static void Test_F1_ProductionPath_RecordsPhasesAndKeepsJudgment()
        {
            string root = Path.Combine(Path.GetTempPath(), "spf-t48-f1a-" + Guid.NewGuid().ToString("N"));
            var queue = new Queue<PlayerInputPacket>();
            var packet = new PlayerInputPacket();
            FieldInfo seq = typeof(PlayerInputPacket).GetField("clientSimulationFrameNumber",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert(seq != null, "原生序号字段 clientSimulationFrameNumber 必须存在");
            seq.SetValue(packet, 77u);

            try
            {
                Bug1ProbeRuntime.__TestOffline(true);
                Bug1ProbeRuntime.__TestInstallSeam(() => true);
                Assert(Bug1ProbeRuntime.Enable(NewConfig("t48-f1a", root)),
                    "Enable 必须成功，LastError=" + Bug1ProbeRuntime.LastError);
                Bug1ProbeRuntime.__TestAddSmoothFrames(6);
                Bug1ProbeRuntime.__TestBindQueue(queue, seq);

                queue.Enqueue(packet);
                Bug1ProbeRuntime.OnEnqueue(queue, packet);
                PlayerInputPacket dequeued = queue.Dequeue();
                Bug1ProbeRuntime.OnDequeue(queue, dequeued);
                Bug1ProbeRuntime.EndStep("t48-test");

                string[] trace = File.ReadAllLines(Path.Combine(root, "trace.jsonl"));
                string runEnd = FindLine(trace, "\"kind\":\"RUN_END\"");
                Assert(runEnd != null, "必须产出 RUN_END 行");
                Assert(runEnd.Contains("\"rx_after_enqueue\":1"), "RUN_END 入队计数必须是 1，实际=" + runEnd);
                Assert(runEnd.Contains("\"rx_after_dequeue\":1"), "RUN_END 出队计数必须是 1，实际=" + runEnd);
                Assert(!runEnd.Contains("PHASE_COVERAGE_MISSING"), "有相位记录时不得判缺失，实际=" + runEnd);

                string resultPath = Path.Combine(root, "result.json");
                Assert(File.Exists(resultPath), "EndStep 必须落 result.json: " + resultPath);
                string result = File.ReadAllText(resultPath);
                Assert(!result.Contains("PHASE_COVERAGE_MISSING"), "result.json 不得判相位缺失，实际=" + result);
                Assert(result.Contains("\"label\":\"NoThresholdEvent\""), "平滑窗应给 NoThresholdEvent，实际=" + result);
            }
            finally
            {
                Cleanup(root);
            }
        }

        /// <summary>F1 反例：一条相位记录都没有时必须硬判 UNKNOWN(PHASE_COVERAGE_MISSING)，并点名两相。</summary>
        public static void Test_F1_ZeroRecordsForcesUnknownPhaseCoverage()
        {
            string root = Path.Combine(Path.GetTempPath(), "spf-t48-f1b-" + Guid.NewGuid().ToString("N"));
            try
            {
                Bug1ProbeRuntime.__TestOffline(true);
                Bug1ProbeRuntime.__TestInstallSeam(() => true);
                Assert(Bug1ProbeRuntime.Enable(NewConfig("t48-f1b", root)),
                    "Enable 必须成功，LastError=" + Bug1ProbeRuntime.LastError);
                Bug1ProbeRuntime.__TestAddSmoothFrames(6);
                Bug1ProbeRuntime.EndStep("t48-test");

                string[] trace = File.ReadAllLines(Path.Combine(root, "trace.jsonl"));
                string runEnd = FindLine(trace, "\"kind\":\"RUN_END\"");
                Assert(runEnd != null, "必须产出 RUN_END 行");
                Assert(runEnd.Contains("\"rx_after_enqueue\":0"), "零记录时 RUN_END 入队计数必须是 0，实际=" + runEnd);
                Assert(runEnd.Contains("\"label\":\"Unknown\""), "零记录必须 UNKNOWN，实际=" + runEnd);
                Assert(runEnd.Contains("AFTER_ENQUEUE") && runEnd.Contains("AFTER_DEQUEUE"),
                    "RUN_END 的 missing_fields 必须点名两相，实际=" + runEnd);

                string result = File.ReadAllText(Path.Combine(root, "result.json"));
                Assert(result.Contains("PHASE_COVERAGE_MISSING"), "result.json 必须标 PHASE_COVERAGE_MISSING，实际=" + result);
                Assert(result.Contains("AFTER_ENQUEUE") && result.Contains("AFTER_DEQUEUE"),
                    "missing_fields 必须点名两相，实际=" + result);
            }
            finally
            {
                Cleanup(root);
            }
        }

        /// <summary>R8-02：Install 失败必须 fail-closed——不置 Enabled、不写 RUN_BEGIN、失败后仍能再次开启。</summary>
        public static void Test_R8_02_InstallFailureFailsClosedAndRetrySucceeds()
        {
            string root = Path.Combine(Path.GetTempPath(), "spf-t48-r802-" + Guid.NewGuid().ToString("N"));
            try
            {
                Bug1ProbeRuntime.__TestOffline(true);

                Bug1ProbeRuntime.__TestInstallSeam(() => false);
                Assert(!Bug1ProbeRuntime.Enable(NewConfig("t48-r802a", root)), "Install 返回 false 时 Enable 必须 false");
                Assert(!Bug1ProbeRuntime.Enabled, "失败后 Enabled 必须为 false，否则会被 ALREADY_RUNNING 卡死");
                Assert(Bug1ProbeRuntime.LastError.StartsWith("INSTALL_FAILED", StringComparison.Ordinal),
                    "失败必须留 INSTALL_FAILED，实际=" + Bug1ProbeRuntime.LastError);
                Assert(Bug1ProbeRuntime.__TestTrace().Length == 0, "失败路径不得写 RUN_BEGIN");

                Bug1ProbeRuntime.__TestInstallSeam(() => { throw new InvalidOperationException("boom"); });
                Assert(!Bug1ProbeRuntime.Enable(NewConfig("t48-r802b", root)), "Install 抛异常时 Enable 必须 false");
                Assert(Bug1ProbeRuntime.LastError.Contains("INSTALL_FAILED:InvalidOperationException"),
                    "异常必须带类型名，实际=" + Bug1ProbeRuntime.LastError);

                Bug1ProbeRuntime.__TestInstallSeam(() => true);
                Assert(Bug1ProbeRuntime.Enable(NewConfig("t48-r802c", root)),
                    "失败后必须能再次开启，LastError=" + Bug1ProbeRuntime.LastError);
                Assert(Bug1ProbeRuntime.Enabled, "成功后 Enabled 必须为 true");
                Assert(FindLine(Bug1ProbeRuntime.__TestTrace(), "\"kind\":\"RUN_BEGIN\"") != null, "成功后才写 RUN_BEGIN");
            }
            finally
            {
                Cleanup(root);
            }
        }

        /// <summary>R8-03：重试判定纯函数——只在 host 就绪、关卡已加载且看得到客机时重试，且有界可关。</summary>
        public static void Test_R8_03_StartupRetryDecision()
        {
            const int Max = 3;
            Assert(Bug1ProbeEntryLogic.EvaluateStartupRetry(false, false, true, true, 1, 0, Max) == Bug1StartupRetryVerdict.GiveUp,
                "未请求开启必须 GiveUp");
            Assert(Bug1ProbeEntryLogic.EvaluateStartupRetry(true, true, true, true, 1, 0, Max) == Bug1StartupRetryVerdict.GiveUp,
                "已在跑必须 GiveUp");
            Assert(Bug1ProbeEntryLogic.EvaluateStartupRetry(true, false, true, true, 1, Max, Max) == Bug1StartupRetryVerdict.GiveUp,
                "用完重试次数必须 GiveUp（有界）");
            Assert(Bug1ProbeEntryLogic.EvaluateStartupRetry(true, false, true, true, 1, 0, 0) == Bug1StartupRetryVerdict.GiveUp,
                "maxAttempts=0 必须 GiveUp（可关闭）");
            Assert(Bug1ProbeEntryLogic.EvaluateStartupRetry(true, false, false, true, 1, 0, Max) == Bug1StartupRetryVerdict.Wait,
                "非 host 必须 Wait");
            Assert(Bug1ProbeEntryLogic.EvaluateStartupRetry(true, false, true, false, 1, 0, Max) == Bug1StartupRetryVerdict.Wait,
                "关卡未加载必须 Wait");
            Assert(Bug1ProbeEntryLogic.EvaluateStartupRetry(true, false, true, true, 0, 0, Max) == Bug1StartupRetryVerdict.Wait,
                "看不到客机必须 Wait（等首个 client 进入）");
            Assert(Bug1ProbeEntryLogic.EvaluateStartupRetry(true, false, true, true, 1, 0, Max) == Bug1StartupRetryVerdict.Retry,
                "host+关卡+客机齐备必须 Retry");
            Assert(Bug1ProbeEntryLogic.EvaluateStartupRetry(true, false, true, true, 2, Max - 1, Max) == Bug1StartupRetryVerdict.Retry,
                "最后一次机会仍应 Retry");
        }

        /// <summary>R8-04：Disable 必须卸干净三处 detour（含 BCL Queue 的 Enqueue/Dequeue），且可再次开启。</summary>
        public static void Test_R8_04_DisableUninstallsDetoursAndReopenSucceeds()
        {
            string root = Path.Combine(Path.GetTempPath(), "spf-t48-r804-" + Guid.NewGuid().ToString("N"));
            var harmony = new HarmonyLib.Harmony("t48-r8-04");
            MethodInfo enqueueHook = typeof(Bug1ProbePatch).GetMethod("Postfix_Enqueue",
                BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo dequeueHook = typeof(Bug1ProbePatch).GetMethod("Postfix_Dequeue",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert(enqueueHook != null && dequeueHook != null, "两个 hook 方法必须存在");
            try
            {
                Bug1ProbeRuntime.__TestOffline(true);
                Bug1ProbeRuntime.__TestInstallSeam(() => true);
                Assert(Bug1ProbePatch.RegisterManual(harmony), "未开启时登记必须成功（不装 hook）");
                Assert(!Bug1ProbePatch.Registered, "未开启时不得安装 hook");

                // 离线环境限制：Assembly-CSharp 的 5 个原生方法在本进程里 Harmony 报
                // "IL Compile Error"，只有 BCL Queue<PlayerInputPacket> 两条能真装。
                // 这里用本 owner 的 harmony 真装这两条，验证 Uninstall 确实卸 detour（不是空实现）。
                Assert(Bug1ProbePatch.ResolveTargets(), "原生锚点必须解析成功");
                Assert(Bug1ProbePatch.EnqueueTarget != null && Bug1ProbePatch.DequeueTarget != null,
                    "Queue 两条 target 必须解析成功");
                harmony.Patch(Bug1ProbePatch.EnqueueTarget, postfix: new HarmonyLib.HarmonyMethod(enqueueHook));
                harmony.Patch(Bug1ProbePatch.DequeueTarget, postfix: new HarmonyLib.HarmonyMethod(dequeueHook));
                Assert(Bug1ProbePatch.Verify(harmony.Id, Bug1ProbePatch.EnqueueTarget, enqueueHook, false),
                    "Enqueue detour 必须装上");
                Assert(Bug1ProbePatch.Verify(harmony.Id, Bug1ProbePatch.DequeueTarget, dequeueHook, false),
                    "Dequeue detour 必须装上");

                Assert(Bug1ProbeRuntime.Enable(NewConfig("t48-r804a", root)),
                    "开启必须成功，LastError=" + Bug1ProbeRuntime.LastError);
                Bug1ProbeRuntime.Disable("t48-test");
                Assert(!Bug1ProbePatch.Registered, "Disable 后必须 Registered=false");
                Assert(!Bug1ProbePatch.Verify(harmony.Id, Bug1ProbePatch.EnqueueTarget, enqueueHook, false),
                    "Disable 后 Enqueue detour 必须卸掉");
                Assert(!Bug1ProbePatch.Verify(harmony.Id, Bug1ProbePatch.DequeueTarget, dequeueHook, false),
                    "Disable 后 Dequeue detour 必须卸掉");

                Assert(Bug1ProbeRuntime.Enable(NewConfig("t48-r804b", root)), "关闭后再次开启必须 START_OK");
                Assert(Bug1ProbeRuntime.Enabled, "再次开启后 Enabled 必须为 true");
                Assert(Bug1ProbePatch.Uninstall(), "Uninstall 第一次必须 true");
                Assert(Bug1ProbePatch.Uninstall(), "Uninstall 必须幂等（第二次仍 true）");
                Assert(!Bug1ProbePatch.Registered, "幂等 Uninstall 后不得 Registered");
            }
            finally
            {
                try { Bug1ProbePatch.Uninstall(); } catch { }
                try { HarmonyLib.Harmony.UnpatchID(harmony.Id); } catch { }
                Cleanup(root);
            }
        }

        /// <summary>F3：拒绝路径不得留空 run 目录；只有 Validate 通过后才建目录。</summary>
        public static void Test_F3_RefusedStartLeavesNoRunDirectory()
        {
            string baseDir = Path.Combine(Path.GetTempPath(), "spf-t48-f3-" + Guid.NewGuid().ToString("N"));
            string root = Path.Combine(baseDir, "HOST");
            try
            {
                Bug1ProbeConfig config;
                string detail;
                bool createdHost;
                bool createdRun;
                string error = Bug1ProbeControl.PrepareRun(true, "bug1-x", "step-1", 1UL, true, 4.0, 4.0, 7.0,
                    Bug1ProbeNativeSpeed.TierWalk, root, out config, out detail, out createdHost, out createdRun);
                Assert(error == Bug1ProbeEntryLogic.ErrorAlreadyRunning, "已开启必须 ALREADY_RUNNING，实际=" + error);
                Assert(!createdHost && !createdRun, "拒绝路径不得声称新建过目录");
                Assert(!Directory.Exists(root), "ALREADY_RUNNING 拒绝路径不得创建 run 目录: " + root);
                Assert(!Directory.Exists(baseDir), "ALREADY_RUNNING 拒绝路径连父目录都不该建: " + baseDir);

                error = Bug1ProbeControl.PrepareRun(false, string.Empty, "step-1", 1UL, true, 4.0, 4.0, 7.0,
                    Bug1ProbeNativeSpeed.TierWalk, root, out config, out detail, out createdHost, out createdRun);
                Assert(error == Bug1ProbeEntryLogic.ErrorRunOrStepMissing, "缺 RunId 必须 RUN_OR_STEP_MISSING，实际=" + error);
                Assert(!Directory.Exists(root), "参数缺失拒绝路径不得创建 run 目录");

                error = Bug1ProbeControl.PrepareRun(false, "bug1-x", "step-1", 1UL, true, 4.0, 4.0, 7.0,
                    Bug1ProbeNativeSpeed.TierWalk, root, out config, out detail, out createdHost, out createdRun);
                Assert(error == null, "参数齐备必须通过，实际=" + error + " detail=" + detail);
                Assert(createdHost && createdRun, "首次创建必须标记两级目录都是本次新建");
                Assert(Directory.Exists(root), "通过后才创建 run 目录");
                Assert(config.SpeedTier == Bug1ProbeNativeSpeed.TierWalk, "config 必须带 speed tier");
                Assert(Math.Abs(config.NativeSpeedCapWalkUS - 4.0) < 1e-9, "config 必须带走档上限");
                Assert(Math.Abs(config.NativeSpeedCapSprintUS - 7.0) < 1e-9, "config 必须带跑档上限");
            }
            finally
            {
                try { if (Directory.Exists(baseDir)) Directory.Delete(baseDir, true); } catch { }
            }
        }

        /// <summary>T49-F1：Enable 失败时只回滚本次新建的空 run 目录，拒绝路径不留残骸。</summary>
        public static void Test_T49_F1_EnableFailureRemovesNewRunDirectory()
        {
            string baseDir = Path.Combine(Path.GetTempPath(), "spf-t49-f1-" + Guid.NewGuid().ToString("N"));
            string root = Path.Combine(baseDir, "HOST");
            try
            {
                Bug1ProbeRuntime.__TestOffline(true);
                Bug1ProbeRuntime.__TestInstallSeam(() => false);

                Bug1ProbeConfig config;
                string detail;
                bool createdHost;
                bool createdRun;
                string error = Bug1ProbeControl.PrepareRun(false, "bug1-f1", "step-1", 1UL, true, 4.0, 4.0, 7.0,
                    Bug1ProbeNativeSpeed.TierWalk, root, out config, out detail, out createdHost, out createdRun);
                Assert(error == null, "PrepareRun 必须通过，实际=" + error + " detail=" + detail);
                Assert(createdHost && createdRun, "首次创建必须标记两级目录都是本次新建");
                Assert(Directory.Exists(root), "PrepareRun 通过后目录必须存在（回滚前）");

                string enableError;
                bool enabled = Bug1ProbeControl.EnablePreparedRun(config, root, createdHost, createdRun, out enableError);
                Assert(!enabled, "Install 返回 false 时 Enable 必须失败");
                Assert(!Bug1ProbeRuntime.Enabled, "安装失败不得留下 Enabled=true");
                Assert(enableError != null && enableError.StartsWith("INSTALL_FAILED", StringComparison.Ordinal),
                    "失败原因必须以 INSTALL_FAILED 开头，实际=" + enableError);
                Assert(!Directory.Exists(root), "T49-F1：Enable 失败后 <runId>/HOST 不得残留: " + root);
                Assert(!Directory.Exists(baseDir), "T49-F1：Enable 失败后本次新建的 <runId> 目录也不得残留: " + baseDir);

                // 成功路径不受影响：装得上时目录必须保留（证明只回滚失败路径）。
                Bug1ProbeRuntime.__TestInstallSeam(() => true);
                error = Bug1ProbeControl.PrepareRun(false, "bug1-f1", "step-1", 1UL, true, 4.0, 4.0, 7.0,
                    Bug1ProbeNativeSpeed.TierWalk, root, out config, out detail, out createdHost, out createdRun);
                Assert(error == null, "第二次 PrepareRun 必须通过，实际=" + error);
                enabled = Bug1ProbeControl.EnablePreparedRun(config, root, createdHost, createdRun, out enableError);
                Assert(enabled, "安装成功时必须开启，error=" + enableError);
                Assert(Directory.Exists(root), "成功路径不得删目录");

                // R10-02：回滚只对「本次新建且为空」的目录生效——已有目录、非空目录一律不碰，
                // 且必须是单级删除（若被改成递归删除 Delete(path,true)，下面 ②③ 会变红）。
                // ① 两级目录都已存在（flags 都为 false）：一个都不许删。
                string existingBase = Path.Combine(Path.GetTempPath(), "spf-t49-f1-exist-" + Guid.NewGuid().ToString("N"));
                string existingRoot = Path.Combine(existingBase, "HOST");
                try
                {
                    Directory.CreateDirectory(existingRoot);
                    Bug1ProbeControl.DiscardNewRunDirectory(existingRoot, false, false);
                    Assert(Directory.Exists(existingRoot) && Directory.Exists(existingBase),
                        "R10-02：flags 均为 false 时不得删除任何目录");
                }
                finally
                {
                    try { if (Directory.Exists(existingBase)) Directory.Delete(existingBase, true); } catch { }
                }

                // ② HOST 非空（flags 都为 true）：HOST 与其内容、run 目录都必须保留。
                string busyBase = Path.Combine(Path.GetTempPath(), "spf-t49-f1-busy-" + Guid.NewGuid().ToString("N"));
                string busyRoot = Path.Combine(busyBase, "HOST");
                try
                {
                    Directory.CreateDirectory(busyRoot);
                    File.WriteAllText(Path.Combine(busyRoot, "trace.jsonl"), "keep-me");
                    Bug1ProbeControl.DiscardNewRunDirectory(busyRoot, true, true);
                    Assert(Directory.Exists(busyRoot), "R10-02：HOST 非空时不得删除 HOST");
                    Assert(File.Exists(Path.Combine(busyRoot, "trace.jsonl")), "R10-02：HOST 内文件必须原样保留");
                    Assert(Directory.Exists(busyBase), "R10-02：HOST 非空时 run 目录也不得删除");
                }
                finally
                {
                    try { if (Directory.Exists(busyBase)) Directory.Delete(busyBase, true); } catch { }
                }

                // ③ run 目录非空、HOST 为空：只删空的 HOST，run 目录里的用户文件必须保留。
                string mixedBase = Path.Combine(Path.GetTempPath(), "spf-t49-f1-mixed-" + Guid.NewGuid().ToString("N"));
                string mixedRoot = Path.Combine(mixedBase, "HOST");
                try
                {
                    Directory.CreateDirectory(mixedRoot);
                    File.WriteAllText(Path.Combine(mixedBase, "user-notes.txt"), "keep-me");
                    Bug1ProbeControl.DiscardNewRunDirectory(mixedRoot, true, true);
                    Assert(!Directory.Exists(mixedRoot), "R10-02：空 HOST 必须被删除");
                    Assert(Directory.Exists(mixedBase), "R10-02：run 目录非空时不得删除");
                    Assert(File.Exists(Path.Combine(mixedBase, "user-notes.txt")), "R10-02：run 目录内用户文件必须保留");
                }
                finally
                {
                    try { if (Directory.Exists(mixedBase)) Directory.Delete(mixedBase, true); } catch { }
                }
            }
            finally
            {
                Cleanup(baseDir);
                try { if (Directory.Exists(baseDir)) Directory.Delete(baseDir, true); } catch { }
            }
        }

        /// <summary>F4：走/跑两档上限必须分开冻结（spec §3.4），不能恒取 sprint。</summary>
        public static void Test_F4_WalkAndSprintCapsAreDistinct()
        {
            double walk;
            double sprint;
            string evidence;
            Assert(Bug1ProbeNativeSpeed.ComputeTiers(4.5d, 7d, 1.25d, 1.1d, out walk, out sprint, out evidence),
                "4.5/7/1.25/1.1 必须可算，evidence=" + evidence);
            Assert(Math.Abs(walk - 6.1875d) < 1e-9, "走档 4.5*1.25*1.1=6.1875，实际=" + walk);
            Assert(Math.Abs(sprint - 9.625d) < 1e-9, "跑档 7*1.25*1.1=9.625，实际=" + sprint);
            Assert(walk < sprint, "走档上限必须低于跑档");
            Assert(evidence.Contains("SPEED_STAND=4.5") && evidence.Contains("SPEED_SPRINT=7"),
                "evidence 必须写清两个原生常量，实际=" + evidence);

            Assert(!Bug1ProbeNativeSpeed.ComputeTiers(4.5d, 7d, 1.25d, 20d, out walk, out sprint, out evidence),
                "超出物理范围必须拒绝");
            Assert(!Bug1ProbeNativeSpeed.ComputeTiers(4.5d, 3d, 1.25d, 1.1d, out walk, out sprint, out evidence),
                "跑档低于走档必须拒绝");
            Assert(!Bug1ProbeNativeSpeed.ComputeTiers(0d, 7d, 1.25d, 1.1d, out walk, out sprint, out evidence),
                "走档常量非正必须拒绝");

            Assert(Bug1ProbeNativeSpeed.ResolveTier("auto", false) == Bug1ProbeNativeSpeed.TierWalk, "auto+非冲刺必须走档");
            Assert(Bug1ProbeNativeSpeed.ResolveTier("auto", true) == Bug1ProbeNativeSpeed.TierSprint, "auto+冲刺必须跑档");
            Assert(Bug1ProbeNativeSpeed.ResolveTier("", true) == Bug1ProbeNativeSpeed.TierSprint, "空配置等同 auto");
            Assert(Bug1ProbeNativeSpeed.ResolveTier("WALK", true) == Bug1ProbeNativeSpeed.TierWalk, "显式 walk 必须覆盖姿态");
            Assert(Bug1ProbeNativeSpeed.ResolveTier("sprint", false) == Bug1ProbeNativeSpeed.TierSprint, "显式 sprint 必须覆盖姿态");
            Assert(Bug1ProbeNativeSpeed.ResolveTier("bogus", false) == null, "非法档位必须返回 null 供入口拒绝");
        }

        /// <summary>
        /// R9-01：**只覆盖纯函数**（EvaluateStartupRetry + AccountRetryAttempt 的穷举），
        /// 不证明生产接线真的采纳了结果——生产侧由 Bug1-47（IL 调用存在）与 Bug1-51
        /// （ApplyRetryOutcome 行为 + IL 绑定）负责。t52 复审指出本用例曾被误读为「接线已验证」，故在此写明边界。
        /// 上限 3 时必须只 Start 3 次、第 4 次不再 Start 且钩子已摘。
        /// </summary>
        public static void Test_R9_01_RetryAccountingStopsAtCapAndUnhooks()
        {
            const int max = 3;
            int used = 0;
            int starts = 0;
            bool hooked = true;
            for (int i = 0; i < 10; i++)
            {
                Bug1StartupRetryVerdict verdict = Bug1ProbeEntryLogic.EvaluateStartupRetry(
                    true, false, true, true, 1, used, max);
                if (verdict != Bug1StartupRetryVerdict.Retry)
                {
                    if (verdict == Bug1StartupRetryVerdict.GiveUp) hooked = false;
                    break;
                }
                starts++;
                Bug1RetryOutcome outcome = Bug1ProbeEntryLogic.AccountRetryAttempt(
                    used, max, false, Bug1ProbeEntryLogic.ErrorRoleNotHost);
                used = outcome.AttemptsUsed;
                if (outcome.Unhook) hooked = false;
                if (outcome.GiveUp) break;
            }
            Assert(starts == 3, "上限 3 次必须只 Start 3 次，实际=" + starts);
            Assert(used == 3, "累计次数必须停在 3，实际=" + used);
            Assert(!hooked, "第 4 次之前必须已摘钩，否则还会继续被事件唤醒");

            Bug1RetryOutcome ok = Bug1ProbeEntryLogic.AccountRetryAttempt(1, max, true, null);
            Assert(ok.AttemptsUsed == 1 && ok.Unhook && !ok.GiveUp, "成功后必须摘钩且不放弃");
            Assert(ok.Reason == Bug1ProbeEntryLogic.RetryReasonStarted, "成功原因码必须为 RETRY，实际=" + ok.Reason);

            Bug1RetryOutcome keep = Bug1ProbeEntryLogic.AccountRetryAttempt(0, max, false,
                Bug1ProbeEntryLogic.ErrorTargetNotFound);
            Assert(keep.AttemptsUsed == 1 && !keep.Unhook && !keep.GiveUp, "未到上限必须保持钩子继续等");
            Assert(keep.Reason == Bug1ProbeEntryLogic.ErrorTargetNotFound, "原因码必须透传失败码，实际=" + keep.Reason);

            Bug1RetryOutcome zero = Bug1ProbeEntryLogic.AccountRetryAttempt(0, 0, false, null);
            Assert(zero.GiveUp && zero.Unhook, "上限 <=0 必须立即放弃并摘钩");
        }

        /// <summary>
        /// R9-01 接线：生产重试入口必须真的调用记账纯函数（删掉调用点 → 本用例红）。
        /// </summary>
        public static void Test_R9_01_RetryWiringUsesAccounting()
        {
            MethodInfo retry = typeof(Bug1ProbeControl).GetMethod("TryRetryStartup",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert(retry != null, "TryRetryStartup 必须存在");
            string control = typeof(Bug1ProbeControl).FullName;
            int accounting = IlContractProbe.CountMethodCalls(retry,
                typeof(Bug1ProbeEntryLogic).FullName, "AccountRetryAttempt");
            Assert(accounting >= 1, "R9-01：TryRetryStartup 必须调用 AccountRetryAttempt 记账，实际=" + accounting);
            Assert(IlContractProbe.CountMethodCalls(retry, control, "DecideRetry") >= 1,
                "重试入口必须仍经 DecideRetry 纯判定");
            Assert(IlContractProbe.CountMethodCalls(retry, control, "UnhookStartupRetry") >= 1,
                "重试入口必须在放弃/成功时摘钩");
        }

        /// <summary>
        /// R9-02：开启路径抛异常时不得留下「已开启」假象——运行时真关闭，且日志走 REFUSED/ENTRY_INIT_FAILED。
        /// </summary>
        public static void Test_R9_02_StartExceptionAbortsToNotEnabled()
        {
            string baseDir = Path.Combine(Path.GetTempPath(), "spf-t49-r902-" + Guid.NewGuid().ToString("N"));
            string root = Path.Combine(baseDir, "HOST");
            try
            {
                Bug1ProbeRuntime.__TestOffline(true);
                Bug1ProbeRuntime.__TestInstallSeam(() => true);
                Bug1ProbeConfig config;
                string detail;
                bool createdHost;
                bool createdRun;
                string error = Bug1ProbeControl.PrepareRun(false, "bug1-r902", "step-1", 1UL, true, 4.0, 4.0, 7.0,
                    Bug1ProbeNativeSpeed.TierWalk, root, out config, out detail, out createdHost, out createdRun);
                Assert(error == null, "PrepareRun 必须通过，实际=" + error);
                string enableError;
                Assert(Bug1ProbeControl.EnablePreparedRun(config, root, createdHost, createdRun, out enableError),
                    "先正常开启: " + enableError);
                Assert(Bug1ProbeRuntime.Enabled, "开启后 Enabled 必须为 true");

                bool ok = Bug1ProbeControl.AbortStart(new InvalidOperationException("boom"));
                Assert(!ok, "AbortStart 必须返回 false（REFUSED ENTRY_INIT_FAILED）");
                Assert(!Bug1ProbeRuntime.Enabled, "R9-02：异常收尾后不得留下 Enabled=true 的假象");

                Assert(!Bug1ProbeControl.AbortStart(new NullReferenceException("boom2")),
                    "未开启时 AbortStart 仍须返回 false（幂等）");
                Assert(!Bug1ProbeRuntime.Enabled, "幂等收尾后仍不得为 Enabled");
            }
            finally
            {
                Cleanup(baseDir);
                try { if (Directory.Exists(baseDir)) Directory.Delete(baseDir, true); } catch { }
            }
        }

        /// <summary>
        /// R9-02 接线：TryStart 与热切换回调都必须有 try/catch，且异常交给 AbortStart 收尾。
        /// </summary>
        public static void Test_R9_02_StartHasExceptionGuard()
        {
            MethodInfo start = typeof(Bug1ProbeControl).GetMethod("TryStart",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert(start != null, "TryStart 必须存在");
            string control = typeof(Bug1ProbeControl).FullName;
            int handlers = IlContractProbe.CountExceptionHandlers(start);
            Assert(handlers >= 1, "R9-02：TryStart 必须有 try/catch，实际 handler=" + handlers);
            Assert(IlContractProbe.CountMethodCalls(start, control, "AbortStart") >= 1,
                "R9-02：TryStart 的异常分支必须交给 AbortStart");
            MethodInfo changed = typeof(Bug1ProbeControl).GetMethod("OnEnabledChanged",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert(changed != null, "OnEnabledChanged 必须存在");
            Assert(IlContractProbe.CountExceptionHandlers(changed) >= 1,
                "R9-02：热切换回调同样必须有 try/catch");
        }

        /// <summary>
        /// T52-F1：生产开启路径必须经 EnablePreparedRun（失败回滚入口），不得绕过它直连 Runtime.Enable。
        /// 纯 IL 断言：把 TryStartCore 换回 Bug1ProbeRuntime.Enable(config) → 本用例红。
        /// </summary>
        public static void Test_T52_F1_StartCoreRoutesThroughEnablePreparedRun()
        {
            MethodInfo core = typeof(Bug1ProbeControl).GetMethod("TryStartCore",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert(core != null, "TryStartCore 必须存在");
            string control = typeof(Bug1ProbeControl).FullName;
            int prepared = IlContractProbe.CountMethodCalls(core, control, "EnablePreparedRun");
            Assert(prepared >= 1, "T52-F1：TryStartCore 必须调用 EnablePreparedRun，实际=" + prepared);
            int direct = IlContractProbe.CountMethodCalls(core, typeof(Bug1ProbeRuntime).FullName, "Enable");
            Assert(direct == 0, "T52-F1：TryStartCore 不得绕过回滚直连 Bug1ProbeRuntime.Enable，实际=" + direct);
            Assert(IlContractProbe.CountMethodCalls(core, control, "PrepareRun") >= 1,
                "T52-F1：TryStartCore 必须先经 PrepareRun 记录本次新建目录");
        }

        /// <summary>
        /// T52-F2/R10-01：记账结果必须被生产路径采纳（写回次数、放弃日志、按 Unhook 摘钩）。
        /// 两层：IL 断言 TryRetryStartup 调 ApplyRetryOutcome；行为断言该生产方法的摘钩/保留两分支。
        /// 「保留调用但永不摘钩」（M-B3）与「忽略 Unhook」（MUT-5）都会在这里红。
        /// </summary>
        public static void Test_T52_F2_RetryOutcomeIsAdopted()
        {
            MethodInfo retry = typeof(Bug1ProbeControl).GetMethod("TryRetryStartup",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert(retry != null, "TryRetryStartup 必须存在");
            string control = typeof(Bug1ProbeControl).FullName;
            int applied = IlContractProbe.CountMethodCalls(retry, control, "ApplyRetryOutcome");
            Assert(applied >= 1, "T52-F2：TryRetryStartup 必须把记账结果交给 ApplyRetryOutcome 采纳，实际=" + applied);

            try
            {
                // ① 放弃分支：次数写回 2 且钩子被摘。
                Bug1ProbeControl.__TestResetRetryState();
                Bug1ProbeControl.__TestArmRetryHook();
                Assert(Bug1ProbeControl.__TestRetryHookArmed, "测试缝必须先让钩子处于挂载态");
                Bug1ProbeControl.ApplyRetryOutcome(new Bug1RetryOutcome
                {
                    AttemptsUsed = 2,
                    GiveUp = true,
                    Unhook = true,
                    Reason = Bug1ProbeEntryLogic.RetryReasonExhausted,
                });
                Assert(Bug1ProbeControl.__TestStartupRetries == 2,
                    "T52-F2：已用次数必须被采纳，实际=" + Bug1ProbeControl.__TestStartupRetries);
                Assert(!Bug1ProbeControl.__TestRetryHookArmed, "T52-F2：Unhook=true 时必须真的摘钩");

                // ② 未到上限：次数写回 1，钩子必须继续挂着等下一个事件。
                Bug1ProbeControl.__TestResetRetryState();
                Bug1ProbeControl.__TestArmRetryHook();
                Bug1ProbeControl.ApplyRetryOutcome(new Bug1RetryOutcome
                {
                    AttemptsUsed = 1,
                    GiveUp = false,
                    Unhook = false,
                    Reason = Bug1ProbeEntryLogic.ErrorTargetNotFound,
                });
                Assert(Bug1ProbeControl.__TestStartupRetries == 1,
                    "T52-F2：未到上限也必须写回次数，实际=" + Bug1ProbeControl.__TestStartupRetries);
                Assert(Bug1ProbeControl.__TestRetryHookArmed, "T52-F2：Unhook=false 时不得摘钩");
            }
            finally
            {
                Bug1ProbeControl.__TestResetRetryState();
            }
        }

        /// <summary>
        /// R12-01（t56 判 high）：只钉「调了 EnablePreparedRun」不够——返回值必须真的参与判定。
        /// 变异 M6「保留调用、丢弃返回值」（call; pop）会让本用例红，而 Bug1-50 仍绿。
        /// 判据见 IlContractProbe.CountCallsWithBranchOnResult（call 后 8 条指令内必须出现条件分支）。
        /// </summary>
        public static void Test_R12_01_EnablePreparedRunResultIsConsumed()
        {
            MethodInfo core = typeof(Bug1ProbeControl).GetMethod("TryStartCore",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert(core != null, "TryStartCore 必须存在");
            string control = typeof(Bug1ProbeControl).FullName;
            int consumed = IlContractProbe.CountCallsWithBranchOnResult(core, control, "EnablePreparedRun", 8);
            // 分工：调用点是否存在由 Bug1-50 负责（缺失即红）；本用例只判「返回值是否被采纳」，
            // 因此调用点缺失时不在此重复报红（M1 直连 Enable 只红 Bug1-50，M6 丢弃返回值只红本用例）。
            if (IlContractProbe.CountMethodCalls(core, control, "EnablePreparedRun") > 0)
            {
                Assert(consumed >= 1,
                    "R12-01：EnablePreparedRun 的返回值必须参与条件判定（call 后 8 条内未见 brtrue/brfalse），实际=" + consumed);
            }
            // 判据自检：同一个探针在「丢弃返回值」样本上必须数出 0，在「参与判定」样本上必须数出 1。
            Assert(IlContractProbe.CountCallsWithBranchOnResult(
                typeof(Bug1ProbeEntryRepairTests).GetMethod("ProbeDiscardResult",
                    BindingFlags.Static | BindingFlags.NonPublic),
                typeof(Bug1ProbeEntryRepairTests).FullName, "ProbeCallee", 8) == 0,
                "R12-01：探针自检失败——丢弃返回值的样本必须数出 0（否则守卫是空转的）");
            Assert(IlContractProbe.CountCallsWithBranchOnResult(
                typeof(Bug1ProbeEntryRepairTests).GetMethod("ProbeConsumeResult",
                    BindingFlags.Static | BindingFlags.NonPublic),
                typeof(Bug1ProbeEntryRepairTests).FullName, "ProbeCallee", 8) == 1,
                "R12-01：探针自检失败——返回值参与判定的样本必须数出 1");
        }

        private static bool ProbeCallee(bool value)
        {
            return value;
        }

        private static bool ProbeDiscardResult()
        {
            ProbeCallee(true);
            return true;
        }

        private static bool ProbeConsumeResult()
        {
            if (!ProbeCallee(true)) return false;
            return true;
        }

        /// <summary>
        /// R12-02（t56 判 medium）：只钉「调了 ApplyRetryOutcome」不够——传进去的必须是真的记账结果。
        /// 变异 M7「ApplyRetryOutcome(new Bug1RetryOutcome())」会让本用例红，而 Bug1-51 仍绿。
        /// </summary>
        public static void Test_R12_02_RetryOutcomeArgumentComesFromAccounting()
        {
            MethodInfo retry = typeof(Bug1ProbeControl).GetMethod("TryRetryStartup",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert(retry != null, "TryRetryStartup 必须存在");
            int fed = IlContractProbe.CountCallsWithArgumentFromProducer(retry,
                typeof(Bug1ProbeControl).FullName, "ApplyRetryOutcome",
                typeof(Bug1ProbeEntryLogic).FullName, "AccountRetryAttempt");
            // 分工同上：调用点缺失由 Bug1-51 负责；本用例只判「实参是否来自记账结果」。
            if (IlContractProbe.CountMethodCalls(retry, typeof(Bug1ProbeControl).FullName, "ApplyRetryOutcome") > 0)
            {
                Assert(fed >= 1,
                    "R12-02：ApplyRetryOutcome 的实参必须来自 AccountRetryAttempt 的返回值（不得传新建默认值），实际=" + fed);
            }
            // 判据自检：实参来自生产者调用必须数出 1；传字面量默认值必须数出 0。
            Assert(IlContractProbe.CountCallsWithArgumentFromProducer(
                typeof(Bug1ProbeEntryRepairTests).GetMethod("ProbeFed",
                    BindingFlags.Static | BindingFlags.NonPublic),
                typeof(Bug1ProbeEntryRepairTests).FullName, "ProbeConsume",
                typeof(Bug1ProbeEntryRepairTests).FullName, "ProbeProduce") == 1,
                "R12-02：探针自检失败——实参来自生产者调用的样本必须数出 1");
            Assert(IlContractProbe.CountCallsWithArgumentFromProducer(
                typeof(Bug1ProbeEntryRepairTests).GetMethod("ProbeDefaultFed",
                    BindingFlags.Static | BindingFlags.NonPublic),
                typeof(Bug1ProbeEntryRepairTests).FullName, "ProbeConsume",
                typeof(Bug1ProbeEntryRepairTests).FullName, "ProbeProduce") == 0,
                "R12-02：探针自检失败——传字面量默认值的样本必须数出 0（否则守卫是空转的）");
        }

        private static int ProbeSink;

        private static int ProbeProduce()
        {
            return 1;
        }

        private static void ProbeConsume(int value)
        {
            ProbeSink = value;
        }

        private static void ProbeFed()
        {
            int value = ProbeProduce();
            ProbeConsume(value);
        }

        private static void ProbeDefaultFed()
        {
            ProbeConsume(0);
        }

        /// <summary>
        /// R10-04：AbortStart 必须把 cfg 的 Enable 回写 false（内存态与配置一致，不留「配置里还开着」的假象）。
        /// BepInEx 的 ConfigEntry 只有带 ConfigFile 的构造，故在临时目录建真实 ConfigFile 后反射注入。
        /// </summary>
        public static void Test_R10_04_AbortStartWritesBackConfig()
        {
            FieldInfo field = typeof(Bug1ProbeControl).GetField("_enabledEntry",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert(field != null, "_enabledEntry 字段必须存在");
            object original = field.GetValue(null);
            string cfgDir = Path.Combine(Path.GetTempPath(), "spf-r10-04-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(cfgDir);
                var file = new BepInEx.Configuration.ConfigFile(Path.Combine(cfgDir, "Bug1Probe.cfg"), false);
                // ConfigEntry<T> 的 4 参构造在 BepInEx 里是 internal（IsAssembly=true），测试侧只能反射构造。
                Type entryType = typeof(BepInEx.Configuration.ConfigEntry<bool>);
                object entry = Activator.CreateInstance(entryType,
                    BindingFlags.Instance | BindingFlags.NonPublic,
                    null,
                    new object[]
                    {
                        file,
                        new BepInEx.Configuration.ConfigDefinition("Bug1Probe", "Enable"),
                        true,
                        BepInEx.Configuration.ConfigDescription.Empty,
                    },
                    null);
                Assert(entry != null, "必须能反射构造 ConfigEntry<bool>");
                PropertyInfo valueProperty = entryType.GetProperty("Value");
                Assert(valueProperty != null, "ConfigEntry<bool>.Value 必须存在");
                Assert((bool)valueProperty.GetValue(entry, null), "测试前 cfg 必须处于开启态");
                field.SetValue(null, entry);
                Bug1ProbeControl.__TestResetRetryState();

                Assert(!Bug1ProbeControl.AbortStart(new InvalidOperationException("cfg-writeback")),
                    "AbortStart 必须返回 false");
                object written = valueProperty.GetValue(entry, null);
                Assert(!(bool)written, "R10-04：AbortStart 必须把 cfg 的 Enable 回写 false，实际=" + written);
            }
            finally
            {
                field.SetValue(null, original);
                try { if (Directory.Exists(cfgDir)) Directory.Delete(cfgDir, true); } catch { }
            }
        }
    }
}
