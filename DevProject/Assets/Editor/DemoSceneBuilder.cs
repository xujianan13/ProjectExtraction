using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MiniGame.Editor
{
    /// <summary>
    /// 创建框架首版的中性 3D 演示场景。
    /// </summary>
    public static class DemoSceneBuilder
    {
        /// <summary>
        /// 创建主场景并将其设为构建入口。
        /// </summary>
        public static void Create()
        {
            const string scenePath = "Assets/Scenes/Main.unity";

            if (!AssetDatabase.IsValidFolder("Assets/Scenes")) AssetDatabase.CreateFolder("Assets", "Scenes");
            var frameworkPrefab = FrameworkPrefabBuilder.EnsurePrefab();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            if (PrefabUtility.InstantiatePrefab(frameworkPrefab, scene) == null)
            {
                Debug.LogError("Framework.prefab 实例化失败。");
                EditorApplication.Exit(1);
                return;
            }

            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.transform.position = new Vector3(0f, 2.5f, -7f);
            camera.transform.LookAt(new Vector3(0f, 0.5f, 0f));
            cameraObject.AddComponent<AudioListener>();

            var lightObject = new GameObject("Directional Light");
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            lightObject.transform.rotation = Quaternion.Euler(35f, -30f, 0f);

            if (!EditorSceneManager.SaveScene(scene, scenePath))
            {
                Debug.LogError($"保存演示场景失败：{scenePath}");
                EditorApplication.Exit(1);
                return;
            }

            HotUpdateProjectSetup.Create();
            Debug.Log($"已创建演示场景并接入热更启动：{scenePath}");
        }
    }
}
