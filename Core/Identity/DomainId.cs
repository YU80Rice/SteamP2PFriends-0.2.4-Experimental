using System;

namespace SteamP2PFriends.Core.Identity
{
    /// <summary>
    /// 跨模块使用的不可变领域机器身份。
    /// 显示名称不属于此类型，也不能替代此身份参与注册、协议或持久化判断。
    /// </summary>
    public readonly struct DomainId : IEquatable<DomainId>
    {
        private readonly string _value;

        internal DomainId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Domain Id 不能为空", nameof(value));
            if (!string.Equals(value, value.Trim(), StringComparison.Ordinal))
                throw new ArgumentException("Domain Id 不得包含首尾空白", nameof(value));

            _value = value;
        }

        public bool IsDefined => !string.IsNullOrEmpty(_value);
        public string Value => _value ?? string.Empty;

        public bool Equals(DomainId other) =>
            string.Equals(Value, other.Value, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is DomainId other && Equals(other);

        public override int GetHashCode() =>
            StringComparer.Ordinal.GetHashCode(Value);

        public override string ToString() => Value;

        public static bool operator ==(DomainId left, DomainId right) => left.Equals(right);
        public static bool operator !=(DomainId left, DomainId right) => !left.Equals(right);
    }

    /// <summary>
    /// 结构基线中的稳定领域身份单一来源。
    /// </summary>
    public static class DomainIds
    {
        public static readonly DomainId Item = new DomainId("Item");
        public static readonly DomainId Resource = new DomainId("Resource");
        public static readonly DomainId Building = new DomainId("Building");
        public static readonly DomainId Zombie = new DomainId("Zombie");
        public static readonly DomainId Animal = new DomainId("Animal");
        public static readonly DomainId Collision = new DomainId("Collision");
    }
}
