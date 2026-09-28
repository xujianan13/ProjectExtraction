using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameFramework
{
    /// <summary>随 YooAsset Package Manifest 原子发布的运行配置。</summary>
    [Serializable]
    public sealed class PackageRuntimeSettings
    {
        [SerializeField] private int _schemaVersion = 1;
        [SerializeField] private string _assemblyName = string.Empty;
        [SerializeField] private string _entryTypeName = string.Empty;
        [SerializeField] private string _entryMethodName = "StartAsync";
        [SerializeField] private string[] _aotMetadataLocations = Array.Empty<string>();
        [SerializeField] private string[] _requiredLocations = Array.Empty<string>();
        [SerializeField] private string _startupSceneLocation = string.Empty;
        [SerializeField] private string _minimumHostVersion = string.Empty;
        [SerializeField] private string _maximumHostVersion = string.Empty;
        [SerializeField] private string _defaultHostServer = string.Empty;
        [SerializeField] private string _fallbackHostServer = string.Empty;
        [SerializeField] private int _downloadMaxConcurrency = UpdatePackageRequest.DefaultDownloadMaxConcurrency;
        [SerializeField] private int _retryCount = UpdatePackageRequest.DefaultRetryCount;
        [SerializeField] private int _timeoutSeconds = UpdatePackageRequest.DefaultTimeoutSeconds;
        [SerializeField] private bool _allowOfflineGameFallback = true;

        /// <summary>配置结构版本。</summary>
        public int SchemaVersion => _schemaVersion;

        /// <summary>热更程序集名称，不含扩展名。</summary>
        public string AssemblyName => _assemblyName ?? string.Empty;

        /// <summary>热更入口类型全名。</summary>
        public string EntryTypeName => _entryTypeName ?? string.Empty;

        /// <summary>热更入口静态方法名。</summary>
        public string EntryMethodName => _entryMethodName ?? string.Empty;

        /// <summary>需要补充元数据的 AOT DLL 逻辑地址。</summary>
        public IReadOnlyList<string> AotMetadataLocations =>
            _aotMetadataLocations ?? Array.Empty<string>();

        /// <summary>该 Package 达到最低可用状态所需的额外逻辑地址。</summary>
        public IReadOnlyList<string> RequiredLocations =>
            _requiredLocations ?? Array.Empty<string>();

        /// <summary>GamePackage 的首个业务场景逻辑地址。</summary>
        public string StartupSceneLocation => _startupSceneLocation ?? string.Empty;

        /// <summary>允许使用该 Package 的最低宿主版本；空字符串表示不限制。</summary>
        public string MinimumHostVersion => _minimumHostVersion ?? string.Empty;

        /// <summary>允许使用该 Package 的最高宿主版本；空字符串表示不限制。</summary>
        public string MaximumHostVersion => _maximumHostVersion ?? string.Empty;

        /// <summary>由当前 UpdaterPackage 版本提供的内容 CDN 地址；为空时使用 BootKernel 保底地址。</summary>
        public string DefaultHostServer => _defaultHostServer ?? string.Empty;

        /// <summary>由当前 UpdaterPackage 版本提供的备用内容 CDN 地址。</summary>
        public string FallbackHostServer => _fallbackHostServer ?? string.Empty;

        /// <summary>内容下载的最大并发数。</summary>
        public int DownloadMaxConcurrency => _downloadMaxConcurrency;

        /// <summary>单文件下载失败重试次数。</summary>
        public int RetryCount => _retryCount;

        /// <summary>网络请求超时秒数。</summary>
        public int TimeoutSeconds => _timeoutSeconds;

        /// <summary>GamePackage 更新不可用时是否允许继续激活本地候选版本。</summary>
        public bool AllowOfflineGameFallback => _allowOfflineGameFallback;

        /// <summary>使用明确默认值解析 Package Settings JSON。</summary>
        /// <param name="json">Package 中加载的 JSON 文本。</param>
        /// <returns>解析后的包级配置。</returns>
        public static PackageRuntimeSettings FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new ArgumentException("Package Settings JSON 不能为空。", nameof(json));
            var settings = new PackageRuntimeSettings();
            JsonUtility.FromJsonOverwrite(json, settings);
            return settings;
        }

        /// <summary>按完整固定字段集合、精确类型和单一字段身份严格解析 JSON。</summary>
        /// <param name="json">从活动 Package 加载的 JSON 原文。</param>
        /// <returns>严格解析后的包级配置。</returns>
        public static PackageRuntimeSettings FromJsonStrict(string json)
        {
            PackageRuntimeSettingsJsonValidator.Validate(json);
            try
            {
                return FromJson(json);
            }
            catch (Exception exception)
            {
                throw new FormatException(
                    "Package Settings JSON 无法映射到固定配置结构。",
                    exception);
            }
        }

        /// <summary>校验配置结构、入口、数值范围和宿主兼容性。</summary>
        /// <param name="minimumSchemaVersion">BootKernel 支持的最低结构版本。</param>
        /// <param name="maximumSchemaVersion">BootKernel 支持的最高结构版本。</param>
        /// <param name="hostVersion">当前宿主版本。</param>
        /// <param name="error">校验失败原因。</param>
        /// <returns>配置是否有效。</returns>
        public bool TryValidate(
            int minimumSchemaVersion,
            int maximumSchemaVersion,
            string hostVersion,
            out string error)
        {
            if (_schemaVersion < minimumSchemaVersion || _schemaVersion > maximumSchemaVersion)
            {
                error = $"配置 Schema 不受支持：actual={_schemaVersion}, " +
                        $"supported={minimumSchemaVersion}-{maximumSchemaVersion}";
                return false;
            }

            if (string.IsNullOrWhiteSpace(AssemblyName) ||
                string.IsNullOrWhiteSpace(EntryTypeName) ||
                string.IsNullOrWhiteSpace(EntryMethodName))
            {
                error = "热更程序集或入口描述不能为空。";
                return false;
            }

            if (_downloadMaxConcurrency <= 0 || _retryCount < 0 || _timeoutSeconds <= 0)
            {
                error = "下载并发、重试次数或超时参数超出有效范围。";
                return false;
            }

            if (!TryValidateVersionRange(hostVersion, out error)) return false;
            if (!TryValidateServer(DefaultHostServer, "主 CDN", out error)) return false;
            if (!TryValidateServer(FallbackHostServer, "备用 CDN", out error)) return false;
            if (!TryValidateLocations(AotMetadataLocations, "AOT Metadata", out error)) return false;
            if (!TryValidateLocations(RequiredLocations, "必需资源", out error)) return false;

            error = string.Empty;
            return true;
        }

        private bool TryValidateVersionRange(string hostVersion, out string error)
        {
            if (string.IsNullOrEmpty(MinimumHostVersion) && string.IsNullOrEmpty(MaximumHostVersion))
            {
                error = string.Empty;
                return true;
            }

            if (!Version.TryParse(hostVersion, out Version current))
            {
                error = $"宿主版本格式无效：{hostVersion}";
                return false;
            }

            if (!string.IsNullOrEmpty(MinimumHostVersion))
            {
                if (!Version.TryParse(MinimumHostVersion, out Version minimum))
                {
                    error = $"最低宿主版本格式无效：{MinimumHostVersion}";
                    return false;
                }
                if (current < minimum)
                {
                    error = $"宿主版本过低：current={current}, minimum={minimum}";
                    return false;
                }
            }

            if (!string.IsNullOrEmpty(MaximumHostVersion))
            {
                if (!Version.TryParse(MaximumHostVersion, out Version maximum))
                {
                    error = $"最高宿主版本格式无效：{MaximumHostVersion}";
                    return false;
                }
                if (current > maximum)
                {
                    error = $"宿主版本过高：current={current}, maximum={maximum}";
                    return false;
                }
            }

            error = string.Empty;
            return true;
        }

        private static bool TryValidateLocations(
            IReadOnlyList<string> locations,
            string description,
            out string error)
        {
            for (int i = 0; i < locations.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(locations[i])) continue;
                error = $"{description} 地址列表包含空项。";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private static bool TryValidateServer(string value, string description, out string error)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                error = string.Empty;
                return true;
            }
            if (!Uri.TryCreate(value, UriKind.Absolute, out Uri uri) ||
                (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            {
                error = $"{description}地址无效：{value}";
                return false;
            }

            error = string.Empty;
            return true;
        }
    }
}
