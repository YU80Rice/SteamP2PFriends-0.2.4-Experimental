using SDG.Unturned;
using SteamP2PFriends.Core.Patches;
using SteamP2PFriends.Shared;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEngine;

namespace SteamP2PFriends.Platform.Diagnostics.Patches
{
    internal struct Bug1ProbeConfig
    {
        public string RunId;
        public string StepId;
        public ulong TargetSteamId;
        public string OutputRoot;
        public double NativeSpeedCapUS;
        /// <summary>t48/F4：spec §3.4 的走档上限（SPEED_STAND 派生），与 sprint 档一起冻结。</summary>
        public double NativeSpeedCapWalkUS;
        /// <summary>t48/F4：跑档上限（SPEED_SPRINT 派生）。窗口实际用哪档见 SpeedTier。</summary>
        public double NativeSpeedCapSprintUS;
        /// <summary>t48/F4：本次窗口实际生效的档位，"walk" 或 "sprint"。</summary>
        public string SpeedTier;
        public bool SpeedCapKnown;
    }

    /// <summary>
    /// Bug1 只读测量运行时：默认关闭；开启后绑定一个 Host 侧真实远端 Guest，
    /// 在真实相位采样 A（movement 根）与 V（player.third 稳定载体），
    /// 结束时离线判定并落盘。仪表自身异常必须停机，不改变原生返回/队列/Transform。
    /// </summary>
    internal static class Bug1ProbeRuntime
    {
        private const string Label = "[SPF-Bug1Probe]";
        private const string RoleDir = "HOST";

        public static bool Enabled { get; private set; }
        public static Bug1ProbeConfig Config { get; private set; }
        public static bool Bound => _target != null && _movement != null && _third != null;
        public static string LastError { get; private set; } = string.Empty;
        public static int BufferedRecords => _traceLines.Count;

        private static Player _target;
        private static PlayerMovement _movement;
        private static Transform _third;
        private static Renderer _thirdRenderer;
        private static Camera _camera;
        private static Bug1StepWindow _window;
        private static readonly Dictionary<int, Bug1FrameSample> FrameByCount = new Dictionary<int, Bug1FrameSample>();
        private static readonly List<string> _traceLines = new List<string>();
        private static long _eventSeq;
        private static long _simCallSeq;
        private static long _sessionEpoch;
        private static long _connectionGeneration;
        private static string _simCallIdOpen = string.Empty;
        private static Bug1Vec3 _simBefore;
        private static bool _simOpen;

        /// <summary>t28：真实队列边界只在目标 PlayerInput.serversidePackets 上生效。</summary>
        private static PlayerInput _input;
        private static Queue<PlayerInputPacket> _serversidePackets;
        private static System.Reflection.FieldInfo _packetSeqField;
        private static System.Reflection.FieldInfo _dismissedField;
        /// <summary>t35：接收/消费相位状态机（rx_id 分配、有界 side table、拒绝/入队互斥）。</summary>
        private static readonly Bug1RxPhaseLedger _rx = new Bug1RxPhaseLedger();
        private static Bug1ConsumeRecord _simConsume;
        private static bool _simHasConsume;
        /// <summary>超上限丢弃的记录数：RUN_END 暴露，缓冲区溢出不得静默。</summary>
        private static long _traceDropped;
        /// <summary>Unity 帧号可读性：0 未探、1 可读、2 不可读（离线用例）。</summary>
        private static int _frameReadState;
        // t45：本 run 实际落下的 AFTER_ENQUEUE / AFTER_DEQUEUE 行数，供 RUN_END 硬判相位覆盖。
        private static int _rxEnqueueRecords;
        private static int _rxDequeueRecords;
        // t48：离线用例开关——不碰 Unity 相机（控制台进程没有 Camera.onPreCull 的真实订阅方）。
        private static bool __Offline;
        // t65：离线用例的采集面证据覆盖（仅测试用；不影响生产路径）。
        private static bool __CaptureOverrideActive;
        private static bool __CaptureTargetAlive;
        private static bool __CaptureThirdAlive;
        private static bool __CaptureRendererAlive;
        private static bool __CaptureHookFailClosed;
        private static string __CaptureLastHookError = string.Empty;
        // t48/R8-02：安装缝，返回 false 等价于 Bug1ProbePatch.Install() 装不上；null 表示走真实安装。
        private static Func<bool> __InstallSeam;

        public static void RegisterSessionReset(System.Action reset)
        {
            if (reset != null) WorldSyncDiagnosticCore.RegisterSessionResetCallback(reset);
        }

        /// <summary>开启前冻结 run/step/target/输出根/速度上限；任一非法则拒绝开启。</summary>
        public static bool Enable(Bug1ProbeConfig config)
        {
            if (Enabled) return true;
            if (string.IsNullOrEmpty(config.RunId) || string.IsNullOrEmpty(config.StepId))
            {
                LastError = "RUN_OR_STEP_MISSING";
                return false;
            }
            if (config.TargetSteamId == 0UL)
            {
                LastError = "TARGET_STEAM_ID_MISSING";
                return false;
            }
            if (!config.SpeedCapKnown || !(config.NativeSpeedCapUS > 0d))
            {
                LastError = "NATIVE_SPEED_CAP_UNRESOLVED";
                return false;
            }
            Config = config;
            _window = new Bug1StepWindow
            {
                StepId = config.StepId,
                NativeSpeedCapUS = config.NativeSpeedCapUS,
                SpeedCapKnown = true,
                RxEnqueueRecords = 0,
                RxDequeueRecords = 0,
            };
            _traceLines.Clear();
            FrameByCount.Clear();
            _eventSeq = 0;
            _simCallSeq = 0;
            _simOpen = false;
            _simCallIdOpen = string.Empty;
            _rx.Reset();
            _simHasConsume = false;
            _rxEnqueueRecords = 0;
            _rxDequeueRecords = 0;
            _traceDropped = 0;
            LastError = string.Empty;

            // t48/R8-02 fail-closed：先把 detour 真正装上，装成功才算开启。
            // 旧实现先置 Enabled=true、再 try{Install()}catch{} 且忽略返回值 → START_OK 但零 hook，
            // 同时 Enabled 留 true，让下一次开启被 ALREADY_RUNNING 永久卡死。
            bool installed;
            try
            {
                installed = __InstallSeam != null ? __InstallSeam() : Bug1ProbePatch.Install();
            }
            catch (Exception ex)
            {
                installed = false;
                LastError = "INSTALL_FAILED:" + ex.GetType().Name;
            }
            if (!installed)
            {
                if (string.IsNullOrEmpty(LastError)
                    || LastError.IndexOf("INSTALL_FAILED", StringComparison.Ordinal) != 0)
                    LastError = "INSTALL_FAILED:patch_install_returned_false";
                RoleLogger.Error("[Shared]", Label + " !!! " + LastError + "（已回滚，未进入采集）");
                RollbackEnable();
                return false;
            }

            Enabled = true;
            if (!__Offline) { BoundCamera(true); HookBoundaryEvents(); }
            Record("RUN_BEGIN", "RUN_BEGIN",
                DoubleJson("native_speed_cap_u_s", Config.NativeSpeedCapUS) + ","
                + DoubleJson("epsilon_u", Bug1ProbeConstants.EpsilonU) + ","
                + DoubleJson("native_speed_cap_walk_u_s", Config.NativeSpeedCapWalkUS) + ","
                + DoubleJson("native_speed_cap_sprint_u_s", Config.NativeSpeedCapSprintUS) + ","
                + "\"speed_tier\":" + Quote(Config.SpeedTier));
            return true;
        }

        /// <summary>
        /// t48/R8-02：开启失败必须完整回滚——卸 detour、退订相机、清窗口与全部绑定，
        /// 绝不留下 Enabled=true 或半装 hook。幂等。
        /// </summary>
        private static void RollbackEnable()
        {
            Enabled = false;
            try { Bug1ProbePatch.Uninstall(); }
            catch (Exception ex) { LastError = LastError + "|UNINSTALL_FAILED:" + ex.GetType().Name; }
            if (!__Offline)
            {
                try { BoundCamera(false); } catch { }
                try { UnhookBoundaryEvents(); } catch { }
            }
            Config = default(Bug1ProbeConfig);
            _target = null;
            _movement = null;
            _third = null;
            _thirdRenderer = null;
            _camera = null;
            _input = null;
            _serversidePackets = null;
            _packetSeqField = null;
            _dismissedField = null;
            _window = null;
            FrameByCount.Clear();
            _traceLines.Clear();
            _rx.Reset();
            _simHasConsume = false;
            _rxEnqueueRecords = 0;
            _rxDequeueRecords = 0;
            _traceDropped = 0;
        }

        static Bug1ProbeRuntime()
        {
            RegisterSessionReset(OnSessionReset);
        }

        /// <summary>t45：运行中把开关改回 false 时先标 CONFIG_CHANGED，再正常收尾，禁止新旧半段拼接。</summary>
        public static void MarkConfigChanged()
        {
            if (_window != null) _window.ConfigChanged = true;
        }

        public static void Disable(string reason)
        {
            if (!Enabled) return;
            try
            {
                EndStep(reason);
            }
            catch (Exception ex)
            {
                LastError = "END_STEP_FAILED:" + ex.GetType().Name;
            }
            Enabled = false;
            if (!__Offline) { BoundCamera(false); try { UnhookBoundaryEvents(); } catch { } }
            // t48/R8-04：关闭必须真正卸掉 7 条 detour（含 BCL Queue<PlayerInputPacket> 的 Enqueue/Dequeue），
            // 只置 Enabled=false 会让 hook 常驻到进程结束。Uninstall 幂等，重复关闭不会抛。
            try { Bug1ProbePatch.Uninstall(); }
            catch (Exception ex) { LastError = "UNINSTALL_FAILED:" + ex.GetType().Name; }
            _target = null;
            _movement = null;
            _third = null;
            _thirdRenderer = null;
            _camera = null;
            _input = null;
            _serversidePackets = null;
            _rx.Reset();
            _simHasConsume = false;
            _rxEnqueueRecords = 0;
            _rxDequeueRecords = 0;
            _window = null;
            FrameByCount.Clear();
            _traceLines.Clear();
        }

