using System;

namespace SteamP2PFriends.Core.Patches.P2PGroupStateRecovery
{
    /// <summary>本次 Quests.load 请求的处理结果。</summary>
    internal enum GroupLoadDecision
    {
        /// <summary>组表已就绪（或输入不满足延后条件），按原生顺序立即执行。</summary>
        PassThrough = 0,

        /// <summary>已排队；本次 load 被阻止，等待当前 epoch 的 GAME 组表加载完成。</summary>
        Deferred = 1
    }

    /// <summary>延后 load 的兑现结果。</summary>
    internal enum DeferredLoadOutcome
    {
        /// <summary>当前没有 pending。</summary>
        None = 0,

        /// <summary>epoch、地图、玩家实例全部匹配，允许执行一次原生 load。</summary>
        Authorized = 1,

        /// <summary>存在 pending 但租约已失效（换局/换图/换实例），已丢弃且不执行。</summary>
        Stale = 2,

        /// <summary>
        /// T24/F2：租约匹配但当前 epoch 的 GAME 组表尚未就绪——不授权、不清 pending、
        /// 不增加 load 授权。调用方必须按"拒绝"处理（save 场景下拦下本次保存）。
        /// </summary>
        NotReady = 3
    }

    /// <summary>
    /// 恢复门控输入（纯数据，便于在无 Unity 的测试进程内执行负控）。
    /// 全部成立才允许延后：P2P host + server + 非 dedicated + 唯一真实 local host
    /// （本地 Player 实例、channel.IsLocalPlayer、loopback、非零且与 Provider.user 一致的 SteamID）。
    /// </summary>
    internal struct GroupRecoveryEligibility
    {
        internal bool IsP2PHostMode;
        internal bool IsServer;
        internal bool IsDedicatedServer;
        internal bool PlayerIsLocalInstance;
        internal bool ChannelIsLocalPlayer;
        internal bool HasOwningSteamPlayer;
        internal bool HasLoopbackConnection;
        internal ulong OwnerSteamId;
        internal ulong ProviderUserId;
        internal int MatchingLocalClientCount;
        internal bool DynamicGroupsAllowed;

        internal bool IsEligible
        {
            get
            {
                return IsP2PHostMode
                    && IsServer
                    && !IsDedicatedServer
                    && PlayerIsLocalInstance
                    && ChannelIsLocalPlayer
                    && HasOwningSteamPlayer
                    && HasLoopbackConnection
                    && OwnerSteamId != 0UL
                    && OwnerSteamId == ProviderUserId
                    && MatchingLocalClientCount == 1
                    && DynamicGroupsAllowed;
            }
        }
    }

    /// <summary>
    /// T18/F7：Unity 对象→门控字段的映射被抽成纯数据，使 owner.playerID 缺失、
    /// 非 loopback、匹配客户端数不为 1 三类拒绝可在无 SDK 的进程里直接验证。
    /// </summary>
    internal struct GroupRecoverySessionFacts
    {
        internal bool IsP2PHostMode;
        internal bool IsServer;
        internal bool IsDedicatedServer;
        internal bool PlayerIsLocalInstance;
        internal bool ChannelIsLocalPlayer;
        internal bool HasOwningSteamPlayer;
        internal bool HasLoopbackConnection;
        internal ulong OwnerSteamId;
        internal ulong ProviderUserId;
        internal int MatchingLocalClientCount;
        internal bool DynamicGroupsAllowed;

        internal GroupRecoveryEligibility ToEligibility()
        {
            return new GroupRecoveryEligibility
            {
                IsP2PHostMode = IsP2PHostMode,
                IsServer = IsServer,
                IsDedicatedServer = IsDedicatedServer,
                PlayerIsLocalInstance = PlayerIsLocalInstance,
                ChannelIsLocalPlayer = ChannelIsLocalPlayer,
                HasOwningSteamPlayer = HasOwningSteamPlayer,
                HasLoopbackConnection = HasLoopbackConnection,
                OwnerSteamId = HasOwningSteamPlayer ? OwnerSteamId : 0UL,
                ProviderUserId = ProviderUserId,
                MatchingLocalClientCount = HasOwningSteamPlayer ? MatchingLocalClientCount : 0,
                DynamicGroupsAllowed = DynamicGroupsAllowed
            };
        }

