using SDG.Unturned;
using SteamP2PFriends.Host;
using SteamP2PFriends.Core.Patches;
using SteamP2PFriends.Shared;
using Steamworks;
using System;
using System.Collections.Generic;
using SteamP2PFriends.Core.Identity;
using SteamP2PFriends.Adapters.Collision;
using SteamP2PFriends.Adapters.Collision.Patches;
using SteamP2PFriends.Adapters.Item.Patches;
using SteamP2PFriends.Adapters.Resource;
using SteamP2PFriends.MultiObserver.Demand;
using SteamP2PFriends.MultiObserver.Lifecycle;
using UnityEngine;

using SteamP2PFriends.Security;

namespace SteamP2PFriends.MultiObserver
{
    /// <summary>
    /// U3 capture and control-plane bridge for the M0 shadow ledger and domain seams. It does
    /// not directly mutate native game state, loaded flags, RPCs, or authorization decisions.
    /// </summary>
    internal static class MultiObserverShadowCoordinator
    {
        private const float ReconcileIntervalSeconds = 1f;
        private const float SummaryIntervalSeconds = 30f;
        private const int SessionEventLogLimit = 160;
        private const int SessionSummaryLogLimit = 120;

        private sealed class ConnectionIdentity
        {
            internal object SteamPlayerReference;
            internal object TransportReference;
            internal ulong Token;
        }

        private sealed class CaptureResult
        {
            internal readonly List<ObserverShadowSample> Samples = new List<ObserverShadowSample>();

            /// <summary>逐条准入输入：每条原生记录是否可用（含身份不可读的记录）。</summary>
            internal readonly List<ObserverSampleRecord> Records = new List<ObserverSampleRecord>();

            internal Dictionary<ulong, ConnectionIdentity> StagedConnections;
            internal ulong StagedNextConnectionToken;

            /// <summary>整批是否可判定（客户端名册本身可用）。false ＝ 本拍整批暂缓。</summary>
            internal bool IsBatchUsable = true;

            internal string BatchFailureReason = string.Empty;
        }

        private sealed class ValidatedObserver
        {
            internal SteamPlayer SteamPlayer;
            internal Player Player;
            internal PlayerMovement Movement;
            internal ulong ObserverId;
        }

        private static readonly MultiObserverShadowLedger Ledger = new MultiObserverShadowLedger();
        private static readonly Dictionary<ulong, ConnectionIdentity> Connections =
            new Dictionary<ulong, ConnectionIdentity>();
        private static readonly Dictionary<string, string> LastMismatch =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly ShadowShutdownGate ShutdownGate = new ShadowShutdownGate();
        private static readonly ShadowFaultBackoff FaultBackoff = new ShadowFaultBackoff();

        /// <summary>
        /// 会话身份门：把「身份缺失/不一致」从一条去重日志后的永久静默变成有限恢复或显式熔断。
        /// 身份由宿主会话建立时确认（<see cref="NotifyHostSessionStarted"/>），会话结束即复位。
        /// </summary>
        private static readonly SessionIdentityGate IdentityGate = new SessionIdentityGate(
            new HeartbeatPolicy(IdentityHeartbeatSeconds, IdentityHeartbeatRepeats),
            IdentityRecoveryWindowSeconds);

        /// <summary>共享面故障心跳的节奏（协调器自己的声明，不借用领域政策）。</summary>
        private static readonly HeartbeatPolicy SharedFaultHeartbeat = new HeartbeatPolicy(5f, 6);

        private static BoundedHeartbeat _sharedFaultHeartbeat;
        private static bool _sharedFaulted;

        private const float IdentityHeartbeatSeconds = 5f;
        private const int IdentityHeartbeatRepeats = 6;
        private const float IdentityRecoveryWindowSeconds = 15f;
        /// <summary>Resource 生产接缝持有的共享投影引擎（同一实例）。</summary>
        private static ResourceProductionControlSeam ResourceProduction;
        private static LifecycleOrchestrationEngine ControlPlaneLifecycle;
        private static CollisionExecutionPort CollisionProduction;
        private static DemandPolicy _collisionDemandPolicy;
        private static readonly HashSet<ulong> CollisionObservers = new HashSet<ulong>();

        private static readonly HashSet<ulong> ResourceObservers = new HashSet<ulong>();

        private static ulong _nextConnectionToken;
        private static float _nextReconcileAt;
        private static float _nextSummaryAt;
        private static int _eventLogCount;
        private static int _summaryLogCount;
        private static string _hostSessionId;

        internal static ulong SessionEpoch => Ledger.SessionEpoch;
        internal static int ObserverCount => Ledger.ObserverCount;

        internal static bool IsResourceProductionActive => ResourceProduction != null && ResourceProduction.IsSessionActive;
        internal static ulong ResourceSessionEpoch => ResourceProduction == null ? 0UL : ResourceProduction.SessionEpoch.Value;

        internal static ulong GetResourceConnectionGeneration(ulong observerId)
        {
            ulong generation;
            return ResourceProduction != null && ResourceProduction.TryGetConnectionGeneration(observerId, out generation)
                ? generation
                : 0UL;
        }

        internal static int GetZombieDemandCount(byte bound) => Ledger.GetZombieDemand(BoundKey.FromNative(bound));

