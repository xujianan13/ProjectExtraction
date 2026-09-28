using System;
using System.Collections.Generic;
using MiniGame.Boot;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MiniGame.Editor
{
    /// <summary>
    /// 创建随安装包发布的内容更新加载场景。
    /// </summary>
    public static class LoadingSceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/Loading.unity";
        private const string MainScenePath = "Assets/Scenes/Main.unity";

        /// <summary>
        /// 创建加载场景并加入 Build Settings。
        /// </summary>
        public static void Create()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
            {
                RemoveSceneEventSystem();
                AddBuildScene(MainScenePath);
                AddBuildScene(ScenePath);
                Debug.Log($"加载场景已存在并完成事件系统迁移：{ScenePath}");
                return;
            }

            if (!AssetDatabase.IsValidFolder("Assets/Scenes")) AssetDatabase.CreateFolder("Assets", "Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var canvasObject = new GameObject("Loading Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            var view = canvasObject.AddComponent<LoadingSceneView>();

            var background = CreateRect("Background", canvasObject.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            background.gameObject.AddComponent<Image>().color = new Color(0.055f, 0.075f, 0.12f, 1f);
            var content = CreateRect("Content", canvasObject.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(760f, 380f), Vector2.zero);

            var title = CreateText("Title", content, font, "正在准备游戏", 44, TextAnchor.MiddleCenter, Color.white,
                new Vector2(0f, 110f), new Vector2(740f, 72f));
            title.fontStyle = FontStyle.Bold;
            var status = CreateText("Status", content, font, "正在检查游戏资源…", 25, TextAnchor.MiddleCenter,
                new Color(0.78f, 0.83f, 0.9f), new Vector2(0f, 32f), new Vector2(740f, 60f));

            var track = CreateRect("Progress Track", content, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(640f, 24f),
                new Vector2(0f, -35f));
            track.gameObject.AddComponent<Image>().color = new Color(0.18f, 0.23f, 0.32f);
            var fill = CreateRect("Progress Fill", track, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero).gameObject.AddComponent<Image>();
            fill.color = new Color(0.25f, 0.71f, 0.96f);
            fill.rectTransform.anchorMax = new Vector2(0f, 1f);

            fill.fillAmount = 0f;
            var percentage = CreateText("Progress Text", content, font, "0%", 21, TextAnchor.MiddleCenter,
                new Color(0.78f, 0.83f, 0.9f), new Vector2(0f, -85f), new Vector2(200f, 45f));

            var failurePanel = CreateRect("Failure Panel", content, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(700f, 110f), new Vector2(0f, -150f));
            var failure = CreateText("Failure Message", failurePanel, font, string.Empty, 21, TextAnchor.MiddleCenter,
                new Color(1f, 0.65f, 0.62f), new Vector2(-75f, 0f), new Vector2(520f, 90f));
            var retryRect = CreateRect("Retry Button", failurePanel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(130f, 50f), new Vector2(275f, 0f));
            retryRect.gameObject.AddComponent<Image>().color = new Color(0.21f, 0.49f, 0.76f);
            var retryButton = retryRect.gameObject.AddComponent<Button>();
            retryButton.targetGraphic = retryRect.GetComponent<Image>();
            CreateText("Retry Label", retryRect, font, "重试", 22, TextAnchor.MiddleCenter, Color.white, Vector2.zero, new Vector2(130f, 50f));
            failurePanel.gameObject.SetActive(false);
            view.Bind(status, percentage, fill, failurePanel.gameObject, failure, retryButton);

            if (!EditorSceneManager.SaveScene(scene, ScenePath))
            {
                Debug.LogError($"保存加载场景失败：{ScenePath}");
                EditorApplication.Exit(1);
                return;
            }

            AddBuildScene(MainScenePath);
            AddBuildScene(ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log($"已创建加载场景并加入 Build Settings：{ScenePath}");
        }

        /// <summary>
        /// 创建并定位 UI 矩形区域。
        /// </summary>
        /// <param name="name">对象名称。</param>
        /// <param name="parent">父节点。</param>
        /// <param name="anchorMin">最小锚点。</param>
        /// <param name="anchorMax">最大锚点。</param>
        /// <param name="size">固定尺寸。</param>
        /// <param name="position">锚点偏移。</param>
        /// <returns>创建完成的矩形节点。</returns>
        private static RectTransform CreateRect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 size, Vector2 position)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            return rect;
        }

        /// <summary>
        /// 创建加载界面的文字控件。
        /// </summary>
        /// <param name="name">对象名称。</param>
        /// <param name="parent">父节点。</param>
        /// <param name="font">使用的字体。</param>
        /// <param name="content">初始文字。</param>
        /// <param name="fontSize">字号。</param>
        /// <param name="alignment">文字对齐方式。</param>
        /// <param name="color">文字颜色。</param>
        /// <param name="position">锚点偏移。</param>
        /// <param name="size">固定尺寸。</param>
        /// <returns>创建完成的文字控件。</returns>
        private static Text CreateText(string name, Transform parent, Font font, string content, int fontSize, TextAnchor alignment,
            Color color, Vector2 position, Vector2 size)
        {
            var text = CreateRect(name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), size, position).gameObject.AddComponent<Text>();
            text.font = font;
            text.text = content;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = color;
            text.raycastTarget = false;
            return text;
        }

        /// <summary>
        /// 确保构建配置包含指定场景，同时保留已有场景。
        /// </summary>
        /// <param name="path">需要加入的场景路径。</param>
        private static void AddBuildScene(string path)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null) return;
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            foreach (var scene in scenes)
            {
                if (string.Equals(scene.path, path, StringComparison.OrdinalIgnoreCase)) return;
            }

            scenes.Add(new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        /// <summary>
        /// 删除旧版加载场景中的独立事件系统，改用框架常驻的事件系统。
        /// </summary>
        private static void RemoveSceneEventSystem()
        {
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            bool openedHere = !scene.IsValid() || !scene.isLoaded;
            if (openedHere) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            bool changed = false;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.GetComponent<EventSystem>() == null) continue;
                UnityEngine.Object.DestroyImmediate(root);
                changed = true;
            }
            if (changed) EditorSceneManager.SaveScene(scene);
            if (openedHere) EditorSceneManager.CloseScene(scene, true);
        }
    }
}
