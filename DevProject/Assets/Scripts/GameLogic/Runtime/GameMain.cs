using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;
using GameLogic.Scaffold;

namespace GameLogic
{
    /// <summary>
    /// 最小项目的热更入口；在这里接入当前游戏自己的服务与存档。
    /// </summary>
    public static class GameMain
    {
        /// <summary>
        /// 项目是否已经完成启动收尾。
        /// </summary>
        public static bool IsReady { get; private set; }

        /// <summary>
        /// 初始化项目内容并进入配置的逻辑首场景。
        /// </summary>
        /// <param name="settings">宿主已经验证的内容包配置。</param>
        /// <param name="ct">启动取消令牌。</param>
        /// <returns>项目内容准备任务。</returns>
        public static async UniTask StartAsync(PackageRuntimeSettings settings, CancellationToken ct)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            ct.ThrowIfCancellationRequested();
            IsReady = false;
            GameEntry.ApplicationScope.Defer(() => IsReady = false);
            if (!await GameEntry.Get<IInputModule>().LoadGameplayActionsAsync("GameInputActions", ct))
                throw new InvalidOperationException("无法加载项目输入动作。");
            if (!await GameLogic.Project.ProjectServices.InitializeAsync(ct))
                throw new InvalidOperationException("项目配置或存档初始化失败，停止进入玩法。");
            ProjectScaffoldBridge.Register(GameEntry.Get<ISceneModule>(), GameEntry.Get<IUIModule>());
            if (string.IsNullOrWhiteSpace(ProjectScaffoldBridge.StartupScene) || !string.IsNullOrWhiteSpace(settings.StartupSceneLocation))
                throw new InvalidOperationException("最小项目需要唯一的脚手架逻辑首场景。");
            await GameLogic.Project.ProjectServices.Coordinator.InitializeAsync();
            ct.ThrowIfCancellationRequested();
            if (!GameStartupCompletion.Register(CompleteStartupAsync))
                throw new InvalidOperationException("项目启动收尾已经登记。");
        }

        /// <summary>
        /// 在宿主确认内容包可用后开放游戏交互，并允许移动端在竖屏启动后自动旋转。
        /// </summary>
        /// <param name="ct">启动取消令牌。</param>
        /// <returns>启动收尾结果。</returns>
        private static UniTask<GameStartupCompletionResult> CompleteStartupAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (UnityEngine.Application.isMobilePlatform) UnityEngine.Screen.orientation = UnityEngine.ScreenOrientation.AutoRotation;
            IsReady = true;
            return UniTask.FromResult(new GameStartupCompletionResult(true));
        }
    }
}
