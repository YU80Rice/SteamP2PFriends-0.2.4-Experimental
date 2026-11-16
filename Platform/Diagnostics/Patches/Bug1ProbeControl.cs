using BepInEx.Configuration;
using SDG.Unturned;
using SteamP2PFriends.Shared;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace SteamP2PFriends.Platform.Diagnostics.Patches
{
    /// <summary>
    /// t45：Bug1 仪表的生产开启入口（默认关闭）。
    /// 选型：BepInEx 配置文件而不是聊天指令——
    ///   1) 仪表是本地只读诊断，规格 §future config 直接指定 `Bug1Probe.Enabled=false` 契约；
    ///   2) 聊天指令要给服务端聊天链再加一个 Harmony 补丁，多一处可能漂移的原生 ABI，
    ///      与本票「只读、零副作用、默认关闭」的目标相悖；
    ///   3) 配置文件既能在启动时生效，也能被 BepInEx ConfigurationManager 在运行中切换，
    ///      切换事件驱动 Enable/Disable，并留下明确日志。
    /// 日志词表：ENTRY_READY / START_OK / REFUSED &lt;错误码&gt; / STOP。
    /// </summary>
    internal static class Bug1ProbeControl
    {
        private const string Label = "[SPF-Bug1Probe]";

        /// <summary>R8-03：启动期开启被 ROLE_NOT_HOST 拒掉后，最多在关卡/玩家事件上重试这么多次。</summary>
        private const int MaxStartupRetries = 3;

        private static ConfigEntry<bool> _enabledEntry;
        private static ConfigEntry<string> _targetEntry;
        private static ConfigEntry<string> _tierEntry;
        private static int _startupRetries;
        private static bool _retryHooked;

        /// <summary>插件诊断阶段调用一次；重复调用无副作用。</summary>
        public static void Initialize()
        {
            if (_enabledEntry != null) return;
            try
            {
                // t145：开关与目标统一从插件主 cfg 读取，不再创建/读取独立 Bug1Probe.cfg。
                _enabledEntry = Core.Config.PluginConfig.Bug1ProbeEnable;
                _targetEntry = Core.Config.PluginConfig.Bug1ProbeTargetPlayer;
                _tierEntry = Core.Config.PluginConfig.Bug1ProbeSpeedTier;
                if (_enabledEntry == null || _targetEntry == null || _tierEntry == null)
                {
                    RoleLogger.Error("[Shared]", Label + " " + Bug1ProbeEntryLogic.ErrorEntryInitFailed
                        + " main config not bound (PluginConfig.Bind must run first)");
                    return;
                }

                _enabledEntry.SettingChanged += OnEnabledChanged;
                RoleLogger.Info("[Shared]", Label + " ENTRY_READY config="
                    + Core.Config.PluginConfig.BoundConfigFilePath
                    + " enabled=" + _enabledEntry.Value + " target=\"" + _targetEntry.Value + "\"");
                RoleLogger.Info("[Shared]", Label + " ENTRY_SPEED_TIER speed_tier=" + _tierEntry.Value);
                if (_enabledEntry.Value) TryStart("startup");
            }
            catch (Exception ex)
            {
                RoleLogger.Error("[Shared]", Label + " " + Bug1ProbeEntryLogic.ErrorEntryInitFailed + " " + ex);
            }
        }

        private static void OnEnabledChanged(object sender, EventArgs e)
        {
            // R9-02：热切换回调同样不得把异常抛给 BepInEx 事件循环；失败统一走 AbortStart。
            try
            {
                OnEnabledChangedCore();
            }
            catch (Exception ex)
            {
                AbortStart(ex);
            }
        }

        private static void OnEnabledChangedCore()
        {
            if (_enabledEntry != null && _enabledEntry.Value)
            {
                // 用户重新开启：重试预算重置，旧的等待钩子先摘掉（TryStart 若再被拒会重新挂上）。
                _startupRetries = 0;
                UnhookStartupRetry();
                TryStart("config-change");
                return;
            }
            if (Bug1ProbeRuntime.Enabled)
            {
                Bug1ProbeRuntime.MarkConfigChanged();
                Bug1ProbeRuntime.Disable("CONFIG_CHANGED");
                RoleLogger.Info("[Shared]", Label + " STOP reason=CONFIG_CHANGED（运行中关闭，本 step 作废并落盘）");
            }
            UnhookStartupRetry();
        }

        /// <summary>按不可手填的原则组装参数并开启；任一前置不满足即拒绝并记录错误码。</summary>
        internal static bool TryStart(string origin)
        {
            // R9-02：开启路径上的任何异常都不得逃逸——否则 cfg 里 Enable=true、内存里没跑起来，
            // 用户看到的是「开了但没数据」。异常统一走 AbortStart（REFUSED + 真关闭 + 回写 cfg）。
            try
            {
                return TryStartCore(origin);
            }
            catch (Exception ex)
            {
                return AbortStart(ex);
            }
        }

        /// <summary>
        /// R9-02：开启失败的统一收尾——运行时确实关掉（不留 Enabled=true 假象），
        /// 并把 cfg 的 Enable 回写 false（只在确实为 true 时写），日志复用 REFUSED/ENTRY_INIT_FAILED 词表。
        /// </summary>
        internal static bool AbortStart(Exception ex)
        {
            string detail = ex == null ? "unknown" : ex.GetType().Name;
            if (Bug1ProbeRuntime.Enabled)
            {
                try { Bug1ProbeRuntime.Disable("ENTRY_EXCEPTION"); }
                catch (Exception disableEx)
                {
                    RoleLogger.Error("[Shared]", Label + " " + Bug1ProbeEntryLogic.ErrorEntryInitFailed
                        + " disable_failed " + disableEx.GetType().Name);
                }
            }
            try
            {
                if (_enabledEntry != null && _enabledEntry.Value) _enabledEntry.Value = false;
            }
            catch (Exception cfgEx)
            {
                RoleLogger.Warn("[Shared]", Label + " ENTRY_CFG_RESET_FAILED " + cfgEx.GetType().Name);
            }
            return Refuse(Bug1ProbeEntryLogic.ErrorEntryInitFailed, "开启过程异常 " + detail);
        }

        private static bool TryStartCore(string origin)
        {
            if (!Provider.isServer)
            {
                // R8-03：stage 6 在插件加载期执行，此刻必然不是 host；旧实现一拒到底，
                // 「改 cfg 再启动」永远采不到。改为挂有界重试钩子（关卡加载 / 首个玩家创建），
                // 由 Bug1ProbeEntryLogic.EvaluateStartupRetry 纯函数判定 Wait/Retry/GiveUp。
                EnsureStartupRetryHook();
                return Refuse(Bug1ProbeEntryLogic.ErrorRoleNotHost,
                    "本仪表只在 Listen Host 侧采集（需要服务端输入队列与权威根），客户端不开启；"
                    + "已挂载有界重试 level-loaded/player-created，最多 " + MaxStartupRetries + " 次");
            }
            string runId = Bug1ProbeEntryLogic.BuildRunId(DateTime.UtcNow);
            string stepId = Bug1ProbeEntryLogic.BuildStepId(1);

            ulong targetId;
            string targetName;
            string targetError = ResolveTarget(out targetId, out targetName);
            if (targetError != null) return Refuse(targetError, "目标玩家未找到或无法唯一确定");

            Player target = FindPlayer(targetId);
            double walkUS;
            double sprintUS;
            bool sprinting;
            string capEvidence;
            if (!Bug1ProbeNativeSpeed.TryResolve(target, out walkUS, out sprintUS, out sprinting, out capEvidence))
                return Refuse(Bug1ProbeEntryLogic.ErrorSpeedCapUnresolved, "原生速度上限取不到: " + capEvidence);

            // F4：spec §3.4 要求走/跑各有上限（SDK PlayerMovement.cs:92-93 SPEED_STAND=4.5f / SPEED_SPRINT=7f）。
            // 两档都在开启时冻结进 manifest；窗口内用哪一档由 SpeedTier 决定（auto = 按目标当前姿态）。
            string configuredTier = _tierEntry != null ? _tierEntry.Value : Bug1ProbeNativeSpeed.TierAuto;
            string tier = Bug1ProbeNativeSpeed.ResolveTier(configuredTier, sprinting);
            if (tier == null)
                return Refuse(Bug1ProbeEntryLogic.ErrorSpeedCapUnresolved, "SpeedTier 取值非法: " + configuredTier);
            double capUS = tier == Bug1ProbeNativeSpeed.TierSprint ? sprintUS : walkUS;

            string outputRoot = Path.Combine(BepInEx.Paths.BepInExRootPath, "diagnostics", "spf-bug1", runId, "HOST");
            // F3：先 Validate（含 ALREADY_RUNNING），全部通过才创建输出目录；拒绝路径不留空 run 目录。
            Bug1ProbeConfig config;
            string prepareDetail;
            bool createdHostDirectory;
            bool createdRunDirectory;
            string error = PrepareRun(Bug1ProbeRuntime.Enabled, runId, stepId, targetId, true, capUS,
                walkUS, sprintUS, tier, outputRoot, out config, out prepareDetail,
                out createdHostDirectory, out createdRunDirectory);
            if (error != null) return Refuse(error, prepareDetail);
            // T49-F1：Enable 失败时回滚本次新建的空 run 目录，拒绝路径不留残骸。
            string enableError;
            if (!EnablePreparedRun(config, outputRoot, createdHostDirectory, createdRunDirectory, out enableError))
                return Refuse(enableError, "运行时拒绝开启");

            RoleLogger.Info("[Shared]", Label + " START_OK origin=" + origin + " run=" + runId + " step=" + stepId
                + " target=\"" + targetName + "\" steamid=" + targetId.ToString(CultureInfo.InvariantCulture)
                + " speed_tier=" + tier
                + " cap_u_s=" + capUS.ToString("R", CultureInfo.InvariantCulture)
                + " walk_u_s=" + walkUS.ToString("R", CultureInfo.InvariantCulture)
                + " sprint_u_s=" + sprintUS.ToString("R", CultureInfo.InvariantCulture)
                + " cap_evidence={" + capEvidence + "} dir=" + outputRoot);
            if (!Bug1ProbeRuntime.TryBindTarget())
                RoleLogger.Warn("[Shared]", Label + " BIND_PENDING " + Bug1ProbeRuntime.LastError
                    + "（目标进入视野/third 建好后自动重试绑定）");
            UnhookStartupRetry();
            return true;
        }

        private static bool Refuse(string code, string detail)
        {
            RoleLogger.Error("[Shared]", Label + " REFUSED " + code + " " + detail);
            return false;
        }

        /// <summary>
        /// Host 视角：只在真实连接列表里解析客机；空选择器仅在唯一客机时自动选中。
        /// R8-01：判空一律走 Bug1ProbeTargetSelector（ReferenceEquals），禁止在本方法里写 == null。
        /// </summary>
        private static string ResolveTarget(out ulong steamId, out string playerName)
        {
            steamId = 0UL;
            playerName = null;
            string selector = _targetEntry != null ? (_targetEntry.Value ?? string.Empty).Trim() : string.Empty;
            ulong localSteamId = 0UL;
            try { localSteamId = Provider.client.m_SteamID; } catch { }
            Bug1TargetSelection selection = Bug1ProbeTargetSelector.Select(
                Bug1ProbeTargetSelector.SnapshotAll(Provider.clients), selector, localSteamId);
            if (selection.Status != Bug1TargetSelectionStatus.Selected)
            {
                RoleLogger.Warn("[Shared]", Label + " TARGET_SELECT " + selection.Status + " reason=" + selection.Reason
                    + " candidates=" + selection.CandidateCount
                    + " skipped=[" + string.Join(",", selection.Skipped.ToArray()) + "]");
                return selection.ErrorCode;
            }
            steamId = selection.SteamId;
            playerName = selection.PlayerName;
            return null;
        }

        private static Player FindPlayer(ulong steamId)
        {
            List<SteamPlayer> clients = Provider.clients;
            List<Bug1ClientSnapshot> snapshots = Bug1ProbeTargetSelector.SnapshotAll(clients);
            for (int i = 0; i < snapshots.Count; i++)
            {
                Bug1ClientSnapshot snapshot = snapshots[i];
                if (!snapshot.HasPlayerId || snapshot.SteamId != steamId) continue;
                SteamPlayer client = clients[i];
                if (ReferenceEquals(client, null)) continue;
                return client.player;
            }
            return null;
        }

        /// <summary>
        /// F3：输出目录只在 Validate 通过之后创建（拒绝路径不留空 run 目录）。
        /// 不碰 Provider/Unity，离线用例可直接调用核对「ALREADY_RUNNING 时不建目录」。
        /// </summary>
        internal static string PrepareRun(bool alreadyEnabled, string runId, string stepId, ulong targetId,
            bool speedCapKnown, double capUS, double walkUS, double sprintUS, string tier, string outputRoot,
            out Bug1ProbeConfig config, out string detail,
            out bool createdHostDirectory, out bool createdRunDirectory)
        {
            config = default(Bug1ProbeConfig);
            detail = "前置参数不齐备";
            createdHostDirectory = false;
            createdRunDirectory = false;
            string error = Bug1ProbeEntryLogic.Validate(alreadyEnabled, runId, stepId, targetId,
                speedCapKnown, capUS, outputRoot);
            if (error != null) return error;
            // T49-F1：记录「本次新建」的两级目录，供 Enable 失败时精确回滚（只删自己建的空目录）。
            string runDirectory = Path.GetDirectoryName(outputRoot);
            bool hostExisted = Directory.Exists(outputRoot);
            bool runExisted = !string.IsNullOrEmpty(runDirectory) && Directory.Exists(runDirectory);
            try { Directory.CreateDirectory(outputRoot); }
            catch (Exception ex)
            {
                detail = ex.GetType().Name;
                return Bug1ProbeEntryLogic.ErrorOutputRootUnresolved;
            }
            createdHostDirectory = !hostExisted;
            createdRunDirectory = !runExisted;
            config = new Bug1ProbeConfig
            {
                RunId = runId,
                StepId = stepId,
                TargetSteamId = targetId,
                OutputRoot = outputRoot,
                NativeSpeedCapUS = capUS,
                NativeSpeedCapWalkUS = walkUS,
                NativeSpeedCapSprintUS = sprintUS,
                SpeedTier = tier,
                SpeedCapKnown = true,
            };
            return null;
        }

        /// <summary>
        /// T49-F1：Enable 返回 false 时回滚本次新建的空目录（只删本次自己建的两级、非递归）。
        /// 任何异常都吞掉并保留目录——宁可留空目录也不误删用户已有内容。
        /// </summary>
        internal static void DiscardNewRunDirectory(string outputRoot, bool createdHostDirectory, bool createdRunDirectory)
        {
            if (string.IsNullOrEmpty(outputRoot)) return;
            if (createdHostDirectory)
            {
                try
                {
                    if (Directory.Exists(outputRoot) && Directory.GetFileSystemEntries(outputRoot).Length == 0)
                        Directory.Delete(outputRoot, false);
                }
                catch (Exception ex)
                {
                    RoleLogger.Warn("[Shared]", Label + " RUN_DIR_CLEANUP_SKIPPED host " + ex.GetType().Name);
                }
            }
            if (!createdRunDirectory) return;
            string runDirectory = Path.GetDirectoryName(outputRoot);
            if (string.IsNullOrEmpty(runDirectory)) return;
            try
            {
                if (Directory.Exists(runDirectory) && Directory.GetFileSystemEntries(runDirectory).Length == 0)
                    Directory.Delete(runDirectory, false);
            }
            catch (Exception ex)
            {
                RoleLogger.Warn("[Shared]", Label + " RUN_DIR_CLEANUP_SKIPPED run " + ex.GetType().Name);
            }
        }

        /// <summary>
        /// T49-F1 的唯一开启出口：Enable 成功才算开启；失败则回滚本次新建目录并返回 false。
        /// 抽成静态方法是为了让离线用例能走同一条生产分支（不碰 Provider/Unity）。
        /// </summary>
        internal static bool EnablePreparedRun(Bug1ProbeConfig config, string outputRoot,
            bool createdHostDirectory, bool createdRunDirectory, out string error)
        {
            if (Bug1ProbeRuntime.Enable(config))
            {
                error = null;
                return true;
            }
            DiscardNewRunDirectory(outputRoot, createdHostDirectory, createdRunDirectory);
            error = Bug1ProbeRuntime.LastError;
            return false;
        }

        /// <summary>R8-03：启动期被 ROLE_NOT_HOST 拒掉后挂有界重试（关卡加载 / 首个玩家创建）。</summary>
        private static void EnsureStartupRetryHook()
        {
            if (_retryHooked) return;
            _retryHooked = true;
            try { Level.onLevelLoaded += OnLevelLoadedRetry; }
            catch (Exception ex) { RoleLogger.Warn("[Shared]", Label + " RETRY_HOOK_FAILED level-loaded " + ex.GetType().Name); }
            try { Player.onPlayerCreated += OnPlayerCreatedRetry; }
            catch (Exception ex) { RoleLogger.Warn("[Shared]", Label + " RETRY_HOOK_FAILED player-created " + ex.GetType().Name); }
            RoleLogger.Info("[Shared]", Label + " RETRY_ARMED max=" + MaxStartupRetries + " used=" + _startupRetries);
        }

        private static void UnhookStartupRetry()
        {
            if (!_retryHooked) return;
            _retryHooked = false;
            try { Level.onLevelLoaded -= OnLevelLoadedRetry; } catch { }
            try { Player.onPlayerCreated -= OnPlayerCreatedRetry; } catch { }
        }

        private static void OnLevelLoadedRetry(int level) { TryRetryStartup("level-loaded"); }

        private static void OnPlayerCreatedRetry(Player player) { TryRetryStartup("player-created"); }

        private static void TryRetryStartup(string trigger)
        {
            try
            {
                if (_enabledEntry == null) { UnhookStartupRetry(); return; }
                bool isHost = false;
                bool levelLoaded = false;
                int observableClients = 0;
                try { isHost = Provider.isServer; } catch { }
                try { levelLoaded = Level.isLoaded; } catch { }
                try { observableClients = CountObservableClients(); } catch { }
                Bug1StartupRetryVerdict verdict = DecideRetry(isHost, levelLoaded, observableClients);
                if (verdict == Bug1StartupRetryVerdict.Wait) return;
                if (verdict == Bug1StartupRetryVerdict.GiveUp)
                {
                    RoleLogger.Warn("[Shared]", Label + " RETRY_GIVEUP trigger=" + trigger
                        + " used=" + _startupRetries + " host=" + isHost + " level_loaded=" + levelLoaded
                        + " clients=" + observableClients);
                    UnhookStartupRetry();
                    return;
                }
                RoleLogger.Info("[Shared]", Label + " RETRY trigger=" + trigger + " attempt=" + (_startupRetries + 1)
                    + " host=" + isHost + " level_loaded=" + levelLoaded + " clients=" + observableClients);
                // R9-01：次数与摘钩/放弃判定收敛到纯函数，离线用例可穷举；此处只做接线。
                bool started = TryStart("retry:" + trigger);
                Bug1RetryOutcome outcome = Bug1ProbeEntryLogic.AccountRetryAttempt(
                    _startupRetries, MaxStartupRetries, started, Bug1ProbeRuntime.LastError);
                // T52-F2/R10-01：记账结果必须真的被采纳（写回次数、放弃日志、按 Unhook 摘钩）。
                // 采纳动作收敛到 ApplyRetryOutcome：行为用例直接断言该生产方法，IL 契约断言本方法确实调用它，
                // 于是「绕过采纳」与「保留调用但永不摘钩」两种回归都会变红。
                ApplyRetryOutcome(outcome);
            }
            catch (Exception ex)
            {
                RoleLogger.Warn("[Shared]", Label + " RETRY_FAILED trigger=" + trigger + " " + ex.GetType().Name);
            }
        }

        /// <summary>
        /// T52-F2/R10-01：采纳重试记账结果——写回已用次数、放弃时留 RETRY_GIVEUP 日志、按 Unhook 摘钩。
        /// 生产路径唯一调用点是 TryRetryStartup；抽出来是为了让「结果是否被采纳」可被行为用例直接断言。
        /// 语句、顺序与日志词与抽取前一致，不改变运行时语义。
        /// </summary>
        internal static void ApplyRetryOutcome(Bug1RetryOutcome outcome)
        {
            _startupRetries = outcome.AttemptsUsed;
            if (outcome.GiveUp)
            {
                RoleLogger.Warn("[Shared]", Label + " RETRY_GIVEUP attempts=" + _startupRetries);
            }
            if (outcome.Unhook) UnhookStartupRetry();
        }

        /// <summary>测试缝：把重试钩子标记为已挂载（不订阅 Unity 事件），供 ApplyRetryOutcome 行为用例使用。</summary>
        internal static void __TestArmRetryHook() { _retryHooked = true; }

        /// <summary>测试缝：只读重试钩子的挂载状态。</summary>
        internal static bool __TestRetryHookArmed { get { return _retryHooked; } }

        /// <summary>测试缝：只读已用重试次数。</summary>
        internal static int __TestStartupRetries { get { return _startupRetries; } }

        /// <summary>测试缝：复位重试状态，避免静态字段在用例间串味。</summary>
        internal static void __TestResetRetryState() { _startupRetries = 0; _retryHooked = false; }

        /// <summary>R8-03 纯判定入口：离线用例通过 Bug1ProbeEntryLogic.EvaluateStartupRetry 覆盖全部分支。</summary>
        internal static Bug1StartupRetryVerdict DecideRetry(bool isHost, bool levelLoaded, int observableClients)
        {
            return Bug1ProbeEntryLogic.EvaluateStartupRetry(
                _enabledEntry != null && _enabledEntry.Value,
                Bug1ProbeRuntime.Enabled,
                isHost, levelLoaded, observableClients, _startupRetries, MaxStartupRetries);
        }

        private static int CountObservableClients()
        {
            List<SteamPlayer> clients = Provider.clients;
            List<Bug1ClientSnapshot> snapshots = Bug1ProbeTargetSelector.SnapshotAll(clients);
            ulong local = 0UL;
            try { local = Provider.client.m_SteamID; } catch { }
            int count = 0;
            for (int i = 0; i < snapshots.Count; i++)
            {
                Bug1ClientSnapshot snapshot = snapshots[i];
                if (snapshot.IsNull || !snapshot.HasPlayerId) continue;
                if (local != 0UL && snapshot.SteamId == local) continue;
                count++;
            }
            return count;
        }
    }
}
