using System;
using System.Collections.Generic;
using SteamP2PFriends.Core.Identity;
using SteamP2PFriends.MultiObserver.Demand;
using SteamP2PFriends.MultiObserver.SPI;

namespace SteamP2PFriends.MultiObserver.Lifecycle
{
    /// <summary>
    /// 共享 Lifecycle Orchestration Engine：把 typed Domain Demand 变成 0 -&gt; 1 Acquire 与
    /// N -&gt; 0 滞回 Release。
    ///
    /// 它拥有需求聚合、租约与区域代次、滞回窗口、身份校验、acquire retry、补偿调度、局部故障
    /// 隔离与统一诊断。它不重新投影空间（投影由共享 Demand Projection Engine 完成）、不触达
    /// 原生状态（原生操作只能经 <see cref="IDomainExecutionPort"/>）、也不知道任何具体领域：
    /// 接入新领域只是把 Demand Policy、Lifecycle Policy 与执行端口注册进来。
    ///
    /// 故障隔离的默认单元是 Domain Id + Region Key + Transition（叠加 Session Epoch 与
    /// Region Generation）：一个区域的转换失败不撤销其它区域、其它领域的租约或状态。
    /// </summary>
    public sealed class LifecycleOrchestrationEngine
    {
        private readonly DemandProjectionEngine _projection;
        private readonly ILifecycleDiagnostics _diagnostics;
        private readonly Dictionary<DomainId, DomainLifecycleState> _domains =
            new Dictionary<DomainId, DomainLifecycleState>();

        private SessionEpoch _sessionEpoch;
        private bool _sessionActive;
        private float _clock;
        private bool _writesSuspended;
        private string _writeSuspensionReason = string.Empty;
        private BoundedHeartbeat _suspensionHeartbeat;

        public LifecycleOrchestrationEngine(
            DemandProjectionEngine projection,
            ILifecycleDiagnostics diagnostics = null)
        {
            _projection = projection ?? throw new ArgumentNullException(nameof(projection));
            _diagnostics = diagnostics ?? DefaultLifecycleDiagnostics.Instance;
        }

        /// <summary>登记一个领域的执行端口。领域集合在注册后不可变：同一 Domain Id 只能注册一次。</summary>
        public void Register(DemandPolicy demandPolicy, IDomainExecutionPort port)
        {
            if (demandPolicy == null) throw new ArgumentNullException(nameof(demandPolicy));
            if (port == null) throw new ArgumentNullException(nameof(port));
            if (port.LifecyclePolicy == null)
                throw new ArgumentException("领域执行端口必须声明自己的 Lifecycle Policy", nameof(port));
            if (demandPolicy.Domain != port.DomainId)
                throw new ArgumentException(
                    "执行端口身份必须与 Demand Policy 的 Domain Id 一致", nameof(port));
            if (_domains.ContainsKey(port.DomainId))
                throw new InvalidOperationException(
                    "同一 Domain Id 只能注册一个执行端口：" + port.DomainId);
            if (!_projection.IsRegistered(demandPolicy.Domain))
                throw new InvalidOperationException(
                    "Demand Policy 必须先注册到共享投影引擎：" + demandPolicy.Domain);
            if (_sessionActive)
                throw new InvalidOperationException(
                    "会话进行中不得新增领域：本次会话的领域集合不可变，不得注册 " + port.DomainId);

            _domains.Add(port.DomainId, new DomainLifecycleState(demandPolicy, port));
        }

        public bool IsRegistered(DomainId domain) => _domains.ContainsKey(domain);

        /// <summary>
        /// 本次会话的领域集合是否已闭合。闭合边界是会话本身：会话进行中不接受新注册
        /// （中途接入的领域拿不到本会话的 Session Epoch），会话结束后下一局可以重新注册。
        /// </summary>
        public bool IsRegistrationClosed => _sessionActive;

        public bool IsSessionActive => _sessionActive;

        public SessionEpoch SessionEpoch => _sessionEpoch;

        public void BeginSession(SessionEpoch sessionEpoch)
        {
            if (_sessionActive && _sessionEpoch == sessionEpoch)
            {
                _diagnostics.TransitionOnce("lifecycle-session-already-active",
                    Build(default, LifecycleEvents.SessionBegin, ELifecycleOutcome.Skipped,
                        ELifecyclePath.Spi, sessionEpoch.Value, null, "reason=already-active"));
                return;
            }

            if (_sessionActive) EndSession();

            uint adapterEpoch = ToAdapterEpoch(sessionEpoch);
            ClearManagedSessionState(recoverFaults: false);
            int participating = 0;
            foreach (DomainLifecycleState state in _domains.Values)
            {


                if (state.RepairRequired)
                {
                    // 故障隔离单元含 Domain Id：熔断中的领域不参与新会话，但不得因此挡住其它
                    // 领域开始会话（那就是跨域熔断）。它保持熔断、拒绝写入，并继续有界心跳。
                    Report(state, LifecycleEvents.DomainFault, ELifecycleOutcome.Skipped,
                        ELifecyclePath.Fallback, sessionEpoch.Value,
                        "reason=repair-required failClosed=true joined=false", role: "Shared",
                        severity: ELifecycleSeverity.Error);
                    continue;
                }

                try
                {
                    state.Port.OnSessionBegin(adapterEpoch);
                    state.Port.ResetReplication(adapterEpoch);
                }
                catch (Exception ex)
                {
                    CleanupAfterFailedBegin(state, sessionEpoch, adapterEpoch, ex);
                    ClearManagedSessionState(recoverFaults: false);
                    Report(state, LifecycleEvents.SessionBegin, ELifecycleOutcome.Failed,
                        ELifecyclePath.Fallback, sessionEpoch.Value,
                        "reason=external-session-initialization-failed failClosed=true exception="
                        + ex.GetType().Name);
                    throw;
                }
                state.SessionEpoch = sessionEpoch;
                participating++;
            }

            if (participating == 0)
            {
                // 没有可参与的领域＝没有可用会话。保持失败闭合，不产生一个「空会话」让调用方
                // 误以为已经就绪。
                DomainLifecycleState first = FirstRegisteredDomain();
                ClearManagedSessionState(recoverFaults: false);
                Report(first, LifecycleEvents.SessionBegin, ELifecycleOutcome.Rejected,
                    ELifecyclePath.Fallback, sessionEpoch.Value,
                    "reason=repair-required failClosed=true participatingDomains=0",
                    severity: ELifecycleSeverity.Error);
                throw new InvalidOperationException(
                    "领域需要先修复才能开始新会话："
                    + (first == null ? "none" : first.Port.DisplayName));
            }

            _sessionEpoch = sessionEpoch;
            _sessionActive = true;
            // 新会话建立即身份重新确认：会话级挂起随会话边界解除并写闭环记录。
            ResumeWrites("session-begin");
        }

        private DomainLifecycleState FirstRegisteredDomain()
        {
            foreach (DomainLifecycleState state in _domains.Values) return state;
            return null;
        }

        /// <summary>
        /// 领域在新会话初始化失败时的收尾：失败方向的清理各自独立捕获，任一清理失败只记日志，
        /// 不掩盖原始失败——原始异常必须原样抛回调用方。
        /// </summary>
        private void CleanupAfterFailedBegin(
            DomainLifecycleState state, SessionEpoch sessionEpoch, uint adapterEpoch, Exception original)
        {
            try
            {
                state.Port.OnSessionEnd();
            }
            catch (Exception cleanupEx)
            {
                Report(state, LifecycleEvents.SessionBeginCleanup, ELifecycleOutcome.Failed,
                    ELifecyclePath.Fallback, sessionEpoch.Value,
                    "reason=lifecycle-cleanup-failed exception=" + cleanupEx.GetType().Name);
            }

            try
            {
                state.Port.ResetReplication(adapterEpoch);
            }
            catch (Exception cleanupEx)
            {
                Report(state, LifecycleEvents.SessionBeginCleanup, ELifecycleOutcome.Failed,
                    ELifecyclePath.Fallback, sessionEpoch.Value,
                    "reason=replication-cleanup-failed exception=" + cleanupEx.GetType().Name);
            }

            Report(state, LifecycleEvents.SessionBegin, ELifecycleOutcome.Failed,
                ELifecyclePath.Fallback, sessionEpoch.Value,
                "reason=session-begin-rolled-back exception=" + original.GetType().Name);
        }

