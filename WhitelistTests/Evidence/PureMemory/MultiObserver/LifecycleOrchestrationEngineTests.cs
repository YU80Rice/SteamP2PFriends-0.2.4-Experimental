using SteamP2PFriends.Core.Identity;
using SteamP2PFriends.MultiObserver.Demand;
using SteamP2PFriends.MultiObserver.Lifecycle;
using SteamP2PFriends.MultiObserver.SPI;
using System;
using System.Collections.Generic;

namespace SteamP2PFriends.WhitelistTests
{
    /// <summary>
    /// 票 03：共享 Lifecycle Orchestration Engine 的最高接缝契约。引擎拥有需求聚合、
    /// 0 -&gt; 1 Acquire、N -&gt; 0 滞回 Release、身份校验、retry、补偿调度与局部故障隔离；
    /// 领域只经 Domain Execution Port 执行原生操作。
    ///
    /// 这些用例用「测试域」而不是 Resource：它们锁的是共享引擎的编排语义本身，
    /// 任何领域（含未来第三域）都必须得到同样的行为。Resource 的已验收表征语义由
    /// ResourceProductionControlSeamTests 单独锁定，两者互不替代。
    /// </summary>
    internal static class LifecycleOrchestrationEngineTests
    {
        private static readonly DomainId PrimaryDomain = DomainIds.Resource;
        private static readonly DomainId SecondaryDomain = DomainIds.Collision;

        /// <summary>
        /// 建立一份共享控制面（唯一空间事实 + 唯一投影引擎 + 唯一编排引擎），并按需注册
        /// 一或两个测试域。生产接线在协调器与 Resource 生产接缝中完成同一组构造。
        /// </summary>
        private static LifecycleOrchestrationEngine CreateEngine(
            out FakeDomainPort primary,
            out FakeDomainPort secondary,
            out DemandProjectionEngine projection,
            byte radius = 0,
            float hysteresisSeconds = 2.0f,
            bool registerSecondary = false)
        {
            var authority = new ObserverSpatialAuthority();
            projection = new DemandProjectionEngine(authority);
            var engine = new LifecycleOrchestrationEngine(projection);
            DemandPolicy primaryPolicy = new DemandPolicy(PrimaryDomain, radius, 64,
                EDemandRegionShape.ChebyshevSquare2D, "test-primary", presence => presence.GameplayAuthorized);
            projection.Register(primaryPolicy);
            primary = new FakeDomainPort(PrimaryDomain, hysteresisSeconds);
            engine.Register(primaryPolicy, primary);

            secondary = null;
            if (registerSecondary)
            {
                DemandPolicy secondaryPolicy = new DemandPolicy(SecondaryDomain, radius, 64,
                    EDemandRegionShape.ChebyshevSquare2D, "test-secondary",
                    presence => presence.GameplayAuthorized);
                projection.Register(secondaryPolicy);
                secondary = new FakeDomainPort(SecondaryDomain, hysteresisSeconds);
                engine.Register(secondaryPolicy, secondary);
            }
            return engine;
        }

        private static bool Throws(Action action)
        {
            try
            {
                action();
                return false;
            }
            catch (Exception)
            {
                return true;
            }
        }

        private static LifecycleOrchestrationEngine CreateSingleEngine(out FakeDomainPort port)
        {
            FakeDomainPort secondary;
            DemandProjectionEngine projection;
            LifecycleOrchestrationEngine engine =
                CreateEngine(out port, out secondary, out projection);
            return engine;
        }

        private static LifecycleOrchestrationEngine CreateSingleEngine(out FakeDomainPort port, byte radius)
        {
            FakeDomainPort secondary;
            DemandProjectionEngine projection;
            LifecycleOrchestrationEngine engine =
                CreateEngine(out port, out secondary, out projection, radius: radius);
            return engine;
        }

        /// <summary>
        /// 断言全部条件成立；任一条件不成立时抛出携带上下文取值的异常，使失败在测试输出里
        /// 自带取证信息（哪些计数、租约、代次与诊断记录与期望不符），不需要重跑调试。
        /// </summary>
        private static bool Expect(params object[] conditionsAndContext)
        {
            for (int i = 0; i < conditionsAndContext.Length - 1; i++)
            {
                if (conditionsAndContext[i] is bool flag && !flag)
                    throw new InvalidOperationException(
                        "期望不成立：" + conditionsAndContext[conditionsAndContext.Length - 1]);
            }
            return true;
        }

        private static string Describe(LifecycleDiagnostic diagnostic)
        {
            return diagnostic == null
                ? "none"
                : diagnostic.EventName + "/" + diagnostic.Outcome + "/region="
                    + (diagnostic.HasRegion ? diagnostic.Region.ToString() : "-")
                    + "/gen=" + diagnostic.RegionGeneration + "/attempt=" + diagnostic.Attempt
                    + "/observer=" + diagnostic.ObserverId + "/epoch=" + diagnostic.SessionEpoch
                    + "/path=" + diagnostic.Path + "/detail=" + diagnostic.Detail;
        }

