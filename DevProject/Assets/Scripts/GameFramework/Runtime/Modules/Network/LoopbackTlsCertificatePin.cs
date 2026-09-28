using System;
using System.Security.Cryptography;

namespace GameFramework
{
    /// <summary>
    /// 本地联调自签证书的精确 SHA-256 固定值。调用方必须同时满足显式开发构建、TLS/WSS
    /// 和回环地址三个条件；该类型在非开发构建中拒绝创建，不能成为生产信任旁路。
    /// </summary>
    public sealed class LoopbackTlsCertificatePin
    {
        private readonly byte[] _digest;

        /// <summary>从 64 个小写或大写十六进制字符创建固定值。</summary>
        /// <param name="sha256Hex">证书 DER 的 SHA-256。</param>
        public LoopbackTlsCertificatePin(string sha256Hex)
        {
#if !UNITY_EDITOR && !DEVELOPMENT_BUILD
            throw new PlatformNotSupportedException("本地证书固定只允许 Editor 或 Development Build。");
#else
            if (string.IsNullOrEmpty(sha256Hex) || sha256Hex.Length != 64)
                throw new ArgumentException("本地证书 SHA-256 必须是 64 位十六进制。", nameof(sha256Hex));
            _digest = new byte[32];
            for (int index = 0; index < _digest.Length; index++)
            {
                int high = Hex(sha256Hex[index * 2]);
                int low = Hex(sha256Hex[index * 2 + 1]);
                if (high < 0 || low < 0)
                    throw new ArgumentException("本地证书 SHA-256 包含非十六进制字符。", nameof(sha256Hex));
                _digest[index] = (byte)((high << 4) | low);
            }
#endif
        }

        /// <summary>校验目标必须是无凭据的 HTTPS/WSS 回环端点。</summary>
        /// <param name="endpoint">待连接端点。</param>
        public void ValidateEndpoint(Uri endpoint)
        {
            if (endpoint == null || !endpoint.IsAbsoluteUri || !endpoint.IsLoopback ||
                !string.IsNullOrEmpty(endpoint.UserInfo) || !string.IsNullOrEmpty(endpoint.Fragment) ||
                endpoint.Scheme != Uri.UriSchemeHttps && endpoint.Scheme != "wss")
            {
                throw new ArgumentException("开发证书固定只能用于无凭据的 HTTPS/WSS 回环端点。", nameof(endpoint));
            }
        }

        /// <summary>以固定时间比较证书 DER 的 SHA-256。</summary>
        /// <param name="certificateData">服务端证书 DER。</param>
        /// <returns>是否精确匹配。</returns>
        public bool Matches(byte[] certificateData)
        {
            if (certificateData == null || certificateData.Length == 0) return false;
            byte[] actual;
            using (SHA256 sha = SHA256.Create()) actual = sha.ComputeHash(certificateData);
            try
            {
                int difference = actual.Length ^ _digest.Length;
                int maximum = Math.Min(actual.Length, _digest.Length);
                for (int index = 0; index < maximum; index++) difference |= actual[index] ^ _digest[index];
                return difference == 0;
            }
            finally
            {
                Array.Clear(actual, 0, actual.Length);
            }
        }

        private static int Hex(char value)
        {
            if (value >= '0' && value <= '9') return value - '0';
            if (value >= 'a' && value <= 'f') return value - 'a' + 10;
            if (value >= 'A' && value <= 'F') return value - 'A' + 10;
            return -1;
        }
    }
}