        internal GroupRecoveryEligibility Evaluate()
        {
            return ToEligibility();
        }
    }

    /// <summary>
    /// 纯状态机（不引用 Unity 类型）。租约 = epoch + 地图 + 本地玩家实例 token。
    /// 只有同一租约下、当前 GAME 组表加载完成后才兑现一次 pending；
    /// 换局、换图、实例销毁、退服一律清空 pending，杜绝旧值带进新局。
    /// </summary>
    internal sealed class GroupStateRecoveryCore
    {
        private struct PendingLoad
        {
            internal bool Exists;
            internal long PlayerToken;
            internal string MapName;
            internal long Epoch;
        }

        private PendingLoad _pending;
        private long _epoch;
        private long _groupsReadyEpoch = -1L;
        private string _groupsReadyMap;

        private long _nativeLoadExecutions;
        private long _deferredRequests;
        private long _flushAuthorizations;
        private long _staleFlushRejections;
        private long _replacedPending;
        private long _boundaryResets;
        private long _retiredLeases;
        private long _lastRetiredToken;
        private long _lastRetiredEpoch;
        private long _abandonedPending;
        private long _destroyedPending;
        private long _unnamedPassThrough;
        private bool _executingDeferredLoad;
        private readonly GroupLeaseLedger _ledger = new GroupLeaseLedger();
        private long _prefixRollbacks;
        private long _reentryRejections;
        private long _invariantViolations;
        private long _duplicateNotifySuppressed;
        private string _lastInvariantViolation = string.Empty;
        private bool _notifying;
        private long _pendingRegisteredAtMs;
        private long _watchdogForcedReleases;
        private long _abandonNoops;
        // T24/F2：未就绪拒绝与重复请求的可观测计数；T24/F1：save 截止阻断计数。
        private long _deferredRepeatRequests;
        private long _deferredLoadNotReady;
        private long _saveBlockedDeadline;

        internal long Epoch => _epoch;
        internal bool Notifying => _notifying;
        internal GroupLeaseLedger Ledger => _ledger;
        internal long PrefixRollbacks => _prefixRollbacks;
        internal long ReentryRejections => _reentryRejections;
        internal long InvariantViolations => _invariantViolations;
        internal long DuplicateNotifySuppressed => _duplicateNotifySuppressed;
        internal string LastInvariantViolation => _lastInvariantViolation;
        internal bool HasPending => _pending.Exists;
        internal long PendingPlayerToken => _pending.Exists ? _pending.PlayerToken : 0L;
        internal string PendingMapName => _pending.Exists ? _pending.MapName : string.Empty;
        internal long NativeLoadExecutions => _nativeLoadExecutions;
        internal long DeferredRequests => _deferredRequests;
        internal long FlushAuthorizations => _flushAuthorizations;
        internal long StaleFlushRejections => _staleFlushRejections;
        internal long ReplacedPending => _replacedPending;
        internal long BoundaryResets => _boundaryResets;
        internal long RetiredLeases => _retiredLeases;
        internal long LastRetiredToken => _lastRetiredToken;
        internal long LastRetiredEpoch => _lastRetiredEpoch;
        internal long AbandonedPending => _abandonedPending;
        internal long DestroyedPending => _destroyedPending;
        internal long UnnamedPassThrough => _unnamedPassThrough;
        internal bool ExecutingDeferredLoad => _executingDeferredLoad;
        // T18/F1：pending 有界寿命与 save 前缀强制兑现的可观测计数。
        internal long WatchdogForcedReleases => _watchdogForcedReleases;
        internal long AbandonNoops => _abandonNoops;
        // T24/F2：未就绪的授权拒绝次数、同租约重复延后次数；T24/F1：save 截止阻断次数。
        internal long DeferredLoadNotReady => _deferredLoadNotReady;
        internal long DeferredRepeatRequests => _deferredRepeatRequests;
        internal long SaveBlockedDeadline => _saveBlockedDeadline;

