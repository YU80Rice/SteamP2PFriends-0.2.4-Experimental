using System;
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

    public sealed class ItemStepFiveAdmission
    {
        private readonly Action<ItemStepFiveRequest> _registerDemand;
        private readonly Action<ItemStepFiveRequest, RegionKey> _registerObserver;
        private readonly Func<ItemStepFiveRequest, RegionKey, bool> _shouldHold;
        private readonly Func<ItemStepFiveRequest, RegionKey, bool> _shouldSuppressDelivered;
        private readonly Action<ItemStepFiveRequest> _prepareDemand;

        public ItemStepFiveAdmission(
            Action<ItemStepFiveRequest> registerDemand,
            Action<ItemStepFiveRequest, RegionKey> registerObserver,
            Func<ItemStepFiveRequest, RegionKey, bool> shouldHold,
            Func<ItemStepFiveRequest, RegionKey, bool> shouldSuppressDelivered,
            Action<ItemStepFiveRequest> prepareDemand = null)
        {
            _registerDemand = registerDemand ?? throw new ArgumentNullException(nameof(registerDemand));
            _registerObserver = registerObserver ?? throw new ArgumentNullException(nameof(registerObserver));
            _shouldHold = shouldHold ?? throw new ArgumentNullException(nameof(shouldHold));
            _shouldSuppressDelivered = shouldSuppressDelivered ?? throw new ArgumentNullException(nameof(shouldSuppressDelivered));
            _prepareDemand = prepareDemand ?? (_ => { });
        }

        public void Admit(ItemStepFiveRequest request, IItemStepFiveLoadedProjection loaded)
        {
            if (loaded == null) throw new ArgumentNullException(nameof(loaded));
            _prepareDemand(request);
            _registerDemand(request);
            int minX = Math.Max(0, request.Center.X - request.Radius);
            int maxX = Math.Min(request.WorldSize - 1, request.Center.X + request.Radius);
            int minY = Math.Max(0, request.Center.Y - request.Radius);
            int maxY = Math.Min(request.WorldSize - 1, request.Center.Y + request.Radius);
            for (int x = minX; x <= maxX; x++)
                for (int y = minY; y <= maxY; y++)
                {
                    RegionKey region = new RegionKey((byte)x, (byte)y);
                    _registerObserver(request, region);
                    if (_shouldHold(request, region) || _shouldSuppressDelivered(request, region))
                        loaded.ProjectLoaded(region);
                }
        }
    }
}
