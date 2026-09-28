using System;

namespace GameFramework
{
    /// <summary>
    /// BootKernel 将已验签 Bootstrap 解析为 GameLogic 可消费的稳定上下文；不暴露未经验证的 JSON，
    /// 也不包含账号令牌、GameTicket 或任何私钥。
    /// </summary>
    public sealed class ClientBootstrapContext
    {
        private readonly byte[] _serverDirectoryPublicKey;

        /// <summary>创建不可变 Bootstrap 上下文。</summary>
        public ClientBootstrapContext(
            string environment,
            string channel,
            string clientPlatform,
            string releaseId,
            string hostVersion,
            string updaterVersion,
            string gameVersion,
            uint protocolVersion,
            long runtimeConfigVersion,
            Uri loginEndpoint,
            Uri serverDirectoryEndpoint,
            string serverDirectoryKeyId,
            byte[] serverDirectoryPublicKey,
            LoopbackTlsCertificatePin loopbackCertificatePin = null)
        {
            Environment = Required(environment, nameof(environment), 64);
            Channel = Required(channel, nameof(channel), 128);
            ClientPlatform = Required(clientPlatform, nameof(clientPlatform), 64);
            ReleaseId = Required(releaseId, nameof(releaseId), 128);
            HostVersion = Required(hostVersion, nameof(hostVersion), 128);
            UpdaterVersion = Required(updaterVersion, nameof(updaterVersion), 128);
            GameVersion = Required(gameVersion, nameof(gameVersion), 128);
            if (protocolVersion == 0) throw new ArgumentOutOfRangeException(nameof(protocolVersion));
            if (runtimeConfigVersion < 1 || runtimeConfigVersion > 9007199254740991L)
                throw new ArgumentOutOfRangeException(nameof(runtimeConfigVersion));
            LoginEndpoint = ValidateHttps(loginEndpoint, nameof(loginEndpoint));
            ServerDirectoryEndpoint = ValidateHttps(serverDirectoryEndpoint, nameof(serverDirectoryEndpoint));
            ServerDirectoryKeyId = Required(serverDirectoryKeyId, nameof(serverDirectoryKeyId), 128);
            if (serverDirectoryPublicKey == null || serverDirectoryPublicKey.Length != 32)
                throw new ArgumentException("ServerDirectory Ed25519 公钥必须是 32 字节。", nameof(serverDirectoryPublicKey));
            _serverDirectoryPublicKey = (byte[])serverDirectoryPublicKey.Clone();
            loopbackCertificatePin?.ValidateEndpoint(LoginEndpoint);
            loopbackCertificatePin?.ValidateEndpoint(ServerDirectoryEndpoint);
            ProtocolVersion = protocolVersion;
            RuntimeConfigVersion = runtimeConfigVersion;
            LoopbackCertificatePin = loopbackCertificatePin;
        }

        /// <summary>部署环境。</summary>
        public string Environment { get; }
        /// <summary>发行渠道。</summary>
        public string Channel { get; }
        /// <summary>客户端平台维度。</summary>
        public string ClientPlatform { get; }
        /// <summary>Bootstrap 绑定的不可变 Release。</summary>
        public string ReleaseId { get; }
        /// <summary>宿主版本。</summary>
        public string HostVersion { get; }
        /// <summary>UpdaterPackage 版本。</summary>
        public string UpdaterVersion { get; }
        /// <summary>GamePackage 版本。</summary>
        public string GameVersion { get; }
        /// <summary>客户端与 ClientGateway 协议版本。</summary>
        public uint ProtocolVersion { get; }
        /// <summary>登录与会话绑定的运行配置版本。</summary>
        public long RuntimeConfigVersion { get; }
        /// <summary>Game Auth HTTPS 基址。</summary>
        public Uri LoginEndpoint { get; }
        /// <summary>Platform ServerDirectory HTTPS 地址。</summary>
        public Uri ServerDirectoryEndpoint { get; }
        /// <summary>目录签名 Key ID。</summary>
        public string ServerDirectoryKeyId { get; }
        /// <summary>仅供显式本地联调的回环证书固定；生产上下文必须为空。</summary>
        public LoopbackTlsCertificatePin LoopbackCertificatePin { get; }

        /// <summary>返回目录验签公钥的防御性副本。</summary>
        public byte[] CopyServerDirectoryPublicKey() =>
            (byte[])_serverDirectoryPublicKey.Clone();

        private static string Required(string value, string name, int maximum)
        {
            if (string.IsNullOrWhiteSpace(value) || value != value.Trim() || value.Length > maximum)
                throw new ArgumentException(name + " 无效。", name);
            return value;
        }

        private static Uri ValidateHttps(Uri value, string name)
        {
            if (value == null || !value.IsAbsoluteUri || value.Scheme != Uri.UriSchemeHttps ||
                !string.IsNullOrEmpty(value.UserInfo) || !string.IsNullOrEmpty(value.Fragment))
                throw new ArgumentException(name + " 必须是无凭据 HTTPS 绝对地址。", name);
            return value;
        }
    }
}