        /// <summary>
        /// Registration Closure：首个会话开始后本次会话的领域集合不可变，再注册即拒绝——
        /// 否则会话中途接入的领域拿不到本会话的 Session Epoch，只会在错误代次上写入。
        /// </summary>
        internal static bool Test_LOE13_RegistrationClosureFreezesDomainSet()
        {
            FakeDomainPort port;
            DemandProjectionEngine projection;
            FakeDomainPort secondary;
            LifecycleOrchestrationEngine engine = CreateEngine(out port, out secondary, out projection);
            DemandPolicy latePolicy = new DemandPolicy(SecondaryDomain, 0, 64,
                EDemandRegionShape.ChebyshevSquare2D, "test-late", presence => presence.GameplayAuthorized);
            projection.Register(latePolicy);
            var late = new FakeDomainPort(SecondaryDomain, 2.0f);

            bool openBeforeSession = !engine.IsRegistrationClosed;
            engine.Register(latePolicy, late);
            bool secondDomainAccepted = engine.IsRegistered(SecondaryDomain);

            engine.BeginSession(new SessionEpoch(11UL));
            bool closedDuringSession = engine.IsRegistrationClosed;
            bool lateRegistrationRejected = Throws(() => engine.Register(latePolicy, late));

            DemandPolicy thirdPolicy = new DemandPolicy(DomainIds.Item, 0, 64,
                EDemandRegionShape.ChebyshevSquare2D, "test-third", presence => presence.GameplayAuthorized);
            projection.Register(thirdPolicy);
            bool newDomainRejected = Throws(() => engine.Register(
                thirdPolicy, new FakeDomainPort(DomainIds.Item, 2.0f)));

            engine.Observe(PrimaryDomain, 100UL, 1001UL, 10, 10, true);
            bool sessionBehaviourIntact = engine.IsLeased(PrimaryDomain, new RegionKey(10, 10))
                && engine.ActiveLeaseCount(SecondaryDomain) == 0;

            // 闭合边界是会话本身：本局结束、下一局开始前可以重新注册（「本次会话」不可变）。
            engine.EndSession();
            bool reopenedBetweenSessions = !engine.IsRegistrationClosed
                && !Throws(() => engine.Register(
                    thirdPolicy, new FakeDomainPort(DomainIds.Item, 2.0f)))
                && engine.IsRegistered(DomainIds.Item);
            engine.BeginSession(new SessionEpoch(12UL));
            bool closedAgainInNewSession = engine.IsRegistrationClosed;

            return openBeforeSession
                && secondDomainAccepted
                && closedDuringSession
                && lateRegistrationRejected
                && newDomainRejected
                && sessionBehaviourIntact
                && reopenedBetweenSessions
                && closedAgainInNewSession;
        }

        /// <summary>
        /// 成功 Acquire 同样产生转换诊断：0 -&gt; 1 是可观测的转换，而不是只在失败时才留下痕迹。
        /// </summary>
        internal static bool Test_LOE14_SuccessfulAcquireIsObservable()
        {
            var sink = new RecordingDiagnostics();
            var projection = new DemandProjectionEngine(new ObserverSpatialAuthority());
            var engine = new LifecycleOrchestrationEngine(projection, sink);
            DemandPolicy policy = new DemandPolicy(PrimaryDomain, 0, 64,
                EDemandRegionShape.ChebyshevSquare2D, "test-primary", presence => presence.GameplayAuthorized);
            projection.Register(policy);
            var port = new FakeDomainPort(PrimaryDomain, 2.0f) { Generation = 5U };
            engine.Register(policy, port);
            engine.BeginSession(new SessionEpoch(21UL));

            RegionKey region = new RegionKey(3, 4);
            engine.Observe(PrimaryDomain, 100UL, 1001UL, region.X, region.Y, true);

            LifecycleDiagnostic acquired = sink.Find(
                LifecycleEvents.RegionAcquire, ELifecycleOutcome.Success);
            return acquired != null
                && acquired.Domain == PrimaryDomain
                && acquired.HasRegion && acquired.Region == region
                && acquired.SessionEpoch == 21UL
                && acquired.RegionGeneration == 5U
                && acquired.ObserverId == 100UL
                && acquired.ConnectionGeneration == 1001UL
                && acquired.Path == ELifecyclePath.Spi
                && acquired.Attempt == 1
                && acquired.Detail != null
                && acquired.Detail.Contains("demand=1")
                && acquired.Detail.Contains("reason=region-demand-entered")
                && port.Acquires.Count == 1;
        }

