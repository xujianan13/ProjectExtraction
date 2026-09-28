using System;
using System.IO;
using System.Linq;
using System.Text;
using GameFramework;
using HybridCLR.Editor;
using HybridCLR.Editor.Commands;
using UnityEditor;
using UnityEngine;
using YooAsset;
using YooAsset.Editor;

namespace MiniGame.Editor
{
    /// <summary>
    /// 收集小游戏内容并构建 GamePackage 资源包。
    /// </summary>
    public static class GamePackageBuildPipeline
    {
        private const string PackageName = "GamePackage";
        private const string ResourcesPath = "Assets/GameResources";
        private const string CodePath = "Assets/GameResources/Code";
        private const string SettingsPath = "Assets/GameResources/Config/GamePackageSettings.json";
        private const string AssemblyName = "GameLogic.dll";

        /// <summary>
        /// 编译并复制游戏程序集，配置 GamePackage 资源收集器。
        /// </summary>
        [MenuItem("Tools/Unity Mini Game Framework/Prepare GamePackage")]
        public static void Prepare()
        {
            ExcelTableExportMenu.EnsureEditorReady();
            ExcelTableExportMenu.Verify();
            GameFramework.Editor.Scaffolding.ScaffoldResult scaffold = GameFramework.Editor.Scaffolding.ScaffoldGenerator.Validate();
            if (!scaffold.success)
                throw new InvalidOperationException("脚手架内容验证失败，请先修复配置或重新生成：" + string.Join("; ", scaffold.errors));
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrEmpty(projectRoot)) throw new InvalidOperationException("无法解析 Unity 工程根目录。");

            BuildTarget target = EditorUserBuildSettings.activeBuildTarget;
            BuildTargetGroup targetGroup = BuildPipeline.GetBuildTargetGroup(target);
            bool isIl2Cpp = PlayerSettings.GetScriptingBackend(targetGroup) == ScriptingImplementation.IL2CPP;
            if (isIl2Cpp) CompileDllCommand.CompileDll(target);
            string sourceDirectory = isIl2Cpp ? SettingsUtil.GetHotUpdateDllsOutputDirByTarget(target) : Path.Combine("Library", "ScriptAssemblies");
            if (!Path.IsPathRooted(sourceDirectory)) sourceDirectory = Path.Combine(projectRoot, sourceDirectory);
            string source = Path.Combine(sourceDirectory, AssemblyName);
            if (!File.Exists(source))
            {
                string guidance = isIl2Cpp ? "请先运行 HybridCLR/CompileDll/ActiveBuildTarget，再执行 Prepare GamePackage。" : "请先等待 Unity 编译 GameLogic 程序集。";
                throw new FileNotFoundException($"游戏程序集不存在：{source}。{guidance}", source);
            }
            if (!AssetDatabase.IsValidFolder(ResourcesPath)) throw new DirectoryNotFoundException($"游戏资源目录不存在：{ResourcesPath}");
            if (!AssetDatabase.IsValidFolder(CodePath)) AssetDatabase.CreateFolder(ResourcesPath, "Code");

            string destination = Path.Combine(projectRoot, CodePath, AssemblyName + ".bytes");
            ExcelTableExportMenu.EnsureEditorReady();
            File.Copy(source, destination, true);
            AssetDatabase.ImportAsset(CodePath + "/" + AssemblyName + ".bytes", ImportAssetOptions.ForceUpdate);

            BundleCollectorPackage package = BundleCollectorSettingData.Setting.Packages
                .FirstOrDefault(item => string.Equals(item.PackageName, PackageName, StringComparison.Ordinal));
            package ??= BundleCollectorSettingData.CreatePackage(PackageName);
            package.EnableAddressable = true;
            package.AutoCollectShaders = false;

            string settingsFile = Path.Combine(projectRoot, SettingsPath);
            if (!File.Exists(settingsFile)) throw new FileNotFoundException("GamePackage 缺少包级配置。", settingsFile);
            PackageRuntimeSettings runtimeSettings = PackageRuntimeSettings.FromJsonStrict(File.ReadAllText(settingsFile, Encoding.UTF8));
            GameContentCollectorSetup.Configure(package, GamePackageBuildSettings.LoadOrCreate(), runtimeSettings);
            BundleCollectorSettingData.SaveFile();
            Debug.Log($"{PackageName} 已准备：{ResourcesPath}，程序集 {destination}");
        }

        /// <summary>
        /// 构建当前平台的离线资源包，并将首包文件复制到 StreamingAssets。
        /// </summary>
        [MenuItem("Tools/Unity Mini Game Framework/Build Offline GamePackage")]
        public static void BuildOffline() => BuildPackage(EBundledCopyOption.ClearAndCopyAll);

