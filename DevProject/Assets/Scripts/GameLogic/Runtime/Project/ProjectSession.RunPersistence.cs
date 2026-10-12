using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using GameFramework;
using GameLogic.Gameplay.Expedition;
using GameLogic.Tables;

namespace GameLogic.Project
{
    /// <summary>
    /// 活动关卡完整恢复结果，不把读取故障解释为新局。
    /// </summary>
    public sealed class ProjectRunSnapshotReadResult
    {
        public bool Success;
        public bool Missing;
        public bool UsedPreviousRevision;
        public string Error;
        public ExpeditionRunSnapshot Snapshot;
    }

    /// <summary>
    /// 单局完整 tick 的不可变候选队列及唯一持久化泵。
    /// </summary>
    public sealed partial class ProjectSession
    {
        private sealed class RunSaveRequest
        {
            public string Identity;
            public List<SaveStateComponent> Components;
            public UniTaskCompletionSource<SaveResult> Receipt;
        }

        private readonly Queue<RunSaveRequest> runSaveQueue = new();
        private readonly List<UniTaskCompletionSource<SaveResult>> runDrainWaiters = new();
        private SaveStateJournal runJournal;
        private int maxPendingRunTicks;
        private string runConfigVersion;
        private bool runSavePump;
        private bool runSaveFailed;
        private bool runReading;
        private bool profileSaveWaiting;
        private SaveResult lastRunSaveResult = SaveResult.Ok();

        public event Action RunSaveFailed;
        public bool RunSaveBlocked => RunSaveInteractionBlocked || runJournal != null && runSaveQueue.Count >= maxPendingRunTicks;
        /// <summary>
        /// 保存故障或独占操作阻止交互；普通队列积压只阻止继续生成战斗帧。
        /// </summary>
        internal bool RunSaveInteractionBlocked => runSaveFailed || profileSaveWaiting || runReading;
        public bool HasPendingRunSave => runSavePump || runSaveQueue.Count != 0 || runReading;
        public long RunJournalRevision => runJournal == null ? 0 : runJournal.Revision;
        public string RunSaveMessage => lastRunSaveResult.Success ? string.Empty : lastRunSaveResult.Message;

        /// <summary>
        /// 用已初始化后端建立固定两区日志，换局不生成新的物理键前缀。
        /// </summary>
        /// <param name="backend">与局外保存共用的完整记录后端。</param>
        private void CreateRunJournal(ISaveBackend backend)
        {
            CfgRunPersistenceRow config = GameTables.CfgRunPersistence.GetById(1);
            if (backend == null || config == null || string.IsNullOrWhiteSpace(config.ConfigVersion) ||
                config.CheckpointTicks < 1 || config.CheckpointTicks > 256 || config.MaxPendingTicks < 1 ||
                config.MaxStateBytes <= 0 || config.MaxStateBytes > 64 * 1024 * 1024 || backend.MaxRecordBytes < 4096)
            {
                FailRunSave(SaveResult.Fail(SaveError.ValidationFailed, "单局保存配置或后端无效。"));
                return;
            }
            maxPendingRunTicks = config.MaxPendingTicks;
            runConfigVersion = config.ConfigVersion;
            runJournal = new SaveStateJournal(backend, "ProjectExtraction/prototype/local/run-state", config.CheckpointTicks, config.MaxStateBytes);
        }

        /// <summary>
        /// 将完整 tick 立即编码为独立字符串并按顺序保存，调用方在预算门禁前停止下一 tick。
        /// </summary>
        /// <param name="snapshot">已收束且后续可被调用方释放的完整快照。</param>
        /// <returns>成功进入队列时为真；失败不会丢弃已有候选。</returns>
        public bool EnqueueRunState(ExpeditionRunSnapshot snapshot) => TryQueueRunState(snapshot, false, out _, out _);

        /// <summary>
        /// 等待这一关键候选的持久化确认，失败时队列保留原候选供重试。
        /// </summary>
        /// <param name="snapshot">候选奖励展示或关键转换之前的完整状态。</param>
        /// <returns>对应候选的保存结果。</returns>
        public UniTask<SaveResult> CommitRunStateAsync(ExpeditionRunSnapshot snapshot)
        {
            TryCommitRunState(snapshot, out UniTask<SaveResult> task);
            return task;
        }