        /// <summary>绑定目标：必须命中唯一真实远端 Guest；取不到 third 稳定载体即 MODEL_ENDPOINT_UNRESOLVED。</summary>
        public static bool TryBindTarget()
        {
            if (!Enabled) return false;
            // t48/R8-01：目标选择走纯函数 + ReferenceEquals 快照。
            // SteamPlayerID 重载了 ==/!=（SteamPlayerID.cs:136-144），任一侧 null 都抛 NRE，
            // 所以这里禁止对 playerID 用 == / != 判空，一律 ReferenceEquals。
            List<SteamPlayer> clients = Provider.clients;
            Bug1TargetSelection selection = Bug1ProbeTargetSelector.SelectBySteamId(
                Bug1ProbeTargetSelector.SnapshotAll(clients), Config.TargetSteamId);
            if (selection.Status != Bug1TargetSelectionStatus.Selected)
            {
                LastError = selection.Status == Bug1TargetSelectionStatus.Ambiguous
                    ? "TARGET_AMBIGUOUS"
                    : "TARGET_NOT_FOUND";
                return false;
            }
            SteamPlayer matched = clients[selection.SelectedIndex];
            Player found = ReferenceEquals(matched, null) ? null : matched.player;
            if (ReferenceEquals(found, null))
            {
                LastError = "TARGET_NOT_FOUND";
                return false;
            }
            _target = found;
            _movement = found.movement;
            _third = found.third;
            BindServersideQueue(found);
            _thirdRenderer = _third != null ? _third.GetComponentInChildren<Renderer>(true) : null;
            if (_movement == null || _third == null || _thirdRenderer == null)
            {
                LastError = "MODEL_ENDPOINT_UNRESOLVED";
                _window.ModelEndpointUnresolved = true;
                return false;
            }
            if (!IsDescendantOf(_thirdRenderer.transform, _third))
            {
                LastError = "RENDERER_CHAIN_UNRESOLVED";
                _window.ModelEndpointUnresolved = true;
                return false;
            }
            _window.StepFirstFrame = Time.frameCount;
            return true;
        }

