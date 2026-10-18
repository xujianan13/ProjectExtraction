using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;
using GameLogic.Gameplay.Expedition;
using GameLogic.Scaffold;

namespace GameLogic.Project
{
    /// <summary>
    /// 项目在框架单局流程之上的展示和持久化阶段。
    /// </summary>
    public enum ProjectRunPhase
    {
        Restoring, HomeReady, AdmissionSaving, LoadingExpedition, Activating, Playing,
        Suspending, FreezingOutcome, Settling, ResultsReady, ReturningHome, RecoveryRequired
    }

    /// <summary>
    /// 局外场景完成内容与界面绑定后的就绪契约。
    /// </summary>
    public interface IProjectHomeReady
    {
        /// <summary>
        /// 本次局外内容是否已经就绪。
        /// </summary>
        bool IsReady { get; }
    }

    /// <summary>
    /// 关卡场景进入时使用的稳定身份与请求代次。
    /// </summary>
    public sealed class ProjectRunEnterData : SceneEnterData
    {
        public string RunId { get; }
        public int LevelId { get; }
        public long OperationId { get; }

        /// <summary>
        /// 创建单次场景加载参数。
        /// </summary>
        /// <param name="runId">单局身份。</param>
        /// <param name="levelId">关卡身份。</param>
        /// <param name="operationId">当前操作代次。</param>
        public ProjectRunEnterData(string runId, int levelId, long operationId)
        {
            RunId = runId;
            LevelId = levelId;
            OperationId = operationId;
        }
    }

    /// <summary>
    /// 唯一的出战、终局和返场协调入口，复用框架单局与场景状态机。
    /// </summary>
    public sealed class ProjectRunCoordinator : IDisposable
    {
        private readonly ProjectSession session;
        private readonly GameRoundFlow flow;
        private readonly CancellationTokenSource lifetime = new();
        private ExpeditionRoundSession round;
        private ProjectRunOutcome frozenOutcome;
        private bool recoveryUiPending;
        private UniTaskCompletionSource<bool> saveWaiter;
        private UniTaskCompletionSource<bool> saveBarrierCompletion;
        private Func<UniTask<ProjectOperationResult>> retrySave;
        private Func<UniTask<bool>> recovery;
        private bool busy;
        private bool retrying;
        private bool cancelRequested;
        private bool disposed;
        private bool closing;
        private bool applicationPaused;
        private int selectedLevelId = 1;
        private bool resumable;
        private bool runtimeSaveRecovery;
        private int saveBarrierDepth;
        private string newAdmissionRunId;
        private int requiredResumeStamina;

        public event Action Changed;
        public ProjectRunPhase Phase { get; private set; } = ProjectRunPhase.Restoring;
        public string Message { get; private set; } = "正在读取游戏进度。";
        public long OperationId { get; private set; }
        public GameRoundState FlowState => flow.State;
        public bool IsPaused => applicationPaused;
        public bool CanStart => !disposed && !closing && !busy && !retrying && Phase == ProjectRunPhase.HomeReady &&
            flow.State == GameRoundState.Menu && !flow.IsBusy && !session.IsBusy && HomeIsReady() &&
            string.IsNullOrEmpty(session.ActiveRunId) && session.Stamina >= RequiredEntryStamina;
        public bool CanResume => !disposed && !closing && !busy && !retrying && resumable && Phase == ProjectRunPhase.HomeReady &&
            flow.State == GameRoundState.Menu && !flow.IsBusy && !session.HasUncommittedSave && session.ActiveRunVersion == 3 &&
            !string.IsNullOrEmpty(session.ActiveRunId) && session.Stamina >= RequiredResumeStamina &&
            (session.RunStage == ProjectRunStage.Admitted || session.RunStage == ProjectRunStage.Running);

        /// <summary>
        /// 新局首次关卡准入需要的体力，第一房不额外收费。
        /// </summary>
        public long RequiredEntryStamina => session.RequiredEntryStamina;

        /// <summary>
        /// 已检查活动关卡下一次实际续关需要的体力，只读检查不会扣费。
        /// </summary>
        public int RequiredResumeStamina => requiredResumeStamina;
        public bool CanRetry => !disposed && !closing && !retrying && Phase == ProjectRunPhase.RecoveryRequired &&
            (saveWaiter != null || (!busy && recovery != null));
        public bool CanReturn => !disposed && !closing && !busy && !retrying && Phase == ProjectRunPhase.ResultsReady;
        public bool CanCancel => !disposed && !closing && !cancelRequested && flow.State == GameRoundState.Loading;

        /// <summary>
        /// 当前合法关卡选择，往返主界面后保留。
        /// </summary>
        public int SelectedLevelId
        {
            get => selectedLevelId;
            set
            {
                foreach (ProjectLevelOption level in session.Levels)
                {
                    if (level.Id != value || session.Level < level.RequiredLevel) continue;
                    selectedLevelId = value;
                    Changed?.Invoke();
                    return;
                }
            }
        }

        /// <summary>
        /// 创建项目协调器和唯一的通用单局流程。
        /// </summary>
        /// <param name="session">已打开的项目存档会话。</param>
        public ProjectRunCoordinator(ProjectSession session)
        {
            this.session = session;
            flow = new GameRoundFlow(new ExpeditionRoundFactory(this), SaveResultAsync);
            flow.StateChanged += OnFlowChanged;
            session.RunSaveFailed += OnRunSaveFailed;
        }

