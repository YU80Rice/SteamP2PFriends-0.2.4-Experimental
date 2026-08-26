using System;

namespace SteamP2PFriends.Core.Identity
{
    /// <summary>
    /// 防御工事的复合身份：二维世界区域加原生 plant 分区。
    /// 它不是 RegionKey 的别名，避免把同一区域内不同 plant 的状态混在一起。
    /// </summary>
    public readonly struct BarricadeKey : IEquatable<BarricadeKey>
    {
        public BarricadeKey(RegionKey region, ushort plant)
        {
            Region = region;
            Plant = plant;
        }

        public RegionKey Region { get; }
        public ushort Plant { get; }
        public int Packed => (Plant << 16) | Region.Packed;

        public static BarricadeKey FromNative(byte x, byte y, ushort plant) =>
            new BarricadeKey(new RegionKey(x, y), plant);

        public static BarricadeKey FromRegion(RegionKey region, ushort plant) =>
            new BarricadeKey(region, plant);

        public bool Equals(BarricadeKey other) => Region == other.Region && Plant == other.Plant;
        public override bool Equals(object obj) => obj is BarricadeKey other && Equals(other);
        public override int GetHashCode() => (Region.GetHashCode() * 397) ^ Plant;
        public override string ToString() => $"{Region}/plant:{Plant}";

        public static bool operator ==(BarricadeKey left, BarricadeKey right) => left.Equals(right);
        public static bool operator !=(BarricadeKey left, BarricadeKey right) => !left.Equals(right);
    }
}