        /// <summary>
        /// Control Plane 唯一接线点：建立唯一的观察者空间事实与唯一的共享投影引擎，注册各领域
        /// 自己声明的 Demand Policy（Resource 与 Collision 共用同一份事实、各持有自己一份领域投影），
        /// 再把 Resource 生产接缝与 Collision 只读影子接到该引擎上。
        /// 半径与世界尺寸各自读取自己声明的来源，不引入跨域共享默认半径。
        /// </summary>
        internal static void ConfigureControlPlane(ResourceDomainAdapter adapter)
        {
            if (adapter == null) throw new ArgumentNullException(nameof(adapter));
            // 世界尺寸只读一次，两个领域各自读自己的半径来源；不存在跨域共享的默认半径。
            byte worldSize = checked((byte)Regions.WORLD_SIZE);
            DemandPolicy resourceDemandPolicy = ResourceDemandPolicy.Create(
                checked((byte)LevelGround.RESOURCE_REGIONS), worldSize);
            DemandPolicy collisionDemandPolicy = CollisionDemandPolicy.Create(
                checked((byte)LevelObjects.OBJECT_REGIONS), worldSize);
            var observerAuthority = new ObserverSpatialAuthority();
            var demandProjection = new DemandProjectionEngine(observerAuthority);
            demandProjection.Register(resourceDemandPolicy);
            demandProjection.Register(collisionDemandPolicy);
            // 共享引擎服务多个领域：诊断出口按记录自带的领域身份路由（Resource 走既有
            // [ResourceObs] 取证格式，Collision 与引擎级转换走默认 [LifecycleObs] 格式），
            // 领域身份来自实际执行端口，不由资源出口默认值改写。
            var lifecycleDiagnostics = new DomainRoutingLifecycleDiagnostics(
                new Dictionary<DomainId, ILifecycleDiagnostics>
                {
                    { DomainIds.Resource, ResourceLifecycleDiagnostics.Instance },
                    { DomainIds.Collision, DefaultLifecycleDiagnostics.Instance }
                },
                DefaultLifecycleDiagnostics.Instance);
            ControlPlaneLifecycle = new LifecycleOrchestrationEngine(
                demandProjection, lifecycleDiagnostics);
            ResourceProduction = new ResourceProductionControlSeam(
                adapter,
                adapter,
                ResourceRegionLifecycleAdapter.GetGeneration,
                demandProjection,
                resourceDemandPolicy,
                ResourceRegionLifecycleAdapter.DefaultHysteresisSeconds,
                ControlPlaneLifecycle);
            var collisionStore = new LevelObjectCollisionAdapter();
            CollisionProduction = new CollisionExecutionPort(
                collisionStore,
                LevelObjectCollisionAdapter.GetGeneration,
                CollisionLifecyclePolicy.Create(),
                message => RoleLogger.Info("[Host]", "[CollisionExecution] " + message));
            ControlPlaneLifecycle.Register(collisionDemandPolicy, CollisionProduction);
            ResourceObservers.Clear();
            CollisionObservers.Clear();
            _collisionDemandPolicy = collisionDemandPolicy;
        }

        internal static void Initialize()
        {
            ShutdownGate.PrepareForInitialize();
        }

