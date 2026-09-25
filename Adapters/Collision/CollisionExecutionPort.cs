using SteamP2PFriends.Core.Identity;
using SteamP2PFriends.MultiObserver.Lifecycle;
using SteamP2PFriends.MultiObserver.SPI;
using System;
using System.Collections.Generic;

namespace SteamP2PFriends.Adapters.Collision
{
    public interface ICollisionOverrideStore
    {
        bool IsIdentityCertain { get; }
        IReadOnlyList<CollisionOverride> Acquire(CollisionExecutionIdentity identity);
        bool IsOwned(CollisionOverride item);
        /// <summary>
        /// 原子撤销：实现必须先验证列表中全部 Override 仍归插件持有，只有全量通过才允许任何撤销。
        /// </summary>
        bool TryRevokeOwnedAtomically(IReadOnlyList<CollisionOverride> overrides);
        object CaptureRegionState(RegionKey regionKey);
        void RestoreRegionState(RegionKey regionKey, object state);
    }

    public sealed class CollisionExecutionPort : IDomainExecutionPort
    {
        private readonly ICollisionOverrideStore _store;
        private readonly Func<RegionKey, uint> _generationReader;
        private readonly Action<string> _diagnosticSink;
        private readonly Dictionary<RegionKey, CollisionAcquisitionReceipt> _receipts =
            new Dictionary<RegionKey, CollisionAcquisitionReceipt>();
        private SessionEpoch _sessionEpoch;
        private ulong _nextAcquireGeneration = 1UL;

        public CollisionExecutionPort(
            ICollisionOverrideStore store,
            Func<RegionKey, uint> generationReader,
            LifecyclePolicy lifecyclePolicy,
            Action<string> diagnosticSink = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _generationReader = generationReader ?? throw new ArgumentNullException(nameof(generationReader));
            _diagnosticSink = diagnosticSink ?? (message => { });
            LifecyclePolicy = lifecyclePolicy ?? throw new ArgumentNullException(nameof(lifecyclePolicy));
        }

        public DomainId DomainId => DomainIds.Collision;
        public string DisplayName => "Collision";
        public LifecyclePolicy LifecyclePolicy { get; }
        public int ReceiptCount => _receipts.Count;
        public bool TryGetReceipt(RegionKey regionKey, out CollisionAcquisitionReceipt receipt) =>
            _receipts.TryGetValue(regionKey, out receipt) && receipt.Valid;

        public CollisionAcquisitionReceipt CurrentReceipt(RegionKey regionKey)
        {
            CollisionAcquisitionReceipt receipt;
            return _receipts.TryGetValue(regionKey, out receipt) ? receipt : default(CollisionAcquisitionReceipt);
        }

        public string LastReleaseReceiptDetail { get; private set; }

        public bool IsCurrentReceipt(CollisionAcquisitionReceipt receipt)
        {
            CollisionAcquisitionReceipt current;
            return receipt.Valid && _receipts.TryGetValue(receipt.RegionKey, out current)
                && current.Equals(receipt);
        }

        public void OnSessionBegin(uint sessionEpoch)
        {
            _sessionEpoch = SessionEpoch.FromNative(sessionEpoch);
            _receipts.Clear();
            _nextAcquireGeneration = 1UL;
        }

        public void OnSessionEnd()
        {
            _receipts.Clear();
            _sessionEpoch = default(SessionEpoch);
        }

        public void ResetReplication(uint sessionEpoch) { }

        public void OnAcquire(LeaseTicket ticket)
        {
            if (!TryAcquire(ticket, out CollisionAcquisitionReceipt receipt))
                throw new CollisionAcquireRejectedException("Collision Acquire identity rejected.");
        }

        public bool TryAcquire(LeaseTicket ticket, out CollisionAcquisitionReceipt receipt)
        {
            if (!CanCommit(ticket) || !_store.IsIdentityCertain)
            {
                receipt = default(CollisionAcquisitionReceipt);
                EmitAcquireDiagnostic(ticket, 0UL, "rejected", "acquire-identity-rejected");
                return false;
            }
            ulong acquireGeneration = _nextAcquireGeneration++;
            if (acquireGeneration == 0UL) acquireGeneration = _nextAcquireGeneration++;
            var identity = new CollisionExecutionIdentity(ticket, acquireGeneration);
            IReadOnlyList<CollisionOverride> overrides = _store.Acquire(identity);
            receipt = new CollisionAcquisitionReceipt(
                DomainIds.Collision, ticket.RegionKey, ticket.SessionEpoch,
                ticket.RegionGeneration, acquireGeneration, overrides);
            if (!receipt.Valid)
            {
                EmitAcquireDiagnostic(ticket, acquireGeneration, "rejected", "receipt-invalid");
                return false;
            }
            _receipts[ticket.RegionKey] = receipt;
            EmitAcquireDiagnostic(ticket, acquireGeneration, "success", "collision-overrides-acquired");
            return true;
        }

        public void OnRelease(LeaseTicket ticket)
        {
            if (!TryRelease(ticket))
                throw new CollisionReleaseRejectedException("Collision Release identity or receipt rejected.");
        }