        /// <summary>
        /// 按持久阶段恢复；失败时保留全局入口，不误报为可出战。
        /// </summary>
        /// <returns>是否完成恢复和必要的界面加载。</returns>
        public async UniTask<bool> InitializeAsync()
        {
            if (disposed || closing || busy) return false;
            busy = true;
            OperationId++;
            SetPhase(ProjectRunPhase.Restoring, "正在恢复游戏进度。");
            resumable = false;
            try
            {
                if (!await ShowTransitionAsync()) return Fail("无法打开流程界面。", InitializeAsync);
                if (session.HasPendingSave)
                {
                    ProjectOperationResult pending = await session.RetryPendingSaveAsync();
                    if (!pending.Success) return Fail(pending.Message, InitializeAsync);
                }
                if (!string.IsNullOrEmpty(session.ActiveRunId) && session.ActiveRunVersion != 3)
                    return Fail("活动关卡版本尚未完成迁移，原存档已保留。", InitializeAsync);
                if (session.RecordedOutcome != null)
                {
                    ProjectOperationResult settled = await session.SettleOutcomeAsync(session.ActiveRunId);
                    if (!settled.Success) return Fail(settled.Message, InitializeAsync);
                }
                else if (!string.IsNullOrEmpty(session.ActiveRunId))
                {
                    if (!await InspectActiveRunAsync()) return false;
                }
                if (session.LastResult != null && !session.LastResult.Viewed) return await ShowResultsAsync();
                return await ReturnHomeCoreAsync();
            }
            catch (Exception error) { return HandleError(error, InitializeAsync); }
            finally { busy = false; Changed?.Invoke(); }
        }

        /// <summary>
        /// 从已就绪主界面发起一次独占出战。
        /// </summary>
        /// <param name="levelId">所选关卡。</param>
        /// <returns>是否已经完成开始授权并激活关卡。</returns>
        public async UniTask<bool> StartRunAsync(int levelId)
        {
            session.RefreshClock();
            if (!CanStart) return false;
            return await EnterRunAsync(levelId, false);
        }

        /// <summary>
        /// 接续原关卡身份，按检查点准备房间并确认本次续关费用。
        /// </summary>
        /// <returns>原关卡完整恢复并激活时为真。</returns>
        public UniTask<bool> ResumeRunAsync()
        {
            session.RefreshClock();
            return CanResume ? EnterRunAsync(session.ActiveRunLevelId, true) : UniTask.FromResult(false);
        }

        /// <summary>
        /// 统一新局和原局的资源加载、初始修订确认与激活。
        /// </summary>
        /// <param name="levelId">关卡身份。</param>
        /// <param name="restoring">是否接续原活动身份。</param>
        /// <returns>关卡已完整就绪并开放模拟时为真。</returns>
        private async UniTask<bool> EnterRunAsync(int levelId, bool restoring)
        {
            busy = true;
            cancelRequested = false;
            OperationId++;
            SelectedLevelId = levelId;
            SetPhase(ProjectRunPhase.AdmissionSaving, "正在准备关卡与入场记录。");
            try
            {
                if (!await ShowTransitionAsync()) return Fail("无法打开加载界面。", ReturnHomeAsync);
                IUIModule ui = GameEntry.Get<IUIModule>();
                await ui.CloseWindowAsync(2101);
                await ui.CloseWindowAsync(2102);
                await ui.CloseWindowAsync(ProjectServices.HomeShopWindow);
                bool entered = restoring ? await flow.RestoreLevelAsync(levelId, session.ActiveRunId, lifetime.Token) :
                    await flow.StartLevelAsync(levelId, lifetime.Token);
                if (!entered)
                {
                    if (restoring && !cancelRequested) return Fail(flow.Error ?? "原关卡恢复失败，存档已保留。", InitializeAsync);
                    await RecoverEntryCoreAsync(cancelRequested ? "取消入场" : flow.Error ?? "关卡加载失败");
                    return false;
                }
                if (disposed || closing || round?.Run == null || !round.Run.IsReady) return false;
                SetPhase(ProjectRunPhase.Activating, "关卡已就绪，正在确认开始。");
                round.Run.ConfirmRoomAdmission();
                if (!await SaveRunBoundaryAsync(round.Run, ProjectRunPhase.Activating)) return false;
                if (!await SaveBarrierAsync(() => session.MarkRunningAsync(round.Run.RunId), ProjectRunPhase.Activating)) return false;
                newAdmissionRunId = null;
                await ui.CloseWindowAsync(ProjectServices.TransitionWindow);
                resumable = false;
                busy = false;
                SetPhase(ProjectRunPhase.Playing, "完成房间并向上前进，击败最终 Boss 后通关带回。");
                if (!round.Run.Activate()) return Fail("关卡激活失败。", () => RecoverInterruptedRunAsync("激活失败"));
                if (Phase == ProjectRunPhase.Playing) round.Run.ResumePendingRewards();
                if (Phase == ProjectRunPhase.Playing && (applicationPaused || GameEntry.IsInBackground)) await SetApplicationPausedAsync(true);
                return true;
            }
            catch (Exception error) { return HandleError(error, () => RecoverInterruptedRunAsync("入场流程中断")); }
            finally
            {
                if (Phase != ProjectRunPhase.FreezingOutcome && Phase != ProjectRunPhase.Settling) busy = false;
                Changed?.Invoke();
            }
        }

        /// <summary>
        /// 在运行帧内锁定终局，并推迟持久化和场景销毁到下一轮调度。
        /// </summary>
        /// <param name="run">发起结束的本局对象。</param>
        /// <param name="exitKind">通关、死亡、放弃或广告撤离的唯一原因。</param>
        /// <param name="retainedItemIds">广告撤离已校验的本关物品身份，其他终局为空。</param>
        /// <returns>本次终局是否被接受。</returns>
        public bool TryEndRun(ExpeditionRun run, ProjectRunExitKind exitKind, IReadOnlyList<string> retainedItemIds = null) =>
            TryEndRunCore(run, exitKind, retainedItemIds);

