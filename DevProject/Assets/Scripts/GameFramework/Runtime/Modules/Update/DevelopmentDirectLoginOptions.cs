using System;

namespace GameFramework
{
    /// <summary>
    /// BootKernel 传给 GameLogic 的开发直连开关与公开提示值；非开发构建必须传入禁用值。
    /// </summary>
    public sealed class DevelopmentDirectLoginOptions
    {
        /// <summary>创建开发直连选项。</summary>
        public DevelopmentDirectLoginOptions(bool enabled, string suggestedCertificateSha256 = null)
        {
            if (!string.IsNullOrEmpty(suggestedCertificateSha256) &&
                !IsSha256(suggestedCertificateSha256))
                throw new ArgumentException("开发证书 SHA-256 必须是 64 位十六进制。", nameof(suggestedCertificateSha256));
            Enabled = enabled;
            SuggestedCertificateSha256 = suggestedCertificateSha256 ?? string.Empty;
        }

        /// <summary>前端是否应进入 Debug 直连登录模式。</summary>
        public bool Enabled { get; }

        /// <summary>本地联调证书的公开 SHA-256 提示值；为空时由开发者在表单中填写。</summary>
        public string SuggestedCertificateSha256 { get; }

        private static bool IsSha256(string value)
        {
            if (value.Length != 64) return false;
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                if ((character >= '0' && character <= '9') ||
                    (character >= 'a' && character <= 'f') ||
                    (character >= 'A' && character <= 'F'))
                    continue;
                return false;
            }
            return true;
        }
    }
}
