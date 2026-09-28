using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameFramework;
using UnityEditor;
using YooAsset.Editor;

namespace MiniGame.Editor
{
    /// <summary>
    /// 将 GameResources 的直接子目录配置为独立资源收集器。
    /// </summary>
    internal static class GameContentCollectorSetup
    {
        private const string ResourcesPath = "Assets/GameResources";
        private const string OwnedGroupDescription = "Managed by GamePackageBuildPipeline";
        private static readonly HashSet<string> DefaultStartupDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Code", "Config", "Input", "Startup"
        };

        /// <summary>
        /// 配置分组与启动标签，同时保留用户创建的不重叠分组。
        /// </summary>
        /// <param name="package">待配置的资源包。</param>
        /// <param name="settings">构建设置。</param>
        /// <param name="runtimeSettings">运行时启动配置。</param>
        internal static void Configure(BundleCollectorPackage package, GamePackageBuildSettings settings, PackageRuntimeSettings runtimeSettings)
        {
            var startupDirectories = new HashSet<string>(DefaultStartupDirectories, StringComparer.OrdinalIgnoreCase);
            foreach (string name in settings.AdditionalStartupDirectories)
            {
                if (string.IsNullOrWhiteSpace(name) || name == "." || name == ".." || name.IndexOfAny(new[] { '/', (char)92, ';' }) >= 0)
                    throw new InvalidOperationException($"额外启动目录只能填写 GameResources 的直接子目录名称：{name}");
                startupDirectories.Add(name.Trim());
            }

            string[] directoryPaths = AssetDatabase.GetSubFolders(ResourcesPath).OrderBy(path => path, StringComparer.Ordinal).ToArray();
            foreach (string name in startupDirectories.Except(DefaultStartupDirectories, StringComparer.OrdinalIgnoreCase))
            {
                if (!directoryPaths.Any(path => Path.GetFileName(path).Equals(name, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException($"额外启动目录不存在：{ResourcesPath}/{name}");
            }

            var rootAssetPaths = GetAssetPaths().Where(path => string.Equals(Path.GetDirectoryName(path)?.Replace((char)92, '/'), ResourcesPath, StringComparison.Ordinal)).ToArray();
            var requiredPaths = ResolveRequiredPaths(runtimeSettings);
            ValidateCustomGroups(package, directoryPaths.Concat(rootAssetPaths).ToArray());
            RemoveLegacyRootCollector(package);
            foreach (BundleCollectorGroup group in package.Groups.Where(item => item.GroupDesc == OwnedGroupDescription).ToArray())
                BundleCollectorSettingData.RemoveGroup(package, group);
            foreach (string directoryPath in directoryPaths)
            {
                string directoryName = Path.GetFileName(directoryPath);
                bool startup = startupDirectories.Contains(directoryName) || requiredPaths.Any(path => IsWithin(path, directoryPath));
                string tags = startupDirectories.Contains(directoryName) ? "Startup" : directoryName + ";Optional" + (startup ? ";Startup" : string.Empty);
                string packRule = directoryName.Equals("Code", StringComparison.OrdinalIgnoreCase) ||
                    directoryName.Equals("Config", StringComparison.OrdinalIgnoreCase) ? nameof(PackDirectory) : nameof(PackSeparately);
                AddManagedGroup(package, directoryName, directoryPath, tags, packRule);
            }

            foreach (string assetPath in rootAssetPaths)
            {
                bool startup = requiredPaths.Contains(assetPath);
                AddManagedGroup(package, "Root_" + Path.GetFileName(assetPath), assetPath,
                    startup ? "Startup" : "Optional", nameof(PackSeparately));
            }

            foreach (string requiredPath in requiredPaths)
            {
                bool covered = directoryPaths.Any(path => IsWithin(requiredPath, path)) || rootAssetPaths.Contains(requiredPath);
                if (!covered) throw new InvalidOperationException($"启动必需资源未被 GameResources 分组收集：{requiredPath}");
            }
        }

        /// <summary>
        /// 在修改设置前检查自动路径与用户分组是否冲突。
        /// </summary>
        /// <param name="package">资源包。</param>
        /// <param name="managedPaths">拟自动管理的收集路径。</param>
        private static void ValidateCustomGroups(BundleCollectorPackage package, string[] managedPaths)
        {
            foreach (BundleCollectorGroup group in package.Groups.Where(item => item.GroupDesc != OwnedGroupDescription))
            {
                foreach (string path in managedPaths)
                {
                    string groupName = "GameContent/" + (AssetDatabase.IsValidFolder(path) ? Path.GetFileName(path) : "Root_" + Path.GetFileName(path));
                    if (group.GroupName == groupName) throw new InvalidOperationException($"GamePackage 自动分组名称与自定义分组冲突：{groupName}");
                    foreach (BundleCollector collector in group.Collectors)
                    {
                        bool legacyRoot = group.GroupName == "GameContent" && collector.CollectPath == ResourcesPath &&
                            collector.AddressRuleName == nameof(AddressByFileName) && collector.PackRuleName == nameof(PackDirectory) &&
                            collector.FilterRuleName == nameof(CollectAll);
                        if (!legacyRoot && (IsWithin(path, collector.CollectPath) || IsWithin(collector.CollectPath, path)))
                            throw new InvalidOperationException($"GamePackage 自动分组 {path} 与自定义收集器 {collector.CollectPath} 重叠；请调整该自定义收集器。");
                    }
                }
            }
        }
        /// <summary>
        /// 从旧版本单根收集器迁移，保留同组中的其他自定义收集器。
        /// </summary>
        /// <param name="package">待迁移的资源包。</param>
        private static void RemoveLegacyRootCollector(BundleCollectorPackage package)
        {
            BundleCollectorGroup legacyGroup = package.Groups.FirstOrDefault(item => item.GroupName == "GameContent");
            if (legacyGroup == null) return;
            foreach (BundleCollector collector in legacyGroup.Collectors.Where(item => item.CollectPath == ResourcesPath && item.AddressRuleName == nameof(AddressByFileName) &&
                item.PackRuleName == nameof(PackDirectory) && item.FilterRuleName == nameof(CollectAll)).ToArray())
                BundleCollectorSettingData.RemoveCollector(legacyGroup, collector);
            if (legacyGroup.Collectors.Count == 0) BundleCollectorSettingData.RemoveGroup(package, legacyGroup);
        }

        /// <summary>
        /// 添加一个不与用户收集器重叠的框架管理分组。
        /// </summary>
        /// <param name="package">资源包。</param>
        /// <param name="name">目录或资产名称。</param>
        /// <param name="path">收集路径。</param>
        /// <param name="tags">资源标签。</param>
        /// <param name="packRule">打包规则名称。</param>
        private static void AddManagedGroup(BundleCollectorPackage package, string name, string path, string tags, string packRule)
        {
            foreach (BundleCollectorGroup existingGroup in package.Groups)
            {
                foreach (BundleCollector existingCollector in existingGroup.Collectors)
                {
                    if (IsWithin(path, existingCollector.CollectPath) || IsWithin(existingCollector.CollectPath, path))
                        throw new InvalidOperationException($"GamePackage 自动分组 {path} 与自定义收集器 {existingCollector.CollectPath} 重叠；请调整该自定义收集器。");
                }
            }

            string groupName = "GameContent/" + name;
            if (package.Groups.Any(item => item.GroupName == groupName))
                throw new InvalidOperationException($"GamePackage 自动分组名称与自定义分组冲突：{groupName}");
            BundleCollectorGroup group = BundleCollectorSettingData.CreateGroup(package, groupName);
            group.GroupDesc = OwnedGroupDescription;
            group.AssetTags = tags;
            BundleCollectorSettingData.CreateCollector(group, new BundleCollector
            {
                CollectPath = path,
                CollectorGUID = AssetDatabase.AssetPathToGUID(path),
                CollectorType = ECollectorType.MainAssetCollector,
                AddressRuleName = nameof(AddressByFileName),
                PackRuleName = packRule,
                FilterRuleName = nameof(CollectAll)
            });
        }

        /// <summary>
        /// 将地址解析为唯一的 GameResources 资产路径。
        /// </summary>
        /// <param name="settings">运行时启动配置。</param>
        /// <returns>必需资产路径集合。</returns>
        private static HashSet<string> ResolveRequiredPaths(PackageRuntimeSettings settings)
        {
            if (string.IsNullOrWhiteSpace(settings.AssemblyName))
                throw new InvalidOperationException("GamePackageSettings 的 AssemblyName 不能为空。");
            var locations = new HashSet<string>(StringComparer.Ordinal) { "GamePackageSettings", settings.AssemblyName + ".dll" };
            foreach (string location in settings.AotMetadataLocations) locations.Add(location);
            foreach (string location in settings.RequiredLocations) locations.Add(location);
            if (!string.IsNullOrWhiteSpace(settings.StartupSceneLocation)) locations.Add(settings.StartupSceneLocation);

            if (locations.Any(string.IsNullOrWhiteSpace))
                throw new InvalidOperationException("GamePackageSettings 包含空的启动必需地址。");
            string[] assetPaths = GetAssetPaths();
            var result = new HashSet<string>(StringComparer.Ordinal);
            foreach (string location in locations)
            {
                string[] matches = assetPaths.Where(path => Path.GetFileNameWithoutExtension(path) == location).ToArray();
                if (matches.Length != 1)
                    throw new InvalidOperationException($"启动必需地址 {location} 在 GameResources 中应对应唯一资产，实际找到 {matches.Length} 个：{string.Join(", ", matches)}");
                result.Add(matches[0]);
            }
            return result;
        }

        /// <summary>
        /// 枚举 GameResources 下的普通资产路径。
        /// </summary>
        /// <returns>按路径排序的资产数组。</returns>
        private static string[] GetAssetPaths()
        {
            return AssetDatabase.FindAssets(string.Empty, new[] { ResourcesPath })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => !string.IsNullOrEmpty(path) && !AssetDatabase.IsValidFolder(path))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
        }

        /// <summary>
        /// 判断路径是否位于收集路径自身或其子路径下。
        /// </summary>
        /// <param name="path">待检查路径。</param>
        /// <param name="collectPath">收集路径。</param>
        /// <returns>是否处于收集范围。</returns>
        private static bool IsWithin(string path, string collectPath)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(collectPath)) return false;
            return path.Equals(collectPath, StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith(collectPath.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase);
        }
    }
}