        /// <summary>
        /// 从暂停菜单主动放弃本局，归还携入装备并损失本关所得，不退还入场体力。
        /// </summary>
        /// <param name="run">已暂停并等待确认的本局。</param>
        /// <returns>放弃已接纳并进入唯一终局流程时为真。</returns>
        public bool TryAbandonRun(ExpeditionRun run) => run != null && run.CanAbandon && TryEndRunCore(run, ProjectRunExitKind.Abandoned, null);

        /// <summary>
        /// 保存当前玩家与房间检查点后暂离，保留活动身份和续关入口。
        /// </summary>
        /// <param name="run">发起暂离的当前关卡。</param>
        /// <returns>检查点已确认并返回主界面时为真。</returns>
        public async UniTask<bool> SuspendRunAsync(ExpeditionRun run)
        {
            if (disposed || closing || busy || Phase != ProjectRunPhase.Playing || session.ActiveRunVersion != 3 ||
                !ReferenceEquals(run, session.CurrentRun) || run == null || !run.CanSuspend ||
                flow.State != GameRoundState.Playing && flow.State != GameRoundState.Paused) return false;
            busy = true;
            OperationId++;
            run.SetPaused(true);
            if (flow.State == GameRoundState.Playing) flow.Pause();
            SetPhase(ProjectRunPhase.Suspending, "正在保存关卡进度并暂离。");
            try
            {
                if (!await SaveRunBoundaryAsync(run, ProjectRunPhase.Suspending)) return false;
                return await ReturnHomeCoreAsync();
            }
            catch (Exception error) { return HandleError(error, InitializeAsync); }
            finally { busy = false; Changed?.Invoke(); }
        }

        /// <summary>
        /// 冻结并记录一次撤离、死亡或主动放弃结果。
        /// </summary>
        /// <param name="run">当前单局对象。</param>
        /// <param name="exitKind">本次唯一终局原因。</param>
        /// <param name="retainedItemIds">广告撤离的本关物品身份。</param>
        /// <returns>当前流程接受终局时为真。</returns>
        private bool TryEndRunCore(ExpeditionRun run, ProjectRunExitKind exitKind, IReadOnlyList<string> retainedItemIds)
        {
            if (disposed || closing || busy || session.HasUncommittedSave || Phase != ProjectRunPhase.Playing || flow.State != GameRoundState.Playing ||
                session.ActiveRunVersion != 3 || !Enum.IsDefined(typeof(ProjectRunExitKind), exitKind) ||
                run == null || !ReferenceEquals(run, session.CurrentRun) || run.RunId != session.ActiveRunId || run.IsSavingRun) return false;
            ProjectRunOutcome outcome = run.CreateOutcome(exitKind, retainedItemIds);
            if (outcome == null) return false;
            run.Stop();
            frozenOutcome = outcome;
            busy = true;
            SetPhase(ProjectRunPhase.FreezingOutcome, exitKind == ProjectRunExitKind.Completed ? "已完成最终挑战，正在记录本局结果。" :
                exitKind == ProjectRunExitKind.AdExtraction ? "广告撤离选择已确认，正在记录本局结果。" :
                exitKind == ProjectRunExitKind.Abandoned ? "已放弃本局，正在记录本局结果。" : "角色死亡，正在记录本局结果。");
            if (exitKind == ProjectRunExitKind.Death) run.BeginDeathPresentation();
            RecordAndCompleteAsync(outcome, run).Forget();
            return true;
        }

        /// <summary>
        /// 保存不可变终局后向框架提交，并检查它确实接受了结果。
        /// </summary>
        /// <param name="outcome">已经冻结的本局结果。</param>
        /// <param name="run">仍持有终局表现的原单局。</param>
        /// <returns>记录和结算任务。</returns>
        private async UniTask RecordAndCompleteAsync(ProjectRunOutcome outcome, ExpeditionRun run)
        {
            try
            {
                await UniTask.Yield(PlayerLoopTiming.Update, lifetime.Token);
                if (!await SaveRunBoundaryAsync(run, ProjectRunPhase.FreezingOutcome)) return;
                if (!await SaveBarrierAsync(() => session.RecordOutcomeAsync(outcome), ProjectRunPhase.FreezingOutcome)) return;
                if (outcome.ExitKind == ProjectRunExitKind.Death)
                    await UniTask.WaitUntil(() => disposed || closing || !ReferenceEquals(session.CurrentRun, run) || !run.IsPresentingDeath,
                        cancellationToken: lifetime.Token);
                if (disposed || closing) return;
                if (!await ShowTransitionAsync())
                {
                    Fail("无法打开结算界面，请重试。", ResumeRecordedOutcomeAsync);
                    return;
                }
                await CompleteRecordedOutcomeCoreAsync();
            }
            catch (Exception error) { HandleError(error, ResumeRecordedOutcomeAsync); }
            finally { busy = false; Changed?.Invoke(); }
        }

        /// <summary>
        /// 在事件未被接受或结算失败后重投同一份已保存结果。
        /// </summary>
        /// <returns>结果是否已经保存和展示。</returns>
        private async UniTask<bool> ResumeRecordedOutcomeAsync()
        {
            if (disposed || closing || busy) return false;
            if (!string.IsNullOrEmpty(session.ActiveRunId) && session.ActiveRunVersion != 3)
                return Fail("活动关卡版本尚未完成迁移，原存档已保留。", InitializeAsync);
            busy = true;
            try
            {
                if (!await ShowTransitionAsync()) return Fail("无法打开结算界面。", ResumeRecordedOutcomeAsync);
                if (session.RecordedOutcome == null && frozenOutcome != null && session.ActiveRunId == frozenOutcome.RunId)
                {
                    if (session.CurrentRun != null && !await SaveRunBoundaryAsync(session.CurrentRun, ProjectRunPhase.FreezingOutcome)) return false;
                    if (!await SaveBarrierAsync(() => session.RecordOutcomeAsync(frozenOutcome), ProjectRunPhase.FreezingOutcome)) return false;
                }
                return await CompleteRecordedOutcomeCoreAsync();
            }
            catch (Exception error) { return HandleError(error, ResumeRecordedOutcomeAsync); }
            finally { busy = false; Changed?.Invoke(); }
        }

