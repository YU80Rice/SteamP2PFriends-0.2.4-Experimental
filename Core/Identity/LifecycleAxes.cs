using System;

namespace SteamP2PFriends.Core.Identity
{
    /// <summary>
    /// 整个世界实例的生命周期身份。零值不是有效会话。
    /// </summary>
    public readonly struct SessionEpoch : IEquatable<SessionEpoch>
    {
        public SessionEpoch(ulong value)
        {
            if (value == 0UL) throw new ArgumentOutOfRangeException(nameof(value));
            Value = value;
        }

        public ulong Value { get; }
        public static SessionEpoch FromNative(ulong value) => new SessionEpoch(value == 0UL ? 1UL : value);
        public ulong ToNative() => Value;
        public bool Equals(SessionEpoch other) => Value == other.Value;
        public override bool Equals(object obj) => obj is SessionEpoch other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => Value.ToString();
        public static bool operator ==(SessionEpoch left, SessionEpoch right) => left.Equals(right);
        public static bool operator !=(SessionEpoch left, SessionEpoch right) => !left.Equals(right);
    }

    /// <summary>
    /// 同一观察者重连时递增的连接生命周期代次。零值表示尚未分配。
    /// </summary>
    public readonly struct ConnectionGeneration : IEquatable<ConnectionGeneration>
    {
        public ConnectionGeneration(uint value) { Value = value; }
        public uint Value { get; }
        public bool IsDefined => Value != 0U;
        public static ConnectionGeneration FromNative(uint value) => new ConnectionGeneration(value);
        public uint ToNative() => Value;
        public bool Equals(ConnectionGeneration other) => Value == other.Value;
        public override bool Equals(object obj) => obj is ConnectionGeneration other && Equals(other);
        public override int GetHashCode() => (int)Value;
        public override string ToString() => Value.ToString();
        public static bool operator ==(ConnectionGeneration left, ConnectionGeneration right) => left.Equals(right);
        public static bool operator !=(ConnectionGeneration left, ConnectionGeneration right) => !left.Equals(right);
    }

    /// <summary>
    /// 区域或导航 Bound 重建代次。零值表示尚未提交过重建。
    /// </summary>
    public readonly struct RegionGeneration : IEquatable<RegionGeneration>
    {
        public RegionGeneration(uint value) { Value = value; }
        public uint Value { get; }
        public bool IsDefined => Value != 0U;
        public static RegionGeneration FromNative(uint value) => new RegionGeneration(value);
        public uint ToNative() => Value;
        public bool Equals(RegionGeneration other) => Value == other.Value;
        public override bool Equals(object obj) => obj is RegionGeneration other && Equals(other);
        public override int GetHashCode() => (int)Value;
        public override string ToString() => Value.ToString();
        public static bool operator ==(RegionGeneration left, RegionGeneration right) => left.Equals(right);
        public static bool operator !=(RegionGeneration left, RegionGeneration right) => !left.Equals(right);
    }

    /// <summary>
    /// 单个实体或槽位复用的代次，与区域重建代次保持独立。
    /// </summary>
    public readonly struct EntityGeneration : IEquatable<EntityGeneration>
    {
        public EntityGeneration(uint value) { Value = value; }
        public uint Value { get; }
        public bool IsDefined => Value != 0U;
        public static EntityGeneration FromNative(uint value) => new EntityGeneration(value);
        public uint ToNative() => Value;
        public bool Equals(EntityGeneration other) => Value == other.Value;
        public override bool Equals(object obj) => obj is EntityGeneration other && Equals(other);
        public override int GetHashCode() => (int)Value;
        public override string ToString() => Value.ToString();
        public static bool operator ==(EntityGeneration left, EntityGeneration right) => left.Equals(right);
        public static bool operator !=(EntityGeneration left, EntityGeneration right) => !left.Equals(right);
    }
}
