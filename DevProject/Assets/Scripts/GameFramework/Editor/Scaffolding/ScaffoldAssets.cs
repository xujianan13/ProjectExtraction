using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GameFramework.Editor.Scaffolding
{
    /// <summary>
    /// 使用 Unity Editor API 首次创建脚手架内容资产。
    /// </summary>
    internal static class ScaffoldAssets
    {
        /// <summary>
        /// 创建 Prefab 或 Additive 内容场景。
        /// </summary>
        /// <param name="entry">资产描述。</param>
        internal static void Create(ScaffoldEntry entry)
        {
            EnsureFolders(Path.GetDirectoryName(entry.assetPath).Replace('\\', '/'));
            if (entry.kind == "Scene" && entry.contentMode == "UnityScene")
            {
                Scene previous = SceneManager.GetActiveScene();
                bool batchMode = Application.isBatchMode;
                Scene created = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, batchMode ? NewSceneMode.Single : NewSceneMode.Additive);
                try
                {
                    var root = new GameObject(entry.address);
                    if (root.scene != created) SceneManager.MoveGameObjectToScene(root, created);
                    if (!EditorSceneManager.SaveScene(created, entry.assetPath))
                        throw new IOException($"无法保存 Unity 内容场景：{entry.assetPath}");
                }
                finally
                {
                    if (!batchMode)
                    {
                        try { if (created.IsValid() && created.isLoaded) EditorSceneManager.CloseScene(created, true); }
                        finally { if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous); }
                    }
                }
                return;
            }

            GameObject gameObject = null;
            try
            {
                gameObject = entry.kind == "Window" ? CreateWindow(entry) : new GameObject(entry.address);
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(gameObject, entry.assetPath);
                if (prefab == null) throw new IOException($"无法保存 Prefab：{entry.assetPath}");
            }
            finally
            {
                if (gameObject != null) UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }
        /// <summary>
        /// 创建窗口的可编辑 uGUI 层级。
        /// </summary>
        /// <param name="entry">窗口描述。</param>
        /// <returns>窗口根节点。</returns>
        private static GameObject CreateWindow(ScaffoldEntry entry)
        {
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null) throw new InvalidOperationException("无法加载内置字体 LegacyRuntime.ttf。");
            var root = new GameObject(entry.address, typeof(RectTransform));
            RectTransform rootRect = root.GetComponent<RectTransform>();
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = rootRect.offsetMax = Vector2.zero;

            RectTransform panel = CreateRect("Panel", rootRect);
            bool page = entry.preset == "Page";
            panel.anchorMin = page ? Vector2.zero : new Vector2(0.5f, 0.5f);
            panel.anchorMax = page ? Vector2.one : new Vector2(0.5f, 0.5f);
            panel.sizeDelta = page ? Vector2.zero : new Vector2(720f, 480f);
            panel.anchoredPosition = Vector2.zero;
            panel.gameObject.AddComponent<UnityEngine.UI.Image>().color = new Color(0.08f, 0.11f, 0.17f, 0.97f);
            RectTransform titleRect = CreateRect("Title", panel);
            titleRect.anchorMin = titleRect.anchorMax = new Vector2(0.5f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.sizeDelta = new Vector2(500f, 70f);
            titleRect.anchoredPosition = new Vector2(0f, -36f);
            var title = titleRect.gameObject.AddComponent<UnityEngine.UI.Text>();
            title.font = font;
            title.text = entry.name;
            title.fontSize = 32;
            title.fontStyle = FontStyle.Bold;
            title.alignment = TextAnchor.MiddleCenter;
            title.color = Color.white;
            title.raycastTarget = false;

            RectTransform buttonRect = CreateRect("CloseButton", panel);
            buttonRect.anchorMin = buttonRect.anchorMax = Vector2.one;
            buttonRect.pivot = Vector2.one;
            buttonRect.sizeDelta = new Vector2(64f, 64f);
            buttonRect.anchoredPosition = new Vector2(-24f, -24f);
            var image = buttonRect.gameObject.AddComponent<UnityEngine.UI.Image>();
            image.color = new Color(0.22f, 0.28f, 0.37f, 1f);
            var button = buttonRect.gameObject.AddComponent<UnityEngine.UI.Button>();
            button.targetGraphic = image;
            RectTransform labelRect = CreateRect("Label", buttonRect);
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;
            var label = labelRect.gameObject.AddComponent<UnityEngine.UI.Text>();
            label.font = font;
            label.text = "×";
            label.fontSize = 28;
            label.color = Color.white;
            label.alignment = TextAnchor.MiddleCenter;
            label.raycastTarget = false;
            return root;
        }

        /// <summary>
        /// 创建并接入 UI 矩形节点。
        /// </summary>
        /// <param name="name">节点名称。</param>
        /// <param name="parent">父节点。</param>
        /// <returns>新节点的矩形组件。</returns>
        private static RectTransform CreateRect(string name, RectTransform parent)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        /// <summary>
        /// 按层创建 Unity 资产目录，使 Unity 维护目录元数据。
        /// </summary>
        /// <param name="path">待创建目录。</param>
        private static void EnsureFolders(string path)
        {
            string[] segments = path.Split('/');
            string current = segments[0];
            for (int index = 1; index < segments.Length; index++)
            {
                string next = current + "/" + segments[index];
                string fullPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", next));
                if (File.Exists(fullPath)) throw new IOException($"资产目录被普通文件占用：{next}");
                if (!AssetDatabase.IsValidFolder(next))
                {
                    if (Directory.Exists(fullPath)) throw new IOException($"Unity 尚未识别现有资产目录：{next}");
                    AssetDatabase.CreateFolder(current, segments[index]);
                    if (!AssetDatabase.IsValidFolder(next)) throw new IOException($"无法创建资产目录：{next}");
                }
                current = next;
            }
        }
    }
}