        /// <summary>
        /// 接续正常结算或冷恢复，绝不为结果创建新局。
        /// </summary>
        /// <returns>是否可展示已保存结果。</returns>
        private async UniTask<bool> CompleteRecordedOutcomeCoreAsync()
        {
            if (!string.IsNullOrEmpty(session.ActiveRunId) && session.ActiveRunVersion != 3)
                return Fail("活动关卡版本尚未完成迁移，原存档已保留。", InitializeAsync);
            if (flow.State == GameRoundState.Paused && session.RecordedOutcome != null) flow.Resume();
            if (flow.State == GameRoundState.Failed && flow.Result.HasValue)
            {
                if (!await flow.RetryAsync(lifetime.Token)) return Fail(flow.Error, ResumeRecordedOutcomeAsync);
            }
            else if (flow.State == GameRoundState.Playing)
            {
                ProjectRunOutcome outcome = session.RecordedOutcome;
                if (flow.IsBusy || round == null || outcome == null || outcome.RunId != flow.CurrentRoundId)
                    return Fail("终局尚未被流程接收，请重试。", ResumeRecordedOutcomeAsync);
                round.PublishCompleted(outcome);
                if (!flow.Result.HasValue || flow.Result.Value.RoundId != outcome.RunId)
                    return Fail("终局通知尚未确认，请重试。", ResumeRecordedOutcomeAsync);
                await UniTask.WaitUntil(() => !flow.IsBusy, cancellationToken: lifetime.Token);
                if (flow.State != GameRoundState.Results) return Fail(flow.Error ?? "结算尚未完成。", ResumeRecordedOutcomeAsync);
            }
            else if (flow.State == GameRoundState.Menu && session.RecordedOutcome != null)
            {
                ProjectOperationResult result = await session.SettleOutcomeAsync(session.ActiveRunId);
                if (!result.Success) return Fail(result.Message, ResumeRecordedOutcomeAsync);
            }
            return await ShowResultsAsync();
        }

        /// <summary>
        /// 框架保存回调必须持久化确认后才正常返回。
        /// </summary>
        /// <param name="result">框架已接受的结果。</param>
        /// <param name="ct">框架结算取消令牌。</param>
        /// <returns>结算保存任务。</returns>
        private async UniTask SaveResultAsync(GameRoundResult result, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            ProjectOperationResult saved = await session.SettleOutcomeAsync(result.RoundId);
            if (!saved.Success) throw new InvalidOperationException(saved.Message);
        }

        /// <summary>
        /// 结果页只负责返回，不承担奖励领取。
        /// </summary>
        /// <returns>主界面是否已经就绪。</returns>
        public async UniTask<bool> ReturnHomeAsync()
        {
            if (disposed || closing || busy) return false;
            if (Phase != ProjectRunPhase.ResultsReady && Phase != ProjectRunPhase.RecoveryRequired) return false;
            if (!string.IsNullOrEmpty(session.ActiveRunId) || session.HasPendingSave) return false;
            busy = true;
            OperationId++;
            try { return await ReturnHomeCoreAsync(); }
            catch (Exception error) { return HandleError(error, ReturnHomeAsync); }
            finally { busy = false; Changed?.Invoke(); }
        }

        /// <summary>
        /// 清理框架单局状态后加载实际 Home，并确认查看标记。
        /// </summary>
        /// <returns>是否完成局外准备。</returns>
        private async UniTask<bool> ReturnHomeCoreAsync()
        {
            SetPhase(ProjectRunPhase.ReturningHome, "正在返回主界面。");
            if (!await ShowTransitionAsync()) return Fail("无法打开返场界面。", InitializeAsync);
            if (!await flow.ReturnToMenuAsync(lifetime.Token)) return Fail(flow.Error ?? "单局仍未退出。", InitializeAsync);
            if (string.IsNullOrEmpty(session.ActiveRunId))
            {
                resumable = false;
                requiredResumeStamina = 0;
            }
            else if (!resumable)
            {
                if (!await InspectActiveRunAsync()) return false;
                if (string.IsNullOrEmpty(session.ActiveRunId) && session.LastResult != null && !session.LastResult.Viewed)
                    return await ShowResultsAsync();
            }
            await GameEntry.Get<IUIModule>().CloseWindowAsync(ProjectServices.ResultWindow);
            if (!HomeIsReady()) await GameEntry.Get<ISceneModule>().ChangeSceneAsync(ContentIds.HomeScene, ct: lifetime.Token);
            if (!HomeIsReady()) return Fail("主界面内容尚未就绪。", InitializeAsync);
            session.RefreshClock();
            if (session.LastResult != null && !session.LastResult.Viewed)
            {
                string runId = session.LastResult.Outcome.RunId;
                if (!await SaveBarrierAsync(() => session.AcknowledgeResultAsync(runId), ProjectRunPhase.ReturningHome)) return false;
            }
            await GameEntry.Get<IUIModule>().CloseWindowAsync(ProjectServices.TransitionWindow);
            round = null;
            recovery = null;
            SetPhase(ProjectRunPhase.HomeReady, resumable ? "已恢复上次关卡，可以继续探索。" : session.LastRunSummary);
            return true;
        }

