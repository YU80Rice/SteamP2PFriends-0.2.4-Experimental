using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx.Configuration;
using SDG.Unturned;
using SteamP2PFriends.Shared;
using UnityEngine;

namespace SteamP2PFriends.Platform.Diagnostics.Patches
{
    /// <summary>
    /// t85/t90：G 门实验探针（t76 §2）——**只测**，不实现插值。
    ///
    /// 默认关闭；未开启时不写日志、不挂任何帧回调。开启后对唯一远端目标做一次性测量：
    /// 在 `Model_0/Model_1` 叶节点上施加 X、Z 各 0.10U 的纯渲染偏移，
    /// 用 `SkinnedMeshRenderer.BakeMesh` 采真实蒙皮表面世界坐标，
    /// 逐 renderer 比较 `max‖(p_after−p_before)−delta‖`，并检查物理侧（root/Third/Skeleton/
    /// Collider world bounds/受控 raycast）在偏移前后字段级不变；本帧内还原且 localPosition 位级相等。
    ///
    /// t90 修订：判定口径回到 t76 §2.2（每 renderer 的 max 残差），比较函数下沉到
    /// <see cref="Bug1GateProbeLogic"/> 并由生产调用；epsilon 默认「未指定」，未指定时只给 UNKNOWN，
    /// 不自己拍阈值；隐藏/未激活 renderer 不进可见集合；快照读失败按 UNKNOWN（不是「没变」）；
    /// GATE.json 带 profile 指纹；目标在首帧前离场会写 JSON 并按上限重试。
    /// </summary>
    internal static class Bug1GateProbe
    {
        private const string Label = "[SPF-Bug1Gate]";
        private const int MaxArmRetries = 3;
        private const float RaycastLength = 3f;

        private static ConfigEntry<bool> _enabledEntry;
        private static ConfigEntry<string> _targetEntry;
        private static ConfigEntry<float> _offsetEntry;
        private static ConfigEntry<int> _samplesEntry;
        private static ConfigEntry<float> _epsilonEntry;
        private static ConfigEntry<float> _sampleIntervalEntry;
        private static ConfigEntry<int> _minSamplesEntry;
        private static ConfigEntry<int> _maxSamplesEntry;

        // R21-01：重试预算按触发源分开——level-loaded（托管就绪前）与 player-created 各 3 次，
        // 前者连续拒绝不得吃掉后者的额度；target-gone 是第三种独立情形，单独计数。
        private static readonly Bug1GateProbeLogic.Bug1RetryBudgetState _retryBudget =
            new Bug1GateProbeLogic.Bug1RetryBudgetState();
        private static int _targetGoneRetries;
        private static bool _levelRetryHooked;
        private static bool _playerRetryHooked;
        private static bool _cameraHooked;
        private static bool _ranOnce;
        private static Player _target;
        private static MethodInfo _bakeMesh;

        // t95：六格姿态覆盖的累计状态（只在一次启用周期内有效）。
        private static readonly List<Bug1GateCellSample> _cellSamples = new List<Bug1GateCellSample>();
        private static Bug1GateResult _lastResult;
        private static Bug1GateCoverageReport _coverage;
        private static string _lastHeadingBucket = Bug1GateProbeLogic.HeadingUnstable;
        private static int _headingStableSamples;
        private static int _sampleSeq;
        private static double _lastSampleTime = -1d;

        internal static void Initialize()
        {
            try
            {
                // t145：开关与参数统一从插件主 cfg 读取，不再创建/读取独立 Bug1Gate.cfg。
                _enabledEntry = Core.Config.PluginConfig.Bug1GateEnable;
                _targetEntry = Core.Config.PluginConfig.Bug1GateTargetPlayer;
                _offsetEntry = Core.Config.PluginConfig.Bug1GateOffsetU;
                _samplesEntry = Core.Config.PluginConfig.Bug1GateSamples;
                _epsilonEntry = Core.Config.PluginConfig.Bug1GateMaxDeviationU;
                _sampleIntervalEntry = Core.Config.PluginConfig.Bug1GateSampleIntervalSeconds;
                _minSamplesEntry = Core.Config.PluginConfig.Bug1GateMinSamplesPerCell;
                _maxSamplesEntry = Core.Config.PluginConfig.Bug1GateMaxSamples;
                if (_enabledEntry == null || _targetEntry == null || _offsetEntry == null
                    || _samplesEntry == null || _epsilonEntry == null || _sampleIntervalEntry == null
                    || _minSamplesEntry == null || _maxSamplesEntry == null)
                {
                    RoleLogger.Error("[Shared]", Label + " INIT_FAILED main config not bound (PluginConfig.Bind must run first)");
                    return;
                }

                _enabledEntry.SettingChanged += OnEnabledChanged;
                if (_enabledEntry.Value) Arm("startup");
            }
            catch (Exception ex)
            {
                RoleLogger.Error("[Shared]", Label + " INIT_FAILED " + ex.GetType().Name);
            }
        }

        /// <summary>
        /// 离线用例复用同一套绑定逻辑（ConfigFile 可离线构造）。
        /// `maxDeviation` 默认 0 = epsilon 未指定：此时判定只能 UNKNOWN/EPSILON_UNSPECIFIED。
        /// </summary>
        internal static void BindConfig(
            ConfigFile file,
            out ConfigEntry<bool> enabled,
            out ConfigEntry<string> target,
            out ConfigEntry<float> offset,
            out ConfigEntry<int> samples,
            out ConfigEntry<float> maxDeviation)
        {
            ConfigEntry<float> sampleInterval;
            ConfigEntry<int> minSamples;
            ConfigEntry<int> maxSamples;
            BindConfig(file, out enabled, out target, out offset, out samples, out maxDeviation,
                out sampleInterval, out minSamples, out maxSamples);
        }

        /// <summary>t95：额外绑定六格覆盖参数（采样间隔 / 每格最小有效样本 / 总样本上限）。</summary>
        internal static void BindConfig(
            ConfigFile file,
            out ConfigEntry<bool> enabled,
            out ConfigEntry<string> target,
            out ConfigEntry<float> offset,
            out ConfigEntry<int> samples,
            out ConfigEntry<float> maxDeviation,
            out ConfigEntry<float> sampleInterval,
            out ConfigEntry<int> minSamples,
            out ConfigEntry<int> maxSamples)
        {
            // t145：键定义收敛到 PluginConfig.BindBug1Gate（生产主 cfg 与离线用例同一份默认值）。
            Core.Config.PluginConfig.BindBug1Gate(file, out enabled, out target, out offset, out samples,
                out maxDeviation, out sampleInterval, out minSamples, out maxSamples);
        }

        private static void OnEnabledChanged(object sender, EventArgs e)
        {
            try
            {
                if (_enabledEntry != null && _enabledEntry.Value)
                {
                    _retryBudget.LevelLoaded = 0;
                    _retryBudget.PlayerCreated = 0;
                    _targetGoneRetries = 0;
                    _ranOnce = false;
                    ResetCoverageState();
                    UnhookRetryHook();
                    Arm("config-change");
                }
                else
                {
                    UnhookCamera();
                    UnhookRetryHook();
                    _target = null;
                }
            }
            catch (Exception ex)
            {
                RoleLogger.Error("[Shared]", Label + " CONFIG_CHANGE_FAILED " + ex.GetType().Name);
            }
        }

        private static void Arm(string origin)
        {
            if (_ranOnce)
            {
                RoleLogger.Warn("[Shared]", Label + " REFUSED " + Bug1GateProbeLogic.ReasonAlreadyRan + " origin=" + origin);
                return;
            }
            if (!Provider.isServer)
            {
                RoleLogger.Warn("[Shared]", Label + " REFUSED " + Bug1GateProbeLogic.ReasonRoleNotHost
                    + " origin=" + origin + "（只测 Listen Host；已挂有界重试，等本地 host 就绪）");
                EnsureRetryHook();
                return;
            }
            string error;
            Player resolved = ResolveTarget(out error);
            if (resolved == null)
            {
                RoleLogger.Warn("[Shared]", Label + " REFUSED " + (error ?? Bug1GateProbeLogic.ReasonTargetNotFound)
                    + " origin=" + origin + "（目标进入视野/关卡就绪后自动重试）");
                EnsureRetryHook();
                return;
            }
            _target = resolved;
            UnhookRetryHook();
            RoleLogger.Info("[Shared]", Label + " ARMED origin=" + origin + " target=" + Describe(resolved));
            HookCamera();
        }

        private static Player ResolveTarget(out string error)
        {
            error = null;
            ulong localSteamId = 0UL;
            try
            {
                if (Provider.client != null) localSteamId = Provider.client.m_SteamID;
            }
            catch (Exception ex)
            {
                error = Bug1GateProbeLogic.ReasonUnexpectedException + ":" + ex.GetType().Name;
                return null;
            }
            string selector = _targetEntry != null ? (_targetEntry.Value ?? string.Empty).Trim() : string.Empty;
            Bug1TargetSelection selection = Bug1ProbeTargetSelector.Select(
                Bug1ProbeTargetSelector.SnapshotAll(Provider.clients), selector, localSteamId);
            string mapped = Bug1GateProbeLogic.MapSelectionError(selection);
            if (mapped != null)
            {
                error = mapped;
                return null;
            }
            List<SteamPlayer> clients = Provider.clients;
            if (clients == null || selection.SelectedIndex < 0 || selection.SelectedIndex >= clients.Count)
            {
                error = Bug1GateProbeLogic.ReasonTargetNotFound;
                return null;
            }
            SteamPlayer client = clients[selection.SelectedIndex];
            if (ReferenceEquals(client, null) || ReferenceEquals(client.player, null))
            {
                error = Bug1GateProbeLogic.ReasonTargetNotFound;
                return null;
            }
            return client.player;
        }