        /// <summary>
        /// 会话边界收尾。每个领域的清理独立捕获：一个领域清理失败不清空其它领域，也不掩盖
        /// 失败本身——全部领域都收尾后，第一个失败按既有失败闭合语义抛回调用方；对应领域
        /// 进入 repair-required，新写入被拒绝。
        /// </summary>
        public void EndSession()
        {
            if (!_sessionActive)
            {
                // 兜底：Ledger 与编排状态短暂失配、或收到重复/失序的会话结束通知时，
                // 唯一空间事实与领域投影也必须清干净，不得跨会话残留。
                // 这里不动 repair-required：失败闭合语义仍由修复路径独占。
                _projection.EndSession();
                _diagnostics.TransitionOnce("lifecycle-session-end-inactive",
                    Build(default, LifecycleEvents.SessionEnd, ELifecycleOutcome.Skipped,
                        ELifecyclePath.Fallback, 0UL, "reason=session-not-active", "Shared"));
                return;
            }

            SessionEpoch endingEpoch = _sessionEpoch;
            uint adapterEpoch = ToAdapterEpoch(endingEpoch);
            Exception cleanupFailure = null;
            List<DomainLifecycleState> cleanupFailed = null;
            List<DomainLifecycleState> faultedBeforeEnd = null;
            foreach (DomainLifecycleState state in _domains.Values)
            {
                if (!state.RepairRequired) continue;
                if (faultedBeforeEnd == null) faultedBeforeEnd = new List<DomainLifecycleState>();
                faultedBeforeEnd.Add(state);
            }
            foreach (DomainLifecycleState state in _domains.Values)
            {
                try
                {
                    state.Port.OnSessionEnd();
                }
                catch (Exception ex)
                {
                    if (cleanupFailure == null) cleanupFailure = ex;
                    if (cleanupFailed == null) cleanupFailed = new List<DomainLifecycleState>();
                    if (!cleanupFailed.Contains(state)) cleanupFailed.Add(state);
                    Report(state, LifecycleEvents.SessionEnd, ELifecycleOutcome.Failed,
                        ELifecyclePath.Fallback, endingEpoch.Value,
                        "reason=lifecycle-session-end-failed state-cleanup-required exception="
                        + ex.GetType().Name);
                }

                try
                {
                    state.Port.ResetReplication(adapterEpoch);
                }
                catch (Exception ex)
                {
                    if (cleanupFailure == null) cleanupFailure = ex;
                    if (cleanupFailed == null) cleanupFailed = new List<DomainLifecycleState>();
                    if (!cleanupFailed.Contains(state)) cleanupFailed.Add(state);
                    Report(state, LifecycleEvents.SessionEnd, ELifecycleOutcome.Failed,
                        ELifecyclePath.Fallback, endingEpoch.Value,
                        "reason=replication-reset-failed state-cleanup-required exception="
                        + ex.GetType().Name);
                }
            }

            ClearManagedSessionState();
            if (cleanupFailed != null)
            {
                // 清理失败只熔断对应领域：状态已清干净，但该领域在修复前不得再写入。
                foreach (DomainLifecycleState state in cleanupFailed) MarkRepairRequired(state);
            }

            // 会话收尾是熔断领域的有限恢复点：本次收尾成功的领域退出熔断并写闭环记录，
            // 收尾再次失败的领域重新熔断——恢复与熔断都留痕，不静默。
            if (faultedBeforeEnd != null)
            {
                foreach (DomainLifecycleState state in faultedBeforeEnd)
                {
                    if (state.RepairRequired) continue;
                    Report(state, LifecycleEvents.DomainFault, ELifecycleOutcome.Success,
                        ELifecyclePath.Spi, endingEpoch.Value,
                        "reason=domain-fault-cleared cause=session-end-recovered failClosed=false",
                        role: "Shared");
                }
            }

            ResumeWrites("session-end");
            if (cleanupFailure != null) throw cleanupFailure;
        }

        private void ClearManagedSessionState(bool recoverFaults = true)
        {
            // 会话边界同时清空唯一空间事实与全部领域投影：旧 Session 的观察者不存在于新会话，
            // 残留事实会让「唯一事实」跨会话失真。熔断标记默认随会话收尾恢复；开始新会话时
            // 必须保留（熔断领域不参与新会话，等它自己的收尾成功才算修复）。
            _projection.EndSession();
            foreach (DomainLifecycleState state in _domains.Values) state.Clear(recoverFaults);
            _sessionEpoch = default(SessionEpoch);
            _sessionActive = false;
        }

        /// <summary>
        /// 提交一次观察者事实并按其领域投影差异驱动生命周期。需求聚合发生在引擎内：
        /// 逐区域引用计数 0 -&gt; 1 触发 Acquire，N -&gt; 0 触发滞回 Release 调度。
        /// 整个观察者更新是一个事务：任一环节抛出即回滚本领域的编排状态、投影贡献与
        /// 已执行的原生副作用，再原样抛回调用方。
        /// </summary>
        public DomainDemandProjection Observe(
            DomainId domain,
            ulong observerId,
            ulong connectionToken,
            byte centerX,
            byte centerY,
            bool gameplayAuthorized)
        {
            DomainLifecycleState state = EnsureDomain(domain);
            EnsureWritable(state);
            if (observerId == 0UL)
            {
                Report(state, LifecycleEvents.ObserverUpdate, ELifecycleOutcome.Rejected,
                    ELifecyclePath.Fallback, state.SessionEpoch.Value, "reason=observer-id-zero",
                    severity: ELifecycleSeverity.Error);
                throw new ArgumentOutOfRangeException(nameof(observerId));
            }
            if (connectionToken == 0UL)
            {
                Report(state, LifecycleEvents.ObserverUpdate, ELifecycleOutcome.Rejected,
                    ELifecyclePath.Fallback, state.SessionEpoch.Value,
                    "reason=connection-generation-zero", severity: ELifecycleSeverity.Error);
                throw new ArgumentOutOfRangeException(nameof(connectionToken));
            }

            bool hadObserver = state.Observers.Contains(observerId);
            bool hadConnection = state.ConnectionTokens.TryGetValue(observerId, out ulong previousConnectionToken);
            bool connectionChanged = hadConnection && previousConnectionToken != connectionToken;
            HashSet<RegionKey> previousRegions = _projection.GetActiveRegions(state.DemandPolicy, observerId);
            DomainLifecycleState.RegionSnapshot snapshot = state.CaptureRegionSnapshot();
            int previousObserverCount = state.ObserverCount;

            var compensations = new List<Action>();
            try
            {
                CaptureDisconnectCompensation(state, observerId, previousConnectionToken,
                    hadConnection && connectionChanged, compensations);
                state.Observers.Add(observerId);
                // 空间事实与投影都归 Control Plane：引擎只提交观测值并消费领域投影差异。
                DomainDemandProjection projection = _projection.Observe(state.DemandPolicy, new ObserverPresence(
                    observerId, connectionToken, new RegionKey(centerX, centerY), gameplayAuthorized));
                ulong exitedConnectionToken = connectionChanged ? previousConnectionToken : connectionToken;
                ProcessExited(state, observerId, exitedConnectionToken, projection.ExitedRegions, compensations);
                ProcessEntered(state, observerId, connectionToken, projection.EnteredRegions, compensations);
                ProcessAcquireRetries(state, observerId, connectionToken, compensations);
                if (connectionChanged)
                {
                    // 连接代次失效是允许的清理触发：旧代次的未完成事务登记不得继续替新连接
                    // 提交（旧代次没有写入资格）。登记撤销必须进补偿列表，回滚时原样放回。
                    List<KeyValuePair<AcquireRetryKey, AcquireRetry>> staleRegistrations =
                        state.RemoveAcquireRetriesForGeneration(observerId, connectionToken);
                    compensations.Add(() => state.RestoreAcquireRetries(staleRegistrations));
                    if (staleRegistrations.Count > 0)
                    {
                        Report(state, LifecycleEvents.ObserverUpdate, ELifecycleOutcome.Rejected,
                            ELifecyclePath.Fallback, state.SessionEpoch.Value,
                            "reason=stale-connection-retry-dropped failClosed=true dropped="
                            + staleRegistrations.Count + " previousConnection=" + previousConnectionToken
                            + " observer=" + observerId);
                    }
                    state.Port.OnObserverDisconnected(observerId, previousConnectionToken);
                }
                state.ConnectionTokens[observerId] = connectionToken;
                // 本拍拿到了可用样本：该观察者退出 Deferred Observer Demand 并留闭环记录。
                ClearDeferred(state, observerId, "observer-sample-available");
                return projection;
            }
            catch (Exception ex)
            {
                RestoreState(state, observerId, hadObserver, hadConnection, previousConnectionToken,
                    previousRegions, snapshot, previousObserverCount);
                bool compensated = RunCompensations(state, compensations, ex);
                Report(state, LifecycleEvents.ObserverUpdate, ELifecycleOutcome.Failed,
                    ELifecyclePath.Fallback, state.SessionEpoch.Value,
                    "transactionRolledBack=" + compensated + " exception=" + ex.GetType().Name);
                throw;
            }
        }

        /// <summary>
        /// 确认离开：移除观察者事实与它的全部投影贡献，剩余区域按引用计数递减并进入滞回。
        /// 与 <see cref="Observe"/> 同为事务单元，失败回滚本领域状态与原生副作用。
        /// </summary>
        public DomainDemandProjection RemoveObserver(DomainId domain, ulong observerId)
        {
            DomainLifecycleState state = EnsureDomain(domain);
            EnsureWritable(state);
            bool hadObserver = state.Observers.Contains(observerId);
            bool hadConnection = state.ConnectionTokens.TryGetValue(observerId, out ulong previousConnectionToken);
            HashSet<RegionKey> previousRegions = _projection.GetActiveRegions(state.DemandPolicy, observerId);
            DomainLifecycleState.RegionSnapshot snapshot = state.CaptureRegionSnapshot();
            int previousObserverCount = state.ObserverCount;

            var compensations = new List<Action>();
            try
            {
                CaptureDisconnectCompensation(state, observerId, previousConnectionToken, hadConnection,
                    compensations);
                DomainDemandProjection projection = _projection.RemoveObserver(state.DemandPolicy, observerId);
                if (projection.HasChanges)
                    ProcessExited(state, observerId, projection.ConnectionToken,
                        projection.ExitedRegions, compensations);
                if (state.ConnectionTokens.TryGetValue(observerId, out ulong connectionToken))
                {
                    object disconnectState = state.Port.CaptureObserverDisconnectState(observerId, connectionToken);
                    compensations.Add(() => state.Port.RestoreObserverDisconnectState(
                        observerId, connectionToken, disconnectState));
                    state.Port.OnObserverDisconnected(observerId, connectionToken);
                    state.ConnectionTokens.Remove(observerId);
                    state.Observers.Remove(observerId);
                    state.ObserverCount = state.Observers.Count;
                    state.DropAcquireRetriesOwnedBy(observerId);
                }
                // 确认离开：暂缓标记随之清除（放在事务尾部，失败回滚时标记原样保留）。
                ClearDeferred(state, observerId, "observer-confirmed-exit");
                return projection;
            }
            catch (Exception ex)
            {
                RestoreState(state, observerId, hadObserver, hadConnection, previousConnectionToken,
                    previousRegions, snapshot, previousObserverCount);
                bool compensated = RunCompensations(state, compensations, ex);
                Report(state, LifecycleEvents.ObserverRemove, ELifecycleOutcome.Failed,
                    ELifecyclePath.Fallback, state.SessionEpoch.Value,
                    "transactionRolledBack=" + compensated + " exception=" + ex.GetType().Name);
                throw;
            }
        }