        /// <summary>
        /// 当前场景必须同时确认内容和窗口已经绑定。
        /// </summary>
        /// <returns>是否为可用的主界面。</returns>
        private static bool HomeIsReady()
        {
            IScene scene = GameEntry.Get<ISceneModule>()?.CurrentScene;
            return scene?.Name == ContentIds.HomeScene && scene.IsActive && scene is IProjectHomeReady ready && ready.IsReady;
        }

        /// <summary>
        /// 冻结故障世界并从最新业务检查点恢复续关入口，不退费或更换身份。
        /// </summary>
        /// <param name="reason">中断原因。</param>
        /// <returns>是否已恢复主界面。</returns>
        public async UniTask<bool> RecoverInterruptedRunAsync(string reason)
        {
            if (disposed || closing || busy) return false;
            if (!string.IsNullOrEmpty(session.ActiveRunId) && session.ActiveRunVersion != 3)
                return Fail("活动关卡版本尚未完成迁移，原存档已保留。", InitializeAsync);
            if (session.RecordedOutcome != null) return await ResumeRecordedOutcomeAsync();
            busy = true;
            session.CurrentRun?.Stop();
            try
            {
                await UniTask.Yield(PlayerLoopTiming.Update, lifetime.Token);
                SetPhase(ProjectRunPhase.Restoring, reason + "，正在恢复原关卡。");
                if (!await ShowTransitionAsync()) return Fail("无法打开恢复界面。", () => RecoverInterruptedRunAsync(reason));
                if ((session.HasPendingRunSave || session.RunSaveBlocked) &&
                    !await SaveBarrierAsync(RetryRunJournalOperationAsync, ProjectRunPhase.Restoring)) return false;
                if (!await flow.ReturnToMenuAsync(lifetime.Token)) return Fail(flow.Error, () => RecoverInterruptedRunAsync(reason));
                return await ReturnHomeCoreAsync();
            }
            catch (Exception error) { return HandleError(error, () => RecoverInterruptedRunAsync(reason)); }
            finally
            {
                if (Phase != ProjectRunPhase.FreezingOutcome && Phase != ProjectRunPhase.Settling) busy = false;
                Changed?.Invoke();
            }
        }

        /// <summary>
        /// 退出未激活资源，只有本轮取消的新准入可以退回，其余活动保持原档。
        /// </summary>
        /// <param name="reason">补偿原因。</param>
        /// <returns>是否完成补偿和返场。</returns>
        private async UniTask<bool> RecoverEntryCoreAsync(string reason)
        {
            SetPhase(ProjectRunPhase.Restoring, reason + "，正在收束入场流程。");
            if (!await ShowTransitionAsync()) return Fail("无法打开恢复界面。", () => RecoverInterruptedRunAsync(reason));
            if (session.RecordedOutcome != null) return await CompleteRecordedOutcomeCoreAsync();
            if (!await flow.ReturnToMenuAsync(lifetime.Token)) return Fail(flow.Error ?? "资源退出失败。", () => RecoverInterruptedRunAsync(reason));
            if (cancelRequested && newAdmissionRunId == session.ActiveRunId && session.RunStage == ProjectRunStage.Admitted)
            {
                string cancelledRunId = newAdmissionRunId;
                if (!await SaveBarrierAsync(() => session.ActiveRunId == cancelledRunId ? session.CompensateEntryAsync(reason) :
                    UniTask.FromResult(new ProjectOperationResult(true, "取消准入已确认。")), ProjectRunPhase.Restoring)) return false;
                newAdmissionRunId = null;
                resumable = false;
            }
            else if (!string.IsNullOrEmpty(session.ActiveRunId) && !await InspectActiveRunAsync()) return false;
            return await ReturnHomeCoreAsync();
        }

        /// <summary>
        /// 检查完整活动状态，终局优先记录并结算，存活状态仅开放原局接续。
        /// </summary>
        /// <returns>活动完整修订兼容当前内容时为真。</returns>
        private async UniTask<bool> InspectActiveRunAsync()
        {
            if (session.ActiveRunVersion != 3) return Fail("活动关卡版本尚未完成迁移，原存档已保留。", InitializeAsync);
            ProjectRunSnapshotReadResult loaded = await session.ReadRunSnapshotAsync(session.ActiveRunId);
            if (!loaded.Success || loaded.Missing || loaded.Snapshot.LevelId != session.ActiveRunLevelId)
                return Fail(loaded.Error ?? "活动关卡完整状态缺失或关卡身份不匹配，原存档已保留。", InitializeAsync);
            ExpeditionRunSnapshot snapshot = loaded.Snapshot;
            if (!ExpeditionRun.ValidateSavedCheckpoint(session, snapshot, out string checkpointError))
                return Fail(checkpointError, InitializeAsync);
            if (!await SyncRunProgressAsync(snapshot, ProjectRunPhase.Restoring)) return false;
            if (snapshot.PendingOutcome != null)
            {
                frozenOutcome = snapshot.PendingOutcome.Copy();
                if (!await SaveBarrierAsync(() => session.RecordOutcomeAsync(frozenOutcome), ProjectRunPhase.Restoring)) return false;
                ProjectOperationResult settled = await session.SettleOutcomeAsync(session.ActiveRunId);
                if (!settled.Success) return Fail(settled.Message, InitializeAsync);
                resumable = false;
                requiredResumeStamina = 0;
                return true;
            }
            if (!ExpeditionRun.TryRestore(session, snapshot, out ExpeditionRun restored, out string error)) return Fail(error, InitializeAsync);
            requiredResumeStamina = restored.RequiredResumeStamina;
            restored.Dispose();
            selectedLevelId = snapshot.LevelId;
            resumable = true;
            return true;
        }

