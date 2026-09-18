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
            DomainLifecycleState repairing = FindRepairRequiredDomain();
            if (repairing != null)
            {
                Report(repairing, LifecycleEvents.SessionBegin, ELifecycleOutcome.Rejected,
                    ELifecyclePath.Fallback, sessionEpoch.Value,
                    "reason=repair-required failClosed=true",
                    severity: ELifecycleSeverity.Error);
                throw new InvalidOperationException(
                    "领域需要先修复才能开始新会话：" + repairing.Port.DisplayName);
            }
            if (_sessionActive && _sessionEpoch == sessionEpoch)
            {
                _diagnostics.TransitionOnce("lifecycle-session-already-active",
                    Build(default, LifecycleEvents.SessionBegin, ELifecycleOutcome.Skipped,
                        ELifecyclePath.Spi, sessionEpoch.Value, null, "reason=already-active"));
                return;
            }

            if (_sessionActive) EndSession();

            uint adapterEpoch = ToAdapterEpoch(sessionEpoch);
            ClearManagedSessionState();
            foreach (DomainLifecycleState state in _domains.Values)
            {
                try
                {
                    state.Port.OnSessionBegin(adapterEpoch);
                    state.Port.ResetReplication(adapterEpoch);
                }
                catch (Exception ex)
                {
                    CleanupAfterFailedBegin(state, sessionEpoch, adapterEpoch, ex);
                    ClearManagedSessionState();
                    Report(state, LifecycleEvents.SessionBegin, ELifecycleOutcome.Failed,
                        ELifecyclePath.Fallback, sessionEpoch.Value,
                        "reason=external-session-initialization-failed failClosed=true exception="
                        + ex.GetType().Name);
                    throw;
                }
                state.SessionEpoch = sessionEpoch;
                state.RepairRequired = false;
            }

            _sessionEpoch = sessionEpoch;
            _sessionActive = true;
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
                foreach (DomainLifecycleState state in cleanupFailed) state.RepairRequired = true;
            }
            if (cleanupFailure != null) throw cleanupFailure;
        }

        private void ClearManagedSessionState()
        {
            // 会话边界同时清空唯一空间事实与全部领域投影：旧 Session 的观察者不存在于新会话，
            // 残留事实会让「唯一事实」跨会话失真。
            _projection.EndSession();
            foreach (DomainLifecycleState state in _domains.Values) state.Clear();
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
                    state.Port.OnObserverDisconnected(observerId, previousConnectionToken);
                }
                state.ConnectionTokens[observerId] = connectionToken;
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
            foreach (DomainLifecycleState state in _domains.Values)
            {
                // 时间轴与刷新是引擎内部驱动的推进，不是调用方发起的写入：熔断中的领域被跳过并
                // 留下有界诊断，其它领域照常推进时间、retry 与滞回——否则一个领域的
                // repair-required 就会变成跨域熔断。
                EnsureSessionActive(state);
                if (state.RepairRequired)
                {
                    ReportSkippedFaultedDomain(state);
                    continue;
                }
                state.Clock += deltaTime;
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
        /// 熔断领域被推进路径跳过时的有界诊断：首次跳过写出一条，此后按 key 去重，
        /// 既不会逐帧淹没日志，也不会静默失明。跳过只影响该领域——其它领域照常推进；
        /// 熔断领域自己的显式写入仍由 <see cref="EnsureWritable"/> 拒绝。
        /// </summary>
        private void ReportSkippedFaultedDomain(DomainLifecycleState state)
        {
            _diagnostics.TransitionOnce("lifecycle-repair-required-" + state.Port.DomainId,
                Build(state.Port.DomainId, LifecycleEvents.SessionState, ELifecycleOutcome.Skipped,
                    ELifecyclePath.Fallback, 0UL,
                    "reason=repair-required failClosed=true skipped=advance", "Shared"));
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
                AcquireRetry leavingRetry;
                if (state.AcquireRetries.TryGetValue(region, out leavingRetry)
                    && leavingRetry.ObserverId == observerId)
                {
                    state.AcquireRetries.Remove(region);
                    compensations.Add(() => state.AcquireRetries[region] = leavingRetry);
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
                            attempt: AcquireAttemptCount(state, region, observerId));
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
                    bool quietSteadyState = deferred
                        && state.AcquireRetries.TryGetValue(region, out AcquireRetry currentRetry)
                        && currentRetry.ObserverId == observerId
                        && currentRetry.Attempts >= state.Policy.DeferredRetry.QuietAttempts;
                    ScheduleAcquireRetry(state, region, observerId, deferred);
                    if (!quietSteadyState)
                    {
                        Report(state, LifecycleEvents.RegionAcquire,
                            deferred ? ELifecycleOutcome.Deferred : ELifecycleOutcome.Skipped,
                            ELifecyclePath.Fallback, state.SessionEpoch.Value,
                            "reason=" + (deferred ? "region-snapshot-deferred" : failureReason)
                            + " attempts=" + state.AcquireRetries[region].Attempts
                            + (acquireFailure == null ? string.Empty : " exception=" + DescribeFailure(acquireFailure)),
                            region, connectionToken, observerId, state.GetStoredGeneration(region).Value,
                            attempt: state.AcquireRetries[region].Attempts,
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

            // 只清除当前观察者自己的登记：其他观察者对该区域的待重试登记仍然有效。
            // 清除必须登记补偿——登记清除若游离在事务补偿之外，任何一次回滚都会留下
            //「区域在投影里、无需求、无 retry 登记」的失衡态。
            AcquireRetry ownRetry;
            if (state.AcquireRetries.TryGetValue(region, out ownRetry) && ownRetry.ObserverId == observerId)
            {
                state.AcquireRetries.Remove(region);
                compensations.Add(() => state.AcquireRetries[region] = ownRetry);
            }
        }

        /// <summary>
        /// 本区域这次 acquire 是第几次尝试：沿用该观察者已登记的失败次数（+1），
        /// 没有登记即为首次尝试。诊断用它区分「一次成功」与「重试后成功」。
        /// </summary>
        private static int AcquireAttemptCount(
            DomainLifecycleState state, RegionKey region, ulong observerId)
        {
            return NextAttemptNumber(state, region, observerId);
        }

        /// <summary>该观察者在该区域的下一个尝试序号：首次为 1，已失败过则在既有次数上 +1。</summary>
        private static int NextAttemptNumber(
            DomainLifecycleState state, RegionKey region, ulong observerId)
        {
            AcquireRetry previous;
            return state.AcquireRetries.TryGetValue(region, out previous)
                && previous.ObserverId == observerId
                    ? previous.Attempts + 1
                    : 1;
        }

        /// <summary>
        /// 登记失败区域的重试：按类别取政策声明的起步间隔并温和倍增到各自上限。
        /// 成功即清除，不设次数上限——区域在本观察者离开前始终保留重试资格。
        /// </summary>
        private static void ScheduleAcquireRetry(
            DomainLifecycleState state, RegionKey region, ulong observerId, bool deferred)
        {
            int attempts = NextAttemptNumber(state, region, observerId);
            RetrySchedule schedule = deferred ? state.Policy.DeferredRetry : state.Policy.FailedRetry;
            state.AcquireRetries[region] = new AcquireRetry(
                observerId, state.Clock + schedule.IntervalFor(attempts), attempts);
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
            foreach (KeyValuePair<RegionKey, AcquireRetry> pair in state.AcquireRetries)
            {
                if (pair.Value.ObserverId == observerId && state.Clock >= pair.Value.NextRetryAt)
                {
                    if (dueRegions == null) dueRegions = new List<RegionKey>();
                    dueRegions.Add(pair.Key);
                }
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
                state.RepairRequired = true;
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
                    state.RepairRequired = true;
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
        /// acquire 失败区域的重试登记：由观察者的下一次更新驱动重试。登记带 observer 归属，
        /// 成功路径只清除自己的登记，不吞并他人的重试资格。
        /// </summary>
        private readonly struct AcquireRetry
        {
            internal AcquireRetry(ulong observerId, float nextRetryAt, int attempts)
            {
                ObserverId = observerId;
                NextRetryAt = nextRetryAt;
                Attempts = attempts;
            }

            internal ulong ObserverId { get; }
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
            internal readonly Dictionary<RegionKey, AcquireRetry> AcquireRetries =
                new Dictionary<RegionKey, AcquireRetry>();
            internal readonly HashSet<ulong> Observers = new HashSet<ulong>();
            internal readonly Dictionary<ulong, ulong> ConnectionTokens = new Dictionary<ulong, ulong>();

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

            internal void Clear()
            {
                Demand.Clear();
                Leases.Clear();
                PendingReleases.Clear();
                AcquireRetries.Clear();
                Observers.Clear();
                ConnectionTokens.Clear();
                ObserverCount = 0;
                ReentryCount = 0;
                Clock = 0f;
                SessionEpoch = default(SessionEpoch);
                RepairRequired = false;
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

            /// <summary>撤销属于该观察者的重试登记——观察者已离开，其重试资格随之作废。</summary>
            internal void DropAcquireRetriesOwnedBy(ulong observerId)
            {
                List<RegionKey> orphaned = null;
                foreach (KeyValuePair<RegionKey, AcquireRetry> pair in AcquireRetries)
                {
                    if (pair.Value.ObserverId != observerId) continue;
                    if (orphaned == null) orphaned = new List<RegionKey>();
                    orphaned.Add(pair.Key);
                }
                if (orphaned == null) return;
                foreach (RegionKey region in orphaned) AcquireRetries.Remove(region);
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
                private readonly Dictionary<RegionKey, AcquireRetry> _acquireRetries;

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
                    _acquireRetries = new Dictionary<RegionKey, AcquireRetry>(state.AcquireRetries);
                }

                internal void ApplyTo(
                    Dictionary<RegionKey, int> demand,
                    Dictionary<RegionKey, RegionGeneration> leases,
                    Dictionary<RegionKey, PendingRelease> pendingReleases,
                    Dictionary<RegionKey, AcquireRetry> acquireRetries)
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
                    foreach (KeyValuePair<RegionKey, AcquireRetry> pair in _acquireRetries)
                        acquireRetries[pair.Key] = pair.Value;
                }
            }
        }
    }
}