        internal static void Tick(bool enabled)
        {
            ThreadUtil.assertIsGameThread();

            if (ShutdownGate.Consume())
            {
                EndSessionIfNeeded("deferred-plugin-shutdown");
                ResetManagedState(resetLogQuotas: true);
                _hostSessionId = null;
                IdentityGate.Reset();
                return;
            }
            if (ShutdownGate.IsLatched) return;

            float now = Time.realtimeSinceStartup;
            if (FaultBackoff.IsFaulted)
            {
                if (!FaultBackoff.TryBeginRecovery(now))
                {
                    // 退避等待期：故障心跳按真实时间继续推进到显式终止；同时推进引擎时钟，
                    // 使引擎侧「写入挂起」的有界心跳不会停在首条。
                    ReportSharedFaultHeartbeat(now, null);
                    AdvanceEngineClock();
                    return;
                }
                // 共享面故障的恢复只隔离共享面自己的状态：不结束会话。僵尸/旁路异常经共享外层
                // 捕获进来时若顺手 EndSession，就会跨域清掉资源等领域的租约；写入闸门改由
                // 引擎的挂起态把住（有界心跳 + 恢复闭环），Session 本身留给会话身份轴。
                // 观察者追踪与连接身份也必须留着：引擎里的租约与需求仍在，追踪集合一旦被清掉，
                // 这些观察者之后即使确认缺席也再也不会被清理，变成永久幽灵需求。
                ForceImmediateReconcile();
                ResourceProduction?.SuspendWrites("shared-fault-recovery");
                SafeWarn($"fault-recovery attempt={FaultBackoff.Attempt} "
                    + "writesSuspended=true sessionEnded=false trackingRetained=true");
            }

            bool active = enabled && HostManager.ShouldProcessClientHostListen();
            if (!active)
            {
                EndSessionIfNeeded(enabled ? "listen-host-inactive" : "disabled");
                ResetManagedState(resetLogQuotas: true);
                return;
            }

            string currentSessionId = HostManager.CurrentSessionId;
            SessionIdentityDecision identity = IdentityGate.Observe(currentSessionId, now);
            if (identity.Changed && IdentityGate.IsReady)
            {
                if (identity.RequiresResynchronization)
                {
                    // 熔断后身份重新可用：失明期间可能已经换了世界，旧 Session Epoch 与旧代次
                    // 不得再写入，必须重建会话后才能继续。
                    EndSessionIfNeeded("session-identity-resynchronized");
                    ResetManagedState(resetLogQuotas: true);
                }
                SafeInfo("session-identity=" + identity.State + " identity="
                    + MaskSession(currentSessionId)
                    + " resynchronized=" + identity.RequiresResynchronization.ToString().ToLowerInvariant());
            }
            if (!IdentityGate.IsReady)
            {
                // 身份缺失或不一致：先在有限窗口内恢复，恢复不了就显式熔断；两种状态下都
                // 阻止写入与破坏性释放，但不清空任何领域——不确定不等于终止。
                ResourceProduction?.SuspendWrites("host-session-identity-" + IdentityGate.State);
                if (identity.ShouldEmit)
                {
                    SafeWarn("session-identity=" + identity.State + " notified="
                        + MaskSession(IdentityGate.AcknowledgedIdentity) + " current="
                        + MaskSession(currentSessionId) + " heartbeat=" + identity.Heartbeat
                        + " writesSuspended="
                        + (ResourceProduction?.IsWriteSuspended ?? false).ToString().ToLowerInvariant()
                        + " failClosed=true");
                }
                AdvanceEngineClock();
                return;
            }
            ClearMismatch("host-session-identity");

            string worldIdentity = BuildWorldIdentity(currentSessionId);
            if (Ledger.BeginSession(worldIdentity))
            {
                Connections.Clear();
                LastMismatch.Clear();
                ResourceObservers.Clear();
                CollisionObservers.Clear();
                if (ResourceProduction != null)
                {
                    ResourceProduction.BeginSession(Core.Identity.SessionEpoch.FromNative(Ledger.SessionEpoch));
                }
                _eventLogCount = 0;
                _summaryLogCount = 0;
                _nextSummaryAt = now;
                    SafeInfo($"session-begin epoch={Ledger.SessionEpoch} world={worldIdentity}");
            }

            ResourceProduction?.AdvanceTime(Time.deltaTime);

            if (now < _nextReconcileAt)
            {
                return;
            }
            _nextReconcileAt = now + ReconcileIntervalSeconds;

            CaptureResult capture = CaptureSamples();
            ObserverSampleAdmissionPlan plan = ObserverSampleAdmission.Plan(
                capture.Records, capture.IsBatchUsable, capture.BatchFailureReason);
            if (plan.IsBatchRejected)
            {
                // 整批不可判定：本拍不提交也不移除任何人，并把已知观察者登记为暂缓。
                SafeMismatch("capture-rejected",
                    "capture-rejected reason=" + capture.BatchFailureReason
                    + "; observers retained as deferred demand");
                DeferKnownObservers("capture-batch-rejected:" + capture.BatchFailureReason);
                return;
            }
            ClearMismatch("capture-rejected");
            ClearMismatch("capture-incomplete");

            // 暂缓集只构造一次：连接身份续用、资源侧删除豁免与追踪集合三处共用同一份。
            var deferred = new HashSet<ulong>(plan.DeferredObserverIds);
            CommitConnectionIdentities(capture, deferred);
            if (deferred.Count > 0)
            {
                SafeEvent("capture-deferred count=" + deferred.Count
                    + " admissible=" + plan.AdmissibleObserverIds.Count
                    + " unknownIdentity=" + (!plan.AllowAbsenceRemoval).ToString().ToLowerInvariant());
            }

            IReadOnlyList<ShadowTransition> transitions =
                Ledger.Reconcile(
                    capture.Samples,
                    Regions.WORLD_SIZE,
                    ItemManager.ITEM_REGIONS,
                    plan.AllowAbsenceRemoval);
            ReconcileResourceProduction(capture.Samples, deferred, plan);
            ReconcileCollisionProduction(capture.Samples, deferred, plan);
            ControlPlaneLifecycle?.AdvanceTime(Time.deltaTime);
            ControlPlaneLifecycle?.Flush(0f);
            ResourceProduction?.Flush(0f);

            foreach (ShadowTransition transition in transitions)
            {
                SafeEvent(
                    $"transition={transition.Kind} observer={Mask(transition.ObserverId)} {transition.Detail}");
            }

            AuditNativeState();
            if (now >= _nextSummaryAt)
            {
                _nextSummaryAt = now + SummaryIntervalSeconds;
                int pendingCount = 0;
                IReadOnlyList<ObserverShadowSnapshot> observers = Ledger.SnapshotObservers();
                foreach (ObserverShadowSnapshot observer in observers)
                {
                    if (!observer.GameplayAuthorized) pendingCount++;
                }

                if (_summaryLogCount < SessionSummaryLogLimit)
                {
                    _summaryLogCount++;
                    // 两个需求计数口径不同：resourceDemandRegions 是接缝已物化租约的区域数，
                    // resourceProjectedDemandRegions 是共享投影引擎按政策算出的需求区域数。
                    // 二者长期分叉即为「有投影、未物化」的信号（暂缓/失败重试路径），应查明原因。
                    SafeInfo(
                        $"summary={_summaryLogCount}/{SessionSummaryLogLimit} epoch={Ledger.SessionEpoch} " +
                        $"observers={Ledger.ObserverCount} pendingObservers={pendingCount} " +
                        $"itemDemandRegions={Ledger.ItemDemandRegionCount} " +
                        $"zombieDemandBounds={Ledger.ZombieDemandBoundCount} " +
                        $"resourceDemandRegions={ResourceProduction?.DemandRegionCount ?? 0} " +
                        $"resourceProjectedDemandRegions={ResourceProduction?.ProjectedDemandRegionCount ?? 0} " +
                        $"resourceRadiusSource={ResourceProduction?.DemandPolicy.RadiusSource ?? "none"} " +
                        $"resourceActiveLeases={ResourceProduction?.ActiveLeaseCount ?? 0} " +
                        $"resourcePendingReleases={ResourceProduction?.PendingReleaseCount ?? 0} " +
                        $"resourceDeferredObservers={ResourceProduction?.DeferredObserverCount ?? 0} " +
                        $"resourceWritesSuspended=" +
                        (ResourceProduction?.IsWriteSuspended ?? false).ToString().ToLowerInvariant() + " " +
                        $"collisionDemandRegions={ControlPlaneLifecycle?.DemandRegionCount(DomainIds.Collision) ?? 0} " +
                        $"collisionActiveLeases={ControlPlaneLifecycle?.ActiveLeaseCount(DomainIds.Collision) ?? 0} " +
                        $"collisionPendingReleases={ControlPlaneLifecycle?.PendingReleaseCount(DomainIds.Collision) ?? 0} " +
                        $"collisionRadiusSource={_collisionDemandPolicy?.RadiusSource ?? "none"} " +
                        "collisionWriter=CollisionExecutionPort controlPlane=active");
                }
            }

            // 共享面本拍走通：确认恢复并解除故障挂起（身份挂起由身份门自己把守，不在这里解除）。
            ReportSharedFaultRecovered();
            if (IdentityGate.IsReady) ResourceProduction?.ResumeWrites("shared-fault-cleared");
            FaultBackoff.MarkSuccess();
        }

