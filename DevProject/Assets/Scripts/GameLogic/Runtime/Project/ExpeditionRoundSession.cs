using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;
using GameLogic.Gameplay.Expedition;

namespace GameLogic.Project
{
    /// <summary>
    /// 将现有关卡运行对象适配到框架单局会话。
    /// </summary>
    public sealed class ExpeditionRoundSession : IGameRoundSession
    {
        private readonly ProjectRunCoordinator coordinator;
        private readonly ProjectSession session;
        private readonly int levelId;
        private readonly string runId;
        private bool disposed;

        public event Action<GameRoundResult> Completed;
        public ExpeditionRun Run { get; private set; }

        /// <summary>
        /// 创建单局适配器，尚未创建场景内容。
        /// </summary>
        /// <param name="coordinator">唯一流程协调器。</param>
        /// <param name="session">存档会话。</param>
        /// <param name="levelId">关卡身份。</param>
        /// <param name="runId">框架生成的单局身份。</param>
        public ExpeditionRoundSession(ProjectRunCoordinator coordinator, ProjectSession session, int levelId, string runId)
        {
            this.coordinator = coordinator;
            this.session = session;
            this.levelId = levelId;
            this.runId = runId;
        }

        /// <summary>
        /// 新局先固化基线再扣费，续关先重放账本再准备房间；返回时仍未开放模拟。
        /// </summary>
        /// <param name="ct">框架加载取消令牌。</param>
        /// <returns>内容和 HUD 就绪后的加载任务。</returns>
        public async UniTask EnterAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            ExpeditionRun created;
            string error;
            bool restored = session.ActiveRunId == runId;
            if (!string.IsNullOrEmpty(session.ActiveRunId) && (!restored || session.ActiveRunVersion != 3))
                throw new InvalidOperationException("活动关卡身份或版本不兼容，原存档已保留。");
            if (restored)
            {
                ProjectRunSnapshotReadResult saved = await session.ReadRunSnapshotAsync(runId);
                if (!saved.Success || saved.Missing || saved.Snapshot.LevelId != levelId)
                    throw new InvalidOperationException(saved.Error ?? "原单局业务检查点缺失或关卡身份不匹配。");
                if (!ExpeditionRun.ValidateSavedCheckpoint(session, saved.Snapshot, out error))
                    throw new InvalidOperationException(error);
                if (!await coordinator.SyncRunProgressAsync(saved.Snapshot, ProjectRunPhase.AdmissionSaving))
                    throw new OperationCanceledException(ct);
                if (!ExpeditionRun.TryRestore(session, saved.Snapshot, out created, out error)) throw new InvalidOperationException(error);
                if (!created.TryPrepareResume(out error))
                {
                    created.Dispose();
                    throw new InvalidOperationException(error);
                }
            }
            else if (!session.TryPrepareRun(runId, levelId, out created, out error)) throw new InvalidOperationException(error);
            Run = created;
            session.BindRun(created);
            if (restored)
            {
                if (!await coordinator.SaveRunBoundaryAsync(created, ProjectRunPhase.AdmissionSaving)) throw new OperationCanceledException(ct);
            }
            else
            {
                if (!await coordinator.SaveRunBoundaryAsync(created, ProjectRunPhase.AdmissionSaving)) throw new OperationCanceledException(ct);
                ct.ThrowIfCancellationRequested();
                if (!await coordinator.SaveBarrierAsync(() => session.AdmitAsync(created), ProjectRunPhase.AdmissionSaving))
                    throw new OperationCanceledException(ct);
                coordinator.MarkNewAdmission(runId);
            }
            ct.ThrowIfCancellationRequested();
            coordinator.SetPhase(ProjectRunPhase.LoadingExpedition, "正在加载地图、角色和战斗界面。");
            long operationId = coordinator.OperationId;
            await GameEntry.Get<ISceneModule>().ChangeSceneAsync(ProjectServices.BattleSceneName,
                new ProjectRunEnterData(runId, levelId, operationId), ct);
            ct.ThrowIfCancellationRequested();
            if (disposed || coordinator.OperationId != operationId || !created.IsReady ||
                GameEntry.Get<ISceneModule>().CurrentScene?.Name != ProjectServices.BattleSceneName)
                throw new InvalidOperationException("关卡就绪检查未通过。");
        }

        /// <summary>
        /// 只退出本会话所属场景，然后释放唯一运行对象。
        /// </summary>
        /// <param name="ct">接口保留的取消令牌，资源退出不会中途放弃。</param>
        /// <returns>完成全部资源释放的任务。</returns>
        public async UniTask ExitAsync(CancellationToken ct)
        {
            Run?.Stop();
            ISceneModule scenes = GameEntry.Get<ISceneModule>();
            if (Run != null && ReferenceEquals(session.CurrentRun, Run) && scenes.CurrentScene?.Name == ProjectServices.BattleSceneName)
                await scenes.ExitCurrentSceneAsync(CancellationToken.None);
            Dispose();
        }

        /// <summary>
        /// 转发框架暂停请求；已结束的运行对象不会恢复。
        /// </summary>
        /// <param name="paused">是否暂停。</param>
        public void SetPaused(bool paused) => Run?.SetPaused(paused);

        /// <summary>
        /// 发布同一持久结果，接收确认由协调器完成。
        /// </summary>
        /// <param name="outcome">已经持久化的终局快照。</param>
        internal void PublishCompleted(ProjectRunOutcome outcome)
        {
            if (disposed || outcome == null || outcome.RunId != runId || outcome.LevelId != levelId) return;
            Completed?.Invoke(new GameRoundResult(runId, levelId, outcome.ExitKind == ProjectRunExitKind.Completed, 0, outcome.ElapsedSeconds));
        }

        /// <summary>
        /// 同步兜底释放，不派生任何死亡、掉落或奖励。
        /// </summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (ReferenceEquals(session.CurrentRun, Run)) session.BindRun(null);
            Run?.Dispose();
            Run = null;
            Completed = null;
        }
    }

    /// <summary>
    /// 所有关卡从 Home 选择，不把撤离解释为连续下一关。
    /// </summary>
    internal sealed class ExpeditionRoundFactory : IGameRoundFactory
    {
        private readonly ProjectRunCoordinator coordinator;
        public int FirstLevelId => 1;

        /// <summary>
        /// 创建项目会话工厂。
        /// </summary>
        /// <param name="coordinator">负责运行对象所有权的协调器。</param>
        internal ExpeditionRoundFactory(ProjectRunCoordinator coordinator) => this.coordinator = coordinator;

        /// <summary>
        /// 检查原型关卡是否存在，等级与体力由项目会话再校验。
        /// </summary>
        /// <param name="levelId">关卡身份。</param>
        /// <returns>配置中是否包含此关卡。</returns>
        public bool ContainsLevel(int levelId) => Array.Exists(PrototypeRules.CreateLevels(), level => level.Id == levelId);

        /// <summary>
        /// 本项目无连续下一关入口。
        /// </summary>
        /// <param name="levelId">当前关卡身份。</param>
        /// <returns>固定返回零。</returns>
        public int GetNextLevelId(int levelId) => 0;

        /// <summary>
        /// 按框架生成的身份创建本次会话。
        /// </summary>
        /// <param name="levelId">关卡身份。</param>
        /// <param name="roundId">框架单局身份。</param>
        /// <returns>新的单局会话。</returns>
        public IGameRoundSession Create(int levelId, string roundId) => coordinator.CreateRound(levelId, roundId);
    }
}