        /// <summary>
        /// 熔断领域的推进被跳过，而不是把其它领域一起拖停：一个领域补偿失败进入
        /// repair-required 之后，另一个领域的时间轴、retry 与滞回释放照常推进。
        /// </summary>
        internal static bool Test_LOE15_FaultedDomainDoesNotStallOthers()
        {
            FakeDomainPort primary;
            FakeDomainPort secondary;
            DemandProjectionEngine projection;
            LifecycleOrchestrationEngine engine = CreateEngine(
                out primary, out secondary, out projection, registerSecondary: true);
            engine.BeginSession(new SessionEpoch(11UL));
            RegionKey region = new RegionKey(10, 10);

            engine.Observe(SecondaryDomain, 100UL, 1001UL, region.X, region.Y, true);
            engine.RemoveObserver(SecondaryDomain, 100UL);

            // 让主领域的一次观察者更新以「补偿也失败」收尾，从而进入 repair-required。
            primary.ThrowOnReplicationEnter = true;
            primary.ThrowOnReplicationRestore = true;
            bool threw = Throws(() => engine.Observe(PrimaryDomain, 200UL, 2001UL, region.X, region.Y, true));
            bool primaryFaulted = threw && engine.IsRepairRequired(PrimaryDomain);

            // 熔断领域的显式写入仍被拒绝（失败闭合），但引擎整体的时间推进不被它中止。
            bool explicitWriteRejected = Throws(() =>
                engine.Observe(PrimaryDomain, 200UL, 2001UL, region.X, region.Y, true));

            engine.AdvanceTime(2.0f);
            engine.Flush(0f);

            bool otherDomainStillAdvanced = secondary.Releases.Count == 1
                && engine.PendingReleaseCount(SecondaryDomain) == 0
                && !engine.IsLeased(SecondaryDomain, region);
            bool sessionSurvives = engine.IsSessionActive;
            bool noLeakFromFaultedDomain = engine.ActiveLeaseCount(PrimaryDomain) == 0;

            return primaryFaulted
                && explicitWriteRejected
                && otherDomainStillAdvanced
                && sessionSurvives
                && noLeakFromFaultedDomain;
        }

        /// <summary>记录全部转换诊断的测试出口：只观察，不参与任何控制流。</summary>
        private sealed class RecordingDiagnostics : ILifecycleDiagnostics
        {
            internal readonly List<LifecycleDiagnostic> Entries = new List<LifecycleDiagnostic>();

            public void Transition(LifecycleDiagnostic diagnostic) => Entries.Add(diagnostic);

            public void TransitionOnce(string onceKey, LifecycleDiagnostic diagnostic) =>
                Entries.Add(diagnostic);

            internal LifecycleDiagnostic Find(string eventName, ELifecycleOutcome outcome)
            {
                return Entries.Find(entry =>
                    entry.EventName == eventName && entry.Outcome == outcome);
            }
        }

        /// <summary>
        /// 测试域执行端口：只执行原生语义并把失败翻译成引擎可执行的结果分类。
        /// 它不聚合需求、不持有租约或滞回——那些都是共享引擎的状态。
        /// </summary>
        private sealed class FakeDomainPort : IDomainExecutionPort
        {
            internal readonly List<LeaseTicket> Acquires = new List<LeaseTicket>();
            internal readonly List<LeaseTicket> Releases = new List<LeaseTicket>();
            internal readonly List<RegionKey> ReplicationEntered = new List<RegionKey>();
            internal readonly List<RegionKey> ReplicationExited = new List<RegionKey>();
            internal uint Generation = 1U;
            internal bool ThrowOnAcquire;
            internal bool ThrowOnRelease;
            internal bool ThrowOnReplicationEnter;
            internal bool ThrowOnReplicationRestore;
            internal RegionKey FailAcquireFor;
            internal int RestoreReplicationCalls;

            internal FakeDomainPort(DomainId domainId, float hysteresisSeconds)
            {
                DomainId = domainId;
                LifecyclePolicy = new LifecyclePolicy(
                    hysteresisSeconds,
                    new RetrySchedule(2.0f, 32f, 5),
                    new RetrySchedule(10.0f, 60f, int.MaxValue),
                    "test-domain");
            }

            public DomainId DomainId { get; }
            public string DisplayName => DomainId.Value;
            public LifecyclePolicy LifecyclePolicy { get; }

            public void OnSessionBegin(uint sessionEpoch) { }
            public void OnSessionEnd() { }
            public void ResetReplication(uint sessionEpoch) { }

            public void OnAcquire(LeaseTicket ticket)
            {
                Acquires.Add(ticket);
                if (ThrowOnAcquire || (FailAcquireFor == ticket.RegionKey && ticket.RegionKey != default(RegionKey)))
                    throw new InvalidOperationException("test acquire failure");
            }

            public void OnRelease(LeaseTicket ticket)
            {
                // 只登记已提交的释放：被拒绝/失败的一次尝试不算一次释放。
                if (ThrowOnRelease) throw new InvalidOperationException("test release failure");
                Releases.Add(ticket);
            }

            public void OnLifecycleTick(float deltaTime) { }
            public void OnReplicationTick(float deltaTime) { }

            public void OnObserverReplicationEntered(ulong observerId, ulong connectionToken, RegionKey regionKey)
            {
                if (ThrowOnReplicationEnter)
                {
                    // 一次性瞬态失败：补偿阶段的重新进入必须能成功，否则测的是补偿失败而不是回滚。
                    ThrowOnReplicationEnter = false;
                    throw new InvalidOperationException("test replication enter failure");
                }
                ReplicationEntered.Add(regionKey);
            }

            public void OnObserverReplicationExited(ulong observerId, ulong connectionToken, RegionKey regionKey)
            {
                ReplicationExited.Add(regionKey);
            }

