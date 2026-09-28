using System;
using System.Collections.Generic;
using YooAsset;

namespace MiniGame.Boot
{
    /// <summary>
    /// 按优先级提供主地址和备用地址的 YooAsset 远端资源服务。
    /// </summary>
    public sealed class YooAssetRemoteService : IRemoteService
    {
        private readonly string defaultHostServer;
        private readonly string fallbackHostServer;

        /// <summary>
        /// 创建远端资源服务。
        /// </summary>
        /// <param name="defaultHostServer">主 CDN 包目录。</param>
        /// <param name="fallbackHostServer">备用 CDN 包目录。</param>
        public YooAssetRemoteService(string defaultHostServer, string fallbackHostServer = null)
        {
            if (string.IsNullOrWhiteSpace(defaultHostServer)) throw new ArgumentException("主 CDN 根地址不能为空。", nameof(defaultHostServer));
            this.defaultHostServer = defaultHostServer.TrimEnd('/');
            this.fallbackHostServer = string.IsNullOrWhiteSpace(fallbackHostServer) ? null : fallbackHostServer.TrimEnd('/');
        }

        /// <summary>
        /// 获取指定文件的主地址和备用地址。
        /// </summary>
        /// <param name="fileName">YooAsset 请求的远端文件名。</param>
        /// <returns>按优先级排列的下载地址。</returns>
        public IReadOnlyList<string> GetRemoteUrls(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) throw new ArgumentNullException(nameof(fileName));
            string url = string.Concat(defaultHostServer, "/", fileName.TrimStart('/'));
            if (string.IsNullOrEmpty(fallbackHostServer) || string.Equals(defaultHostServer, fallbackHostServer, StringComparison.Ordinal))
                return new[] { url };
            return new[] { url, string.Concat(fallbackHostServer, "/", fileName.TrimStart('/')) };
        }
    }
}
