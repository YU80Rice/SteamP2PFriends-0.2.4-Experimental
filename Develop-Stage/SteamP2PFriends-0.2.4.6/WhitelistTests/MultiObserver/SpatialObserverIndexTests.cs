using SteamP2PFriends.MultiObserver.Spatial;
using System;
using System.Collections.Generic;

namespace SteamP2PFriends.WhitelistTests
{
    internal static class SpatialObserverIndexTests
    {
        internal static bool Test_SPI01_Grid2DDiffCalculation()
        {
            var index = new SpatialObserverIndex();
            // Initial position (10, 10), radius 1 (3x3 = 9 regions)
            SpatialRelevanceDiff diff1 = index.UpdateGrid2D(1, 101, 10, 10, 1, 64);
            if (!diff1.HasChanges || diff1.EnteredKeys.Length != 9 || diff1.ExitedKeys.Length != 0)
                return false;

            // Move to (10, 11) -> 3 new entered, 3 exited, 6 retained
            SpatialRelevanceDiff diff2 = index.UpdateGrid2D(1, 101, 10, 11, 1, 64);
            if (!diff2.HasChanges || diff2.EnteredKeys.Length != 3 || diff2.ExitedKeys.Length != 3)
                return false;

            // Move to same (10, 11) -> no changes
            SpatialRelevanceDiff diff3 = index.UpdateGrid2D(1, 101, 10, 11, 1, 64);
            return !diff3.HasChanges && diff3.EnteredKeys.Length == 0 && diff3.ExitedKeys.Length == 0;
        }

        internal static bool Test_SPI02_Bound1DDiffCalculation()
        {
            var index = new SpatialObserverIndex();
            // Enter bound 7
            SpatialRelevanceDiff diff1 = index.UpdateBound1D(1, 101, 7);
            if (!diff1.HasChanges || diff1.EnteredKeys.Length != 1 || diff1.EnteredKeys[0] != 7 || diff1.ExitedKeys.Length != 0)
                return false;

            // Move to bound 10
            SpatialRelevanceDiff diff2 = index.UpdateBound1D(1, 101, 10);
            if (!diff2.HasChanges || diff2.EnteredKeys.Length != 1 || diff2.EnteredKeys[0] != 10
                || diff2.ExitedKeys.Length != 1 || diff2.ExitedKeys[0] != 7)
                return false;

            // Exit all bounds (byte.MaxValue = 255)
            SpatialRelevanceDiff diff3 = index.UpdateBound1D(1, 101, byte.MaxValue);
            return diff3.HasChanges && diff3.EnteredKeys.Length == 0 && diff3.ExitedKeys.Length == 1 && diff3.ExitedKeys[0] == 10;
        }

        internal static bool Test_SPI03_ObserverDisconnectReleasesAll()
        {
            var index = new SpatialObserverIndex();
            index.UpdateGrid2D(1, 101, 10, 10, 1, 64);
            SpatialRelevanceDiff diff = index.RemoveObserver(1);
            return diff.HasChanges && diff.EnteredKeys.Length == 0 && diff.ExitedKeys.Length == 9;
        }

        internal static bool Test_SPI04_ReconnectTokenInvalidatesOldState()
        {
            var index = new SpatialObserverIndex();
            index.UpdateGrid2D(1, 101, 10, 10, 1, 64);

            // Reconnect with new token 202 at (20, 20)
            SpatialRelevanceDiff diff = index.UpdateGrid2D(1, 202, 20, 20, 1, 64);
            // Because token changed, all 9 old regions at (10,10) are exited, 9 new regions at (20,20) entered
            return diff.HasChanges && diff.EnteredKeys.Length == 9 && diff.ExitedKeys.Length == 9;
        }
    }
}