        /// <summary>T18/F1：pending 已存活毫秒数（0 表示当前无 pending）。</summary>
        internal long PendingAgeMs(long nowMs)
        {
            return _pending.Exists && _pendingRegisteredAtMs > 0L
                ? Math.Max(0L, nowMs - _pendingRegisteredAtMs)
                : 0L;
        }

        /// <summary>
        /// Unity 的 mscorlib 没有 Environment.TickCount64；用无符号 TickCount 得到
        /// 约 49 天不回绕的单调毫秒数，足以度量一次 pending 的寿命。
        /// </summary>
        internal static long MonotonicNowMs()
        {
            return (long)(uint)Environment.TickCount;
        }

        /// <summary>最近一次会话边界/放弃原因（仅诊断）。</summary>
        internal string LastReason { get; private set; }

        /// <summary>会话边界（回 MENU / 退服 / 新局）：epoch 递增并清空 pending 与就绪标记。</summary>
        internal void ResetSession(string reason)
        {
            _epoch++;
            ClearPending();
            _groupsReadyEpoch = -1L;
            _groupsReadyMap = null;
            _boundaryResets++;
            // t18/F4：账本清零前先退役旧租约，保证"新 epoch 计数=0"可核验为
            // 新租约的 0，而不是"这行代码把旧账抹了所以看起来 0"。
            if (_ledger.Token != 0L)
            {
                _retiredLeases++;
                _lastRetiredToken = _ledger.Token;
                _lastRetiredEpoch = _ledger.Epoch;
            }
            _ledger.Reset();
            LastReason = reason;
        }

        /// <summary>当前 epoch 的 GAME 组表加载完成（含合法空表）。</summary>
        internal void NoteGroupTableLoaded(string mapName)
        {
            _groupsReadyEpoch = _epoch;
            _groupsReadyMap = mapName ?? string.Empty;
        }

        /// <summary>
        /// T24/F2：组表就绪的编译级谓词——当前 epoch、非空地图、本 epoch 已记录 GAME 完成。
        /// 不以成员数判断（合法空表也算就绪）；native 表对象是否存在由 glue 层另行核实。
        /// </summary>
        internal bool IsGroupsReadyFor(string mapName)
        {
            return !string.IsNullOrEmpty(mapName)
                && _groupsReadyEpoch == _epoch
                && string.Equals(_groupsReadyMap, mapName, StringComparison.Ordinal);
        }

        /// <summary>
        /// 原生 Quests.load 被触发。组表已就绪则放行；否则只记录这一个 pending 并阻止本次执行。
        /// 同一实例在同一地图重复请求是幂等的（不会二次排队）。
        /// </summary>
        internal GroupLoadDecision OnLoadRequested(long playerToken, string mapName)
        {
            if (string.IsNullOrEmpty(mapName) || playerToken <= 0L)
            {
                _unnamedPassThrough++;
                return GroupLoadDecision.PassThrough;
            }

            if (IsGroupsReadyFor(mapName))
            {
                _nativeLoadExecutions++;
                return GroupLoadDecision.PassThrough;
            }

            if (_pending.Exists)
            {
                bool sameLease = _pending.PlayerToken == playerToken
                    && _pending.Epoch == _epoch
                    && string.Equals(_pending.MapName, mapName, StringComparison.Ordinal);
                if (sameLease)
                {
                    // T24：同租约重复请求（组表仍未就绪）保持 Deferred——不 ClearPending、
                    // 不重新 Begin。未 Ready 绝不授权原生 load；
                    // 兜底只允许发生在 save 前缀的最后期限点（且仍需 Ready 门）。
                    _deferredRequests++;
                    _deferredRepeatRequests++;
                    LastReason = "deferred-repeat:not-ready";
                    return GroupLoadDecision.Deferred;
                }
                _replacedPending++;
            }

            _pending.Exists = true;
            _pending.PlayerToken = playerToken;
            _pending.MapName = mapName;
            _pending.Epoch = _epoch;
            _pendingRegisteredAtMs = MonotonicNowMs();
            _ledger.Begin(_epoch, mapName, playerToken);
            _deferredRequests++;
            return GroupLoadDecision.Deferred;
        }

