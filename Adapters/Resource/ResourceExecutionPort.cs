using SteamP2PFriends.Core.Identity;
using SteamP2PFriends.MultiObserver.Lifecycle;
using SteamP2PFriends.MultiObserver.SPI;
using System;

namespace SteamP2PFriends.Adapters.Resource
{
    /// <summary>
    /// Resource 域在共享生命周期编排引擎上的执行端口：把共享引擎的编排决策翻译成资源域的
    /// 原生操作，并把原生失败翻译成引擎可执行的结果分类。
    ///
    /// 它只执行，不编排——不扫描观察者、不重算需求、不持有区域集合、租约、滞回或 retry 状态；
    /// 那些全部归共享引擎。原生 ResourceManager 仍是实际网络写入者，本端口把领域适配器接成
    /// 引擎可调用的形状，不新增第二个资源状态写入者。
    /// </summary>
    public sealed class ResourceExecutionPort : IDomainExecutionPort
    {
        private readonly ILifecycleDomainAdapter _lifecycle;
        private readonly IReversibleRegionLifecycleAdapter _reversibleLifecycle;
        private readonly IStateReplicationAdapter _replication;
        private readonly IReversibleObserverReplicationAdapter _reversibleReplication;
        private readonly IReversibleObserverDisconnectAdapter _reversibleDisconnect;
        private readonly Func<RegionKey, uint> _generationReader;

        public ResourceExecutionPort(
            ILifecycleDomainAdapter lifecycle,
            IStateReplicationAdapter replication,
            Func<RegionKey, uint> generationReader,
            LifecyclePolicy lifecyclePolicy)
        {
            _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
            _replication = replication ?? throw new ArgumentNullException(nameof(replication));
            _generationReader = generationReader ?? throw new ArgumentNullException(nameof(generationReader));
            LifecyclePolicy = lifecyclePolicy ?? throw new ArgumentNullException(nameof(lifecyclePolicy));
            _reversibleLifecycle = lifecycle as IReversibleRegionLifecycleAdapter
                ?? throw new ArgumentException(
                    "Resource lifecycle adapter must provide a reversible region state seam.",
                    nameof(lifecycle));
            _reversibleReplication = replication as IReversibleObserverReplicationAdapter;
            _reversibleDisconnect = lifecycle as IReversibleObserverDisconnectAdapter;
            if (_lifecycle.DomainId != DomainIds.Resource)
                throw new ArgumentException(
                    "Resource 执行端口只承载 Resource 身份的领域适配器。", nameof(lifecycle));
        }

        public DomainId DomainId => DomainIds.Resource;

        public string DisplayName => _lifecycle.DisplayName;

        public LifecyclePolicy LifecyclePolicy { get; }

        public void OnSessionBegin(uint sessionEpoch) => _lifecycle.OnSessionBegin(sessionEpoch);

        public void OnSessionEnd() => _lifecycle.OnSessionEnd();

        public void ResetReplication(uint sessionEpoch) => _replication.ResetReplication(sessionEpoch);

        public void OnAcquire(LeaseTicket ticket) => _lifecycle.OnAcquire(ticket);

        public void OnRelease(LeaseTicket ticket) => _lifecycle.OnRelease(ticket);

        public void OnLifecycleTick(float deltaTime) => _lifecycle.OnTick(deltaTime);

        public void OnReplicationTick(float deltaTime) => _replication.OnReplicationTick(deltaTime);

        public void OnObserverReplicationEntered(ulong observerId, ulong connectionToken, RegionKey regionKey) =>
            _replication.OnObserverEntered(observerId, connectionToken, regionKey);

        public void OnObserverReplicationExited(ulong observerId, ulong connectionToken, RegionKey regionKey) =>
            _replication.OnObserverExited(observerId, connectionToken, regionKey);

        public void OnObserverDisconnected(ulong observerId, ulong connectionToken) =>
            _lifecycle.OnObserverDisconnect(observerId, connectionToken);

        public RegionGeneration ReadRegionGeneration(RegionKey regionKey) =>
            RegionGeneration.FromNative(_generationReader(regionKey));

        /// <summary>
        /// 原生 foliage 尚未烘焙该区域＝暂缓（短延迟重试、不计 fault）；其余失败按一般失败隔离。
        /// 引擎不解析异常文本，分类只在这里发生。
        /// </summary>
        public EDomainFailureKind ClassifyRegionFailure(Exception exception)
        {
            return exception is ResourceNativeSnapshotUnavailableException
                ? EDomainFailureKind.Deferred
                : EDomainFailureKind.Failed;
        }

        /// <summary>
        /// 资源释放的业务拒绝表示原生状态未产生副作用，因此不需要补偿；其余失败按「可能有副作用」
        /// 处理，由引擎执行快照补偿。
        /// </summary>
        public EDomainFailureKind ClassifyReleaseFailure(Exception exception)
        {
            return exception is ResourceReleaseRejectedException
                ? EDomainFailureKind.Rejected
                : EDomainFailureKind.Failed;
        }

        public object CaptureRegionState(RegionKey regionKey) =>
            _reversibleLifecycle.CaptureRegionState(regionKey);

        public void RestoreRegionState(RegionKey regionKey, object state) =>
            _reversibleLifecycle.RestoreRegionState(regionKey, state);

        public object CaptureObserverReplicationState(ulong observerId, ulong connectionToken)
        {
            return _reversibleReplication == null
                ? null
                : _reversibleReplication.CaptureObserverReplicationState(observerId, connectionToken);
        }

        public void RestoreObserverReplicationState(
            ulong observerId, ulong connectionToken, object state)
        {
            if (_reversibleReplication == null) return;
            _reversibleReplication.RestoreObserverReplicationState(observerId, connectionToken, state);
        }

        public object CaptureObserverDisconnectState(ulong observerId, ulong connectionToken)
        {
            return _reversibleDisconnect == null
                ? null
                : _reversibleDisconnect.CaptureObserverDisconnectState(observerId, connectionToken);
        }

        public void RestoreObserverDisconnectState(ulong observerId, ulong connectionToken, object state)
        {
            if (_reversibleDisconnect == null) return;
            _reversibleDisconnect.RestoreObserverDisconnectState(observerId, connectionToken, state);
        }
    }
}