        /// <summary>推进时间轴并刷新已持有租约的区域代次。区域级失败只记录，不改变租约。</summary>
        public void AdvanceTime(float deltaTime)
        {
            if (deltaTime < 0f) throw new ArgumentOutOfRangeException(nameof(deltaTime));
            _clock += deltaTime;
            if (_writesSuspended)
            {
                // 身份不确定期间不做任何推进：既不放行写入，也不提交破坏性释放；租约原样保留。
                ReportSuspensionHeartbeat();
                return;
            }
            foreach (DomainLifecycleState state in _domains.Values)
            {
                // 时间轴与刷新是引擎内部驱动的推进，不是调用方发起的写入：熔断中的领域被跳过并
                // 留下有界诊断，其它领域照常推进时间、retry 与滞回——否则一个领域的
                // repair-required 就会变成跨域熔断。
                EnsureSessionActive(state);
                // 时间轴先推进：熔断领域的推进被跳过，但它自己的有界心跳仍要按间隔走，
                // 否则首条之后永远到不了下一个间隔，又退回「一条日志后静默」。
                state.Clock += deltaTime;
                if (state.RepairRequired)
                {
                    ReportSkippedFaultedDomain(state);
                    continue;
                }
                foreach (RegionKey region in state.LeaseKeys())
                {
                    try
                    {
                        RegionGeneration stored = state.Leases[region];
                        RegionGeneration current = state.Port.ReadRegionGeneration(region);
                        if (current.Value < stored.Value)
                        {
                            Report(state, LifecycleEvents.RegionGenerationRead, ELifecycleOutcome.Rejected,
                                ELifecyclePath.Fallback, state.SessionEpoch.Value,
                                "reason=generation-regressed state-retained=true current=" + current.Value
                                + " stored=" + stored.Value, region, 0UL, 0UL, stored.Value);
                            continue;
                        }
                        state.Leases[region] = current;
                    }
                    catch (Exception ex)
                    {
                        Report(state, LifecycleEvents.RegionGenerationRead, ELifecycleOutcome.Failed,
                            ELifecyclePath.Fallback, state.SessionEpoch.Value,
                            "reason=generation-reader-failed retryable=true state-retained=true exception="
                            + ex.GetType().Name, region, 0UL, 0UL, state.Leases[region].Value);
                    }
                }
            }
        }

        public void Flush(float deltaTime)
        {
            if (_writesSuspended)
            {
                ReportSuspensionHeartbeat();
                return;
            }
            foreach (DomainLifecycleState state in _domains.Values)
            {
                EnsureSessionActive(state);
                if (state.RepairRequired)
                {
                    ReportSkippedFaultedDomain(state);
                    continue;
                }
                FlushDomain(state, deltaTime);
            }
        }

        /// <summary>引擎级熔断：会话不存在时推进路径仍按失败闭合拒绝，并留下诊断。</summary>
        private void EnsureSessionActive(DomainLifecycleState state)
        {
            if (_sessionActive) return;
            Report(state, LifecycleEvents.SessionState, ELifecycleOutcome.Failed,
                ELifecyclePath.Fallback, 0UL, "reason=session-not-active", role: "Shared");
            throw new InvalidOperationException(
                "共享编排引擎没有活动会话：" + state.Port.DisplayName);
        }

        /// <summary>
        /// 熔断领域被推进路径跳过时的有界心跳：首条立即写出，此后按领域政策间隔重复，
        /// 重复次数受有界上限约束，用尽时写出一条显式终止记录——既不会逐帧淹没日志，
        /// 也不是「一条去重日志之后永久静默」。跳过只影响该领域：其它领域照常推进；
        /// 熔断领域自己的显式写入仍由 <see cref="EnsureWritable"/> 拒绝。
        /// </summary>
        private void ReportSkippedFaultedDomain(DomainLifecycleState state)
        {
            EBoundedHeartbeat kind = NextFaultHeartbeat(state);
            if (kind == EBoundedHeartbeat.Suppressed) return;
            Report(state, LifecycleEvents.DomainFault,
                kind == EBoundedHeartbeat.Exhausted ? ELifecycleOutcome.Skipped : ELifecycleOutcome.Rejected,
                ELifecyclePath.Fallback, state.SessionEpoch.Value,
                "reason=repair-required failClosed=true skipped=advance heartbeat=" + kind
                + " attempts=" + state.FaultHeartbeat.EmittedCount,
                role: "Shared",
                severity: kind == EBoundedHeartbeat.Exhausted
                    ? ELifecycleSeverity.Warn
                    : ELifecycleSeverity.Error);
        }

        /// <summary>
        /// 熔断该领域并起搏它的有界心跳。只有「进入熔断」才重新起搏：持续熔断的心跳有界，
        /// 重复用尽后写出显式终止记录并静默等待状态变化，不再按周期重启刷屏。
        /// </summary>
        private static void MarkRepairRequired(DomainLifecycleState state)
        {
            if (state.RepairRequired) return;
            state.RepairRequired = true;
            state.FaultHeartbeat = new BoundedHeartbeat(state.Policy.Heartbeat, state.Clock);
        }

        private static EBoundedHeartbeat NextFaultHeartbeat(DomainLifecycleState state)
        {
            return state.FaultHeartbeat.Next(state.Clock);
        }

        /// <summary>
        /// 进入该领域的暂缓观察者：本拍观测不可用（记录读不全、身份不可判定）却无法证明离开。
        /// 既有贡献原样保留，不递减需求、不登记释放、不触达领域释放——「无法证明离开就不做
        /// 破坏性释放」。持续暂缓按领域政策写出有界心跳，恢复（收到可用样本或确认离开）时写闭环记录。
        /// </summary>
        public void DeferObserver(DomainId domain, ulong observerId, string reason)
        {
            DomainLifecycleState state = EnsureDomain(domain);
            if (observerId == 0UL) return;
            bool newlyDeferred = !state.DeferredObservers.ContainsKey(observerId);
            if (newlyDeferred)
            {
                state.DeferredObservers.Add(observerId, new DeferredObserver(reason, _clock));
                Report(state, LifecycleEvents.ObserverDeferred, ELifecycleOutcome.Deferred,
                    ELifecyclePath.Fallback, state.SessionEpoch.Value,
                    "reason=" + (reason ?? "unspecified") + " deferredObservers="
                    + state.DeferredObservers.Count + " contributionRetained=true",
                    observerId: observerId, role: "Shared", severity: ELifecycleSeverity.Warn);
            }

            // 只有新观察者进入暂缓才重新起搏：持续暂缓的心跳有界，终止后不自动重启——
            // 否则「有界心跳」会退化成按周期无限刷屏。清空后再次进入暂缓时自然重新起搏。
            if (newlyDeferred) StartDeferredHeartbeat(state);
            ReportDeferredHeartbeat(state, NextDeferredHeartbeat(state));
        }

        private static void StartDeferredHeartbeat(DomainLifecycleState state)
        {
            if (state.DeferredHeartbeat.IsRunning) return;
            state.DeferredHeartbeat = new BoundedHeartbeat(state.Policy.Heartbeat, state.Clock);
        }

        /// <summary>书面一条暂缓心跳记录；未到间隔或被有界上限终止时静默。</summary>
        private void ReportDeferredHeartbeat(DomainLifecycleState state, EBoundedHeartbeat kind)
        {
            if (kind == EBoundedHeartbeat.Suppressed) return;
            Report(state, LifecycleEvents.ObserverDeferred,
                kind == EBoundedHeartbeat.Exhausted ? ELifecycleOutcome.Skipped : ELifecycleOutcome.Deferred,
                ELifecyclePath.Fallback, state.SessionEpoch.Value,
                "heartbeat=" + kind + " deferredObservers=" + state.DeferredObservers.Count
                + " contributionRetained=true",
                attempt: state.DeferredHeartbeat.EmittedCount,
                role: "Shared",
                severity: kind == EBoundedHeartbeat.Exhausted
                    ? ELifecycleSeverity.Warn
                    : ELifecycleSeverity.Info);
        }

        private static EBoundedHeartbeat NextDeferredHeartbeat(DomainLifecycleState state)
        {
            if (!state.DeferredHeartbeat.IsRunning) return EBoundedHeartbeat.Suppressed;
            return state.DeferredHeartbeat.Next(state.Clock);
        }

        /// <summary>
        /// 清除该观察者的暂缓标记并写闭环记录：观测恢复可用或已确认离开。只有确认离开、
        /// 代次失效、会话重置才会走到这里——暂缓本身不会清理任何贡献。
        /// </summary>
        private void ClearDeferred(DomainLifecycleState state, ulong observerId, string cause)
        {
            if (!state.DeferredObservers.Remove(observerId)) return;
            Report(state, LifecycleEvents.ObserverDeferred,
                ELifecycleOutcome.Success, ELifecyclePath.Spi, state.SessionEpoch.Value,
                "reason=deferred-cleared cause=" + cause + " deferredObservers="
                + state.DeferredObservers.Count,
                observerId: observerId, role: "Shared");
            if (state.DeferredObservers.Count == 0)
            {
                state.DeferredHeartbeat.Stop();
            }
        }

