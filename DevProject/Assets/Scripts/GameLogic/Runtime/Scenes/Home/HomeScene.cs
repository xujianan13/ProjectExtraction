using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;
using GameLogic.Project;
using UnityEngine.SceneManagement;
using YooAsset;

namespace GameLogic.Scenes.Home
{
    /// <summary>
    /// Home逻辑场景；内容和玩法均由本轮进入持有。
    /// </summary>
    public sealed class HomeScene : SceneBase, IProjectHomeReady
    {
        /// <summary>
        /// 当前内容、玩法和窗口是否已经完成绑定。
        /// </summary>
        public bool IsReady { get; private set; }

        private LifetimeScope ownedScope;
        private IGameplayModule gameplays;
        private GameplayHandle gameplayHandle;
        private IUIModule ui;
        private bool openedWindow;
        private PrefabPool pool;
        private PrefabInstance instance;
        private SceneHandle sceneHandle;

        /// <summary>
        /// 创建逻辑场景。
        /// </summary>
        public HomeScene() : base(GameLogic.Scaffold.ContentIds.HomeScene) { }

        /// <summary>
        /// 加载内容并进入关联玩法。
        /// </summary>
        /// <param name="enterData">场景进入参数。</param>
        /// <param name="ct">本轮取消令牌。</param>
        /// <returns>场景进入任务。</returns>
        protected override async UniTask OnEnterSceneAsync(SceneEnterData enterData, CancellationToken ct)
        {
            IsReady = false;
            LifetimeScope scope = SceneScope;
            ownedScope = scope;
            bool entered = false;
            try
            {
                PrefabPool created = await GameEntry.Get<IPrefabPoolModule>().CreatePoolAsync(
                    GameLogic.Scaffold.ContentAddresses.HomeScene, ct: ct);
                if (created == null) throw new InvalidOperationException("无法创建Home场景内容池。");
                if (!ReferenceEquals(ownedScope, scope) || !scope.IsActive)
                {
                    await created.DisposeAsync();
                    return;
                }
                pool = created;
                instance = pool.Spawn();
                if (instance == null) throw new InvalidOperationException("无法生成Home场景内容。");
                if (!ReferenceEquals(ownedScope, scope) || !scope.IsActive) return;
                gameplays = GameEntry.Get<IGameplayModule>();
                GameplayHandle started = await gameplays.StartAsync(
                    GameLogic.Scaffold.ProjectContent.CreateMainGameplay(), ct: ct);
                if (!ReferenceEquals(ownedScope, scope) || !scope.IsActive)
                {
                    if (started.IsValid) await gameplays.StopAsync(started);
                    return;
                }
                gameplayHandle = started;
                if (!ReferenceEquals(ownedScope, scope) || !scope.IsActive) return;
                ui = GameEntry.Get<IUIModule>();
                if (await ui.OpenWindowAsync(GameLogic.Scaffold.ContentIds.HomeWindow, ct: ct) == null)
                    throw new InvalidOperationException("无法创建 Home 主界面。");
                openedWindow = true;
                if (!ReferenceEquals(ownedScope, scope) || !scope.IsActive) return;
                IsReady = true;
                entered = true;
            }
            finally
            {
                if (!entered) await CleanupAsync(scope);
            }
        }

        /// <summary>
        /// 等待本轮关联玩法和内容退出。
        /// </summary>
        /// <param name="ct">独立退出令牌。</param>
        /// <returns>退出任务。</returns>
        protected override UniTask OnExitSceneAsync(CancellationToken ct) => CleanupAsync(ownedScope);

        /// <summary>
        /// 强制停止当前轮次并安排异步资源清理。
        /// </summary>
        protected override void OnForceExit()
        {
            IsReady = false;
            GameplayHandle handle = gameplayHandle;
            IGameplayModule currentGameplays = gameplays;
            PrefabInstance currentInstance = instance;
            PrefabPool currentPool = pool;
            SceneHandle currentScene = sceneHandle;
            IUIModule currentUi = ui;
            bool closeWindow = openedWindow;
            ownedScope = null;
            gameplayHandle = GameplayHandle.Invalid;
            gameplays = null;
            instance = null;
            pool = null;
            sceneHandle = null;
            ui = null;
            openedWindow = false;
            try
            {
                if (closeWindow && currentUi != null)
                    currentUi.CloseWindowAsync(GameLogic.Scaffold.ContentIds.HomeWindow, GameWindowCloseReason.ScopeExit, CancellationToken.None).Forget();
            }
            finally
            {
                try
                {
                    if (handle.IsValid && currentGameplays != null) currentGameplays.Stop(handle);
                }
                finally
                {
                    try
                    {
                        currentInstance?.Dispose();
                    }
                    finally
                    {
                        try
                        {
                            currentPool?.Dispose();
                        }
                        finally
                        {
                            if (currentScene != null) currentScene.UnloadSceneAsync();
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 清理指定轮次拥有的对象和租约。
        /// </summary>
        /// <param name="scope">进入时捕获的作用域。</param>
        /// <returns>完成释放后的任务。</returns>
        private async UniTask CleanupAsync(LifetimeScope scope)
        {
            if (!ReferenceEquals(ownedScope, scope)) return;
            IsReady = false;
            GameplayHandle handle = gameplayHandle;
            IGameplayModule currentGameplays = gameplays;
            PrefabInstance currentInstance = instance;
            PrefabPool currentPool = pool;
            SceneHandle currentScene = sceneHandle;
            IUIModule currentUi = ui;
            bool closeWindow = openedWindow;
            ownedScope = null;
            gameplayHandle = GameplayHandle.Invalid;
            gameplays = null;
            instance = null;
            pool = null;
            sceneHandle = null;
            ui = null;
            openedWindow = false;
            try
            {
                if (closeWindow && currentUi != null)
                    await currentUi.CloseWindowAsync(GameLogic.Scaffold.ContentIds.HomeWindow, GameWindowCloseReason.ScopeExit, CancellationToken.None);
            }
            finally
            {
                try
                {
                    if (handle.IsValid && currentGameplays != null)
                        await currentGameplays.StopAsync(handle, CancellationToken.None);
                }
                finally
                {
                    try
                    {
                        currentInstance?.Dispose();
                    }
                    finally
                    {
                        try
                        {
                            if (currentPool != null) await currentPool.DisposeAsync();
                        }
                        finally
                        {
                            if (currentScene != null)
                            {
                                var operation = currentScene.UnloadSceneAsync();
                                await UniTask.WaitUntil(() => operation.IsDone);
                            }
                        }
                    }
                }
            }
        }
    }
}