        /// <summary>
        /// t28：绑定目标 PlayerInput 的原生 serversidePackets 队列实例。
        /// 取不到只记 RX_QUEUE_UNRESOLVED（相位缺失会让判定 UNKNOWN），绝不猜一个队列。
        /// </summary>
        private static void BindServersideQueue(Player player)
        {
            _input = null;
            _serversidePackets = null;
            try
            {
                _input = player.input;
                if (_packetSeqField == null)
                {
                    _packetSeqField = typeof(PlayerInputPacket).GetField(
                        "clientSimulationFrameNumber",
                        System.Reflection.BindingFlags.Instance
                        | System.Reflection.BindingFlags.Public
                        | System.Reflection.BindingFlags.NonPublic);
                }
                var queueField = typeof(PlayerInput).GetField(
                    "serversidePackets",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                _serversidePackets = queueField != null && _input != null
                    ? queueField.GetValue(_input) as Queue<PlayerInputPacket>
                    : null;
            }
            catch (Exception ex)
            {
                LastError = "RX_QUEUE_BIND_FAILED:" + ex.GetType().Name;
            }
            if (_serversidePackets == null) LastError = "RX_QUEUE_UNRESOLVED";
        }

        private static bool IsDescendantOf(Transform child, Transform ancestor)
        {
            for (Transform t = child; t != null; t = t.parent)
                if (ReferenceEquals(t, ancestor)) return true;
            return false;
        }

        /// <summary>只在开启时订阅主相机回调；关闭时立即解除，避免任何额外采样。</summary>
        public static void BoundCamera(bool subscribe)
        {
            try
            {
                Camera.onPreCull -= OnCameraPreCull;
                if (subscribe && Enabled) Camera.onPreCull += OnCameraPreCull;
            }
            catch (Exception ex)
            {
                LastError = "CAMERA_HOOK_FAILED:" + ex.GetType().Name;
            }
        }

        private static Bug1FrameSample GetOrCreateFrame(int frameCount)
        {
            Bug1FrameSample frame;
            if (!FrameByCount.TryGetValue(frameCount, out frame))
            {
                frame = new Bug1FrameSample
                {
                    Frame = frameCount,
                    MonoTicks = System.Diagnostics.Stopwatch.GetTimestamp(),
                    MonoFrequency = System.Diagnostics.Stopwatch.Frequency,
                    CameraIsMain = true,
                };
                FrameByCount.Add(frameCount, frame);
                if (_window.Frames.Count == 0) _window.StepFirstFrame = frameCount;
                _window.StepLastFrame = frameCount;
                _window.Frames.Add(frame);
            }
            return frame;
        }

        private static Bug1Vec3 ToVec(Transform t)
        {
            Vector3 p = t.position;
            return new Bug1Vec3(p.x, p.y, p.z);
        }

        /// <summary>
        /// t60：窗口边界事件。目标离场或关卡切换必须立刻收口，否则相机继续建帧、
        /// 却再也采不到 BEFORE_UPDATE，整轮会被误判 UNKNOWN(MISSING_PHASE)。
        /// </summary>
        private static void HookBoundaryEvents()
        {
            try { Player.onPlayerDestroyed += OnPlayerDestroyedBoundary; }
            catch (Exception ex) { LastError = "BOUNDARY_HOOK_FAILED:player_destroyed:" + ex.GetType().Name; }
            try { Level.onLevelLoaded += OnLevelLoadedBoundary; }
            catch (Exception ex) { LastError = "BOUNDARY_HOOK_FAILED:level_loaded:" + ex.GetType().Name; }
        }

        private static void UnhookBoundaryEvents()
        {
            try { Player.onPlayerDestroyed -= OnPlayerDestroyedBoundary; } catch { }
            try { Level.onLevelLoaded -= OnLevelLoadedBoundary; } catch { }
        }

        private static void OnPlayerDestroyedBoundary(Player player)
        {
            try
            {
                if (!Enabled || _window == null) return;
                if (ReferenceEquals(player, null) || !ReferenceEquals(player, _target)) return;
                Disable("target_left");
            }
            catch (Exception ex) { LastError = "BOUNDARY_TARGET_LEFT_FAILED:" + ex.GetType().Name; }
        }

        private static void OnLevelLoadedBoundary(int level)
        {
            try
            {
                if (!Enabled || _window == null) return;
                Disable("level_changed");
            }
            catch (Exception ex) { LastError = "BOUNDARY_LEVEL_CHANGED_FAILED:" + ex.GetType().Name; }
        }

        /// <summary>§3：vec3 一律有限 float64 对象；数值用 R 格式，避免精度丢失。</summary>
        private static string VecJson(string name, Bug1Vec3 v)
        {
            return "\"" + name + "\":{\"x\":" + v.X.ToString("R", CultureInfo.InvariantCulture)
                + ",\"y\":" + v.Y.ToString("R", CultureInfo.InvariantCulture)
                + ",\"z\":" + v.Z.ToString("R", CultureInfo.InvariantCulture) + "}";
        }

        private static string DoubleJson(string name, double value)
        {
            return "\"" + name + "\":" + value.ToString("R", CultureInfo.InvariantCulture);
        }

        /// <summary>RX 入口前缀：只记队列深度，不读 NetPakReader、不改参数。</summary>
        public static void OnReceiveInputsBefore(PlayerInput input)
        {
            if (!Enabled || input == null || !ReferenceEquals(input, _input) || _window == null) return;
            try
            {
                _rx.BeginReceive(_serversidePackets != null ? _serversidePackets.Count : -1);
            }
            catch (Exception ex) { LastError = "RX_BEFORE_FAILED:" + ex.GetType().Name; }
        }

        /// <summary>
        /// RX 入口后缀：本调用没有发生真实 Enqueue 才记 RX_REJECT，因此与 AFTER_ENQUEUE 构造互斥。
        /// 拒绝原因只在原生可直接判定的情况下给出，其余写 null 并注明不可得。
        /// </summary>
        public static void OnReceiveInputsAfter(PlayerInput input)
        {
            if (!Enabled || input == null || !ReferenceEquals(input, _input) || _window == null) return;
            try
            {
                string reason = IsDismissedRaw(input) ? "dismissed" : null;
                int depthAfter = _serversidePackets != null ? _serversidePackets.Count : -1;
                string extra = _rx.CompleteReceive(reason, depthAfter);
                if (extra == null) return;
                Record("RX", "RX_REJECT", extra);
            }
            catch (Exception ex) { LastError = "RX_REJECT_FAILED:" + ex.GetType().Name; }
        }

        /// <summary>
        /// RX/AFTER_ENQUEUE：只在真实 Queue.Enqueue 完成后记录（规格禁止用入口 Postfix 冒充入队成功）。
        /// depth 以队列自身 Count 为准；前后深度对不上时产出 invalid 的 ENQUEUE_GUARD_REJECTED，
        /// 计数并留痕，绝不静默丢弃，也不让同一次调用再落一条 RX_REJECT。
        /// </summary>
        public static void OnEnqueue(Queue<PlayerInputPacket> queue, PlayerInputPacket packet)
        {
            if (!Enabled || queue == null || packet == null || _window == null) return;
            if (_serversidePackets == null || !ReferenceEquals(queue, _serversidePackets)) return;
            try
            {
                int depthAfter = queue.Count;
                uint? seq = ReadInputSeq(packet);
                bool guardRejected;
                string extra = _rx.OnEnqueue(seq, PacketTypeOf(packet), _rx.ObservedDepthBefore, depthAfter,
                    out guardRejected);
                if (guardRejected)
                    Record("RX", "ENQUEUE_GUARD_REJECTED", extra,
                        new[] { "enqueue_depth_discontinuity" });
                else
                {
                    Record("RX", "AFTER_ENQUEUE", extra);
                    _rxEnqueueRecords++;
                }
            }
            catch (Exception ex) { LastError = "RX_ENQUEUE_FAILED:" + ex.GetType().Name; }
        }

        /// <summary>CONSUME/AFTER_DEQUEUE：取真实出队的那个包查 rx 表；查不到写 null + unavailable，不拿最近收到的顶替。</summary>
        public static void OnDequeue(Queue<PlayerInputPacket> queue, PlayerInputPacket packet)
        {
            if (!Enabled || queue == null || packet == null || _window == null) return;
            if (_serversidePackets == null || !ReferenceEquals(queue, _serversidePackets)) return;
            try
            {
                int depthAfter = queue.Count;
                uint? seq = ReadInputSeq(packet);
                string extra = _rx.OnDequeue(seq, PacketTypeOf(packet), depthAfter);
                Record("CONSUME", "AFTER_DEQUEUE", extra,
                    seq.HasValue ? null : new[] { "inputseq_unavailable" });
                _rxDequeueRecords++;
            }
            catch (Exception ex) { LastError = "RX_DEQUEUE_FAILED:" + ex.GetType().Name; }
        }

        /// <summary>
        /// t35：三个相位的字段构造已上移到 Bug1ProbeRxPhases（纯函数，可离线用例与变异验证）；
        /// 本类只负责从原生队列/包对象取真值并落 trace。
        /// </summary>
        private static uint? ReadInputSeq(PlayerInputPacket packet)
        {
            if (_packetSeqField == null) return null;
            try
            {
                object value = _packetSeqField.GetValue(packet);
                return value is uint seq ? seq : (uint?)null;
            }
            catch { return null; }
        }

        /// <summary>包类型按运行时类型名判定，避免绑定 SDK 内部可见性细节；不识别就是 Unknown。</summary>
        private static Bug1PacketType PacketTypeOf(PlayerInputPacket packet)
        {
            string name = packet.GetType().Name;
            if (name == "WalkingPlayerInputPacket") return Bug1PacketType.Walk;
            if (name == "DrivingPlayerInputPacket") return Bug1PacketType.Drive;
            return Bug1PacketType.Unknown;
        }

        /// <summary>isDismissed 是原生私有字段；只在能读到且为 true 时给原因，读不到就写 unavailable。</summary>
        private static bool IsDismissedRaw(PlayerInput input)
        {
            try
            {
                if (_dismissedField == null)
                {
                    _dismissedField = typeof(PlayerInput).GetField(
                        "isDismissed", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                }
                object value = _dismissedField != null ? _dismissedField.GetValue(input) : null;
                return value is bool dismissed && dismissed;
            }
            catch { return false; }
        }



        /// <summary>FRAME/BEFORE_UPDATE：只读快照，不压制 Update。</summary>
        public static void OnBeforeUpdate(PlayerMovement movement)
        {
            if (!Enabled || _window == null) return;
            if (!Bound)
            {
                // t45：正式入口刚开启时目标可能还没进视野/third 还没建好；在这里低频重试绑定，
                // 直到稳定载体齐备为止（绑定成功前不采样、不写帧记录）。
                try { TryBindTarget(); }
                catch (Exception ex) { LastError = "BIND_FAILED:" + ex.GetType().Name; }
                if (!Bound) return;
            }
            if (movement == null || !ReferenceEquals(movement, _movement)) return;
            try
            {
                Bug1FrameSample frame = GetOrCreateFrame(Time.frameCount);
                frame.HasBeforeUpdate = true;
                frame.ABeforeUpdate = ToVec(_movement.transform);
                frame.AYawBeforeUpdate = _movement.transform.eulerAngles.y;
                frame.FixedTickId = Time.frameCount;
                Record("FRAME", "BEFORE_UPDATE",
                    VecJson("a_pos_before_update", frame.ABeforeUpdate) + ","
                    + DoubleJson("a_yaw_before_update_deg", frame.AYawBeforeUpdate));
            }
            catch (Exception ex) { LastError = "BEFORE_UPDATE_FAILED:" + ex.GetType().Name; }
        }

        /// <summary>SIM/BEFORE：真实 walking simulate 入口，只读根位置。</summary>
        public static void OnSimBefore(PlayerMovement movement)
        {
            if (!Enabled || movement == null || !ReferenceEquals(movement, _movement) || _window == null) return;
            try
            {
                _simOpen = true;
                _simCallIdOpen = "sim-" + (++_simCallSeq).ToString(CultureInfo.InvariantCulture);
                _simBefore = ToVec(_movement.transform);
                Bug1ConsumeRecord pending;
                _simHasConsume = _rx.TakePending(out pending);
                _simConsume = pending;
                Record("SIM", "BEFORE", "\"sim_call_id\":\"" + _simCallIdOpen + "\","
                    + VecJson("a_pos", _simBefore)
                    + (_simHasConsume
                        ? ",\"consume_rx_id\":" + (_simConsume.HasRxId ? "\"" + _simConsume.RxId.ToString(CultureInfo.InvariantCulture) + "\"" : "null")
                          + ",\"consume_inputseq\":" + (_simConsume.HasInputSeq ? _simConsume.InputSeq.ToString(CultureInfo.InvariantCulture) : "null")
                        : ",\"consume_rx_id\":null,\"consume_inputseq\":null"));
            }
            catch (Exception ex) { LastError = "SIM_BEFORE_FAILED:" + ex.GetType().Name; }
        }

        /// <summary>SIM/AFTER：正常返回后配对；异常路径不会走到这里，不伪造 AFTER。</summary>
        public static void OnSimAfter(PlayerMovement movement)
        {
            if (!Enabled || movement == null || !ReferenceEquals(movement, _movement) || _window == null) return;
            try
            {
                if (!_simOpen) return;
                var sim = new Bug1SimSample
                {
                    SimCallId = _simCallIdOpen,
                    Frame = Time.frameCount,
                    HasBefore = true,
                    HasAfter = true,
                    ABefore = _simBefore,
                    AAfter = ToVec(_movement.transform),
                    HasInputSeq = _simHasConsume && _simConsume.HasInputSeq,
                    InputSeq = _simConsume.InputSeq,
                    HasRxId = _simHasConsume && _simConsume.HasRxId,
                    RxId = _simConsume.RxId,
                    HasQueueDepth = _simHasConsume,
                    QueueDepth = _simConsume.QueueDepthAfter,
                };
                _window.Sims.Add(sim);
                _simOpen = false;
                Record("SIM", "AFTER", "\"sim_call_id\":\"" + sim.SimCallId + "\","
                    + VecJson("a_pos", sim.AAfter) + ","
                    + "\"inputseq\":" + (sim.HasInputSeq ? sim.InputSeq.ToString(CultureInfo.InvariantCulture) : "null") + ","
                    + "\"rx_id\":" + (sim.HasRxId ? "\"" + sim.RxId.ToString(CultureInfo.InvariantCulture) + "\"" : "null") + ","
                    + "\"queue_depth\":" + (sim.HasQueueDepth ? sim.QueueDepth.ToString(CultureInfo.InvariantCulture) : "null")
                    + (sim.HasInputSeq && sim.HasRxId && sim.HasQueueDepth
                        ? string.Empty
                        : ",\"unavailable\":{\"consume_association\":\"no_matching_consume_record\"}"),
                    sim.HasInputSeq && sim.HasRxId && sim.HasQueueDepth ? null : new[] { "consume_association" });
            }
            catch (Exception ex) { LastError = "SIM_AFTER_FAILED:" + ex.GetType().Name; }
        }

        /// <summary>FRAME/RENDER_READY：主相机 cull/render 之前；相同 frameCount 只取主相机一次。</summary>
        private static void OnCameraPreCull(Camera cam)
        {
            if (!Enabled || _window == null || cam == null) return;
            try
            {
                Camera main = MainCamera.instance;
                if (main == null || !ReferenceEquals(cam, main)) return;
                _camera = cam;
                Bug1FrameSample frame = GetOrCreateFrame(Time.frameCount);
                if (frame.HasRenderReady) { frame.CameraDuplicate = true; frame.CameraIsMain = false; return; }
                frame.HasRenderReady = true;
                frame.CameraIsMain = true;
                frame.ThirdCarrierVerified = _third != null && _thirdRenderer != null;
                frame.RendererVerified = frame.ThirdCarrierVerified && IsDescendantOf(_thirdRenderer.transform, _third);
                frame.ModelEndpointResolved = frame.ThirdCarrierVerified && frame.RendererVerified;
                frame.APos = ToVec(_movement.transform);
                frame.AYaw = _movement.transform.eulerAngles.y;
                frame.VPos = ToVec(_third);
                frame.VYaw = _third.eulerAngles.y;
                frame.HasLastInputSeq = _rx.HasLast && _rx.Last.HasInputSeq;
                frame.LastInputSeq = _rx.Last.InputSeq;
                frame.HasLastRxId = _rx.HasLast && _rx.Last.HasRxId;
                frame.LastRxId = _rx.Last.RxId;
                frame.HasQueueDepth = _rx.HasLast;
                frame.QueueDepth = _rx.Last.QueueDepthAfter;
                // t60：失配必须可解释——给规则、原因码与候选 rx_id/inputseq，不再只写一个笼统串。
                string associationReason = Bug1InputAssociation.Classify(
                    _rx.HasLast, frame.HasLastInputSeq, frame.HasLastRxId, frame.HasQueueDepth);
                Bug1InterpolationSample interp = SampleInterpolation();
                Record("FRAME", "RENDER_READY",
                    VecJson("a_pos", frame.APos) + ","
                    + DoubleJson("a_yaw_deg", frame.AYaw) + ","
                    + VecJson("v_pos", frame.VPos) + ","
                    + DoubleJson("v_yaw_deg", frame.VYaw) + ","
                    + "\"camera_main\":" + (frame.CameraIsMain ? "true" : "false") + ","
                    + "\"camera_duplicate\":" + (frame.CameraDuplicate ? "true" : "false") + ","
                    + "\"third_carrier_verified\":" + (frame.ThirdCarrierVerified ? "true" : "false") + ","
                    + "\"renderer_chain_verified\":" + (frame.RendererVerified ? "true" : "false") + ","
                    + "\"model_endpoint_resolved\":" + (frame.ModelEndpointResolved ? "true" : "false") + ","
                    + "\"inputseq\":" + (frame.HasLastInputSeq ? frame.LastInputSeq.ToString(CultureInfo.InvariantCulture) : "null") + ","
                    + "\"rx_id\":" + (frame.HasLastRxId ? "\"" + frame.LastRxId.ToString(CultureInfo.InvariantCulture) + "\"" : "null") + ","
                    + "\"queue_depth\":" + (frame.HasQueueDepth ? frame.QueueDepth.ToString(CultureInfo.InvariantCulture) : "null")
                    + "," + Bug1InputAssociation.JsonFragment(
                        associationReason, frame.HasLastRxId, frame.LastRxId, frame.HasLastInputSeq, frame.LastInputSeq)
                    + "," + InterpolationJson(interp),
                    associationReason == null ? null : new[] { "input_association:" + associationReason });
            }
            catch (Exception ex) { LastError = "RENDER_READY_FAILED:" + ex.GetType().Name; }
        }

        // t60：远端插值观测（只读）。SDK 依据：PlayerInput.tick、PlayerInput.RATE、PlayerMovement.lastUpdatePos，
        // 对应 PlayerMovement.cs:1746 的 Lerp(lastUpdatePos, transform.position, (now - input.tick) / RATE)。
        private static System.Reflection.FieldInfo _inputRateField;
        private static System.Reflection.PropertyInfo _inputTickProperty;
        private static System.Reflection.FieldInfo _lastUpdatePosField;

        private static Bug1InterpolationSample SampleInterpolation()
        {
            bool hasTick = false, hasSampleTime = false, hasRate = false;
            bool hasLast = false, hasAuth = false, hasModel = false;
            double tick = 0d, sampleTime = 0d, rate = 0d;
            Bug1Vec3 lastPos = default(Bug1Vec3), authPos = default(Bug1Vec3), modelPos = default(Bug1Vec3);
            try
            {
                if (_inputTickProperty == null)
                    _inputTickProperty = typeof(PlayerInput).GetProperty("tick",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
                if (_inputTickProperty != null && _target != null && _target.input != null)
                {
                    object raw = _inputTickProperty.GetValue(_target.input, null);
                    if (raw is float) { tick = (float)raw; hasTick = true; }
                }
                hasSampleTime = TryReadRealtimeSinceStartup(out sampleTime);
                if (_inputRateField == null)
                    _inputRateField = typeof(PlayerInput).GetField("RATE",
                        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (_inputRateField != null)
                {
                    object rawRate = _inputRateField.GetValue(null);
                    if (rawRate is float) { rate = (float)rawRate; hasRate = true; }
                }
                if (_lastUpdatePosField == null)
                    _lastUpdatePosField = typeof(PlayerMovement).GetField("lastUpdatePos",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                if (_lastUpdatePosField != null && _movement != null)
                {
                    object rawPos = _lastUpdatePosField.GetValue(_movement);
                    if (rawPos is Vector3)
                    {
                        Vector3 v = (Vector3)rawPos;
                        lastPos = new Bug1Vec3(v.x, v.y, v.z);
                        hasLast = true;
                    }
                }
                if (_movement != null)
                {
                    Vector3 v = _movement.transform.position;
                    authPos = new Bug1Vec3(v.x, v.y, v.z);
                    hasAuth = true;
                }
                if (_target != null && _target.first != null)
                {
                    Vector3 v = _target.first.position;
                    modelPos = new Bug1Vec3(v.x, v.y, v.z);
                    hasModel = true;
                }
            }
            catch { }
            return Bug1InterpolationSample.Compute(hasTick, tick, hasSampleTime, sampleTime, hasRate, rate,
                hasLast, lastPos, hasAuth, authPos, hasModel, modelPos);
        }

        /// <summary>采样时刻：读不到就报 unavailable，绝不用 0 冒充。</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool TryReadRealtimeSinceStartup(out double value)
        {
            try { value = Time.realtimeSinceStartup; return true; }
            catch { value = 0d; return false; }
        }

        private static string InterpolationJson(Bug1InterpolationSample s)
        {
            var sb = new StringBuilder(320);
            sb.Append("\"remote_input_tick\":").Append(s.HasTick ? s.Tick.ToString("R", CultureInfo.InvariantCulture) : "null");
            sb.Append(",\"sample_time_s\":").Append(s.HasSampleTime ? s.SampleTime.ToString("R", CultureInfo.InvariantCulture) : "null");
            sb.Append(",\"input_rate\":").Append(s.HasRate ? s.Rate.ToString("R", CultureInfo.InvariantCulture) : "null");
            sb.Append(",\"lerp_factor\":").Append(s.HasLerp ? s.Lerp.ToString("R", CultureInfo.InvariantCulture) : "null");
            sb.Append(",\"lerp_factor_clamped\":").Append(s.HasLerp ? s.LerpClamped.ToString("R", CultureInfo.InvariantCulture) : "null");
            sb.Append(",\"last_update_pos\":").Append(s.HasLastUpdatePos ? VecJsonInline(s.LastUpdatePos) : "null");
            sb.Append(",\"authority_pos\":").Append(s.HasAuthorityPos ? VecJsonInline(s.AuthorityPos) : "null");
            sb.Append(",\"model_pos\":").Append(s.HasModelPos ? VecJsonInline(s.ModelPos) : "null");
            sb.Append(",\"interpolation_unavailable\":[");
            for (int i = 0; i < s.Unavailable.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('"').Append(s.Unavailable[i]).Append('"');
            }
            sb.Append(']');
            return sb.ToString();
        }

        private static string VecJsonInline(Bug1Vec3 v) =>
            "[" + v.X.ToString("R", CultureInfo.InvariantCulture)
            + "," + v.Y.ToString("R", CultureInfo.InvariantCulture)
            + "," + v.Z.ToString("R", CultureInfo.InvariantCulture) + "]";

        /// <summary>
        /// 统一记录：只写标量，不碰 Unity 对象；热路径不做字符串拼接以外的 I/O。
        /// valid 由调用方按"这条记录是否构成一次可信观测"计算，不再恒 true；
        /// invalidReasons 非空时同写 invalid_reasons，下游据此读成 UNKNOWN 而非样本。
        /// </summary>
        private static void Record(string kind, string phase, string extra, string[] invalidReasons = null)
        {
            if (_traceLines.Count >= Bug1ProbeConstants.MaxTraceRecords) { _traceDropped++; return; }
            bool valid = invalidReasons == null || invalidReasons.Length == 0;
            var sb = new StringBuilder(256);
            sb.Append('{');
            sb.Append("\"schema_version\":\"").Append(Bug1ProbeConstants.SchemaVersion).Append("\",");
            sb.Append("\"run_id\":\"").Append(Escape(Config.RunId)).Append("\",");
            sb.Append("\"step_id\":\"").Append(Escape(Config.StepId)).Append("\",");
            sb.Append("\"kind\":\"").Append(kind).Append("\",");
            sb.Append("\"phase\":\"").Append(phase).Append("\",");
            sb.Append("\"event_seq\":\"").Append((++_eventSeq).ToString(CultureInfo.InvariantCulture)).Append("\",");
            sb.Append("\"role\":\"HOST\",");
            sb.Append("\"target_steam_id\":\"").Append(Config.TargetSteamId.ToString(CultureInfo.InvariantCulture)).Append("\",");
            sb.Append("\"a_origin\":\"SERVER_AUTHORITY\",");
            sb.Append("\"frame_count\":").Append(ReadFrameCount().ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append("\"mono_ticks\":\"").Append(System.Diagnostics.Stopwatch.GetTimestamp().ToString(CultureInfo.InvariantCulture)).Append("\",");
            sb.Append("\"mono_frequency\":\"").Append(System.Diagnostics.Stopwatch.Frequency.ToString(CultureInfo.InvariantCulture)).Append("\",");
            sb.Append("\"epoch\":\"").Append(_sessionEpoch.ToString(CultureInfo.InvariantCulture)).Append("\",");
            sb.Append("\"connection_generation\":\"").Append(_connectionGeneration.ToString(CultureInfo.InvariantCulture)).Append("\",");
            sb.Append("\"valid\":").Append(valid ? "true" : "false");
            if (!valid)
                sb.Append(",\"invalid_reasons\":[").Append(QuoteArray(invalidReasons)).Append(']');
            if (!string.IsNullOrEmpty(extra)) sb.Append(',').Append(extra);
            sb.Append('}');
            _traceLines.Add(sb.ToString());
        }

        private static string QuoteArray(string[] values)
        {
            var sb = new StringBuilder(values.Length * 24);
            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('"').Append(Escape(values[i])).Append('"');
            }
            return sb.ToString();
        }

        /// <summary>
        /// Unity 帧号只读；离线用例进程没有 Unity icall，Mono 在 JIT 该调用点时就会抛
        /// SecurityException，因此必须隔离到独立方法才能在调用侧捕获。取不到返回 -1，
        /// 由记录如实标注不可得，绝不伪造帧号。
        /// </summary>
        private static long ReadFrameCount()
        {
            if (_frameReadState == 2) return -1L;
            try
            {
                long value = ReadFrameCountRaw();
                _frameReadState = 1;
                return value;
            }
            catch (Exception)
            {
                _frameReadState = 2;
                return -1L;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static long ReadFrameCountRaw() => Time.frameCount;

        private static string Escape(string value)
            => string.IsNullOrEmpty(value) ? string.Empty : value.Replace("\\", "\\\\").Replace("\"", "\\\"");

        /// <summary>t48：把字符串包成 JSON 字面量（null 当空串），供 RUN_BEGIN 的 speed_tier 使用。</summary>
        private static string Quote(string value) => "\"" + Escape(value ?? string.Empty) + "\"";

        /// <summary>RUN_END：先判定再落盘；仪表异常只记录故障，不向上抛改成原生命令返回。</summary>
        public static void EndStep(string reason)
        {
            if (_window == null) return;
            try
            {
                // t60：判定前先收口——只在「首末都相位齐全」的帧之间判定，两端半帧单独计数。
                // 旧实现把 session_reset 之后由相机继续创建、却因目标已销毁而采不到 BEFORE_UPDATE 的
                // 空白帧也算进窗内，导致整轮被误判 UNKNOWN(MISSING_PHASE)。
                Bug1WindowBoundaryContext boundaryCtx = BuildBoundaryContext(reason);
                Bug1WindowBounds bounds = Bug1ProbeWindowBoundary.Finalize(_window, boundaryCtx);
                _window.WindowHeadExcluded = bounds.HeadExcluded;
                _window.WindowTailExcluded = bounds.TailExcluded;
                _window.WindowInteriorPartial = bounds.InteriorPartial;
                _window.WindowExcludedReason = bounds.ExcludedReason;
                _window.WindowTailBoundaryKind = bounds.TailBoundaryKind;
                _window.WindowHeadBoundaryKind = bounds.HeadBoundaryKind;
                _window.WindowTailUnexplained = bounds.TailUnexplained;
                _window.WindowTailRenderOnly = bounds.TailRenderOnly;
                _window.WindowTailFirstFrame = bounds.TailFirstFrame;
                _window.WindowTailLastFrame = bounds.TailLastFrame;
                _window.CaptureState = CaptureStateJson(boundaryCtx, bounds);
                if (_window.Frames.Count > 0) _window.StepLastFrame = _window.Frames[_window.Frames.Count - 1].Frame;
                Record("CONTEXT", "CONTEXT",
                    "\"step_id\":\"" + Escape(Config.StepId) + "\","
                    + "\"fixed_conditions_valid\":" + (_window.FixedConditionsValid ? "true" : "false") + ","
                    + "\"pause_crossed\":" + (_window.PauseCrossed ? "true" : "false") + ","
                    + "\"config_changed\":" + (_window.ConfigChanged ? "true" : "false") + ","
                    + "\"group_changed\":" + (_window.GroupChanged ? "true" : "false") + ","
                    + "\"distance_out_of_band\":" + (_window.DistanceOutOfBand ? "true" : "false") + ","
                    + "\"culling_context_unresolved\":" + (_window.CullingContextUnresolved ? "true" : "false") + ","
                    + "\"right_censored\":" + (_window.RightCensored ? "true" : "false"));
                // t48/F1 修复：相位计数只有一套真值——静态累加器 _rxEnqueueRecords/_rxDequeueRecords。
                // 旧实现在 Enable 时把 window 计数置 0 后再也不同步，ValidatePhaseCoverage 永远看到 0，
                // 于是每局生产 run 都被硬判 UNKNOWN(PHASE_COVERAGE_MISSING)；而 RUN_END 又打印静态值（如 500），
                // 同一局产物自相矛盾。判读前同步，RUN_END 也用同步后的同一套数。
                if (_window.RxEnqueueRecords.HasValue) _window.RxEnqueueRecords = _rxEnqueueRecords;
                if (_window.RxDequeueRecords.HasValue) _window.RxDequeueRecords = _rxDequeueRecords;
                int rxAfterEnqueue = _window.RxEnqueueRecords ?? _rxEnqueueRecords;
                int rxAfterDequeue = _window.RxDequeueRecords ?? _rxDequeueRecords;
                var verdict = Bug1ProbeJudge.Judge(_window);
                Record("RUN_END", "RUN_END",
                    "\"end_reason\":\"" + Escape(reason) + "\",\"label\":\"" + verdict.Label + "\","
                    + "\"rx_guard_rejected\":" + _rx.GuardRejected.ToString(CultureInfo.InvariantCulture) + ","
                    + "\"rx_evicted\":" + _rx.EvictedRxEntries.ToString(CultureInfo.InvariantCulture) + ","
                    + "\"rx_superseded\":" + _rx.SupersededRxEntries.ToString(CultureInfo.InvariantCulture) + ","
                    + "\"trace_dropped\":" + _traceDropped.ToString(CultureInfo.InvariantCulture)
                    + ",\"rx_after_enqueue\":" + rxAfterEnqueue.ToString(CultureInfo.InvariantCulture)
                    + ",\"rx_after_dequeue\":" + rxAfterDequeue.ToString(CultureInfo.InvariantCulture)
                    + ",\"speed_tier\":" + Quote(Config.SpeedTier)
                    + ",\"window_head_excluded\":" + verdict.WindowHeadExcluded.ToString(CultureInfo.InvariantCulture)
                    + ",\"window_tail_excluded\":" + verdict.WindowTailExcluded.ToString(CultureInfo.InvariantCulture)
                    + ",\"window_interior_partial\":" + verdict.WindowInteriorPartial.ToString(CultureInfo.InvariantCulture)
                    + ",\"phase_missing_genuine\":" + verdict.PhaseMissingGenuine.ToString(CultureInfo.InvariantCulture)
                    + ",\"phase_missing_out_of_window\":" + verdict.PhaseMissingOutOfWindow.ToString(CultureInfo.InvariantCulture)
                    + ",\"window_excluded_reason\":" + Quote(verdict.WindowExcludedReason)
                    + ",\"window_tail_boundary\":" + Quote(verdict.WindowTailBoundaryKind)
                    + ",\"window_head_boundary\":" + Quote(verdict.WindowHeadBoundaryKind)
                    + ",\"window_tail_unexplained\":" + (verdict.WindowTailUnexplained ? "true" : "false")
                    + ",\"window_tail_render_only\":" + verdict.WindowTailRenderOnly.ToString(CultureInfo.InvariantCulture)
                    + ",\"window_tail_first_frame\":" + verdict.WindowTailFirstFrame.ToString(CultureInfo.InvariantCulture)
                    + ",\"window_tail_last_frame\":" + verdict.WindowTailLastFrame.ToString(CultureInfo.InvariantCulture)
                    + ",\"capture_state\":" + (string.IsNullOrEmpty(verdict.CaptureState) ? "null" : verdict.CaptureState)
                    // T71/M1-XZ-RELATIVE-v2：M1 只由同相位相对落后+回补收敛决定；这里导出新口径字段，
                    // 不再写无轴含义的 root_span，避免审计者把三维跨度当成 M1 证据。
                    + ",\"m1_rule_revision\":" + Quote(verdict.M1RuleRevision)
                    + ",\"motion_axes\":" + Quote(verdict.MotionAxes)
                    + ",\"horizontal_motion_min_u\":" + verdict.HorizontalMotionMinU.ToString("R", CultureInfo.InvariantCulture)
                    + ",\"render_root_span_xz_u\":" + verdict.RenderRootSpanXZ.ToString("R", CultureInfo.InvariantCulture)
                    + ",\"sim_root_span_3d_u\":" + verdict.SimRootSpan3D.ToString("R", CultureInfo.InvariantCulture)
                    + ",\"lag_witness_frame\":" + verdict.LagWitnessFrame.ToString(CultureInfo.InvariantCulture)
                    + ",\"lag_witness_authority\":" + VecJsonInline(verdict.LagWitnessAuthority)
                    + ",\"lag_witness_carrier\":" + VecJsonInline(verdict.LagWitnessCarrier)
                    + ",\"lag_before_release_u\":" + verdict.LagBeforeReleaseU.ToString("R", CultureInfo.InvariantCulture)
                    + ",\"lag_after_release_u\":" + verdict.LagAfterReleaseU.ToString("R", CultureInfo.InvariantCulture)
                    + ",\"strict_m1_events\":" + verdict.StrictM1Events.ToString(CultureInfo.InvariantCulture)
                    + ",\"horizontal_stall_segments\":" + verdict.HorizontalStallSegments.ToString(CultureInfo.InvariantCulture)
                    + ",\"horizontal_candidates\":" + verdict.HorizontalCandidates.ToString(CultureInfo.InvariantCulture)
                    + ",\"auth_segments\":" + verdict.AuthSegments.ToString(CultureInfo.InvariantCulture)
                    + ",\"non_m1_segments\":" + verdict.NonM1Segments.ToString(CultureInfo.InvariantCulture)
                    + ",\"candidate_rejections\":[" + QuoteList(verdict.CandidateRejections) + "]"
                    // T79/CLOSED_EVENT_PRIORITY-v1：右截断只局部生效，事件计数是下界；
                    // 这两组布尔与逐段范围必须与 result.json / manifest 同源。
                    + ",\"event_aggregation_revision\":" + Quote(verdict.EventAggregationRevision)
                    + ",\"selected_event_complete\":" + (verdict.SelectedEventComplete ? "true" : "false")
                    + ",\"event_scan_complete\":" + (verdict.EventScanComplete ? "true" : "false")
                    + ",\"strict_m1_events_is_lower_bound\":" + (verdict.StrictM1EventsIsLowerBound ? "true" : "false")
                    + ",\"right_censored_segments\":" + verdict.RightCensoredSegments.ToString(CultureInfo.InvariantCulture)
                    + ",\"right_censored_ranges\":" + RightCensoredRangesJson(verdict.RightCensoredRanges)
                    + ",\"closed_events\":" + ClosedEventsJson(verdict.ClosedEvents)
                    + ",\"missing_fields\":[" + QuoteList(verdict.MissingFields) + "]");
                string dir = (__Offline && string.IsNullOrEmpty(Config.OutputRoot))
                    ? "(offline)"
                    : WriteArtifacts(verdict, reason);
                RoleLogger.Info("[Shared]",
                    Label + " RUN_END step=" + Config.StepId + " label=" + verdict.Label
                    + " n_expected=" + verdict.NExpected + " observed=" + verdict.NObserved
                    + " missing=" + verdict.MissingFields.Count + " dir=" + dir);
            }
            catch (Exception ex)
            {
                LastError = "END_STEP_FAILED:" + ex.GetType().Name;
                RoleLogger.Error("[Shared]", Label + " END_STEP_FAILED " + ex);
            }
            finally
            {
                _window = null;
                FrameByCount.Clear();
                _traceLines.Clear();
            }
        }

        /// <summary>
        /// t65：收口时刻的采集面证据。边界是否合法不能只看 reason——还要确认 detour 没被 fail-closed、
        /// 目标确实已销毁（target_left）。任一读取失败按「不健康」处理（fail-closed），宁可判 UNKNOWN。
        /// </summary>
        private static Bug1WindowBoundaryContext BuildBoundaryContext(string reason)
        {
            var ctx = new Bug1WindowBoundaryContext { EndReason = reason ?? string.Empty };
            if (__CaptureOverrideActive)
            {
                ctx.TargetAlive = __CaptureTargetAlive;
                ctx.ThirdAlive = __CaptureThirdAlive;
                ctx.RendererAlive = __CaptureRendererAlive;
                ctx.HookFailClosed = __CaptureHookFailClosed;
                ctx.LastHookError = __CaptureLastHookError ?? string.Empty;
                return ctx;
            }
            try { ctx.TargetAlive = UnityAlive(_target); }
            catch { ctx.TargetAlive = false; }
            try { ctx.ThirdAlive = UnityAlive(_third); }
            catch { ctx.ThirdAlive = false; }
            try { ctx.RendererAlive = UnityAlive(_thirdRenderer); }
            catch { ctx.RendererAlive = false; }
            try { ctx.HookFailClosed = Bug1ProbePatch.FailClosed; }
            catch { ctx.HookFailClosed = true; }
            try
            {
                Exception last = Bug1ProbePatch.LastFailure;
                ctx.LastHookError = ReferenceEquals(last, null) ? string.Empty : last.GetType().Name;
            }
            catch { ctx.LastHookError = "unavailable"; }
            return ctx;
        }

        /// <summary>
        /// t65：离线控制台里 UnityEngine 的 ECall 会在 JIT 期就抛 SecurityException（不是运行时 catch 得住的），
        /// 所以 Unity 对象存活判定必须单独成方法，只有真实 Unity 进程才会 JIT 到它。
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool UnityAlive(UnityEngine.Component obj)
        {
            if (ReferenceEquals(obj, null)) return false;
            try { return !ReferenceEquals(obj.gameObject, null); }
            catch { return false; }
        }

        /// <summary>t65：capture_state 五要素 + 首帧号，原样进 RUN_END 与 result.json。</summary>
        private static string CaptureStateJson(Bug1WindowBoundaryContext ctx, Bug1WindowBounds bounds)
        {
            var sb = new StringBuilder(220);
            sb.Append("{\"end_reason\":\"").Append(Escape(ctx.EndReason)).Append('"');
            sb.Append(",\"target_alive\":").Append(ctx.TargetAlive ? "true" : "false");
            sb.Append(",\"third_alive\":").Append(ctx.ThirdAlive ? "true" : "false");
            sb.Append(",\"renderer_alive\":").Append(ctx.RendererAlive ? "true" : "false");
            sb.Append(",\"hook_fail_closed\":").Append(ctx.HookFailClosed ? "true" : "false");
            sb.Append(",\"last_hook_error\":");
            sb.Append(string.IsNullOrEmpty(ctx.LastHookError) ? "null" : "\"" + Escape(ctx.LastHookError) + "\"");
            sb.Append(",\"first_frame\":").Append(bounds.FirstFrame.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"tail_boundary\":\"").Append(bounds.TailBoundaryKind).Append('"');
            sb.Append('}');
            return sb.ToString();
        }

        // ---- 测试缝（internal，仅测试程序集可见）----
        // 只赋既有引用与窗口，不构造 Unity 对象、不装 Harmony hook、不写盘；
        // 目的是让用例穿过 Runtime.OnEnqueue/OnDequeue 这两条生产入口，而不是绕过它们直调 ledger。

        internal static void __TestEnable(PlayerInput input, Queue<PlayerInputPacket> queue,
            System.Reflection.FieldInfo seqField, Bug1StepWindow window, Bug1ProbeConfig config)
        {
            Config = config;
            _input = input;
            _serversidePackets = queue;
            _packetSeqField = seqField;
            _window = window;
            _rx.Reset();
            _simHasConsume = false;
            _rxEnqueueRecords = 0;
            _rxDequeueRecords = 0;
            _traceDropped = 0;
            _eventSeq = 0;
            _frameReadState = 0;
            _traceLines.Clear();
            Enabled = true;
        }

        internal static string[] __TestTrace() => _traceLines.ToArray();

        /// <summary>t48：离线用例走真实 Enable/EndStep，但不订阅 Unity 相机（控制台进程里无真实相机回调）。</summary>
        internal static void __TestOffline(bool offline) { __Offline = offline; }

        /// <summary>t65：离线用例直接给定采集面证据（避免构造 Unity Player/Renderer）。</summary>
        internal static void __TestSetCaptureState(bool targetAlive, bool thirdAlive, bool rendererAlive,
            bool hookFailClosed, string lastHookError)
        {
            __CaptureOverrideActive = true;
            __CaptureTargetAlive = targetAlive;
            __CaptureThirdAlive = thirdAlive;
            __CaptureRendererAlive = rendererAlive;
            __CaptureHookFailClosed = hookFailClosed;
            __CaptureLastHookError = lastHookError ?? string.Empty;
        }

        internal static void __TestClearCaptureState()
        {
            __CaptureOverrideActive = false;
            __CaptureLastHookError = string.Empty;
        }

        /// <summary>t65：把窗口目标指向一个离线构造的 Player 实例，用于行为级边界用例。</summary>
        internal static void __TestBindTarget(Player target) { _target = target; }

        /// <summary>t48/R8-02：注入安装缝；传 null 恢复真实 Bug1ProbePatch.Install()。</summary>
        internal static void __TestInstallSeam(Func<bool> seam) { __InstallSeam = seam; }

        /// <summary>t48：把真实 Queue 与原生序号字段接到运行时（等价 TryBindTarget 里的 BindServersideQueue）。</summary>
        internal static void __TestBindQueue(Queue<PlayerInputPacket> queue, System.Reflection.FieldInfo seqField)
        {
            _serversidePackets = queue;
            _packetSeqField = seqField;
        }

        /// <summary>
        /// t48/F1：向当前窗口注入 count 个合成帧（帧相位齐全、位置匀速微动）。
        /// 用于验证「Enable → 真实 OnEnqueue/OnDequeue → EndStep」整条生产路径的相位覆盖，
        /// 只补帧数据，不触碰 _rxEnqueueRecords/_rxDequeueRecords 与 _rx 台账。
        /// </summary>
        internal static void __TestAddSmoothFrames(int count)
        {
            if (_window == null) return;
            _window.FixedConditionsValid = true;
            const long frequency = 10000000L;
            int start = _window.Frames.Count;
            for (int i = 0; i < count; i++)
            {
                int frame = start + i;
                double x = 0.02 * frame;
                _window.Frames.Add(new Bug1FrameSample
                {
                    Frame = frame,
                    FixedTickId = frame,
                    MonoTicks = (long)Math.Round(frame * (double)frequency / 75.0),
                    MonoFrequency = frequency,
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
                    LastInputSeq = (uint)(frame + 1),
                    HasLastRxId = true,
                    LastRxId = (ulong)(frame + 1),
                    HasQueueDepth = true,
                    QueueDepth = 1,
                });
            }
            if (_window.Frames.Count > 0)
            {
                _window.StepFirstFrame = _window.Frames[0].Frame;
                _window.StepLastFrame = _window.Frames[_window.Frames.Count - 1].Frame;
            }
        }

        /// <summary>
        /// t60：向当前窗口注入 count 个「只有 render、没有 update」的尾巴帧，
        /// 复现目标离场后相机仍建帧的真实形态；用于验证 EndStep 会在判定前把它们收口掉。
        /// </summary>
        internal static void __TestAddPartialTailFrames(int count)
        {
            if (_window == null) return;
            const long frequency = 10000000L;
            int start = _window.Frames.Count;
            for (int i = 0; i < count; i++)
            {
                int frame = start + i;
                _window.Frames.Add(new Bug1FrameSample
                {
                    Frame = frame,
                    MonoTicks = (long)Math.Round(frame * (double)frequency / 75.0),
                    MonoFrequency = frequency,
                    HasBeforeUpdate = false,
                    HasRenderReady = true,
                    CameraIsMain = true,
                    ThirdCarrierVerified = false,
                    RendererVerified = false,
                    ModelEndpointResolved = false,
                    APos = new Bug1Vec3(0, 0, 0),
                    VPos = new Bug1Vec3(0, 0, 0),
                });
            }
            _window.StepLastFrame = _window.Frames[_window.Frames.Count - 1].Frame;
        }

        internal static void __TestDisable()
        {
            Enabled = false;
            _input = null;
            _serversidePackets = null;
            _packetSeqField = null;
            _dismissedField = null;
            _window = null;
            _rx.Reset();
            _simHasConsume = false;
            _traceLines.Clear();
            _traceDropped = 0;
        }

        /// <summary>
        /// t28：manifest 的运行时真实身份（game_mvid / SPF 程序集 SHA256 / 版本）。
        /// 取不到就写 null + 具体 unavailable 原因，绝不留 UNRESOLVED 之类占位符。
        /// </summary>
        internal static string BuildEnvironmentManifestFields()
        {
            var sb = new StringBuilder(320);
            string mvidReason;
            string mvid = ResolveGameMvid(out mvidReason);
            sb.Append("\"game_mvid\":").Append(mvid == null ? "null" : "\"" + mvid + "\"");
            string hashReason;
            string hash = ResolveSpfSha256(out hashReason);
            sb.Append(",\"spf_assembly_sha256\":").Append(hash == null ? "null" : "\"" + hash + "\"");
            string version = ResolveSpfVersion();
            sb.Append(",\"spf_assembly_version\":").Append(version == null ? "null" : "\"" + Escape(version) + "\"");
            var unavailable = new List<string>();
            if (mvid == null) unavailable.Add("\"game_mvid\":\"" + Escape(mvidReason) + "\"");
            if (hash == null) unavailable.Add("\"spf_assembly_sha256\":\"" + Escape(hashReason) + "\"");
            if (version == null) unavailable.Add("\"spf_assembly_version\":\"assembly-version-empty\"");
            sb.Append(",\"unavailable\":{").Append(string.Join(",", unavailable.ToArray())).Append('}');
            return sb.ToString();
        }

        private static string ResolveGameMvid(out string reason)
        {
            reason = null;
            try
            {
                Guid mvid = typeof(PlayerInput).Assembly.ManifestModule.ModuleVersionId;
                if (mvid == Guid.Empty) { reason = "module-version-id-empty"; return null; }
                return mvid.ToString("D");
            }
            catch (Exception ex)
            {
                reason = "game-assembly-unavailable:" + ex.GetType().Name;
                return null;
            }
        }

        private static string ResolveSpfSha256(out string reason)
        {
            reason = null;
            try
            {
                string location = typeof(Bug1ProbeRuntime).Assembly.Location;
                if (string.IsNullOrEmpty(location)) { reason = "assembly-location-empty"; return null; }
                using (var sha = System.Security.Cryptography.SHA256.Create())
                using (var stream = File.OpenRead(location))
                {
                    byte[] hash = sha.ComputeHash(stream);
                    var sb = new StringBuilder(hash.Length * 2);
                    foreach (byte b in hash) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                    return sb.ToString();
                }
            }
            catch (Exception ex)
            {
                reason = "assembly-hash-failed:" + ex.GetType().Name;
                return null;
            }
        }

        private static string ResolveSpfVersion()
        {
            try
            {
                Version version = typeof(Bug1ProbeRuntime).Assembly.GetName().Version;
                return version == null ? null : version.ToString();
            }
            catch { return null; }
        }

        /// <summary>落盘：manifest + trace 分卷 + result；原始 trace 不可用时不产 PASS。</summary>
        private static string WriteArtifacts(Bug1Verdict verdict, string reason)
        {
            string root = Config.OutputRoot;
            if (string.IsNullOrEmpty(root))
                root = Path.Combine(BepInEx.Paths.BepInExRootPath, "diagnostics", "spf-bug1", Config.RunId, RoleDir);
            Directory.CreateDirectory(root);

            // t45 交付契约：目录固定为 <OutputRoot>/trace.jsonl + manifest.json + result.json。
            string tracePath = Path.Combine(root, "trace.jsonl");
            File.WriteAllLines(tracePath, _traceLines, new UTF8Encoding(false));

            var manifest = new StringBuilder();
            manifest.Append('{');
            manifest.Append("\"schema_version\":\"").Append(Bug1ProbeConstants.SchemaVersion).Append("\",");
            manifest.Append("\"run_id\":\"").Append(Escape(Config.RunId)).Append("\",");
            manifest.Append("\"step_id\":\"").Append(Escape(Config.StepId)).Append("\",");
            manifest.Append("\"role\":\"HOST\",");
            manifest.Append("\"target_steam_id\":\"").Append(Config.TargetSteamId.ToString(CultureInfo.InvariantCulture)).Append("\",");
            manifest.Append("\"native_speed_cap_u_s\":").Append(Config.NativeSpeedCapUS.ToString("R", CultureInfo.InvariantCulture)).Append(',');
            // t48/F4：spec §3.4 要求走/跑各有上限，两档与本次生效档位都写进 manifest。
            manifest.Append("\"native_speed_cap_walk_u_s\":").Append(Config.NativeSpeedCapWalkUS.ToString("R", CultureInfo.InvariantCulture)).Append(',');
            manifest.Append("\"native_speed_cap_sprint_u_s\":").Append(Config.NativeSpeedCapSprintUS.ToString("R", CultureInfo.InvariantCulture)).Append(',');
            manifest.Append("\"speed_tier\":").Append(Quote(Config.SpeedTier)).Append(',');
            manifest.Append("\"epsilon_u\":").Append(Bug1ProbeConstants.EpsilonU.ToString("R", CultureInfo.InvariantCulture)).Append(',');
            manifest.Append("\"epsilon_yaw_deg\":").Append(Bug1ProbeConstants.EpsilonYawDeg.ToString("R", CultureInfo.InvariantCulture)).Append(',');
            manifest.Append("\"position_quantum_u\":").Append(Bug1ProbeConstants.PositionQuantumU.ToString("R", CultureInfo.InvariantCulture)).Append(',');
            // T71：判定规则修订号与水平事件尺度随 manifest 冻结，避免新旧口径混读。
            manifest.Append("\"m1_rule_revision\":\"").Append(Bug1ProbeConstants.M1RuleRevision).Append("\",");
            manifest.Append("\"motion_axes\":\"").Append(Bug1ProbeConstants.MotionAxes).Append("\",");
            manifest.Append("\"horizontal_motion_min_u\":").Append(Bug1ProbeConstants.HorizontalMotionMinU.ToString("R", CultureInfo.InvariantCulture)).Append(',');
            // T79：事件聚合口径与本次判读的完整性布尔随 manifest 冻结，与 RUN_END / result.json 同源。
            manifest.Append("\"event_aggregation_revision\":\"").Append(Bug1ProbeConstants.EventAggregationRevision).Append("\",");
            manifest.Append("\"selected_event_complete\":").Append(verdict.SelectedEventComplete ? "true" : "false").Append(',');
            manifest.Append("\"event_scan_complete\":").Append(verdict.EventScanComplete ? "true" : "false").Append(',');
            manifest.Append("\"strict_m1_events_is_lower_bound\":").Append(verdict.StrictM1EventsIsLowerBound ? "true" : "false").Append(',');
            manifest.Append("\"right_censored_segments\":").Append(verdict.RightCensoredSegments.ToString(CultureInfo.InvariantCulture)).Append(',');
            // R14-01：manifest 与 RUN_END / result.json 必须同源同字段集，ranges 用同一构造器。
            manifest.Append("\"right_censored_ranges\":").Append(RightCensoredRangesJson(verdict.RightCensoredRanges)).Append(',');
            manifest.Append("\"closed_events\":").Append(ClosedEventsJson(verdict.ClosedEvents)).Append(',');
            manifest.Append("\"output_root\":\"").Append(Escape(Path.GetFullPath(root))).Append("\",");
            manifest.Append(BuildEnvironmentManifestFields());
            manifest.Append('}');
            File.WriteAllText(Path.Combine(root, "manifest.json"), manifest.ToString(), new UTF8Encoding(false));

            var result = new StringBuilder();
            result.Append('{');
            result.Append("\"schema_version\":\"").Append(Bug1ProbeConstants.SchemaVersion).Append("\",");
            result.Append("\"step_id\":\"").Append(Escape(verdict.StepId)).Append("\",");
            result.Append("\"valid\":").Append(verdict.Valid ? "true" : "false").Append(',');
            result.Append("\"label\":\"").Append(verdict.Label).Append("\",");
            result.Append("\"reason\":\"").Append(Escape(verdict.Reason)).Append("\",");
            result.Append("\"n_expected\":").Append(verdict.NExpected.ToString(CultureInfo.InvariantCulture)).Append(',');
            result.Append("\"observed\":").Append(verdict.NObserved.ToString(CultureInfo.InvariantCulture)).Append(',');
            // T71/M1-XZ-RELATIVE-v2 §3.1：新口径字段。旧 root_span（三维、无轴语义）不再导出，
            // 原 result.json 文件本身保留在历史 run 目录里供审计，不被本口径覆盖。
            result.Append("\"m1_rule_revision\":\"").Append(Escape(verdict.M1RuleRevision)).Append("\",");
            result.Append("\"motion_axes\":\"").Append(Escape(verdict.MotionAxes)).Append("\",");
            result.Append("\"horizontal_motion_min_u\":").Append(verdict.HorizontalMotionMinU.ToString("R", CultureInfo.InvariantCulture)).Append(',');
            result.Append("\"render_root_span_xz_u\":").Append(verdict.RenderRootSpanXZ.ToString("R", CultureInfo.InvariantCulture)).Append(',');
            result.Append("\"sim_root_span_3d_u\":").Append(verdict.SimRootSpan3D.ToString("R", CultureInfo.InvariantCulture)).Append(',');
            result.Append("\"lag_witness_frame\":").Append(verdict.LagWitnessFrame.ToString(CultureInfo.InvariantCulture)).Append(',');
            result.Append("\"lag_witness_authority_pos\":").Append(VecJsonInline(verdict.LagWitnessAuthority)).Append(',');
            result.Append("\"lag_witness_carrier_pos\":").Append(VecJsonInline(verdict.LagWitnessCarrier)).Append(',');
            result.Append("\"lag_before_release_u\":").Append(verdict.LagBeforeReleaseU.ToString("R", CultureInfo.InvariantCulture)).Append(',');
            result.Append("\"lag_after_release_u\":").Append(verdict.LagAfterReleaseU.ToString("R", CultureInfo.InvariantCulture)).Append(',');
            result.Append("\"horizontal_stall_segments\":").Append(verdict.HorizontalStallSegments.ToString(CultureInfo.InvariantCulture)).Append(',');
            result.Append("\"horizontal_candidates\":").Append(verdict.HorizontalCandidates.ToString(CultureInfo.InvariantCulture)).Append(',');
            result.Append("\"auth_segments\":").Append(verdict.AuthSegments.ToString(CultureInfo.InvariantCulture)).Append(',');
            result.Append("\"non_m1_segments\":").Append(verdict.NonM1Segments.ToString(CultureInfo.InvariantCulture)).Append(',');
            result.Append("\"candidate_rejections\":[").Append(QuoteList(verdict.CandidateRejections)).Append("],");
            result.Append("\"epsilon_u\":").Append(verdict.EpsilonU.ToString("R", CultureInfo.InvariantCulture)).Append(',');
            result.Append("\"candidate_a\":").Append(verdict.CandidateA.ToString(CultureInfo.InvariantCulture)).Append(',');
            result.Append("\"candidate_b\":").Append(verdict.CandidateB.ToString(CultureInfo.InvariantCulture)).Append(',');
            result.Append("\"release_frame\":").Append(verdict.ReleaseFrame.ToString(CultureInfo.InvariantCulture)).Append(',');
            result.Append("\"strict_m1_events\":").Append(verdict.StrictM1Events.ToString(CultureInfo.InvariantCulture)).Append(',');
            // T79/CLOSED_EVENT_PRIORITY-v1：与 RUN_END 行同一套数——右截断只局部生效，事件计数是下界。
            result.Append("\"event_aggregation_revision\":\"").Append(Escape(verdict.EventAggregationRevision)).Append("\",");
            result.Append("\"selected_event_complete\":").Append(verdict.SelectedEventComplete ? "true" : "false").Append(',');
            result.Append("\"event_scan_complete\":").Append(verdict.EventScanComplete ? "true" : "false").Append(',');
            result.Append("\"strict_m1_events_is_lower_bound\":").Append(verdict.StrictM1EventsIsLowerBound ? "true" : "false").Append(',');
            result.Append("\"right_censored_segments\":").Append(verdict.RightCensoredSegments.ToString(CultureInfo.InvariantCulture)).Append(',');
            result.Append("\"right_censored_ranges\":").Append(RightCensoredRangesJson(verdict.RightCensoredRanges)).Append(',');
            result.Append("\"closed_events\":").Append(ClosedEventsJson(verdict.ClosedEvents)).Append(',');
            // t60：窗口收口与缺相位归因，必须与 RUN_END 行同一套数。
            result.Append("\"window_head_excluded\":").Append(verdict.WindowHeadExcluded.ToString(CultureInfo.InvariantCulture)).Append(',');
            result.Append("\"window_tail_excluded\":").Append(verdict.WindowTailExcluded.ToString(CultureInfo.InvariantCulture)).Append(',');
            result.Append("\"window_interior_partial\":").Append(verdict.WindowInteriorPartial.ToString(CultureInfo.InvariantCulture)).Append(',');
            result.Append("\"phase_missing_genuine\":").Append(verdict.PhaseMissingGenuine.ToString(CultureInfo.InvariantCulture)).Append(',');
            result.Append("\"phase_missing_out_of_window\":").Append(verdict.PhaseMissingOutOfWindow.ToString(CultureInfo.InvariantCulture)).Append(',');
            result.Append("\"window_excluded_reason\":").Append(Quote(verdict.WindowExcludedReason)).Append(',');
            // t65：尾部归因与采集面证据，与 RUN_END 行同源。
            result.Append("\"window_tail_boundary\":").Append(Quote(verdict.WindowTailBoundaryKind)).Append(',');
            result.Append("\"window_head_boundary\":").Append(Quote(verdict.WindowHeadBoundaryKind)).Append(',');
            result.Append("\"window_tail_unexplained\":").Append(verdict.WindowTailUnexplained ? "true" : "false").Append(',');
            result.Append("\"window_tail_render_only\":").Append(verdict.WindowTailRenderOnly.ToString(CultureInfo.InvariantCulture)).Append(',');
            result.Append("\"window_tail_first_frame\":").Append(verdict.WindowTailFirstFrame.ToString(CultureInfo.InvariantCulture)).Append(',');
            result.Append("\"window_tail_last_frame\":").Append(verdict.WindowTailLastFrame.ToString(CultureInfo.InvariantCulture)).Append(',');
            result.Append("\"capture_state\":").Append(string.IsNullOrEmpty(verdict.CaptureState) ? "null" : verdict.CaptureState).Append(',');
            result.Append("\"missing_fields\":[").Append(QuoteList(verdict.MissingFields)).Append("],");
            result.Append("\"unpaired_sim_ids\":[").Append(QuoteList(verdict.UnpairedSimIds)).Append("],");
            result.Append("\"missing_frames\":[").Append(IntList(verdict.MissingFrames)).Append("]");
            result.Append('}');
            File.WriteAllText(Path.Combine(root, "result.json"), result.ToString(), new UTF8Encoding(false));

            RoleLogger.Info("[Shared]", Label + " artifact dir=" + Path.GetFullPath(root) + " end_reason=" + reason);
            return Path.GetFullPath(root);
        }

        /// <summary>T79：已闭合事件清单，供审计核对 strict_m1_events 的每一件来源。</summary>
        private static string ClosedEventsJson(List<Bug1ClosedEvent> events)
        {
            var sb = new StringBuilder();
            sb.Append('[');
            for (int i = 0; i < events.Count; i++)
            {
                if (i > 0) sb.Append(',');
                Bug1ClosedEvent ev = events[i];
                sb.Append('{');
                sb.Append("\"kind\":\"").Append(Escape(ev.Kind)).Append("\",");
                sb.Append("\"stall_start_frame\":").Append(ev.StallStartFrame.ToString(CultureInfo.InvariantCulture)).Append(',');
                sb.Append("\"stall_end_frame\":").Append(ev.StallEndFrame.ToString(CultureInfo.InvariantCulture)).Append(',');
                sb.Append("\"release_frame\":").Append(ev.ReleaseFrame.ToString(CultureInfo.InvariantCulture)).Append(',');
                sb.Append("\"evidence_complete\":").Append(ev.EvidenceComplete ? "true" : "false");
                sb.Append('}');
            }
            sb.Append(']');
            return sb.ToString();
        }

        /// <summary>T79：局部右截断段清单（release_frame 恒 -1），与正式候选三元组分开导出。</summary>
        private static string RightCensoredRangesJson(List<Bug1RightCensoredRange> ranges)
        {
            var sb = new StringBuilder();
            sb.Append('[');
            for (int i = 0; i < ranges.Count; i++)
            {
                if (i > 0) sb.Append(',');
                Bug1RightCensoredRange range = ranges[i];
                sb.Append('{');
                sb.Append("\"stall_start_frame\":").Append(range.StallStartFrame.ToString(CultureInfo.InvariantCulture)).Append(',');
                sb.Append("\"stall_end_frame\":").Append(range.StallEndFrame.ToString(CultureInfo.InvariantCulture)).Append(',');
                sb.Append("\"release_frame\":").Append(range.ReleaseFrame.ToString(CultureInfo.InvariantCulture)).Append(',');
                sb.Append("\"reason\":\"").Append(Escape(range.Reason)).Append('"');
                sb.Append('}');
            }
            sb.Append(']');
            return sb.ToString();
        }

        private static string QuoteList(List<string> values)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < values.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('"').Append(Escape(values[i])).Append('"');
            }
            return sb.ToString();
        }

        private static string IntList(List<int> values)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < values.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(values[i].ToString(CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        /// <summary>session reset：换 epoch 立即停机，不把旧实例写入新局。</summary>
        public static void OnSessionReset()
        {
            _sessionEpoch++;
            _connectionGeneration++;
            if (Enabled) Disable("session_reset");
        }
    }
}