        /// <summary>该领域当前处于暂缓的观察者数。</summary>
        public int DeferredObserverCount(DomainId domain) => EnsureDomain(domain).DeferredObservers.Count;

        public bool IsObserverDeferred(DomainId domain, ulong observerId) =>
            EnsureDomain(domain).DeferredObservers.ContainsKey(observerId);

        /// <summary>
        /// 挂起写入：会话身份不确定或共享面故障恢复期间，拒绝新写入与破坏性释放，但保留全部
        /// 租约、需求与暂缓态（身份不确定时不得清空其它领域）。持续挂起按领域政策写出有界心跳。
        /// </summary>
        public void SuspendWrites(string reason)
        {
            if (_writesSuspended)
            {
                ReportSuspensionHeartbeat();
                return;
            }

            _writesSuspended = true;
            _writeSuspensionReason = reason ?? "unspecified";
            _suspensionHeartbeat = new BoundedHeartbeat(SharedHeartbeatPolicy(), _clock);
            ReportSuspensionHeartbeat();
        }

        /// <summary>恢复写入并写闭环记录：会话身份重新确认或共享面故障已确认恢复。</summary>
        public void ResumeWrites(string reason)
        {
            if (!_writesSuspended) return;
            _writesSuspended = false;
            _suspensionHeartbeat.Stop();
            _diagnostics.Transition(Build(default(DomainId), LifecycleEvents.SessionSuspended,
                ELifecycleOutcome.Success, ELifecyclePath.Spi,
                _sessionActive ? _sessionEpoch.Value : 0UL,
                "reason=writes-resumed cause=" + (reason ?? "unspecified")
                + " previousCause=" + _writeSuspensionReason, "Shared"));
            _writeSuspensionReason = string.Empty;
        }

        public bool IsWriteSuspended => _writesSuspended;

        public string WriteSuspensionReason => _writeSuspensionReason;

        private void ReportSuspensionHeartbeat()
        {
            if (!_suspensionHeartbeat.IsRunning) return;
            EBoundedHeartbeat kind = _suspensionHeartbeat.Next(_clock);
            if (kind == EBoundedHeartbeat.Suppressed) return;
            _diagnostics.Transition(Build(default(DomainId), LifecycleEvents.SessionSuspended,
                kind == EBoundedHeartbeat.Exhausted ? ELifecycleOutcome.Skipped : ELifecycleOutcome.Rejected,
                ELifecyclePath.Fallback, _sessionActive ? _sessionEpoch.Value : 0UL,
                "reason=writes-suspended cause=" + _writeSuspensionReason + " failClosed=true heartbeat="
                + kind + " attempts=" + _suspensionHeartbeat.EmittedCount, "Shared"));
        }

        /// <summary>
        /// 引擎级心跳的节奏来源：取本会话任一领域声明的政策，避免引擎内置跨域默认值。
        /// 没有注册领域时（会话尚未接线）用最小可用节奏，保证挂起本身仍可观测。
        /// </summary>
        private HeartbeatPolicy SharedHeartbeatPolicy()
        {
            foreach (DomainLifecycleState state in _domains.Values) return state.Policy.Heartbeat;
            return new HeartbeatPolicy(5f, 3);
        }

        public void Tick(float deltaTime)
        {
            AdvanceTime(deltaTime);
            Flush(0f);
        }

        /// <summary>
        /// 处理区域退出：撤销该观察者的复制贡献、递减需求引用计数，并在最后一个贡献者离开时
        /// 调度滞回释放。暂缓/失败重试中的区域从未完成进入、需求也未计入，因此由登记撤销分支
        /// 接住，跳过需求递减以避免状态失衡（撤销同样登记补偿）。
        /// </summary>
        private void ProcessExited(
            DomainLifecycleState state,
            ulong observerId,
            ulong connectionToken,
            RegionKey[] regions,
            List<Action> compensations)
        {
            foreach (RegionKey region in regions)
            {
                if (state.HasAcquireRetryFor(region, observerId))
                {
                    // 该观察者在此区域只留下未完成的事务：撤销它的全部登记（含陈旧连接代次），
                    // 不递减需求——暂缓/失败中的区域从未完成进入、需求也未计入。
                    List<KeyValuePair<AcquireRetryKey, AcquireRetry>> removed =
                        state.RemoveAcquireRetriesFor(region, observerId);
                    compensations.Add(() => state.RestoreAcquireRetries(removed));
                    continue;
                }
                // 兜底：过时退出容忍——区域在投影里却既无需求也无本观察者登记，属历史失衡残留
                // 或未知路径。记日志跳过，不抛 underflow，也不动其它观察者的有效登记。
                if (state.GetDemand(region) <= 0)
                {
                    Report(state, LifecycleEvents.RegionExit, ELifecycleOutcome.Skipped,
                        ELifecyclePath.Spi, state.SessionEpoch.Value,
                        "reason=stale-exit-without-demand observer=" + observerId,
                        region, connectionToken, observerId, state.GetStoredGeneration(region).Value);
                    continue;
                }

                try
                {
                    object replicationState = state.Port.CaptureObserverReplicationState(observerId, connectionToken);
                    compensations.Add(() => state.Port.RestoreObserverReplicationState(
                        observerId, connectionToken, replicationState));
                    compensations.Add(() => state.Port.OnObserverReplicationEntered(
                        observerId, connectionToken, region));
                    state.Port.OnObserverReplicationExited(observerId, connectionToken, region);
                }
                catch (Exception ex)
                {
                    Report(state, LifecycleEvents.ReplicationRemove, ELifecycleOutcome.Failed,
                        ELifecyclePath.Fallback, state.SessionEpoch.Value,
                        "observer=" + observerId + " exception=" + DescribeFailure(ex),
                        region, connectionToken, observerId, state.GetStoredGeneration(region).Value);
                    throw;
                }

                int next = state.DecrementDemand(region);
                if (next != 0 || !state.Leases.ContainsKey(region)) continue;

                RegionGeneration storedGeneration = state.Leases[region];
                RegionGeneration observedGeneration = state.Port.ReadRegionGeneration(region);
                RegionGeneration pendingGeneration = observedGeneration.Value < storedGeneration.Value
                    ? storedGeneration
                    : observedGeneration;
                if (observedGeneration.Value < storedGeneration.Value)
                {
                    Report(state, LifecycleEvents.RegionReleaseScheduled, ELifecycleOutcome.Rejected,
                        ELifecyclePath.Fallback, state.SessionEpoch.Value,
                        "reason=generation-regressed state-retained=true current="
                        + observedGeneration.Value + " stored=" + storedGeneration.Value,
                        region, connectionToken, observerId, storedGeneration.Value);
                }
                state.PendingReleases[region] = new PendingRelease
                {
                    SessionEpoch = state.SessionEpoch,
                    RegionKey = region,
                    RegionGeneration = pendingGeneration,
                    Deadline = state.Clock + state.Policy.HysteresisSeconds
                };
                Report(state, LifecycleEvents.RegionReleaseScheduled, ELifecycleOutcome.Scheduled,
                    ELifecyclePath.Spi, state.SessionEpoch.Value,
                    "authority=" + state.Port.DisplayName + " hysteresisSeconds="
                    + state.Policy.HysteresisSeconds.ToString("0.###") + " demand=0",
                    region, 0UL, 0UL, state.PendingReleases[region].RegionGeneration.Value);
            }
        }