            public void OnObserverDisconnected(ulong observerId, ulong connectionToken) { }

            public RegionGeneration ReadRegionGeneration(RegionKey regionKey) =>
                RegionGeneration.FromNative(Generation);

            public EDomainFailureKind ClassifyRegionFailure(Exception exception) =>
                EDomainFailureKind.Failed;

            public EDomainFailureKind ClassifyReleaseFailure(Exception exception) =>
                EDomainFailureKind.Failed;

            public object CaptureRegionState(RegionKey regionKey) => new object();
            public void RestoreRegionState(RegionKey regionKey, object state) { }
            public object CaptureObserverReplicationState(ulong observerId, ulong connectionToken) => new object();
            public void RestoreObserverReplicationState(
                ulong observerId, ulong connectionToken, object state)
            {
                RestoreReplicationCalls++;
                if (ThrowOnReplicationRestore)
                    throw new InvalidOperationException("test replication restore failure");
            }
            public object CaptureObserverDisconnectState(ulong observerId, ulong connectionToken) => new object();
            public void RestoreObserverDisconnectState(
                ulong observerId, ulong connectionToken, object state) { }
        }

        /// <summary>
        /// 引擎消费共享投影引擎算出的 typed Domain Demand，而不是自己感知空间：两个领域注册在
        /// 同一份唯一空间事实与同一引擎上，各自拿到自己领域的需求；引擎不持有空间索引。
        /// </summary>
        internal static bool Test_LOE01_ConsumesTypedDemandFromSharedProjection()
        {
            FakeDomainPort primary;
            FakeDomainPort secondary;
            DemandProjectionEngine projection;
            LifecycleOrchestrationEngine engine = CreateEngine(
                out primary, out secondary, out projection, registerSecondary: true);
            engine.BeginSession(new SessionEpoch(11UL));

            engine.Observe(PrimaryDomain, 100UL, 1001UL, 10, 10, true);
            engine.Observe(SecondaryDomain, 100UL, 1001UL, 10, 10, true);

            bool factsAreShared = projection.Authority.Count == 1
                && projection.TryGetDemand(engine.GetDemandPolicy(PrimaryDomain),
                    new RegionKey(10, 10), out DomainDemand primaryDemand)
                && primaryDemand.Domain == PrimaryDomain
                && projection.TryGetDemand(engine.GetDemandPolicy(SecondaryDomain),
                    new RegionKey(10, 10), out DomainDemand secondaryDemand)
                && secondaryDemand.Domain == SecondaryDomain;
            bool demandsArePerDomain = engine.GetDemand(PrimaryDomain, new RegionKey(10, 10)) == 1
                && engine.GetDemand(SecondaryDomain, new RegionKey(10, 10)) == 1
                && engine.IsLeased(PrimaryDomain, new RegionKey(10, 10))
                && engine.IsLeased(SecondaryDomain, new RegionKey(10, 10))
                && primary.Acquires.Count == 1
                && secondary.Acquires.Count == 1;

            return factsAreShared && demandsArePerDomain;
        }

        /// <summary>
        /// 需求聚合在引擎内完成：多个观察者贡献同一区域只产生一次 0 -&gt; 1 Acquire，
        /// 计数为 2 时一次离开不释放、也不撤销仍需要的租约。
        /// </summary>
        internal static bool Test_LOE02_AggregatesDemandAndAcquiresOnce()
        {
            FakeDomainPort port;
            LifecycleOrchestrationEngine engine = CreateSingleEngine(out port);
            engine.BeginSession(new SessionEpoch(11UL));
            RegionKey region = new RegionKey(10, 10);

            engine.Observe(PrimaryDomain, 100UL, 1001UL, region.X, region.Y, true);
            engine.Observe(PrimaryDomain, 200UL, 2001UL, region.X, region.Y, true);

            bool aggregated = engine.GetDemand(PrimaryDomain, region) == 2
                && engine.ActiveLeaseCount(PrimaryDomain) == 1
                && port.Acquires.Count == 1
                && port.ReplicationEntered.Count == 2;

            engine.RemoveObserver(PrimaryDomain, 100UL);

            return aggregated
                && engine.GetDemand(PrimaryDomain, region) == 1
                && engine.IsLeased(PrimaryDomain, region)
                && engine.PendingReleaseCount(PrimaryDomain) == 0
                && port.Releases.Count == 0;
        }