        /// <summary>
        /// 明确区分候选是否已入队，拒绝时返回失败任务且不产生可重试的新候选。
        /// </summary>
        /// <param name="snapshot">候选奖励展示或关键转换之前的完整状态。</param>
        /// <param name="task">对应候选回执或明确拒绝结果。</param>
        /// <returns>候选已被唯一队列接纳时为真。</returns>
        internal bool TryCommitRunState(ExpeditionRunSnapshot snapshot, out UniTask<SaveResult> task)
        {
            bool accepted = TryQueueRunState(snapshot, true, out RunSaveRequest request, out SaveResult rejected);
            task = accepted ? request.Receipt.Task : UniTask.FromResult(rejected);
            return accepted;
        }

        /// <summary>
        /// 重试队首未决候选的原修订，随后顺序排空已有队列。
        /// </summary>
        /// <returns>所有排队状态完成或再次失败的结果。</returns>
        public UniTask<SaveResult> RetryRunSaveAsync()
        {
            if (disposed || runJournal == null || saving || runReading)
                return UniTask.FromResult(SaveResult.Fail(SaveError.Conflict, "当前不能重试单局保存。"));
            if (runSaveFailed && !runJournal.HasPending) return UniTask.FromResult(lastRunSaveResult);
            runSaveFailed = false;
            lastRunSaveResult = SaveResult.Ok();
            return WaitForRunSavesAsync();
        }

        /// <summary>
        /// 等待当前 FIFO 排空；失败立即返回，局外写入必须在成功后取得独占门禁。
        /// </summary>
        /// <returns>已入队状态全部确认或明确失败的结果。</returns>
        public UniTask<SaveResult> WaitForRunSavesAsync()
        {
            if (disposed) return UniTask.FromResult(SaveResult.Fail(SaveError.Closed, "项目会话已结束。"));
            if (runSaveFailed) return UniTask.FromResult(lastRunSaveResult);
            if (runSaveQueue.Count == 0 && !runSavePump && !runReading) return UniTask.FromResult(SaveResult.Ok());
            var waiter = new UniTaskCompletionSource<SaveResult>();
            runDrainWaiters.Add(waiter);
            StartRunSavePump();
            return waiter.Task;
        }

        /// <summary>
        /// 在唯一会话中冷读取指定活动的最新完整状态，不写回或混用其他活动身份。
        /// </summary>
        /// <param name="identity">局外主存档记录的当前单局身份。</param>
        /// <returns>完整快照、明确缺失或读取错误。</returns>
        public async UniTask<ProjectRunSnapshotReadResult> ReadRunSnapshotAsync(string identity)
        {
            if (disposed || runJournal == null || saving || profileSaveWaiting || HasPendingRunSave)
                return new ProjectRunSnapshotReadResult { Error = "当前不能读取单局保存。" };
            runReading = true;
            SaveJournalLoadResult loaded = await runJournal.LoadAsync(identity);
            runReading = false;
            ResolveRunDrainWaiters(loaded.Result);
            if (!loaded.Result.Success) return new ProjectRunSnapshotReadResult { Error = loaded.Result.Message };
            if (loaded.Missing) return new ProjectRunSnapshotReadResult { Success = true, Missing = true };
            if (!ExpeditionSnapshotCodec.TryDecode(loaded.Components, out ExpeditionRunSnapshot snapshot, out string error))
                return new ProjectRunSnapshotReadResult { Error = error };
            if (snapshot.RunId != identity || snapshot.ConfigVersion != runConfigVersion)
                return new ProjectRunSnapshotReadResult { Error = "单局快照身份或配置版本不匹配。" };
            return new ProjectRunSnapshotReadResult { Success = true, UsedPreviousRevision = loaded.UsedPreviousRevision, Snapshot = snapshot };
        }

