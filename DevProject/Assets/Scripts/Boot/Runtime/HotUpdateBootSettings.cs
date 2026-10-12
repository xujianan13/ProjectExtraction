using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;
using UnityEngine;
using UnityEngine.Networking;
using YooAsset;

namespace MiniGame.Boot
{
    /// <summary>
    /// 内容包在当前宿主中的运行模式。
    /// </summary>
    public enum HotUpdatePlayMode
    {
        Auto,
        EditorSimulate,
        Offline,
        Host,
        Web
    }

    /// <summary>
    /// 玩家存档的平台后端与临时内存会话选择。
    /// </summary>
    public enum SaveBackendMode { Auto, File, BrowserIndexedDb, PlatformSdk, Memory }
    /// <summary>
    /// 随宿主发布的 GamePackage 初始化配置。
    /// </summary>
    [CreateAssetMenu(fileName = "HotUpdateBootSettings", menuName = "MiniGame/Hot Update Boot Settings")]
    public sealed class HotUpdateBootSettings : ScriptableObject, IUpdatePackageOptionsProvider, IUpdatePackageSettingsSource
    {
        [SerializeField] private HotUpdatePlayMode playMode = HotUpdatePlayMode.Auto;
        [SerializeField] private SaveBackendMode saveBackendMode = SaveBackendMode.Auto;
        [SerializeField] private string channel = UpdatePackageRequest.DefaultChannel;
        [SerializeField] private string defaultHostServer = string.Empty;
        [SerializeField] private string fallbackHostServer = string.Empty;
        [SerializeField] private bool allowCachedPackageFallback = true;
        [Tooltip("开启后启动前下载全部缺失资源；关闭时只准备启动标签，其余资源在使用时加载。")]
        [SerializeField] private bool downloadAllResourcesBeforeLaunch;
        [SerializeField] private string[] additionalStartupDownloadTags = Array.Empty<string>();

        /// <summary>
        /// 发布渠道。
        /// </summary>
        public string Channel => string.IsNullOrWhiteSpace(channel) ? UpdatePackageRequest.DefaultChannel : channel;

        /// <summary>
        /// 玩家存档后端选择。
        /// </summary>
        public SaveBackendMode SaveBackend => saveBackendMode;

        /// <summary>
        /// 更新检查失败时是否继续使用已经激活的本地包。
        /// </summary>
        public bool AllowCachedPackageFallback => allowCachedPackageFallback;

        /// <summary>
        /// 创建启动更新请求，默认只准备启动资源和公共依赖。
        /// </summary>
        /// <param name="hostVersion">当前宿主程序版本。</param>
        /// <returns>保持现有兼容性检查与激活恢复规则的更新请求。</returns>
        public UpdatePackageRequest CreateUpdateRequest(string hostVersion)
        {
            List<string> tags = null;
            if (!downloadAllResourcesBeforeLaunch)
            {
                tags = new List<string> { "Startup" };
                if (additionalStartupDownloadTags != null) tags.AddRange(additionalStartupDownloadTags);
            }
            return new UpdatePackageRequest(HotUpdatePackageNames.Game, Channel, hostVersion,
                activationMode: UpdateActivationMode.Immediate, downloadTags: tags);
        }

        /// <summary>
        /// 获取当前平台使用的运行模式。
        /// </summary>
        /// <returns>最终的运行模式。</returns>
        public HotUpdatePlayMode ResolvePlayMode()
        {
            if (playMode != HotUpdatePlayMode.Auto) return playMode;
#if UNITY_EDITOR
            return HotUpdatePlayMode.EditorSimulate;
#elif UNITY_WEBGL
            return string.IsNullOrWhiteSpace(defaultHostServer) ? HotUpdatePlayMode.Offline : HotUpdatePlayMode.Web;
#else
            return string.IsNullOrWhiteSpace(defaultHostServer) ? HotUpdatePlayMode.Offline : HotUpdatePlayMode.Host;
#endif
        }

        /// <summary>
        /// 按运行模式创建 YooAsset 初始化参数。
        /// </summary>
        /// <param name="request">GamePackage 更新请求。</param>
        /// <returns>YooAsset 初始化参数。</returns>
        public InitializePackageOptions CreateOptions(UpdatePackageRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            switch (ResolvePlayMode())
            {
                case HotUpdatePlayMode.EditorSimulate: return YooAssetUpdateInitialization.CreateEditorSimulate(request);
                case HotUpdatePlayMode.Offline: return YooAssetUpdateInitialization.CreateOffline(request);
                case HotUpdatePlayMode.Host: return YooAssetUpdateInitialization.CreateHost(request, defaultHostServer, fallbackHostServer);
                case HotUpdatePlayMode.Web: return YooAssetUpdateInitialization.CreateWeb(request, defaultHostServer, fallbackHostServer);
                default: throw new InvalidOperationException("无法确定 GamePackage 的运行模式。");
            }
        }