        internal static void NotifyHostSessionStarted(string sessionId)
        {
            ThreadUtil.assertIsGameThread();
            if (string.IsNullOrEmpty(sessionId))
                throw new ArgumentException("Host session identity is required.", nameof(sessionId));
            if (!string.Equals(_hostSessionId, sessionId, StringComparison.Ordinal))
            {
                EndSessionIfNeeded("host-session-replaced");
                ResetManagedState(resetLogQuotas: true);
                _hostSessionId = sessionId;
            }
            IdentityGate.Bind(sessionId);
        }

        internal static void NotifyHostSessionEnded(string sessionId, string reason)
        {
            ThreadUtil.assertIsGameThread();
            if (!string.IsNullOrEmpty(_hostSessionId)
                && !string.IsNullOrEmpty(sessionId)
                && !string.Equals(_hostSessionId, sessionId, StringComparison.Ordinal))
            {
                SafeEvent("ignored stale session-end notification");
                return;
            }

            EndSessionIfNeeded("host-session-ended:" + (reason ?? "unknown"));
            ResetManagedState(resetLogQuotas: true);
            _hostSessionId = null;
            IdentityGate.Reset();
        }

        internal static void HandleTickFailure(Exception exception)
        {
            try
            {
                float now = Time.realtimeSinceStartup;
                if (!FaultBackoff.RecordFailure(now)) return;
                // 共享面故障也是持续异常：按有界心跳写出（首条 + 按间隔重复 + 显式终止），
                // 恢复时写闭环。逐次容量上限会让持续故障在若干条之后永久静默，因此不再用它封顶。
                StartSharedFaultHeartbeat(now);
                ReportSharedFaultHeartbeat(now, exception);
            }
            catch { }
        }

        /// <summary>
        /// 共享面故障的有界心跳起搏。持续故障在退避等待期间不会再有新的失败记录，
        /// 因此心跳由「已在故障中」这一事实驱动（等待期每拍按真实时间推进），
        /// 而不是只在失败发生时才推进——否则等待期就是静默期。
        /// </summary>
        private static void StartSharedFaultHeartbeat(float now)
        {
            // 故障 episode 的活跃标记与心跳是否仍在运行是两件事：心跳用尽（Exhausted）后
            // 仍在故障中，恢复时必须照样写闭环——闭环由 episode 决定，心跳只决定重复输出。
            // 同理，同一 episode 内不得重启心跳：退避失败会再次进入这里，若按 IsRunning 重建，
            // 「有界心跳」就会被无限重启，退回刷屏。
            if (_sharedFaulted) return;
            _sharedFaulted = true;
            _sharedFaultHeartbeat = new BoundedHeartbeat(SharedFaultHeartbeat, now);
        }

        private static void ReportSharedFaultHeartbeat(float now, Exception exception)
        {
            EBoundedHeartbeat kind = _sharedFaultHeartbeat.IsRunning
                ? _sharedFaultHeartbeat.Next(now)
                : EBoundedHeartbeat.Suppressed;
            if (kind == EBoundedHeartbeat.Suppressed) return;
            SafeWarn($"fault heartbeat={kind} attempt={FaultBackoff.Attempt} " +
                $"retryAt={FaultBackoff.NextRecoveryAt:F1} " +
                $"type={exception?.GetType().Name ?? "unknown"}; legacy writers unchanged");
        }

        /// <summary>共享面重新走通：写恢复闭环并结束故障心跳。</summary>
        private static void ReportSharedFaultRecovered()
        {
            if (!_sharedFaulted) return;
            _sharedFaulted = false;
            _sharedFaultHeartbeat.Stop();
            SafeWarn("fault-cleared recovered=true sessionEnded=false trackingRetained=true");
        }

        internal static void Shutdown()
        {
            ShutdownGate.Request();
            try
            {
                ThreadUtil.assertIsGameThread();
            }
            catch
            {
                // Never mutate dictionaries off-thread. A future game-thread Tick drains this
                // request; process teardown otherwise reclaims managed state.
                return;
            }

            if (ShutdownGate.Consume())
            {
                EndSessionIfNeeded("plugin-shutdown");
                ResetManagedState(resetLogQuotas: true);
                _hostSessionId = null;
                IdentityGate.Reset();
            }
        }

