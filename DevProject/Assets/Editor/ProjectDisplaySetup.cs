using System.IO;
using GameFramework;
using UnityEditor;
using UnityEngine;

namespace MiniGame.Editor
{
    /// <summary>
    /// 使用编辑器接口应用本项目的竖屏起步、横屏兼容与安全区配置。
    /// </summary>
    public static class ProjectDisplaySetup
    {
        private const string HomePath = "Assets/GameResources/UI/Home/UI_Home.prefab";
        private const string ControlsPath = "Assets/GameResources/Input/UI_VirtualControls.prefab";

        /// <summary>
        /// 应用开发期显示默认值，并保留模板已有的窗口与输入绑定。
        /// </summary>
        [MenuItem("Tools/ProjectExtraction/Apply Display Defaults")]
        public static void Apply()
        {
            GameObject homeAsset = AssetDatabase.LoadAssetAtPath<GameObject>(HomePath);
            GameObject controlsAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ControlsPath);
            if (homeAsset == null || homeAsset.transform.Find("Panel/Title") == null ||
                homeAsset.transform.Find("Panel/CloseButton") == null || controlsAsset == null ||
                controlsAsset.GetComponent<Canvas>() == null)
            {
                Debug.LogError("显示配置所需的首页或虚拟控件结构不存在，请先完成 Minimal 初始化。");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }

            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.allowedAutorotateToPortrait = true;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.defaultIsNativeResolution = false;
            PlayerSettings.defaultScreenWidth = 1080;
            PlayerSettings.defaultScreenHeight = 1920;
            PlayerSettings.defaultWebScreenWidth = 1080;
            PlayerSettings.defaultWebScreenHeight = 1920;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;

            GameObject home = PrefabUtility.LoadPrefabContents(HomePath);
            Transform panel = home.transform.Find("Panel");
            if (panel.GetComponent<GameSafeAreaFitter>() == null) panel.gameObject.AddComponent<GameSafeAreaFitter>();
            var background = home.GetComponent<UnityEngine.UI.Image>();
            if (background == null) background = home.AddComponent<UnityEngine.UI.Image>();
            background.color = panel.GetComponent<UnityEngine.UI.Image>().color;
            background.raycastTarget = false;
            RectTransform title = (RectTransform)panel.Find("Title");
            title.anchorMin = new Vector2(0f, 1f);
            title.anchorMax = Vector2.one;
            title.offsetMin = new Vector2(112f, -106f);
            title.offsetMax = new Vector2(-112f, -36f);
            PrefabUtility.SaveAsPrefabAsset(home, HomePath, out bool homeSaved);
            PrefabUtility.UnloadPrefabContents(home);

            GameObject controls = PrefabUtility.LoadPrefabContents(ControlsPath);
            UnityEngine.UI.CanvasScaler scaler = controls.GetComponent<UnityEngine.UI.CanvasScaler>();
            if (scaler == null) scaler = controls.AddComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.screenMatchMode = UnityEngine.UI.CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            Transform safeArea = controls.transform.Find("SafeArea");
            if (safeArea == null)
            {
                safeArea = new GameObject("SafeArea", typeof(RectTransform)).transform;
                safeArea.SetParent(controls.transform, false);
                for (int index = controls.transform.childCount - 1; index >= 0; index--)
                {
                    Transform child = controls.transform.GetChild(index);
                    if (child != safeArea) child.SetParent(safeArea, false);
                }
            }
            if (safeArea.GetComponent<GameSafeAreaFitter>() == null) safeArea.gameObject.AddComponent<GameSafeAreaFitter>();
            PrefabUtility.SaveAsPrefabAsset(controls, ControlsPath, out bool controlsSaved);
            PrefabUtility.UnloadPrefabContents(controls);
            AssetDatabase.SaveAssets();
            if (!homeSaved || !controlsSaved)
            {
                Debug.LogError("显示配置保存失败，请检查首页与虚拟控件资源。");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }
            WriteReport();
        }

        /// <summary>
        /// 记录当前项目身份、显示设置和构建模块可用性，便于复核安装结果。
        /// </summary>
        public static void WriteReport()
        {
            string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../../verification"));
            Directory.CreateDirectory(directory);
            BuildTargetGroup[] groups = { BuildTargetGroup.Standalone, BuildTargetGroup.Android, BuildTargetGroup.iOS, BuildTargetGroup.WebGL };
            var report = new DisplayReport
            {
                unityVersion = Application.unityVersion,
                company = PlayerSettings.companyName,
                product = PlayerSettings.productName,
                identifiers = System.Array.ConvertAll(groups, PlayerSettings.GetApplicationIdentifier),
                orientation = PlayerSettings.defaultInterfaceOrientation.ToString(),
                desktopWidth = PlayerSettings.defaultScreenWidth,
                desktopHeight = PlayerSettings.defaultScreenHeight,
                resizableWindow = PlayerSettings.resizableWindow,
                androidModule = BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android),
                iosModule = BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.iOS, BuildTarget.iOS),
                webglModule = BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.WebGL, BuildTarget.WebGL),
                windowsModule = BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64),
                renderPipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline == null ? "Built-in" : "Custom"
            };
            File.WriteAllText(Path.Combine(directory, "project-display.json"), JsonUtility.ToJson(report, true));
            Debug.Log("ProjectExtraction 显示配置已保存：1080×1920 竖屏启动、启动完成后移动端自动旋转、PC 窗口可调整、安全区已接入。");
        }

        /// <summary>
        /// 保存编辑器读取的开发配置与模块状态。
        /// </summary>
        [System.Serializable]
        private sealed class DisplayReport
        {
            public string unityVersion, company, product, orientation, renderPipeline;
            public string[] identifiers;
            public int desktopWidth, desktopHeight;
            public bool resizableWindow, androidModule, iosModule, webglModule, windowsModule;
        }
    }
}