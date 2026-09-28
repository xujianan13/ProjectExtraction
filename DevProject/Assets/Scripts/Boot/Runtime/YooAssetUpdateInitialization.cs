using System;
using GameFramework;
using YooAsset;

namespace MiniGame.Boot
{
    /// <summary>
    /// 创建 YooAsset 各运行模式的初始化参数。
    /// </summary>
    public static class YooAssetUpdateInitialization
    {
        /// <summary>
        /// 创建编辑器模拟运行参数。
        /// </summary>
        /// <param name="request">内容包更新请求。</param>
        /// <returns>编辑器模拟参数。</returns>
        public static InitializePackageOptions CreateEditorSimulate(UpdatePackageRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var result = EditorSimulateBuildInvoker.Build(request.PackageName, (int)EBundleType.VirtualAssetBundle);
            return new EditorSimulateModeOptions
            {
                EditorFileSystemParameters = FileSystemParameters.CreateDefaultEditorFileSystemParameters(result.PackageRootDirectory)
            };
        }

        /// <summary>
        /// 创建离线运行参数。
        /// </summary>
        /// <param name="request">内容包更新请求。</param>
        /// <returns>内置包初始化参数。</returns>
        public static InitializePackageOptions CreateOffline(UpdatePackageRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            return new OfflinePlayModeOptions
            {
                BuiltinFileSystemParameters = FileSystemParameters.CreateDefaultBuiltinFileSystemParameters()
            };
        }

        /// <summary>
        /// 创建联机运行参数。
        /// </summary>
        /// <param name="request">内容包更新请求。</param>
        /// <param name="defaultHostServer">主 CDN 包目录。</param>
        /// <param name="fallbackHostServer">备用 CDN 包目录。</param>
        /// <returns>联机包初始化参数。</returns>
        public static InitializePackageOptions CreateHost(UpdatePackageRequest request, string defaultHostServer, string fallbackHostServer = null)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var remoteService = new YooAssetRemoteService(defaultHostServer, fallbackHostServer);
            var builtinFileSystem = FileSystemParameters.CreateDefaultBuiltinFileSystemParameters();
            builtinFileSystem.AddParameter(EFileSystemParameter.CopyBuiltinPackageManifest, true);
            var cacheFileSystem = FileSystemParameters.CreateDefaultSandboxFileSystemParameters(remoteService);
            cacheFileSystem.AddParameter(EFileSystemParameter.DownloadMaxConcurrency, 8);
            cacheFileSystem.AddParameter(EFileSystemParameter.DownloadMaxRequestPerFrame, 1);
            cacheFileSystem.AddParameter(EFileSystemParameter.DownloadWatchdogTimeout, 10);
            return new HostPlayModeOptions
            {
                BuiltinFileSystemParameters = builtinFileSystem,
                CacheFileSystemParameters = cacheFileSystem
            };
        }

        /// <summary>
        /// 创建 WebGL 运行参数。
        /// </summary>
        /// <param name="request">内容包更新请求。</param>
        /// <param name="defaultHostServer">主 CDN 包目录。</param>
        /// <param name="fallbackHostServer">备用 CDN 包目录。</param>
        /// <returns>Web 包初始化参数。</returns>
        public static InitializePackageOptions CreateWeb(UpdatePackageRequest request, string defaultHostServer, string fallbackHostServer = null)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var remoteService = new YooAssetRemoteService(defaultHostServer, fallbackHostServer);
            return new WebPlayModeOptions
            {
                WebServerFileSystemParameters = FileSystemParameters.CreateDefaultWebRemoteFileSystemParameters(remoteService)
            };
        }
    }
}
