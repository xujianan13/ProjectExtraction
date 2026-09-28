using System;

namespace GameFramework
{
    /// <summary>
    /// 音频播放句柄。Id 单调递增不复用；Channel 用于 O(1) 定位所属通道。默认值（Id=0）表示无效。
    /// </summary>
    public readonly struct AudioHandle : IEquatable<AudioHandle>
    {
        /// <summary>句柄整型 ID。0 为无效值。</summary>
        public int Id { get; }

        /// <summary>所属通道，用于回查 slot。</summary>
        public AudioChannel Channel { get; }

        /// <summary>
        /// 是否为非默认句柄；播放结束后标识仍保留，当前状态请通过 IAudioModule.IsPlaying 查询。
        /// </summary>
        public bool IsValid => Id != 0;

        /// <summary>由 AudioModule 内部分配 Id 与 Channel 后构造。</summary>
        public AudioHandle(int id, AudioChannel channel)
        {
            Id = id;
            Channel = channel;
        }

        /// <inheritdoc />
        public bool Equals(AudioHandle other) => Id == other.Id && Channel == other.Channel;

        /// <inheritdoc />
        public override bool Equals(object obj) => obj is AudioHandle other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode() => (Id * 397) ^ (int)Channel;

        /// <inheritdoc />
        public override string ToString() => $"AudioHandle({Channel}:{Id})";

        public static bool operator ==(AudioHandle a, AudioHandle b) => a.Equals(b);
        public static bool operator !=(AudioHandle a, AudioHandle b) => !a.Equals(b);
    }
}
