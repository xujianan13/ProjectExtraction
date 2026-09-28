using System;
using GameFramework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MiniGame.Editor
{
    /// <summary>
    /// 创建框架启动预制体，并将其放入开发工程的主场景。
    /// </summary>
    public static class FrameworkPrefabBuilder
    {
        private const string PrefabPath = "Assets/Resources/Framework.prefab";
        private const string ScenePath = "Assets/Scenes/Main.unity";

        /// <summary>
        /// 创建启动预制体并确保主场景引用它。
        /// </summary>
        [MenuItem("Tools/Unity Mini Game Framework/Create Framework Prefab")]
        public static void CreateAndAttach()
        {
            var prefab = EnsurePrefab();
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            if (!scene.IsValid()) throw new InvalidOperationException($"无法打开主场景：{ScenePath}");

            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.GetComponent<FrameworkBootstrap>() == null) continue;
                if (PrefabUtility.GetCorrespondingObjectFromSource(root) != prefab)
                    throw new InvalidOperationException("主场景已有 FrameworkBootstrap，但不是 Framework.prefab 的实例。");
                Debug.Log($"主场景已有框架预制体实例：{ScenePath}");
                return;
            }

            var instance = PrefabUtility.InstantiatePrefab(prefab, scene) as GameObject;
            if (instance == null) throw new InvalidOperationException("Framework.prefab 实例化失败。");
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException($"保存主场景失败：{ScenePath}");
            AssetDatabase.SaveAssets();
            Debug.Log($"已创建框架预制体并挂载到主场景：{PrefabPath}");
        }

        /// <summary>
        /// 确保固定资源目录和框架启动预制体存在。
        /// </summary>
        /// <returns>框架启动预制体资产。</returns>
        public static GameObject EnsurePrefab()
        {
            EnsureFolder("Resources");
            EnsureFolder("GameResources");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab != null)
            {
                if (prefab.GetComponent<FrameworkBootstrap>() == null)
                    throw new InvalidOperationException($"框架预制体缺少 FrameworkBootstrap：{PrefabPath}");
                return prefab;
            }

            var root = new GameObject("Framework");
            root.AddComponent<FrameworkBootstrap>();
            prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            UnityEngine.Object.DestroyImmediate(root);
            if (prefab == null) throw new InvalidOperationException($"创建框架预制体失败：{PrefabPath}");
            AssetDatabase.SaveAssets();
            return prefab;
        }

        /// <summary>
        /// 确保 Assets 下存在指定的资源目录。
        /// </summary>
        /// <param name="folderName">资源目录名称。</param>
        private static void EnsureFolder(string folderName)
        {
            var path = $"Assets/{folderName}";
            if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.CreateFolder("Assets", folderName);
        }
    }
}