        /// <summary>
        /// N -&gt; 0 走滞回窗口：最后一个贡献者离开只调度释放，窗口内租约保留，
        /// 到点才提交释放。窗口长度取自领域声明的 Lifecycle Policy，不是引擎硬编码。
        /// </summary>
        internal static bool Test_LOE03_LastExitSchedulesHysteresisRelease()
        {
            FakeDomainPort port;
            LifecycleOrchestrationEngine engine = CreateSingleEngine(out port);
            engine.BeginSession(new SessionEpoch(11UL));
            RegionKey region = new RegionKey(10, 10);
            float hysteresis = engine.GetLifecyclePolicy(PrimaryDomain).HysteresisSeconds;

            engine.Observe(PrimaryDomain, 100UL, 1001UL, region.X, region.Y, true);
            engine.RemoveObserver(PrimaryDomain, 100UL);
            bool scheduled = engine.GetDemand(PrimaryDomain, region) == 0
                && engine.PendingReleaseCount(PrimaryDomain) == 1
                && port.Releases.Count == 0;

            engine.AdvanceTime(hysteresis - 0.01f);
            engine.Flush(0f);
            bool heldInsideWindow = engine.IsLeased(PrimaryDomain, region)
                && engine.PendingReleaseCount(PrimaryDomain) == 1
                && port.Releases.Count == 0;

            engine.AdvanceTime(0.02f);
            engine.Flush(0f);

            return scheduled
                && heldInsideWindow
                && !engine.IsLeased(PrimaryDomain, region)
                && engine.PendingReleaseCount(PrimaryDomain) == 0
                && port.Releases.Count == 1
                && port.Releases[0].RegionKey == region;
        }

        /// <summary>
        /// 滞回窗口内需求回升即撤销释放调度，且不产生第二次 Acquire——滞回的目的是不打断
        /// 短暂离开又回来的观察者，而不是重新获取一次权威。
        /// </summary>
        internal static bool Test_LOE04_ReentryInsideHysteresisCancelsRelease()
        {
            FakeDomainPort port;
            LifecycleOrchestrationEngine engine = CreateSingleEngine(out port);
            engine.BeginSession(new SessionEpoch(11UL));
            RegionKey region = new RegionKey(10, 10);

            engine.Observe(PrimaryDomain, 100UL, 1001UL, region.X, region.Y, true);
            engine.RemoveObserver(PrimaryDomain, 100UL);
            engine.AdvanceTime(1.0f);
            engine.Observe(PrimaryDomain, 100UL, 1002UL, region.X, region.Y, true);

            bool cancelled = engine.PendingReleaseCount(PrimaryDomain) == 0
                && engine.ReentryCount(PrimaryDomain) == 1
                && engine.GetDemand(PrimaryDomain, region) == 1;

            engine.AdvanceTime(2.0f);
            engine.Flush(0f);

            return cancelled
                && engine.IsLeased(PrimaryDomain, region)
                && port.Acquires.Count == 1
                && port.Releases.Count == 0
                && port.ReplicationEntered.Count == 2;
        }

        /// <summary>
        /// 身份校验（区域代次）：待释放区域的原生代次回退表示该区域曾重建，释放必须被拒绝并
        /// 保留状态，直到代次与登记一致——陈旧代次没有提交资格。代次前进则重排滞回窗口。
        /// </summary>
        internal static bool Test_LOE05_RegionGenerationIdentityGatesRelease()
        {
            FakeDomainPort port;
            LifecycleOrchestrationEngine engine = CreateSingleEngine(out port);
            engine.BeginSession(new SessionEpoch(11UL));
            RegionKey region = new RegionKey(10, 10);
            port.Generation = 4U;

            engine.Observe(PrimaryDomain, 100UL, 1001UL, region.X, region.Y, true);
            engine.RemoveObserver(PrimaryDomain, 100UL);
            port.Generation = 3U;
            engine.AdvanceTime(2.0f);
            engine.Flush(0f);

            bool regressedReleaseRejected = port.Releases.Count == 0
                && engine.PendingReleaseCount(PrimaryDomain) == 1
                && engine.IsLeased(PrimaryDomain, region)
                && engine.TryGetLease(PrimaryDomain, region, out RegionGeneration stored, out _)
                && stored.Value == 4U;

            port.Generation = 5U;
            engine.AdvanceTime(2.0f);
            engine.Flush(0f);
            bool regressedReleaseRetriedAfterGenerationMoved = port.Releases.Count == 0
                && engine.PendingReleaseCount(PrimaryDomain) == 1;

            engine.AdvanceTime(2.0f);
            engine.Flush(0f);

            return regressedReleaseRejected
                && regressedReleaseRetriedAfterGenerationMoved
                && port.Releases.Count == 1
                && port.Releases[0].RegionGeneration.Value == 5U;
        }

