using System;

namespace GameFramework
{
    /// <summary>
    /// 流程句柄。指向一个已启动的根流程，可用于暂停、恢复或取消。
    /// 默认值 (Id = 0) 表示无效句柄。
    /// </summary>
    public readonly struct FlowHandle : IEquatable<FlowHandle>
    {
        /// <summary>句柄整型 ID。0 为无效值，由 FlowModule 单调递增分配，不复用。</summary>
        public int Id { get; }

        /// <summary>是否为有效句柄。</summary>
        public bool IsValid => Id != 0;

        /// <summary>由 FlowModule 内部分配 ID 时构造。</summary>
        /// <param name="id">句柄 ID。</param>
        public FlowHandle(int id)
        {
            Id = id;
        }

        /// <inheritdoc />
        public bool Equals(FlowHandle other) => Id == other.Id;

        /// <inheritdoc />
        public override bool Equals(object obj) => obj is FlowHandle other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode() => Id;

        /// <inheritdoc />
        public override string ToString() => $"FlowHandle({Id})";

        /// <summary>判断两个句柄是否相等。</summary>
        public static bool operator ==(FlowHandle a, FlowHandle b) => a.Id == b.Id;

        /// <summary>判断两个句柄是否不相等。</summary>
        public static bool operator !=(FlowHandle a, FlowHandle b) => a.Id != b.Id;
    }
}
