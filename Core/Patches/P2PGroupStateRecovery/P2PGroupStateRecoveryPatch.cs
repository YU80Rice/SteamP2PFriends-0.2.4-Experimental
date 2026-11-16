using HarmonyLib;
using SDG.NetTransport.Loopback;
using SDG.Unturned;
using SteamP2PFriends.Host;
using SteamP2PFriends.Shared;
using Steamworks;
using System;
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace SteamP2PFriends.Core.Patches.P2PGroupStateRecovery
{
    /// <summary>
    /// Bug3（同存档跨局主机队伍身份丢失）最小生产修复的 Harmony 胶水层。
    ///
    /// 只做三件事：
    ///   1. 原生 PlayerQuests.InitializePlayer 触发的首次 load，在"当前 GAME 组表尚未加载完成"
    ///      时阻止本次执行并排队（前缀返回 false）；InitializePlayer 的容器初始化与事件订阅
    ///      在此之前已经跑完，且不会二次执行 InitializePlayer。
    ///   2. 原生 GroupManager.onLevelLoaded(GAME) 成功后（含合法空表），校验同 epoch/地图/实例，
    ///      只调用一次原生 Quests.load，让存档里的 alpha/OWNER 从已就绪组表正常恢复。
    ///   3. 通过原生 changeRank → SendGroupState.InvokeAndLoopback → ReceiveGroupState 修正
    ///      早已发出的本机 none 投影；不新增组包、不加自定义频道、不改 members。
    ///
    /// 手动登记：本类不带 [HarmonyPatch]，避免 PatchAll 顺带注册；由
    /// SteamP2PFriendsPlugin.PatchRegistrationAuditModules.cs 显式调用 RegisterManual。
    /// </summary>
    public static class P2PGroupStateRecoveryPatch
    {
        public const bool Enabled = true;

        private const string LogTag = "[P2P-GroupRecovery]";

        private sealed class PlayerToken
        {
            internal long Value;
        }

        /// <summary>
        /// T24/F1：活对象写入安全证明。键 = PlayerQuests 引用 + epoch + token（map 一并绑定）。
        /// 只回答"这个仍可能被 save 的实例，本 key 的原生 load 是否完整成功过"，
        /// 不是恢复算法、不保留旧局全账。
        /// </summary>
        private sealed class WriteSafetySlot
        {
            internal long Epoch;
            internal long Token;
            internal string MapName = string.Empty;
            internal bool Managed;
            internal bool NativeLoadCompleted;
            internal string BlockReason = string.Empty;
        }

        private static ConditionalWeakTable<PlayerQuests, WriteSafetySlot> WriteSafety =
            new ConditionalWeakTable<PlayerQuests, WriteSafetySlot>();

        private static readonly GroupStateRecoveryCore Core = new GroupStateRecoveryCore();
        private static readonly ConditionalWeakTable<PlayerQuests, PlayerToken> PlayerTokens =
            new ConditionalWeakTable<PlayerQuests, PlayerToken>();
        private static long _nextPlayerToken;
        private static PlayerQuests _pendingQuests;
        private static PlayerQuests _notifyTarget;

        // T14/E5：登记状态与失败快照（失败必须传播给父入口，不只打日志）。
        private static Harmony _harmony;
        private static bool _registered;
        private static string _registeredHarmonyId;
        private static Guid _registeredModuleMvid = Guid.Empty;
        private static P2PGroupRegistrationState _registrationState = P2PGroupRegistrationState.Unregistered;
        private static P2PGroupRegistrationFailure _lastFailure;
        private static int _registrationAttempt;
        private static bool _levelSubscribed;
        private static bool _providerSubscribed;
        private static bool _playerSubscribed;
        private static bool _playerSubscriptionUnavailable;
        private static bool _providerSubscriptionUnavailable;

        // T14/E2：可注入 seam，仅供控制台测试；生产路径保持 null。
        internal static Func<PlayerQuests, GroupRecoveryEligibility> EligibilityResolverForTests;
        internal static Func<string> MapNameResolverForTests;
        internal static System.Action CommitObserverForTests;

        internal static GroupStateRecoveryCore CoreForTests => Core;
        internal static bool RegisteredForTests => _registered;
        internal static bool PendingQuestsForTests => !ReferenceEquals(_pendingQuests, null);
        internal static long PendingTokenForTests => ReferenceEquals(_pendingQuests, null) ? 0L : TokenOf(_pendingQuests);
        internal static P2PGroupRegistrationState RegistrationStateForTests => _registrationState;
        internal static P2PGroupRegistrationFailure LastFailureForTests => _lastFailure;
        internal static bool LevelSubscribedForTests => _levelSubscribed;
        internal static bool ProviderSubscribedForTests => _providerSubscribed;
        internal static bool PlayerSubscribedOrUnavailableForTests => _playerSubscribed || _playerSubscriptionUnavailable;
        internal static string LastPrefixFailure => _lastPrefixFailure;
        private static string _lastPrefixFailure = string.Empty;

        // T18适配 seam：让控制台用例能在不触发真实 Unity 生命周期的情况下驱动边界处理器、
        // 模拟原生 load 与组状态发布。生产路径全部保持 null。
        internal static System.Action<PlayerQuests> DeferredLoadInvokerForTests;
        internal static System.Action<PlayerQuests, EPlayerGroupRank> ChangeRankActionForTests;
        /// <summary>T24/F2：控制台无原生表对象，用例用该探针替代只读反射；生产保持 null。</summary>
        internal static Func<bool> NativeGroupTableProbeForTests;
        /// <summary>T24/F2：GroupManager.knownGroups 的只读反射缓存（登记时解析，失败即注册失败）。</summary>
        private static FieldInfo _knownGroupsField;
        /// <summary>T24/F1：测试读取某个实例的写入安全证明（生产不使用）。</summary>
        internal static bool IsSaveBlockedForTests(PlayerQuests quests) => IsSaveBlocked(quests);
        internal static bool IsNativeLoadCompletedForTests(PlayerQuests quests) => IsNativeLoadCompleted(quests);
        internal static void InvokeLevelLoadedForTests(int level) => OnLevelLoaded(level);
        internal static void InvokeServerShutdownForTests() => OnServerShutdown();
        internal static string LastReentryDetail { get; private set; } = string.Empty;

        /// <summary>测试 seam：模拟"正处于受控 flags 通知中"，用于验证重入 load 被拒绝。</summary>
        internal static void BeginNotifyForTests(PlayerQuests quests)
        {
            _notifyTarget = quests;
            Core.NoteNotifyAttempted();
        }

        internal static void EndNotifyForTests(string reason)
        {
            _notifyTarget = null;
            Core.NoteNotifyFailed(reason ?? "test-end");
        }
        internal static MethodInfo LoadTargetForTests { get; private set; }
        internal static MethodInfo GroupTableTargetForTests { get; private set; }
        internal static MethodInfo SaveTargetForTests { get; private set; }
        internal static MethodInfo LoadPrefixMethod => AccessTools.Method(typeof(P2PGroupStateRecoveryPatch), "Prefix_Load");
        internal static MethodInfo GroupTablePostfixMethod => AccessTools.Method(typeof(P2PGroupStateRecoveryPatch), "Postfix_GroupTableLoaded");
        internal static MethodInfo SavePrefixMethod => AccessTools.Method(typeof(P2PGroupStateRecoveryPatch), "Prefix_Save");

        /// <summary>
        /// T14/E5：生产登记。失败必须抛出 P2PGroupRegistrationException，让父入口
        /// （PatchRegistrationAuditModules 的 catch）把注册阶段标记为失败，而不是只留一行日志。
        /// 同 Harmony.Id 重入先核验真实补丁与生命周期订阅；跨实例/跨 Assembly 热重载显式拒绝并要求重启。
        /// </summary>
        public static void RegisterManual(Harmony harmony)
        {
            if (harmony == null)
                throw new ArgumentNullException(nameof(harmony));

            _registrationAttempt++;
            _registrationState = P2PGroupRegistrationState.Registering;
            Guid moduleMvid = typeof(P2PGroupStateRecoveryPatch).Assembly.ManifestModule.ModuleVersionId;

            try
            {
                if (_registered && string.Equals(_registeredHarmonyId, harmony.Id, StringComparison.Ordinal)
                    && _registeredModuleMvid == moduleMvid)
                {
                    if (VerifyCurrent(harmony))
                    {
                        _registrationState = P2PGroupRegistrationState.Ready;
                        return;
                    }
                    if (!UnpatchOwned(harmony))
                        throw RegistrationFailure(P2PGroupRegistrationFailureReason.RollbackFailed,
                            "same-id-rollback", true, null);
                    _registered = false;
                }
                else if (_registered)
                {
                    // 无法证明旧 Harmony 实例/旧 Assembly 的委托已安全退订：只做本模块有界清理，
                    // 然后显式失败并要求重启，绝不在旧补丁之上再叠一层。
                    bool rolledBack = UnpatchOwned(_harmony);
                    throw RegistrationFailure(P2PGroupRegistrationFailureReason.StaleOwnerOrHotReload,
                        "hot-reload", true, null, rolledBack ? "rolled-back" : "rollback-failed");
                }

                MethodInfo load = AccessTools.Method(typeof(PlayerQuests), "load", Type.EmptyTypes);
                MethodInfo groupTable = AccessTools.Method(typeof(GroupManager), "onLevelLoaded", new[] { typeof(int) });
                // T18/F1：save 前缀是 pending 的最后期限点；找不到时按注册失败处理，不得静默缺省。
                MethodInfo save = AccessTools.Method(typeof(PlayerQuests), "save", Type.EmptyTypes);
                // T24/F2：knownGroups 只读反射用于核实 native 表对象；解析失败按注册失败处理。
                FieldInfo knownGroupsField = AccessTools.Field(typeof(GroupManager), "knownGroups");
                if (load == null || groupTable == null || save == null || knownGroupsField == null)
                    throw RegistrationFailure(P2PGroupRegistrationFailureReason.TargetResolveFailed,
                        "target-resolve", false, null);

                _knownGroupsField = knownGroupsField;

                LoadTargetForTests = load;
                GroupTableTargetForTests = groupTable;
                SaveTargetForTests = save;
                harmony.Patch(load, prefix: new HarmonyMethod(LoadPrefixMethod));
                harmony.Patch(groupTable, postfix: new HarmonyMethod(GroupTablePostfixMethod));
                harmony.Patch(save, prefix: new HarmonyMethod(SavePrefixMethod));

                if (!VerifyRegistered(harmony, load, groupTable, save))
                {
                    bool rolledBack = UnpatchOwned(harmony);
                    throw RegistrationFailure(P2PGroupRegistrationFailureReason.PatchVerifyFailed,
                        "patch-verify", !rolledBack, null, rolledBack ? "rolled-back" : "rollback-failed");
                }

                try
                {
                    SubscribeNativeEvents();
                }
                catch (P2PGroupRegistrationException)
                {
                    // t18/F5：订阅失败不得留下已装 patch（半注册态），先回滚再以失败态抛出。
                    bool rolledBack = UnpatchOwned(harmony);
                    UnsubscribeNativeEvents();
                    throw RegistrationFailure(P2PGroupRegistrationFailureReason.LifecycleSubscribeFailed,
                        "lifecycle-subscribe", !rolledBack, null, rolledBack ? "rolled-back" : "rollback-failed");
                }

                if (!_levelSubscribed
                    || (!_providerSubscribed && !_providerSubscriptionUnavailable)
                    || (!_playerSubscribed && !_playerSubscriptionUnavailable))
                {
                    bool rolledBack = UnpatchOwned(harmony);
                    UnsubscribeNativeEvents();
                    throw RegistrationFailure(P2PGroupRegistrationFailureReason.LifecycleSubscribeFailed,
                        "lifecycle-subscribe", !rolledBack, null, rolledBack ? "rolled-back" : "rollback-failed");
                }

                _harmony = harmony;
                _registered = true;
                _registeredHarmonyId = harmony.Id;
                _registeredModuleMvid = moduleMvid;
                _registrationState = P2PGroupRegistrationState.Ready;
                RoleLogger.Info("[Shared]", LogTag
                    + " registered: defer local-host PlayerQuests.load until current GAME group table is ready");
            }
            catch (P2PGroupRegistrationException ex)
            {
                RecordRegistrationFailure(ex.Failure);
                throw;
            }
            catch (Exception ex)
            {
                P2PGroupRegistrationFailure failure = BuildFailure(
                    P2PGroupRegistrationFailureReason.LifecycleSubscribeFailed, "unexpected", false);
                RecordRegistrationFailure(failure);
                throw new P2PGroupRegistrationException(failure, LogTag + " registration failed unexpectedly", ex);
            }
        }

        private static bool VerifyCurrent(Harmony harmony)
        {
            return VerifyRegistered(harmony, LoadTargetForTests, GroupTableTargetForTests, SaveTargetForTests)
                && _levelSubscribed
                && _providerSubscribed
                && _playerSubscribed;
        }

        private static void RecordRegistrationFailure(P2PGroupRegistrationFailure failure)
        {
            _registrationState = P2PGroupRegistrationState.Failed;
            _lastFailure = failure;
            try
            {
                RoleLogger.Error("[Shared]", LogTag + " registration FAILED: feature=" + failure.Feature
                    + " attempt=" + failure.RegistrationAttempt + " owner=" + failure.OwnerId
                    + " reason=" + failure.Reason + " stage=" + failure.Stage
                    + " rollback=" + failure.RollbackStatus + " restartRequired=" + failure.RestartRequired);
            }
            catch
            {
                // 日志失败不得抹掉失败态（失败快照已先写入静态字段）。
            }
        }

        private static P2PGroupRegistrationException RegistrationFailure(
            P2PGroupRegistrationFailureReason reason, string stage, bool restartRequired, Exception inner,
            string rollbackStatus = "not-needed")
        {
            P2PGroupRegistrationFailure failure = BuildFailure(reason, stage, restartRequired);
            failure.RollbackStatus = rollbackStatus;
            return new P2PGroupRegistrationException(failure,
                LogTag + " registration failed: " + reason + " at " + stage, inner);
        }

        private static P2PGroupRegistrationFailure BuildFailure(
            P2PGroupRegistrationFailureReason reason, string stage, bool restartRequired)
        {
            return new P2PGroupRegistrationFailure
            {
                Feature = "P2PGroupRecovery",
                RegistrationAttempt = _registrationAttempt,
                OwnerId = _registeredHarmonyId ?? string.Empty,
                ModuleMvid = typeof(P2PGroupStateRecoveryPatch).Assembly.ManifestModule.ModuleVersionId.ToString(),
                Original = LoadTargetForTests != null ? LoadTargetForTests.DeclaringType.Name + "." + LoadTargetForTests.Name : string.Empty,
                PatchMethod = LoadPrefixMethod != null ? LoadPrefixMethod.Name : string.Empty,
                Stage = stage,
                Reason = reason,
                RollbackStatus = "not-needed",
                RestartRequired = restartRequired
            };
        }

        /// <summary>只撤销本模块自己的两个补丁；绝不 UnpatchSelf 共享 Harmony 实例。</summary>
        private static bool UnpatchOwned(Harmony harmony)
        {
            if (harmony == null)
                return false;
            try
            {
                if (LoadTargetForTests != null && LoadPrefixMethod != null)
                    harmony.Unpatch(LoadTargetForTests, LoadPrefixMethod);
                if (GroupTableTargetForTests != null && GroupTablePostfixMethod != null)
                    harmony.Unpatch(GroupTableTargetForTests, GroupTablePostfixMethod);
                if (SaveTargetForTests != null && SavePrefixMethod != null)
                    harmony.Unpatch(SaveTargetForTests, SavePrefixMethod);
                return true;
            }
            catch (Exception ex)
            {
                RoleLogger.Error("[Shared]", LogTag + " bounded unpatch failed: " + ex);
                return false;
            }
        }

        /// <summary>
        /// 生产登记自检：owner、original（GetPatchInfo 的键）、PatchMethod 三者必须与本次登记一致。
        /// </summary>
        internal static bool VerifyRegistered(Harmony harmony, MethodInfo load, MethodInfo groupTable, MethodInfo save)
        {
            if (harmony == null || load == null || groupTable == null || save == null)
                return false;
            if (load.DeclaringType != typeof(PlayerQuests)
                || groupTable.DeclaringType != typeof(GroupManager)
                || save.DeclaringType != typeof(PlayerQuests))
                return false;
            return HasPatch(harmony.Id, load, LoadPrefixMethod, true)
                && HasPatch(harmony.Id, groupTable, GroupTablePostfixMethod, false)
                && HasPatch(harmony.Id, save, SavePrefixMethod, true);
        }

        private static bool HasPatch(string harmonyId, MethodInfo original, MethodInfo patchMethod, bool prefix)
        {
            if (string.IsNullOrEmpty(harmonyId) || original == null || patchMethod == null)
                return false;
            HarmonyLib.Patches info = Harmony.GetPatchInfo(original);
            if (info == null)
                return false;
            var patches = prefix ? info.Prefixes : info.Postfixes;
            if (patches == null)
                return false;
            foreach (Patch patch in patches)
            {
                if (patch != null && patch.owner == harmonyId && patch.PatchMethod == patchMethod)
                    return true;
            }
            return false;
        }

        private static void SubscribeNativeEvents()
        {
            // 分三段、各自捕获：类型初始化异常可能在 JIT 该方法时抛出，因此每段都放进
            // 独立 NoInlining 方法，让 catch 落在方法边界内。
            try
            {
                SubscribeLevelEvents();
                _levelSubscribed = true;
            }
            catch (Exception ex)
            {
                _levelSubscribed = false;
                throw RegistrationFailure(P2PGroupRegistrationFailureReason.LifecycleSubscribeFailed,
                    "level-subscribe", false, ex);
            }

            try
            {
                SubscribeProviderEvents();
                _providerSubscribed = true;
            }
            catch (Exception ex)
            {
                _providerSubscribed = false;
                if (IsEnvironmentTypeLoadFailure(ex))
                {
                    _providerSubscriptionUnavailable = true;
                    LogSubscribeFailure("Provider", ex);
                }
                else
                {
                    throw RegistrationFailure(P2PGroupRegistrationFailureReason.LifecycleSubscribeFailed,
                        "provider-subscribe", false, ex);
                }
            }

            try
            {
                SubscribePlayerEvents();
                _playerSubscribed = true;
            }
            catch (Exception ex)
            {
                _playerSubscribed = false;
                if (IsEnvironmentTypeLoadFailure(ex))
                {
                    // 控制台/无 Unity 运行时：Player 静态 cctor 无法初始化属环境不可用；
                    // Unity 内（生产）必须成功，否则按登记失败处理。
                    _playerSubscriptionUnavailable = true;
                    LogSubscribeFailure("Player", ex);
                }
                else
                {
                    throw RegistrationFailure(P2PGroupRegistrationFailureReason.LifecycleSubscribeFailed,
                        "player-subscribe", false, ex);
                }
            }
        }

        /// <summary>控制台测试进程缺少 Unity 运行时时，原生静态 cctor 会抛类型初始化异常。</summary>
        private static bool IsEnvironmentTypeLoadFailure(Exception ex)
        {
            for (Exception current = ex; current != null; current = current.InnerException)
            {
                if (current is TypeInitializationException)
                    return true;
                if (current is ReflectionTypeLoadException)
                    return true;
            }
            return false;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void SubscribeLevelEvents()
        {
            // 先减后加：重复登记（不同 Harmony 实例/热重载）也只保留一份订阅。
            Level.onLevelLoaded -= OnLevelLoaded;
            Level.onLevelLoaded += OnLevelLoaded;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void SubscribePlayerEvents()
        {
            Player.onPlayerDestroyed -= OnPlayerDestroyed;
            Player.onPlayerDestroyed += OnPlayerDestroyed;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void SubscribeProviderEvents()
        {
            Provider.onServerShutdown -= OnServerShutdown;
            Provider.onServerShutdown += OnServerShutdown;
        }

        private static void LogSubscribeFailure(string family, Exception ex)
        {
            RoleLogger.Error("[Shared]", LogTag + " native event subscription failed (" + family + "): " + ex);
        }

        private static void OnLevelLoaded(int level)
        {
            // 回 MENU/SETUP 即会话边界：清空 pending/ready 并递增 epoch，旧局值不得进入新局。
            if (level <= Level.BUILD_INDEX_MENU)
                ResetBoundary("menu-level-loaded:" + level);
        }

        private static void OnServerShutdown()
        {
            ResetBoundary("server-shutdown");
        }

        private static void OnPlayerDestroyed(Player player)
        {
            try
            {
                PlayerQuests quests = player != null ? player.quests : null;
                if (ReferenceEquals(quests, null))
                    return;
                long token = TokenOf(quests);
                // T14/E2：非短路 |=，两条件都为真时 Core.OnPlayerDestroyed 也必须被调用，
                // 否则 _destroyedPending 永不增长、账本与用例脱耦。
                bool wasPending = ReferenceEquals(quests, _pendingQuests);
                wasPending |= Core.OnPlayerDestroyed(token);
                if (ReferenceEquals(quests, _notifyTarget))
                    _notifyTarget = null;
                if (!wasPending)
                    return;
                _pendingQuests = null;
                RoleLogger.Info("[Shared]", LogTag
                    + " pending load dropped: local player destroyed before group table ready");
            }
            catch (Exception ex)
            {
                RoleLogger.Error("[Shared]", LogTag + " OnPlayerDestroyed failed: " + ex);
            }
        }

        private static void ResetBoundary(string reason)
        {
            bool hadPending = Core.HasPending;
            Core.ResetSession(reason);
            _pendingQuests = null;
            if (hadPending)
                RoleLogger.Info("[Shared]", LogTag + " session boundary cleared pending: " + reason);
        }

        /// <summary>
        /// PlayerQuests.load 前缀：合格本地 Host 的首次加载且当前 epoch 组表未完成 → 阻止本次执行并排队。
        /// 其余情况（Guest/单机/dedicated/组表已就绪/正在执行被授权的 load）一律放行。
        /// </summary>
        public static bool Prefix_Load(PlayerQuests __instance)
        {
            bool committed = false;
            try
            {
                // T14/E4：受控 flags 通知期间，回调若重入 load，必须记录并拒绝，
                // 不得复用 ExecutingDeferredLoad 让重入者获准入。
                // 注意：这里用 ReferenceEquals 而不是 Unity 重载的 ==。Unity 假 null 会把
                // 尚未绑定原生实例的对象当作空，而本方法只做状态机判定；真实销毁由
                // Player.onPlayerDestroyed 订阅负责清账。
                if (Core.Notifying && !ReferenceEquals(__instance, null) && ReferenceEquals(__instance, _notifyTarget))
                {
                    // T18/F6：拒绝必须留可观测证据（token/地图），且仅当能确认是同一 _notifyTarget 时才拒绝；
                    // 若无法确认（目标已清空/不同实例），调用方继续走到下面的放行分支。
                    long reentryToken = TokenOf(__instance);
                    string reentryMap = CurrentSurvivalMapName();
                    Core.NoteReentryRejected();
                    LastReentryDetail = "token=" + reentryToken + " map=" + reentryMap;
                    try
                    {
                        RoleLogger.Warn("[Shared]", LogTag + " reentrant PlayerQuests.load rejected during flags notify (map="
                            + reentryMap + " token=" + reentryToken + ")");
                    }
                    catch
                    {
                        // 日志失败不得改变拒绝结果。
                    }
                    return false;
                }
                if (Core.ExecutingDeferredLoad || ReferenceEquals(__instance, null))
                    return true;

                GroupRecoveryEligibility eligibility = EligibilityResolverForTests != null
                    ? EligibilityResolverForTests(__instance)
                    : BuildEligibility(__instance);
                if (!eligibility.IsEligible)
                {
                    // T31：不合格也必须留一条门控证据（含 12 项布尔），否则线上无法区分
                    // "判定为不合格"与"判定路径被异常打断"。
                    LogGate(eligibility, 0L, string.Empty, "NotEligible");
                    return true;
                }

                long token = EnsureCurrentToken(__instance);
                string mapName = CurrentSurvivalMapName();
                GroupLoadDecision decision = GroupLoadPlanner.Decide(eligibility, mapName, token, Core);
                LogGate(eligibility, token, mapName,
                    decision == GroupLoadDecision.Deferred ? "Deferred" : "PassThrough");
                if (decision == GroupLoadDecision.PassThrough)
                    return true;
                // T24/F2：不存在"未 Ready 强制放行"的旁路——Decide 只会给出 PassThrough/Deferred。

                // 先提交 pending，再做任何可能抛异常的工作（测试 seam / 日志）。
                // 若此处抛出，catch 必须回滚账，否则 Postfix 仍会 Authorized → 二次原生 load。
                _pendingQuests = __instance;
                committed = true;
                // T24/F1：活对象从此进入"受控且未完成"状态——原生 load 未成功前禁止落盘。
                MarkInstanceManaged(__instance, token, mapName);
                System.Action observer = CommitObserverForTests;
                if (observer != null)
                    observer();
                RoleLogger.Info("[Shared]", LogTag + " deferred PlayerQuests.load (map=" + mapName
                    + " epoch=" + Core.Epoch + " token=" + token + ")");
                return false;
            }
            catch (Exception ex)
            {
                // T24/F1：失败方向是"拒绝本次 load 并阻塞保存"，绝不落到"原生 load 用默认值成功 + save 落盘"。
                // 仅在确认是本机 Host 上下文时接管（Guest/SP/dedicated 仍走原生行为）。
                bool hostContext = false;
                try
                {
                    hostContext = HostManager.IsP2PHostMode && Provider.isServer && !Dedicator.IsDedicatedServer;
                }
                catch
                {
                    // 探测失败按非 Host 处理，保持原生路径。
                }
                if (committed)
                {
                    Core.AbandonPending("prefix-failed");
                    _pendingQuests = null;
                }
                Core.NotePrefixRollback();
                _lastPrefixFailure = ex.ToString();
                if (committed || hostContext)
                {
                    MarkInstanceBlocked(__instance, "prefix-failed");
                    try
                    {
                        RoleLogger.Error("[Shared]", LogTag
                            + " Prefix_Load failed; saving blocked for this instance (fail-closed): " + ex);
                    }
                    catch
                    {
                        // 日志失败不得改变失败方向。
                    }
                    return false;
                }
                try
                {
                    RoleLogger.Error("[Shared]", LogTag + " Prefix_Load failed; falling back to native load: " + ex);
                }
                catch
                {
                    // 日志失败不得改变回滚结果。
                }
                return true;
            }
        }

        /// <summary>
        /// T24/F1：save 前缀 = 写入安全门。受控实例（本 key defer 过）在原生 load 正常返回前
        /// 一律拒绝保存；pending 到达 save 截止点时先尝试一次兑现（仍需 Ready 门），失败同样拒绝。
        /// 失败方向：宁可本局不落盘并显式 Degraded，也绝不用默认值/空表覆盖 Quests.dat。
        /// </summary>
        public static bool Prefix_Save(PlayerQuests __instance)
        {
            try
            {
                if (ReferenceEquals(__instance, null))
                    return true;
                // 受控 load / flags 通知重入期间：未 Complete，禁止落盘。
                if (Core.ExecutingDeferredLoad || Core.Notifying)
                    return false;

                WriteSafetySlot slot = InstanceSlot(__instance);
                // T24/F1：只要活对象仍带着"受控且未完成"证明就必须拦住保存——包括跨 epoch 复用的旧脏对象；
                // 该屏障只能由"本对象一次成功的新 load"解除（MarkInstanceComplete），ResetSession 不擦除。
                if (slot != null && slot.Managed && !slot.NativeLoadCompleted)
                {
                    if (Core.HasPending && ReferenceEquals(__instance, _pendingQuests))
                        TryFlushAtSaveDeadline(__instance, slot);
                    if (slot.Managed && !slot.NativeLoadCompleted)
                    {
                        slot.BlockReason = "save-blocked:not-loaded";
                        Core.NoteSaveBlockedDeadline("save-blocked:not-loaded");
                        try
                        {
                            RoleLogger.Warn("[Shared]", LogTag
                                + " save blocked: deferred native load has not completed (map="
                                + slot.MapName + " token=" + slot.Token + ")");
                        }
                        catch
                        {
                            // 日志失败不得放行保存。
                        }
                        return false;
                    }
                    return true;
                }

                if (Core.HasPending && ReferenceEquals(__instance, _pendingQuests))
                {
                    DeferredLoadOutcome outcome = TryFlushAtSaveDeadline(__instance, slot);
                    if (outcome != DeferredLoadOutcome.Authorized)
                        return false;
                    WriteSafetySlot after = InstanceSlot(__instance);
                    return after != null && after.NativeLoadCompleted;
                }
                return true;
            }
            catch (Exception ex)
            {
                try
                {
                    RoleLogger.Error("[Shared]", LogTag + " Prefix_Save blocked on failure (fail-closed): " + ex);
                }
                catch
                {
                    // 日志失败仍按拒绝保存处理。
                }
                return false;
            }
        }

        /// <summary>
        /// GroupManager.onLevelLoaded 后缀：MENU 不处理；GAME（及以后）先登记"当前 epoch 组表已就绪"，
        /// 再尝试兑现一次 pending。原生组表重建与 load 已在本方法之前同步完成。
        /// </summary>
        public static void Postfix_GroupTableLoaded(int level)
        {
            try
            {
                if (level <= Level.BUILD_INDEX_MENU)
                    return;
                if (!HostManager.IsP2PHostMode || !Provider.isServer || Dedicator.IsDedicatedServer)
                    return;

                string mapName = CurrentSurvivalMapName();
                if (string.IsNullOrEmpty(mapName))
                    return;

                Core.NoteGroupTableLoaded(mapName);
                DeferredLoadOutcome outcome = Core.TryTakeDeferredLoad(mapName, out long token);
                if (outcome == DeferredLoadOutcome.Stale)
                {
                    _pendingQuests = null;
                    RoleLogger.Warn("[Shared]", LogTag + " stale pending discarded at GAME group table (map="
                        + mapName + " epoch=" + Core.Epoch + ")");
                    return;
                }
                if (outcome == DeferredLoadOutcome.NotReady)
                {
                    Core.NoteNotReadyRejection("not-ready-at-postfix");
                    RoleLogger.Warn("[Shared]", LogTag + " deferred load not authorized: group table not ready (map="
                        + mapName + " epoch=" + Core.Epoch + ")");
                    return;
                }
                if (outcome != DeferredLoadOutcome.Authorized)
                    return;

                ExecuteDeferredLoad(token, mapName);
            }
            catch (Exception ex)
            {
                RoleLogger.Error("[Shared]", LogTag + " Postfix_GroupTableLoaded failed: " + ex);
            }
        }

        private static bool ExecuteDeferredLoad(long token, string mapName)
        {
            PlayerQuests quests = _pendingQuests;
            _pendingQuests = null;
            if (ReferenceEquals(quests, null))
            {
                Core.AbandonPending("instance-missing");
                RoleLogger.Warn("[Shared]", LogTag + " deferred load dropped: player instance unavailable (map="
                    + mapName + " token=" + token + ")");
                return false;
            }
            long actualToken = TokenOf(quests);
            if (actualToken != token)
            {
                Core.AbandonPending("token-mismatch");
                RoleLogger.Warn("[Shared]", LogTag + " deferred load dropped: player token mismatch (expected="
                    + token + " actual=" + actualToken + ")");
                return false;
            }
            if (!Core.IsLeaseValid(token, mapName))
            {
                Core.AbandonPending("lease-changed");
                RoleLogger.Warn("[Shared]", LogTag + " deferred load dropped: lease changed before load (map="
                    + mapName + " token=" + token + ")");
                return false;
            }
            // T24/F2：真正调原生 load 前再次核实组表就绪；未就绪绝不执行（否则会把 Nil/MEMBER 读进内存）。
            if (!Core.IsGroupsReadyFor(mapName) || !NativeGroupTablePresent())
            {
                Core.NoteNotReadyRejection("not-ready-at-execute");
                Core.AbandonPending("not-ready-at-execute");
                RoleLogger.Warn("[Shared]", LogTag + " deferred load refused: group table not ready (map="
                    + mapName + " token=" + token + ")");
                return false;
            }

            Core.NoteDeferredLoadEntered();
            try
            {
                // T18 测试 seam：控制台无 Unity 运行时，无法真正读 Quests.dat；
                // 生产路径始终为 null，走原生 quests.load()。
                System.Action<PlayerQuests> invoker = DeferredLoadInvokerForTests;
                if (invoker != null)
                    invoker(quests);
                else
                    quests.load();
                // 只有正常返回才算 LoadSucceeded；wasLoadCalled 原生首行就置 true，不能当成功证据。
                Core.NoteDeferredLoadSucceeded();
                // T24/F1：在通知/日志之前提交写入安全证明——正常返回到达此处即"本 key 已可信"。
                MarkInstanceComplete(quests, token, mapName);
                try
                {
                    RoleLogger.Info("[Shared]", LogTag + " deferred native Quests.load executed once (map=" + mapName
                        + " epoch=" + Core.Epoch + " token=" + token + ")");
                }
                catch
                {
                    // 日志失败不得撤销成功证明。
                }
            }
            catch (Exception ex)
            {
                Core.NoteDeferredLoadFailed("native-load-threw");
                try
                {
                    RoleLogger.Error("[Shared]", LogTag + " deferred native Quests.load threw; not retrying: " + ex);
                }
                catch
                {
                    // 失败态已写入账本。
                }
                return false;
            }
            finally
            {
                Core.NoteDeferredExecutionExited();
            }

            if (!Core.IsLeaseValid(token, mapName))
            {
                Core.AbandonPending("lease-changed-after-load");
                return true;
            }

            // T12/E4：先补发本机 flags 就绪，再走原生组状态发布，让组监听者看到已就绪的 flags。
            NotifyFlagsUpdated(quests, token, mapName);
            PublishNativeGroupState(quests, mapName);
            return true;
        }

        private static void NotifyFlagsUpdated(PlayerQuests quests, long token, string mapName)
        {
            try
            {
                bool payloadNonEmpty = HasRecoveredPayload(quests);
                FlagsUpdated handler = quests.onFlagsUpdated;
                _notifyTarget = quests;
                TryNotifyFlagsCore(payloadNonEmpty, handler, Core.IsLeaseValid(token, mapName), token, mapName);
            }
            catch (Exception ex)
            {
                Core.NoteNotifyFailed("notify-harness-failed");
                try
                {
                    RoleLogger.Error("[Shared]", LogTag + " flags notification failed: " + ex);
                }
                catch
                {
                    // 失败态已写入账本。
                }
            }
            finally
            {
                _notifyTarget = null;
            }
        }

        /// <summary>
        /// T14/E4 受控补发：最多一次，仅在同 epoch/地图/实例、真实 load 成功且恢复 payload 非空时；
        /// 复用当时的委托快照，正常 multicast 调用一次；失败记为 NotificationFailed，不重试、不整批重发。
        /// </summary>
        internal static bool TryNotifyFlagsCore(
            bool payloadNonEmpty,
            FlagsUpdated handler,
            bool leaseValid,
            long token,
            string mapName)
        {
            if (!leaseValid)
            {
                Core.AbandonPending("notify-lease-changed");
                return false;
            }
            if (!payloadNonEmpty || handler == null)
            {
                Core.NoteNotifySkippedEmpty();
                return false;
            }
            if (Core.Ledger.NotifyAttempts > 0)
            {
                Core.NoteDuplicateNotifySuppressed();
                return false;
            }
            if (!Core.Ledger.CanNotify())
            {
                Core.NoteInvariantViolation("notify-before-load-success");
                return false;
            }

            Core.NoteNotifyAttempted();
            // T24/t22 回退：委托快照只做一次普通 multicast 调用；任一订阅者抛错即中断后续，
            // 外层记 NotificationFailed，不重试、不整批重发、不做逐订阅者补偿。
            try
            {
                handler();
            }
            catch (Exception ex)
            {
                Core.NoteNotifyFailed("notification-threw");
                try
                {
                    RoleLogger.Error("[Shared]", LogTag + " flags notification threw; no retry (map=" + mapName
                        + " token=" + token + "): " + ex);
                }
                catch
                {
                    // 失败态已写入账本。
                }
                return false;
            }

            Core.NoteNotifySucceeded();
            try
            {
                RoleLogger.Info("[Shared]", LogTag + " flags notification dispatched once (map=" + mapName
                    + " token=" + token + ")");
            }
            catch
            {
                // 日志失败不得改变已成功的通知结果。
            }
            return true;
        }

        private static bool HasRecoveredPayload(PlayerQuests quests)
        {
            if (ReferenceEquals(quests, null))
                return false;
            var flags = quests.flagsList;
            var questsList = quests.questsList;
            return (flags != null && flags.Count > 0) || (questsList != null && questsList.Count > 0);
        }

        internal static bool HasRecoveredPayloadForTests(PlayerQuests quests) => HasRecoveredPayload(quests);

        internal static void PublishNativeGroupStateForTests(PlayerQuests quests, string mapName)
            => PublishNativeGroupState(quests, mapName);

        /// <summary>
        /// deferred load 已恢复 groupID/groupRank，但原生 load 不触发组事件；早先发出的 none 本机投影
        /// 需要一次原生回环来纠正。changeRank 走原生 SendGroupState.InvokeAndLoopback，最终
        /// ReceiveGroupState 触发 groupIDChanged/triggerGroupUpdated，且不增加 members。
        /// 空存档/无合法组（none/MEMBER）保持原生默认，不发布、不猜默认队伍。
        /// </summary>
        private static void PublishNativeGroupState(PlayerQuests quests, string mapName)
        {
            try
            {
                if (!quests.isMemberOfAGroup)
                    return;
                CSteamID groupId = quests.groupID;
                EPlayerGroupRank rank = quests.groupRank;
                System.Action<PlayerQuests, EPlayerGroupRank> publishOverride = ChangeRankActionForTests;
                if (publishOverride != null)
                    publishOverride(quests, rank);
                else
                    quests.changeRank(rank);
                RoleLogger.Info("[Shared]", LogTag + " native group state published after restore (map=" + mapName
                    + " group=" + Mask(groupId) + " rank=" + rank + ")");
            }
            catch (Exception ex)
            {
                RoleLogger.Error("[Shared]", LogTag + " PublishNativeGroupState failed: " + ex);
            }
        }

        /// <summary>恢复门控：只有"P2P host + server + 非 dedicated + 唯一真实 local host"才允许延后。</summary>
        internal static GroupRecoveryEligibility BuildEligibility(PlayerQuests quests)
        {
            if (ReferenceEquals(quests, null))
                return default(GroupRecoveryEligibility);

            var facts = new GroupRecoverySessionFacts();
            facts.IsP2PHostMode = HostManager.IsP2PHostMode;
            facts.IsServer = Provider.isServer;
            facts.IsDedicatedServer = Dedicator.IsDedicatedServer;

            Player player = quests.player;
            facts.PlayerIsLocalInstance = player != null && ReferenceEquals(player, Player.LocalPlayer);

            SteamChannel channel = quests.channel;
            facts.ChannelIsLocalPlayer = channel != null && channel.IsLocalPlayer;
            SteamPlayer owner = channel != null ? channel.owner : null;
            facts.HasOwningSteamPlayer = HasBoundPlayerId(owner);
            facts.HasLoopbackConnection = !ReferenceEquals(owner, null)
                && owner.transportConnection is TransportConnection_Loopback;
            facts.OwnerSteamId = BoundSteamIdOf(owner);
            facts.ProviderUserId = Provider.user.m_SteamID;
            // T18/F7：映射与判定分离；三类拒绝（owner.playerID null / 非 loopback / 计数≠1）
            // 在纯数据层即可被用例覆盖，不依赖 SDK。
            facts.MatchingLocalClientCount = facts.HasOwningSteamPlayer
                ? CountClientsWithSteamId(facts.OwnerSteamId)
                : 0;

            ModeConfigData config = Provider.modeConfigData;
            facts.DynamicGroupsAllowed = config != null
                && config.Gameplay != null
                && config.Gameplay.Allow_Dynamic_Groups;
            return facts.Evaluate();
        }

        private static int CountClientsWithSteamId(ulong steamId)
        {
            if (steamId == 0UL)
                return 0;
            var clients = Provider.clients;
            if (clients == null)
                return 0;
            int count = 0;
            for (int index = 0; index < clients.Count; index++)
            {
                SteamPlayer client = clients[index];
                if (!HasBoundPlayerId(client))
                    continue;
                if (client.playerID.steamID.m_SteamID == steamId)
                    count++;
            }
            return count;
        }

        /// <summary>
        /// Bug3 根因（T31）：owner 侧 SteamPlayerID 是否已绑定。
        /// SDG.Unturned.SteamPlayerID 重载了 ==/!=，运算符实现直接取 playerID_0.steamID，
        /// 对 null 调用即抛 NullReferenceException（主机本地玩家早期初始化时 playerID 为 null）。
        /// 判空必须发生在调用运算符之前，只能用 ReferenceEquals，禁止 try/catch 吞异常。
        /// </summary>
        internal static bool HasBoundPlayerId(SteamPlayer owner)
            => !ReferenceEquals(owner, null) && !ReferenceEquals(owner.playerID, null);

        /// <summary>owner 的 SteamID；playerID 未绑定时为 0（全程不调用 SDK 运算符）。</summary>
        internal static ulong BoundSteamIdOf(SteamPlayer owner)
            => HasBoundPlayerId(owner) ? owner.playerID.steamID.m_SteamID : 0UL;

        /// <summary>
        /// T31 门控日志（纯格式化，便于用例断言）：12 项布尔 + token/epoch/map + decision。
        /// decision 取 Deferred / PassThrough / NotEligible；该路径只在 PlayerQuests.load 前缀
        /// 触发，不在逐帧 Update 中调用，因此每次判定一条 Info 不会逐帧刷屏。
        /// </summary>
        internal static string FormatGateLine(
            GroupRecoveryEligibility eligibility, long token, long epoch, string mapName, string decision)
        {
            return "gate"
                + " p2pHost=" + Flag(eligibility.IsP2PHostMode)
                + " server=" + Flag(eligibility.IsServer)
                + " nonDedicated=" + Flag(!eligibility.IsDedicatedServer)
                + " localPlayer=" + Flag(eligibility.PlayerIsLocalInstance)
                + " localChannel=" + Flag(eligibility.ChannelIsLocalPlayer)
                + " owner=" + Flag(eligibility.HasOwningSteamPlayer)
                + " loopback=" + Flag(eligibility.HasLoopbackConnection)
                + " ownerSteamIdNonZero=" + Flag(eligibility.OwnerSteamId != 0UL)
                + " ownerIsProviderUser=" + Flag(eligibility.OwnerSteamId == eligibility.ProviderUserId)
                + " uniqueLocalClient=" + Flag(eligibility.MatchingLocalClientCount == 1)
                + " dynamicGroups=" + Flag(eligibility.DynamicGroupsAllowed)
                + " eligible=" + Flag(eligibility.IsEligible)
                + " | token=" + token
                + " epoch=" + epoch
                + " map=" + (mapName ?? string.Empty)
                + " | decision=" + (decision ?? string.Empty);
        }

        private static string Flag(bool value) => value ? "true" : "false";

        private static void LogGate(
            GroupRecoveryEligibility eligibility, long token, string mapName, string decision)
        {
            try
            {
                RoleLogger.Info("[Shared]",
                    LogTag + " " + FormatGateLine(eligibility, token, Core.Epoch, mapName, decision));
            }
            catch
            {
                // 日志失败不得改变判定结果；判空已在此之前完成，不依赖 catch 兜底。
            }
        }

        /// <summary>
        /// T24/F2：native 组表对象存在性——只读反射 GroupManager.knownGroups；null 视为未就绪，
        /// 非 null（含合法空表）视为就绪。反射不可用时按未就绪处理（失败方向：不 load）。
        /// </summary>
        private static bool NativeGroupTablePresent()
        {
            Func<bool> probe = NativeGroupTableProbeForTests;
            if (probe != null)
                return probe();
            try
            {
                FieldInfo field = _knownGroupsField;
                if (field == null)
                    return false;
                object value = field.GetValue(null);
                return value is IDictionary;
            }
            catch
            {
                return false;
            }
        }

        private static WriteSafetySlot InstanceSlot(PlayerQuests quests)
        {
            if (ReferenceEquals(quests, null))
                return null;
            WriteSafetySlot slot;
            return WriteSafety.TryGetValue(quests, out slot) ? slot : null;
        }

        private static void MarkInstanceManaged(PlayerQuests quests, long token, string mapName)
        {
            if (ReferenceEquals(quests, null))
                return;
            WriteSafetySlot slot = WriteSafety.GetOrCreateValue(quests);
            slot.Epoch = Core.Epoch;
            slot.Token = token;
            slot.MapName = mapName ?? string.Empty;
            slot.Managed = true;
            slot.NativeLoadCompleted = false;
            slot.BlockReason = "pending-native-load";
        }

        private static void MarkInstanceBlocked(PlayerQuests quests, string reason)
        {
            if (ReferenceEquals(quests, null))
                return;
            WriteSafetySlot slot = WriteSafety.GetOrCreateValue(quests);
            slot.Managed = true;
            slot.NativeLoadCompleted = false;
            slot.BlockReason = reason;
        }

        private static void MarkInstanceComplete(PlayerQuests quests, long token, string mapName)
        {
            WriteSafetySlot slot = InstanceSlot(quests);
            if (slot == null)
                return;
            // 租约已变（换局/换图）不得把旧租约的"成功"写给新租约。
            if (slot.Token != token || slot.Epoch != Core.Epoch
                || !string.Equals(slot.MapName, mapName ?? string.Empty, StringComparison.Ordinal))
                return;
            slot.Managed = true;
            slot.NativeLoadCompleted = true;
            slot.BlockReason = "complete";
        }

        private static bool IsSaveBlocked(PlayerQuests quests)
        {
            WriteSafetySlot slot = InstanceSlot(quests);
            return slot != null && slot.Managed && !slot.NativeLoadCompleted;
        }

        private static bool IsNativeLoadCompleted(PlayerQuests quests)
        {
            WriteSafetySlot slot = InstanceSlot(quests);
            return slot != null && slot.NativeLoadCompleted;
        }

        /// <summary>
        /// T24/F1：同实例跨 epoch 复用时轮换 token，旧回调（持旧 token）无法命中新租约。
        /// </summary>
        private static long EnsureCurrentToken(PlayerQuests quests)
        {
            long token = TokenOf(quests);
            WriteSafetySlot slot = InstanceSlot(quests);
            if (slot == null || slot.Token == 0 || slot.Epoch == Core.Epoch)
                return token;

            token = ++_nextPlayerToken;
            PlayerTokens.Remove(quests);
            PlayerTokens.Add(quests, new PlayerToken { Value = token });
            slot.Token = token;
            slot.Epoch = Core.Epoch;
            slot.NativeLoadCompleted = false;
            return token;
        }

        /// <summary>
        /// T24/F1：save 截止点兑现一次。授权成功才执行；未就绪/失效一律以 SaveBlocked 收口，不复活 pending。
        /// 返回真实授权结果（Authorized 表示"已尝试执行"，执行是否成功看写入安全证明）。
        /// </summary>
        private static DeferredLoadOutcome TryFlushAtSaveDeadline(PlayerQuests quests, WriteSafetySlot slot)
        {
            string mapName = Core.PendingMapName;
            long token = Core.PendingPlayerToken;
            long ageMs = Core.PendingAgeMs(GroupStateRecoveryCore.MonotonicNowMs());
            // T24/F2：native knownGroups 为 null（非 IDictionary）时，即便 epoch/map 就绪也不得授权。
            if (!NativeGroupTablePresent())
            {
                Core.NoteSaveBlockedDeadline("save-blocked:no-group-table");
                return DeferredLoadOutcome.NotReady;
            }
            DeferredLoadOutcome outcome = Core.TryTakeDeferredLoad(mapName, out long authorizedToken);
            if (outcome != DeferredLoadOutcome.Authorized)
            {
                Core.NoteSaveBlockedDeadline("save-blocked:" + outcome.ToString().ToLowerInvariant());
                try
                {
                    RoleLogger.Warn("[Shared]", LogTag + " save deadline refused to load: group table not ready or "
                        + "lease invalid (outcome=" + outcome + " map=" + mapName + " token=" + token
                        + " ageMs=" + ageMs + ")");
                }
                catch
                {
                    // 日志失败不得放行保存。
                }
                return outcome;
            }

            Core.NoteWatchdogForcedRelease("watchdog:save-prefix");
            try
            {
                RoleLogger.Warn("[Shared]", LogTag
                    + " pending load reached save deadline; executing deferred native load once (map="
                    + mapName + " token=" + token + " ageMs=" + ageMs + ")");
            }
            catch
            {
                // 日志失败不得阻止兑现。
            }
            ExecuteDeferredLoad(authorizedToken, mapName);
            return outcome;
        }

        /// <summary>只有 SURVIVAL 才有 Quests.dat 队伍语义；其它关卡类型不延后、不干预。</summary>
        private static string CurrentSurvivalMapName()
        {
            Func<string> resolver = MapNameResolverForTests;
            if (resolver != null)
                return resolver() ?? string.Empty;

            LevelInfo info = Level.info;
            if (info == null || info.type != ELevelType.SURVIVAL)
                return string.Empty;
            return string.IsNullOrEmpty(info.name) ? string.Empty : info.name;
        }

        /// <summary>
        /// T14/E5 测试隔离：撤销本模块补丁与订阅、清空静态状态；仅供控制台证据使用，
        /// 生产路径不调用（生产登记只发生一次）。
        /// </summary>
        /// <summary>T18/F5：有界撤销本模块的三条生命周期订阅，供失败回滚与测试复位共用。</summary>
        private static void UnsubscribeNativeEvents()
        {
            try
            {
                Level.onLevelLoaded -= OnLevelLoaded;
                Player.onPlayerDestroyed -= OnPlayerDestroyed;
                Provider.onServerShutdown -= OnServerShutdown;
            }
            catch
            {
                // 控制台环境类型不可用时忽略。
            }
            _levelSubscribed = false;
            _providerSubscribed = false;
            _playerSubscribed = false;
        }

        internal static void ResetForTests()
        {
            UnpatchOwned(_harmony);
            UnsubscribeNativeEvents();

            _harmony = null;
            _registered = false;
            _registeredHarmonyId = null;
            _registeredModuleMvid = Guid.Empty;
            _registrationState = P2PGroupRegistrationState.Unregistered;
            _lastFailure = default(P2PGroupRegistrationFailure);
            _levelSubscribed = false;
            _providerSubscribed = false;
            _playerSubscribed = false;
            _playerSubscriptionUnavailable = false;
            _providerSubscriptionUnavailable = false;
            _pendingQuests = null;
            _notifyTarget = null;
            _lastPrefixFailure = string.Empty;
            LastReentryDetail = string.Empty;
            DeferredLoadInvokerForTests = null;
            ChangeRankActionForTests = null;
            SaveTargetForTests = null;
            NativeGroupTableProbeForTests = null;
            EligibilityResolverForTests = null;
            MapNameResolverForTests = null;
            CommitObserverForTests = null;
            // 写入安全证明按实例存在；测试复位重建表，避免跨用例串味。
            WriteSafety = new ConditionalWeakTable<PlayerQuests, WriteSafetySlot>();
            Core.ResetSession("test-reset");
            Core.ResetCountersForTests();
        }

        private static long TokenOf(PlayerQuests quests)
        {
            // ReferenceEquals：避免 Unity 假 null 让"未绑定原生实例"的替身拿到 token=0。
            if (ReferenceEquals(quests, null))
                return 0L;
            PlayerToken token = PlayerTokens.GetValue(quests, _ => new PlayerToken { Value = ++_nextPlayerToken });
            return token.Value;
        }

        private static string Mask(CSteamID id)
        {
            return "..." + (id.m_SteamID % 10000UL);
        }
    }
}





