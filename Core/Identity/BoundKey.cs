using System;

namespace SteamP2PFriends.Core.Identity
{
    /// <summary>
    /// 一维导航 Bound 身份。它与二维 RegionKey 有意保持不同类型，255 表示原生“无有效 Bound”。
    /// </summary>
    public readonly struct BoundKey : IEquatable<BoundKey>
    {
        public const byte NoneValue = byte.MaxValue;

        public BoundKey(byte value)
        {
            Value = value;
        }

        public byte Value { get; }
        public bool IsNone => Value == NoneValue;

        public static BoundKey None => new BoundKey(NoneValue);
        public static BoundKey FromNative(byte value) => new BoundKey(value);
        public byte ToNative() => Value;

        public bool Equals(BoundKey other) => Value == other.Value;
        public override bool Equals(object obj) => obj is BoundKey other && Equals(other);
        public override int GetHashCode() => Value;
        public override string ToString() => IsNone ? "<none>" : Value.ToString();

        public static bool operator ==(BoundKey left, BoundKey right) => left.Equals(right);
        public static bool operator !=(BoundKey left, BoundKey right) => !left.Equals(right);

    }
}
