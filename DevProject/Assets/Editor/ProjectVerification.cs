using System;
using System.IO;
using GameFramework;
using GameFramework.Editor.ApiIndex;
using GameFramework.Editor.Scaffolding;
using MiniGame.Boot;
using UnityEditor;
using UnityEngine;

namespace MiniGame.Editor
{
    /// <summary>
    /// 统一命令行验收使用的 Editor 入口。
    /// </summary>
    public static class ProjectVerification
    {
        /// <summary>
        /// 完成新模板待处理的初始化；自动初始化已经完成时不重复生成内容。
        /// </summary>
        public static void InitializeTemplate()
        {
            string identity = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.project-identity.json"));
            if (File.Exists(identity)) ProjectTemplateSetup.ApplyAndValidate();
        }

        /// <summary>
        /// 校验已编译项目的导表、脚手架、API 索引及首次导入状态。
        /// </summary>
        public static void Validate()
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
            if (File.Exists(Path.Combine(root, ".project-identity.json")))
                throw new InvalidOperationException("新项目首次初始化尚未完成，请先完成导入和编译。");
            ExcelTableExportMenu.EnsureEditorReady();
            ExcelTableExportMenu.Verify();
            ScaffoldResult result = ScaffoldGenerator.Validate();
            if (!result.success) throw new InvalidOperationException(string.Join("; ", result.errors));
            if (!FrameworkApiIndex.Check()) throw new InvalidOperationException("API 索引不一致，请检查公开 API 后刷新索引。");
            Debug.Log("统一项目校验通过。");
        }

        /// <summary>
        /// 在验证副本中设置离线模式并构建新版本内容包。
        /// </summary>
        public static void BuildOffline()
        {
            EnsureVerificationCopy();
            var boot = new SerializedObject(AssetDatabase.LoadAssetAtPath<HotUpdateBootSettings>("Assets/Resources/HotUpdateBootSettings.asset"));
            boot.FindProperty("playMode").enumValueIndex = (int)HotUpdatePlayMode.Offline;
            boot.ApplyModifiedPropertiesWithoutUndo();
            var settings = new SerializedObject(GamePackageBuildSettings.LoadOrCreate());
            settings.FindProperty("_packageVersion").stringValue = "verify-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
            settings.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            GamePackageBuildPipeline.BuildOffline();
        }

        /// <summary>
        /// 为独立 Player 构建准备新的内容版本，随后调用正式构建入口。
        /// </summary>
        public static void BuildWindows()
        {
            EnsureVerificationCopy();
            var settings = new SerializedObject(GamePackageBuildSettings.LoadOrCreate());
            settings.FindProperty("_packageVersion").stringValue = "verify-player-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
            settings.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            ProjectBuildPipeline.BuildWindowsOffline();
        }

        /// <summary>
        /// 限制会修改发布设置的验收命令只作用于工具创建的隔离副本。
        /// </summary>
        private static void EnsureVerificationCopy()
        {
            string marker = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.framework-verification-copy"));
            if (!File.Exists(marker) || File.ReadAllText(marker).Trim() != "UnityMiniGameFramework verification copy v1")
                throw new InvalidOperationException("此命令仅供统一验收工具创建的隔离副本使用。");
        }
    }
}