        /// <summary>
        /// 保存同一完整候选，首次入队与以后重试分别执行，不增加重复修订。
        /// </summary>
        /// <param name="run">完整边界所属单局。</param>
        /// <param name="resumePhase">确认后的继续阶段。</param>
        /// <returns>该候选已经完整持久确认时为真。</returns>
        internal UniTask<bool> SaveRunBoundaryAsync(ExpeditionRun run, ProjectRunPhase resumePhase)
        {
            if (!run.TryCaptureState(out ExpeditionRunSnapshot snapshot, out string error))
                return UniTask.FromResult(Fail(error, () => RecoverInterruptedRunAsync("完整关卡快照失败")));
            return SaveSnapshotBarrierAsync(snapshot, resumePhase);
        }

        /// <summary>
        /// 先保存冻结的关卡日志，再同步永久经验与房间扣费账本，重试不创建重复修订。
        /// </summary>
        /// <param name="snapshot">进入屏障前冻结的完整业务候选。</param>
        /// <param name="resumePhase">确认后的继续阶段。</param>
        /// <returns>这份候选已经完整持久确认时为真。</returns>
        internal async UniTask<bool> SaveSnapshotBarrierAsync(ExpeditionRunSnapshot snapshot, ProjectRunPhase resumePhase)
        {
            bool attempted = false;
            bool submitted = false;
            if (!await SaveBarrierAsync(async () =>
            {
                bool retry = attempted;
                attempted = true;
                if (!submitted)
                {
                    SaveResult drained = retry ? await session.RetryRunSaveAsync() : await session.WaitForRunSavesAsync();
                    if (!drained.Success) return new ProjectOperationResult(false, drained.Message);
                    submitted = session.TryCommitRunState(snapshot, out UniTask<SaveResult> receipt);
                    SaveResult committed = await receipt;
                    return new ProjectOperationResult(committed.Success, committed.Message);
                }
                return await RetryRunJournalOperationAsync();
            }, resumePhase)) return false;
            return await SyncRunProgressAsync(snapshot, resumePhase);
        }

        /// <summary>
        /// 将已经确认的关卡日志同步到永久账号；新局准入前只保存基线。
        /// </summary>
        /// <param name="snapshot">关卡日志中已经确认的业务快照。</param>
        /// <param name="resumePhase">同步完成后继续的流程阶段。</param>
        /// <returns>两本账的累计水位已确认一致时为真。</returns>
        internal UniTask<bool> SyncRunProgressAsync(ExpeditionRunSnapshot snapshot, ProjectRunPhase resumePhase)
        {
            if (string.IsNullOrEmpty(session.ActiveRunId)) return UniTask.FromResult(true);
            return SaveBarrierAsync(() => session.CommitRunProgressAsync(snapshot), resumePhase);
        }

        /// <summary>
        /// 记录本轮新准入成功，取消只可退回这份尚未运行的扣费。
        /// </summary>
        /// <param name="runId">已经确认准入的单局身份。</param>
        internal void MarkNewAdmission(string runId) => newAdmissionRunId = runId;

        /// <summary>
        /// 将单局日志的原候选重试转换为项目统一保存回执。
        /// </summary>
        /// <returns>同一候选的持久化结果。</returns>
        private async UniTask<ProjectOperationResult> RetryRunJournalOperationAsync()
        {
            SaveResult result = await session.RetryRunSaveAsync();
            return new ProjectOperationResult(result.Success, result.Message);
        }

        /// <summary>
        /// 普通 tick 的异步保存失败只挂起一条恢复流程，关键保存自行处理其回执。
        /// </summary>
        private void OnRunSaveFailed()
        {
            if (disposed || closing || runtimeSaveRecovery || saveBarrierDepth != 0 || Phase != ProjectRunPhase.Playing) return;
            runtimeSaveRecovery = true;
            RecoverRuntimeSaveAsync().Forget();
        }

        /// <summary>
        /// 等待玩家显式重试原 FIFO，确认后关闭恢复界面并接续同一运行对象。
        /// </summary>
        /// <returns>唯一普通 tick 保存恢复任务。</returns>
        private async UniTask RecoverRuntimeSaveAsync()
        {
            bool attempted = false;
            bool accepted = await SaveBarrierAsync(() =>
            {
                if (!attempted)
                {
                    attempted = true;
                    return UniTask.FromResult(new ProjectOperationResult(false, session.RunSaveMessage));
                }
                return RetryRunJournalOperationAsync();
            }, ProjectRunPhase.Playing);
            runtimeSaveRecovery = false;
            if (accepted && Phase == ProjectRunPhase.Playing) session.CurrentRun?.ResumePendingRewards();
        }

