using System;
using MiniGame.Boot;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MiniGame.Editor
{
    /// <summary>
    /// 为示例工程创建固定启动配置并接入 Loading 场景。
    /// </summary>
    public static class HotUpdateProjectSetup
    {
        private const string MainScenePath = "Assets/Scenes/Main.unity";
        private const string SettingsPath = "Assets/Resources/HotUpdateBootSettings.asset";

        /// <summary>
        /// 创建加载场景、宿主配置和主场景启动组件。
        /// </summary>
        [MenuItem("Tools/Unity Mini Game Framework/Set Up Hot Update Boot")]
        public static void Create()
        {
            LoadingSceneBuilder.Create();
            if (AssetDatabase.LoadAssetAtPath<HotUpdateBootSettings>(SettingsPath) == null)
            {
                var settings = ScriptableObject.CreateInstance<HotUpdateBootSettings>();
                AssetDatabase.CreateAsset(settings, SettingsPath);
            }

            var scene = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
            if (!scene.IsValid()) throw new InvalidOperationException("无法打开 Main.unity。");
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i].name == "[MiniGameBootstrap]") UnityEngine.Object.DestroyImmediate(roots[i]);
            }

            if (GameObject.Find("[HotUpdateBootstrap]") == null)
            {
                var bootstrap = new GameObject("[HotUpdateBootstrap]");
                bootstrap.AddComponent<HotUpdateBootstrap>();
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("热更启动已接入 Main.unity；Loading.unity 和 HotUpdateBootSettings.asset 已准备。");
        }
    }
}
