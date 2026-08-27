using SteamP2PFriends.Core.Identity;
using System;

namespace SteamP2PFriends.WhitelistTests
{
    /// <summary>
    /// Ticket 03 身份接缝的纯内存契约测试。
    /// </summary>
    internal static class IdentityContractTests
    {
        internal static bool Test_ID01_RegionKeyRoundTripsPackedCoordinates()
        {
            RegionKey key = new RegionKey(12, 34);
            return key.Packed == ((12 << 8) | 34)
                && RegionKey.TryFromPacked(key.Packed, out RegionKey decoded)
                && decoded == key;
        }

        internal static bool Test_ID02_RegionKeyRejectsOutOfRangePackedValues()
        {
            return !RegionKey.TryFromPacked(-1, out _)
                && !RegionKey.TryFromPacked(ushort.MaxValue + 1, out _);
        }

        internal static bool Test_ID03_BoundKeyHasIndependentSentinel()
        {
            BoundKey bound = BoundKey.FromNative(7);
            return bound != BoundKey.None
                && bound.ToNative() == 7
                && BoundKey.None.IsNone
                && BoundKey.None.ToNative() == byte.MaxValue;
        }

        internal static bool Test_ID04_DomainIdIsSeparateFromDisplayName()
        {
            DomainId id = DomainIds.Resource;
            string displayName = "资源";
            return id.IsDefined
                && id.Value == "Resource"
                && !string.Equals(id.Value, displayName, StringComparison.Ordinal)
                && id == DomainIds.Resource;
        }

        internal static bool Test_ID05_BarricadeKeyKeepsPlantSeparateFromRegion()
        {
            BarricadeKey first = BarricadeKey.FromNative(4, 5, 1);
            BarricadeKey second = BarricadeKey.FromNative(4, 5, 2);
            return first.Region == new RegionKey(4, 5)
                && first.Plant == 1
                && first != second
                && first.Packed != second.Packed;
        }

        internal static bool Test_All()
        {
            return Test_ID01_RegionKeyRoundTripsPackedCoordinates()
                && Test_ID02_RegionKeyRejectsOutOfRangePackedValues()
                && Test_ID03_BoundKeyHasIndependentSentinel()
                && Test_ID04_DomainIdIsSeparateFromDisplayName()
                && Test_ID05_BarricadeKeyKeepsPlantSeparateFromRegion();
        }
    }
}