        /// <summary>
        /// 保存失败时挂起原流程，只允许重试原候选。
        /// </summary>
        /// <param name="save">原保存事务。</param>
        /// <param name="resumePhase">确认后的继续阶段。</param>
        /// <returns>保存是否已经确认。</returns>
        internal async UniTask<bool> SaveBarrierAsync(Func<UniTask<ProjectOperationResult>> save, ProjectRunPhase resumePhase)
        {
            while (saveBarrierCompletion != null)
                if (!await saveBarrierCompletion.Task.AttachExternalCancellation(lifetime.Token)) return false;
            if (disposed || closing) return false;
            var completion = new UniTaskCompletionSource<bool>();
            saveBarrierCompletion = completion;
            saveBarrierDepth++;
            UniTaskCompletionSource<bool> waiter = null;
            bool completed = false;
            try
            {
                ProjectOperationResult result = await save();
                if (result.Success) return completed = !disposed && !closing;
                if (disposed || closing) return false;
                waiter = new UniTaskCompletionSource<bool>();
                saveWaiter = waiter;
                retrySave = save;
                SetPhase(ProjectRunPhase.RecoveryRequired, result.Message);
                EnsureRecoveryVisibleAsync().Forget();
                bool accepted = await waiter.Task.AttachExternalCancellation(lifetime.Token);
                if (accepted && !disposed && !closing)
                {
                    if (resumePhase == ProjectRunPhase.Playing)
                        await GameEntry.Get<IUIModule>().CloseWindowAsync(ProjectServices.TransitionWindow);
                    if (!disposed && !closing) SetPhase(resumePhase, "保存已确认，继续处理。");
                }
                return completed = accepted && !disposed && !closing;
            }
            finally
            {
                if (waiter != null && ReferenceEquals(saveWaiter, waiter)) { saveWaiter = null; retrySave = null; }
                saveBarrierDepth--;
                saveBarrierCompletion = null;
                completion.TrySetResult(completed);
            }
        }

        /// <summary>
        /// 全局恢复按钮按原失败阶段分发，不盲目重开 Flow。
        /// </summary>
        /// <returns>本次重试是否成功。</returns>
        public async UniTask<bool> RetryAsync()
        {
            if (!CanRetry) return false;
            retrying = true;
            Changed?.Invoke();
            try
            {
                if (saveWaiter != null)
                {
                    if (session.HasPendingSave)
                    {
                        ProjectOperationResult pending = await session.RetryPendingSaveAsync();
                        if (!pending.Success) { Message = pending.Message; return false; }
                    }
                    ProjectOperationResult saved = await retrySave();
                    if (!saved.Success) { Message = saved.Message; return false; }
                    saveWaiter.TrySetResult(true);
                    return true;
                }
                return recovery != null && await recovery();
            }
            catch (Exception error) { return HandleError(error, recovery); }
            finally { retrying = false; Changed?.Invoke(); }
        }

        /// <summary>
        /// 取消加载只发出意图；在途保存先收束，再执行补偿。
        /// </summary>
        public void CancelLoading()
        {
            if (!CanCancel) return;
            cancelRequested = true;
            flow.CancelLoading();
            Message = "正在取消入场；未确认的保存需要先完成重试。";
            Changed?.Invoke();
        }

        /// <summary>
        /// 暂停只影响实际游玩，终局保存不会被暂停状态阻塞。
        /// </summary>
        /// <param name="paused">应用是否处于后台。</param>
        /// <returns>暂停与当前完整边界保存的执行任务。</returns>
        public async UniTask SetApplicationPausedAsync(bool paused)
        {
            applicationPaused = paused;
            if (Phase == ProjectRunPhase.Playing)
            {
                if (paused && flow.State == GameRoundState.Playing) flow.Pause();
                else if (!paused && flow.State == GameRoundState.Paused) flow.Resume();
                session.CurrentRun?.SetPaused(paused);
                if (paused && session.CurrentRun != null)
                {
                    UniTaskCompletionSource<bool> active = saveBarrierCompletion;
                    if (active != null) await active.Task.AttachExternalCancellation(lifetime.Token);
                    else await SaveRunBoundaryAsync(session.CurrentRun, ProjectRunPhase.Playing);
                    if (Phase == ProjectRunPhase.Playing)
                    {
                        bool stillPaused = applicationPaused || GameEntry.IsInBackground;
                        if (!stillPaused && flow.State == GameRoundState.Paused) flow.Resume();
                        session.CurrentRun?.SetPaused(stillPaused);
                    }
                }
            }
            Changed?.Invoke();
        }

        /// <summary>
        /// 打开与任何逻辑场景无关的流程界面。
        /// </summary>
        /// <returns>窗口是否可用。</returns>
        private async UniTask<bool> ShowTransitionAsync()
        {
            try { return await GameEntry.Get<IUIModule>().OpenWindowAsync(ProjectServices.TransitionWindow, ct: lifetime.Token) != null; }
            catch (Exception error)
            {
                if (!disposed && !closing) GameEntry.Get<ILogModule>()?.Error("ProjectRun", "流程界面加载失败：" + error.Message);
                return false;
            }
        }

        /// <summary>
        /// 专用窗口不可用时使用框架提示框保留玩家可操作的恢复入口。
        /// </summary>
        /// <returns>等待玩家选择重试的恢复展示任务。</returns>
        private async UniTask EnsureRecoveryVisibleAsync()
        {
            if (recoveryUiPending || disposed || closing) return;
            recoveryUiPending = true;
            try
            {
                await UniTask.Yield(PlayerLoopTiming.Update, lifetime.Token);
                if (Phase != ProjectRunPhase.RecoveryRequired || await ShowTransitionAsync()) return;
                await GameEntry.Get<IUIModule>().AlertAsync("流程尚未完成", Message, "重试", lifetime.Token);
                await UniTask.WaitUntil(() => !busy || saveWaiter != null, cancellationToken: lifetime.Token);
                recoveryUiPending = false;
                if (CanRetry) await RetryAsync();
            }
            catch (OperationCanceledException) when (disposed || closing) { }
            finally { recoveryUiPending = false; }
        }

        /// <summary>
        /// 结果界面打开失败时仍保留过渡窗口中的摘要与返回入口。
        /// </summary>
        /// <returns>是否已有持久结果可展示。</returns>
        private async UniTask<bool> ShowResultsAsync()
        {
            if (session.LastResult == null) return Fail("缺少已保存的结果。", ResumeRecordedOutcomeAsync);
            frozenOutcome = null;
            SetPhase(ProjectRunPhase.ResultsReady, session.LastRunSummary);
            try
            {
                GameWindow window = await GameEntry.Get<IUIModule>().OpenWindowAsync(ProjectServices.ResultWindow, ct: lifetime.Token);
                if (window != null) await GameEntry.Get<IUIModule>().CloseWindowAsync(ProjectServices.TransitionWindow);
            }
            catch (Exception error)
            {
                GameEntry.Get<ILogModule>()?.Error("ProjectRun", "结果界面加载失败：" + error.Message);
                Message = session.LastRunSummary + "；结果界面加载失败，可返回主界面。";
            }
            return true;
        }