        private static CaptureResult CaptureSamples()
        {
            var result = new CaptureResult();
            var clients = Provider.clients;
            if (clients == null) return RejectBatch(result, "client-list-unavailable");

            SteamPlayer[] snapshot;
            try
            {
                int count = clients.Count;
                if (count > 64) return RejectBatch(result, "observer-capacity-exceeded");
                snapshot = new SteamPlayer[count];
                for (int index = 0; index < count; index++) snapshot[index] = clients[index];
                if (clients.Count != count) return RejectBatch(result, "client-list-changed-during-copy");
            }
            catch (Exception ex)
            {
                return RejectBatch(result, "client-snapshot-error:" + ex.GetType().Name);
            }

            var validated = new List<ValidatedObserver>(snapshot.Length);
            var unusableObserverIds = new List<ulong>();
            var activeObservers = new HashSet<ulong>();
            for (int index = 0; index < snapshot.Length; index++)
            {
                SteamPlayer steamPlayer = snapshot[index];
                Player player = steamPlayer?.player;
                PlayerMovement movement = player?.movement;
                object transport = steamPlayer?.transportConnection;
                ulong observerId = steamPlayer?.playerID?.steamID.m_SteamID ?? 0UL;
                if (observerId == 0UL
                    || player == null
                    || movement == null
                    || transport == null
                    || movement.loadedRegions == null
                    || !activeObservers.Add(observerId))
                {
                    // 单条记录不可用只暂缓它自己的观察者：其余有效观察者照常提交本拍更新，
                    // 不冻结整批；这条记录也不被当作「已离开」，因此不会触发破坏性释放。
                    // 身份读不到（observerId=0）时按「无法证明谁在场」处理，本拍不再按缺席移除。
                    unusableObserverIds.Add(observerId);
                    continue;
                }
                validated.Add(new ValidatedObserver
                {
                    SteamPlayer = steamPlayer,
                    Player = player,
                    Movement = movement,
                    ObserverId = observerId
                });
            }

            var stagedConnections = new Dictionary<ulong, ConnectionIdentity>();
            ulong stagedNextToken = _nextConnectionToken;
            try
            {
                foreach (ValidatedObserver observer in validated)
                {
                    if (!TryGetStagedConnectionToken(
                        observer.ObserverId,
                        observer.SteamPlayer,
                        stagedConnections,
                        ref stagedNextToken,
                        out ulong connectionToken,
                        out string tokenFailure))
                    {
                        // 连接代次不可得同样只暂缓该观察者：它本拍没有可用样本，
                        // 但既有的连接身份与令牌原样保留，不推进也不释放。
                        ResourceObservability.Error("[Host]", "ConnectionGeneration", "-",
                            Ledger.SessionEpoch, 0UL, 0U, "Fallback", true, "failed",
                            "reason=" + tokenFailure + " failClosed=true observer=" + Mask(observer.ObserverId));
                        unusableObserverIds.Add(observer.ObserverId);
                        continue;
                    }
                    CountNativeLoadedItemRegions(
                        observer.Movement,
                        out int nativeLoadedItems,
                        out int nativeStaleLoadedItems);
                    int committedItems = CountCommittedItemRegions(
                        observer.Movement.region_x,
                        observer.Movement.region_y);
                    // Mirror ZombieManager.onBoundUpdated: a player outside all navigation bounds
                    // remains a world observer, but has no functional zombie region demand.
                    bool hasFunctionalZombieBound = LevelNavigation.checkSafe(observer.Movement.bound);
                    bool local = observer.Player.channel != null && observer.Player.channel.IsLocalPlayer;
                    bool authorized = local || !P2PApprovalManager.IsPending(new CSteamID(observer.ObserverId));

                    result.Samples.Add(new ObserverShadowSample(
                        observer.ObserverId,
                        connectionToken,
                        observer.Movement.region_x,
                        observer.Movement.region_y,
                        BoundKey.FromNative(observer.Movement.bound),
                        hasFunctionalZombieBound,
                        local,
                        authorized,
                        nativeLoadedItems,
                        nativeStaleLoadedItems,
                        committedItems));
                }
            }
            catch (Exception ex)
            {
                return RejectBatch(result, "capture-build-error:" + ex.GetType().Name);
            }

            foreach (ulong unusableObserverId in unusableObserverIds)
                result.Records.Add(new ObserverSampleRecord(unusableObserverId, usable: false));
            foreach (ValidatedObserver observer in validated)
                result.Records.Add(new ObserverSampleRecord(observer.ObserverId, usable: true));

            result.StagedConnections = stagedConnections;
            result.StagedNextConnectionToken = stagedNextToken;
            return result;
        }

        /// <summary>
        /// 整批不可判定（客户端名册缺失、容量超限、拷贝期间变化、构建异常）：本拍既不提交
        /// 任何样本，也不移除任何观察者，并在共享引擎上把这些观察者登记为暂缓——他们的既有
        /// 贡献原样保留（延迟释放属票 09 的运行时核对口径）。
        /// </summary>
        private static CaptureResult RejectBatch(CaptureResult result, string reason)
        {
            result.IsBatchUsable = false;
            result.BatchFailureReason = reason;
            result.Samples.Clear();
            result.Records.Clear();
            result.StagedConnections = null;
            SafeEvent("capture-rejected reason=" + reason);
            return result;
        }

