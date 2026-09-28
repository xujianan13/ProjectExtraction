using System;

namespace GameFramework
{
    /// <summary>
    /// 玩法实例句柄，用于从 GameplayModule 操作已启动的玩法。
    /// </summary>
    public readonly struct GameplayHandle : IEquatable<GameplayHandle>
    {
        /// <summary>无效句柄。</summary>
        public static readonly GameplayHandle Invalid = new(0);

        /// <summary>句柄内部编号。</summary>
        public int Id { get; }

        /// <summary>创建玩法句柄。</summary>
        /// <param name="id">内部编号。</param>
        public GameplayHandle(int id)
        {
            Id = id;
        }

        /// <summary>句柄是否有效。</summary>
        public bool IsValid => Id > 0;

        /// <inheritdoc />
        public bool Equals(GameplayHandle other) => Id == other.Id;

        /// <inheritdoc />
        public override bool Equals(object obj) => obj is GameplayHandle other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode() => Id;

        /// <inheritdoc />
        public override string ToString() => $"GameplayHandle({Id})";

        /// <summary>判断两个句柄是否相等。</summary>
        public static bool operator ==(GameplayHandle a, GameplayHandle b) => a.Id == b.Id;

        /// <summary>判断两个句柄是否不相等。</summary>
        public static bool operator !=(GameplayHandle a, GameplayHandle b) => a.Id != b.Id;
    }
}
