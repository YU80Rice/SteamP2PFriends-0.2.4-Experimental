using SteamP2PFriends.MultiObserver.Spatial;
using SteamP2PFriends.Core.Identity;
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
            if (!diff1.HasChanges || diff1.EnteredRegions.Length != 9 || diff1.ExitedRegions.Length != 0)
                return false;

            // Move to (10, 11) -> 3 new entered, 3 exited, 6 retained
            SpatialRelevanceDiff diff2 = index.UpdateGrid2D(1, 101, 10, 11, 1, 64);
            if (!diff2.HasChanges || diff2.EnteredRegions.Length != 3 || diff2.ExitedRegions.Length != 3)
                return false;

            // Move to same (10, 11) -> no changes
            SpatialRelevanceDiff diff3 = index.UpdateGrid2D(1, 101, 10, 11, 1, 64);
            return !diff3.HasChanges && diff3.EnteredRegions.Length == 0 && diff3.ExitedRegions.Length == 0;
        }

        internal static bool Test_SPI02_Bound1DDiffCalculation()
        {
            var index = new SpatialObserverIndex();
            // Enter bound 7
            SpatialRelevanceDiff diff1 = index.UpdateBound1D(1, 101, 7);
            if (!diff1.HasChanges || diff1.EnteredBounds.Length != 1 || diff1.EnteredBounds[0] != BoundKey.FromNative(7) || diff1.ExitedBounds.Length != 0)
                return false;

            // Move to bound 10
            SpatialRelevanceDiff diff2 = index.UpdateBound1D(1, 101, 10);
            if (!diff2.HasChanges || diff2.EnteredBounds.Length != 1 || diff2.EnteredBounds[0] != BoundKey.FromNative(10)
                || diff2.ExitedBounds.Length != 1 || diff2.ExitedBounds[0] != BoundKey.FromNative(7))
                return false;

            // Exit all bounds (byte.MaxValue = 255)
            SpatialRelevanceDiff diff3 = index.UpdateBound1D(1, 101, byte.MaxValue);
            return diff3.HasChanges && diff3.EnteredBounds.Length == 0 && diff3.ExitedBounds.Length == 1 && diff3.ExitedBounds[0] == BoundKey.FromNative(10);
        }

        internal static bool Test_SPI03_ObserverDisconnectReleasesAll()
        {
            var index = new SpatialObserverIndex();
            index.UpdateGrid2D(1, 101, 10, 10, 1, 64);
            SpatialRelevanceDiff diff = index.RemoveObserver(1);
            return diff.HasChanges && diff.EnteredRegions.Length == 0 && diff.ExitedRegions.Length == 9;
        }

        internal static bool Test_SPI05_Grid2DProjectionIsChebyshevAndWorldClipped()
        {
            // 票 01 表征门：二维投影形状 = 切比雪夫方形（|dx| <= r 且 |dy| <= r），
            // 并按世界边界裁剪。本方法只锁形状与裁剪语义，不锁具体领域半径值。
            HashSet<RegionKey> interior = SpatialObserverIndex.CalculateGrid2D(30, 30, 3, 64);
            bool square = interior.Count == 49
                && interior.Contains(new RegionKey(27, 27))
                && interior.Contains(new RegionKey(33, 33))
                && !interior.Contains(new RegionKey(34, 30))
                && !interior.Contains(new RegionKey(30, 34))
                && !interior.Contains(new RegionKey(26, 30));

            HashSet<RegionKey> single = SpatialObserverIndex.CalculateGrid2D(10, 10, 0, 64);
            bool radiusZero = single.Count == 1 && single.Contains(new RegionKey(10, 10));

            HashSet<RegionKey> minCorner = SpatialObserverIndex.CalculateGrid2D(0, 0, 3, 64);
            bool clippedLow = minCorner.Count == 16
                && minCorner.Contains(new RegionKey(0, 0))
                && minCorner.Contains(new RegionKey(3, 3));

            HashSet<RegionKey> maxCorner = SpatialObserverIndex.CalculateGrid2D(63, 63, 3, 64);
            bool clippedHigh = maxCorner.Count == 16
                && maxCorner.Contains(new RegionKey(60, 60))
                && maxCorner.Contains(new RegionKey(63, 63));

            HashSet<RegionKey> edge = SpatialObserverIndex.CalculateGrid2D(0, 30, 3, 64);
            bool clippedEdge = edge.Count == 28
                && edge.Contains(new RegionKey(0, 30))
                && edge.Contains(new RegionKey(3, 27));

            return square && radiusZero && clippedLow && clippedHigh && clippedEdge;
        }

        internal static bool Test_SPI04_ReconnectTokenInvalidatesOldState()
        {
            var index = new SpatialObserverIndex();
            index.UpdateGrid2D(1, 101, 10, 10, 1, 64);

            // Reconnect with new token 202 at (20, 20)
            SpatialRelevanceDiff diff = index.UpdateGrid2D(1, 202, 20, 20, 1, 64);
            // Because token changed, all 9 old regions at (10,10) are exited, 9 new regions at (20,20) entered
            return diff.HasChanges && diff.EnteredRegions.Length == 9 && diff.ExitedRegions.Length == 9;
        }
    }
}
