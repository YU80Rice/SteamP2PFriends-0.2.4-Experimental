using System;

namespace SteamP2PFriends.Core.Identity
{
    /// <summary>
    /// 二维世界区域身份。Packed 仅作为原生边界的集中编码形式，不能在模块间以裸整数传播。
    /// </summary>
    public readonly struct RegionKey : IEquatable<RegionKey>
    {
        public RegionKey(byte x, byte y)
        {
            X = x;
            Y = y;
        }

        public byte X { get; }
        public byte Y { get; }
        public int Packed => (X << 8) | Y;

        public static RegionKey FromPacked(int packed)
        {
            if (!TryFromPacked(packed, out RegionKey key))
                throw new ArgumentOutOfRangeException(nameof(packed), "Region Key 必须是 0..65535 的二维编码");
            return key;
        }

        public static bool TryFromPacked(int packed, out RegionKey key)
        {
            if (packed < 0 || packed > ushort.MaxValue)
            {
                key = default;
                return false;
            }

            key = new RegionKey((byte)(packed >> 8), (byte)(packed & byte.MaxValue));
            return true;
        }

        public bool Equals(RegionKey other) => X == other.X && Y == other.Y;
        public override bool Equals(object obj) => obj is RegionKey other && Equals(other);
        public override int GetHashCode() => Packed;
        public override string ToString() => $"({X},{Y})";

        public static bool operator ==(RegionKey left, RegionKey right) => left.Equals(right);
        public static bool operator !=(RegionKey left, RegionKey right) => !left.Equals(right);

    }
}