        /// <summary>
        /// 当前 GAME 组表加载完成后调用：只有 epoch + 地图 + 实例 token 全部匹配才授权一次；
        /// 否则视为旧租约，丢弃 pending 且不执行。
        /// </summary>
        internal DeferredLoadOutcome TryTakeDeferredLoad(string mapName, out long playerToken)
        {
            playerToken = 0L;
            if (!_pending.Exists)
                return DeferredLoadOutcome.None;

            bool sameEpoch = _pending.Epoch == _epoch;
            bool sameMap = string.Equals(_pending.MapName, mapName ?? string.Empty, StringComparison.Ordinal);
            if (!sameEpoch || !sameMap)
            {
                ClearPending();
                _staleFlushRejections++;
                _ledger.MarkAbandoned("stale-lease");
                LastReason = "stale-lease";
                return DeferredLoadOutcome.Stale;
            }

            // T24/F2：组表未就绪绝不授权——不清 pending、不 MarkClaimed、不执行原生 load。
            // 原生 load 在未就绪表上会把合法 alpha 降成 Nil/MEMBER，随后的 save 会把症状持久化。
            if (!IsGroupsReadyFor(mapName))
            {
                _deferredLoadNotReady++;
                LastReason = "not-ready";
                return DeferredLoadOutcome.NotReady;
            }

            playerToken = _pending.PlayerToken;
            ClearPending();
            _flushAuthorizations++;
            _ledger.MarkClaimed();
            return DeferredLoadOutcome.Authorized;
        }

        /// <summary>本地玩家实例销毁：同 token 的 pending 立即作废，不得恢复到后续实例。</summary>
        internal bool OnPlayerDestroyed(long playerToken)
        {
            if (!_pending.Exists || playerToken <= 0L || _pending.PlayerToken != playerToken)
                return false;
            ClearPending();
            _destroyedPending++;
            return true;
        }

        /// <summary>退服/断连/异常回滚等显式放弃：清 pending，且绝不写回存档。</summary>
        internal bool AbandonPending(string reason)
        {
            if (!_pending.Exists)
            {
                // T18/F8：pending 已空时不再静默——计数并留痕，便于诊断"以为在放弃什么"。
                _abandonNoops++;
                LastReason = "abandon-noop:" + (reason ?? string.Empty);
                _ledger.MarkAbandoned(reason);
                return false;
            }
            ClearPending();
            _abandonedPending++;
            LastReason = reason;
            _ledger.MarkAbandoned(reason);
            return true;
        }

        /// <summary>
        /// T18/F1：GAME 后缀可能永不到来时，save 前缀是最后期限点。
        /// 强制兑现 pending 原生 load 前先记账并标记 Degraded（调用方必须 Warn 到玩家可见日志）。
        /// </summary>
        internal void NoteWatchdogForcedRelease(string reason)
        {
            _watchdogForcedReleases++;
            LastReason = reason;
            _ledger.MarkDegraded(reason);
        }

        /// <summary>
        /// T24/F1：save 截止点组表仍未就绪——按失败方向收口：不执行原生 load、不保存本局进度，
        /// 显式 Degraded（宁可本局不落盘，也不用默认值/空表覆盖 Quests.dat）。
        /// </summary>
        internal void NoteSaveBlockedDeadline(string reason)
        {
            _saveBlockedDeadline++;
            LastReason = reason;
            _ledger.MarkSkippedWatchdog(reason);
        }

        /// <summary>T24/F2：未就绪授权拒绝（诊断用，不算已兑现）。</summary>
        internal void NoteNotReadyRejection(string reason)
        {
            _deferredLoadNotReady++;
            LastReason = reason;
        }

        /// <summary>非 SURVIVAL / 无玩家 token 的原生放行计数（不参与恢复）。</summary>
        internal void NoteUnnamedPassThrough()
        {
            _unnamedPassThrough++;
        }

        /// <summary>T14/E3：Prefix 提交 pending 后又抛异常，已回滚账。</summary>
        internal void NotePrefixRollback()
        {
            _prefixRollbacks++;
        }

        /// <summary>T14/E4：受控通知期间回调重入 load，拒绝并记录（不放行）。</summary>
        internal void NoteReentryRejected()
        {
            _reentryRejections++;
            LastReason = "reentry-during-notify";
        }