        /// <summary>
        /// 同步框架的结算状态，不在事件回调内重入场景切换。
        /// </summary>
        /// <param name="state">框架当前阶段。</param>
        private void OnFlowChanged(GameRoundState state)
        {
            if (state == GameRoundState.Settling) SetPhase(ProjectRunPhase.Settling, "正在释放关卡并保存结算。");
            else Changed?.Invoke();
        }

        /// <summary>
        /// 更新项目阶段并同步局外操作门禁。
        /// </summary>
        /// <param name="phase">新阶段。</param>
        /// <param name="message">玩家可读的当前状态。</param>
        internal void SetPhase(ProjectRunPhase phase, string message)
        {
            Phase = phase;
            Message = message ?? "操作失败，请重试。";
            session.SetHomeEditable(phase == ProjectRunPhase.HomeReady);
            Changed?.Invoke();
        }

        /// <summary>
        /// 记录可恢复失败，并指定唯一的恢复操作。
        /// </summary>
        /// <param name="message">失败说明。</param>
        /// <param name="retry">恢复入口。</param>
        /// <returns>固定返回失败。</returns>
        private bool Fail(string message, Func<UniTask<bool>> retry)
        {
            recovery = retry;
            SetPhase(ProjectRunPhase.RecoveryRequired, message);
            EnsureRecoveryVisibleAsync().Forget();
            return false;
        }

        /// <summary>
        /// 在异步框架调用边界记录异常并保留恢复入口。
        /// </summary>
        /// <param name="error">框架或平台错误。</param>
        /// <param name="retry">恢复入口。</param>
        /// <returns>固定返回失败。</returns>
        private bool HandleError(Exception error, Func<UniTask<bool>> retry)
        {
            if (disposed || closing) return false;
            GameEntry.Get<ILogModule>()?.Error("ProjectRun", error.ToString());
            return Fail(error.Message, retry);
        }

        /// <summary>
        /// 创建并登记框架本次会话，资源释放由此会话独占。
        /// </summary>
        /// <param name="levelId">关卡身份。</param>
        /// <param name="runId">框架生成的单局身份。</param>
        /// <returns>新的单局适配对象。</returns>
        internal ExpeditionRoundSession CreateRound(int levelId, string runId)
        {
            round = new ExpeditionRoundSession(this, session, levelId, runId);
            return round;
        }

        /// <summary>
        /// 正常关闭先停止新请求并等待异步退出完成。
        /// </summary>
        /// <returns>资源是否退出成功。</returns>
        public async UniTask<bool> CloseAsync()
        {
            if (disposed) return true;
            if (closing || saveWaiter != null) return false;
            UniTaskCompletionSource<bool> activeSave = saveBarrierCompletion;
            if (activeSave != null && !await activeSave.Task.AttachExternalCancellation(lifetime.Token)) return false;
            ExpeditionRun current = session.CurrentRun;
            if (current != null && !string.IsNullOrEmpty(session.ActiveRunId) && session.RecordedOutcome == null)
            {
                current.SetPaused(true);
                if (!await SaveRunBoundaryAsync(current, Phase)) return false;
            }
            closing = true;
            session.SetHomeEditable(false);
            saveWaiter?.TrySetResult(false);
            SaveResult runSaved = await session.WaitForRunSavesAsync();
            if (!runSaved.Success)
            {
                closing = false;
                return Fail(runSaved.Message, () => RecoverInterruptedRunAsync("退出时原关卡尚未保存"));
            }
            if (!await flow.CloseAsync())
            {
                closing = false;
                return Fail(flow.Error ?? "退出失败，请重试恢复。", () => RecoverInterruptedRunAsync("关闭时资源退出失败"));
            }
            try
            {
                ISceneModule scenes = GameEntry.Get<ISceneModule>();
                if (scenes?.CurrentScene?.Name == ContentIds.HomeScene) await scenes.ExitCurrentSceneAsync();
                IUIModule ui = GameEntry.Get<IUIModule>();
                if (ui != null)
                {
                    await ui.CloseWindowAsync(ProjectServices.ResultWindow);
                    await ui.CloseWindowAsync(ProjectServices.TransitionWindow);
                }
                Dispose();
                return true;
            }
            catch (Exception error)
            {
                closing = false;
                return HandleError(error, RetryCloseAndRestoreAsync);
            }
        }

        /// <summary>
        /// 已关闭 Flow 的外围清理失败后重新收束服务，再按存档恢复。
        /// </summary>
        /// <returns>项目服务和可操作界面是否恢复。</returns>
        private async UniTask<bool> RetryCloseAndRestoreAsync()
        {
            if (!await ProjectServices.InitializeAsync(CancellationToken.None)) return false;
            return await ProjectServices.Coordinator.InitializeAsync();
        }

        /// <summary>
        /// 应用强制关闭时取消回调并同步兜底释放；已记录结果保留在存档中。
        /// </summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            closing = true;
            saveWaiter?.TrySetResult(false);
            lifetime.Cancel();
            flow.StateChanged -= OnFlowChanged;
            session.RunSaveFailed -= OnRunSaveFailed;
            flow.Dispose();
            lifetime.Dispose();
            Changed = null;
        }
    }
}
