using System;

namespace GameFramework
{
    /// <summary>
    /// 事件标识符。用 int 而非 string / Type 作为 key，避免反射与字符串 GC，业务侧集中维护一份常量表。
    /// 支持从 int 隐式转换，调用方可直接传数字字面量。
    /// </summary>
    public readonly struct EventId : IEquatable<EventId>
    {
        /// <summary>事件原始整型值。建议由业务侧的静态常量类集中分配，避免数值冲突。</summary>
        public int Value { get; }

        /// <summary>用整型值构造事件 ID。</summary>
        public EventId(int value) { Value = value; }

        /// <summary>从整型隐式转换为事件 ID，调用方可直接传数字字面量。</summary>
        public static implicit operator EventId(int value) => new(value);

        /// <summary>显式转换为整型，等价于读取 <see cref="Value"/>。</summary>
        public static explicit operator int(EventId id) => id.Value;

        /// <inheritdoc />
        public bool Equals(EventId other) => Value == other.Value;

        /// <inheritdoc />
        public override bool Equals(object obj) => obj is EventId other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode() => Value;

        /// <inheritdoc />
        public override string ToString() => $"EventId({Value})";

        public static bool operator ==(EventId a, EventId b) => a.Value == b.Value;
        public static bool operator !=(EventId a, EventId b) => a.Value != b.Value;
    }
}