        private static void EnsureRetryHook()
        {
            // R21-01：只挂「还有预算」的触发源。level-loaded 耗尽后不再重挂，
            // 但 player-created 仍按自己的额度继续等。
            if (!_levelRetryHooked && _retryBudget.LevelLoaded < MaxArmRetries)
            {
                _levelRetryHooked = true;
                try { Level.onLevelLoaded += OnLevelLoadedRetry; }
                catch (Exception ex)
                {
                    _levelRetryHooked = false;
                    RoleLogger.Warn("[Shared]", Label + " RETRY_HOOK_FAILED level " + ex.GetType().Name);
                }
            }
            if (!_playerRetryHooked && _retryBudget.PlayerCreated < MaxArmRetries)
            {
                _playerRetryHooked = true;
                try { Player.onPlayerCreated += OnPlayerCreatedRetry; }
                catch (Exception ex)
                {
                    _playerRetryHooked = false;
                    RoleLogger.Warn("[Shared]", Label + " RETRY_HOOK_FAILED player " + ex.GetType().Name);
                }
            }
        }

        private static void UnhookRetryHook()
        {
            if (_levelRetryHooked)
            {
                _levelRetryHooked = false;
                try { Level.onLevelLoaded -= OnLevelLoadedRetry; } catch { }
            }
            if (_playerRetryHooked)
            {
                _playerRetryHooked = false;
                try { Player.onPlayerCreated -= OnPlayerCreatedRetry; } catch { }
            }
        }

        /// <summary>
        /// R21-01：GIVEUP 只摘掉**耗尽的那个触发源**，另一源的订阅与额度保持不变。
        /// R21-02（t114）：未知/未来新增的源只记日志、**不动任何订阅**——
        /// 旧实现把所有非 level-loaded 的值都落到 player 分支，会误摘 player-created 钩子。
        /// </summary>
        private static void UnhookRetrySource(Bug1GateProbeLogic.Bug1RetryTrigger trigger)
        {
            switch (trigger)
            {
                case Bug1GateProbeLogic.Bug1RetryTrigger.LevelLoaded:
                    if (!_levelRetryHooked) return;
                    _levelRetryHooked = false;
                    try { Level.onLevelLoaded -= OnLevelLoadedRetry; } catch { }
                    return;
                case Bug1GateProbeLogic.Bug1RetryTrigger.PlayerCreated:
                    if (!_playerRetryHooked) return;
                    _playerRetryHooked = false;
                    try { Player.onPlayerCreated -= OnPlayerCreatedRetry; } catch { }
                    return;
                default:
                    RoleLogger.Warn("[Shared]", Label + " RETRY_UNHOOK_SKIPPED trigger="
                        + Bug1GateProbeLogic.TriggerName(trigger)
                        + "（未知触发源不摘除任何订阅）");
                    return;
            }
        }

        /// <summary>R21-01 测试缝：清空两个触发源的重试预算（不触碰 cfg / 相机 / 目标）。</summary>
        internal static void __TestResetRetryBudget()
        {
            _retryBudget.LevelLoaded = 0;
            _retryBudget.PlayerCreated = 0;
            _targetGoneRetries = 0;
            UnhookRetryHook();
        }

        /// <summary>R21-01 测试缝：走生产同一条记账路径（只记账，不 Arm）。</summary>
        internal static Bug1GateProbeLogic.Bug1RetryDecision __TestAccountRetry(
            Bug1GateProbeLogic.Bug1RetryTrigger trigger)
        {
            return AccountRetry(trigger);
        }

        /// <summary>R21-02 测试缝：未知源不得摘除任何订阅。</summary>
        internal static void __TestUnhookRetrySource(Bug1GateProbeLogic.Bug1RetryTrigger trigger)
        {
            UnhookRetrySource(trigger);
        }

        /// <summary>R21-02 测试缝：读两个触发源的订阅标志（不触碰 Unity 事件）。</summary>
        internal static void __TestRetryHookFlags(out bool levelLoaded, out bool playerCreated)
        {
            levelLoaded = _levelRetryHooked;
            playerCreated = _playerRetryHooked;
        }

        /// <summary>R21-02 测试缝：直接摆布订阅标志，用于验证「未知源不改订阅」。</summary>
        internal static void __TestSetRetryHookFlags(bool levelLoaded, bool playerCreated)
        {
            _levelRetryHooked = levelLoaded;
            _playerRetryHooked = playerCreated;
        }

        /// <summary>R21-01 测试缝：读两个触发源各自的已用次数。</summary>
        internal static void __TestRetryBudget(out int levelLoaded, out int playerCreated)
        {
            levelLoaded = _retryBudget.LevelLoaded;
            playerCreated = _retryBudget.PlayerCreated;
        }

        private static void OnLevelLoadedRetry(int level)
        {
            // R21-02：枚举常量——传错就编不过，不存在字符串漂移这条路径。
            TryRetry(Bug1GateProbeLogic.Bug1RetryTrigger.LevelLoaded);
        }

        private static void OnPlayerCreatedRetry(Player player)
        {
            TryRetry(Bug1GateProbeLogic.Bug1RetryTrigger.PlayerCreated);
        }

        /// <summary>
        /// R21-01：唯一记账点——按触发源消费预算。生产与用例走同一条路径，
        /// 把计数改回全局共享会让「level-loaded 用完后 player-created 仍可重试」的用例变红。
        /// </summary>
        private static Bug1GateProbeLogic.Bug1RetryDecision AccountRetry(
            Bug1GateProbeLogic.Bug1RetryTrigger trigger)
        {
            return Bug1GateProbeLogic.DecideRetry(_retryBudget, trigger, MaxArmRetries);
        }

        private static void TryRetry(Bug1GateProbeLogic.Bug1RetryTrigger trigger)
        {
            try
            {
                if (_enabledEntry == null || !_enabledEntry.Value) { UnhookRetryHook(); return; }
                Bug1GateProbeLogic.Bug1RetryDecision decision = AccountRetry(trigger);
                if (!decision.Retry)
                {
                    RoleLogger.Warn("[Shared]", Label + " " + decision.Reason
                        + " attempts=" + decision.Used + "/" + MaxArmRetries
                        + " trigger=" + decision.Trigger
                        + "（该触发源已耗尽；另一触发源保留自己的预算）");
                    UnhookRetrySource(trigger);
                    return;
                }
                RoleLogger.Info("[Shared]", Label + " RETRY attempt=" + decision.Used + "/" + MaxArmRetries
                    + " trigger=" + decision.Trigger);
                Arm("retry:" + decision.Trigger);
            }
            catch (Exception ex)
            {
                RoleLogger.Error("[Shared]", Label + " RETRY_FAILED " + ex.GetType().Name);
            }
        }

        private static void HookCamera()
        {
            if (_cameraHooked) return;
            _cameraHooked = true;
            try { Camera.onPreCull += OnPreCull; }
            catch (Exception ex)
            {
                _cameraHooked = false;
                RoleLogger.Error("[Shared]", Label + " CAMERA_HOOK_FAILED " + ex.GetType().Name);
            }
        }

        private static void ResetCoverageState()
        {
            _cellSamples.Clear();
            _lastResult = null;
            _coverage = null;
            _headingStableSamples = 0;
            _lastHeadingBucket = Bug1GateProbeLogic.HeadingUnstable;
            _sampleSeq = 0;
            _lastSampleTime = -1d;
        }

        private static void UnhookCamera()
        {
            if (!_cameraHooked) return;
            _cameraHooked = false;
            try { Camera.onPreCull -= OnPreCull; } catch { }
        }

        private static void OnPreCull(Camera camera)
        {
            if (!_cameraHooked) return;
            Player target = _target;
            if (ReferenceEquals(target, null) || !UnityObjectAlive(target))
            {
                UnhookCamera();
                HandleTargetGone();
                return;
            }
            try
            {
                _ranOnce = true;
                // 第一帧先跑 t92 的完整单帧测量（三条边 + 逐 renderer 残差），写首份 GATE.json。
                if (ReferenceEquals(_lastResult, null)) RunGate(target);
                if (!_cameraHooked) return;
                // 之后每帧按间隔做一次六格采样；覆盖完成/反例/上限即收尾。
                SampleOnce(target);
            }
            catch (Exception ex)
            {
                Bug1GateResult failed = NewResult(target);
                Fail(failed, Bug1GateProbeLogic.ReasonUnexpectedException, ex.GetType().Name);
                _lastResult = failed;
                Emit(failed);
                UnhookCamera();
            }
        }

