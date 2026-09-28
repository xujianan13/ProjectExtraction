using System;

namespace GameFramework
{
    /// <summary>
    /// 定时器句柄。指向一个已注册的定时器，可用于 Cancel；句柄是 readonly struct，可安全持有。
    /// 默认值 (Id = 0) 表示无效句柄。
    /// </summary>
    public readonly struct TimerHandle : IEquatable<TimerHandle>
    {
        /// <summary>句柄整型 ID。0 为无效值，由 TimerModule 单调递增分配，不复用。</summary>
        public int Id { get; }

        /// <summary>是否为有效句柄。</summary>
        public bool IsValid => Id != 0;

        /// <summary>由 TimerModule 内部分配 ID 时构造。</summary>
        public TimerHandle(int id) { Id = id; }

        /// <inheritdoc />
        public bool Equals(TimerHandle other) => Id == other.Id;

        /// <inheritdoc />
        public override bool Equals(object obj) => obj is TimerHandle other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode() => Id;

        /// <inheritdoc />
        public override string ToString() => $"TimerHandle({Id})";

        public static bool operator ==(TimerHandle a, TimerHandle b) => a.Id == b.Id;
        public static bool operator !=(TimerHandle a, TimerHandle b) => a.Id != b.Id;
    }
}