        /// <summary>
        /// 在入口校验容量和身份并冻结编码，任何失败均保留已有 FIFO。
        /// </summary>
        /// <param name="snapshot">完整业务候选。</param>
        /// <param name="receipt">是否需要独立候选回执。</param>
        /// <param name="request">已经冻结的候选。</param>
        /// <param name="rejected">未入队原因。</param>
        /// <returns>成功进入唯一队列时为真。</returns>
        private bool TryQueueRunState(ExpeditionRunSnapshot snapshot, bool receipt, out RunSaveRequest request, out SaveResult rejected)
        {
            request = null;
            rejected = SaveResult.Fail(SaveError.Conflict, "单局保存已阻塞，请先完成原保存。");
            if (disposed || runJournal == null || saving || RunSaveBlocked) return false;
            if (snapshot == null || snapshot.ConfigVersion != runConfigVersion ||
                (runSaveQueue.Count != 0 && runSaveQueue.Peek().Identity != snapshot.RunId))
            {
                rejected = SaveResult.Fail(SaveError.ValidationFailed, "单局候选身份或配置版本无效。");
                return false;
            }
            if (!ExpeditionSnapshotCodec.TryEncode(snapshot, out List<SaveStateComponent> components, out string error))
            {
                rejected = SaveResult.Fail(SaveError.ValidationFailed, error);
                FailRunSave(rejected);
                return false;
            }
            request = new RunSaveRequest { Identity = snapshot.RunId, Components = components,
                Receipt = receipt ? new UniTaskCompletionSource<SaveResult>() : null };
            runSaveQueue.Enqueue(request);
            StartRunSavePump();
            rejected = SaveResult.Ok();
            return true;
        }

        /// <summary>
        /// 仅创建一个异步泵，局外写入期间不启动新的物理操作。
        /// </summary>
        private void StartRunSavePump()
        {
            if (disposed || saving || runReading || runSavePump || runSaveFailed || runSaveQueue.Count == 0) return;
            runSavePump = true;
            PumpRunSavesAsync().Forget();
        }

        /// <summary>
        /// 顺序确认队首后才移除，并在故障时保持原日志候选和后续帧。
        /// </summary>
        /// <returns>唯一异步持久化泵的执行任务。</returns>
        private async UniTask PumpRunSavesAsync()
        {
            while (!disposed && !saving && !runSaveFailed && runSaveQueue.Count != 0)
            {
                RunSaveRequest request = runSaveQueue.Peek();
                SaveResult result = runJournal.HasPending ? await runJournal.RetryAsync() :
                    await runJournal.CommitAsync(request.Identity, request.Components);
                if (disposed) break;
                if (!result.Success)
                {
                    runSavePump = false;
                    FailRunSave(result);
                    return;
                }
                bool notify = request.Receipt != null || runSaveQueue.Count >= maxPendingRunTicks;
                runSaveQueue.Dequeue();
                request.Receipt?.TrySetResult(result);
                if (notify) Changed?.Invoke();
            }
            runSavePump = false;
            if (runSaveFailed) ResolveRunDrainWaiters(lastRunSaveResult);
            else if (runSaveQueue.Count == 0) ResolveRunDrainWaiters(SaveResult.Ok());
        }

        /// <summary>
        /// 发布明确失败并结束当前等待，不改变队列和已确认状态。
        /// </summary>
        /// <param name="result">失败保存结果。</param>
        private void FailRunSave(SaveResult result)
        {
            runSaveFailed = true;
            lastRunSaveResult = result;
            foreach (RunSaveRequest request in runSaveQueue.ToArray()) request.Receipt?.TrySetResult(result);
            ResolveRunDrainWaiters(result);
            GameEntry.Get<ILogModule>()?.Error("ProjectSession", result.Message);
            RunSaveFailed?.Invoke();
            Changed?.Invoke();
        }

        /// <summary>
        /// 完成所有排空等待者，先移出集合避免回执续体重入修改集合。
        /// </summary>
        /// <param name="result">当前排空或故障结果。</param>
        private void ResolveRunDrainWaiters(SaveResult result)
        {
            if (runDrainWaiters.Count == 0) return;
            var waiters = runDrainWaiters.ToArray();
            runDrainWaiters.Clear();
            foreach (UniTaskCompletionSource<SaveResult> waiter in waiters) waiter.TrySetResult(result);
        }

        /// <summary>
        /// 结束调用方等待，正在执行的完整记录提交仍由后端收束。
        /// </summary>
        private void DisposeRunPersistence()
        {
            SaveResult result = SaveResult.Fail(SaveError.Closed, "项目会话已结束。");
            foreach (RunSaveRequest request in runSaveQueue) request.Receipt?.TrySetResult(result);
            runSaveQueue.Clear();
            ResolveRunDrainWaiters(result);
            RunSaveFailed = null;
        }
    }
}