        private static void AuditNativeState()
        {
            IReadOnlyList<ObserverShadowSnapshot> observers = Ledger.SnapshotObservers();
            foreach (ObserverShadowSnapshot observer in observers)
            {
                int desiredCount = observer.ItemRegionCount;
                if (!observer.IsLocalPlayer && observer.NativeLoadedItemRegions != desiredCount)
                {
                    SafeMismatch(
                        $"item-loaded:{observer.ObserverId}",
                        $"observer={Mask(observer.ObserverId)} desired={desiredCount} " +
                        $"nativeLoaded={observer.NativeLoadedItemRegions} committed={observer.CommittedItemRegions}");
                }
                else
                {
                    ClearMismatch($"item-loaded:{observer.ObserverId}");
                }

                if (observer.NativeStaleLoadedItemRegions > 0)
                {
                    SafeMismatch(
                        $"item-stale:{observer.ObserverId}",
                        $"observer={Mask(observer.ObserverId)} staleLoadedOutsideDesired=" +
                        observer.NativeStaleLoadedItemRegions);
                }
                else
                {
                    ClearMismatch($"item-stale:{observer.ObserverId}");
                }

                if (observer.CommittedItemRegions != desiredCount)
                {
                    SafeMismatch(
                        $"item-authority:{observer.ObserverId}",
                        $"observer={Mask(observer.ObserverId)} desired={desiredCount} " +
                        $"authorityCommitted={observer.CommittedItemRegions}");
                }
                else
                {
                    ClearMismatch($"item-authority:{observer.ObserverId}");
                }
            }

            IReadOnlyDictionary<BoundKey, int> demand = Ledger.SnapshotZombieDemand();
            var activeZombieMismatchKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (KeyValuePair<BoundKey, int> pair in demand)
            {
                string mismatchKey = $"zombie-demand:{pair.Key}";
                activeZombieMismatchKeys.Add(mismatchKey);
                int nativeCount = ReadNativeZombiePlayerCount(pair.Key.Value);
                if (nativeCount != pair.Value)
                {
                    SafeMismatch(
                        mismatchKey,
                        $"bound={pair.Key} observerDemand={pair.Value} nativePlayerCount={nativeCount}");
                }
                else
                {
                    ClearMismatch(mismatchKey);
                }
            }

            var staleZombieKeys = new List<string>();
            foreach (string key in LastMismatch.Keys)
            {
                if (key.StartsWith("zombie-demand:", StringComparison.Ordinal)
                    && !activeZombieMismatchKeys.Contains(key))
                {
                    staleZombieKeys.Add(key);
                }
            }
            foreach (string key in staleZombieKeys) ClearMismatch(key);
        }

        internal static bool TryAllocateConnectionGeneration(ref ulong next, out ulong token)
        {
            token = 0UL;
            if (next == ulong.MaxValue) return false;
            next++;
            if (next == 0UL) return false;
            token = next;
            return true;
        }

        private static bool TryGetStagedConnectionToken(
            ulong observerId,
            SteamPlayer steamPlayer,
            Dictionary<ulong, ConnectionIdentity> stagedConnections,
            ref ulong stagedNextToken,
            out ulong token,
            out string failure)
        {
            token = 0UL;
            failure = "none";
            object transport = steamPlayer.transportConnection;
            if (Connections.TryGetValue(observerId, out ConnectionIdentity identity)
                && ReferenceEquals(identity.SteamPlayerReference, steamPlayer)
                && ReferenceEquals(identity.TransportReference, transport))
            {
                stagedConnections[observerId] = identity;
                token = identity.Token;
                return true;
            }

            if (!TryAllocateConnectionGeneration(ref stagedNextToken, out token))
            {
                failure = "connection-generation-overflow";
                return false;
            }
            identity = new ConnectionIdentity
            {
                SteamPlayerReference = steamPlayer,
                TransportReference = transport,
                Token = stagedNextToken
            };
            stagedConnections[observerId] = identity;
            return true;
        }

        private static void CountNativeLoadedItemRegions(
            PlayerMovement movement,
            out int desiredLoaded,
            out int staleLoaded)
        {
            LoadedRegion[,] loadedRegions = movement.loadedRegions;
            if (loadedRegions == null) throw new InvalidOperationException("loadedRegions unavailable");

            desiredLoaded = 0;
            staleLoaded = 0;
            int radius = ItemManager.ITEM_REGIONS;
            int maxX = Math.Min(loadedRegions.GetLength(0), Regions.WORLD_SIZE);
            int maxY = Math.Min(loadedRegions.GetLength(1), Regions.WORLD_SIZE);
            for (int x = 0; x < maxX; x++)
            {
                for (int y = 0; y < maxY; y++)
                {
                    LoadedRegion region = loadedRegions[x, y];
                    if (region == null || !region.isItemsLoaded) continue;
                    bool desired = Math.Abs(x - movement.region_x) <= radius
                        && Math.Abs(y - movement.region_y) <= radius;
                    if (desired) desiredLoaded++;
                    else staleLoaded++;
                }
            }
        }

        private static int CountCommittedItemRegions(byte centerX, byte centerY)
        {
            int count = 0;
            int radius = ItemManager.ITEM_REGIONS;
            for (int x = centerX - radius; x <= centerX + radius; x++)
            {
                for (int y = centerY - radius; y <= centerY + radius; y++)
                {
                    if (!Regions.checkSafe(x, y)) continue;
                    if (AuthoritativeItemGenerationGatePatch.GetStateForShadow((byte)x, (byte)y)
                        == RegionGenerationState.Committed)
                    {
                        count++;
                    }
                }
            }
            return count;
        }

