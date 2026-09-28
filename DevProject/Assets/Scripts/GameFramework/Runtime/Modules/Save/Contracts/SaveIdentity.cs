using System;
using System.Text;

namespace GameFramework
{
    /// <summary>
    /// 一份逻辑存档的稳定身份。
    /// </summary>
    public readonly struct SaveIdentity : IEquatable<SaveIdentity>
    {
        public string GameId { get; }
        public string Environment { get; }
        public string ProfileId { get; }
        public string SlotId { get; }
        public bool IsValid => Valid(GameId) && Valid(Environment) && Valid(ProfileId) && Valid(SlotId);
        public string KeyPrefix => IsValid ? $"saves/v1/{Encode(GameId)}/{Encode(Environment)}/{Encode(ProfileId)}/{Encode(SlotId)}" : null;

        /// <summary>
        /// 创建逻辑存档身份。
        /// </summary>
        /// <param name="gameId">游戏标识。</param>
        /// <param name="environment">环境标识。</param>
        /// <param name="profileId">玩家标识。</param>
        /// <param name="slotId">槽位标识。</param>
        public SaveIdentity(string gameId, string environment, string profileId, string slotId)
        {
            GameId = gameId;
            Environment = environment;
            ProfileId = profileId;
            SlotId = slotId;
        }

        /// <summary>
        /// 判断身份是否相同。
        /// </summary>
        /// <param name="other">另一存档身份。</param>
        /// <returns>四段身份是否完全相同。</returns>
        public bool Equals(SaveIdentity other) => GameId == other.GameId && Environment == other.Environment &&
            ProfileId == other.ProfileId && SlotId == other.SlotId;

        /// <summary>
        /// 判断对象是否为相同身份。
        /// </summary>
        /// <param name="obj">待比较对象。</param>
        /// <returns>对象是否为相同身份。</returns>
        public override bool Equals(object obj) => obj is SaveIdentity other && Equals(other);

        /// <summary>
        /// 计算身份哈希。
        /// </summary>
        /// <returns>身份哈希。</returns>
        public override int GetHashCode() => HashCode.Combine(GameId, Environment, ProfileId, SlotId);

        /// <summary>
        /// 检查身份片段。
        /// </summary>
        /// <param name="value">片段内容。</param>
        /// <returns>片段是否有效。</returns>
        private static bool Valid(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Trim() != value) return false;
            try { return new UTF8Encoding(false, true).GetByteCount(value) <= 128; }
            catch (EncoderFallbackException) { return false; }
        }

        /// <summary>
        /// 将身份片段编码为不含路径特殊字符的 Base64URL。
        /// </summary>
        /// <param name="value">原始片段。</param>
        /// <returns>安全片段。</returns>
        private static string Encode(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd('=')
            .Replace('+', '-').Replace('/', '_');
    }
}
