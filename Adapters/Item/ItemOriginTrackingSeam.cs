using System;

namespace SteamP2PFriends.Adapters.Item
{
    public interface IItemOriginTracker
    {
        void TrackNaturalItem(uint instanceId);
        void TrackDroppedItem(uint instanceId);
        void TrackUnknownItem(uint instanceId);
        bool IsTrackedDropped(uint instanceId);
        bool IsTrackedNatural(uint instanceId);
    }

    public static class ItemOriginTrackingSeam
    {
        private static IItemOriginTracker _activeTracker;

        public static IItemOriginTracker ActiveTracker => _activeTracker;

        public static void SetActiveTracker(IItemOriginTracker tracker)
        {
            _activeTracker = tracker;
        }

        public static void NotifyItemDropped(uint instanceId)
        {
            if (instanceId != 0U)
            {
                _activeTracker?.TrackDroppedItem(instanceId);
            }
        }

        public static void NotifyNaturalItem(uint instanceId)
        {
            if (instanceId != 0U)
            {
                _activeTracker?.TrackNaturalItem(instanceId);
            }
        }

        public static bool IsTrackedDropped(uint instanceId) =>
            _activeTracker != null && _activeTracker.IsTrackedDropped(instanceId);

        public static bool IsTrackedNatural(uint instanceId) =>
            _activeTracker != null && _activeTracker.IsTrackedNatural(instanceId);
    }
}
