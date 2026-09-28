using System;
using System.IO;
using System.Linq;
using HybridCLR.Editor;
using HybridCLR.Editor.Commands;
using HybridCLR.Editor.Installer;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MiniGame.Editor
{
    /// <summary>
    /// 为新项目准备 HybridCLR 并构建 Windows 离线安装包。
    /// </summary>
    public static class ProjectBuildPipeline
    {
        private const BuildTarget WindowsTarget = BuildTarget.StandaloneWindows64;
        private const string PreparationFileName = "ProjectBuildPreparation.json";

        /// <summary>
        /// 安装当前项目的 HybridCLR 工具链并生成 Windows 构建代码。
        /// </summary>
        [MenuItem("Tools/Unity Mini Game Framework/Prepare Windows Player")]
        public static void PrepareWindows()
        {
            ValidateEnvironment();
            string identityPath = Path.Combine(GetProjectRoot(), ".project-identity.json");
            if (File.Exists(identityPath)) ProjectTemplateSetup.ApplyAndValidate();
            ExcelTableExportMenu.EnsureEditorReady();

            var installer = new InstallerController();
            if (!installer.HasInstalledHybridCLR() || installer.PackageVersion != installer.InstalledLibil2cppVersion)
            {
                string source = Environment.GetEnvironmentVariable("MINIGAME_HYBRIDCLR_SOURCE");
                if (string.IsNullOrWhiteSpace(source)) installer.InstallDefaultHybridCLR();
                else
                {
                    if (!Directory.Exists(Path.Combine(source, "hybridclr")))
                        throw new BuildFailedException($"MINIGAME_HYBRIDCLR_SOURCE 必须指向含 hybridclr 子目录的 libil2cpp：{source}");
                    installer.InstallFromLocal(source);
                }
                installer = new InstallerController();
                if (!installer.HasInstalledHybridCLR() || installer.PackageVersion != installer.InstalledLibil2cppVersion)
                    throw new BuildFailedException("HybridCLR 安装未完成，检查工具链来源与当前包版本。");
            }

            PrebuildCommand.GenerateAll();
            HostAotMetadataBuild.Prepare(WindowsTarget);
            string bridge = Path.Combine(SettingsUtil.GeneratedCppDir, "MethodBridge.cpp");
            if (!File.Exists(bridge)) throw new BuildFailedException("HybridCLR 未生成 MethodBridge.cpp。");
            var preparation = new Preparation { unityVersion = Application.unityVersion, hybridClrVersion = installer.PackageVersion,
                target = WindowsTarget.ToString(), development = EditorUserBuildSettings.development };
            File.WriteAllText(GetPreparationPath(), JsonUtility.ToJson(preparation, true));
            Debug.Log($"Windows Player 已准备：{GetPreparationPath()}。请退出 Unity，再启动第二个构建会话。");
        }

        /// <summary>
        /// 构建离线 GamePackage 与 Windows IL2CPP Player。
        /// </summary>
        [MenuItem("Tools/Unity Mini Game Framework/Build Windows Offline Player")]
        public static void BuildWindowsOffline()
        {
            ValidateEnvironment();
            var installer = new InstallerController();
            if (!installer.HasInstalledHybridCLR() || installer.PackageVersion != installer.InstalledLibil2cppVersion)
                throw new BuildFailedException("HybridCLR 工具链未安装或版本不符，请先执行 Prepare Windows Player。");

            string preparationPath = GetPreparationPath();
            if (!File.Exists(preparationPath)) throw new BuildFailedException("缺少本项目的 Windows 生成记录，请先执行 Prepare Windows Player。");
            Preparation preparation = JsonUtility.FromJson<Preparation>(File.ReadAllText(preparationPath));
            if (preparation == null || preparation.unityVersion != Application.unityVersion || preparation.hybridClrVersion != installer.PackageVersion ||
                preparation.target != WindowsTarget.ToString() || preparation.development != EditorUserBuildSettings.development ||
                !File.Exists(Path.Combine(SettingsUtil.GeneratedCppDir, "MethodBridge.cpp")))
                throw new BuildFailedException("HybridCLR 生成配置与当前构建不匹配，请重新执行 Prepare Windows Player。");
            HostAotMetadataBuild.Validate(WindowsTarget);

            string[] scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray();
            if (scenes.Length == 0 || scenes[0] != "Assets/Scenes/Main.unity" || !scenes.Contains("Assets/Scenes/Loading.unity"))
                throw new BuildFailedException("启用的构建场景必须以 Main.unity 开始，并包含 Loading.unity。");

            GamePackageBuildPipeline.BuildOffline();
            string output = Environment.GetEnvironmentVariable("MINIGAME_PLAYER_OUTPUT");
            if (string.IsNullOrWhiteSpace(output)) output = Path.Combine(GetUnityProjectRoot(), "Builds", "Windows", "Game.exe");
            else if (!Path.IsPathRooted(output)) output = Path.GetFullPath(Path.Combine(GetUnityProjectRoot(), output));
            if (!string.Equals(Path.GetExtension(output), ".exe", StringComparison.OrdinalIgnoreCase))
                throw new BuildFailedException("MINIGAME_PLAYER_OUTPUT 必须指向 Windows .exe 文件。");
            Directory.CreateDirectory(Path.GetDirectoryName(output));

            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = scenes, locationPathName = output,
                target = WindowsTarget, options = EditorUserBuildSettings.development ? BuildOptions.Development : BuildOptions.None });
            if (report == null) throw new BuildFailedException("Unity 未返回 Player 构建报告。");
            var result = new PlayerBuildResult { result = report.summary.result.ToString(), output = output,
                totalSizeBytes = report.summary.totalSize.ToString(), totalTime = report.summary.totalTime.ToString(),
                totalErrors = report.summary.totalErrors, totalWarnings = report.summary.totalWarnings,
                unityVersion = Application.unityVersion, builtAtUtc = DateTime.UtcNow.ToString("O") };
            string reportPath = Path.Combine(Path.GetDirectoryName(output), "build-report.json");
            File.WriteAllText(reportPath, JsonUtility.ToJson(result, true));
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                throw new BuildFailedException($"Windows Player 构建失败：{report.summary.result}，错误 {report.summary.totalErrors}。报告：{reportPath}");
            Debug.Log($"Windows 离线 Player 构建完成：{output}；报告：{reportPath}");
        }

        /// <summary>
        /// 验证当前 Unity 会话的 Windows IL2CPP 构建环境。
        /// </summary>
        private static void ValidateEnvironment()
        {
            ExcelTableExportMenu.EnsureEditorReady();
            if (EditorUserBuildSettings.activeBuildTarget != WindowsTarget)
                throw new BuildFailedException("请先将 Unity 活动构建平台切换到 StandaloneWindows64。");
            if (PlayerSettings.GetScriptingBackend(BuildTargetGroup.Standalone) != ScriptingImplementation.IL2CPP)
                throw new BuildFailedException("Standalone 构建必须启用 IL2CPP。");
        }

        /// <summary>
        /// 获取包含 DevProject 的新项目根目录。
        /// </summary>
        /// <returns>新项目根目录。</returns>
        private static string GetProjectRoot()
        {
            DirectoryInfo project = Directory.GetParent(Application.dataPath);
            if (project?.Parent == null || project.Name != "DevProject")
                throw new BuildFailedException("Unity 工程必须位于项目根目录的 DevProject 文件夹中。");
            return project.Parent.FullName;
        }

        /// <summary>
        /// 获取 Unity 工程根目录。
        /// </summary>
        /// <returns>DevProject 完整路径。</returns>
        private static string GetUnityProjectRoot() => Path.Combine(GetProjectRoot(), "DevProject");

        /// <summary>
        /// 获取本项目的 HybridCLR 生成记录路径。
        /// </summary>
        /// <returns>生成记录的完整路径。</returns>
        private static string GetPreparationPath() => Path.Combine(GetUnityProjectRoot(), "HybridCLRData", PreparationFileName);

        [Serializable]
        private sealed class Preparation
        {
            public string unityVersion;
            public string hybridClrVersion;
            public string target;
            public bool development;
        }

        [Serializable]
        private sealed class PlayerBuildResult
        {
            public string result;
            public string output;
            public string totalSizeBytes;
            public string totalTime;
            public int totalErrors;
            public int totalWarnings;
            public string unityVersion;
            public string builtAtUtc;
        }
    }
}
