using System;

namespace GameFramework
{
    /// <summary>仓库外部渠道层签发并注入客户端的一次性不透明登录票据。</summary>
    public readonly struct ChannelLoginTicket
    {
        private const int MaxChannelIdLength = 128;
        private const int MaxTicketLength = 8192;

        /// <summary>创建经过本地边界校验的渠道登录票据。</summary>
        /// <param name="channelId">渠道稳定标识。</param>
        /// <param name="value">一次性不透明票据。</param>
        public ChannelLoginTicket(string channelId, string value)
        {
            if (!IsValidChannelId(channelId))
                throw new ArgumentException("渠道标识格式无效。", nameof(channelId));
            if (string.IsNullOrEmpty(value) || value.Length > MaxTicketLength)
                throw new ArgumentException("渠道登录票据长度无效。", nameof(value));

            ChannelId = channelId;
            Value = value;
        }

        /// <summary>获取渠道稳定标识。</summary>
        public string ChannelId { get; }

        /// <summary>获取敏感的一次性不透明票据；禁止写入日志、遥测或普通配置。</summary>
        public string Value { get; }

        /// <summary>返回不泄露敏感票据的诊断文本。</summary>
        /// <returns>只包含渠道标识的脱敏文本。</returns>
        public override string ToString()
        {
            return string.Concat("ChannelLoginTicket(", ChannelId, ", [REDACTED])");
        }

        private static bool IsValidChannelId(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > MaxChannelIdLength)
                return false;
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                bool alphaNumeric =
                    character >= 'a' && character <= 'z' ||
                    character >= 'A' && character <= 'Z' ||
                    character >= '0' && character <= '9';
                if (alphaNumeric || character == '.' || character == '_' || character == '-')
                    continue;
                return false;
            }

            char first = value[0];
            return first >= 'a' && first <= 'z' ||
                   first >= 'A' && first <= 'Z' ||
                   first >= '0' && first <= '9';
        }
    }
}