        /// <summary>T14/E4：同一租约的第二次补发请求被抑制（正常应永不发生）。</summary>
        internal void NoteDuplicateNotifySuppressed()
        {
            _duplicateNotifySuppressed++;
        }

        /// <summary>T14/E4：账本不变式被违反（反例变异必须让用例变红）。</summary>
        internal void NoteInvariantViolation(string detail)
        {
            _invariantViolations++;
            _lastInvariantViolation = detail ?? string.Empty;
            // T18/F8：不变式违反独立成账本字段，不再冒充 Abandoned 状态。
            _ledger.MarkInvariantViolation(detail);
        }

        /// <summary>当前授权租约是否仍是同一 epoch + 地图 + token（load 前后都要校验）。</summary>
        internal bool IsLeaseValid(long playerToken, string mapName)
        {
            return _ledger.SameLease(_epoch, mapName, playerToken);
        }

        /// <summary>T14/E4：真正进入受控原生 load（正常返回前不得算成功）。</summary>
        internal void NoteDeferredLoadEntered()
        {
            _ledger.MarkLoadEntered();
        }

        /// <summary>原生 load 正常返回——唯一可信的 LoadSucceeded 证据。</summary>
        internal void NoteDeferredLoadSucceeded()
        {
            _ledger.MarkLoadSucceeded();
        }

        internal void NoteDeferredLoadFailed(string reason)
        {
            _ledger.MarkLoadFailed(reason);
            // t18/F3：原生 load 抛错必须与 E3 回滚同口径——整条恢复链降级，
            // 运行日志据此给出可观测告警，而不是只在账本里留一行。
            _ledger.MarkDegraded("load-failed:" + reason);
            LastReason = reason;
        }

        internal void NoteNotifySkippedEmpty()
        {
            _ledger.MarkSkippedEmpty();
        }

        internal void NoteNotifyAttempted()
        {
            _notifying = true;
            _ledger.MarkNotifyAttempted();
        }

        internal void NoteNotifySucceeded()
        {
            _notifying = false;
            _ledger.MarkNotifySucceeded();
        }

        internal void NoteNotifyFailed(string reason)
        {
            _notifying = false;
            _ledger.MarkNotifyFailed(reason);
            LastReason = reason;
        }

        /// <summary>进入/退出"正在执行被授权的原生 load"，仅供 Prefix 重入判定。</summary>
        internal void NoteDeferredExecutionEntered()
        {
            _executingDeferredLoad = true;
        }

        internal void NoteDeferredExecutionExited()
        {
            _executingDeferredLoad = false;
        }

        /// <summary>
        /// 测试隔离：清空全部计数与账本（不动 epoch）。生产不使用。
        /// </summary>
        internal void ResetCountersForTests()
        {
            ClearPending();
            _groupsReadyEpoch = -1L;
            _groupsReadyMap = null;
            _nativeLoadExecutions = 0;
            _deferredRequests = 0;
            _flushAuthorizations = 0;
            _staleFlushRejections = 0;
            _replacedPending = 0;
            _boundaryResets = 0;
            _retiredLeases = 0;
            _lastRetiredToken = 0L;
            _lastRetiredEpoch = 0L;
            _abandonedPending = 0;
            _destroyedPending = 0;
            _unnamedPassThrough = 0;
            _executingDeferredLoad = false;
            _notifying = false;
            _prefixRollbacks = 0;
            _reentryRejections = 0;
            _invariantViolations = 0;
            _duplicateNotifySuppressed = 0;
            _lastInvariantViolation = string.Empty;
            _pendingRegisteredAtMs = 0L;
            _watchdogForcedReleases = 0;
            _abandonNoops = 0;
            _deferredRepeatRequests = 0;
            _deferredLoadNotReady = 0;
            _saveBlockedDeadline = 0;
            _ledger.Reset();
            LastReason = "test-reset-counters";
        }

        private void ClearPending()
        {
            _pending.Exists = false;
            _pending.PlayerToken = 0L;
            _pending.MapName = null;
            _pending.Epoch = 0L;
        }
    }
}