        /// <summary>
        /// 从与候选版本同目录的旁路配置读取宿主兼容信息。
        /// </summary>
        /// <param name="request">内容包更新请求。</param>
        /// <param name="packageVersion">待激活的内容版本。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>与候选内容包一同发布的配置原文。</returns>
        public async UniTask<string> LoadSettingsAsync(UpdatePackageRequest request, string packageVersion, CancellationToken ct)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(packageVersion)) throw new ArgumentException("候选内容版本不能为空。", nameof(packageVersion));
            HotUpdatePlayMode mode = ResolvePlayMode();
#if UNITY_EDITOR
            if (mode == HotUpdatePlayMode.EditorSimulate)
            {
                TextAsset editorSettings = UnityEditor.AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/GameResources/Config/GamePackageSettings.json");
                if (editorSettings == null)
                    throw new UpdateException(UpdateErrorCode.PackageManifestLoadFailed, "编辑器中缺少 GamePackageSettings.json。", false);
                return editorSettings.text;
            }
#endif
            if (mode == HotUpdatePlayMode.Offline)
                return await LoadBuiltinSettingsAsync(request, packageVersion, ct);

            IHttpModule http = GameEntry.Get<IHttpModule>();
            if (http == null)
                throw new UpdateException(UpdateErrorCode.PackageInitializationFailed, "框架 HTTP 模块未初始化。", false);

            string fileName = $"{request.PackageName}_{packageVersion}.settings.json";
            var remoteService = new YooAssetRemoteService(defaultHostServer, fallbackHostServer);
            var urls = remoteService.GetRemoteUrls(fileName);
            string lastError = string.Empty;
            for (int i = 0; i < urls.Count; i++)
            {
                var response = await http.SendAsync(new HttpRequest(HttpMethod.Get, urls[i]) { TimeoutSeconds = request.TimeoutSeconds }, ct);
                if (response.IsSuccess)
                {
                    string json = response.GetBodyAsString();
                    if (!string.IsNullOrWhiteSpace(json)) return json;
                    lastError = $"兼容配置为空：{fileName}";
                }
                else
                {
                    lastError = $"兼容配置下载失败：{fileName}, status={response.StatusCode}, error={response.Error}";
                }
            }

            throw new UpdateException(UpdateErrorCode.PackageManifestLoadFailed, lastError, true);
        }

        /// <summary>
        /// 读取安装包内的基础内容版本。
        /// </summary>
        /// <param name="packageName">内容包名称。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>内置版本；不存在时返回空字符串。</returns>
        public async UniTask<string> LoadBuiltinVersionAsync(string packageName, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(packageName)) throw new ArgumentException("内容包名称不能为空。", nameof(packageName));
            string version = await ReadBuiltinTextAsync(packageName, packageName + ".version", 10, ct);
            return version.Trim();
        }

        /// <summary>
        /// 从安装包内读取与内容版本一同构建的兼容配置。
        /// </summary>
        /// <param name="request">内容包更新请求。</param>
        /// <param name="packageVersion">内置内容版本。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>内置包级配置原文。</returns>
        private static async UniTask<string> LoadBuiltinSettingsAsync(UpdatePackageRequest request, string packageVersion, CancellationToken ct)
        {
            string fileName = $"{request.PackageName}_{packageVersion}.settings.json";
            string json = await ReadBuiltinTextAsync(request.PackageName, fileName, request.TimeoutSeconds, ct);
            if (string.IsNullOrWhiteSpace(json))
                throw new UpdateException(UpdateErrorCode.PackageManifestLoadFailed, $"内置包兼容配置读取失败：{fileName}", false);
            return json;
        }

        /// <summary>
        /// 读取 StreamingAssets 中的内容包文本文件。
        /// </summary>
        /// <param name="packageName">内容包名称。</param>
        /// <param name="fileName">要读取的文件名。</param>
        /// <param name="timeoutSeconds">读取超时秒数。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>文件文本；读取失败时返回空字符串。</returns>
        private static async UniTask<string> ReadBuiltinTextAsync(string packageName, string fileName, int timeoutSeconds, CancellationToken ct)
        {
            string root = Application.streamingAssetsPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string path = $"{root}/yoo/{packageName}/{fileName}";
            if (path.IndexOf("://", StringComparison.Ordinal) < 0) path = new Uri(path).AbsoluteUri;
            using var webRequest = UnityWebRequest.Get(path);
            webRequest.timeout = timeoutSeconds;
            UnityWebRequestAsyncOperation operation = webRequest.SendWebRequest();
            await UniTask.WaitUntil(() => operation.isDone, cancellationToken: ct);
            return webRequest.result == UnityWebRequest.Result.Success ? webRequest.downloadHandler.text : string.Empty;
        }
    }
}
