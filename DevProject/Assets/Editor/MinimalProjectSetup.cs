using System;
using System.IO;
using GameFramework.Editor.Scaffolding;
using UnityEditor;
using UnityEngine;

namespace MiniGame.Editor
{
    /// <summary>
    /// 通过正式脚手架创建最小项目的空玩法、首页窗口和首场景。
    /// </summary>
    public static class MinimalProjectSetup
    {
        /// <summary>
        /// 在新项目首次导入期间生成起始内容；后续编译完成后再准备资源包。
        /// </summary>
        public static void Create()
        {
            string repository = Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
            var project = JsonUtility.FromJson<ScaffoldProject>(File.ReadAllText(Path.Combine(repository, "config/scaffold/project.json")));
            if (project.entries.Count == 0)
            {
                var request = new ScaffoldRequest
                {
                    setStartup = true, startupScene = "Home",
                    entries = new[]
                    {
                        new ScaffoldEntry { kind = "Gameplay", name = "Main" },
                        new ScaffoldEntry { kind = "Window", name = "Home", preset = "Page" },
                        new ScaffoldEntry { kind = "Scene", name = "Home", gameplay = "Main", window = "Home" }
                    }
                };
                ScaffoldResult result = ScaffoldGenerator.Run(request);
                if (!result.success) throw new InvalidOperationException("最小项目内容生成失败：" + string.Join("; ", result.errors));
            }
            DemoSceneBuilder.Create();
            AssetDatabase.SaveAssets();
            Debug.Log("最小项目内容已生成，等待 Unity 编译后准备 GamePackage。");
        }
    }
}