        /// <summary>
        /// 身份校验（会话代次）：会话边界清空全部待释放登记，旧会话的 pending 不得跨会话提交；
        /// 新会话从空状态重建，旧观察者事实与投影贡献都必须消失。
        /// </summary>
        internal static bool Test_LOE06_SessionEpochGatesPendingReleases()
        {
            FakeDomainPort port;
            DemandProjectionEngine projection;
            FakeDomainPort secondary;
            LifecycleOrchestrationEngine engine = CreateEngine(out port, out secondary, out projection);
            RegionKey region = new RegionKey(10, 10);

            engine.BeginSession(new SessionEpoch(11UL));
            engine.Observe(PrimaryDomain, 100UL, 1001UL, region.X, region.Y, true);
            engine.RemoveObserver(PrimaryDomain, 100UL);
            bool pendingInOldSession = engine.PendingReleaseCount(PrimaryDomain) == 1;

            engine.EndSession();
            bool clearedOnEnd = engine.PendingReleaseCount(PrimaryDomain) == 0
                && engine.ActiveLeaseCount(PrimaryDomain) == 0
                && engine.GetDemand(PrimaryDomain, region) == 0
                && projection.Authority.Count == 0
                && !engine.IsSessionActive;

            // 会话收尾后引擎拒绝一切写入（失败闭合）：旧会话的 pending 已随会话边界清空，
            // 不存在可跨会话提交的释放，也不存在「无会话仍可推进」的窗口。
            bool refusedAfterEnd = Throws(() => engine.AdvanceTime(10f))
                && Throws(() => engine.Flush(0f));
            bool noCrossSessionRelease = port.Releases.Count == 0;
            engine.EndSession(); // 重复/失序的会话结束通知必须幂等
            bool idempotentEnd = !engine.IsSessionActive && port.Releases.Count == 0;
            int acquiresBeforeReopen = port.Acquires.Count;

            engine.BeginSession(new SessionEpoch(12UL));
            engine.Observe(PrimaryDomain, 100UL, 2002UL, region.X, region.Y, true);

            return pendingInOldSession
                && clearedOnEnd
                && refusedAfterEnd
                && noCrossSessionRelease
                && idempotentEnd
                && engine.SessionEpoch.Value == 12UL
                && port.Acquires.Count == acquiresBeforeReopen + 1
                && engine.IsLeased(PrimaryDomain, region)
                && engine.TryGetConnectionGeneration(PrimaryDomain, 100UL, out ulong token)
                && token == 2002UL;
        }

        /// <summary>
        /// retry 归引擎：失败区域按领域声明的节奏重试（起步间隔内不重试，到点才重试），
        /// 成功即清除登记、补齐需求与租约。节奏取自 Lifecycle Policy，不是引擎常量。
        /// </summary>
        internal static bool Test_LOE07_RetryFollowsDomainPolicyAndClearsOnSuccess()
        {
            FakeDomainPort port;
            LifecycleOrchestrationEngine engine = CreateSingleEngine(out port);
            engine.BeginSession(new SessionEpoch(11UL));
            RegionKey region = new RegionKey(10, 10);
            float baseInterval = engine.GetLifecyclePolicy(PrimaryDomain)
                .FailedRetry.BaseIntervalSeconds;
            port.ThrowOnAcquire = true;

            engine.Observe(PrimaryDomain, 100UL, 1001UL, region.X, region.Y, true);
            bool registered = engine.PendingAcquireRetryCount(PrimaryDomain) == 1
                && !engine.IsLeased(PrimaryDomain, region)
                && engine.GetDemand(PrimaryDomain, region) == 0
                && port.Acquires.Count == 1;

            engine.AdvanceTime(baseInterval * 0.5f);
            engine.Observe(PrimaryDomain, 100UL, 1001UL, region.X, region.Y, true);
            bool notYetDue = port.Acquires.Count == 1;

            port.ThrowOnAcquire = false;
            engine.AdvanceTime(baseInterval * 0.6f);
            engine.Observe(PrimaryDomain, 100UL, 1001UL, region.X, region.Y, true);

            return registered
                && notYetDue
                && engine.PendingAcquireRetryCount(PrimaryDomain) == 0
                && engine.IsLeased(PrimaryDomain, region)
                && engine.GetDemand(PrimaryDomain, region) == 1
                && port.Acquires.Count == 2;
        }

        /// <summary>
        /// 局部故障隔离：单区域 acquire 失败只隔离「Domain Id + Region Key + 转换」这一个单元——
        /// 其它区域照常建立租约，失败区域只留下重试登记，不整批回滚、不向调用方抛出。
        /// </summary>
        internal static bool Test_LOE08_SingleRegionFailureIsIsolated()
        {
            FakeDomainPort port;
            LifecycleOrchestrationEngine engine = CreateSingleEngine(out port, radius: 1);
            engine.BeginSession(new SessionEpoch(11UL));
            RegionKey center = new RegionKey(10, 10);
            RegionKey failing = new RegionKey(11, 10);
            port.FailAcquireFor = failing;

            engine.Observe(PrimaryDomain, 100UL, 1001UL, center.X, center.Y, true);

            bool isolated = engine.IsLeased(PrimaryDomain, center)
                && !engine.IsLeased(PrimaryDomain, failing)
                && engine.ActiveLeaseCount(PrimaryDomain) == 8
                && engine.GetDemand(PrimaryDomain, center) == 1
                && engine.GetDemand(PrimaryDomain, failing) == 0
                && engine.PendingAcquireRetryCount(PrimaryDomain) == 1
                && engine.ObserverCount(PrimaryDomain) == 1;

            port.FailAcquireFor = default(RegionKey);
            float retryAfter = engine.GetLifecyclePolicy(PrimaryDomain)
                .FailedRetry.BaseIntervalSeconds;
            engine.AdvanceTime(retryAfter);
            engine.Observe(PrimaryDomain, 100UL, 1001UL, center.X, center.Y, true);

            return isolated
                && engine.IsLeased(PrimaryDomain, failing)
                && engine.ActiveLeaseCount(PrimaryDomain) == 9
                && engine.PendingAcquireRetryCount(PrimaryDomain) == 0;
        }