        private static int ReadNativeZombiePlayerCount(byte bound)
        {
            ZombieRegion[] regions = ZombieManager.regions;
            if (regions == null || bound >= regions.Length || regions[bound] == null) return 0;
            return regions[bound].PlayerCountInRegion;
        }

        private static string BuildWorldIdentity(string sessionId)
        {
            string serverId = Provider.serverID ?? string.Empty;
            string map = Provider.map ?? string.Empty;
            return sessionId + "|" + serverId + "|" + map;
        }

        private static void CommitConnectionIdentities(
            CaptureResult capture, HashSet<ulong> deferred)
        {
            // 暂缓观察者的连接身份与令牌必须原样续用：本拍没读到记录不等于换了连接，
            // 更不等于离开——身份被顺手作废就会让它的下一次可用样本变成「重连」，
            // 从而把同一观察者的贡献拆成两个连接代次。
            var carried = new Dictionary<ulong, ConnectionIdentity>();
            foreach (ulong observerId in deferred)
            {
                if (Connections.TryGetValue(observerId, out ConnectionIdentity identity))
                    carried[observerId] = identity;
            }

            var removed = new List<ulong>();
            foreach (ulong observerId in Connections.Keys)
            {
                if (capture.StagedConnections.ContainsKey(observerId)) continue;
                if (carried.ContainsKey(observerId)) continue;
                removed.Add(observerId);
            }
            Connections.Clear();
            foreach (KeyValuePair<ulong, ConnectionIdentity> pair in capture.StagedConnections)
                Connections.Add(pair.Key, pair.Value);
            foreach (KeyValuePair<ulong, ConnectionIdentity> pair in carried)
            {
                if (!Connections.ContainsKey(pair.Key)) Connections.Add(pair.Key, pair.Value);
            }
            _nextConnectionToken = capture.StagedNextConnectionToken;
            foreach (ulong observerId in removed)
                RemoveObserverMismatch(observerId);
        }

        /// <summary>
        /// 把当前已知的资源观察者整体登记为暂缓（整批不可判定时）：本拍既没有证据证明它们
        /// 离开，也没有证据刷新它们的位置，因此保留既有贡献、不产生破坏性释放。
        /// </summary>
        private static void DeferKnownObservers(string reason)
        {
            foreach (ulong observerId in ResourceObservers)
                ResourceProduction?.DeferObserver(observerId, reason);
            foreach (ulong observerId in CollisionObservers)
                ControlPlaneLifecycle?.DeferObserver(
                    DomainIds.Collision, observerId, reason);
        }

        private static void EndSessionIfNeeded(string reason)
        {
            bool ledgerEnded = Ledger.EndSession();
            // Control Plane 侧的会话状态无条件收尾：即便 Ledger 已非活动（状态短暂失配或
            // 重复/失序的会话结束通知），唯一空间事实与领域投影也必须清干净，不得跨会话残留。
            try
            {
                ResourceProduction?.EndSession();
            }
            catch (Exception ex)
            {
                SafeWarn($"session-end resource cleanup failed type={ex.GetType().Name}; managed state cleared fail-closed");
            }
            finally
            {
                ResourceObservers.Clear();
                if (ledgerEnded)
                {
                    SafeInfo($"session-end nextEpoch={Ledger.SessionEpoch} reason={reason}");
                }
            }
        }

        private static void ResetManagedState(bool resetLogQuotas)
        {
            Connections.Clear();
            ResourceObservers.Clear();
            CollisionObservers.Clear();
            LastMismatch.Clear();

            if (resetLogQuotas)
            {
                _eventLogCount = 0;
                _summaryLogCount = 0;
            }
            ForceImmediateReconcile();
            _nextSummaryAt = 0f;
        }

        /// <summary>
        /// 强制下一拍立即重新核对（不清任何观察者状态）。故障恢复与状态重置都经它表达同一语义，
        /// 避免「立即重核」这件事在两处各自内联、日后漂移。
        /// </summary>
        private static void ForceImmediateReconcile()
        {
            _nextReconcileAt = 0f;
        }