        /// <summary>R16-02：目标在首帧采样前离场——写 JSON、按上限重试，耗尽时明确「已放弃+原因」。</summary>
        private static void HandleTargetGone()
        {
            Bug1GateResult result = NewResult(null);
            Fail(result, Bug1GateProbeLogic.ReasonTargetGone, "target left between arm and first sample");
            _ranOnce = false;
            if (_targetGoneRetries < MaxArmRetries)
            {
                _targetGoneRetries++;
                result.Notes.Add("retry " + _targetGoneRetries.ToString(CultureInfo.InvariantCulture) + "/"
                    + MaxArmRetries.ToString(CultureInfo.InvariantCulture) + " after target gone");
                Emit(result);
                RoleLogger.Warn("[Shared]", Label + " TARGET_GONE retry=" + _targetGoneRetries + "/" + MaxArmRetries);
                EnsureRetryHook();
                return;
            }
            result.Notes.Add("gave up after " + MaxArmRetries.ToString(CultureInfo.InvariantCulture)
                + " retries: target kept leaving before first sample");
            Emit(result);
            RoleLogger.Error("[Shared]", Label + " GIVEUP reason=" + Bug1GateProbeLogic.ReasonTargetGone
                + " attempts=" + _targetGoneRetries + "（已写 GATE.json，放弃本次启用周期）");
        }

        // ---- t95：六格（idle/walk/sprint × 两朝向）姿态覆盖采样 ----

        /// <summary>
        /// 一次六格采样：判格子 → 零偏移对照（C0/C1）→ 同帧内 X/Z 各扰一次并完整恢复。
        /// 采样间隔、每格最小样本、总样本上限都来自 cfg；覆盖完成/反例/上限即收尾写 GATE.json。
        /// </summary>
        private static void SampleOnce(Player target)
        {
            double now;
            if (!TryReadRealtime(out now)) return;
            double interval = _sampleIntervalEntry != null ? _sampleIntervalEntry.Value : 1.0d;
            if (interval < 0d) interval = 0d;
            if (_lastSampleTime >= 0d && now - _lastSampleTime < interval) return;
            _lastSampleTime = now;

            Transform third = null;
            try { third = target.third; } catch { }
            if (ReferenceEquals(third, null))
            {
                FinishCoverage("third_unresolved");
                return;
            }
            Transform skeleton = SafeFind(third, "Skeleton");
            var modelNodes = new List<Transform>(2);
            Transform model0 = SafeFind(third, "Model_0");
            Transform model1 = SafeFind(third, "Model_1");
            if (!ReferenceEquals(model0, null)) modelNodes.Add(model0);
            if (!ReferenceEquals(model1, null)) modelNodes.Add(model1);
            List<SkinnedMeshRenderer> renderers = CollectVisibleRenderers(modelNodes, NewResult(target));
            if (renderers.Count == 0)
            {
                FinishCoverage("no_visible_surface");
                return;
            }

            string stanceName = StanceName(target);
            bool moving = IsMoving(target);
            string speedReason;
            // R17-02：EPlayerStance 没有 IDLE，站立不动与站立行走都报 STAND，必须叠加原生 isMoving。
            string speedCell = Bug1GateProbeLogic.ClassifyStanceCell(stanceName, moving, out speedReason);
            Vector3 forward = Vector3.forward;
            try { forward = target.transform.forward; } catch { }
            float yawDeg;
            string headingReason;
            string bucket = Bug1GateProbeLogic.ClassifyHeadingBucket(
                forward.x, forward.z, out yawDeg, out headingReason);
            int stableCount;
            string heading = Bug1GateProbeLogic.AdvanceHeadingStability(
                _lastHeadingBucket, bucket, _headingStableSamples, out stableCount);
            _lastHeadingBucket = bucket;
            _headingStableSamples = stableCount;
            string cell = Bug1GateProbeLogic.ComposeCellId(speedCell, heading);
            _sampleSeq++;
            if (cell == null)
            {
                _cellSamples.Add(new Bug1GateCellSample
                {
                    Cell = "unassigned",
                    SampleId = "s" + _sampleSeq.ToString(CultureInfo.InvariantCulture),
                    Available = false,
                    Stance = stanceName,
                    MeasuredYawDeg = yawDeg,
                    Detail = "cell-unstable:" + speedReason + "/" + headingReason
                        + " stance=" + stanceName + " moving=" + moving,
                });
                CheckCoverageLimits();
                return;
            }

            Bug1PoseSnapshot c0 = SnapshotPose(skeleton, target, renderers);
            Bug1PoseSnapshot c1 = SnapshotPose(skeleton, target, renderers);
            if (!c0.Available || !c1.Available || !Bug1GateProbeLogic.PosesEqual(c0, c1))
            {
                _cellSamples.Add(new Bug1GateCellSample
                {
                    Cell = cell,
                    SampleId = "s" + _sampleSeq.ToString(CultureInfo.InvariantCulture),
                    Available = false,
                    Stance = stanceName,
                    MeasuredYawDeg = yawDeg,
                    Detail = Bug1GateProbeLogic.ReasonPoseUnstable + ":"
                        + Bug1GateProbeLogic.PoseDiff(c0, c1),
                });
                CheckCoverageLimits();
                return;
            }
            double offset = OffsetU();
            MeasureCellAxis(target, third, skeleton, modelNodes, renderers, cell, "X",
                new Vector3((float)offset, 0f, 0f), c1, stanceName, yawDeg);
            MeasureCellAxis(target, third, skeleton, modelNodes, renderers, cell, "Z",
                new Vector3(0f, 0f, (float)offset), c1, stanceName, yawDeg);
            CheckCoverageLimits();
        }