        /// <summary>
        /// 构建供 CDN 发布的内容包，不改动内置首包。
        /// </summary>
        [MenuItem("Tools/Unity Mini Game Framework/Build Remote GamePackage")]
        public static void BuildRemote() => BuildPackage(EBundledCopyOption.None);

        /// <summary>
        /// 构建完整远端包，同时仅将 Startup 标签资源复制进安装包。
        /// </summary>
        [MenuItem("Tools/Unity Mini Game Framework/Build Startup GamePackage")]
        public static void BuildStartup() => BuildPackage(EBundledCopyOption.ClearAndCopyByTags);

        /// <summary>
        /// 按独立内容版本构建当前平台 GamePackage。
        /// </summary>
        /// <param name="copyOption">首包资源复制方式。</param>
        private static void BuildPackage(EBundledCopyOption copyOption)
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrEmpty(projectRoot)) throw new InvalidOperationException("无法解析 Unity 工程根目录。");
            GamePackageBuildSettings settings = GamePackageBuildSettings.LoadOrCreate();
            string packageVersion = settings.PackageVersion?.Trim();
            if (string.IsNullOrWhiteSpace(packageVersion)) throw new InvalidOperationException("GamePackage 内容版本为空，请在 GamePackageBuildSettings.asset 中填写版本。");
            if (packageVersion.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || packageVersion == "." || packageVersion == "..")
                throw new InvalidOperationException($"GamePackage 内容版本包含非法目录名字符：{packageVersion}");

            BuildTarget target = EditorUserBuildSettings.activeBuildTarget;
            string outputRoot = Path.Combine(projectRoot, "AssetBundleBuild");
            string outputDirectory = Path.Combine(outputRoot, target.ToString(), PackageName, packageVersion);
            if (Directory.Exists(outputDirectory))
                throw new InvalidOperationException($"{PackageName} 内容版本 {packageVersion} 已构建：{outputDirectory}。请在 GamePackageBuildSettings.asset 中设置新版本后再构建。");

            Prepare();
            string settingsFile = Path.Combine(projectRoot, SettingsPath);
            if (!File.Exists(settingsFile)) throw new FileNotFoundException("GamePackage 缺少包级配置。", settingsFile);
            byte[] settingsBytes = File.ReadAllBytes(settingsFile);
            PackageRuntimeSettings.FromJsonStrict(Encoding.UTF8.GetString(settingsBytes));
            AssetDatabase.ImportAsset(SettingsPath, ImportAssetOptions.ForceUpdate);

            bool copyToStreamingAssets = copyOption != EBundledCopyOption.None;
            var parameters = new ScriptableBuildParameters
            {
                BuildOutputRoot = outputRoot,
                BundledFileRoot = BundleBuilderHelper.GetStreamingAssetsRoot(),
                BuildPipeline = nameof(EBuildPipeline.ScriptableBuildPipeline),
                BuildBundleType = (int)EBundleType.AssetBundle,
                BuildTarget = target,
                PackageName = PackageName,
                PackageVersion = packageVersion,
                EnableSharePackRule = true,
                VerifyBuildingResult = true,
                BundledCopyOption = copyOption,
                BundledCopyParams = copyOption == EBundledCopyOption.ClearAndCopyByTags ? "Startup" : string.Empty,
                CompressOption = ECompressOption.LZ4
            };
            BuildResult result = new ScriptableBuildPipeline().Run(parameters, true);
            if (!result.Success) throw new InvalidOperationException($"{PackageName} 构建失败：{result.FailedTask}，{result.ErrorInfo}");
            string settingsSidecar = Path.Combine(result.OutputPackageDirectory, $"{PackageName}_{packageVersion}.settings.json");
            File.WriteAllBytes(settingsSidecar, settingsBytes);
            if (copyToStreamingAssets)
            {
                string builtinDirectory = Path.Combine(BundleBuilderHelper.GetStreamingAssetsRoot(), PackageName);
                Directory.CreateDirectory(builtinDirectory);
                File.WriteAllBytes(Path.Combine(builtinDirectory, Path.GetFileName(settingsSidecar)), settingsBytes);
            }
            AssetDatabase.Refresh();
            string buildKind = copyOption == EBundledCopyOption.ClearAndCopyAll ? "离线首包" :
                copyOption == EBundledCopyOption.ClearAndCopyByTags ? "启动首包" : "远端包";
            Debug.Log($"{PackageName} {buildKind}构建完成：{result.OutputPackageDirectory}");
        }
    }
}