        /// <summary>
        /// 跨域故障隔离：一个领域的原生释放失败只熔断该领域——另一个领域的需求、租约与
        /// 待释放登记原样保留。共享引擎的故障单元含 Domain Id，因此不存在跨域熔断。
        /// </summary>
        internal static bool Test_LOE09_DomainFailureDoesNotCrossDomains()
        {
            FakeDomainPort primary;
            FakeDomainPort secondary;
            DemandProjectionEngine projection;
            LifecycleOrchestrationEngine engine = CreateEngine(
                out primary, out secondary, out projection, registerSecondary: true);
            engine.BeginSession(new SessionEpoch(11UL));
            RegionKey region = new RegionKey(10, 10);

            engine.Observe(PrimaryDomain, 100UL, 1001UL, region.X, region.Y, true);
            engine.Observe(SecondaryDomain, 100UL, 1001UL, region.X, region.Y, true);
            engine.RemoveObserver(PrimaryDomain, 100UL);
            engine.RemoveObserver(SecondaryDomain, 100UL);
            primary.ThrowOnRelease = true;

            engine.AdvanceTime(2.0f);
            engine.Flush(0f);
            bool primaryRetained = primary.Releases.Count == 0
                && engine.PendingReleaseCount(PrimaryDomain) == 1
                && engine.IsLeased(PrimaryDomain, region);
            bool secondaryUntouched = secondary.Releases.Count == 1
                && engine.PendingReleaseCount(SecondaryDomain) == 0
                && !engine.IsLeased(SecondaryDomain, region)
                && engine.GetDemand(SecondaryDomain, region) == 0;
            bool sessionStillActive = engine.IsSessionActive
                && !engine.IsRepairRequired(PrimaryDomain)
                && !engine.IsRepairRequired(SecondaryDomain);

            primary.ThrowOnRelease = false;
            engine.AdvanceTime(2.0f);
            engine.Flush(0f);

            return Expect(primaryRetained, secondaryUntouched, sessionStillActive,
                primary.Releases.Count == 1, engine.PendingReleaseCount(PrimaryDomain) == 0,
                "pRel=" + primary.Releases.Count + " sRel=" + secondary.Releases.Count
                + " pPend=" + engine.PendingReleaseCount(PrimaryDomain)
                + " sPend=" + engine.PendingReleaseCount(SecondaryDomain)
                + " pLease=" + engine.IsLeased(PrimaryDomain, region)
                + " sLease=" + engine.IsLeased(SecondaryDomain, region)
                + " sDemand=" + engine.GetDemand(SecondaryDomain, region)
                + " repairP=" + engine.IsRepairRequired(PrimaryDomain)
                + " repairS=" + engine.IsRepairRequired(SecondaryDomain)
                + " active=" + engine.IsSessionActive);
        }

        /// <summary>
        /// 补偿调度：复制阶段失败（事务语义）回滚整个观察者更新——需求聚合、租约、投影贡献
        /// 与已执行的原生副作用全部还原，异常原样抛回调用方。
        /// </summary>
        internal static bool Test_LOE10_TransactionRollbackRestoresStateAndProjection()
        {
            FakeDomainPort port;
            DemandProjectionEngine projection;
            FakeDomainPort secondary;
            LifecycleOrchestrationEngine engine = CreateEngine(out port, out secondary, out projection);
            engine.BeginSession(new SessionEpoch(11UL));
            RegionKey region = new RegionKey(10, 10);

            engine.Observe(PrimaryDomain, 100UL, 1001UL, region.X, region.Y, true);
            bool committed = engine.GetDemand(PrimaryDomain, region) == 1
                && engine.IsLeased(PrimaryDomain, region);

            port.ThrowOnReplicationEnter = true;
            bool threw = false;
            try
            {
                engine.Observe(PrimaryDomain, 100UL, 1001UL, 11, 11, true);
            }
            catch (Exception)
            {
                threw = true;
            }

            bool rolledBack = threw
                && engine.GetDemand(PrimaryDomain, region) == 1
                && engine.GetDemand(PrimaryDomain, new RegionKey(11, 11)) == 0
                && engine.ActiveLeaseCount(PrimaryDomain) == 1
                && engine.IsLeased(PrimaryDomain, region)
                && !engine.IsLeased(PrimaryDomain, new RegionKey(11, 11))
                && !engine.IsRepairRequired(PrimaryDomain);
            bool projectionRestored = projection.TryGetDemand(
                    engine.GetDemandPolicy(PrimaryDomain), region, out DomainDemand demand)
                && demand.ObserverCount == 1
                && engine.ProjectedDemandRegionCount(PrimaryDomain) == 1;
            bool compensationRan = port.RestoreReplicationCalls >= 1;

            return Expect(committed, rolledBack, projectionRestored, compensationRan,
                "demand=" + engine.GetDemand(PrimaryDomain, region)
                + " demandNew=" + engine.GetDemand(PrimaryDomain, new RegionKey(11, 11))
                + " leases=" + engine.ActiveLeaseCount(PrimaryDomain)
                + " leaseNew=" + engine.IsLeased(PrimaryDomain, new RegionKey(11, 11))
                + " repair=" + engine.IsRepairRequired(PrimaryDomain)
                + " restoreCalls=" + port.RestoreReplicationCalls
                + " projected=" + engine.ProjectedDemandRegionCount(PrimaryDomain)
                + " threw=" + threw);
        }