        public bool TryRelease(LeaseTicket ticket)
        {
            CollisionAcquisitionReceipt receipt = default(CollisionAcquisitionReceipt);
            if (!CanCommit(ticket) || ticket.ActiveDemandCount != 0 || !_store.IsIdentityCertain
                || !_receipts.TryGetValue(ticket.RegionKey, out receipt)
                || !receipt.Valid
                || receipt.SessionEpoch != ticket.SessionEpoch
                || receipt.RegionGeneration != ticket.RegionGeneration)
            {
                LastReleaseReceiptDetail = DescribeReleaseRejection(ticket, receipt);
                _diagnosticSink(LastReleaseReceiptDetail);
                return false;
            }

            if (!_store.TryRevokeOwnedAtomically(receipt.Overrides))
            {
                LastReleaseReceiptDetail = "receiptAcquireGeneration=" + receipt.AcquireGeneration
                    + " ownership-unproven";
                _diagnosticSink(LastReleaseReceiptDetail);
                return false;
            }
            _receipts.Remove(ticket.RegionKey);
            LastReleaseReceiptDetail = "receiptAcquireGeneration=" + receipt.AcquireGeneration;
            _diagnosticSink(LastReleaseReceiptDetail);
            return true;
        }

        public bool TryRelease(LeaseTicket ticket, CollisionAcquisitionReceipt expectedReceipt)
        {
            CollisionAcquisitionReceipt current = default(CollisionAcquisitionReceipt);
            bool currentMatches = _receipts.TryGetValue(ticket.RegionKey, out current)
                && current.Equals(expectedReceipt);
            if (!IsCurrentReceipt(expectedReceipt) || !currentMatches)
            {
                LastReleaseReceiptDetail = "receiptAcquireGeneration=" + expectedReceipt.AcquireGeneration
                    + " current=" + current.AcquireGeneration;
                _diagnosticSink(LastReleaseReceiptDetail);
                return false;
            }
            return TryRelease(ticket);
        }

        public string LastAcquireDetail { get; private set; }

        private void EmitAcquireDiagnostic(
            LeaseTicket ticket, ulong acquireGeneration, string outcome, string reason)
        {
            LastAcquireDetail = "domain=" + DomainIds.Collision
                + " region=" + ticket.RegionKey
                + " sessionEpoch=" + ticket.SessionEpoch.Value
                + " regionGeneration=" + ticket.RegionGeneration.Value
                + " acquireGeneration=" + acquireGeneration
                + " outcome=" + outcome + " reason=" + reason;
            _diagnosticSink(LastAcquireDetail);
        }

        private string DescribeReleaseRejection(
            LeaseTicket ticket, CollisionAcquisitionReceipt receipt)
        {
            ulong receiptGeneration = receipt.Valid ? receipt.AcquireGeneration : 0UL;
            return "receiptAcquireGeneration=" + receiptGeneration
                + " releaseEpoch=" + ticket.SessionEpoch.Value
                + " releaseRegionGeneration=" + ticket.RegionGeneration.Value
                + " reason=release-identity-rejected";
        }

        private bool CanCommit(LeaseTicket ticket)
        {
            return ticket.Valid && ticket.DomainId == DomainIds.Collision
                && _sessionEpoch.Value != 0UL
                && ticket.SessionEpoch == _sessionEpoch
                && ticket.RegionGeneration.IsDefined
                && _generationReader(ticket.RegionKey) == ticket.RegionGeneration.Value;
        }

        public void OnLifecycleTick(float deltaTime) { }
        public void OnReplicationTick(float deltaTime) { }
        public void OnObserverReplicationEntered(ulong observerId, ulong connectionToken, RegionKey regionKey) { }
        public void OnObserverReplicationExited(ulong observerId, ulong connectionToken, RegionKey regionKey) { }
        public void OnObserverDisconnected(ulong observerId, ulong connectionToken) { }

        public RegionGeneration ReadRegionGeneration(RegionKey regionKey) =>
            RegionGeneration.FromNative(_generationReader(regionKey));

        public EDomainFailureKind ClassifyRegionFailure(Exception exception) =>
            EDomainFailureKind.Failed;

        public EDomainFailureKind ClassifyReleaseFailure(Exception exception) =>
            exception is CollisionReleaseRejectedException
                ? EDomainFailureKind.Rejected : EDomainFailureKind.Failed;

        public object CaptureRegionState(RegionKey regionKey) => _store.CaptureRegionState(regionKey);
        public void RestoreRegionState(RegionKey regionKey, object state) =>
            _store.RestoreRegionState(regionKey, state);
        public object CaptureObserverReplicationState(ulong observerId, ulong connectionToken) => null;
        public void RestoreObserverReplicationState(ulong observerId, ulong connectionToken, object state) { }
        public object CaptureObserverDisconnectState(ulong observerId, ulong connectionToken) => null;
        public void RestoreObserverDisconnectState(ulong observerId, ulong connectionToken, object state) { }
    }

    public sealed class CollisionAcquireRejectedException : Exception
    {
        public CollisionAcquireRejectedException(string message) : base(message) { }
    }

    public sealed class CollisionReleaseRejectedException : Exception
    {
        public CollisionReleaseRejectedException(string message) : base(message) { }
    }
}
