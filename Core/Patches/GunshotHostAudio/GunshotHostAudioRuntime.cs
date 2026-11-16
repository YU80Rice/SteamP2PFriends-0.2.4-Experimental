using HarmonyLib;
using SDG.Unturned;
using SteamP2PFriends.Host;
using SteamP2PFriends.Shared;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace SteamP2PFriends.Core.Patches.GunshotHostAudio
{
    /// <summary>
    /// t116：Host 本机原生枪声叶补偿（默认关闭，fail-closed）。
    ///
    /// 唯一行为：在 Host 的 UseableGun.fire() 真实 server 发送锚点
    /// （SendPlayShoot.Invoke 正常返回之后、barrel durability 修改之前）补一次
    /// 该 UseableGun 实例原有的 playGunshot()。不复制 clip/衰减、不新建 AudioSource、
    /// 不调用 shoot()/project()/ReceivePlayShoot()、不新增 RPC。
    ///
    /// 去重账本：fire() Prefix 入口分配 ordinal 并建键；锚点处 claim-before-call；
    /// 同一键全生命周期最多一次叶调用；叶失败记 Failed 不重试。
    /// </summary>
    internal static class GunshotHostAudioRuntime
    {
        internal const string Label = "[Bug2HostGunshot]";
        private const int GateLogLimit = 200;

        private static readonly Stack<FireFrame> _frames = new Stack<FireFrame>();
        private static readonly Dictionary<uint, int> _lastWeaponInstanceHash = new Dictionary<uint, int>();
        private static readonly Dictionary<uint, uint> _weaponGenerations = new Dictionary<uint, uint>();

        private static long _nextOrdinal;
        private static uint _connectionGeneration = 1U;
        private static int _lastClientCount = -1;
        private static int _gateLogCount;
        private static FieldInfo _gunshotAudioSourceField;
        // R26-02：用编译后的 ldfld 取值，而不是 FieldInfo.GetValue——后者在 .NET Framework
        // 上会触发 UseableGun 的类型初始化（cctor 依赖 Unity 原生 ECall，测试宿主会抛
        // SecurityException）。ldfld 只读实例字段，不触发 cctor，生产/测试行为一致。
        private static Func<UseableGun, AudioSource> _gunshotAudioSourceGetter;
        private static Action<UseableGun> _defaultLeaf;
        private static bool _defaultLeafResolved;
        private static int _supplementalLeafDepth;
        private static bool _nativeObserverInstalled;
        private static int _leafResolveFailures;

        internal static bool Enabled { get; private set; }
        internal static bool FailClosed { get; private set; }
        internal static string FailClosedReason { get; private set; } = "";
        internal static int LeafCallCount { get; private set; }
        internal static int NativeLeafObservationCount { get; private set; }
        internal static int AbortedAfterAnchorCount { get; private set; }
        internal static int SuccessfulShotCount { get; private set; }
        internal static int LeafResolveFailures => _leafResolveFailures;
        internal static bool NativeObserverInstalled => _nativeObserverInstalled;
        internal static int GateLogCount => _gateLogCount;
        internal static string LastGateLine { get; private set; } = "";
        internal static int FrameDepth => _frames.Count;

        // 测试缝：生产路径为 null；测试用纯数据事实与可计数叶替换 Unity 依赖。
        internal static Func<UseableGun, GunshotGateFacts> FactsProviderForTest;
        internal static Action<UseableGun> LeafInvokerForTest;

        // R24-02：leaf 解析结果可注入；返回 null 时走 LeafUnresolved + fail-closed。
        internal static Func<Action<UseableGun>> LeafResolverForTest;

        // R24-04：AudioSource 探测可注入，便于在无 Unity 宿主验证 15 项门控映射。
        internal static Func<UseableGun, AudioSource> AudioSourceProbeForTest;

        // 测试缝：非 Unity 宿主没有 UnityEngine.Random 的 ECall，用可观测的保存/恢复替身。
        internal static Func<object> RandomStateSaveForTest;
        internal static Action<object> RandomStateRestoreForTest;

        // R27-03：SessionEpoch 的静默降级必须可观测（跨会话去重键不能无提示地退化成 1）。
        internal static Func<string> SessionIdProviderForTest;
        internal static int SessionEpochFallbackCount { get; private set; }
        internal static string SessionEpochFallbackReason { get; private set; } = "";

        internal static void SetEnabled(bool enabled)
        {
            Enabled = enabled;
        }

        internal static void SetFailClosed(string reason)
        {
            FailClosed = true;
            FailClosedReason = string.IsNullOrEmpty(reason) ? "unspecified" : reason;
        }

        /// <summary>
        /// 运行期恢复：热开启/重新安装时清除黏性 FailClosed。
        /// 只由 Control 在显式 enable 且重新安装成功后调用。
        /// </summary>
        internal static void ClearFailClosed()
        {
            FailClosed = false;
            FailClosedReason = "";
        }

        internal static void SetNativeObserverInstalled(bool installed)
        {
            _nativeObserverInstalled = installed;
        }

        /// <summary>
        /// playGunshot Postfix 观察器：记录「该 UseableGun 实例本次 fire 已进入原生叶」。
        /// 我们自己补的那一次叶调用（_supplementalLeafDepth &gt; 0）不算原生。
        /// </summary>
        internal static void OnNativeLeafObserved(UseableGun gun)
        {
            try
            {
                if (_supplementalLeafDepth > 0)
                {
                    return;
                }

                NativeLeafObservationCount++;
                if (_frames.Count == 0)
                {
                    return;
                }

                FireFrame frame = _frames.Peek();
                if (!ReferenceEquals(frame.Gun, gun))
                {
                    return;
                }

                frame.NativeLeafEntered = true;
                _frames.Pop();
                _frames.Push(frame);
            }
            catch
            {
                // 观察器异常不得影响真实 fire。
            }
        }

        internal static void OnFireEnter(UseableGun gun)
        {
            if (!Enabled || FailClosed)
            {
                _frames.Push(default(FireFrame));
                return;
            }

            long ordinal = ++_nextOrdinal;
            GunshotFireKey key = BuildKey(gun, ordinal);
            _frames.Push(new FireFrame { Key = key, Gun = gun });
        }

        /// <summary>
        /// 锚点调用：SendPlayShoot.Invoke 正常返回后、耐久修改之前。
        /// 任何异常都必须被吞掉（宁可没声，也不能打断真实 fire）。
        /// </summary>
        internal static void TryCompensateAfterSend(UseableGun gun)
        {
            if (!Enabled || FailClosed)
            {
                return;
            }

            try
            {
                if (_frames.Count == 0)
                {
                    LogGate(default(GunshotGateFacts), default(GunshotFireKey), "NoFireFrame");
                    return;
                }

                FireFrame frame = _frames.Peek();
                if (!ReferenceEquals(frame.Gun, gun))
                {
                    LogGate(default(GunshotGateFacts), frame.Key, "AssociationMismatch");
                    return;
                }

                // 锚点实际命中：本方法只由 transpiler 在唯一 SendPlayShoot.Invoke 之后插入调用。
                frame.AnchorReached = true;
                // 勾稽完成：frame.Gun 与当前 gun 实例一致（上方 ReferenceEquals）。
                frame.AssociationChecked = true;
                _frames.Pop();
                _frames.Push(frame);

                GunshotGateFacts facts = FactsProviderForTest != null
                    ? FactsProviderForTest(gun)
                    : Collect(gun);

                // 三项都不硬置：
                //   RealSendAnchor      = 锚点实际命中（上方 AnchorReached）；
                //   AssociationComplete = 上方 ReferenceEquals(frame.Gun, gun) 的勾稽结果；
                //   NativeLeafNotEntered= playGunshot Postfix 观察器记录的「本次 fire 已进入原生叶」取反。
                //
                // R24-01：原先「观察器未安装」分支已删除。Enabled && !FailClosed && !observer
                // 在生产不可达：Control 只在 Apply(true) 时 SetEnabled(true)，而 Apply(true) 必须先
                // 通过 RegisterManual()——注册失败会 Fail() 置 FailClosed，注册成功则只有在
                // Harmony.GetPatchInfo 回查到我们自己的 Postfix 后才 SetNativeObserverInstalled(true)。
                // 因此本方法开头的 FailClosed 短路就是该不变量的兜底；这里不再保留不可达分支。
                facts.RealSendAnchor = frame.AnchorReached;
                facts.AssociationComplete = frame.AssociationChecked;
                facts.NativeLeafNotEntered = !frame.NativeLeafEntered;

                if (!GunshotHostAudioPolicy.IsEligible(facts))
                {
                    LogGate(facts, frame.Key, "NotEligible");
                    return;
                }

                if (!GunshotHostAudioLedger.TryClaim(frame.Key))
                {
                    LogGate(facts, frame.Key, "Duplicate");
                    return;
                }

                // R24-02：优先显式注入的叶；其次可注入 resolver（测试 null 路径）；最后生产解析。
                Action<UseableGun> leaf = LeafInvokerForTest
                    ?? (LeafResolverForTest != null ? LeafResolverForTest() : ResolveDefaultLeaf());
                if (leaf == null)
                {
                    GunshotHostAudioLedger.Fail(frame.Key);
                    SetFailClosed("playGunshot unresolved");
                    LogGate(facts, frame.Key, "LeafUnresolved");
                    return;
                }

                object rngState;
                bool rngCaptured;
                try
                {
                    if (RandomStateSaveForTest != null)
                    {
                        rngState = RandomStateSaveForTest();
                        rngCaptured = true;
                    }
                    else
                    {
                        rngState = UnityEngine.Random.state;
                        rngCaptured = true;
                    }
                }
                catch (Exception ex)
                {
                    GunshotHostAudioLedger.Fail(frame.Key);
                    SetFailClosed("rng state unavailable: " + ex.GetType().Name);
                    LogGate(facts, frame.Key, "RngUnavailable:" + ex.GetType().Name);
                    return;
                }

                try
                {
                    _supplementalLeafDepth++;
                    try
                    {
                        leaf(gun);
                    }
                    finally
                    {
                        _supplementalLeafDepth--;
                    }

                    frame.Compensated = true;
                    _frames.Pop();
                    _frames.Push(frame);
                    GunshotHostAudioLedger.Complete(frame.Key);
                    LeafCallCount++;
                    LogGate(facts, frame.Key, "Compensate");
                }
                catch (Exception ex)
                {
                    GunshotHostAudioLedger.Fail(frame.Key);
                    LogGate(facts, frame.Key, "LeafException:" + ex.GetType().Name);
                }
                finally
                {
                    if (rngCaptured)
                    {
                        if (RandomStateRestoreForTest != null)
                        {
                            RandomStateRestoreForTest(rngState);
                        }
                        else
                        {
                            UnityEngine.Random.state = (UnityEngine.Random.State)rngState;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // fail-closed：补偿路径自身异常不得打断真实 fire。
                SetFailClosed("compensate exception: " + ex.GetType().Name);
                LogGate(default(GunshotGateFacts), default(GunshotFireKey), "CompensateException:" + ex.GetType().Name);
            }
        }

        internal static void OnFireExit(UseableGun gun, bool faulted)
        {
            if (!Enabled)
            {
                if (_frames.Count > 0) _frames.Pop();
                return;
            }

            if (_frames.Count == 0)
            {
                return;
            }

            FireFrame frame = _frames.Pop();
            if (frame.Compensated)
            {
                // 已补声后真实 fire 抛异常：不能用 Completed→Fail 的无效迁移掩盖，
                // 单独计数并在 gate 行显式标记，保证分母与标记都可见。
                if (faulted)
                {
                    AbortedAfterAnchorCount++;
                    LogGate(default(GunshotGateFacts), frame.Key, "AbortedAfterAnchor");
                }
                else
                {
                    SuccessfulShotCount++;
                }

                return;
            }

            if (faulted && !ReferenceEquals(frame.Gun, null))
            {
                GunshotHostAudioLedger.Fail(frame.Key);
            }
        }

        /// <summary>
        /// R25-02：Collect 从 SDK 读到的原始值。测试通过 CollectObserverForTest 观察这些值，
        /// 覆盖「SDK 读取 → BuildFactsFromState 实参」的接线，而不只是纯函数映射。
        /// </summary>
        internal struct GunshotCollectSources
        {
            internal bool P2PHostMode;
            internal bool ListenHostActive;
            internal bool Dedicated;
            internal bool Connected;
            internal bool ServerActive;
            internal bool LevelLoaded;
            internal bool ShooterPresent;
            internal bool ShooterChannelPresent;
            internal bool ShooterChannelIsLocal;
            internal bool GunPresent;
            internal bool AssetPresent;
            internal bool ProjectilePresent;
            internal bool IsTurret;
            internal bool ClipPresent;
            internal bool AudioSourcePresent;
        }

        internal static Action<GunshotCollectSources> CollectObserverForTest;

        /// <summary>
        /// 生产事实采集：只读 SDK 状态；任何异常都返回全 false 并记录错误（fail-closed）。
        /// R24-04：15 项门控的映射抽到 BuildFactsFromState 纯函数，Collect 只负责读 SDK 原始值，
        /// 便于在无 Unity 宿主逐项断言每个门控（而不是只依赖 FactsProviderForTest）。
        /// R25-02：原始值先装入 GunshotCollectSources 并交给观察器，使「读 SDK → 传参」的接线可被用例抓到。
        /// </summary>
        internal static GunshotGateFacts Collect(UseableGun gun)
        {
            try
            {
                Player shooter = gun == null ? null : gun.player;
                ItemGunAsset asset = gun == null ? null : gun.equippedGunAsset;
                var sources = new GunshotCollectSources
                {
                    P2PHostMode = HostManager.IsP2PHostMode,
                    ListenHostActive = HostManager.ShouldProcessClientHostListen(),
                    Dedicated = Dedicator.IsDedicatedServer,
                    Connected = Provider.isConnected,
                    ServerActive = Provider.isServer,
                    LevelLoaded = Level.isLoaded,
                    ShooterPresent = shooter != null,
                    ShooterChannelPresent = shooter != null && shooter.channel != null,
                    ShooterChannelIsLocal = shooter != null && shooter.channel != null && shooter.channel.IsLocalPlayer,
                    GunPresent = gun != null,
                    AssetPresent = asset != null,
                    ProjectilePresent = asset != null && asset.projectile != null,
                    IsTurret = asset != null && asset.isTurret,
                    ClipPresent = asset != null && asset.shoot != null,
                    // R27-02：音频源判定收敛到单一函数，消费端不再自行拼「gun != null」。
                    AudioSourcePresent = HasGunshotAudioSource(gun),
                };

                if (CollectObserverForTest != null)
                {
                    CollectObserverForTest(sources);
                }

                return BuildFactsFromState(
                    p2pHostMode: sources.P2PHostMode,
                    listenHostActive: sources.ListenHostActive,
                    dedicated: sources.Dedicated,
                    connected: sources.Connected,
                    serverActive: sources.ServerActive,
                    levelLoaded: sources.LevelLoaded,
                    shooterPresent: sources.ShooterPresent,
                    shooterChannelPresent: sources.ShooterChannelPresent,
                    shooterChannelIsLocal: sources.ShooterChannelIsLocal,
                    gunPresent: sources.GunPresent,
                    assetPresent: sources.AssetPresent,
                    projectilePresent: sources.ProjectilePresent,
                    isTurret: sources.IsTurret,
                    clipPresent: sources.ClipPresent,
                    audioSourcePresent: sources.AudioSourcePresent,
                    // 这三项由 TryCompensateAfterSend 依据锚点命中/勾稽/观察器覆盖；
                    // 直接调用 Collect 时保持 false（fail-closed）。
                    realSendAnchor: false,
                    nativeLeafNotEntered: false,
                    associationComplete: false);
            }
            catch (Exception ex)
            {
                var facts = default(GunshotGateFacts);
                facts.CollectionError = ex.GetType().Name;
                return facts;
            }
        }

        /// <summary>
        /// R24-04：15 项门控的纯映射。输入是普通值（不读静态 SDK 状态），
        /// 因此测试可以对每一项做「只翻转一个输入」的断言。
        /// </summary>
        internal static GunshotGateFacts BuildFactsFromState(
            bool p2pHostMode,
            bool listenHostActive,
            bool dedicated,
            bool connected,
            bool serverActive,
            bool levelLoaded,
            bool shooterPresent,
            bool shooterChannelPresent,
            bool shooterChannelIsLocal,
            bool gunPresent,
            bool assetPresent,
            bool projectilePresent,
            bool isTurret,
            bool clipPresent,
            bool audioSourcePresent,
            bool realSendAnchor,
            bool nativeLeafNotEntered,
            bool associationComplete)
        {
            return new GunshotGateFacts
            {
                P2PHostMode = p2pHostMode,
                ListenHostActive = listenHostActive,
                NotDedicated = !dedicated,
                Connected = connected,
                ServerActive = serverActive,
                LevelLoaded = levelLoaded,
                ShooterNotLocal = shooterPresent && shooterChannelPresent && !shooterChannelIsLocal,
                ProjectileNull = assetPresent && !projectilePresent,
                TurretFalse = assetPresent && !isTurret,
                WeaponInstanceValid = gunPresent,
                AudioSourcePresent = gunPresent && audioSourcePresent,
                ClipPresent = assetPresent && clipPresent,
                RealSendAnchor = realSendAnchor,
                NativeLeafNotEntered = nativeLeafNotEntered,
                AssociationComplete = associationComplete,
            };
        }

        internal static GunshotFireKey BuildKey(UseableGun gun, long ordinal)
        {
            uint netId = 0U;
            uint weaponGen = 0U;
            try
            {
                if (gun != null)
                {
                    netId = gun.GetNetId().id;
                    weaponGen = WeaponGenerationFor(gun, netId);
                }
            }
            catch
            {
                netId = 0U;
                weaponGen = 0U;
            }

            return new GunshotFireKey(SessionEpoch(), ConnectionGeneration(), weaponGen, netId, ordinal);
        }

        internal static void ResetForTest()
        {
            _frames.Clear();
            GunshotHostAudioLedger.Reset();
            _lastWeaponInstanceHash.Clear();
            _weaponGenerations.Clear();
            _nextOrdinal = 0L;
            _connectionGeneration = 1U;
            _lastClientCount = -1;
            _gateLogCount = 0;
            LeafCallCount = 0;
            LastGateLine = "";
            FailClosed = false;
            FailClosedReason = "";
            FactsProviderForTest = null;
            LeafInvokerForTest = null;
            LeafResolverForTest = null;
            AudioSourceProbeForTest = null;
            CollectObserverForTest = null;
            RandomStateSaveForTest = null;
            RandomStateRestoreForTest = null;
            SessionIdProviderForTest = null;
            SessionEpochFallbackCount = 0;
            SessionEpochFallbackReason = "";
            _supplementalLeafDepth = 0;
            _nativeObserverInstalled = false;
            _leafResolveFailures = 0;
            _defaultLeaf = null;
            _defaultLeafResolved = false;
            NativeLeafObservationCount = 0;
            AbortedAfterAnchorCount = 0;
            SuccessfulShotCount = 0;
            Enabled = false;
        }

        private static uint WeaponGenerationFor(UseableGun gun, uint netId)
        {
            int instanceHash = RuntimeHelpers.GetHashCode(gun);
            int lastHash;
            uint generation;
            if (!_weaponGenerations.TryGetValue(netId, out generation))
            {
                generation = 0U;
            }

            if (!_lastWeaponInstanceHash.TryGetValue(netId, out lastHash) || lastHash != instanceHash)
            {
                _lastWeaponInstanceHash[netId] = instanceHash;
                generation++;
                _weaponGenerations[netId] = generation;
                GunshotHostAudioLedger.PurgeWeapon(netId, generation);
            }

            return generation;
        }

        private static uint ConnectionGeneration()
        {
            try
            {
                int count = Provider.clients == null ? 0 : Provider.clients.Count;
                if (count != _lastClientCount)
                {
                    _lastClientCount = count;
                    _connectionGeneration++;
                    GunshotHostAudioLedger.PurgeExcept(SessionEpoch(), _connectionGeneration);
                }
            }
            catch
            {
                // Provider 尚未初始化时保持上一世代。
            }

            return _connectionGeneration;
        }

        private static ulong SessionEpoch()
        {
            try
            {
                string sessionId = SessionIdProviderForTest != null
                    ? SessionIdProviderForTest()
                    : HostManager.CurrentSessionId;
                if (string.IsNullOrEmpty(sessionId))
                {
                    return 1UL;
                }

                ulong hash = 14695981039346656037UL;
                for (int i = 0; i < sessionId.Length; i++)
                {
                    hash ^= sessionId[i];
                    hash *= 1099511628211UL;
                }

                return hash == 0UL ? 1UL : hash;
            }
            catch (Exception ex)
            {
                // R27-03：降级不能静默——计数 + 原因可被用例断言（跨会话去重键碰撞风险可见）。
                SessionEpochFallbackCount++;
                SessionEpochFallbackReason = ex.GetType().Name;
                return 1UL;
            }
        }

        /// <summary>
        /// R27-02：枪是否真的有可播放音频源——门控 AudioSourcePresent 的唯一判定。
        /// 反射失配/字段缺失/实例为空都返回 false（fail-closed），消费端不得放宽成 gun != null。
        /// </summary>
        internal static bool HasGunshotAudioSource(UseableGun gun)
        {
            return gun != null && GetGunshotAudioSource(gun) != null;
        }

        private static AudioSource GetGunshotAudioSource(UseableGun gun)
        {
            if (AudioSourceProbeForTest != null)
            {
                return AudioSourceProbeForTest(gun);
            }

            if (gun == null)
            {
                return null;
            }

            if (_gunshotAudioSourceGetter == null)
            {
                _gunshotAudioSourceField = AccessTools.Field(typeof(UseableGun), "gunshotAudioSource");
                if (_gunshotAudioSourceField == null)
                {
                    return null;
                }

                var getter = new DynamicMethod(
                    "GetGunshotAudioSource",
                    typeof(AudioSource),
                    new[] { typeof(UseableGun) },
                    typeof(GunshotHostAudioRuntime).Module,
                    true);
                ILGenerator il = getter.GetILGenerator();
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, _gunshotAudioSourceField);
                il.Emit(OpCodes.Ret);
                _gunshotAudioSourceGetter = (Func<UseableGun, AudioSource>)getter.CreateDelegate(
                    typeof(Func<UseableGun, AudioSource>));
            }

            return _gunshotAudioSourceGetter(gun);
        }

        /// <summary>
        /// 生产 leaf 解析：只在成功时缓存；失败不永久缓存 null（R23-06），
        /// 下次 fire 仍会重试解析并累计失败次数。
        /// </summary>
        internal static Action<UseableGun> ResolveDefaultLeaf()
        {
            if (_defaultLeafResolved)
            {
                return _defaultLeaf;
            }

            try
            {
                MethodInfo method = AccessTools.Method(typeof(UseableGun), "playGunshot", Type.EmptyTypes);
                if (method == null)
                {
                    _leafResolveFailures++;
                    return null;
                }

                _defaultLeaf = (Action<UseableGun>)Delegate.CreateDelegate(typeof(Action<UseableGun>), method);
                _defaultLeafResolved = true;
            }
            catch (Exception ex)
            {
                _leafResolveFailures++;
                FailClosedReason = "playGunshot delegate: " + ex.GetType().Name;
                _defaultLeaf = null;
            }

            return _defaultLeaf;
        }

        private static void LogGate(GunshotGateFacts facts, GunshotFireKey key, string decision)
        {
            try
            {
                LastGateLine = GunshotHostAudioPolicy.FormatGateLine(facts, key, decision, MapName());
                _gateLogCount++;
                if (_gateLogCount <= GateLogLimit)
                {
                    RoleLogger.Info("[Shared]", Label + " " + LastGateLine);
                }
            }
            catch
            {
                // 日志失败不得影响 fire。
            }
        }

        private static string MapName()
        {
            try
            {
                return Level.info == null ? null : Level.info.name;
            }
            catch
            {
                return null;
            }
        }

        private struct FireFrame
        {
            internal GunshotFireKey Key;
            internal UseableGun Gun;
            internal bool AnchorReached;
            internal bool AssociationChecked;
            internal bool NativeLeafEntered;
            internal bool Compensated;
        }
    }
}
