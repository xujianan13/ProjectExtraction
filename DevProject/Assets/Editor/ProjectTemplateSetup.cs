using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace MiniGame.Editor
{
    /// <summary>
    /// 在新项目首次导入时通过 Unity API 应用项目身份。
    /// </summary>
    [InitializeOnLoad]
    public static class ProjectTemplateSetup
    {
        private const string IdentityFileName = ".project-identity.json";

        /// <summary>
        /// 注册首次导入后的身份配置任务。
        /// </summary>
        static ProjectTemplateSetup()
        {
            EditorApplication.delayCall += ApplyIfPending;
        }

        /// <summary>
        /// 应用待处理的身份配置并准备首次运行所需的 GamePackage。
        /// </summary>
        [MenuItem("Tools/Unity Mini Game Framework/Apply New Project Identity")]
        public static void ApplyAndValidate()
        {
            string repositoryRoot = GetRepositoryRoot();
            string identityPath = Path.Combine(repositoryRoot, IdentityFileName);
            if (!File.Exists(identityPath)) throw new FileNotFoundException("没有待应用的新项目身份配置。", identityPath);

            ProjectIdentity identity = JsonUtility.FromJson<ProjectIdentity>(File.ReadAllText(identityPath));
            if (identity == null || string.IsNullOrWhiteSpace(identity.projectName) || string.IsNullOrWhiteSpace(identity.companyName) ||
                string.IsNullOrWhiteSpace(identity.applicationId) || !Regex.IsMatch(identity.applicationId, @"^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$"))
                throw new InvalidDataException("新项目身份配置无效，请检查 .project-identity.json。");

            ValidateFiles(repositoryRoot, identity.template == "Minimal");
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Unity 尚未完成资源导入和脚本编译，请稍后重试。");

            if (!identity.identityApplied)
            {
                PlayerSettings.productName = identity.projectName;
                PlayerSettings.companyName = identity.companyName;
                foreach (BuildTargetGroup group in new[] { BuildTargetGroup.Standalone, BuildTargetGroup.Android, BuildTargetGroup.iOS, BuildTargetGroup.WebGL })
                    PlayerSettings.SetApplicationIdentifier(group, identity.applicationId);
                AssetDatabase.SaveAssets();

                if (PlayerSettings.productName != identity.projectName || PlayerSettings.companyName != identity.companyName ||
                    PlayerSettings.GetApplicationIdentifier(BuildTargetGroup.Android) != identity.applicationId ||
                    PlayerSettings.GetApplicationIdentifier(BuildTargetGroup.iOS) != identity.applicationId)
                    throw new InvalidOperationException("Unity PlayerSettings 身份校验失败，配置文件已保留以供重试。");

                identity.identityApplied = true;
                File.WriteAllText(identityPath, JsonUtility.ToJson(identity, true));
            }

            if (identity.template == "Minimal" && !identity.contentPrepared)
            {
                MinimalProjectSetup.Create();
                identity.contentPrepared = true;
                File.WriteAllText(identityPath, JsonUtility.ToJson(identity, true));
                EditorApplication.delayCall += ApplyIfPending;
                return;
            }
            GamePackageBuildPipeline.Prepare();
            File.Delete(identityPath);
            Debug.Log($"新项目身份与 GamePackage 已准备：{identity.projectName} / {identity.companyName} / {identity.applicationId}");
        }

        /// <summary>
        /// 首次导入时仅在存在待处理配置的情况下应用身份。
        /// </summary>
        private static void ApplyIfPending()
        {
            if (!File.Exists(Path.Combine(GetRepositoryRoot(), IdentityFileName))) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += ApplyIfPending;
                return;
            }
            ApplyAndValidate();
        }

        /// <summary>
        /// 获取包含 DevProject 的项目根目录。
        /// </summary>
        /// <returns>新项目根目录。</returns>
        private static string GetRepositoryRoot()
        {
            DirectoryInfo unityProject = Directory.GetParent(Application.dataPath);
            if (unityProject?.Parent == null || unityProject.Name != "DevProject")
                throw new InvalidOperationException("Unity 工程必须保留在项目根目录的 DevProject 文件夹中。");
            return unityProject.Parent.FullName;
        }

        /// <summary>
        /// 确认复制结果保留启动、输入、数据和依赖的关键文件。
        /// </summary>
        /// <param name="repositoryRoot">新项目根目录。</param>
        /// <param name="minimal">是否采用无演示业务的最小模板。</param>
        private static void ValidateFiles(string repositoryRoot, bool minimal)
        {
            string[] required =
            {
                "DevProject/Assets/Scenes/Main.unity", "DevProject/Assets/Scenes/Loading.unity",
                "DevProject/Assets/Resources/Framework.prefab", "DevProject/Assets/GameResources/Config/GamePackageSettings.json",
                "DevProject/Assets/GameResources/Input/GameInputActions.inputactions",
                "DevProject/Assets/GameResources/Config/Tables/Table_ExampleItems.json",
                "DevProject/Assets/Scripts/GameLogic/Runtime/Gen/GameTables.Gen.cs",
                "DevProject/Assets/Scripts/GameLogic/Runtime/GameLogic.asmdef",
                "DevProject/Packages/manifest.json", "DevProject/Packages/UniTask/package.json",
                "DevProject/Packages/YooAsset/package.json", "DevProject/Packages/HybridCLR/package.json",
                "config/excel/ExampleItems.xlsx", "tool/package-lock.json"
            };
            foreach (string relative in required)
            {
                if (minimal && relative.Contains("ExampleItems")) continue;
                string path = Path.Combine(repositoryRoot, relative);
                if (!File.Exists(path)) throw new FileNotFoundException($"新项目缺少模板关键文件：{relative}", path);
            }
        }

        /// <summary>
        /// 保存由复制脚本生成的一次性身份信息。
        /// </summary>
        [Serializable]
        private sealed class ProjectIdentity
        {
            public string projectName;
            public string companyName;
            public string applicationId;
            public bool identityApplied;
            public string template;
            public bool contentPrepared;
        }
    }
}