        /// <summary>
        /// 单轴事务：基线 Bake（P0）→ 施加 delta → P1 + 姿态 A1 → finally 还原 → P2 + 姿态 A2。
        /// P0/P1/P2 与 A0/A1/A2 都在同一个相机回调内完成，不跨帧比较动画自然变化。
        /// </summary>
        private static void MeasureCellAxis(
            Player target,
            Transform third,
            Transform skeleton,
            List<Transform> modelNodes,
            List<SkinnedMeshRenderer> renderers,
            string cell,
            string axis,
            Vector3 delta,
            Bug1PoseSnapshot baselinePose,
            string stanceName,
            double measuredYawDeg)
        {
            var sample = new Bug1GateCellSample
            {
                Cell = cell,
                SampleId = "s" + _sampleSeq.ToString(CultureInfo.InvariantCulture) + "-" + axis,
                Stance = stanceName,
                MeasuredYawDeg = measuredYawDeg,
            };
            var originals = new List<Vector3>(modelNodes.Count);
            var originalBits = new List<Vector3>(modelNodes.Count);
            for (int i = 0; i < modelNodes.Count; i++)
            {
                originals.Add(modelNodes[i].localPosition);
                originalBits.Add(modelNodes[i].localPosition);
            }
            var localBitsBefore = new float[modelNodes.Count * 3];
            for (int i = 0; i < modelNodes.Count; i++)
            {
                localBitsBefore[i * 3] = originalBits[i].x;
                localBitsBefore[i * 3 + 1] = originalBits[i].y;
                localBitsBefore[i * 3 + 2] = originalBits[i].z;
            }
            Bug1GateTransformSnapshot rootBefore = SnapshotTransform(target.transform);
            Bug1GateTransformSnapshot thirdBefore = SnapshotTransform(third);
            Bug1GateTransformSnapshot skeletonBefore = SnapshotTransform(skeleton);
            bool collidersBeforeAvailable;
            List<Bug1GateColliderSnapshot> colliderBefore = SnapshotColliders(target, out collidersBeforeAvailable);
            Bug1GateRaycastSnapshot raycastBefore = RaycastProbe(third);

            var baseline = new List<List<Vector3>>(renderers.Count);
            for (int r = 0; r < renderers.Count; r++)
            {
                baseline.Add(BakeSample(renderers[r], SampleCap()));
            }
            double offset = OffsetU();
            try
            {
                for (int i = 0; i < modelNodes.Count; i++)
                {
                    modelNodes[i].localPosition = originals[i] + delta;
                }
                Bug1PoseSnapshot poseAfter = SnapshotPose(skeleton, target, renderers);
                bool poseEqual = Bug1GateProbeLogic.PosesEqual(baselinePose, poseAfter);
                string poseDiff = poseEqual ? string.Empty : Bug1GateProbeLogic.PoseDiff(baselinePose, poseAfter);
                double maxResidual = 0d;
                double maxMoved = 0d;
                double minMoved = double.MaxValue;
                bool anyBake = false;
                for (int r = 0; r < renderers.Count; r++)
                {
                    List<Vector3> before = baseline[r];
                    List<Vector3> moved = BakeSample(renderers[r], SampleCap());
                    if (before == null || moved == null || before.Count != moved.Count) continue;
                    anyBake = true;
                    for (int p = 0; p < before.Count; p++)
                    {
                        Vector3 diff = moved[p] - before[p];
                        double residual = (diff - delta).magnitude;
                        double movedBy = diff.magnitude;
                        if (residual > maxResidual) maxResidual = residual;
                        if (movedBy > maxMoved) maxMoved = movedBy;
                        if (movedBy < minMoved) minMoved = movedBy;
                    }
                }
                sample.Available = anyBake;
                sample.PoseEqual = poseEqual;
                sample.VertexMoved = anyBake && maxMoved >= offset * 0.5d;
                sample.Torn = anyBake && maxMoved >= offset * 0.5d && minMoved <= offset * 0.1d;
                sample.MaxResidualU = maxResidual;
                sample.EpsilonSpecified = EpsilonU() > 0f;
                sample.Detail = "axis=" + axis
                    + " max_moved_u=" + maxMoved.ToString("R", CultureInfo.InvariantCulture)
                    + " min_moved_u=" + (minMoved == double.MaxValue ? "n/a" : minMoved.ToString("R", CultureInfo.InvariantCulture))
                    + (poseEqual ? string.Empty : " pose_diff=" + poseDiff);
            }
            finally
            {
                Restore(modelNodes, originals);
            }

            var localBitsAfter = new float[modelNodes.Count * 3];
            for (int i = 0; i < modelNodes.Count; i++)
            {
                Vector3 restored = modelNodes[i].localPosition;
                localBitsAfter[i * 3] = restored.x;
                localBitsAfter[i * 3 + 1] = restored.y;
                localBitsAfter[i * 3 + 2] = restored.z;
            }
            // R17-03：还原门是位级比较（逐 float32 位），不做 epsilon；MV-5 就是把它改成恒真。
            bool restoreExact = Bug1GateProbeLogic.RestoreBitsEqual(localBitsBefore, localBitsAfter);
            Bug1PoseSnapshot poseRestored = SnapshotPose(skeleton, target, renderers);
            bool poseRestoredEqual = Bug1GateProbeLogic.PosesEqual(baselinePose, poseRestored);
            // R17-03：还原后必须复烘 P2 并与 P0 基线比较，证明可见表面真的回到基线（此前缺这一步）。
            double restoreResidual = 0d;
            bool surfaceRestored = true;
            for (int r = 0; r < renderers.Count; r++)
            {
                List<Vector3> before = baseline[r];
                List<Vector3> restored = BakeSample(renderers[r], SampleCap());
                if (before == null || restored == null || before.Count != restored.Count)
                {
                    surfaceRestored = false;
                    continue;
                }
                for (int p = 0; p < before.Count; p++)
                {
                    double residual = (restored[p] - before[p]).magnitude;
                    if (residual > restoreResidual) restoreResidual = residual;
                }
            }
            if (restoreResidual > Bug1GateProbeLogic.RestoreEpsilonU) surfaceRestored = false;
            bool collidersAfterAvailable;
            List<Bug1GateColliderSnapshot> colliderAfter = SnapshotColliders(target, out collidersAfterAvailable);
            Bug1GateRaycastSnapshot raycastAfter = RaycastProbe(third);
            bool edges = rootBefore.Available && thirdBefore.Available && skeletonBefore.Available
                && Bug1GateProbeLogic.TransformsEqual(rootBefore, SnapshotTransform(target.transform))
                && Bug1GateProbeLogic.TransformsEqual(thirdBefore, SnapshotTransform(third))
                && Bug1GateProbeLogic.TransformsEqual(skeletonBefore, SnapshotTransform(skeleton));
            sample.InvarianceOk = edges && restoreExact && surfaceRestored && poseRestoredEqual
                && collidersBeforeAvailable && collidersAfterAvailable
                && Bug1GateProbeLogic.CollidersEqual(colliderBefore, colliderAfter)
                && raycastBefore.Available && raycastAfter.Available
                && Bug1GateProbeLogic.RaycastsEqual(raycastBefore, raycastAfter);
            if (!sample.InvarianceOk && string.IsNullOrEmpty(sample.Detail))
            {
                sample.Detail = "invariance_failed restore_exact=" + restoreExact
                    + " surface_restored=" + surfaceRestored
                    + " restore_residual_u=" + restoreResidual.ToString("R", CultureInfo.InvariantCulture);
            }
            _cellSamples.Add(sample);
        }

        private static double OffsetU()
        {
            return _offsetEntry != null && _offsetEntry.Value > 0f
                ? _offsetEntry.Value
                : Bug1GateProbeLogic.DefaultOffsetU;
        }

        private static float EpsilonU()
        {
            return _epsilonEntry != null ? _epsilonEntry.Value : 0f;
        }