        private static void ReconcileResourceProduction(
            IReadOnlyList<ObserverShadowSample> samples,
            HashSet<ulong> deferred,
            ObserverSampleAdmissionPlan plan)
        {
            if (ResourceProduction == null)
            {
                ResourceObservability.NoticeOnce("resource-production-unavailable", "[Host]",
                    "ResourceProduction", "-", Ledger.SessionEpoch, 0UL, 0U,
                    "Fallback", false, "skipped", "reason=production-control-seam-unavailable");
                return;
            }

            var seen = new HashSet<ulong>();
            foreach (ObserverShadowSample sample in samples)
            {
                // 每个进程内的有效观察者都提交事实，资格由 Resource Demand Policy 声明、由共享
                // 投影引擎执行：不合格（未获玩法资格）只是不贡献新需求，其既有贡献进入
                // Deferred Observer Demand 而不是被当作确认离开——「无法证明离开就不做破坏性
                // 释放」。只有真正缺席（样本列表里没有且不在暂缓集里）才走 RemoveObserver。
                if (sample.ObserverId == 0UL || sample.ConnectionToken == 0UL
                    || !seen.Add(sample.ObserverId))
                    continue;

                ResourceProduction.UpdateObserver(
                    sample.ObserverId,
                    sample.ConnectionToken,
                    sample.ItemRegionX,
                    sample.ItemRegionY,
                    sample.GameplayAuthorized);
            }

            foreach (ulong observerId in deferred)
                ResourceProduction.DeferObserver(observerId, "observer-sample-unusable");

            var previouslyTracked = new List<ulong>(ResourceObservers);
            bool allowAbsenceRemoval = plan.AllowAbsenceRemoval;
            if (allowAbsenceRemoval)
            {
                var removed = new List<ulong>();
                foreach (ulong observerId in ResourceObservers)
                {
                    if (!seen.Contains(observerId) && !deferred.Contains(observerId)) removed.Add(observerId);
                }
                foreach (ulong observerId in removed)
                    ResourceProduction.RemoveObserver(observerId);
            }
            else
            {
                // 本拍存在身份不可读的记录：谁在场无法判定，因此本拍不具备按缺席移除的资格。
                // 已追踪集合原样保留，等下一拍拿到可用样本时再判缺席。
                ResourceObservability.NoticeOnce("resource-absence-removal-deferred", "[Host]",
                    "ResourceProduction", "-", Ledger.SessionEpoch, 0UL, 0U,
                    "Fallback", false, "deferred",
                    "reason=unknown-observer-identity absenceRemovalSuppressed=true");
            }

            ResourceObservers.Clear();
            foreach (ulong observerId in seen) ResourceObservers.Add(observerId);
            foreach (ulong observerId in deferred) ResourceObservers.Add(observerId);
            if (!allowAbsenceRemoval)
            {
                foreach (ulong observerId in previouslyTracked) ResourceObservers.Add(observerId);
            }
        }

        private static void ReconcileCollisionProduction(
            IReadOnlyList<ObserverShadowSample> samples,
            HashSet<ulong> deferred,
            ObserverSampleAdmissionPlan plan)
        {
            if (ControlPlaneLifecycle == null || CollisionProduction == null) return;
            var seen = new HashSet<ulong>();
            foreach (ObserverShadowSample sample in samples)
            {
                if (sample.ObserverId == 0UL || sample.ConnectionToken == 0UL
                    || !seen.Add(sample.ObserverId)) continue;
                ControlPlaneLifecycle.Observe(DomainIds.Collision, sample.ObserverId,
                    sample.ConnectionToken, sample.ItemRegionX, sample.ItemRegionY,
                    sample.GameplayAuthorized);
            }
            foreach (ulong observerId in deferred)
                ControlPlaneLifecycle.DeferObserver(DomainIds.Collision, observerId,
                    "observer-sample-unusable");
            if (plan.AllowAbsenceRemoval)
            {
                var removed = new List<ulong>();
                foreach (ulong observerId in CollisionObservers)
                    if (!seen.Contains(observerId) && !deferred.Contains(observerId)) removed.Add(observerId);
                foreach (ulong observerId in removed)
                {
                    ControlPlaneLifecycle.RemoveObserver(DomainIds.Collision, observerId);
                    CollisionObservers.Remove(observerId);
                }
            }
            foreach (ulong observerId in seen) CollisionObservers.Add(observerId);
            foreach (ulong observerId in deferred) CollisionObservers.Add(observerId);
        }

        private static RegionKey[] ToRegionArray(HashSet<RegionKey> regions)
        {
            var array = new RegionKey[regions.Count];
            regions.CopyTo(array);
            Array.Sort(array, (left, right) => left.Packed.CompareTo(right.Packed));
            return array;
        }

        /// <summary>
        /// 提前返回的路径也要推进引擎时钟：引擎的写入挂起心跳按引擎时钟起搏，
        /// 时钟不走路，持续故障/身份不确定期间的心跳就会停在首条——又退回「一条记录后静默」。
        /// </summary>
        private static void AdvanceEngineClock()
        {
            ResourceProduction?.AdvanceTime(Time.deltaTime);
        }

        private static void SafeMismatch(string key, string detail)
        {
            if (LastMismatch.TryGetValue(key, out string previous)
                && string.Equals(previous, detail, StringComparison.Ordinal))
            {
                return;
            }
            LastMismatch[key] = detail;
            SafeEvent("mismatch " + detail);
        }

        private static void ClearMismatch(string key)
        {
            if (LastMismatch.Remove(key)) SafeEvent("resolved key=" + key);
        }

        private static void RemoveObserverMismatch(ulong observerId)
        {
            string suffix = ":" + observerId;
            var removed = new List<string>();
            foreach (string key in LastMismatch.Keys)
            {
                if (key.EndsWith(suffix, StringComparison.Ordinal)) removed.Add(key);
            }
            foreach (string key in removed) LastMismatch.Remove(key);
        }

        private static void SafeEvent(string message)
        {
            if (_eventLogCount >= SessionEventLogLimit) return;
            _eventLogCount++;
            SafeInfo($"event={_eventLogCount}/{SessionEventLogLimit} {message}");
        }

        private static void SafeInfo(string message)
        {
            try { RoleLogger.Info("[Host]", "[MultiObserver/M0] " + message); }
            catch { }
        }

        private static void SafeWarn(string message)
        {
            try { RoleLogger.Warn("[Shared]", "[MultiObserver/M0] " + message); }
            catch { }
        }

        private static string Mask(ulong observerId)
        {
            try { return DiagnosticMaskUtil.MaskSteamId(observerId); }
            catch { return "mask-error"; }
        }

        private static string MaskSession(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId)) return "missing";
            return sessionId.Length <= 8 ? sessionId : sessionId.Substring(0, 8);
        }
    }
}
