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

        /// <summary>
        /// 票 04：单条不可用样本只暂缓它自己的观察者，不冻结其它观察者、其它区域或其它领域。
        /// 暂缓不是零需求，也不是离开——被暂缓观察者的既有需求与租约原样保留，其它观察者仍能
        /// 在本拍提交并在新区域建立租约。
        /// </summary>
        internal static bool Test_ARI01_UnusableSampleDefersOnlyThatObserver()
        {
            FakeDomainPort primary;
            FakeDomainPort secondary;
            DemandProjectionEngine projection;
            LifecycleOrchestrationEngine engine = CreateEngine(
                out primary, out secondary, out projection, radius: 1, registerSecondary: true);
            engine.BeginSession(new SessionEpoch(31UL));

            engine.Observe(PrimaryDomain, 100UL, 1001UL, 10, 10, true);
            engine.Observe(PrimaryDomain, 200UL, 2001UL, 20, 20, true);
            engine.Observe(SecondaryDomain, 100UL, 1001UL, 10, 10, true);
            int secondaryLeases = engine.ActiveLeaseCount(SecondaryDomain);

            // 观察者 100 本拍记录读不全（引擎在最高接缝上只收到「暂缓」这一事实）。
            engine.DeferObserver(PrimaryDomain, 100UL, "invalid-observer-record");

            // 其它观察者照常提交本拍更新：200 移动到新区域后立即建立租约。
            engine.Observe(PrimaryDomain, 200UL, 2001UL, 30, 30, true);

            bool onlyThatObserverDeferred = engine.DeferredObserverCount(PrimaryDomain) == 1
                && engine.IsObserverDeferred(PrimaryDomain, 100UL)
                && !engine.IsObserverDeferred(PrimaryDomain, 200UL);
            bool otherObserverStillUpdates = engine.IsLeased(PrimaryDomain, new RegionKey(30, 30))
                && engine.GetDemand(PrimaryDomain, new RegionKey(30, 30)) == 1
                // 9 个来自被暂缓观察者（贡献保留）+ 9 个来自另一观察者旧区的滞回保留 + 9 个新租约。
                && engine.ActiveLeaseCount(PrimaryDomain) == 27;
            bool deferredContributionRetained = engine.IsLeased(PrimaryDomain, new RegionKey(10, 10))
                && engine.GetDemand(PrimaryDomain, new RegionKey(10, 10)) == 1
                && primary.Releases.Count == 0;
            bool otherDomainUntouched = engine.ActiveLeaseCount(SecondaryDomain) == secondaryLeases
                && engine.DeferredObserverCount(SecondaryDomain) == 0
                && secondary.Releases.Count == 0;

            return Expect(onlyThatObserverDeferred, otherObserverStillUpdates,
                deferredContributionRetained, otherDomainUntouched,
                "deferred=" + engine.DeferredObserverCount(PrimaryDomain)
                + " otherDeferred=" + engine.DeferredObserverCount(SecondaryDomain)
                + " primaryLeases=" + engine.ActiveLeaseCount(PrimaryDomain)
                + " secondaryLeases=" + engine.ActiveLeaseCount(SecondaryDomain)
                + " demandNew=" + engine.GetDemand(PrimaryDomain, new RegionKey(30, 30))
                + " demandOld=" + engine.GetDemand(PrimaryDomain, new RegionKey(10, 10))
                + " releases=" + primary.Releases.Count + "/" + secondary.Releases.Count);
        }

        /// <summary>
        /// 票 04：暂缓不触发破坏性释放。持续多拍的暂缓记录在时间推进与刷新之后仍不递减需求、
        /// 不登记待释放、不调用领域释放；而真正的确认离开（RemoveObserver）仍走滞回释放。
        /// </summary>
        internal static bool Test_ARI02_DeferredDemandNeverReleases()
        {
            FakeDomainPort port;
            LifecycleOrchestrationEngine engine = CreateSingleEngine(out port, radius: 1);
            engine.BeginSession(new SessionEpoch(31UL));
            RegionKey center = new RegionKey(10, 10);

            engine.Observe(PrimaryDomain, 100UL, 1001UL, center.X, center.Y, true);
            for (int frame = 0; frame < 20; frame++)
            {
                engine.DeferObserver(PrimaryDomain, 100UL, "invalid-observer-record");
                engine.AdvanceTime(1.0f);
                engine.Flush(0f);
            }

            bool nothingReleased = port.Releases.Count == 0
                && engine.PendingReleaseCount(PrimaryDomain) == 0
                && engine.ActiveLeaseCount(PrimaryDomain) == 9
                && engine.GetDemand(PrimaryDomain, center) == 1
                && engine.ObserverCount(PrimaryDomain) == 1
                && port.ReplicationExited.Count == 0;

            // 确认离开仍必须能清理其贡献：暂缓只挡住「无法证明离开」的推断，不挡住确认离开。
            engine.RemoveObserver(PrimaryDomain, 100UL);
            engine.AdvanceTime(engine.GetLifecyclePolicy(PrimaryDomain).HysteresisSeconds);
            engine.Flush(0f);

            bool confirmedExitStillReleases = engine.DeferredObserverCount(PrimaryDomain) == 0
                && port.Releases.Count == 9
                && engine.ActiveLeaseCount(PrimaryDomain) == 0;

            return Expect(nothingReleased, confirmedExitStillReleases,
                "releasesWhileDeferred=" + port.Releases.Count
                + " releasedRegions=" + port.Releases.Count
                + " pending=" + engine.PendingReleaseCount(PrimaryDomain)
                + " leases=" + engine.ActiveLeaseCount(PrimaryDomain)
                + " exited=" + port.ReplicationExited.Count
                + " deferred=" + engine.DeferredObserverCount(PrimaryDomain));
        }

        /// <summary>
        /// 票 04：持续暂缓的心跳有界且必留闭环。首条立即写出，此后按领域声明的间隔重复到上限，
        /// 再写一条显式终止记录后停止刷屏——不是「一条去重日志后永久静默」；观测恢复可用时
        /// 写闭环记录并重新起搏。
        /// </summary>
        internal static bool Test_ARI03_DeferredHeartbeatIsBoundedAndCloses()
        {
            var sink = new RecordingDiagnostics();
            var projection = new DemandProjectionEngine(new ObserverSpatialAuthority());
            var engine = new LifecycleOrchestrationEngine(projection, sink);
            DemandPolicy policy = new DemandPolicy(PrimaryDomain, 0, 64,
                EDemandRegionShape.ChebyshevSquare2D, "test-primary", presence => presence.GameplayAuthorized);
            projection.Register(policy);
            HeartbeatPolicy heartbeat = new FakeDomainPort(PrimaryDomain, 2.0f).LifecyclePolicy.Heartbeat;
            var port = new FakeDomainPort(PrimaryDomain, 2.0f);
            engine.Register(policy, port);
            engine.BeginSession(new SessionEpoch(31UL));
            engine.Observe(PrimaryDomain, 100UL, 1001UL, 10, 10, true);

            for (int second = 0; second < 40; second++)
            {
                engine.DeferObserver(PrimaryDomain, 100UL, "invalid-observer-record");
                engine.AdvanceTime(1.0f);
            }

            int entryRecords = CountEntries(sink, withMarker: false);
            int heartbeatRecords = CountEntries(sink, withMarker: true);
            LifecycleDiagnostic terminal = sink.Find(LifecycleEvents.ObserverDeferred, ELifecycleOutcome.Skipped);
            bool bounded = entryRecords == 1
                && heartbeatRecords == 1 + heartbeat.MaxRepeats
                && terminal != null
                && terminal.Detail.Contains("heartbeat=Exhausted");

            for (int second = 0; second < 40; second++)
            {
                engine.DeferObserver(PrimaryDomain, 100UL, "invalid-observer-record");
                engine.AdvanceTime(1.0f);
            }

            bool noFloodAfterTerminal = CountEntries(sink, withMarker: false) == entryRecords
                && CountEntries(sink, withMarker: true) == heartbeatRecords
                && engine.DeferredObserverCount(PrimaryDomain) == 1
                && engine.IsLeased(PrimaryDomain, new RegionKey(10, 10));

            engine.Observe(PrimaryDomain, 100UL, 1001UL, 10, 10, true);
            LifecycleDiagnostic closed = sink.Find(LifecycleEvents.ObserverDeferred, ELifecycleOutcome.Success);
            bool closesOnRecovery = closed != null
                && closed.Detail.Contains("cause=observer-sample-available")
                && engine.DeferredObserverCount(PrimaryDomain) == 0;

            engine.DeferObserver(PrimaryDomain, 200UL, "invalid-observer-record");
            bool reopensForNewDeferral = CountEntries(sink, withMarker: true) == heartbeatRecords + 1
                && CountEntries(sink, withMarker: false) == entryRecords + 1;

            return Expect(bounded, noFloodAfterTerminal, closesOnRecovery, reopensForNewDeferral,
                "entries=" + entryRecords + " heartbeats=" + heartbeatRecords
                + " terminal=" + (terminal != null) + " maxRepeats=" + heartbeat.MaxRepeats
                + " deferred=" + engine.DeferredObserverCount(PrimaryDomain)
                + " detail=" + Describe(terminal) + " trail=" + DescribeDeferredTrail(sink));
        }

        /// <summary>统计暂缓诊断里带/不带心跳标记的记录数（进入暂缓 vs 心跳）。</summary>
        private static int CountEntries(RecordingDiagnostics sink, bool withMarker)
        {
            int count = 0;
            foreach (LifecycleDiagnostic entry in sink.Entries)
            {
                if (entry.EventName != LifecycleEvents.ObserverDeferred) continue;
                if (entry.Outcome != ELifecycleOutcome.Deferred) continue;
                bool hasMarker = entry.Detail != null && entry.Detail.Contains("heartbeat=");
                if (hasMarker == withMarker) count++;
            }
            return count;
        }

        /// <summary>暂缓诊断序列的取证串：失败时能直接看出心跳的节奏与终止点。</summary>
        private static string DescribeDeferredTrail(RecordingDiagnostics sink)
        {
            var trail = new System.Text.StringBuilder();
            foreach (LifecycleDiagnostic entry in sink.Entries)
            {
                if (entry.EventName != LifecycleEvents.ObserverDeferred) continue;
                trail.Append('[').Append(entry.Outcome).Append('|');
                int marker = entry.Detail == null ? -1 : entry.Detail.IndexOf("heartbeat=");
                trail.Append(marker < 0 ? "entry" : entry.Detail.Substring(marker));
                trail.Append("|attempt=").Append(entry.Attempt).Append(']');
            }
            return trail.ToString();
        }

        /// <summary>
        /// 票 04：retry 身份与观察者贡献事务粒度一致——同一区域的两个观察者各自失败时，
        /// 两份重试资格并存、互不吞并（区域单槽退出），各自到点只补齐自己的复制贡献。
        /// </summary>
        internal static bool Test_ARI05_AcquireRetryIsTransactionScoped()
        {
            FakeDomainPort port;
            LifecycleOrchestrationEngine engine = CreateSingleEngine(out port);
            engine.BeginSession(new SessionEpoch(31UL));
            RegionKey region = new RegionKey(10, 10);
            port.FailAcquireFor = region;

            engine.Observe(PrimaryDomain, 100UL, 1001UL, region.X, region.Y, true);
            engine.Observe(PrimaryDomain, 200UL, 2001UL, region.X, region.Y, true);

            bool bothQualified = engine.PendingAcquireRetryCount(PrimaryDomain) == 2
                && engine.AcquireRetryQualificationCount(PrimaryDomain, region) == 2
                && port.Acquires.Count == 2
                && !engine.IsLeased(PrimaryDomain, region);

            port.FailAcquireFor = default(RegionKey);
            float retryAfter = engine.GetLifecyclePolicy(PrimaryDomain).FailedRetry.BaseIntervalSeconds;
            engine.AdvanceTime(retryAfter);
            engine.Observe(PrimaryDomain, 100UL, 1001UL, region.X, region.Y, true);

            bool ownRetryKeepsOther = engine.IsLeased(PrimaryDomain, region)
                && engine.GetDemand(PrimaryDomain, region) == 1
                && engine.AcquireRetryQualificationCount(PrimaryDomain, region) == 1
                && CountReplicationEntries(port, region, 100UL) == 1
                && CountReplicationEntries(port, region, 200UL) == 0;

            engine.Observe(PrimaryDomain, 200UL, 2001UL, region.X, region.Y, true);

            return Expect(bothQualified, ownRetryKeepsOther,
                engine.GetDemand(PrimaryDomain, region) == 2,
                engine.AcquireRetryQualificationCount(PrimaryDomain, region) == 0,
                CountReplicationEntries(port, region, 200UL) == 1,
                port.Acquires.Count == 3,
                "qualifications=" + engine.AcquireRetryQualificationCount(PrimaryDomain, region)
                + " demand=" + engine.GetDemand(PrimaryDomain, region)
                + " acquires=" + port.Acquires.Count
                + " entries100=" + CountReplicationEntries(port, region, 100UL)
                + " entries200=" + CountReplicationEntries(port, region, 200UL));
        }

        private static int CountReplicationEntries(FakeDomainPort port, RegionKey region, ulong observerId)
        {
            int count = 0;
            foreach (ObserverReplicationEvent entry in port.EnteredByObserver)
            {
                if (entry.RegionKey == region && entry.ObserverId == observerId) count++;
            }
            return count;
        }

        /// <summary>按观察者归属的复制贡献登记：用于证明「只补齐自己的贡献」。</summary>
        private readonly struct ObserverReplicationEvent
        {
            internal ObserverReplicationEvent(ulong observerId, RegionKey regionKey)
            {
                ObserverId = observerId;
                RegionKey = regionKey;
            }

            internal ulong ObserverId { get; }
            internal RegionKey RegionKey { get; }
        }

        /// <summary>
        /// 票 04：陈旧代次没有写入资格。观察者换连接后，旧连接代次留下的未完成事务登记不得
        /// 继续替新连接提交重试，也不得把尝试次数继承给新事务；撤销必须留诊断，回滚时原样还回。
        /// </summary>
        internal static bool Test_ARI06_StaleConnectionRetryHasNoWriteEligibility()
        {
            var sink = new RecordingDiagnostics();
            var projection = new DemandProjectionEngine(new ObserverSpatialAuthority());
            var engine = new LifecycleOrchestrationEngine(projection, sink);
            DemandPolicy policy = new DemandPolicy(PrimaryDomain, 0, 64,
                EDemandRegionShape.ChebyshevSquare2D, "test-primary", presence => presence.GameplayAuthorized);
            projection.Register(policy);
            var port = new FakeDomainPort(PrimaryDomain, 2.0f);
            engine.Register(policy, port);
            engine.BeginSession(new SessionEpoch(31UL));
            RegionKey region = new RegionKey(10, 10);
            port.FailAcquireFor = region;

            engine.Observe(PrimaryDomain, 100UL, 1001UL, region.X, region.Y, true);
            bool registeredUnderOldGeneration = engine.AcquireRetryQualificationCount(PrimaryDomain, region) == 1
                && engine.PendingAcquireRetryCount(PrimaryDomain) == 1;

            // 换连接且仍旧失败：旧代次的登记被撤销（不得替新连接提交），新事务从第 1 次起算，
            // 不继承旧代次的尝试次数。
            engine.AdvanceTime(engine.GetLifecyclePolicy(PrimaryDomain).FailedRetry.BaseIntervalSeconds);
            engine.Observe(PrimaryDomain, 100UL, 1002UL, region.X, region.Y, true);

            LifecycleDiagnostic latest = sink.Last(LifecycleEvents.RegionAcquire);
            bool freshTransactionRestarts = latest != null
                && latest.ConnectionGeneration == 1002UL
                && latest.Attempt == 1
                && latest.ObserverId == 100UL
                && latest.HasRegion && latest.Region == region;
            bool staleRegistrationWithdrawn = engine.AcquireRetryQualificationCount(PrimaryDomain, region) == 1
                && engine.PendingAcquireRetryCount(PrimaryDomain) == 1
                && !engine.IsLeased(PrimaryDomain, region)
                && engine.GetDemand(PrimaryDomain, region) == 0;

            // 到点重试由新代次驱动并成功：租约成立、登记清除，全程不产生补偿恢复。
            port.FailAcquireFor = default(RegionKey);
            engine.AdvanceTime(engine.GetLifecyclePolicy(PrimaryDomain).FailedRetry.BaseIntervalSeconds);
            engine.Observe(PrimaryDomain, 100UL, 1002UL, region.X, region.Y, true);
            bool newGenerationDrivesRetry = engine.IsLeased(PrimaryDomain, region)
                && engine.AcquireRetryQualificationCount(PrimaryDomain, region) == 0
                && port.Acquires.Count == 3
                && port.RestoreRegionStateCalls == 0;

            return Expect(registeredUnderOldGeneration, freshTransactionRestarts,
                staleRegistrationWithdrawn, newGenerationDrivesRetry,
                "latest=" + Describe(latest) + " qualification="
                + engine.AcquireRetryQualificationCount(PrimaryDomain, region)
                + " pending=" + engine.PendingAcquireRetryCount(PrimaryDomain)
                + " acquires=" + port.Acquires.Count
                + " leases=" + engine.ActiveLeaseCount(PrimaryDomain));
        }

        /// <summary>
        /// 票 04：会话身份不确定时阻止新写入与破坏性释放，但保留全部租约、需求与暂缓态，
        /// 并按领域政策写出有界心跳；身份重新确认后写闭环记录并恢复写入。
        /// </summary>
        internal static bool Test_ARI07_IdentityUncertaintyGatesWritesAndKeepsLeases()
        {
            FakeDomainPort port;
            LifecycleOrchestrationEngine engine = CreateSingleEngine(out port, radius: 1);
            engine.BeginSession(new SessionEpoch(31UL));
            RegionKey center = new RegionKey(10, 10);
            engine.Observe(PrimaryDomain, 100UL, 1001UL, center.X, center.Y, true);

            engine.SuspendWrites("host-session-identity-Recovering");
            bool suspended = engine.IsWriteSuspended;

            // 挂起期间：新写入被拒绝，破坏性释放不发生，租约与需求原样保留。
            bool writesRejected = Throws(() =>
                engine.Observe(PrimaryDomain, 200UL, 2001UL, center.X, center.Y, true));
            bool removalRejected = Throws(() => engine.RemoveObserver(PrimaryDomain, 100UL));
            for (int frame = 0; frame < 30; frame++)
            {
                engine.AdvanceTime(1.0f);
                engine.Flush(0f);
            }

            bool stateRetained = port.Releases.Count == 0
                && engine.ActiveLeaseCount(PrimaryDomain) == 9
                && engine.GetDemand(PrimaryDomain, center) == 1
                && engine.PendingReleaseCount(PrimaryDomain) == 0
                && engine.ObserverCount(PrimaryDomain) == 1;
            bool sessionSurvives = engine.IsSessionActive && !engine.IsRepairRequired(PrimaryDomain);

            engine.ResumeWrites("host-session-identity-Ready");
            engine.Observe(PrimaryDomain, 200UL, 2001UL, center.X, center.Y, true);

            return Expect(suspended, writesRejected, removalRejected, stateRetained, sessionSurvives,
                !engine.IsWriteSuspended && engine.GetDemand(PrimaryDomain, center) == 2
                && port.Releases.Count == 0,
                "suspended=" + suspended + " releases=" + port.Releases.Count
                + " leases=" + engine.ActiveLeaseCount(PrimaryDomain)
                + " demand=" + engine.GetDemand(PrimaryDomain, center)
                + " observers=" + engine.ObserverCount(PrimaryDomain)
                + " active=" + engine.IsSessionActive);
        }

        /// <summary>
        /// 票 04：会话身份不可恢复时显式熔断——熔断期间同样拒绝写入与破坏性释放并持续有界心跳；
        /// 只有新建会话才重新开放写入，旧 Session Epoch 与旧代次不得再写入。
        /// </summary>
        internal static bool Test_ARI08_IdentityCircuitBreakRequiresNewSession()
        {
            var sink = new RecordingDiagnostics();
            var projection = new DemandProjectionEngine(new ObserverSpatialAuthority());
            var engine = new LifecycleOrchestrationEngine(projection, sink);
            DemandPolicy policy = new DemandPolicy(PrimaryDomain, 0, 64,
                EDemandRegionShape.ChebyshevSquare2D, "test-primary", presence => presence.GameplayAuthorized);
            projection.Register(policy);
            var port = new FakeDomainPort(PrimaryDomain, 2.0f);
            engine.Register(policy, port);
            engine.BeginSession(new SessionEpoch(31UL));
            RegionKey region = new RegionKey(10, 10);
            engine.Observe(PrimaryDomain, 100UL, 1001UL, region.X, region.Y, true);
            engine.DeferObserver(PrimaryDomain, 100UL, "invalid-observer-record");

            // 熔断态的语义与挂起一致：拒绝写入、保留贡献、不破坏性释放。
            engine.SuspendWrites("host-session-identity-CircuitBroken");
            bool circuitBrokenRefusesWrites = Throws(() =>
                engine.Observe(PrimaryDomain, 300UL, 3001UL, region.X, region.Y, true));
            engine.AdvanceTime(30.0f);
            engine.Flush(0f);
            bool contributionKept = port.Releases.Count == 0
                && engine.IsLeased(PrimaryDomain, region)
                && engine.GetDemand(PrimaryDomain, region) == 1
                && engine.ObserverCount(PrimaryDomain) == 1;

            // 挂起/熔断持续时按领域政策留痕：挂起记录 + 有界心跳，不是一条去重日志后静默。
            int gateRecords = 0;
            foreach (LifecycleDiagnostic entry in sink.Entries)
            {
                if (entry.EventName == LifecycleEvents.SessionSuspended) gateRecords++;
            }
            bool heartbeatObservable = gateRecords >= 2;

            // 失明期间的旧 epoch 与旧代次在重建后没有写入资格：重建清空一切，
            // 旧观察者事实与投影贡献都不得残留。
            engine.EndSession();
            bool clearedForReuse = engine.DeferredObserverCount(PrimaryDomain) == 0
                && engine.ActiveLeaseCount(PrimaryDomain) == 0
                && !engine.IsWriteSuspended
                && !engine.IsSessionActive;
            engine.BeginSession(new SessionEpoch(32UL));
            engine.Observe(PrimaryDomain, 100UL, 4004UL, region.X, region.Y, true);

            return Expect(circuitBrokenRefusesWrites, contributionKept, heartbeatObservable,
                clearedForReuse,
                engine.SessionEpoch.Value == 32UL
                && engine.GetDemand(PrimaryDomain, region) == 1
                && port.Acquires.Count == 2
                && sink.Find(LifecycleEvents.SessionSuspended, ELifecycleOutcome.Success) != null,
                "gateRecords=" + gateRecords + " releases=" + port.Releases.Count
                + " leases=" + engine.ActiveLeaseCount(PrimaryDomain)
                + " deferred=" + engine.DeferredObserverCount(PrimaryDomain)
                + " acquires=" + port.Acquires.Count
                + " active=" + engine.IsSessionActive);
        }

        /// <summary>
        /// 票 04：故障隔离单元至少是 Domain Id + Region Key + Transition。同域一个区域的
        /// acquire 失败不牵动同域其它区域、也不牵动另一领域的同区需求；一个区域的释放失败
        /// 只保留该区域的待释放登记，另一领域的释放照常提交——释放失败本身不升级成领域熔断。
        /// </summary>
        internal static bool Test_ARI09_FaultUnitIsDomainRegionTransition()
        {
            FakeDomainPort primary;
            FakeDomainPort secondary;
            DemandProjectionEngine projection;
            LifecycleOrchestrationEngine engine = CreateEngine(
                out primary, out secondary, out projection, registerSecondary: true);
            engine.BeginSession(new SessionEpoch(31UL));
            RegionKey regionA = new RegionKey(10, 10);
            RegionKey regionB = new RegionKey(20, 20);

            primary.FailAcquireFor = regionA;
            engine.Observe(PrimaryDomain, 100UL, 1001UL, regionA.X, regionA.Y, true);
            engine.Observe(PrimaryDomain, 200UL, 2001UL, regionB.X, regionB.Y, true);
            engine.Observe(SecondaryDomain, 100UL, 1001UL, regionA.X, regionA.Y, true);
            primary.FailAcquireFor = default(RegionKey);

            bool acquireTransitionIsolated = !engine.IsLeased(PrimaryDomain, regionA)
                && engine.AcquireRetryQualificationCount(PrimaryDomain, regionA) == 1
                && engine.IsLeased(PrimaryDomain, regionB)
                && engine.IsLeased(SecondaryDomain, regionA)
                && engine.ActiveLeaseCount(SecondaryDomain) == 1
                && secondary.Releases.Count == 0;
            Expect(acquireTransitionIsolated,
                "leaseA=" + engine.IsLeased(PrimaryDomain, regionA)
                + " qualificationA=" + engine.AcquireRetryQualificationCount(PrimaryDomain, regionA)
                + " leaseB=" + engine.IsLeased(PrimaryDomain, regionB)
                + " secondaryLease=" + engine.IsLeased(SecondaryDomain, regionA)
                + " secondaryLeases=" + engine.ActiveLeaseCount(SecondaryDomain)
                + " secondaryReleases=" + secondary.Releases.Count);

            primary.ThrowOnRelease = true;
            engine.RemoveObserver(PrimaryDomain, 200UL);
            engine.RemoveObserver(SecondaryDomain, 100UL);
            engine.AdvanceTime(engine.GetLifecyclePolicy(PrimaryDomain).HysteresisSeconds);
            engine.Flush(0f);

            // 主域只有 B 区建立了租约（A 区 acquire 从未成功），因此只有一处待释放登记。
            bool releaseTransitionIsolated = primary.Releases.Count == 0
                && engine.PendingReleaseCount(PrimaryDomain) == 1
                && secondary.Releases.Count == 1
                && engine.PendingReleaseCount(SecondaryDomain) == 0
                && engine.ActiveLeaseCount(SecondaryDomain) == 0;
            bool failureDoesNotEscalateToDomainFault = engine.IsSessionActive
                && !engine.IsRepairRequired(PrimaryDomain)
                && !engine.IsRepairRequired(SecondaryDomain);
            Expect(releaseTransitionIsolated, failureDoesNotEscalateToDomainFault,
                "primaryReleases=" + primary.Releases.Count
                + " pendingPrimary=" + engine.PendingReleaseCount(PrimaryDomain)
                + " secondaryReleases=" + secondary.Releases.Count
                + " pendingSecondary=" + engine.PendingReleaseCount(SecondaryDomain)
                + " secondaryLeases=" + engine.ActiveLeaseCount(SecondaryDomain)
                + " repair=" + engine.IsRepairRequired(PrimaryDomain) + "/"
                + engine.IsRepairRequired(SecondaryDomain));

            primary.ThrowOnRelease = false;
            engine.AdvanceTime(engine.GetLifecyclePolicy(PrimaryDomain).HysteresisSeconds);
            engine.Flush(0f);

            return Expect(acquireTransitionIsolated, releaseTransitionIsolated,
                failureDoesNotEscalateToDomainFault,
                primary.Releases.Count == 1 && engine.PendingReleaseCount(PrimaryDomain) == 0,
                "primaryLeasesA=" + engine.IsLeased(PrimaryDomain, regionA)
                + " qualificationA=" + engine.AcquireRetryQualificationCount(PrimaryDomain, regionA)
                + " primaryLeasesB=" + engine.IsLeased(PrimaryDomain, regionB)
                + " secondaryLeases=" + engine.ActiveLeaseCount(SecondaryDomain)
                + " primaryReleases=" + primary.Releases.Count
                + " secondaryReleases=" + secondary.Releases.Count
                + " pendingPrimary=" + engine.PendingReleaseCount(PrimaryDomain)
                + " repair=" + engine.IsRepairRequired(PrimaryDomain) + "/"
                + engine.IsRepairRequired(SecondaryDomain));
        }

        /// <summary>
        /// 票 04：熔断领域的持续异常具备有界心跳（首条 + 重复到上限 + 显式终止），
        /// 会话收尾成功即恢复并写闭环记录——恢复不是静默的，新会话也不再被它挡住。
        /// </summary>
        internal static bool Test_ARI10_FaultHeartbeatIsBoundedAndClosesOnRecovery()
        {
            var sink = new RecordingDiagnostics();
            var engine = CreateEngineWithDiagnostics(sink, out FakeDomainPort primary, out FakeDomainPort secondary);
            engine.BeginSession(new SessionEpoch(31UL));
            RegionKey region = new RegionKey(10, 10);

            primary.ThrowOnReplicationEnter = true;
            primary.ThrowOnReplicationRestore = true;
            bool faulted = Throws(() =>
                engine.Observe(PrimaryDomain, 100UL, 1001UL, region.X, region.Y, true))
                && engine.IsRepairRequired(PrimaryDomain);

            engine.Observe(SecondaryDomain, 200UL, 2001UL, region.X, region.Y, true);
            for (int second = 0; second < 40; second++) engine.AdvanceTime(1.0f);
            engine.Flush(0f);

            int faultRecords = CountFaultRecords(sink);
            LifecycleDiagnostic terminal = sink.Find(LifecycleEvents.DomainFault, ELifecycleOutcome.Skipped);
            int maxRepeats = primary.LifecyclePolicy.Heartbeat.MaxRepeats;
            bool bounded = faultRecords == 1 + 1 + maxRepeats && terminal != null
                && terminal.Detail.Contains("heartbeat=Exhausted");
            bool otherDomainUnaffected = engine.IsLeased(SecondaryDomain, region)
                && engine.GetDemand(SecondaryDomain, region) == 1
                && !engine.IsRepairRequired(SecondaryDomain)
                && engine.IsSessionActive;
            Expect(faulted, bounded, otherDomainUnaffected,
                "faulted=" + faulted + " faultRecords=" + faultRecords
                + " terminal=" + Describe(terminal)
                + " secondaryLease=" + engine.IsLeased(SecondaryDomain, region));

            // 会话收尾失败＝熔断未被修复：熔断领域在下一局不参与，但不得挡住其它领域开始会话。
            primary.ThrowOnSessionEnd = true;
            bool endFailedClosed = Throws(() => engine.EndSession())
                && engine.IsRepairRequired(PrimaryDomain)
                && !engine.IsSessionActive;

            int primaryBeginsBefore = primary.SessionBeginCalls;
            int secondaryBeginsBefore = secondary.SessionBeginCalls;
            engine.BeginSession(new SessionEpoch(32UL));
            bool healthyDomainStillBegins = engine.IsSessionActive
                && engine.IsRepairRequired(PrimaryDomain)
                // 熔断领域不参与本局：它拿不到新 Session Epoch，因此旧代次的写入资格不被延续。
                && primary.SessionBeginCalls == primaryBeginsBefore
                && secondary.SessionBeginCalls == secondaryBeginsBefore + 1
                && Throws(() => engine.Observe(PrimaryDomain, 100UL, 2002UL, region.X, region.Y, true));
            engine.Observe(SecondaryDomain, 200UL, 2002UL, region.X, region.Y, true);
            bool isolatedDomainStaysIsolated = engine.IsLeased(SecondaryDomain, region);
            Expect(endFailedClosed, healthyDomainStillBegins, isolatedDomainStaysIsolated,
                "repair=" + engine.IsRepairRequired(PrimaryDomain)
                + " secondaryLease=" + engine.IsLeased(SecondaryDomain, region)
                + " active=" + engine.IsSessionActive);

            // 会话收尾成功＝有限恢复：熔断清除并写闭环记录，下一局该领域重新参与。
            primary.ThrowOnSessionEnd = false;
            engine.EndSession();
            bool recovered = !engine.IsRepairRequired(PrimaryDomain)
                && sink.Find(LifecycleEvents.DomainFault, ELifecycleOutcome.Success) != null;
            engine.BeginSession(new SessionEpoch(33UL));
            primary.ThrowOnReplicationEnter = false;
            primary.ThrowOnReplicationRestore = false;
            engine.Observe(PrimaryDomain, 100UL, 3003UL, region.X, region.Y, true);
            bool recoversIntoService = engine.IsLeased(PrimaryDomain, region)
                && engine.GetDemand(PrimaryDomain, region) == 1
                && !engine.IsRepairRequired(PrimaryDomain);

            return Expect(recovered, recoversIntoService,
                "recovered=" + recovered + " repair=" + engine.IsRepairRequired(PrimaryDomain)
                + " primaryLease=" + engine.IsLeased(PrimaryDomain, region)
                + " demand=" + engine.GetDemand(PrimaryDomain, region));
        }

        private static int CountFaultRecords(RecordingDiagnostics sink)
        {
            int count = 0;
            foreach (LifecycleDiagnostic entry in sink.Entries)
            {
                if (entry.EventName == LifecycleEvents.DomainFault
                    && entry.Outcome != ELifecycleOutcome.Success) count++;
            }
            return count;
        }

        /// <summary>两份领域状态各自独立的引擎，用于跨域隔离类断言（两个领域都注册）。</summary>
        private static LifecycleOrchestrationEngine CreateEngineWithDiagnostics(
            RecordingDiagnostics sink, out FakeDomainPort primary, out FakeDomainPort secondary)
        {
            var projection = new DemandProjectionEngine(new ObserverSpatialAuthority());
            var engine = new LifecycleOrchestrationEngine(projection, sink);
            DemandPolicy primaryPolicy = new DemandPolicy(PrimaryDomain, 0, 64,
                EDemandRegionShape.ChebyshevSquare2D, "test-primary", presence => presence.GameplayAuthorized);
            DemandPolicy secondaryPolicy = new DemandPolicy(SecondaryDomain, 0, 64,
                EDemandRegionShape.ChebyshevSquare2D, "test-secondary", presence => presence.GameplayAuthorized);
            projection.Register(primaryPolicy);
            projection.Register(secondaryPolicy);
            primary = new FakeDomainPort(PrimaryDomain, 2.0f);
            secondary = new FakeDomainPort(SecondaryDomain, 2.0f);
            engine.Register(primaryPolicy, primary);
            engine.Register(secondaryPolicy, secondary);
            return engine;
        }

        /// <summary>
        /// 票 04：被暂缓观察者的贡献不是零需求。同区另一观察者离开后，需求引用计数只降到
        /// 暂缓者仍占的那一份，租约不会因为「暂缓被当成零需求」而提前进入滞回释放。
        /// </summary>
        internal static bool Test_ARI04_DeferredContributionStillCountsAsDemand()
        {
            FakeDomainPort port;
            LifecycleOrchestrationEngine engine = CreateSingleEngine(out port);
            engine.BeginSession(new SessionEpoch(31UL));
            RegionKey region = new RegionKey(10, 10);

            engine.Observe(PrimaryDomain, 100UL, 1001UL, region.X, region.Y, true);
            engine.Observe(PrimaryDomain, 200UL, 2001UL, region.X, region.Y, true);
            engine.DeferObserver(PrimaryDomain, 100UL, "invalid-observer-record");
            engine.RemoveObserver(PrimaryDomain, 200UL);

            bool demandSurvivesThroughDeferred = engine.GetDemand(PrimaryDomain, region) == 1
                && engine.PendingReleaseCount(PrimaryDomain) == 0
                && engine.IsLeased(PrimaryDomain, region)
                && engine.ProjectedDemandRegionCount(PrimaryDomain) == 1;

            engine.AdvanceTime(engine.GetLifecyclePolicy(PrimaryDomain).HysteresisSeconds * 2f);
            engine.Flush(0f);

            return Expect(demandSurvivesThroughDeferred,
                port.Releases.Count == 0 && engine.ActiveLeaseCount(PrimaryDomain) == 1,
                "demand=" + engine.GetDemand(PrimaryDomain, region)
                + " pending=" + engine.PendingReleaseCount(PrimaryDomain)
                + " releases=" + port.Releases.Count
                + " leases=" + engine.ActiveLeaseCount(PrimaryDomain)
                + " projected=" + engine.ProjectedDemandRegionCount(PrimaryDomain));
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

            /// <summary>该事件名最近一条记录：用于核对「新事务从第 1 次起算」这类时序断言。</summary>
            internal LifecycleDiagnostic Last(string eventName)
            {
                LifecycleDiagnostic last = null;
                foreach (LifecycleDiagnostic entry in Entries)
                {
                    if (entry.EventName == eventName) last = entry;
                }
                return last;
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
            internal readonly List<ObserverReplicationEvent> EnteredByObserver =
                new List<ObserverReplicationEvent>();
            internal readonly List<RegionKey> ReplicationExited = new List<RegionKey>();
            internal uint Generation = 1U;
            internal bool ThrowOnAcquire;
            internal bool ThrowOnRelease;
            internal bool ThrowOnSessionEnd;
            internal bool ThrowOnReplicationEnter;
            internal bool ThrowOnReplicationRestore;
            internal RegionKey FailAcquireFor;
            internal int RestoreReplicationCalls;
            internal int RestoreRegionStateCalls;

            /// <summary>本域收到过几次会话开始：熔断领域不得参与新会话，靠它取证。</summary>
            internal int SessionBeginCalls;

            internal FakeDomainPort(DomainId domainId, float hysteresisSeconds)
            {
                DomainId = domainId;
                LifecyclePolicy = new LifecyclePolicy(
                    hysteresisSeconds,
                    new RetrySchedule(2.0f, 32f, 5),
                    new RetrySchedule(10.0f, 60f, int.MaxValue),
                    new HeartbeatPolicy(5f, 2),
                    "test-domain");
            }

            public DomainId DomainId { get; }
            public string DisplayName => DomainId.Value;
            public LifecyclePolicy LifecyclePolicy { get; }

            public void OnSessionBegin(uint sessionEpoch) => SessionBeginCalls++;
            public void OnSessionEnd()
            {
                if (ThrowOnSessionEnd) throw new InvalidOperationException("test session-end failure");
            }
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
                EnteredByObserver.Add(new ObserverReplicationEvent(observerId, regionKey));
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
            public void RestoreRegionState(RegionKey regionKey, object state) => RestoreRegionStateCalls++;
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

        /// <summary>
        /// 票 09-R1：滞回内重入行必须携带权威身份。event=LeaseReentry 的记录来自实际执行
        /// 端口，domain 与 authority 都必须是端口自己的领域；同区双领域各自保留身份，不得
        /// 互相覆盖。旧实现 detail 只有 hysteresisCancelled=true，单行无法识别 Collision。
        /// </summary>
        internal static bool Test_LOE16_ReentryCarriesAuthorityAndDomain()
        {
            var sink = new RecordingDiagnostics();
            LifecycleOrchestrationEngine engine =
                CreateEngineWithDiagnostics(sink, out FakeDomainPort resource, out FakeDomainPort collision);
            engine.BeginSession(new SessionEpoch(11UL));
            RegionKey region = new RegionKey(10, 10);

            engine.Observe(DomainIds.Resource, 100UL, 1001UL, region.X, region.Y, true);
            engine.Observe(DomainIds.Collision, 200UL, 2002UL, region.X, region.Y, true);
            engine.RemoveObserver(DomainIds.Resource, 100UL);
            engine.RemoveObserver(DomainIds.Collision, 200UL);
            engine.AdvanceTime(1.0f);
            engine.Observe(DomainIds.Resource, 100UL, 1001UL, region.X, region.Y, true);
            engine.Observe(DomainIds.Collision, 200UL, 2002UL, region.X, region.Y, true);

            LifecycleDiagnostic resourceReentry = sink.Entries.Find(entry =>
                entry.EventName == LifecycleEvents.RegionReentry && entry.Domain == DomainIds.Resource);
            LifecycleDiagnostic collisionReentry = sink.Entries.Find(entry =>
                entry.EventName == LifecycleEvents.RegionReentry && entry.Domain == DomainIds.Collision);
            bool collisionIdentityCorrelates = collisionReentry != null
                && collisionReentry.Outcome == ELifecycleOutcome.Success
                && collisionReentry.HasRegion && collisionReentry.Region == region
                && collisionReentry.SessionEpoch == 11UL
                && collisionReentry.ObserverId == 200UL
                && collisionReentry.ConnectionGeneration == 2002UL
                && collisionReentry.RegionGeneration == 1U
                && collisionReentry.Detail != null
                && collisionReentry.Detail.Contains("authority=Collision")
                && collisionReentry.Detail.Contains("hysteresisCancelled=true");
            bool resourceIdentityRetained = resourceReentry != null
                && resourceReentry.Outcome == ELifecycleOutcome.Success
                && resourceReentry.Detail != null
                && resourceReentry.Detail.Contains("authority=Resource")
                && !resourceReentry.Detail.Contains("authority=Collision");
            bool reentryCancelledPendingWithoutSecondAcquire =
                engine.PendingReleaseCount(DomainIds.Resource) == 0
                && engine.PendingReleaseCount(DomainIds.Collision) == 0
                && engine.IsLeased(DomainIds.Resource, region)
                && engine.IsLeased(DomainIds.Collision, region)
                && resource.Acquires.Count == 1 && collision.Acquires.Count == 1
                && resource.Releases.Count == 0 && collision.Releases.Count == 0;
            // 最高接缝记录 → 真实生产格式化出口：[LifecycleObs] 行单行可判读 Reentry 契约。
            string collisionReentryLine = collisionReentry == null
                ? string.Empty
                : DefaultLifecycleDiagnostics.FormatLine(collisionReentry);
            bool lineCarriesContract = collisionReentryLine.Contains("event=LeaseReentry")
                && collisionReentryLine.Contains("domain=Collision")
                && collisionReentryLine.Contains("authority=Collision")
                && collisionReentryLine.Contains("outcome=Success")
                && collisionReentryLine.Contains("hysteresisCancelled=true")
                && collisionReentryLine.Contains("region=(10,10)")
                && collisionReentryLine.Contains("sessionEpoch=11")
                && collisionReentryLine.Contains("regionGeneration=1")
                && collisionReentryLine.Contains("observer=200")
                && collisionReentryLine.Contains("connectionGeneration=2002");

            return Expect(collisionIdentityCorrelates, resourceIdentityRetained,
                reentryCancelledPendingWithoutSecondAcquire, lineCarriesContract,
                "collision=" + Describe(collisionReentry) + " resource=" + Describe(resourceReentry)
                + " line=" + collisionReentryLine
                + " resourceAcquires=" + resource.Acquires.Count + " collisionAcquires=" + collision.Acquires.Count
                + " resourceReleases=" + resource.Releases.Count + " collisionReleases=" + collision.Releases.Count);
        }
    }
}
