using SteamP2PFriends.Core.Identity;
using SteamP2PFriends.MultiObserver.Lifecycle;
using SteamP2PFriends.MultiObserver.SPI;
using System;
using System.Collections.Generic;

namespace SteamP2PFriends.Adapters.Item
{
    public sealed class ItemRegionNotPreparedException : InvalidOperationException
    {
        public ItemRegionNotPreparedException(string message) : base(message) { }
    }

    public sealed class ItemAcquireRejectedException : Exception
    {
        public ItemAcquireRejectedException(string message) : base(message) { }
    }

    public sealed class ItemAcquireFailedException : Exception
    {
        public ItemAcquireFailedException(string message) : base(message) { }
    }

    public sealed class ItemObserverReplicationFailureException : InvalidOperationException
    {
        public ItemObserverReplicationFailureException(string message, bool safeToRetry, Exception inner = null)
            : base(message, inner) { SafeToRetry = safeToRetry; }
        public bool SafeToRetry { get; }
    }

    public sealed class ItemReleaseRejectedException : Exception
    {
        public ItemReleaseRejectedException(string message) : base(message) { }
    }

    public sealed class ItemExecutionPort : IDomainExecutionPort,
        ILocalObserverReplicationFailurePort, IAcquireRecoveryExhaustionPort
    {
        private readonly ItemPreparationAuthority _preparationAuthority;
        private readonly ItemMetadataStore _metadataStore;
        private readonly Func<RegionKey, uint> _generationReader;
        private readonly Func<float> _timeReader;
        private readonly Action<string> _diagnosticSink;
        private readonly Func<ulong, bool> _isHostObserver;
        private readonly Action<RegionKey> _hostPresenter;
        private readonly Action<ulong, ulong, RegionKey> _guestBaselineBinder;
        private readonly Action<RegionKey> _hostPresentationCleaner;
        private readonly Action<ulong, ulong, RegionKey> _guestBaselineUnbinder;
        private readonly Func<bool> _isCandidateActive;

        private int _hostPresentationCount;
        private int _guestBaselineBindingCount;

        private readonly Dictionary<RegionKey, ItemAcquisitionReceipt> _receipts =
            new Dictionary<RegionKey, ItemAcquisitionReceipt>();
        private readonly Dictionary<RegionKey, float> _activationTimes =
            new Dictionary<RegionKey, float>();
        private SessionEpoch _sessionEpoch;
        private ulong _nextAcquireGeneration = 1UL;

        public ItemExecutionPort(
            ItemPreparationAuthority preparationAuthority,
            ItemMetadataStore metadataStore,
            Func<RegionKey, uint> generationReader,
            LifecyclePolicy lifecyclePolicy,
            Func<float> timeReader = null,
            Action<string> diagnosticSink = null,
            Func<ulong, bool> isHostObserver = null,
            Action<RegionKey> hostPresenter = null,
            Action<ulong, ulong, RegionKey> guestBaselineBinder = null,
            Action<RegionKey> hostPresentationCleaner = null,
            Action<ulong, ulong, RegionKey> guestBaselineUnbinder = null,
            Func<bool> isCandidateActive = null)
        {
            _preparationAuthority = preparationAuthority ?? throw new ArgumentNullException(nameof(preparationAuthority));
            _metadataStore = metadataStore ?? throw new ArgumentNullException(nameof(metadataStore));
            _generationReader = generationReader ?? throw new ArgumentNullException(nameof(generationReader));
            LifecyclePolicy = lifecyclePolicy ?? throw new ArgumentNullException(nameof(lifecyclePolicy));
            _timeReader = timeReader ?? SafeDefaultTimeReader;
            _diagnosticSink = diagnosticSink ?? (_ => { });
            _isHostObserver = isHostObserver;
            _hostPresenter = hostPresenter;
            _guestBaselineBinder = guestBaselineBinder;
            _hostPresentationCleaner = hostPresentationCleaner;
            _guestBaselineUnbinder = guestBaselineUnbinder;
            _isCandidateActive = isCandidateActive;
        }

        private static readonly System.Diagnostics.Stopwatch _uptime =
            System.Diagnostics.Stopwatch.StartNew();

        private static float SafeDefaultTimeReader() =>
            (float)_uptime.Elapsed.TotalSeconds;

        public DomainId DomainId => DomainIds.Item;
        public string DisplayName => "Item";
        public LifecyclePolicy LifecyclePolicy { get; }
        public ItemPreparationAuthority PreparationAuthority => _preparationAuthority;
        public ItemMetadataStore MetadataStore => _metadataStore;

        public int ReceiptCount => _receipts.Count;
        public int ActivatedRegionCount => _activationTimes.Count;
        public float CurrentTime => _timeReader();

        public bool IsRegionActivated(RegionKey regionKey) =>
            _activationTimes.ContainsKey(regionKey);

        public float GetActivationTime(RegionKey regionKey) =>
            _activationTimes.TryGetValue(regionKey, out float time) ? time : -1f;

        public string LastAcquireDetail { get; private set; }
        public string LastReleaseReceiptDetail { get; private set; }
        public string LastReplicationDetail { get; private set; }
        public bool IsConfiguredHost(ulong observerId) =>
            _isHostObserver != null && _isHostObserver(observerId);

        public int HostPresentationCount => _hostPresentationCount;
        public int GuestBaselineBindingCount => _guestBaselineBindingCount;

        public ItemAcquisitionReceipt CurrentReceipt(RegionKey regionKey) =>
            _receipts.TryGetValue(regionKey, out ItemAcquisitionReceipt receipt) ? receipt : default(ItemAcquisitionReceipt);

        public bool IsCurrentReceipt(ItemAcquisitionReceipt receipt) =>
            receipt.Valid && _receipts.TryGetValue(receipt.RegionKey, out ItemAcquisitionReceipt current)
            && current.Equals(receipt);

        public bool TryGetReceipt(RegionKey regionKey, out ItemAcquisitionReceipt receipt) =>
            _receipts.TryGetValue(regionKey, out receipt) && receipt.Valid;

        public void OnSessionBegin(uint sessionEpoch)
        {
            var newEpoch = SessionEpoch.FromNative(sessionEpoch);
            if (_sessionEpoch.Value != 0UL && !_sessionEpoch.Equals(newEpoch))
            {
                _metadataStore.Clear();
            }
            _sessionEpoch = newEpoch;
            _receipts.Clear();
            _activationTimes.Clear();
            _nextAcquireGeneration = 1UL;
        }

        public void OnSessionEnd()
        {
            _receipts.Clear();
            _activationTimes.Clear();
            _metadataStore.Clear();
            _sessionEpoch = default(SessionEpoch);
        }

        private bool CanCommit(LeaseTicket ticket)
        {
            return ticket.Valid
                && ticket.DomainId == DomainIds.Item
                && _sessionEpoch.Value != 0UL
                && ticket.SessionEpoch == _sessionEpoch
                && ticket.RegionGeneration.IsDefined
                && _generationReader(ticket.RegionKey) == ticket.RegionGeneration.Value;
        }

        public void ResetReplication(uint sessionEpoch) { }

        public void OnAcquire(LeaseTicket ticket)
        {
            if (!CanCommit(ticket))
            {
                LastAcquireDetail = $"domain={DomainIds.Item} region={ticket.RegionKey} sessionEpoch={ticket.SessionEpoch.Value} outcome=rejected reason=identity-rejected";
                _diagnosticSink(LastAcquireDetail);
                throw new ItemAcquireRejectedException("Item Acquire identity rejected.");
            }

            if (!_preparationAuthority.IsRegionPrepared(ticket.RegionKey))
            {
                LastAcquireDetail = $"domain={DomainIds.Item} region={ticket.RegionKey} sessionEpoch={ticket.SessionEpoch.Value} outcome=deferred reason=region-not-prepared";
                _diagnosticSink(LastAcquireDetail);
                throw new ItemRegionNotPreparedException(
                    $"Region ({ticket.RegionKey.X}, {ticket.RegionKey.Y}) is not prepared.");
            }

            if (!TryAcquire(ticket, out _))
            {
                throw new ItemAcquireFailedException("Item Acquire failed.");
            }
        }

        public bool TryAcquire(LeaseTicket ticket, out ItemAcquisitionReceipt receipt)
        {
            if (!CanCommit(ticket) || !_preparationAuthority.IsRegionPrepared(ticket.RegionKey))
            {
                receipt = default(ItemAcquisitionReceipt);
                return false;
            }

            float now = _timeReader();
            if (!_activationTimes.ContainsKey(ticket.RegionKey))
            {
                bool activated = false;
                try
                {
                    activated = _preparationAuthority.Bridge.ActivateRegionTiming(
                        (byte)ticket.RegionKey.X, (byte)ticket.RegionKey.Y, now,
                        id => _metadataStore.TryGetMetadata(id, out var rec) && rec.Origin == ItemOriginKind.Natural);
                }
                catch (Exception ex)
                {
                    LastAcquireDetail = $"domain={DomainIds.Item} region={ticket.RegionKey} sessionEpoch={ticket.SessionEpoch.Value} outcome=failed reason=activation-timing-exception:{ex.GetType().Name}";
                    _diagnosticSink(LastAcquireDetail);
                    receipt = default(ItemAcquisitionReceipt);
                    return false;
                }

                if (!activated)
                {
                    LastAcquireDetail = $"domain={DomainIds.Item} region={ticket.RegionKey} sessionEpoch={ticket.SessionEpoch.Value} outcome=failed reason=activation-timing-failed";
                    _diagnosticSink(LastAcquireDetail);
                    receipt = default(ItemAcquisitionReceipt);
                    return false;
                }

                _activationTimes[ticket.RegionKey] = now;
                _metadataStore.ActivateRegionNaturalTiming(ticket.RegionKey, now);
            }

            ulong gen = _nextAcquireGeneration++;
            RegionGeneration actualGen = ReadRegionGeneration(ticket.RegionKey);
            receipt = new ItemAcquisitionReceipt(
                DomainIds.Item, ticket.RegionKey, ticket.SessionEpoch,
                actualGen, gen, now);
            _receipts[ticket.RegionKey] = receipt;
            LastAcquireDetail = $"domain={DomainIds.Item} region={ticket.RegionKey} sessionEpoch={ticket.SessionEpoch.Value} acquireGeneration={gen} outcome=success";
            _diagnosticSink(LastAcquireDetail);
            return true;
        }

        public void OnRelease(LeaseTicket ticket)
        {
            if (!TryRelease(ticket))
            {
                throw new ItemReleaseRejectedException("Item Release identity or receipt rejected.");
            }
        }

        public bool TryRelease(LeaseTicket ticket)
        {
            bool hasReceipt = _receipts.TryGetValue(ticket.RegionKey, out ItemAcquisitionReceipt receipt) && receipt.Valid;
            if (!CanCommit(ticket) || ticket.ActiveDemandCount != 0 || !hasReceipt
                || !receipt.SessionEpoch.Equals(ticket.SessionEpoch)
                || !receipt.RegionGeneration.Equals(ticket.RegionGeneration))
            {
                LastReleaseReceiptDetail = $"domain={DomainIds.Item} region={ticket.RegionKey} outcome=rejected reason=release-identity-rejected";
                _diagnosticSink(LastReleaseReceiptDetail);
                return false;
            }

            _receipts.Remove(ticket.RegionKey);
            LastReleaseReceiptDetail = $"domain={DomainIds.Item} region={ticket.RegionKey} outcome=success";
            _diagnosticSink(LastReleaseReceiptDetail);
            return true;
        }

        public bool TryRelease(LeaseTicket ticket, ItemAcquisitionReceipt expectedReceipt)
        {
            if (!IsCurrentReceipt(expectedReceipt))
            {
                LastReleaseReceiptDetail = $"domain={DomainIds.Item} region={ticket.RegionKey} outcome=rejected reason=stale-receipt";
                _diagnosticSink(LastReleaseReceiptDetail);
                return false;
            }
            return TryRelease(ticket);
        }

        public void OnLifecycleTick(float deltaTime) { }
        public void OnReplicationTick(float deltaTime) { }
        private ItemReturnSettlement _returnSettlement;

        public void BindReturnSettlement(ItemReturnSettlement settlement) =>
            _returnSettlement = settlement;

        public Action<ulong, ulong, RegionKey> AcquireRecoveryExhausted { get; set; }

        public void DeliverSettlementBaseline(ulong observerId, ulong connectionToken, RegionKey regionKey, bool host)
        {
            if (host)
            {
                _hostPresenter?.Invoke(regionKey);
                _hostPresentationCount++;
                return;
            }
            _guestBaselineBinder?.Invoke(observerId, connectionToken, regionKey);
            _guestBaselineBindingCount++;
        }

        public void OnObserverReplicationEntered(ulong observerId, ulong connectionToken, RegionKey regionKey)
        {
            if (!_preparationAuthority.IsRegionPrepared(regionKey))
            {
                LastReplicationDetail = $"domain={DomainIds.Item} observer={observerId} region={regionKey} outcome=rejected reason=region-not-prepared";
                _diagnosticSink(LastReplicationDetail);
                throw new ItemRegionNotPreparedException($"Region ({regionKey.X}, {regionKey.Y}) is not prepared.");
            }

            if (_isCandidateActive != null && !_isCandidateActive())
            {
                LastReplicationDetail = $"domain={DomainIds.Item} observer={observerId} region={regionKey} outcome=suppressed reason=legacy-writer-active";
                _diagnosticSink(LastReplicationDetail);
                return;
            }

            bool host = _isHostObserver != null && _isHostObserver(observerId);
            if (_returnSettlement != null && _returnSettlement.Hold(regionKey, observerId, connectionToken, host))
            {
                LastReplicationDetail = $"domain={DomainIds.Item} observer={observerId} region={regionKey} outcome=waiting-settlement";
                _diagnosticSink(LastReplicationDetail);
                return;
            }

            if (host)
            {
                _hostPresenter?.Invoke(regionKey);
                _hostPresentationCount++;
                LastReplicationDetail = $"domain={DomainIds.Item} observer={observerId} region={regionKey} role=host outcome=presented";
            }
            else
            {
                _guestBaselineBinder?.Invoke(observerId, connectionToken, regionKey);
                _guestBaselineBindingCount++;
                LastReplicationDetail = $"domain={DomainIds.Item} observer={observerId} region={regionKey} role=guest outcome=bound";
            }
            _diagnosticSink(LastReplicationDetail);
        }

        public void OnObserverReplicationExited(ulong observerId, ulong connectionToken, RegionKey regionKey)
        {
            _returnSettlement?.ReleaseWaiter(regionKey, observerId, connectionToken);
            if (_isHostObserver != null && _isHostObserver(observerId))
            {
                _hostPresentationCleaner?.Invoke(regionKey);
            }
            else
            {
                _guestBaselineUnbinder?.Invoke(observerId, connectionToken, regionKey);
            }
        }

        public void OnObserverDisconnected(ulong observerId, ulong connectionToken) =>
            _returnSettlement?.ReleaseObserver(observerId, connectionToken);

        public RegionGeneration ReadRegionGeneration(RegionKey regionKey) =>
            RegionGeneration.FromNative(_generationReader(regionKey));

        public EDomainFailureKind ClassifyRegionFailure(Exception exception)
        {
            if (exception is ItemRegionNotPreparedException)
                return EDomainFailureKind.Deferred;
            if (exception is ItemAcquireRejectedException)
                return EDomainFailureKind.Rejected;
            return EDomainFailureKind.Failed;
        }

        public EDomainFailureKind ClassifyReleaseFailure(Exception exception) =>
            exception is ItemReleaseRejectedException
                ? EDomainFailureKind.Rejected
                : EDomainFailureKind.Failed;

        public EObserverReplicationFailureDisposition HandleObserverReplicationFailure(
            ulong observerId, ulong connectionToken, RegionKey regionKey, Exception exception)
        {
            if (exception is ItemObserverReplicationFailureException failure && failure.SafeToRetry)
                return EObserverReplicationFailureDisposition.Retry;
            _returnSettlement?.QuarantineHalfPacket(observerId, connectionToken, regionKey);
            return EObserverReplicationFailureDisposition.Quarantine;
        }

        public void OnAcquireRecoveryExhausted(
            RegionKey region, ulong observerId, ulong connectionToken) =>
            AcquireRecoveryExhausted?.Invoke(observerId, connectionToken, region);

        public object CaptureRegionState(RegionKey regionKey) => null;
        public void RestoreRegionState(RegionKey regionKey, object state) { }
        public object CaptureObserverReplicationState(ulong observerId, ulong connectionToken) => null;
        public void RestoreObserverReplicationState(ulong observerId, ulong connectionToken, object state) { }
        public object CaptureObserverDisconnectState(ulong observerId, ulong connectionToken) => null;
        public void RestoreObserverDisconnectState(ulong observerId, ulong connectionToken, object state) { }
    }
}