        private static int SampleCap()
        {
            return _samplesEntry != null && _samplesEntry.Value > 0
                ? _samplesEntry.Value
                : Bug1GateProbeLogic.DefaultSampleCount;
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static bool TryReadRealtime(out double value)
        {
            value = 0d;
            try
            {
                value = Time.realtimeSinceStartup;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string StanceName(Player target)
        {
            try
            {
                return target.stance.stance.ToString();
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>R17-02：原生移动标志（PlayerMovement.isMoving），与 stance 叠加派生 idle/walk。</summary>
        private static bool IsMoving(Player target)
        {
            try
            {
                return target.movement.isMoving;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>R18-03：总样本硬上限（含不可用样本），防止未定样本无限追加。</summary>
        internal const int MaxTotalSamplesDefault = 4000;

        /// <summary>R18-02：深度上限纯判定（64 允许、65 拒绝），供 CollectSubtree 与离线用例共用。</summary>
        internal static bool PoseDepthAllowed(int depth)
        {
            return depth >= 0 && depth <= Bug1GateProbeLogic.MaxPoseDepth;
        }

        /// <summary>R18-01：节点数超限即截断（与 PoseWalkAllowed 互补，便于离线断言）。</summary>
        internal static bool PoseWalkTruncated(int collectedNodes, int maxNodes)
        {
            return !Bug1GateProbeLogic.PoseWalkAllowed(collectedNodes, maxNodes);
        }

        /// <summary>R18-01：截断必须让快照不可用，原因里带 collected/limit 供复核。</summary>
        internal static string BuildPoseTruncatedReason(int collectedNodes, int maxNodes)
        {
            int cap = maxNodes > 0 ? maxNodes : Bug1GateProbeLogic.MaxPoseNodes;
            return "pose_truncated:" + collectedNodes.ToString(CultureInfo.InvariantCulture)
                + "/" + cap.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// R19-01：截断 → 快照可用的唯一决策点。返回 true 表示「快照可用」；截断时返回 false
        /// 并给出 pose_truncated:&lt;collected&gt;/&lt;limit&gt;。SnapshotPose 只调 ApplyPoseAvailability，
        /// 不再自己拼条件，避免「调了函数但结果被丢弃/反转」的假绿。
        /// </summary>
        internal static bool PoseSnapshotAvailable(Bug1PoseWalkState state, out string reason)
        {
            reason = null;
            if (ReferenceEquals(state, null) || !state.Truncated) return true;
            reason = BuildPoseTruncatedReason(state.Collected, Bug1GateProbeLogic.MaxPoseNodes);
            return false;
        }

        /// <summary>R19-01：PoseSnapshotAvailable 的互补命名（true=必须标不可用），供调用方按语义取用。</summary>
        internal static bool ShouldMarkPoseUnavailable(Bug1PoseWalkState state, out string reason)
        {
            return !PoseSnapshotAvailable(state, out reason);
        }

        /// <summary>
        /// R19-01：把可用性判定落到快照上（唯一写入点）。返回值与 snapshot.Available 必须一致；
        /// 截断时同时写 UnavailableReason。用例直接读返回值与快照字段，因此把 Available 反转会变红。
        /// </summary>
        internal static bool ApplyPoseAvailability(Bug1PoseSnapshot snapshot, Bug1PoseWalkState state)
        {
            string reason;
            bool available = PoseSnapshotAvailable(state, out reason);
            if (ReferenceEquals(snapshot, null)) return available;
            if (available)
            {
                snapshot.Available = true;
                snapshot.UnavailableReason = null;
                return true;
            }
            snapshot.Available = false;
            snapshot.UnavailableReason = reason;
            return false;
        }

        /// <summary>R18-03：总样本预算按 _cellSamples.Count（含不可用样本）判定。</summary>
        internal static bool TotalSamplesExceeded(int totalSamples)
        {
            return totalSamples >= MaxTotalSamplesDefault;
        }

        /// <summary>R18-05：覆盖模式 note 只能由这里生成，单帧口径在该路径上不可达。</summary>
        internal static string BuildCoverageNote(int samples, int validSamples)
        {
            return Bug1GateProbeLogic.ModeCoverageNote
                + "；采样 " + samples.ToString(CultureInfo.InvariantCulture)
                + " 次、有效样本 " + validSamples.ToString(CultureInfo.InvariantCulture) + " 个";
        }

        /// <summary>R18-05：单次测量路径专用 note（与覆盖 note 分开，避免模式混写）。</summary>
        internal static string BuildSingleNote()
        {
            return Bug1GateProbeLogic.ModeSingleNote;
        }

        /// <summary>覆盖收尾：反例/样本上限/全部通过时停采，把汇总写进同一份 GATE.json。</summary>
        private static void CheckCoverageLimits()
        {
            // R18-03：总样本（含不可用样本）硬上限，防止频繁转向/越界姿态把列表撑爆。
            if (TotalSamplesExceeded(_cellSamples.Count))
            {
                FinishCoverage("max_total_samples");
                return;
            }
            int maxSamples = _maxSamplesEntry != null && _maxSamplesEntry.Value > 0
                ? _maxSamplesEntry.Value
                : Bug1GateProbeLogic.MaxSamplesDefault;
            int minSamples = _minSamplesEntry != null && _minSamplesEntry.Value > 0
                ? _minSamplesEntry.Value
                : Bug1GateProbeLogic.MinSamplesPerCellDefault;
            Bug1GateCoverageReport report = Bug1GateProbeLogic.EvaluateCoverage(_cellSamples, minSamples, EpsilonU());
            if (report.Status == Bug1GateProbeLogic.StatusPass)
            {
                FinishCoverage("all_cells_passed");
                return;
            }
            if (report.Status == Bug1GateProbeLogic.StatusFail)
            {
                FinishCoverage("counterexample");
                return;
            }
            // R17-04：只有可用样本才占预算；unassigned/姿态不稳样本不计，避免频繁转向提前耗尽上限。
            if (Bug1GateProbeLogic.CountValidSamples(_cellSamples) >= maxSamples)
            {
                FinishCoverage("max_samples_reached");
            }
        }

        private static void FinishCoverage(string reason)
        {
            UnhookCamera();
            _target = null;
            int minSamples = _minSamplesEntry != null && _minSamplesEntry.Value > 0
                ? _minSamplesEntry.Value
                : Bug1GateProbeLogic.MinSamplesPerCellDefault;
            _coverage = Bug1GateProbeLogic.EvaluateCoverage(_cellSamples, minSamples, EpsilonU());
            Bug1GateResult result = _lastResult ?? NewResult(null);
            int validSamples = Bug1GateProbeLogic.CountValidSamples(_cellSamples);
            result.Notes.Add("coverage_finished:" + reason
                + " samples=" + _cellSamples.Count.ToString(CultureInfo.InvariantCulture)
                + " valid=" + validSamples.ToString(CultureInfo.InvariantCulture));
            // R18-05：覆盖模式只能调 BuildCoverageNote；BuildSingleNote 在此路径不可达。
            result.Notes.Add(BuildCoverageNote(_cellSamples.Count, validSamples));
            Emit(result);
            RoleLogger.Info("[Shared]", Label + " COVERAGE status=" + _coverage.Status
                + " reason=" + _coverage.Reason + " detail=" + _coverage.Detail
                + " samples=" + _cellSamples.Count.ToString(CultureInfo.InvariantCulture)
                + " valid=" + validSamples.ToString(CultureInfo.InvariantCulture)
                + " finished=" + reason);
        }

        private static Bug1GateResult NewResult(Player target)
        {
            var result = new Bug1GateResult
            {
                RunId = "gate-" + DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture),
                TargetName = target == null ? string.Empty : Describe(target),
                OffsetU = _offsetEntry != null ? _offsetEntry.Value : Bug1GateProbeLogic.DefaultOffsetU,
            };
            float epsilon = _epsilonEntry != null ? _epsilonEntry.Value : 0f;
            if (epsilon > 0f)
            {
                result.EpsilonU = epsilon;
                result.EpsilonSource = Bug1GateProbeLogic.EpsilonSourceConfig;
            }
            else
            {
                result.EpsilonU = Bug1GateProbeLogic.UnspecifiedEpsilonU;
                result.EpsilonSource = Bug1GateProbeLogic.EpsilonSourceUnspecified;
            }
            return result;
        }

        private static void RunGate(Player target)
        {
            Bug1GateResult result = NewResult(target);
            _lastResult = result;
            Transform third = null;
            try { third = target.third; } catch { third = null; }
            if (ReferenceEquals(third, null))
            {
                Fail(result, Bug1GateProbeLogic.ReasonThirdUnresolved, "player.third is null");
                Emit(result);
                return;
            }

            Transform skeleton = SafeFind(third, "Skeleton");
            Transform model0 = SafeFind(third, "Model_0");
            Transform model1 = SafeFind(third, "Model_1");
            var modelNodes = new List<Transform>(2);
            if (!ReferenceEquals(model0, null)) modelNodes.Add(model0);
            if (!ReferenceEquals(model1, null)) modelNodes.Add(model1);
            if (modelNodes.Count == 0)
            {
                Fail(result, Bug1GateProbeLogic.ReasonModelNodeMissing, "neither Model_0 nor Model_1 exists under Third");
                Emit(result);
                return;
            }

            // t76 §2.1 纯节点门：先收集子树里的组件类型名，判定交给纯函数（R16-01）。
            List<string> impure = Bug1GateProbeLogic.CollectImpureComponentTypes(CollectComponentTypeNames(modelNodes));
            result.PureNode = impure.Count == 0;
            if (!result.PureNode)
            {
                Fail(result, Bug1GateProbeLogic.ReasonImpureRenderNode,
                    Bug1GateProbeLogic.FormatImpureComponents(impure));
                Emit(result);
                return;
            }

            List<SkinnedMeshRenderer> renderers = CollectVisibleRenderers(modelNodes, result);
            if (renderers.Count == 0)
            {
                Fail(result, Bug1GateProbeLogic.ReasonNoVisibleSurface,
                    "no enabled/active SkinnedMeshRenderer with a non-empty mesh under Model_0/Model_1");
                Emit(result);
                return;
            }

            int samples = _samplesEntry != null && _samplesEntry.Value > 0
                ? _samplesEntry.Value
                : Bug1GateProbeLogic.DefaultSampleCount;
            double offset = result.OffsetU > 0d ? result.OffsetU : Bug1GateProbeLogic.DefaultOffsetU;
            var delta = new Vector3((float)offset, 0f, 0f);
            var deltaZ = new Vector3(0f, 0f, (float)offset);

            var originals = new List<Vector3>(modelNodes.Count);
            var originalBits = new List<Vector3>(modelNodes.Count);
            for (int i = 0; i < modelNodes.Count; i++)
            {
                originals.Add(modelNodes[i].localPosition);
                originalBits.Add(modelNodes[i].localPosition);
            }

            Bug1GateTransformSnapshot rootBefore = SnapshotTransform(target.transform);
            Bug1GateTransformSnapshot thirdBefore = SnapshotTransform(third);
            Bug1GateTransformSnapshot skeletonBefore = SnapshotTransform(skeleton);
            bool collidersAvailable;
            List<Bug1GateColliderSnapshot> colliderBefore = SnapshotColliders(target, out collidersAvailable);
            Bug1GateRaycastSnapshot raycastBefore = RaycastProbe(third);
            result.ColliderAvailable = collidersAvailable;
            result.RaycastAvailable = raycastBefore.Available;

            var baseline = new Dictionary<SkinnedMeshRenderer, List<Vector3>>();
            var displacementLists = new List<List<double>>(renderers.Count);
            for (int i = 0; i < renderers.Count; i++)
            {
                SkinnedMeshRenderer renderer = renderers[i];
                Bug1GateRendererStats stats = BuildStats(renderer, third);
                displacementLists.Add(new List<double>());
                result.Renderers.Add(stats);
                List<Vector3> points = BakeSample(renderer, samples);
                if (points == null)
                {
                    Fail(result, Bug1GateProbeLogic.ReasonBakeFailed, "baseline bake failed: " + SafeName(renderer));
                    Restore(modelNodes, originals);
                    Emit(result);
                    return;
                }
                baseline[renderer] = points;
                stats.SampleCount = points.Count;
                result.SampleCount += points.Count;
            }
            result.RendererCount = renderers.Count;
            result.ProfileHash = Bug1GateProbeLogic.ComputeProfileHash(BuildProfiles(renderers, third));

            bool restoreExact = true;
            bool surfaceRestored = true;
            try
            {
                MeasureAxis(renderers, modelNodes, originals, delta, baseline, result.Renderers, displacementLists);
                MeasureAxis(renderers, modelNodes, originals, deltaZ, baseline, result.Renderers, displacementLists);
            }
            finally
            {
                Restore(modelNodes, originals);
            }

            for (int i = 0; i < modelNodes.Count; i++)
            {
                if (!Bug1GateProbeLogic.BitEqual(modelNodes[i].localPosition.x, originalBits[i].x)
                    || !Bug1GateProbeLogic.BitEqual(modelNodes[i].localPosition.y, originalBits[i].y)
                    || !Bug1GateProbeLogic.BitEqual(modelNodes[i].localPosition.z, originalBits[i].z))
                {
                    restoreExact = false;
                }
            }
            result.RestoreExact = restoreExact;

            // 覆盖/可逆门：还原后重新烘一次，必须回到基线 epsilon 内。
            for (int i = 0; i < renderers.Count; i++)
            {
                SkinnedMeshRenderer renderer = renderers[i];
                List<Vector3> restored = BakeSample(renderer, samples);
                List<Vector3> before = baseline[renderer];
                if (restored == null || restored.Count != before.Count)
                {
                    surfaceRestored = false;
                    continue;
                }
                for (int p = 0; p < restored.Count; p++)
                {
                    if ((restored[p] - before[p]).magnitude > Bug1GateProbeLogic.RestoreEpsilonU)
                    {
                        surfaceRestored = false;
                        break;
                    }
                }
            }
            result.SurfaceRestored = surfaceRestored;

            Bug1GateTransformSnapshot rootAfter = SnapshotTransform(target.transform);
            Bug1GateTransformSnapshot thirdAfter = SnapshotTransform(third);
            Bug1GateTransformSnapshot skeletonAfter = SnapshotTransform(skeleton);
            bool collidersAfterAvailable;
            List<Bug1GateColliderSnapshot> colliderAfter = SnapshotColliders(target, out collidersAfterAvailable);
            Bug1GateRaycastSnapshot raycastAfter = RaycastProbe(third);

            result.RootBefore = rootBefore;
            result.RootAfter = rootAfter;
            result.ThirdBefore = thirdBefore;
            result.ThirdAfter = thirdAfter;
            result.SkeletonBefore = skeletonBefore;
            result.SkeletonAfter = skeletonAfter;
            result.ColliderBefore.AddRange(colliderBefore);
            result.ColliderAfter.AddRange(colliderAfter);
            result.RaycastBefore = raycastBefore;
            result.RaycastAfter = raycastAfter;

            // T92-F1：三条物理边逐条判定——root / Third / Skeleton 任一条被动了都要 FAIL，
            // 拒绝码分别落到 ROOT_CHANGED / THIRD_CHANGED / SKELETON_CHANGED，不许只比其中两条。
            result.TransformsAvailable = rootBefore.Available && rootAfter.Available
                && thirdBefore.Available && thirdAfter.Available
                && skeletonBefore.Available && skeletonAfter.Available;
            result.RootUnchanged = Bug1GateProbeLogic.TransformsEqual(rootBefore, rootAfter);
            result.ThirdUnchanged = Bug1GateProbeLogic.TransformsEqual(thirdBefore, thirdAfter);
            result.SkeletonUnchanged = Bug1GateProbeLogic.TransformsEqual(skeletonBefore, skeletonAfter);

            // T86-F4：任一侧快照不可用 → 保持 available=false，交给 Decide 判 UNKNOWN（不是「没变」）。
            result.ColliderAvailable = collidersAvailable && collidersAfterAvailable;
            result.RaycastAvailable = raycastBefore.Available && raycastAfter.Available;
            result.CollidersUnchanged = result.ColliderAvailable
                && Bug1GateProbeLogic.CollidersEqual(colliderBefore, colliderAfter);
            result.RaycastUnchanged = result.RaycastAvailable
                && Bug1GateProbeLogic.RaycastsEqual(raycastBefore, raycastAfter);

            Aggregate(result, displacementLists);
            result.RuntimeMeasured = true;
            var invariance = new Bug1GateInvariance
            {
                TransformsAvailable = result.TransformsAvailable,
                RootUnchanged = result.RootUnchanged,
                ThirdUnchanged = result.ThirdUnchanged,
                SkeletonUnchanged = result.SkeletonUnchanged,
                CollidersAvailable = result.ColliderAvailable,
                CollidersUnchanged = result.CollidersUnchanged,
                RaycastAvailable = result.RaycastAvailable,
                RaycastUnchanged = result.RaycastUnchanged,
            };
            string status;
            string reason = Bug1GateProbeLogic.Decide(
                true,
                result.PureNode,
                result.RestoreExact,
                result.SurfaceRestored,
                invariance,
                result.OffsetU,
                result.Renderers,
                result.EpsilonU,
                out status);
            result.Status = status;
            result.Reason = reason;
            result.DeviationRatio = Bug1GateProbeLogic.DeviationRatio(result.OffsetU,
                new Bug1GateDisplacementStats { Count = result.SampleCount, Median = result.MedianU, P50 = result.P50U });
            // R18-05：单次测量路径只调 BuildSingleNote；覆盖模式另走 BuildCoverageNote。
            result.Notes.Add(BuildSingleNote());
            result.Notes.Add("transform_edges_partial：本探针只保证 root / Third / Skeleton 根三条变换未变"
                + "（任一条被动了即 FAIL：ROOT_CHANGED / THIRD_CHANGED / SKELETON_CHANGED）；"
                + "SnapshotTransform 只读世界 position + quaternion（7 个标量、无 scale），"
                + "Skeleton 根不变不等于子骨骼没被动过——子骨骼 pose 覆盖不在本探针范围内，由后续轮处理");
            if (result.EpsilonU <= 0d)
            {
                result.Notes.Add("epsilon_source=unspecified：t76 §2.2 未给数值，判定停在 UNKNOWN；"
                    + "请决策者按 max_residual_u 定阈，严禁实现方自选阈值");
            }
            Emit(result);
        }

        /// <summary>逐轴施加 delta，记录每个 renderer 全部采样点的残差与位移（t76 §2.2 原口径）。</summary>
        private static void MeasureAxis(
            List<SkinnedMeshRenderer> renderers,
            List<Transform> modelNodes,
            List<Vector3> originals,
            Vector3 delta,
            Dictionary<SkinnedMeshRenderer, List<Vector3>> baseline,
            List<Bug1GateRendererStats> statsList,
            List<List<double>> displacement)
        {
            for (int i = 0; i < modelNodes.Count; i++)
            {
                modelNodes[i].localPosition = originals[i] + delta;
            }
            for (int r = 0; r < renderers.Count; r++)
            {
                SkinnedMeshRenderer renderer = renderers[r];
                List<Vector3> before = baseline[renderer];
                List<Vector3> moved = BakeSample(renderer, before.Count);
                if (moved == null || moved.Count != before.Count)
                {
                    continue;
                }
                Bug1GateRendererStats stats = statsList[r];
                List<double> magnitudes = displacement[r];
                for (int p = 0; p < before.Count; p++)
                {
                    Vector3 diff = moved[p] - before[p];
                    double residual = (diff - delta).magnitude;
                    double movedBy = diff.magnitude;
                    if (residual > stats.MaxResidualU) stats.MaxResidualU = residual;
                    if (movedBy > stats.MaxDisplacementU) stats.MaxDisplacementU = movedBy;
                    magnitudes.Add(movedBy);
                }
            }
        }

        private static void Restore(List<Transform> modelNodes, List<Vector3> originals)
        {
            for (int i = 0; i < modelNodes.Count && i < originals.Count; i++)
            {
                modelNodes[i].localPosition = originals[i];
            }
        }

        private static void Aggregate(Bug1GateResult result, List<List<double>> displacement)
        {
            double worst = 0d;
            double worstRatio = 0d;
            var all = new List<double>();
            for (int i = 0; i < result.Renderers.Count; i++)
            {
                Bug1GateRendererStats stats = result.Renderers[i];
                List<double> magnitudes = i < displacement.Count ? displacement[i] : null;
                if (magnitudes != null && magnitudes.Count > 0)
                {
                    Bug1GateDisplacementStats summary = Bug1GateProbeLogic.Summarize(magnitudes);
                    stats.P10 = summary.P10;
                    stats.P50 = summary.P50;
                    stats.P90 = summary.P90;
                    all.AddRange(magnitudes);
                }
                stats.MaxDeviationRatio = Bug1GateProbeLogic.MaxDeviationRatio(result.OffsetU, stats.MaxResidualU);
                if (stats.MaxResidualU > worst) worst = stats.MaxResidualU;
                if (stats.MaxDeviationRatio > worstRatio) worstRatio = stats.MaxDeviationRatio;
            }
            Bug1GateDisplacementStats totals = Bug1GateProbeLogic.Summarize(all);
            result.MaxResidualU = worst;
            result.MaxDeviationRatio = worstRatio;
            result.P10U = totals.P10;
            result.P50U = totals.P50;
            result.P90U = totals.P90;
            result.MedianU = totals.Median;
        }

        private static Bug1GateRendererStats BuildStats(SkinnedMeshRenderer renderer, Transform third)
        {
            var stats = new Bug1GateRendererStats
            {
                Path = PathOf(renderer.transform, third),
                MeshName = renderer.sharedMesh != null ? renderer.sharedMesh.name : string.Empty,
                BoneCount = renderer.bones != null ? renderer.bones.Length : 0,
                RootBonePath = renderer.rootBone != null ? PathOf(renderer.rootBone, third) : string.Empty,
            };
            return stats;
        }

        private static List<Bug1GateRendererProfile> BuildProfiles(List<SkinnedMeshRenderer> renderers, Transform third)
        {
            var profiles = new List<Bug1GateRendererProfile>(renderers.Count);
            for (int i = 0; i < renderers.Count; i++)
            {
                SkinnedMeshRenderer renderer = renderers[i];
                var profile = new Bug1GateRendererProfile
                {
                    Path = PathOf(renderer.transform, third),
                    MeshName = renderer.sharedMesh != null ? renderer.sharedMesh.name : string.Empty,
                    BoneCount = renderer.bones != null ? renderer.bones.Length : 0,
                    RootBonePath = renderer.rootBone != null ? PathOf(renderer.rootBone, third) : string.Empty,
                };
                Material[] materials = renderer.sharedMaterials;
                if (materials != null)
                {
                    for (int m = 0; m < materials.Length; m++)
                    {
                        profile.MaterialNames.Add(materials[m] != null ? materials[m].name : "<null>");
                    }
                }
                Collider[] colliders = renderer.GetComponents<Collider>();
                for (int c = 0; c < colliders.Length; c++)
                {
                    if (ReferenceEquals(colliders[c], null)) continue;
                    profile.ColliderTypes.Add(colliders[c].GetType().Name
                        + (colliders[c].enabled ? ":on" : ":off"));
                }
                profiles.Add(profile);
            }
            return profiles;
        }

        /// <summary>T86-F3：只有 enabled 且 activeInHierarchy 的 renderer 才算可见表面；排除项写审计。</summary>
        private static List<SkinnedMeshRenderer> CollectVisibleRenderers(List<Transform> modelNodes, Bug1GateResult result)
        {
            var visible = new List<SkinnedMeshRenderer>();
            for (int n = 0; n < modelNodes.Count; n++)
            {
                SkinnedMeshRenderer[] found = modelNodes[n].GetComponentsInChildren<SkinnedMeshRenderer>(true);
                for (int i = 0; i < found.Length; i++)
                {
                    SkinnedMeshRenderer renderer = found[i];
                    string path = PathOf(renderer.transform, modelNodes[n].parent);
                    if (ReferenceEquals(renderer, null))
                    {
                        result.ExcludedRenderers.Add(new Bug1GateRendererAudit { Path = path, Reason = "<null-renderer>" });
                        continue;
                    }
                    if (renderer.sharedMesh == null || renderer.sharedMesh.vertexCount <= 0)
                    {
                        result.ExcludedRenderers.Add(new Bug1GateRendererAudit { Path = path, Reason = "empty-mesh" });
                        continue;
                    }
                    if (!renderer.enabled)
                    {
                        result.ExcludedRenderers.Add(new Bug1GateRendererAudit { Path = path, Reason = "renderer-disabled" });
                        continue;
                    }
                    if (!renderer.gameObject.activeInHierarchy)
                    {
                        result.ExcludedRenderers.Add(new Bug1GateRendererAudit { Path = path, Reason = "gameobject-inactive" });
                        continue;
                    }
                    visible.Add(renderer);
                }
            }
            return visible;
        }

        private static List<string> CollectComponentTypeNames(List<Transform> modelNodes)
        {
            var names = new List<string>();
            for (int n = 0; n < modelNodes.Count; n++)
            {
                Component[] components = modelNodes[n].GetComponentsInChildren<Component>(true);
                for (int i = 0; i < components.Length; i++)
                {
                    Component component = components[i];
                    if (ReferenceEquals(component, null))
                    {
                        names.Add("<null-component>");
                        continue;
                    }
                    try
                    {
                        names.Add(component.GetType().Name);
                    }
                    catch (Exception ex)
                    {
                        names.Add("<read-failed:" + ex.GetType().Name + ">");
                    }
                }
            }
            return names;
        }

        private static List<Vector3> BakeSample(SkinnedMeshRenderer renderer, int maxSamples)
        {
            try
            {
                Mesh mesh = ResolveBakeMesh();
                if (ReferenceEquals(mesh, null)) return null;
                if (!InvokeBakeMesh(renderer, mesh)) return null;
                Vector3[] vertices = mesh.vertices;
                if (vertices == null || vertices.Length == 0) return null;
                List<int> indices = Bug1GateProbeLogic.SelectSampleIndices(vertices.Length, maxSamples);
                var samples = new List<Vector3>(indices.Count);
                Matrix4x4 matrix = renderer.transform.localToWorldMatrix;
                for (int i = 0; i < indices.Count; i++)
                {
                    samples.Add(matrix.MultiplyPoint3x4(vertices[indices[i]]));
                }
                return samples;
            }
            catch
            {
                return null;
            }
        }

        private static bool InvokeBakeMesh(SkinnedMeshRenderer renderer, Mesh mesh)
        {
            try
            {
                if (_bakeMesh == null)
                {
                    _bakeMesh = typeof(SkinnedMeshRenderer).GetMethod("BakeMesh", new[] { typeof(Mesh), typeof(bool) })
                        ?? typeof(SkinnedMeshRenderer).GetMethod("BakeMesh", new[] { typeof(Mesh) });
                }
                if (_bakeMesh == null) return false;
                object[] args = _bakeMesh.GetParameters().Length == 2
                    ? new object[] { mesh, false }
                    : new object[] { mesh };
                _bakeMesh.Invoke(renderer, args);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static Mesh ResolveBakeMesh()
        {
            try
            {
                return new Mesh();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// t95 / t94 §3.1：姿态快照。集合 K = Skeleton 全子树（含 inactive）∪ 参与 renderer 的
        /// bones[]/rootBone 实际引用 ∪ 玩家 Collider 挂载 Transform 到 root 的父链。
        /// 每节点读 localPosition/localRotation/localScale（10 个 float）与 localToWorldMatrix（16 个 float），
        /// 由 Logic 做 float32 逐位比较（无哈希、无 epsilon）。不可读/缺失/非有限 → Available=false。
        /// 覆盖范围声明：只覆盖节点本地 TRS 与世界矩阵，不含 bindpose/skinMatrix 与 mesh 顶点。
        /// </summary>
        private static Bug1PoseSnapshot SnapshotPose(Transform skeleton, Player target, List<SkinnedMeshRenderer> renderers)
        {
            var snapshot = new Bug1PoseSnapshot();
            Transform root = null;
            try { root = target != null ? target.transform : null; } catch { root = null; }
            var map = new Dictionary<string, Transform>(StringComparer.Ordinal);
            var skeletonPaths = new List<string>();
            var bonePaths = new List<string>();
            var colliderPaths = new List<string>();
            var skeletonWalk = new Bug1PoseWalkState();
            if (!ReferenceEquals(skeleton, null))
                CollectSubtree(skeleton, root, skeletonPaths, map, 0, skeletonWalk);
            // R19-01：截断集不能参与 PosesEqual——可用性只由 ApplyPoseAvailability 判定并写入；
            // 这里不重复拼条件，调用点与判定点合一，避免「调了却丢弃/反转」的假绿。
            if (!ApplyPoseAvailability(snapshot, skeletonWalk)) return snapshot;
            if (renderers != null)
            {
                for (int r = 0; r < renderers.Count; r++)
                {
                    SkinnedMeshRenderer renderer = renderers[r];
                    if (ReferenceEquals(renderer, null)) continue;
                    Transform rootBone = null;
                    Transform[] bones = null;
                    try
                    {
                        rootBone = renderer.rootBone;
                        bones = renderer.bones;
                    }
                    catch
                    {
                        continue;
                    }
                    AddPath(rootBone, root, bonePaths, map);
                    if (bones != null)
                    {
                        for (int b = 0; b < bones.Length; b++) AddPath(bones[b], root, bonePaths, map);
                    }
                }
            }
            if (!ReferenceEquals(root, null))
            {
                Collider[] colliders = null;
                try { colliders = target.GetComponentsInChildren<Collider>(true); } catch { colliders = null; }
                if (colliders != null)
                {
                    for (int c = 0; c < colliders.Length; c++)
                    {
                        if (ReferenceEquals(colliders[c], null)) continue;
                        AddColliderChain(colliders[c].transform, root, colliderPaths, map);
                    }
                }
            }
            List<Bug1PoseNodePlan> plan = Bug1GateProbeLogic.BuildPoseNodePlan(skeletonPaths, bonePaths, colliderPaths);
            for (int i = 0; i < plan.Count; i++)
            {
                Transform node;
                if (!map.TryGetValue(plan[i].Path, out node) || ReferenceEquals(node, null))
                {
                    snapshot.Available = false;
                    snapshot.UnavailableReason = "missing:" + plan[i].Path;
                    return snapshot;
                }
                Bug1PoseEntry entry = ReadPoseEntry(node, plan[i].Path, plan[i].Role);
                if (ReferenceEquals(entry, null))
                {
                    snapshot.Available = false;
                    snapshot.UnavailableReason = "unreadable:" + plan[i].Path;
                    return snapshot;
                }
                snapshot.Entries.Add(entry);
            }
            if (snapshot.Entries.Count == 0)
            {
                snapshot.Available = false;
                snapshot.UnavailableReason = "empty-node-set";
            }
            return snapshot;
        }

        /// <summary>R18-01：子树遍历状态——超限时置 Truncated，调用方必须 fail-closed。</summary>
        internal sealed class Bug1PoseWalkState
        {
            public bool Truncated;
            public int Collected;
        }

        /// <summary>
        /// R17-01 / R18-01 / R18-02：Skeleton 子树遍历必须有界——节点数上限 MaxPoseNodes、深度上限 MaxPoseDepth。
        /// 超限不再静默 return：置 state.Truncated 并记录已收集节点数，由 SnapshotPose 标成不可用。
        /// </summary>
        private static void CollectSubtree(
            Transform node, Transform root, List<string> paths, Dictionary<string, Transform> map)
        {
            CollectSubtree(node, root, paths, map, 0, null);
        }

        private static void CollectSubtree(
            Transform node, Transform root, List<string> paths, Dictionary<string, Transform> map,
            int depth, Bug1PoseWalkState state)
        {
            if (!ReferenceEquals(state, null) && state.Truncated) return;
            if (ReferenceEquals(node, null)) return;
            if (!PoseDepthAllowed(depth))
            {
                MarkPoseTruncated(paths, state);
                return;
            }
            if (!Bug1GateProbeLogic.PoseWalkAllowed(paths.Count, Bug1GateProbeLogic.MaxPoseNodes))
            {
                MarkPoseTruncated(paths, state);
                return;
            }
            AddPath(node, root, paths, map);
            int count = 0;
            try { count = node.childCount; } catch { return; }
            for (int i = 0; i < count; i++)
            {
                Transform child = null;
                try { child = node.GetChild(i); } catch { child = null; }
                if (!ReferenceEquals(child, null)) CollectSubtree(child, root, paths, map, depth + 1, state);
            }
        }

        private static void MarkPoseTruncated(List<string> paths, Bug1PoseWalkState state)
        {
            if (ReferenceEquals(state, null)) return;
            state.Truncated = true;
            state.Collected = paths == null ? 0 : paths.Count;
        }

        private static void AddPath(Transform node, Transform root, List<string> paths, Dictionary<string, Transform> map)
        {
            if (ReferenceEquals(node, null)) return;
            string path = PathOf(node, root);
            if (string.IsNullOrEmpty(path)) return;
            if (!map.ContainsKey(path)) map[path] = node;
            paths.Add(path);
        }

        /// <summary>Collider 所挂 Transform 到玩家 root 的父链，全部纳入姿态集合。</summary>
        private static void AddColliderChain(Transform node, Transform root, List<string> paths, Dictionary<string, Transform> map)
        {
            Transform current = node;
            int guard = 0;
            while (!ReferenceEquals(current, null) && guard++ < 64)
            {
                AddPath(current, root, paths, map);
                if (ReferenceEquals(current, root)) break;
                try { current = current.parent; } catch { current = null; }
            }
        }

        private static Bug1PoseEntry ReadPoseEntry(Transform node, string path, string role)
        {
            try
            {
                Vector3 localPosition = node.localPosition;
                Quaternion localRotation = node.localRotation;
                Vector3 localScale = node.localScale;
                Matrix4x4 world = node.localToWorldMatrix;
                if (!IsFinite(localPosition) || !IsFinite(localRotation) || !IsFinite(localScale)) return null;
                var entry = new Bug1PoseEntry { Path = path, Role = role, HasLocal = true, HasWorld = true };
                entry.Local[0] = localPosition.x;
                entry.Local[1] = localPosition.y;
                entry.Local[2] = localPosition.z;
                entry.Local[3] = localRotation.x;
                entry.Local[4] = localRotation.y;
                entry.Local[5] = localRotation.z;
                entry.Local[6] = localRotation.w;
                entry.Local[7] = localScale.x;
                entry.Local[8] = localScale.y;
                entry.Local[9] = localScale.z;
                float[] worldValues =
                {
                    world.m00, world.m01, world.m02, world.m03,
                    world.m10, world.m11, world.m12, world.m13,
                    world.m20, world.m21, world.m22, world.m23,
                    world.m30, world.m31, world.m32, world.m33,
                };
                for (int i = 0; i < Bug1GateProbeLogic.PoseWorldFieldCount; i++)
                {
                    if (!IsFinite(worldValues[i])) return null;
                    entry.World[i] = worldValues[i];
                }
                return entry;
            }
            catch
            {
                return null;
            }
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(Quaternion value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z) && IsFinite(value.w);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static Bug1GateTransformSnapshot SnapshotTransform(Transform transform)
        {
            var snapshot = new Bug1GateTransformSnapshot();
            if (ReferenceEquals(transform, null)) return snapshot;
            try
            {
                Vector3 position = transform.position;
                Quaternion rotation = transform.rotation;
                snapshot.Px = position.x;
                snapshot.Py = position.y;
                snapshot.Pz = position.z;
                snapshot.Qx = rotation.x;
                snapshot.Qy = rotation.y;
                snapshot.Qz = rotation.z;
                snapshot.Qw = rotation.w;
                snapshot.Available = true;
            }
            catch
            {
                snapshot.Available = false;
            }
            return snapshot;
        }

        /// <summary>T86-F4：读失败 → available=false（不是「空表等于没变」）。</summary>
        private static List<Bug1GateColliderSnapshot> SnapshotColliders(Player target, out bool available)
        {
            available = true;
            var snapshots = new List<Bug1GateColliderSnapshot>();
            try
            {
                Collider[] colliders = target.GetComponentsInChildren<Collider>(true);
                for (int i = 0; i < colliders.Length; i++)
                {
                    Collider collider = colliders[i];
                    if (ReferenceEquals(collider, null)) continue;
                    Bounds bounds = collider.bounds;
                    snapshots.Add(new Bug1GateColliderSnapshot
                    {
                        Name = PathOf(collider.transform, target.transform),
                        Type = collider.GetType().Name,
                        Enabled = collider.enabled,
                        Available = true,
                        CenterX = bounds.center.x,
                        CenterY = bounds.center.y,
                        CenterZ = bounds.center.z,
                        SizeX = bounds.size.x,
                        SizeY = bounds.size.y,
                        SizeZ = bounds.size.z,
                    });
                }
            }
            catch
            {
                available = false;
            }
            return snapshots;
        }

        private static Bug1GateRaycastSnapshot RaycastProbe(Transform third)
        {
            var snapshot = new Bug1GateRaycastSnapshot();
            if (ReferenceEquals(third, null)) return snapshot;
            try
            {
                Vector3 origin = third.position + Vector3.up * 1.0f + third.forward * 1.5f;
                RaycastHit hit;
                bool hitSomething = Physics.Raycast(origin, -third.forward, out hit, RaycastLength,
                    ~0, QueryTriggerInteraction.Ignore);
                snapshot.Available = true;
                snapshot.HitName = hitSomething && hit.collider != null
                    ? hit.collider.GetType().Name + "@" + PathOf(hit.collider.transform, third)
                    : "none";
                snapshot.Distance = hitSomething ? hit.distance : -1d;
            }
            catch
            {
                snapshot.Available = false;
            }
            return snapshot;
        }

        private static Bug1GateResult Fail(Bug1GateResult result, string reason, string detail)
        {
            result.Status = Bug1GateProbeLogic.StatusFail;
            result.Reason = reason;
            result.FailureDetail = detail ?? string.Empty;
            result.RuntimeMeasured = false;
            return result;
        }

        /// <summary>R16-06：日志与 JSON 用同一份最终状态；写盘失败就把状态改成 OUTPUT_FAILED 再打日志。</summary>
        private static void Emit(Bug1GateResult result)
        {
            string json = Bug1GateProbeLogic.BuildJson(result, _coverage);
            string path = null;
            try
            {
                string root = Path.Combine(BepInEx.Paths.BepInExRootPath, "diagnostics", "spf-bug1-gate", result.RunId);
                Directory.CreateDirectory(root);
                path = Path.Combine(root, "GATE.json");
                File.WriteAllText(path, json, new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                result.Status = Bug1GateProbeLogic.StatusFail;
                result.Reason = Bug1GateProbeLogic.ReasonOutputFailed;
                result.FailureDetail = ex.GetType().Name;
                json = Bug1GateProbeLogic.BuildJson(result, _coverage);
                RoleLogger.Error("[Shared]", Label + " GATE_RESULT=" + result.Status
                    + " reason=" + result.Reason + " detail=" + ex.GetType().Name + " json=" + json);
                return;
            }
            RoleLogger.Info("[Shared]", Label + " GATE_RESULT=" + result.Status
                + " reason=" + result.Reason
                + " samples=" + result.SampleCount.ToString(CultureInfo.InvariantCulture)
                + " renderers=" + result.RendererCount.ToString(CultureInfo.InvariantCulture)
                + " max_residual_u=" + result.MaxResidualU.ToString("R", CultureInfo.InvariantCulture)
                + " epsilon_u=" + result.EpsilonU.ToString("R", CultureInfo.InvariantCulture)
                + " profile_hash=" + result.ProfileHash
                + " json=" + path);
        }

        private static Transform SafeFind(Transform root, string name)
        {
            try { return root.Find(name); }
            catch { return null; }
        }

        private static bool UnityObjectAlive(UnityEngine.Object value)
        {
            try { return value != null; }
            catch { return false; }
        }

        private static string Describe(Player player)
        {
            try
            {
                if (!ReferenceEquals(player, null) && !ReferenceEquals(player.channel, null) && player.channel.owner != null)
                {
                    return player.channel.owner.playerID.playerName;
                }
            }
            catch { }
            return "player";
        }

        private static string SafeName(UnityEngine.Object value)
        {
            try { return value != null ? value.name : "<null>"; }
            catch { return "<unreadable>"; }
        }

        private static string PathOf(Transform node, Transform root)
        {
            if (ReferenceEquals(node, null)) return string.Empty;
            var parts = new List<string>(6);
            Transform current = node;
            int guard = 0;
            while (!ReferenceEquals(current, null) && guard++ < 64)
            {
                parts.Add(current.name);
                if (ReferenceEquals(current, root)) break;
                current = current.parent;
            }
            parts.Reverse();
            return string.Join("/", parts.ToArray());
        }
    }
}
