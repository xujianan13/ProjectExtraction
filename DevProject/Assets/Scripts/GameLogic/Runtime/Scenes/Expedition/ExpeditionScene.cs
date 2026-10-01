using System;
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
        private bool openingRogueChoice;

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
            ExpeditionRun current = ProjectServices.Session?.CurrentRun;
            if (enterData is not ProjectRunEnterData request || current == null ||
                request.RunId != current.RunId || request.LevelId != current.LevelId ||
                request.OperationId != ProjectServices.Coordinator?.OperationId)
                throw new System.InvalidOperationException("关卡场景入参与当前单局不匹配。");
            run = current;
            LifetimeScope scope = SceneScope;
            current.RogueChoiceChanged += OnRogueChoiceChanged;
            scope.Defer(() => current.RogueChoiceChanged -= OnRogueChoiceChanged);
            scope.Defer(() =>
            {
                if (gameplay.IsValid) GameEntry.Get<IGameplayModule>().Stop(gameplay);
                Cleanup();
            });
            run.AttachScene();
            if (run.RoomView == null) throw new System.InvalidOperationException("首张地图准备失败。");
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
            GameWindow hud = await GameEntry.Get<IUIModule>().OpenWindowAsync(ProjectServices.BattleHudWindow, new GameWindowOpenArgs(run), ct);
            if (hud == null || !gameplay.IsValid || !scope.IsActive || !ReferenceEquals(ProjectServices.Session?.CurrentRun, run) ||
                request.OperationId != ProjectServices.Coordinator?.OperationId || !run.MarkReady())
                throw new System.InvalidOperationException("关卡内容或 HUD 未能完成准备。");
        }

        /// <summary>
        /// 先停止模拟和界面，再释放本场景表现。
        /// </summary>
        /// <param name="ct">退出阶段取消令牌。</param>
        /// <returns>退出任务。</returns>
        protected override async UniTask OnExitSceneAsync(CancellationToken ct)
        {
            run?.Stop();
            await GameEntry.Get<IUIModule>().CloseWindowAsync(ProjectServices.RogueChoiceWindow, GameWindowCloseReason.ScopeExit);
            await GameEntry.Get<IUIModule>().CloseWindowAsync(ProjectServices.BattleHudWindow, GameWindowCloseReason.ScopeExit);
            if (gameplay.IsValid) await GameEntry.Get<IGameplayModule>().StopAsync(gameplay);
            Cleanup();
        }

        /// <summary>
        /// 强制退出仍然先停止唯一模拟入口。
        /// </summary>
        protected override void OnForceExit()
        {
            run?.Stop();
            if (gameplay.IsValid) GameEntry.Get<IGameplayModule>().Stop(gameplay);
            Cleanup();
        }

        /// <summary>
        /// 在单局停在安全帧边界后安排弹出当前奖励。
        /// </summary>
        private void OnRogueChoiceChanged()
        {
            if (run?.IsRunning == true && run.IsChoosingRogue && SceneScope.IsActive && !openingRogueChoice)
                OpenRogueChoiceAsync(run, SceneScope).Forget();
        }

        /// <summary>
        /// 使用场景作用域打开三选一，失败时保留冻结状态与 HUD 重开入口。
        /// </summary>
        /// <param name="owner">发起选择的单局。</param>
        /// <param name="scope">当前场景作用域。</param>
        /// <returns>弹窗打开任务。</returns>
        private async UniTask OpenRogueChoiceAsync(ExpeditionRun owner, LifetimeScope scope)
        {
            openingRogueChoice = true;
            try
            {
                await UniTask.Yield(PlayerLoopTiming.Update, scope.Token);
                if (!scope.IsActive || !ReferenceEquals(run, owner) || !owner.IsRunning || !owner.IsChoosingRogue) return;
                GameWindow window = await GameEntry.Get<IUIModule>().OpenWindowAsync(ProjectServices.RogueChoiceWindow,
                    new GameWindowOpenArgs(owner), scope.Token);
                if (window == null) GameEntry.Get<ILogModule>()?.Error("Roguelike", "三选一窗口未打开，请从 HUD 继续选择。");
            }
            catch (OperationCanceledException) when (!scope.IsActive) { }
            catch (Exception error)
            {
                if (scope.IsActive) GameEntry.Get<ILogModule>()?.Error("Roguelike", "三选一窗口打开失败：" + error.Message);
            }
            finally { openingRogueChoice = false; }
        }

        /// <summary>
        /// 释放本次场景拥有的资源。
        /// </summary>
        private void Cleanup()
        {
            gameplay = GameplayHandle.Invalid;
            presentation?.Dispose();
            presentation = null;
            run?.DetachScene();
            run = null;
        }
    }
}
