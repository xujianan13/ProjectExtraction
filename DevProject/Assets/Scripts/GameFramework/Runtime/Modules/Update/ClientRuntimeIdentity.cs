using System;

namespace GameFramework
{
    /// <summary>
    /// 描述当前实际激活的客户端版本身份；该对象不代表 Platform Bootstrap 已经通过验签，
    /// 也不携带服务端点、令牌或任何信任密钥。
    /// </summary>
    public sealed class ClientRuntimeIdentity
    {
        /// <summary>创建当前激活客户端的不可变版本身份。</summary>
        public ClientRuntimeIdentity(
            string channel,
            string clientPlatform,
            string hostVersion,
            string updaterVersion,
            string gameVersion,
            uint protocolVersion,
            long runtimeConfigVersion)
        {
            Channel = Required(channel, nameof(channel), 128);
            ClientPlatform = Required(clientPlatform, nameof(clientPlatform), 64);
            HostVersion = Required(hostVersion, nameof(hostVersion), 128);
            UpdaterVersion = Required(updaterVersion, nameof(updaterVersion), 128);
            GameVersion = Required(gameVersion, nameof(gameVersion), 128);
            if (protocolVersion == 0)
                throw new ArgumentOutOfRangeException(nameof(protocolVersion));
            if (runtimeConfigVersion < 1 || runtimeConfigVersion > 9007199254740991L)
                throw new ArgumentOutOfRangeException(nameof(runtimeConfigVersion));

            ProtocolVersion = protocolVersion;
            RuntimeConfigVersion = runtimeConfigVersion;
        }

        /// <summary>当前发行渠道。</summary>
        public string Channel { get; }

        /// <summary>当前客户端平台维度。</summary>
        public string ClientPlatform { get; }

        /// <summary>当前宿主版本。</summary>
        public string HostVersion { get; }

        /// <summary>当前实际激活的 UpdaterPackage 版本。</summary>
        public string UpdaterVersion { get; }

        /// <summary>当前实际激活的 GamePackage 版本。</summary>
        public string GameVersion { get; }

        /// <summary>当前客户端协议版本。</summary>
        public uint ProtocolVersion { get; }

        /// <summary>当前客户端运行配置版本。</summary>
        public long RuntimeConfigVersion { get; }

        private static string Required(string value, string name, int maximum)
        {
            if (string.IsNullOrWhiteSpace(value) ||
                value != value.Trim() ||
                value.Length > maximum)
                throw new ArgumentException(name + " 无效。", name);
            return value;
        }
    }
}
