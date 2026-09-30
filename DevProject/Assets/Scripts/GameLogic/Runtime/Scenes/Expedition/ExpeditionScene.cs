using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;
using GameLogic.Gameplay.Expedition;
using GameLogic.Project;

namespace GameLogic.Scenes.Expedition
{
    /// <summary>
    /// 关卡逻辑场景，持有本次地图表现、输入上下文与战斗玩法。
    /// </summary>
    public sealed class ExpeditionScene : SceneBase
    {
        private GameplayHandle gameplay;
        private ExpeditionPresentation presentation;
        private ExpeditionRun run;

        /// <summary>
        /// 创建关卡逻辑场景。
        /// </summary>
        public ExpeditionScene() : base(ProjectServices.BattleSceneName) { }

        /// <summary>
        /// 接入已经完成入场事务的单局。
        /// </summary>
        /// <param name="enterData">场景附加参数。</param>
        /// <param name="ct">本次进入取消令牌。</param>
        /// <returns>场景准备任务。</returns>
        protected override async UniTask OnEnterSceneAsync(SceneEnterData enterData, CancellationToken ct)
        {
            run = ProjectServices.Session?.CurrentRun;
            if (run == null)
            {
                throw new System.InvalidOperationException("没有已提交的单局，拒绝进入战斗场景。");
            }
            LifetimeScope scope = SceneScope;
            run.AttachScene();
            presentation = new ExpeditionPresentation(run);
            var inputContext = GameEntry.Get<IInputModule>().PushContext("Gameplay");
            scope.Defer(inputContext.Dispose);
            gameplay = await GameEntry.Get<IGameplayModule>().StartAsync(new ExpeditionGameplay(run, presentation), ct: ct);
            if (!scope.IsActive)
            {
                if (gameplay.IsValid) await GameEntry.Get<IGameplayModule>().StopAsync(gameplay);
                gameplay = GameplayHandle.Invalid;
                return;
            }
            await GameEntry.Get<IUIModule>().OpenWindowAsync(ProjectServices.BattleHudWindow, new GameWindowOpenArgs(run), ct);
        }

        /// <summary>
        /// 先停止模拟和界面，再释放场景表现与战斗数据。
        /// </summary>
        /// <param name="ct">退出阶段取消令牌。</param>
        /// <returns>退出任务。</returns>
        protected override async UniTask OnExitSceneAsync(CancellationToken ct)
        {
            await GameEntry.Get<IUIModule>().CloseWindowAsync(ProjectServices.BattleHudWindow, GameWindowCloseReason.ScopeExit);
            if (gameplay.IsValid) await GameEntry.Get<IGameplayModule>().StopAsync(gameplay);
            Cleanup();
        }

        /// <summary>
        /// 强制退出仍然先停止唯一模拟入口。
        /// </summary>
        protected override void OnForceExit()
        {
            if (gameplay.IsValid) GameEntry.Get<IGameplayModule>().Stop(gameplay);
            Cleanup();
        }

        /// <summary>
        /// 释放本次场景拥有的资源。
        /// </summary>
        private void Cleanup()
        {
            gameplay = GameplayHandle.Invalid;
            presentation?.Dispose();
            presentation = null;
            run?.Dispose();
            run = null;
        }
    }
}
