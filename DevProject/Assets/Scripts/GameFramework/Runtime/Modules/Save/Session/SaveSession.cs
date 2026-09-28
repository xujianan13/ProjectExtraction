using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace GameFramework
{
    /// <summary>
    /// 模块内部管理的非泛型会话端口。
    /// </summary>
    internal interface ISaveSession : IDisposable
    {
        /// <summary>
        /// 读取会话状态的独立快照。
        /// </summary>
        /// <returns>不含存档正文的状态。</returns>
        SaveSessionStatus GetStatus();
        /// <summary>
        /// 推进自动保存与操作超时。
        /// </summary>
        void Tick();

        /// <summary>
        /// 接收前后台状态。
        /// </summary>
        /// <param name="background">是否进入后台。</param>
        void OnBackground(bool background);
    }

    /// <summary>
    /// 一个逻辑存档的保存会话；调用入口和快照生成须在 Unity 主线程执行。
    /// </summary>
    /// <typeparam name="T">游戏存档数据类型。</typeparam>
    public sealed class SaveSession<T> : ISaveSession
    {
        private readonly SaveRepository<T> _repository;
        private readonly SavePolicy _policy;
        private readonly Func<double> _clock;
        private readonly Action _release;
        private Func<T> _snapshot;
        private SaveCandidate _candidate;
        private SavePhase _phase = SavePhase.Inactive;
        private SaveResult _lastResult = SaveResult.Ok();
        private bool _active, _accepted, _running, _disposed, _released, _closing, _closedSaved;
        private long _revision, _savedRevision, _candidateRevision;
        private int _attempts;
        private double _dirtySince, _changedAt, _startedAt, _retryAt;

        public SaveLoadResult<T> LoadResult { get; }
        public event Action<SaveSessionStatus> StatusChanged;
        private bool Dirty => _revision > _savedRevision;

        /// <summary>
        /// 创建尚未激活的会话。
        /// </summary>
        /// <param name="repository">存档仓库。</param>
        /// <param name="loadResult">加载结果。</param>
        /// <param name="policy">独立的保存策略。</param>
        /// <param name="clock">单调时钟。</param>
        /// <param name="release">物理操作收束后释放身份占用。</param>
        internal SaveSession(SaveRepository<T> repository, SaveLoadResult<T> loadResult, SavePolicy policy, Func<double> clock, Action release)
        {
            _repository = repository;
            LoadResult = loadResult;
            _policy = policy;
            _clock = clock;
            _release = release;
            _accepted = loadResult.Origin != SaveLoadOrigin.Recovered;
            _revision = loadResult.NeedsWrite ? 1 : 0;
            _dirtySince = _changedAt = _clock();
        }

        /// <summary>
        /// 读取当前状态的独立快照。
        /// </summary>
        /// <returns>会话状态。</returns>
        public SaveSessionStatus GetStatus() => new(_disposed ? SavePhase.Closed : _phase, _active && !_disposed,
            Dirty, _repository.Sequence, _revision, _savedRevision, _lastResult);

        /// <summary>
        /// 绑定快照生成器；生成器应返回独立 DTO，不能返回正在被其他线程修改的数据。
        /// </summary>
        /// <param name="snapshot">主线程快照生成器。</param>
        /// <returns>绑定结果。</returns>
        public SaveResult BindSnapshot(Func<T> snapshot)
        {
            if (_disposed || _closing) return Closed();
            if (_active || _running) return SaveResult.Fail(SaveError.Conflict, "激活后不能更换存档快照来源。");
            if (snapshot == null) return SaveResult.Fail(SaveError.InvalidArgument, "存档快照生成器不能为空。");
            _snapshot = snapshot;
            return SaveResult.Ok();
        }

        /// <summary>
        /// 明确接受本次打开所选出的常规双槽恢复数据。
        /// </summary>
        /// <returns>确认结果。</returns>
        public SaveResult AcceptRecovery()
        {
            if (_disposed || _closing) return Closed();
            _accepted = true;
            return SaveResult.Ok();
        }

        /// <summary>
        /// 在游戏初始化成功后允许保存。
        /// </summary>
        /// <returns>激活结果。</returns>
        public SaveResult Activate()
        {
            if (_disposed || _closing) return Closed();
            if (_active) return SaveResult.Ok();
            if (!_accepted) return SaveResult.Fail(SaveError.RecoveryRequired, "需要先确认存档恢复。");
            if (_snapshot == null) return SaveResult.Fail(SaveError.InvalidArgument, "需要先绑定存档快照生成器。");
            _active = true;
            _phase = SavePhase.Idle;
            _dirtySince = _changedAt = _clock();
            Notify();
            return SaveResult.Ok();
        }

        /// <summary>
        /// 标记持久字段发生变化；保存中的变化会保留到下一次提交。
        /// </summary>
        /// <returns>标记结果。</returns>
        public SaveResult MarkDirty()
        {
            if (_disposed || _closing) return Closed();
            if (_revision == long.MaxValue) return SaveResult.Fail(SaveError.Conflict, "存档修订号已耗尽。");
            double now = _clock();
            if (!Dirty) _dirtySince = now;
            _changedAt = now;
            _revision++;
            Notify();
            return SaveResult.Ok();
        }

        /// <summary>
        /// 等待调用时的修订号已被持久化；取消仅结束调用方等待。
        /// </summary>
        /// <param name="ct">等待取消令牌。</param>
        /// <returns>保存结果和修订号回执。</returns>
        public UniTask<SaveFlushResult> FlushAsync(CancellationToken ct = default) => FlushCoreAsync(_revision, ct);

        /// <summary>
        /// 手动重试；先核对结果不确定的候选，原物理操作在途时禁止再次写入。
        /// </summary>
        /// <param name="ct">等待取消令牌。</param>
        /// <returns>保存回执。</returns>
        public async UniTask<SaveFlushResult> RetryAsync(CancellationToken ct = default)
        {
            long target = _revision;
            if (_disposed) return Receipt(Closed(), target);
            if (!_active) return Receipt(SaveResult.Fail(SaveError.NotActive, "存档会话尚未激活。"), target);
            if (ct.IsCancellationRequested) return Receipt(Cancelled(), target);
            if (_lastResult.Error == SaveError.Conflict || _lastResult.Error == SaveError.FormatUnsupported ||
                _lastResult.Error == SaveError.VersionTooNew) return Receipt(_lastResult, target);
            if (_running) return _phase == SavePhase.Uncertain ? Receipt(_lastResult, target) : await FlushCoreAsync(target, ct);
            if (_candidate != null)
            {
                StartPhysical(true);
                while (_running && !_disposed)
                {
                    if (ct.IsCancellationRequested) return Receipt(Cancelled(), target);
                    CheckTimeout();
                    if (_phase == SavePhase.Uncertain) return Receipt(_lastResult, target);
                    await UniTask.Yield(PlayerLoopTiming.Update);
                }
                if (_disposed) return Receipt(Closed(), target);
                if (_phase == SavePhase.Uncertain) return Receipt(_lastResult, target);
                if (!_lastResult.Success && _lastResult.Error != SaveError.WriteFailed) return Receipt(_lastResult, target);
            }
            _attempts = 0;
            _phase = SavePhase.Idle;
            _lastResult = SaveResult.Ok();
            return await FlushCoreAsync(target, ct);
        }

        /// <summary>
        /// 冻结会话的新变更并保存最终修订号，成功后释放会话；失败则保留会话供重试。
        /// </summary>
        /// <param name="ct">等待取消令牌。</param>
        /// <returns>关闭结果。</returns>
        public async UniTask<SaveResult> CloseAsync(CancellationToken ct = default)
        {
            if (_disposed) return _closedSaved ? SaveResult.Ok() : _running ?
                SaveResult.Fail(SaveError.Uncertain, "关闭后的写入仍在收束。") : Closed();
            if (_closing) return SaveResult.Fail(SaveError.Conflict, "存档正在关闭。");
            _closing = true;
            SaveFlushResult flush = await FlushCoreAsync(_revision, ct);
            if (!flush.Result.Success) { _closing = false; return flush.Result; }
            while (_running)
            {
                if (ct.IsCancellationRequested) { _closing = false; return Cancelled(); }
                CheckTimeout();
                if (_phase == SavePhase.Uncertain) { _closing = false; return _lastResult; }
                await UniTask.Yield(PlayerLoopTiming.Update);
            }
            _closedSaved = true;
            Dispose();
            return SaveResult.Ok();
        }

        /// <summary>
        /// 同步结束调度；已在途的物理写入继续收束，期间仍占用存档身份。
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _active = false;
            _snapshot = null;
            StatusChanged = null;
            ReleaseIfQuiescent();
        }

        /// <summary>
        /// 按单调时钟推进自动保存；不受游戏时间缩放影响。
        /// </summary>
        public void Tick()
        {
            if (_disposed) return;
            CheckTimeout();
            if (!_active || _running || _closing || !Dirty || !_policy.AutoSave || _phase == SavePhase.Uncertain) return;
            double now = _clock();
            if (_phase == SavePhase.Error)
            {
                if (!_lastResult.CanRetry || _attempts >= _policy.MaxAttempts || now < _retryAt) return;
                _phase = SavePhase.Idle;
            }
            else if (now - _changedAt < _policy.DebounceSeconds && now - _dirtySince < _policy.MaxWaitSeconds) return;
            BeginWrite();
        }

        /// <summary>
        /// 切后台立即尝试保存；回前台继续处理到期任务。
        /// </summary>
        /// <param name="background">是否进入后台。</param>
        public void OnBackground(bool background)
        {
            if (_disposed) return;
            if (background && _policy.FlushOnBackground && _active && Dirty && !_running && _phase == SavePhase.Idle) BeginWrite();
            if (!background) Tick();
        }

        /// <summary>
        /// 执行指定修订号的保存屏障。
        /// </summary>
        /// <param name="target">目标修订号。</param>
        /// <param name="ct">等待取消令牌。</param>
        /// <returns>保存回执。</returns>
        private async UniTask<SaveFlushResult> FlushCoreAsync(long target, CancellationToken ct)
        {
            if (!_active && !_disposed) return Receipt(SaveResult.Fail(SaveError.NotActive, "存档会话尚未激活。"), target);
            while (true)
            {
                if (_disposed) return Receipt(Closed(), target);
                if (ct.IsCancellationRequested) return Receipt(Cancelled(), target);
                if (_savedRevision >= target) return Receipt(SaveResult.Ok(), target);
                CheckTimeout();
                if (_phase == SavePhase.Error || _phase == SavePhase.Uncertain) return Receipt(_lastResult, target);
                if (!_running) BeginWrite();
                if (_phase == SavePhase.Error || _phase == SavePhase.Uncertain) return Receipt(_lastResult, target);
                if (_savedRevision >= target) continue;
                await UniTask.Yield(PlayerLoopTiming.Update);
            }
        }

        /// <summary>
        /// 同步捕获并编码快照，随后启动唯一物理写入。
        /// </summary>
        private void BeginWrite()
        {
            if (_running || _disposed || !_active || !Dirty) return;
            _candidateRevision = _revision;
            T snapshot;
            try { snapshot = _snapshot(); }
            catch (Exception error)
            {
                Fail(SaveResult.Fail(SaveError.ValidationFailed, "生成存档快照失败：" + error.Message));
                return;
            }
            if (_disposed) return;
            SaveResult prepared = _repository.Prepare(snapshot, out _candidate);
            if (!prepared.Success) { _candidate = null; Fail(prepared); return; }
            _attempts++;
            StartPhysical(false);
        }

        /// <summary>
        /// 启动不会因调用方取消而被遗弃的物理任务。
        /// </summary>
        /// <param name="reconcile">是否仅核对上次候选。</param>
        private void StartPhysical(bool reconcile)
        {
            _running = true;
            _startedAt = _clock();
            _phase = SavePhase.Saving;
            _lastResult = SaveResult.Ok();
            RunPhysicalAsync(reconcile).Forget();
            if (_running) Notify();
        }

        /// <summary>
        /// 等待底层真实完成，再修改保存基线和释放占用。
        /// </summary>
        /// <param name="reconcile">是否仅核对候选。</param>
        /// <returns>物理操作收束任务。</returns>
        private async UniTask RunPhysicalAsync(bool reconcile)
        {
            SaveResult result;
            try { result = reconcile ? await _repository.ReconcileAsync(_candidate) : await _repository.CommitAsync(_candidate); }
            catch (Exception error)
            {
                result = SaveResult.Fail(SaveError.Uncertain, "存档操作异常，需要核对写入结果：" + error.Message);
            }
            await UniTask.SwitchToMainThread();
            if (result.Success)
            {
                _savedRevision = Math.Max(_savedRevision, _candidateRevision);
                _candidate = null;
                _attempts = 0;
                _phase = SavePhase.Idle;
                _lastResult = result;
            }
            else
            {
                if (result.Error != SaveError.Uncertain) _candidate = null;
                SetFailure(result);
            }
            _running = false;
            Notify();
            ReleaseIfQuiescent();
        }

        /// <summary>
        /// 检查等待时限；超时不会终止底层写入或释放占用。
        /// </summary>
        private void CheckTimeout()
        {
            if (!_running || _phase == SavePhase.Uncertain || _clock() - _startedAt < _policy.OperationTimeoutSeconds) return;
            _phase = SavePhase.Uncertain;
            _lastResult = SaveResult.Fail(SaveError.Uncertain, "存档操作超时，正在等待底层写入收束。");
            Notify();
        }

        /// <summary>
        /// 保存失败状态并通知订阅者。
        /// </summary>
        /// <param name="result">失败结果。</param>
        private void Fail(SaveResult result)
        {
            SetFailure(result);
            Notify();
        }

        /// <summary>
        /// 记录失败和退避时间。
        /// </summary>
        /// <param name="result">失败结果。</param>
        private void SetFailure(SaveResult result)
        {
            _lastResult = result;
            _phase = result.Error == SaveError.Uncertain ? SavePhase.Uncertain : SavePhase.Error;
            _retryAt = _clock() + _policy.RetryBaseDelaySeconds * Math.Pow(2, Math.Min(10, Math.Max(0, _attempts - 1)));
            if (!_disposed) GameEntry.Get<ILogModule>()?.Warn("Save", result.Message);
        }

        /// <summary>
        /// 发送状态通知，监听器异常不能破坏已经完成的存档提交。
        /// </summary>
        private void Notify()
        {
            if (_disposed || StatusChanged == null) return;
            SaveSessionStatus status = GetStatus();
            foreach (Action<SaveSessionStatus> listener in StatusChanged.GetInvocationList())
            {
                try { listener(status); }
                catch (Exception error) { GameEntry.Get<ILogModule>()?.Warn("Save", "存档状态监听失败：" + error.Message); }
            }
        }

        /// <summary>
        /// 确认不会再发生迟到写入后释放身份。
        /// </summary>
        private void ReleaseIfQuiescent()
        {
            if (!_disposed || _running || _released) return;
            _released = true;
            _release();
        }

        /// <summary>
        /// 创建保存回执。
        /// </summary>
        /// <param name="result">操作结果。</param>
        /// <param name="target">等待目标。</param>
        /// <returns>保存回执。</returns>
        private SaveFlushResult Receipt(SaveResult result, long target) => new(result, _repository.Sequence, target, _savedRevision);

        /// <summary>
        /// 创建会话关闭结果。
        /// </summary>
        /// <returns>关闭错误。</returns>
        private static SaveResult Closed() => SaveResult.Fail(SaveError.Closed, "存档会话已关闭或正在关闭。");

        /// <summary>
        /// 创建等待取消结果。
        /// </summary>
        /// <returns>取消结果。</returns>
        private static SaveResult Cancelled() => SaveResult.Fail(SaveError.Cancelled, "已取消等待存档操作。");
    }
}
