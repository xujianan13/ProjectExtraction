#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;
using GameLogic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace MiniGame.Tests
{
    /// <summary>
    /// 在生成内容的项目副本中验证真实启动、异步加载和退出释放。
    /// </summary>
    public sealed class ScaffoldContentPlayModeTests
    {
        private bool started;

        /// <summary>
        /// 反复进入生成场景，检查窗口、玩法、实例池和场景句柄的完整生命周期。
        /// </summary>
        /// <returns>集成验证协程。</returns>
        [UnityTest]
        public IEnumerator GeneratedScenesLoadCancelReenterAndRelease() => UniTask.ToCoroutine(async () =>
        {
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "../../config/scaffold/project.json"));
            Assert.IsTrue(File.Exists(path), "缺少脚手架项目描述。");
            var project = JsonUtility.FromJson<ProjectDescription>(File.ReadAllText(path));
            if (project.entries.Length == 0 || string.IsNullOrEmpty(project.startupScene))
                Assert.Ignore("请在验证副本应用 config/scaffold/example-request.json 后执行内容集成测试。");

            started = true;
            await SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            await WaitAsync(() => GameEntry.IsRunning && GameEntry.Get<ISceneModule>()?.CurrentScene?.IsActive == true &&
                GameMain.IsReady && !SceneManager.GetSceneByName("Loading").isLoaded, "脚手架首场景未就绪。");
            var scenes = GameEntry.Get<ISceneModule>();
            var ui = GameEntry.Get<IUIModule>();
            Assert.AreEqual(project.startupScene, scenes.CurrentScene.Name);
            await scenes.ExitCurrentSceneAsync();
            await WaitEmptyAsync();
            int baselineLeases = ActiveLeases();

            Entry unityScene = project.entries.FirstOrDefault(entry => entry.kind == "Scene" && entry.contentMode == "UnityScene");
            if (unityScene != null)
            {
                using var cancellation = new CancellationTokenSource();
                UniTask enter = scenes.ChangeSceneAsync(unityScene.name, ct: cancellation.Token);
                cancellation.Cancel();
                Assert.IsTrue(await enter.SuppressCancellationThrow(), "进入中的取消应由场景模块传播。");
                await WaitEmptyAsync();
                await WaitAsync(() => !SceneManager.GetSceneByPath(unityScene.assetPath).isLoaded, "取消后遗留 Unity 内容场景。");
                Assert.AreEqual(baselineLeases, ActiveLeases(), "取消后资源租约未回到基线。");
            }

            foreach (Entry entry in project.entries.Where(item => item.kind == "Scene"))
            {
                for (int round = 0; round < 2; round++)
                {
                    await scenes.ChangeSceneAsync(entry.name);
                    var scene = (SceneBase)scenes.CurrentScene;
                    LifetimeScope scope = scene.SceneScope;
                    Assert.IsTrue(scope.IsActive);
                    Assert.AreEqual(string.IsNullOrEmpty(entry.gameplay) ? 0 : 1, GameEntry.Get<IGameplayModule>().Count);
                    if (entry.contentMode == "UnityScene")
                        Assert.IsTrue(SceneManager.GetSceneByPath(entry.assetPath).isLoaded, "Unity 内容场景应真实加载。");

                    if (!string.IsNullOrEmpty(entry.window))
                    {
                        Entry window = project.entries.Single(item => item.kind == "Window" && item.name == entry.window);
                        GameWindow opened = ui.OpenedWindows.Single(item => item.Id == window.windowId);
                        Assert.AreSame(opened, await ui.OpenWindowAsync(window.windowId), "重复打开应聚焦同一窗口。");
                        Assert.IsNotNull(opened.RootTransform.Find("Panel/Title"));
                        Assert.IsNotNull(opened.RootTransform.Find("Panel/CloseButton"));
                        Assert.IsTrue(await ui.CloseWindowAsync(window.windowId));
                        Assert.AreEqual(GameWindowState.Destroyed, opened.State, "默认立即释放的窗口应销毁。");
                        await ui.OpenWindowAsync(window.windowId);
                    }

                    await scenes.ExitCurrentSceneAsync();
                    await WaitEmptyAsync();
                    Assert.AreEqual(LifetimeScopeState.Disposed, scope.State);
                    Assert.AreEqual(0, scope.RegistrationCount);
                    Assert.AreEqual(baselineLeases, ActiveLeases(), "退出后资源租约未回到基线。");
                    if (entry.contentMode == "UnityScene")
                        Assert.IsFalse(SceneManager.GetSceneByPath(entry.assetPath).isLoaded, "退出后内容场景应卸载。");
                }
            }

            foreach (Entry entry in project.entries.Where(item => item.kind == "Scene"))
            {
                await scenes.ChangeSceneAsync(entry.name);
                var scene = (SceneBase)scenes.CurrentScene;
                using var exitCancellation = new CancellationTokenSource();
                exitCancellation.Cancel();
                await scene.ExitSceneAsync(exitCancellation.Token);
                await scenes.ExitCurrentSceneAsync();
                await WaitEmptyAsync();
                Assert.AreEqual(baselineLeases, ActiveLeases(), "退出令牌已取消时也必须完成资源释放。");
                if (entry.contentMode == "UnityScene")
                    Assert.IsFalse(SceneManager.GetSceneByPath(entry.assetPath).isLoaded);
            }
            foreach (Entry window in project.entries.Where(entry => entry.kind == "Window"))
            {
                GameWindow opened = await ui.OpenWindowAsync(window.windowId);
                Assert.AreEqual(window.preset == "Modal", opened.Config.UseMask);
                Assert.IsTrue(await ui.CloseWindowAsync(window.windowId));
            }
            await WaitEmptyAsync();
            Assert.AreEqual(baselineLeases, ActiveLeases());
        });

        /// <summary>
        /// 等待内容句柄清空，允许 Unity 完成本帧延迟销毁。
        /// </summary>
        /// <returns>清理等待任务。</returns>
        private static async UniTask WaitEmptyAsync()
        {
            await WaitAsync(() => GameEntry.Get<IGameplayModule>().Count == 0 && GameEntry.Get<IPrefabPoolModule>().Count == 0 &&
                GameEntry.Get<IUIModule>().OpenedWindows.Count == 0, "退出后仍存在玩法、实例池或窗口。");
            await UniTask.Yield();
            await UniTask.Yield();
        }

        /// <summary>
        /// 读取统一诊断中的活动资源租约数量。
        /// </summary>
        /// <returns>当前活动租约数量。</returns>
        private static int ActiveLeases()
        {
            DiagnosticSection resource = GameEntry.Get<IDiagnosticsModule>().CaptureSnapshot().Sections.Single(item => item.Name == "Resource");
            return int.Parse(resource.Values.Single(item => item.Name == "ActiveLeases").Value);
        }

        /// <summary>
        /// 在有界时间内等待框架状态。
        /// </summary>
        /// <param name="ready">完成条件。</param>
        /// <param name="message">超时断言说明。</param>
        /// <returns>异步等待任务。</returns>
        private static async UniTask WaitAsync(Func<bool> ready, string message)
        {
            float deadline = Time.realtimeSinceStartup + 60f;
            while (!ready() && Time.realtimeSinceStartup < deadline) await UniTask.Yield();
            Assert.IsTrue(ready(), message);
        }

        /// <summary>
        /// 关闭测试启动的内容与宿主，避免影响其他测试。
        /// </summary>
        /// <returns>清理协程。</returns>
        [UnityTearDown]
        public IEnumerator TearDown() => UniTask.ToCoroutine(async () =>
        {
            if (!started) return;
            if (GameEntry.IsRunning)
            {
                await GameEntry.Get<ISceneModule>().ExitCurrentSceneAsync();
                await GameEntry.Get<IUIModule>().DestroyWindowScopeAsync(GameWindowScope.Scene);
                await GameEntry.Get<IUIModule>().DestroyWindowScopeAsync(GameWindowScope.Session);
            }
            foreach (string name in new[] { "[HotUpdateBootstrap]", "Framework" })
            {
                GameObject instance = GameObject.Find(name);
                if (instance != null) UnityEngine.Object.Destroy(instance);
            }
            await UniTask.Yield();
            GameEntry.StopFramework();
            GameEntry host = UnityEngine.Object.FindObjectOfType<GameEntry>();
            if (host != null) UnityEngine.Object.Destroy(host.gameObject);
            await UniTask.Yield();
            Scene main = SceneManager.GetSceneByName("Main");
            if (main.IsValid() && main.isLoaded)
            {
                Scene cleanup = SceneManager.CreateScene("ScaffoldTestCleanup");
                SceneManager.SetActiveScene(cleanup);
                await SceneManager.UnloadSceneAsync(main);
            }
            started = false;
        });

        /// <summary>
        /// 读取测试所需的项目描述字段。
        /// </summary>
        [Serializable]
        private sealed class ProjectDescription
        {
            public string startupScene = string.Empty;
            public Entry[] entries = Array.Empty<Entry>();
        }

        /// <summary>
        /// 读取生成内容和关联字段。
        /// </summary>
        [Serializable]
        private sealed class Entry
        {
            public string kind = string.Empty;
            public string name = string.Empty;
            public string contentMode = string.Empty;
            public string assetPath = string.Empty;
            public string gameplay = string.Empty;
            public string window = string.Empty;
            public string preset = string.Empty;
            public int windowId;
        }
    }
}
#endif
