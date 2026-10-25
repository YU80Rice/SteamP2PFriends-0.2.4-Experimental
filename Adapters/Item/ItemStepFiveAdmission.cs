using System;
using System.Collections.Generic;
using SteamP2PFriends.Core.Identity;

namespace SteamP2PFriends.Adapters.Item
{
    public readonly struct ItemStepFiveRequest
    {
        public ItemStepFiveRequest(
            ulong observerId,
            ulong connectionToken,
            RegionKey center,
            int radius,
            int worldSize,
            bool host)
        {
            ObserverId = observerId;
            ConnectionToken = connectionToken;
            Center = center;
            Radius = radius;
            WorldSize = worldSize;
            Host = host;
        }

        public ulong ObserverId { get; }
        public ulong ConnectionToken { get; }
        public RegionKey Center { get; }
        public int Radius { get; }
        public int WorldSize { get; }
        public bool Host { get; }
    }

    public interface IItemStepFiveLoadedProjection
    {
        void ProjectLoaded(RegionKey region);
    }

    public sealed class ItemStepFiveLoadedProjection : IItemStepFiveLoadedProjection
    {
        private readonly Action<RegionKey> _project;

        public ItemStepFiveLoadedProjection(Action<RegionKey> project)
        {
            _project = project ?? throw new ArgumentNullException(nameof(project));
        }

        public void ProjectLoaded(RegionKey region) => _project(region);
    }

    internal enum ItemStepFiveResult { RunOriginal, Waiting, Rejected, Accepted }

    internal readonly struct ItemBoundStepFiveRequest
    {
        internal ItemBoundStepFiveRequest(
            ItemStepFiveRequest value, SessionEpoch epoch, object playerKey)
        {
            Value = value;
            Epoch = epoch;
            PlayerKey = playerKey;
        }

        internal ItemStepFiveRequest Value { get; }
        internal SessionEpoch Epoch { get; }
        internal object PlayerKey { get; }
    }

    public sealed class ItemStepFiveAdmission
    {
        private readonly Action<ItemStepFiveRequest> _registerDemand;
        private readonly Action<ItemStepFiveRequest, RegionKey> _registerObserver;
        private readonly Func<ItemStepFiveRequest, RegionKey, bool> _shouldHold;
        private readonly Func<ItemStepFiveRequest, RegionKey, bool> _shouldSuppressDelivered;
        private readonly Action<ItemStepFiveRequest> _prepareDemand;
        private readonly Func<bool> _isCurrent;
        private readonly Func<ItemBoundStepFiveRequest, IItemStepFiveLoadedProjection, ItemStepFiveResult> _validateOwned;
        private readonly Func<ItemStepFiveRequest, ItemStepFiveResult> _registerOwnedDemand;

        public ItemStepFiveAdmission(
            Action<ItemStepFiveRequest> registerDemand,
            Action<ItemStepFiveRequest, RegionKey> registerObserver,
            Func<ItemStepFiveRequest, RegionKey, bool> shouldHold,
            Func<ItemStepFiveRequest, RegionKey, bool> shouldSuppressDelivered,
            Action<ItemStepFiveRequest> prepareDemand = null,
            Func<bool> isCurrent = null)
            : this(registerDemand, registerObserver, shouldHold, shouldSuppressDelivered,
                  prepareDemand, isCurrent, null, null)
        {
        }

        internal ItemStepFiveAdmission(
            Action<ItemStepFiveRequest> registerDemand,
            Action<ItemStepFiveRequest, RegionKey> registerObserver,
            Func<ItemStepFiveRequest, RegionKey, bool> shouldHold,
            Func<ItemStepFiveRequest, RegionKey, bool> shouldSuppressDelivered,
            Action<ItemStepFiveRequest> prepareDemand,
            Func<bool> isCurrent,
            Func<ItemBoundStepFiveRequest, IItemStepFiveLoadedProjection, ItemStepFiveResult> validateOwned,
            Func<ItemStepFiveRequest, ItemStepFiveResult> registerOwnedDemand)
        {
            _registerDemand = registerDemand ?? throw new ArgumentNullException(nameof(registerDemand));
            _registerObserver = registerObserver ?? throw new ArgumentNullException(nameof(registerObserver));
            _shouldHold = shouldHold ?? throw new ArgumentNullException(nameof(shouldHold));
            _shouldSuppressDelivered = shouldSuppressDelivered ?? throw new ArgumentNullException(nameof(shouldSuppressDelivered));
            _prepareDemand = prepareDemand ?? (_ => { });
            _isCurrent = isCurrent;
            _validateOwned = validateOwned;
            _registerOwnedDemand = registerOwnedDemand;
        }

        public void Admit(ItemStepFiveRequest request, IItemStepFiveLoadedProjection loaded)
        {
            if (loaded == null) throw new ArgumentNullException(nameof(loaded));
            if (_isCurrent != null && !_isCurrent()) return;
            Execute(request, loaded,
                value => { _registerDemand(value); return ItemStepFiveResult.Accepted; });
        }

        internal ItemStepFiveResult Admit(ItemBoundStepFiveRequest request, IItemStepFiveLoadedProjection loaded)
        {
            if (loaded == null) throw new ArgumentNullException(nameof(loaded));
            if (_validateOwned == null || _registerOwnedDemand == null)
                return ItemStepFiveResult.Rejected;
            var guard = _validateOwned(request, loaded);
            if (guard != ItemStepFiveResult.Accepted) return guard;
            return Execute(request.Value, loaded, _registerOwnedDemand);
        }

        private ItemStepFiveResult Execute(
            ItemStepFiveRequest value, IItemStepFiveLoadedProjection loaded,
            Func<ItemStepFiveRequest, ItemStepFiveResult> registerDemand)
        {
            _prepareDemand(value);
            ItemStepFiveResult admitted = registerDemand(value);
            if (admitted != ItemStepFiveResult.Accepted) return admitted;
            foreach (RegionKey region in EnumerateRequestedRegions(value))
            {
                _registerObserver(value, region);
                if (_shouldHold(value, region) || _shouldSuppressDelivered(value, region))
                    loaded.ProjectLoaded(region);
            }
            return ItemStepFiveResult.Accepted;
        }

        internal static IEnumerable<RegionKey> EnumerateRequestedRegions(ItemStepFiveRequest value)
        {
            int minX = Math.Max(0, value.Center.X - value.Radius);
            int maxX = Math.Min(value.WorldSize - 1, value.Center.X + value.Radius);
            int minY = Math.Max(0, value.Center.Y - value.Radius);
            int maxY = Math.Min(value.WorldSize - 1, value.Center.Y + value.Radius);
            for (int x = minX; x <= maxX; x++)
                for (int y = minY; y <= maxY; y++)
                    yield return new RegionKey((byte)x, (byte)y);
        }
    }
}