        /// <summary>
        /// 取证描述：把原生失败的类型与消息拼接为可观测片段。刻意独立成静态方法，使转换路径的
        /// 方法体内不包含任何 <c>get_Message</c> 调用——分类边界不解析异常文本，消息只在这里
        /// 承载，供运行时日志定位具体失败原生区域。
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(
            System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static string DescribeFailure(Exception exception)
        {
            return exception.GetType().Name + " message=" + exception.Message;
        }

        private void ProcessEntered(
            DomainLifecycleState state,
            ulong observerId,
            ulong connectionToken,
            RegionKey[] regions,
            List<Action> compensations)
        {
            foreach (RegionKey region in regions)
            {
                ProcessSingleRegionEntry(state, observerId, connectionToken, region, compensations);
            }

            state.ObserverCount = state.Observers.Count;
        }

        /// <summary>
        /// 单区域进入处理：0 -&gt; 1 时获取区域权威并登记复制贡献，随后递增需求引用计数。
        /// 区域级 acquire 失败在此隔离——只跳过本区域、保留可重试登记，不向调用方抛出，
        /// 从而不触发整批回滚，也不进入协调器的故障退避。replication 段失败仍保持事务语义
        /// （整批回滚）。异常文本仅经 <see cref="DescribeFailure"/> 承载。
        /// </summary>
        private void ProcessSingleRegionEntry(
            DomainLifecycleState state,
            ulong observerId,
            ulong connectionToken,
            RegionKey region,
            List<Action> compensations)
        {
            int previous = state.GetDemand(region);
            bool hasLease = state.Leases.ContainsKey(region);
            bool hasPendingRelease = state.PendingReleases.ContainsKey(region);
            if (previous == 0 && !hasLease)
            {
                string failureReason = "acquire-failed";
                Exception acquireFailure = null;
                bool acquired = false;
                int compensationBase = compensations.Count;
                try
                {
                    failureReason = "generation-reader-failed-before-acquire";
                    RegionGeneration beforeAcquire = state.Port.ReadRegionGeneration(region);
                    failureReason = "invalid-ticket";
                    LeaseTicket acquireTicket = CreateTicket(state, region, beforeAcquire, 1);
                    if (!acquireTicket.Valid)
                        throw new InvalidOperationException("Lifecycle acquire ticket is invalid.");
                    failureReason = "region-snapshot-failed";
                    object acquireState = state.Port.CaptureRegionState(region);
                    compensations.Add(() => state.Port.RestoreRegionState(region, acquireState));
                    failureReason = "acquire-failed";
                    state.Port.OnAcquire(acquireTicket);
                    failureReason = "generation-reader-failed-after-acquire";
                    RegionGeneration acquiredGeneration = state.Port.ReadRegionGeneration(region);
                    failureReason = "generation-regressed-after-acquire";
                    if (acquiredGeneration.Value >= beforeAcquire.Value)
                    {
                        state.Leases[region] = acquiredGeneration;
                        acquired = true;
                        Report(state, LifecycleEvents.RegionAcquire, ELifecycleOutcome.Success,
                            ELifecyclePath.Spi, state.SessionEpoch.Value,
                            "reason=region-demand-entered authority=" + state.Port.DisplayName
                            + " demand=" + (previous + 1),
                            region, connectionToken, observerId, acquiredGeneration.Value,
                            attempt: AcquireAttemptCount(state, region, observerId, connectionToken));
                    }
                }
                catch (Exception ex)
                {
                    acquireFailure = ex;
                }

                if (!acquired)
                {
                    // 撤销本区域已登记的补偿（如快照恢复）：本区域失败不进整批回滚。
                    compensations.RemoveRange(compensationBase, compensations.Count - compensationBase);
                    EDomainFailureKind kind = acquireFailure == null
                        ? EDomainFailureKind.Failed
                        : state.Port.ClassifyRegionFailure(acquireFailure);
                    bool deferred = kind == EDomainFailureKind.Deferred;
                    AcquireRetryKey retryKey = TransactionKey(region, observerId, connectionToken);
                    bool quietSteadyState = deferred
                        && state.AcquireRetries.TryGetValue(retryKey, out AcquireRetry currentRetry)
                        && currentRetry.Attempts >= state.Policy.DeferredRetry.QuietAttempts;
                    ScheduleAcquireRetry(state, region, observerId, connectionToken, deferred);
                    if (!quietSteadyState)
                    {
                        Report(state, LifecycleEvents.RegionAcquire,
                            deferred ? ELifecycleOutcome.Deferred : ELifecycleOutcome.Skipped,
                            ELifecyclePath.Fallback, state.SessionEpoch.Value,
                            "reason=" + (deferred ? "region-snapshot-deferred" : failureReason)
                            + " attempts=" + state.AcquireRetries[retryKey].Attempts
                            + (acquireFailure == null ? string.Empty : " exception=" + DescribeFailure(acquireFailure)),
                            region, connectionToken, observerId, state.GetStoredGeneration(region).Value,
                            attempt: state.AcquireRetries[retryKey].Attempts,
                            role: "Host",
                            // 暂缓是预期内的短延迟等待（常规信息）；一般失败是跳过（错误）。
                            severity: deferred ? ELifecycleSeverity.Info : ELifecycleSeverity.Error);
                    }
                    return;
                }
            }

            try
            {
                object replicationState = state.Port.CaptureObserverReplicationState(observerId, connectionToken);
                compensations.Add(() => state.Port.RestoreObserverReplicationState(
                    observerId, connectionToken, replicationState));
                compensations.Add(() => state.Port.OnObserverReplicationExited(
                    observerId, connectionToken, region));
                state.Port.OnObserverReplicationEntered(observerId, connectionToken, region);
            }
            catch (Exception ex)
            {
                Report(state, LifecycleEvents.ReplicationEnqueue, ELifecycleOutcome.Failed,
                    ELifecyclePath.Fallback, state.SessionEpoch.Value,
                    "observer=" + observerId + " exception=" + DescribeFailure(ex),
                    region, connectionToken, observerId, state.GetStoredGeneration(region).Value);
                throw;
            }

            state.Demand[region] = previous + 1;
            if (previous == 0 && hasPendingRelease)
            {
                state.PendingReleases.Remove(region);
                state.ReentryCount++;
                Report(state, LifecycleEvents.RegionReentry, ELifecycleOutcome.Success,
                    ELifecyclePath.Spi, state.SessionEpoch.Value, "hysteresisCancelled=true",
                    region, connectionToken, observerId, state.Port.ReadRegionGeneration(region).Value);
            }

            // 只清除当前事务（本观察者 + 本连接代次）自己的登记：其他观察者、其他连接代次
            // 对该区域的待重试登记仍然有效。清除必须登记补偿——登记清除若游离在事务补偿之外，
            // 任何一次回滚都会留下「区域在投影里、无需求、无 retry 登记」的失衡态。
            AcquireRetryKey ownKey = TransactionKey(region, observerId, connectionToken);
            AcquireRetry ownRetry;
            if (state.AcquireRetries.TryGetValue(ownKey, out ownRetry))
            {
                state.AcquireRetries.Remove(ownKey);
                compensations.Add(() => state.AcquireRetries[ownKey] = ownRetry);
            }
        }

        /// <summary>
        /// 本区域这次 acquire 是第几次尝试：沿用该观察者本连接代次已登记的失败次数（+1），
        /// 没有登记即为首次尝试。诊断用它区分「一次成功」与「重试后成功」。
        /// </summary>
        private static int AcquireAttemptCount(
            DomainLifecycleState state, RegionKey region, ulong observerId, ulong connectionToken)
        {
            return NextAttemptNumber(state, TransactionKey(region, observerId, connectionToken));
        }

        private static AcquireRetryKey TransactionKey(
            RegionKey region, ulong observerId, ulong connectionToken)
        {
            return new AcquireRetryKey(region, observerId, connectionToken);
        }

        /// <summary>
        /// 该事务的下一个尝试序号：首次为 1，同一连接代次内失败过则在既有次数上 +1。
        /// 连接代次是事务身份的一部分，因此换连接即为新事务，尝试序号从头计。
        /// </summary>
        private static int NextAttemptNumber(DomainLifecycleState state, AcquireRetryKey key)
        {
            return state.AcquireRetries.TryGetValue(key, out AcquireRetry previous)
                ? previous.Attempts + 1
                : 1;
        }

        /// <summary>
        /// 登记失败事务的重试：按类别取政策声明的起步间隔并温和倍增到各自上限。
        /// 成功即清除，不设次数上限——该事务在其观察者离开或连接代次失效前始终保留重试资格。
        /// </summary>
        private static void ScheduleAcquireRetry(
            DomainLifecycleState state,
            RegionKey region,
            ulong observerId,
            ulong connectionToken,
            bool deferred)
        {
            AcquireRetryKey key = TransactionKey(region, observerId, connectionToken);
            int attempts = NextAttemptNumber(state, key);
            RetrySchedule schedule = deferred ? state.Policy.DeferredRetry : state.Policy.FailedRetry;
            state.AcquireRetries[key] = new AcquireRetry(
                state.Clock + schedule.IntervalFor(attempts), attempts);
        }

        /// <summary>
        /// 处理该观察者已到期的暂缓/失败 acquire 重试。重试走与首次进入完全相同的
        /// <see cref="ProcessSingleRegionEntry"/>：成功即补齐复制贡献与需求并清除登记；
        /// 仍失败则按类别推迟下次重试。同位置更新不会重复产生进入差异，因此重试只能由登记驱动。
        /// </summary>
        private void ProcessAcquireRetries(
            DomainLifecycleState state,
            ulong observerId,
            ulong connectionToken,
            List<Action> compensations)
        {
            if (state.AcquireRetries.Count == 0) return;
            List<RegionKey> dueRegions = null;
            foreach (KeyValuePair<AcquireRetryKey, AcquireRetry> pair in state.AcquireRetries)
            {
                // 只有当前事务（本观察者 + 本连接代次）到点才重试：陈旧连接代次的登记没有
                // 提交资格，它的处置见 Observe 的连接代次失效分支。
                if (pair.Key.ObserverId != observerId
                    || pair.Key.ConnectionToken != connectionToken
                    || state.Clock < pair.Value.NextRetryAt) continue;
                if (dueRegions == null) dueRegions = new List<RegionKey>();
                dueRegions.Add(pair.Key.Region);
            }

            if (dueRegions == null) return;
            foreach (RegionKey region in dueRegions)
            {
                ProcessSingleRegionEntry(state, observerId, connectionToken, region, compensations);
            }
        }

        /// <summary>
        /// 提交到点的滞回释放，并驱动领域帧心跳。释放只在需求确为零、会话代次未变、
        /// 区域代次未回退时提交；代次前进则重新起算滞回窗口。每一步的失败都只作用于本区域：
        /// 补偿失败熔断该领域，其它区域与其它领域不受影响。
        /// </summary>
        private void FlushDomain(DomainLifecycleState state, float deltaTime)
        {
            var due = new List<PendingRelease>();
            foreach (PendingRelease pending in state.PendingReleases.Values)
            {
                if (pending.Deadline <= state.Clock && state.GetDemand(pending.RegionKey) == 0)
                    due.Add(pending);
            }

            foreach (PendingRelease pending in due)
            {
                PendingRelease currentPending;
                if (!state.PendingReleases.TryGetValue(pending.RegionKey, out currentPending)) continue;
                TryCommitRelease(state, currentPending);
            }

            try
            {
                state.Port.OnLifecycleTick(deltaTime);
            }
            catch (Exception ex)
            {
                Report(state, LifecycleEvents.LifecycleTick, ELifecycleOutcome.Failed,
                    ELifecyclePath.Fallback, state.SessionEpoch.Value,
                    "exception=" + ex.GetType().Name);
                throw;
            }

            try
            {
                state.Port.OnReplicationTick(deltaTime);
            }
            catch (Exception ex)
            {
                Report(state, LifecycleEvents.ReplicationTick, ELifecycleOutcome.Failed,
                    ELifecyclePath.Fallback, state.SessionEpoch.Value,
                    "exception=" + ex.GetType().Name);
                throw;
            }
        }

        /// <summary>
        /// 单区域释放提交边界：校验会话代次、区域代次与需求身份，再捕获原生快照、调用领域
        /// 释放并按结果决定补偿。拒绝提交时状态原样保留并重排下一次尝试。
        /// </summary>
        private void TryCommitRelease(DomainLifecycleState state, PendingRelease pending)
        {
            RegionKey region = pending.RegionKey;
            if (pending.SessionEpoch != state.SessionEpoch)
            {
                // 陈旧会话代次没有写入资格：保留 pending、重排下一次尝试，并留下失败闭合痕迹。
                RepostPendingRelease(state, pending);
                Report(state, LifecycleEvents.RegionRelease, ELifecycleOutcome.Rejected,
                    ELifecyclePath.Fallback, pending.SessionEpoch.Value,
                    "reason=session-generation-mismatch retryable=true state-retained=true",
                    region, 0UL, 0UL, pending.RegionGeneration.Value);
                Report(state, LifecycleEvents.RegionRelease, ELifecycleOutcome.Failed,
                    ELifecyclePath.Fallback, pending.SessionEpoch.Value,
                    "reason=session-generation-mismatch retryable=true pendingReleaseRetained=true",
                    region, 0UL, 0UL, pending.RegionGeneration.Value);
                return;
            }

            RegionGeneration current;
            try
            {
                current = state.Port.ReadRegionGeneration(region);
            }
            catch (Exception ex)
            {
                RepostPendingRelease(state, pending);
                Report(state, LifecycleEvents.RegionRelease, ELifecycleOutcome.Failed,
                    ELifecyclePath.Fallback, pending.SessionEpoch.Value,
                    "reason=generation-reader-failed retryable=true pendingReleaseRetained=true exception="
                    + ex.GetType().Name, region, 0UL, 0UL, pending.RegionGeneration.Value);
                return;
            }

            if (current.Value < pending.RegionGeneration.Value)
            {
                RepostPendingRelease(state, pending);
                Report(state, LifecycleEvents.RegionRelease, ELifecycleOutcome.Rejected,
                    ELifecyclePath.Fallback, pending.SessionEpoch.Value,
                    "reason=generation-regressed retryable=true state-retained=true current="
                    + current.Value + " stored=" + pending.RegionGeneration.Value,
                    region, 0UL, 0UL, pending.RegionGeneration.Value);
                return;
            }

            if (current != pending.RegionGeneration)
            {
                pending.RegionGeneration = current;
                pending.Deadline = state.Clock + state.Policy.HysteresisSeconds;
                state.Leases[region] = current;
                Report(state, LifecycleEvents.RegionReleaseDeferred, ELifecycleOutcome.Deferred,
                    ELifecyclePath.Spi, pending.SessionEpoch.Value,
                    "reason=stale-region-generation deadlineInSeconds="
                    + state.Policy.HysteresisSeconds.ToString("0.###"),
                    region, 0UL, 0UL, current.Value);
                return;
            }

            AttemptRelease(state, pending);
        }

        /// <summary>保留 pending 并重排下一次尝试：最短一个滞回窗口，避免失败风暴。</summary>
        private static void RepostPendingRelease(DomainLifecycleState state, PendingRelease pending)
        {
            pending.Deadline = state.Clock + Math.Max(state.Policy.HysteresisSeconds, 0.1f);
        }

        /// <summary>
        /// 释放提交本体。捕获原生快照后调用领域释放：业务拒绝不执行补偿（领域声明未产生副作用），
        /// 其它失败执行快照补偿。补偿本身失败即熔断该领域——宁可不关，也不留下不确定状态。
        /// </summary>
        private void AttemptRelease(DomainLifecycleState state, PendingRelease pending)
        {
            RegionKey region = pending.RegionKey;
            RegionGeneration generation = pending.RegionGeneration;
            Report(state, LifecycleEvents.RegionReleaseAttempt, ELifecycleOutcome.Attempt,
                ELifecyclePath.Spi, pending.SessionEpoch.Value, "authority=" + state.Port.DisplayName,
                region, 0UL, 0UL, generation.Value);

            object releaseState = null;
            try
            {
                releaseState = state.Port.CaptureRegionState(region);
                LeaseTicket ticket = CreateTicket(state, region, generation, 0);
                if (!ticket.Valid)
                {
                    // 票据身份不成立：不调用领域释放，也不补偿——没有任何原生副作用需要撤销。
                    RepostPendingRelease(state, pending);
                    Report(state, LifecycleEvents.RegionRelease, ELifecycleOutcome.Failed,
                        ELifecyclePath.Fallback, pending.SessionEpoch.Value,
                        "reason=invalid-ticket retryable=true pendingReleaseRetained=true "
                        + "compensationSucceeded=true", region, 0UL, 0UL, generation.Value);
                    return;
                }
                state.Port.OnRelease(ticket);
                state.PendingReleases.Remove(region);
                state.Leases.Remove(region);
                Report(state, LifecycleEvents.RegionRelease, ELifecycleOutcome.Success,
                    ELifecyclePath.Spi, pending.SessionEpoch.Value, "authority=" + state.Port.DisplayName,
                    region, 0UL, 0UL, generation.Value);
            }
            catch (Exception ex)
            {
                RepostPendingRelease(state, pending);
                EDomainFailureKind kind = state.Port.ClassifyReleaseFailure(ex);
                bool compensationRan = kind != EDomainFailureKind.Rejected;
                bool restored = !compensationRan
                    || TryRestoreRegionState(state, region, releaseState);
                Report(state, LifecycleEvents.RegionRelease, ELifecycleOutcome.Failed,
                    ELifecyclePath.Fallback, pending.SessionEpoch.Value,
                    "reason=" + DescribeReleaseFailure(kind) + " retryable=true pendingReleaseRetained=true "
                    + "compensationSucceeded=" + restored + " exception=" + ex.GetType().Name,
                    region, 0UL, 0UL, generation.Value);
            }
        }

        private static string DescribeReleaseFailure(EDomainFailureKind kind)
        {
            return kind == EDomainFailureKind.Rejected
                ? "release-rejected"
                : "external-release-may-have-side-effect";
        }

        private bool TryRestoreRegionState(DomainLifecycleState state, RegionKey regionKey, object snapshot)
        {
            try
            {
                state.Port.RestoreRegionState(regionKey, snapshot);
                return true;
            }
            catch (Exception ex)
            {
                MarkRepairRequired(state);
                Report(state, LifecycleEvents.RegionReleaseCompensation, ELifecycleOutcome.Failed,
                    ELifecyclePath.Fallback, state.SessionEpoch.Value,
                    "reason=region-state-restore-failed failClosed=true exception=" + ex.GetType().Name,
                    regionKey, 0UL, 0UL, state.GetStoredGeneration(regionKey).Value);
                return false;
            }
        }

        /// <summary>引擎级（跨域）诊断：会话边界这类不归属单个领域的转换。</summary>

        /// <summary>
        /// 事务回滚：把本领域的编排状态还原到本次观察者更新之前，并还原该观察者的投影贡献。
        /// 原生副作用由补偿列表单独还原；任一方向失败都具名记录，不静默跳过。
        /// </summary>
        private void RestoreState(
            DomainLifecycleState state,
            ulong observerId,
            bool hadObserver,
            bool hadConnection,
            ulong previousConnectionToken,
            HashSet<RegionKey> previousRegions,
            DomainLifecycleState.RegionSnapshot previousSnapshot,
            int previousObserverCount)
        {
            state.RestoreRegionSnapshot(previousSnapshot);
            state.ObserverCount = previousObserverCount;
            if (hadObserver) state.Observers.Add(observerId); else state.Observers.Remove(observerId);
            if (hadConnection) state.ConnectionTokens[observerId] = previousConnectionToken;
            else state.ConnectionTokens.Remove(observerId);
            if (hadObserver)
            {
                _projection.RestoreObserverRegions(state.DemandPolicy, observerId,
                    previousConnectionToken, previousRegions);
            }
            else
            {
                _projection.RemoveObserver(state.DemandPolicy, observerId);
            }
        }

        /// <summary>逆序执行补偿。任一补偿失败即熔断该领域（原生副作用不确定），但其它补偿继续。</summary>
        private bool RunCompensations(
            DomainLifecycleState state, List<Action> compensations, Exception original)
        {
            bool clean = true;
            for (int i = compensations.Count - 1; i >= 0; i--)
            {
                try
                {
                    compensations[i]();
                }
                catch (Exception ex)
                {
                    clean = false;
                    MarkRepairRequired(state);
                    Report(state, LifecycleEvents.ObserverCompensation, ELifecycleOutcome.Failed,
                        ELifecyclePath.Fallback, state.SessionEpoch.Value,
                        "transactionRolledBack=false compensationFailed=true original="
                        + original.GetType().Name + " exception=" + ex.GetType().Name,
                        role: "Shared");
                }
            }
            return clean;
        }

        /// <summary>捕获即将失效的连接代次的原生状态，供重连失败时恢复真实旧快照。</summary>
        private static void CaptureDisconnectCompensation(
            DomainLifecycleState state,
            ulong observerId,
            ulong connectionToken,
            bool shouldCapture,
            List<Action> compensations)
        {
            if (!shouldCapture) return;
            object disconnectState = state.Port.CaptureObserverDisconnectState(observerId, connectionToken);
            compensations.Add(() => state.Port.RestoreObserverDisconnectState(
                observerId, connectionToken, disconnectState));
        }

        private static LeaseTicket CreateTicket(
            DomainLifecycleState state, RegionKey region, RegionGeneration generation, int demand)
        {
            return new LeaseTicket(state.Port.DomainId, region, state.SessionEpoch, generation, demand, true);
        }

        /// <summary>引擎级（跨域）诊断：会话边界这类不归属单个领域的转换。</summary>
        private static LifecycleDiagnostic Build(
            DomainId domain,
            string eventName,
            ELifecycleOutcome outcome,
            ELifecyclePath path,
            ulong sessionEpoch,
            string detail,
            string role = "Host")
        {
            return new LifecycleDiagnostic(domain, eventName, outcome)
            {
                Role = role,
                Path = path,
                Severity = LifecycleDiagnostic.DeriveSeverity(outcome),
                SpiActive = LifecycleDiagnostic.IsSharedPath(path),
                SessionEpoch = sessionEpoch,
                Detail = detail
            };
        }

        /// <summary>领域级转换诊断：领域身份、角色与路径由调用点给出，事件字段保持统一命名。</summary>
        private void Report(
            DomainLifecycleState state,
            string eventName,
            ELifecycleOutcome outcome,
            ELifecyclePath path,
            ulong sessionEpoch,
            string detail,
            RegionKey? region = null,
            ulong connectionGeneration = 0UL,
            ulong observerId = 0UL,
            uint regionGeneration = 0U,
            int attempt = 0,
            string role = "Host",
            ELifecycleSeverity? severity = null)
        {
            _diagnostics.Transition(new LifecycleDiagnostic(state.Port.DomainId, eventName, outcome)
            {
                Role = role,
                Path = path,
                Severity = severity ?? LifecycleDiagnostic.DeriveSeverity(outcome),
                SpiActive = LifecycleDiagnostic.IsSharedPath(path),
                Region = region ?? default(RegionKey),
                HasRegion = region.HasValue,
                SessionEpoch = sessionEpoch,
                ConnectionGeneration = connectionGeneration,
                RegionGeneration = regionGeneration,
                ObserverId = observerId,
                Attempt = attempt,
                Detail = detail
            });
        }

        private DomainLifecycleState EnsureDomain(DomainId domain)
        {
            DomainLifecycleState state;
            if (!_domains.TryGetValue(domain, out state))
                throw new InvalidOperationException("领域未注册到共享编排引擎：" + domain);
            return state;
        }

        private void EnsureWritable(DomainLifecycleState state)
        {
            if (!_sessionActive)
            {
                Report(state, LifecycleEvents.SessionState, ELifecycleOutcome.Failed,
                    ELifecyclePath.Fallback, 0UL, "reason=session-not-active", role: "Shared");
                throw new InvalidOperationException(
                    "共享编排引擎没有活动会话：" + state.Port.DisplayName);
            }
            if (_writesSuspended)
            {
                // 会话身份不确定（或共享面故障恢复中）时阻止新写入与破坏性释放，但保留全部
                // 已建立的租约与需求——不确定不等于终止，更不等于清空其它领域。
                Report(state, LifecycleEvents.SessionSuspended, ELifecycleOutcome.Rejected,
                    ELifecyclePath.Fallback, state.SessionEpoch.Value,
                    "reason=writes-suspended cause=" + _writeSuspensionReason + " failClosed=true",
                    role: "Shared");
                throw new InvalidOperationException(
                    "会话身份不确定期间拒绝写入：" + state.Port.DisplayName);
            }
            if (state.RepairRequired)
            {
                Report(state, LifecycleEvents.SessionState, ELifecycleOutcome.Failed,
                    ELifecyclePath.Fallback, state.SessionEpoch.Value,
                    "reason=external-compensation-failed failClosed=true", role: "Shared");
                throw new InvalidOperationException(
                    "领域需要先修复才能继续写入：" + state.Port.DisplayName);
            }
        }

        private DomainLifecycleState FindRepairRequiredDomain()
        {
            foreach (DomainLifecycleState state in _domains.Values)
            {
                if (state.RepairRequired) return state;
            }
            return null;
        }

        private static uint ToAdapterEpoch(SessionEpoch epoch)
        {
            if (epoch.Value > uint.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(epoch), "领域 SPI 目前只接受 32 位会话代。");
            return (uint)epoch.Value;
        }

        /// <summary>该领域声明的 Demand Policy（诊断与半径来源取证）。</summary>
        public DemandPolicy GetDemandPolicy(DomainId domain) => EnsureDomain(domain).DemandPolicy;

        /// <summary>该领域声明的 Lifecycle Policy（滞回窗口与 retry 节奏取证）。</summary>
        public LifecyclePolicy GetLifecyclePolicy(DomainId domain) => EnsureDomain(domain).Policy;

        /// <summary>共享投影引擎当前为该领域算出的需求区域数（不是已物化租约数）。</summary>
        public int ProjectedDemandRegionCount(DomainId domain) =>
            _projection.GetDemandRegionCount(EnsureDomain(domain).DemandPolicy);

        public int GetDemand(DomainId domain, RegionKey regionKey) =>
            EnsureDomain(domain).GetDemand(regionKey);

        public bool IsLeased(DomainId domain, RegionKey regionKey) =>
            EnsureDomain(domain).Leases.ContainsKey(regionKey);

        /// <summary>取该区域的租约视图；无租约时返回 false，不以零值租约代替不存在。</summary>
        public bool TryGetLease(
            DomainId domain, RegionKey regionKey,
            out RegionGeneration regionGeneration, out int activeDemandCount)
        {
            DomainLifecycleState state = EnsureDomain(domain);
            if (state.Leases.TryGetValue(regionKey, out regionGeneration))
            {
                activeDemandCount = state.GetDemand(regionKey);
                return true;
            }

            regionGeneration = default(RegionGeneration);
            activeDemandCount = 0;
            return false;
        }

        public int ActiveLeaseCount(DomainId domain) => EnsureDomain(domain).Leases.Count;

        public int PendingReleaseCount(DomainId domain) => EnsureDomain(domain).PendingReleases.Count;

        public int DemandRegionCount(DomainId domain) => EnsureDomain(domain).Demand.Count;

        public int PendingAcquireRetryCount(DomainId domain) => EnsureDomain(domain).AcquireRetries.Count;

        /// <summary>
        /// 该区域当前有效的重试资格数（事务粒度）：同一区域两个观察者各自失败时各占一份，
        /// 不存在「后写者覆盖先写者」的区域单槽互吞。
        /// </summary>
        public int AcquireRetryQualificationCount(DomainId domain, RegionKey regionKey) =>
            EnsureDomain(domain).AcquireRetryQualificationCount(regionKey);

        public int ObserverCount(DomainId domain) => EnsureDomain(domain).ObserverCount;

        public int ReentryCount(DomainId domain) => EnsureDomain(domain).ReentryCount;

        public bool IsRepairRequired(DomainId domain) => EnsureDomain(domain).RepairRequired;

        public bool TryGetConnectionGeneration(DomainId domain, ulong observerId, out ulong connectionGeneration)
        {
            return EnsureDomain(domain).ConnectionTokens.TryGetValue(observerId, out connectionGeneration);
        }

        /// <summary>滞回释放登记：到期后由 Flush 提交，需求回升即撤销。</summary>
        private sealed class PendingRelease
        {
            internal SessionEpoch SessionEpoch;
            internal RegionKey RegionKey;
            internal RegionGeneration RegionGeneration;
            internal float Deadline;
        }

        /// <summary>
        /// 暂缓观察者的事实：为什么暂缓、从什么时候开始。贡献不在这里——它原样留在投影、
        /// 需求计数与租约里，暂缓只阻止「把它当成零需求」。
        /// </summary>
        private readonly struct DeferredObserver
        {
            internal DeferredObserver(string reason, float since)
            {
                Reason = reason ?? string.Empty;
                Since = since;
            }

            internal string Reason { get; }
            internal float Since { get; }
        }

        /// <summary>
        /// acquire retry 的事务身份。领域由每域状态承载、会话由会话边界承载，这里补齐
        /// 区域 + 观察者 + 连接代次——即「观察者贡献事务」的粒度。区域单槽会让两个观察者
        /// 互相吞并重试资格，因此键必须是事务，而不是区域。
        /// </summary>
        private readonly struct AcquireRetryKey : IEquatable<AcquireRetryKey>
        {
            internal AcquireRetryKey(RegionKey region, ulong observerId, ulong connectionToken)
            {
                Region = region;
                ObserverId = observerId;
                ConnectionToken = connectionToken;
            }

            internal RegionKey Region { get; }
            internal ulong ObserverId { get; }
            internal ulong ConnectionToken { get; }

            public bool Equals(AcquireRetryKey other) =>
                Region == other.Region
                && ObserverId == other.ObserverId
                && ConnectionToken == other.ConnectionToken;

            public override bool Equals(object obj) => obj is AcquireRetryKey other && Equals(other);

            public override int GetHashCode() =>
                ((Region.Packed * 397) ^ ObserverId.GetHashCode()) * 397 ^ ConnectionToken.GetHashCode();
        }

        /// <summary>
        /// 观察者的下一次更新驱动的重试登记。登记带事务身份，成功路径只清除自己那一份，
        /// 不吞并同一区域里其它观察者（或其它连接代次）的登记。
        /// </summary>
        private readonly struct AcquireRetry
        {
            internal AcquireRetry(float nextRetryAt, int attempts)
            {
                NextRetryAt = nextRetryAt;
                Attempts = attempts;
            }

            internal float NextRetryAt { get; }
            internal int Attempts { get; }
        }

        /// <summary>
        /// 单个领域的编排状态：需求引用计数、租约与区域代次、滞回登记、retry 登记与身份轴。
        /// 领域集合在注册后不可变，因此每域一份状态、共享同一份空间事实与投影引擎。
        /// </summary>
        private sealed class DomainLifecycleState
        {
            internal DomainLifecycleState(DemandPolicy demandPolicy, IDomainExecutionPort port)
            {
                DemandPolicy = demandPolicy;
                Port = port;
                Policy = port.LifecyclePolicy;
            }

            internal DemandPolicy DemandPolicy { get; }
            internal IDomainExecutionPort Port { get; }
            internal LifecyclePolicy Policy { get; }

            internal readonly Dictionary<RegionKey, int> Demand = new Dictionary<RegionKey, int>();
            internal readonly Dictionary<RegionKey, RegionGeneration> Leases =
                new Dictionary<RegionKey, RegionGeneration>();
            internal readonly Dictionary<RegionKey, PendingRelease> PendingReleases =
                new Dictionary<RegionKey, PendingRelease>();
            internal readonly Dictionary<AcquireRetryKey, AcquireRetry> AcquireRetries =
                new Dictionary<AcquireRetryKey, AcquireRetry>();
            internal readonly HashSet<ulong> Observers = new HashSet<ulong>();
            internal readonly Dictionary<ulong, ulong> ConnectionTokens = new Dictionary<ulong, ulong>();

            /// <summary>
            /// Deferred Observer Demand：本拍观测不可用（记录读不全、身份不可判定）但无法证明
            /// 已离开的观察者。它们的既有贡献原样保留——暂缓不是零需求，也不是破坏性释放。
            /// </summary>
            internal readonly Dictionary<ulong, DeferredObserver> DeferredObservers =
                new Dictionary<ulong, DeferredObserver>();

            /// <summary>持续暂缓的有界心跳：首条 + 按间隔重复 + 显式终止；清空时写闭环记录。</summary>
            internal BoundedHeartbeat DeferredHeartbeat;

            /// <summary>熔断领域的有界心跳：被推进路径跳过时按它写出，而不是一条去重日志后静默。</summary>
            internal BoundedHeartbeat FaultHeartbeat;

            internal SessionEpoch SessionEpoch;
            internal int ObserverCount;
            internal int ReentryCount;
            internal bool RepairRequired;
            internal float Clock;

            internal int GetDemand(RegionKey regionKey) =>
                Demand.TryGetValue(regionKey, out int count) ? count : 0;

            internal RegionGeneration GetStoredGeneration(RegionKey regionKey) =>
                Leases.TryGetValue(regionKey, out RegionGeneration generation)
                    ? generation
                    : default(RegionGeneration);

            internal List<RegionKey> LeaseKeys() => new List<RegionKey>(Leases.Keys);

            internal void Clear(bool recoverFaults = true)
            {
                Demand.Clear();
                Leases.Clear();
                PendingReleases.Clear();
                AcquireRetries.Clear();
                Observers.Clear();
                ConnectionTokens.Clear();
                DeferredObservers.Clear();
                DeferredHeartbeat.Stop();
                FaultHeartbeat.Stop();
                ObserverCount = 0;
                ReentryCount = 0;
                Clock = 0f;
                SessionEpoch = default(SessionEpoch);
                // 会话边界是熔断领域的有限恢复点：会话收尾成功即视为修复；收尾失败会在
                // EndSession 里重新标记，不在这里静默清掉。
                if (recoverFaults) RepairRequired = false;
            }

            internal int DecrementDemand(RegionKey region)
            {
                int count;
                if (!Demand.TryGetValue(region, out count) || count <= 0)
                    throw new InvalidOperationException(
                        "区域需求引用计数下溢：" + Port.DisplayName + "@" + region);
                if (count == 1)
                {
                    Demand.Remove(region);
                    return 0;
                }

                Demand[region] = count - 1;
                return count - 1;
            }

            /// <summary>撤销属于该观察者的全部重试登记——观察者已离开，它的重试资格随之作废。</summary>
            internal void DropAcquireRetriesOwnedBy(ulong observerId)
            {
                List<AcquireRetryKey> orphaned = MatchingKeys(key => key.ObserverId == observerId);
                if (orphaned == null) return;
                foreach (AcquireRetryKey key in orphaned) AcquireRetries.Remove(key);
            }

            /// <summary>
            /// 连接代次失效：撤销该观察者遗留的陈旧代次登记并返回被撤销的条目（供补偿还回）。
            /// 陈旧代次没有提交资格，它不能继续替新连接驱动重试；新连接按自己的事务在
            /// <see cref="ProcessSingleRegionEntry"/> 里重新登记。
            /// </summary>
            internal List<KeyValuePair<AcquireRetryKey, AcquireRetry>> RemoveAcquireRetriesForGeneration(
                ulong observerId, ulong connectionToken)
            {
                List<AcquireRetryKey> stale = MatchingKeys(key =>
                    key.ObserverId == observerId && key.ConnectionToken != connectionToken);
                var removed = new List<KeyValuePair<AcquireRetryKey, AcquireRetry>>();
                if (stale == null) return removed;
                foreach (AcquireRetryKey key in stale)
                {
                    removed.Add(new KeyValuePair<AcquireRetryKey, AcquireRetry>(key, AcquireRetries[key]));
                    AcquireRetries.Remove(key);
                }
                return removed;
            }

            /// <summary>该区域是否留有该观察者的未完成事务登记（含陈旧连接代次）。</summary>
            internal bool HasAcquireRetryFor(RegionKey region, ulong observerId)
            {
                foreach (AcquireRetryKey key in AcquireRetries.Keys)
                {
                    if (key.Region == region && key.ObserverId == observerId) return true;
                }
                return false;
            }

            /// <summary>撤销该观察者在该区域的全部未完成事务登记，返回被撤销的条目供补偿还原。</summary>
            internal List<KeyValuePair<AcquireRetryKey, AcquireRetry>> RemoveAcquireRetriesFor(
                RegionKey region, ulong observerId)
            {
                List<AcquireRetryKey> keys = MatchingKeys(key =>
                    key.Region == region && key.ObserverId == observerId);
                var removed = new List<KeyValuePair<AcquireRetryKey, AcquireRetry>>();
                if (keys == null) return removed;
                foreach (AcquireRetryKey key in keys)
                {
                    removed.Add(new KeyValuePair<AcquireRetryKey, AcquireRetry>(key, AcquireRetries[key]));
                    AcquireRetries.Remove(key);
                }
                return removed;
            }

            /// <summary>补偿路径：把被撤销的登记原样放回。</summary>
            internal void RestoreAcquireRetries(
                List<KeyValuePair<AcquireRetryKey, AcquireRetry>> removed)
            {
                if (removed == null) return;
                foreach (KeyValuePair<AcquireRetryKey, AcquireRetry> pair in removed)
                    AcquireRetries[pair.Key] = pair.Value;
            }

            /// <summary>该区域当前有效的重试资格数（事务粒度）：同区多观察者各占一份。</summary>
            internal int AcquireRetryQualificationCount(RegionKey region)
            {
                int count = 0;
                foreach (AcquireRetryKey key in AcquireRetries.Keys)
                {
                    if (key.Region == region) count++;
                }
                return count;
            }

            private List<AcquireRetryKey> MatchingKeys(Func<AcquireRetryKey, bool> matches)
            {
                List<AcquireRetryKey> matched = null;
                foreach (AcquireRetryKey key in AcquireRetries.Keys)
                {
                    if (!matches(key)) continue;
                    if (matched == null) matched = new List<AcquireRetryKey>();
                    matched.Add(key);
                }
                return matched;
            }

            internal RegionSnapshot CaptureRegionSnapshot() => new RegionSnapshot(this);

            internal void RestoreRegionSnapshot(RegionSnapshot snapshot)
            {
                snapshot.ApplyTo(
                    Demand, Leases, PendingReleases, AcquireRetries);
            }

            /// <summary>事务回滚用的区域状态快照：只含编排状态，不含原生状态。</summary>
            internal sealed class RegionSnapshot
            {
                private readonly Dictionary<RegionKey, int> _demand;
                private readonly Dictionary<RegionKey, RegionGeneration> _leases;
                private readonly Dictionary<RegionKey, PendingRelease> _pendingReleases;
                private readonly Dictionary<AcquireRetryKey, AcquireRetry> _acquireRetries;

                internal RegionSnapshot(DomainLifecycleState state)
                {
                    _demand = new Dictionary<RegionKey, int>(state.Demand);
                    _leases = new Dictionary<RegionKey, RegionGeneration>(state.Leases);
                    _pendingReleases = new Dictionary<RegionKey, PendingRelease>(state.PendingReleases.Count);
                    foreach (KeyValuePair<RegionKey, PendingRelease> pair in state.PendingReleases)
                    {
                        _pendingReleases[pair.Key] = new PendingRelease
                        {
                            SessionEpoch = pair.Value.SessionEpoch,
                            RegionKey = pair.Value.RegionKey,
                            RegionGeneration = pair.Value.RegionGeneration,
                            Deadline = pair.Value.Deadline
                        };
                    }
                    _acquireRetries = new Dictionary<AcquireRetryKey, AcquireRetry>(state.AcquireRetries);
                }

                internal void ApplyTo(
                    Dictionary<RegionKey, int> demand,
                    Dictionary<RegionKey, RegionGeneration> leases,
                    Dictionary<RegionKey, PendingRelease> pendingReleases,
                    Dictionary<AcquireRetryKey, AcquireRetry> acquireRetries)
                {
                    demand.Clear();
                    foreach (KeyValuePair<RegionKey, int> pair in _demand) demand[pair.Key] = pair.Value;
                    leases.Clear();
                    foreach (KeyValuePair<RegionKey, RegionGeneration> pair in _leases)
                        leases[pair.Key] = pair.Value;
                    pendingReleases.Clear();
                    foreach (KeyValuePair<RegionKey, PendingRelease> pair in _pendingReleases)
                        pendingReleases[pair.Key] = pair.Value;
                    acquireRetries.Clear();
                    foreach (KeyValuePair<AcquireRetryKey, AcquireRetry> pair in _acquireRetries)
                        acquireRetries[pair.Key] = pair.Value;
                }
            }
        }
    }
}
