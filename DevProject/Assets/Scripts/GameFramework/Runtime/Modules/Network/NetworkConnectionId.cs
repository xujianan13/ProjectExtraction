using System;

namespace GameFramework
{
    /// <summary>NetworkModule 分配的稳定进程内连接句柄。</summary>
    public readonly struct NetworkConnectionId : IEquatable<NetworkConnectionId>
    {
        /// <summary>句柄数值；零表示无效句柄。</summary>
        public int Value { get; }

        /// <summary>句柄是否有效。</summary>
        public bool IsValid => Value > 0;

        /// <summary>由 NetworkModule 内部分配后构造。</summary>
        public NetworkConnectionId(int value)
        {
            Value = value;
        }

        /// <summary>从整型隐式转换为连接句柄。</summary>
        public static implicit operator NetworkConnectionId(int value) => new(value);

        /// <summary>显式取得句柄数值。</summary>
        public static explicit operator int(NetworkConnectionId id) => id.Value;

        /// <inheritdoc />
        public bool Equals(NetworkConnectionId other) => Value == other.Value;

        /// <inheritdoc />
        public override bool Equals(object obj) =>
            obj is NetworkConnectionId other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode() => Value;

        /// <inheritdoc />
        public override string ToString() => $"NetworkConnectionId({Value})";

        /// <summary>判断两个句柄是否相等。</summary>
        public static bool operator ==(
            NetworkConnectionId left,
            NetworkConnectionId right) => left.Equals(right);

        /// <summary>判断两个句柄是否不等。</summary>
        public static bool operator !=(
            NetworkConnectionId left,
            NetworkConnectionId right) => !left.Equals(right);
    }
}
