using System;

namespace GameFramework
{
    /// <summary>仓库外实名适配层签发的一次性不透明验证票据，不包含玩家原始身份信息。</summary>
    public readonly struct RealNameVerificationTicket
    {
        private const int MaximumProviderIdLength = 128;
        private const int MaximumTicketLength = 8192;

        /// <summary>创建经过本地边界校验的实名验证票据。</summary>
        /// <param name="providerId">服务端允许的外部验证方稳定标识。</param>
        /// <param name="value">只能提交给 Game Auth 的一次性不透明票据。</param>
        public RealNameVerificationTicket(string providerId, string value)
        {
            if (!IsValidProviderId(providerId))
                throw new ArgumentException("实名验证方标识格式无效。", nameof(providerId));
            if (string.IsNullOrEmpty(value) || value.Length > MaximumTicketLength ||
                value.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0)
                throw new ArgumentException("实名验证票据长度或格式无效。", nameof(value));
            ProviderId = providerId;
            Value = value;
        }

        /// <summary>获取外部验证方稳定标识。</summary>
        public string ProviderId { get; }

        /// <summary>获取敏感的一次性票据；禁止写入日志、遥测、PlayerPrefs 或普通配置。</summary>
        public string Value { get; }

        /// <summary>返回不泄露验证票据的诊断文本。</summary>
        /// <returns>只包含验证方标识的脱敏文本。</returns>
        public override string ToString()
        {
            return string.Concat("RealNameVerificationTicket(", ProviderId, ", [REDACTED])");
        }

        private static bool IsValidProviderId(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > MaximumProviderIdLength)
                return false;
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                bool alphaNumeric = character >= 'a' && character <= 'z' ||
                                    character >= 'A' && character <= 'Z' ||
                                    character >= '0' && character <= '9';
                if (!alphaNumeric && character != '.' && character != '_' && character != '-')
                    return false;
            }
            return true;
        }
    }
}