        /// <summary>
        /// 释放幂等：提交成功后登记与租约同时消失，再次推进时间与刷新不会产生第二次释放——
        /// 引擎不按区域无条件重放释放命令。
        /// </summary>
        internal static bool Test_LOE11_CommitReleaseIsIdempotent()
        {
            FakeDomainPort port;
            LifecycleOrchestrationEngine engine = CreateSingleEngine(out port);
            engine.BeginSession(new SessionEpoch(11UL));
            RegionKey region = new RegionKey(10, 10);

            engine.Observe(PrimaryDomain, 100UL, 1001UL, region.X, region.Y, true);
            engine.RemoveObserver(PrimaryDomain, 100UL);
            engine.AdvanceTime(2.0f);
            engine.Flush(0f);
            bool releasedOnce = port.Releases.Count == 1
                && engine.PendingReleaseCount(PrimaryDomain) == 0
                && !engine.IsLeased(PrimaryDomain, region);

            engine.AdvanceTime(10.0f);
            engine.Flush(0f);
            engine.Flush(0f);

            return releasedOnce
                && port.Releases.Count == 1
                && engine.ActiveLeaseCount(PrimaryDomain) == 0
                && engine.ObserverCount(PrimaryDomain) == 0;
        }

        /// <summary>
        /// 统一诊断：转换记录至少能关联领域、区域、转换、会话代次、区域代次、尝试次数、结果
        /// 与原因；观察者贡献类转换再带观察者与连接代次。诊断只观察，不改变任何状态。
        /// </summary>
        internal static bool Test_LOE12_DiagnosticsCarryCorrelationFields()
        {
            var sink = new RecordingDiagnostics();
            var authority = new ObserverSpatialAuthority();
            var projection = new DemandProjectionEngine(authority);
            var engine = new LifecycleOrchestrationEngine(projection, sink);
            DemandPolicy policy = new DemandPolicy(PrimaryDomain, 0, 64,
                EDemandRegionShape.ChebyshevSquare2D, "test-primary", presence => presence.GameplayAuthorized);
            projection.Register(policy);
            var port = new FakeDomainPort(PrimaryDomain, 2.0f);
            engine.Register(policy, port);

            engine.BeginSession(new SessionEpoch(11UL));
            RegionKey region = new RegionKey(10, 10);
            port.Generation = 7U;
            engine.Observe(PrimaryDomain, 100UL, 1001UL, region.X, region.Y, true);
            engine.RemoveObserver(PrimaryDomain, 100UL);
            engine.AdvanceTime(2.0f);
            engine.Flush(0f);

            LifecycleDiagnostic release = sink.Find(LifecycleEvents.RegionRelease, ELifecycleOutcome.Success);
            LifecycleDiagnostic scheduled = sink.Find(
                LifecycleEvents.RegionReleaseScheduled, ELifecycleOutcome.Scheduled);
            bool releaseCorrelates = release != null
                && release.Domain == PrimaryDomain
                && release.HasRegion && release.Region == region
                && release.SessionEpoch == 11UL
                && release.RegionGeneration == 7U
                && release.Path == ELifecyclePath.Spi;
            bool schedulingCarriesTransitionAndOutcome = scheduled != null
                && scheduled.HasRegion && scheduled.Region == region
                && scheduled.Detail != null && scheduled.Detail.Contains("demand=0");
            // 观察者贡献类转换必须再带观察者与连接代次：用一次 acquire 失败触发该类记录。
            RegionKey failing = new RegionKey(20, 20);
            port.ThrowOnAcquire = true;
            engine.Observe(PrimaryDomain, 300UL, 3003UL, failing.X, failing.Y, true);
            LifecycleDiagnostic acquireFailure = sink.Find(
                LifecycleEvents.RegionAcquire, ELifecycleOutcome.Skipped);
            bool observerTransitionsCarryObserver = acquireFailure != null
                && acquireFailure.ObserverId == 300UL
                && acquireFailure.ConnectionGeneration == 3003UL
                && acquireFailure.HasRegion && acquireFailure.Region == failing
                && acquireFailure.Attempt == 1
                && acquireFailure.Detail != null
                && acquireFailure.Detail.Contains("reason=acquire-failed");
            bool stateUntouchedByDiagnostics = port.Releases.Count == 1
                && !engine.IsLeased(PrimaryDomain, region);

            return Expect(releaseCorrelates, schedulingCarriesTransitionAndOutcome,
                observerTransitionsCarryObserver, stateUntouchedByDiagnostics,
                "release=" + Describe(release) + " scheduled=" + Describe(scheduled)
                + " acquire=" + Describe(acquireFailure)
                + " releases=" + port.Releases.Count
                + " leased=" + engine.IsLeased(PrimaryDomain, region));
        }
    }
}
