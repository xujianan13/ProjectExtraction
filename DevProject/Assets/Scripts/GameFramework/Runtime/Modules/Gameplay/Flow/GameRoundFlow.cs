using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace GameFramework
{
    /// <summary>
    /// 表示一局游戏所处的阶段。
    /// </summary>
    public enum GameRoundState { Menu, Loading, Playing, Paused, Settling, Results, Returning, Failed, Disposed }

    /// <summary>
    /// 保存一局游戏的结算数据。
    /// </summary>
    public readonly struct GameRoundResult
    {
        /// <summary>
        /// 创建结算数据。
        /// </summary>
        /// <param name="roundId">本局唯一标识。</param>
        /// <param name="levelId">关卡标识。</param>
        /// <param name="won">是否获胜。</param>
        /// <param name="score">本局分数。</param>
        /// <param name="elapsedSeconds">本局耗时秒数。</param>
        public GameRoundResult(string roundId, int levelId, bool won, int score, float elapsedSeconds)
        {
            RoundId = roundId;
            LevelId = levelId;
            Won = won;
            Score = score;
            ElapsedSeconds = elapsedSeconds;
        }

        /// <summary>
        /// 获取本局唯一标识。
        /// </summary>
        public string RoundId { get; }
        /// <summary>
        /// 获取关卡标识。
        /// </summary>
        public int LevelId { get; }
        /// <summary>
        /// 获取胜利状态。
        /// </summary>
        public bool Won { get; }
        /// <summary>
        /// 获取本局分数。
        /// </summary>
        public int Score { get; }
        /// <summary>
        /// 获取本局耗时秒数。
        /// </summary>
        public float ElapsedSeconds { get; }
    }

    /// <summary>
    /// 定义单局内容的进入、退出和暂停契约。
    /// </summary>
    public interface IGameRoundSession : IDisposable
    {
        /// <summary>
        /// 一局完成时触发。
        /// </summary>
        event Action<GameRoundResult> Completed;
        /// <summary>
        /// 进入本局并完成加载。
        /// </summary>
        /// <param name="ct">加载取消令牌。</param>
        /// <returns>异步进入操作。</returns>
        UniTask EnterAsync(CancellationToken ct);
        /// <summary>
        /// 退出本局并释放异步资源。
        /// </summary>
        /// <param name="ct">退出取消令牌。</param>
        /// <returns>异步退出操作。</returns>
        UniTask ExitAsync(CancellationToken ct);
        /// <summary>
        /// 设置本局暂停状态。
        /// </summary>
        /// <param name="paused">是否暂停。</param>
        void SetPaused(bool paused);
    }

    /// <summary>
    /// 创建关卡会话并提供关卡顺序。
    /// </summary>
    public interface IGameRoundFactory
    {
        /// <summary>
        /// 获取首关标识。
        /// </summary>
        int FirstLevelId { get; }
        /// <summary>
        /// 检查关卡是否存在。
        /// </summary>
        /// <param name="levelId">关卡标识。</param>
        /// <returns>关卡是否存在。</returns>
        bool ContainsLevel(int levelId);
        /// <summary>
        /// 获取下一关标识，零表示没有下一关。
        /// </summary>
        /// <param name="levelId">当前关卡标识。</param>
        /// <returns>下一关标识或零。</returns>
        int GetNextLevelId(int levelId);
        /// <summary>
        /// 创建新的单局会话。
        /// </summary>
        /// <param name="levelId">关卡标识。</param>
        /// <param name="roundId">本局唯一标识。</param>
        /// <returns>新会话。</returns>
        IGameRoundSession Create(int levelId, string roundId);
    }

    /// <summary>
    /// 串行控制单局加载、结算与退出。
    /// </summary>
    public sealed class GameRoundFlow : IDisposable
    {
        private readonly IGameRoundFactory _factory;
        private readonly Func<GameRoundResult, CancellationToken, UniTask> _saveResult;
        private IGameRoundSession _session;
        private CancellationTokenSource _operationCts;
        private UniTaskCompletionSource _idle;
        private UniTaskCompletionSource<bool> _closeDone;
        private bool _busy;
        private bool _closing;
        private bool _sessionExited;
        private bool _saved;
        private bool _loadingCancelled;

        /// <summary>
        /// 创建单局流程控制器。
        /// </summary>
        /// <param name="factory">关卡会话工厂。</param>
        /// <param name="saveResult">结算保存函数。</param>
        public GameRoundFlow(IGameRoundFactory factory, Func<GameRoundResult, CancellationToken, UniTask> saveResult = null)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _saveResult = saveResult;
            State = GameRoundState.Menu;
        }

        /// <summary>
        /// 流程状态变化时触发。
        /// </summary>
        public event Action<GameRoundState> StateChanged;
        /// <summary>
        /// 获取当前流程状态。
        /// </summary>
        public GameRoundState State { get; private set; }
        /// <summary>
        /// 获取当前关卡标识。
        /// </summary>
        public int CurrentLevelId { get; private set; }
        /// <summary>
        /// 获取当前单局标识。
        /// </summary>
        public string CurrentRoundId { get; private set; }
        /// <summary>
        /// 获取已收到的结算结果。
        /// </summary>
        public GameRoundResult? Result { get; private set; }
        /// <summary>
        /// 获取最近的失败信息。
        /// </summary>
        public string Error { get; private set; }
        /// <summary>
        /// 获取是否可以进入下一关。
        /// </summary>
        public bool HasNextLevel
        {
            get
            {
                if (State != GameRoundState.Results || !Result.HasValue || !Result.Value.Won) return false;
                try { return _factory.GetNextLevelId(CurrentLevelId) is int next && next != 0 && _factory.ContainsLevel(next); }
                catch (Exception exception)
                {
                    Log(exception);
                    return false;
                }
            }
        }

        /// <summary>
        /// 获取流程是否正在转场。
        /// </summary>
        public bool IsBusy => _busy;

        /// <summary>
        /// 从菜单加载指定关卡。
        /// </summary>
        /// <param name="levelId">目标关卡标识。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>是否成功进入关卡。</returns>
        public async UniTask<bool> StartLevelAsync(int levelId, CancellationToken ct = default)
        {
            if (_closing || _busy || State != GameRoundState.Menu || ct.IsCancellationRequested) return false;
            try { if (!_factory.ContainsLevel(levelId)) return false; }
            catch (Exception exception)
            {
                Fail(exception);
                return false;
            }
            Begin(ct);
            CurrentLevelId = levelId;
            CurrentRoundId = Guid.NewGuid().ToString("N");
            Result = null;
            _saved = false;
            _sessionExited = false;
            _loadingCancelled = false;
            Error = null;
            SetState(GameRoundState.Loading);
            if (_closing)
            {
                End();
                return false;
            }
            try
            {
                _session = _factory.Create(levelId, CurrentRoundId);
                if (_session == null) throw new InvalidOperationException("关卡工厂返回空会话。");
                _session.Completed += OnCompleted;
                await _session.EnterAsync(_operationCts.Token);
                if (_operationCts.IsCancellationRequested || _closing || State == GameRoundState.Disposed)
                {
                    await CleanupSessionAsync();
                    if (!_closing) ResetToMenu();
                    return false;
                }
                SetState(GameRoundState.Playing);
                return !_closing && State == GameRoundState.Playing;
            }
            catch (OperationCanceledException) when (_operationCts.IsCancellationRequested || ct.IsCancellationRequested)
            {
                await CleanupAfterLoadFailureAsync();
                if (!_closing) ResetToMenu();
                return false;
            }
            catch (Exception exception)
            {
                await CleanupAfterLoadFailureAsync();
                if (!_closing && !_loadingCancelled) Fail(exception);
                else if (!_closing) ResetToMenu();
                return false;
            }
            finally { End(); }
        }

        /// <summary>
        /// 重试失败结算或重新开始当前关卡。
        /// </summary>
        /// <param name="ct">取消令牌。</param>
        /// <returns>重试是否成功。</returns>
        public async UniTask<bool> RetryAsync(CancellationToken ct = default)
        {
            if (_closing || _busy || ct.IsCancellationRequested) return false;
            if (State == GameRoundState.Failed && Result.HasValue) return await SettleAsync(ct);
            if (State != GameRoundState.Results && State != GameRoundState.Failed) return false;
            int levelId = CurrentLevelId;
            if (!await ReturnToMenuAsync(ct)) return false;
            return await StartLevelAsync(levelId, ct);
        }

        /// <summary>
        /// 胜利后加载下一关。
        /// </summary>
        /// <param name="ct">取消令牌。</param>
        /// <returns>是否成功进入下一关。</returns>
        public async UniTask<bool> NextLevelAsync(CancellationToken ct = default)
        {
            if (_closing || _busy || !HasNextLevel || ct.IsCancellationRequested) return false;
            int next;
            try { next = _factory.GetNextLevelId(CurrentLevelId); }
            catch (Exception exception)
            {
                Fail(exception);
                return false;
            }
            if (!await ReturnToMenuAsync(ct)) return false;
            return await StartLevelAsync(next, ct);
        }

        /// <summary>
        /// 完成资源释放并返回菜单。
        /// </summary>
        /// <param name="ct">取消令牌。</param>
        /// <returns>是否返回菜单。</returns>
        public async UniTask<bool> ReturnToMenuAsync(CancellationToken ct = default)
        {
            if (_closing || _busy || State == GameRoundState.Disposed || ct.IsCancellationRequested) return false;
            if (State == GameRoundState.Menu) return true;
            if (Result.HasValue && !_saved) return false;
            Begin(ct);
            SetState(GameRoundState.Returning);
            try
            {
                if (_closing || State == GameRoundState.Disposed) return false;
                if (_session != null) await ExitSessionAsync();
                ResetToMenu();
                return true;
            }
            catch (Exception exception)
            {
                Fail(exception);
                return false;
            }
            finally { End(); }
        }

        /// <summary>
        /// 取消正在加载的关卡。
        /// </summary>
        public void CancelLoading()
        {
            if (State != GameRoundState.Loading || !_busy) return;
            _loadingCancelled = true;
            _operationCts?.Cancel();
        }

        /// <summary>
        /// 暂停当前单局。
        /// </summary>
        /// <returns>是否成功暂停。</returns>
        public bool Pause() => SetPaused(true, GameRoundState.Playing, GameRoundState.Paused);

        /// <summary>
        /// 恢复当前单局。
        /// </summary>
        /// <returns>是否成功恢复。</returns>
        public bool Resume() => SetPaused(false, GameRoundState.Paused, GameRoundState.Playing);

        /// <summary>
        /// 等待当前转场并彻底关闭流程。
        /// </summary>
        /// <param name="ct">保留的调用方取消令牌；关闭仍等待资源清理完成。</param>
        /// <returns>是否完成关闭。</returns>
        public async UniTask<bool> CloseAsync(CancellationToken ct = default)
        {
            if (State == GameRoundState.Disposed) return true;
            if (_closeDone != null) return await _closeDone.Task;
            _closeDone = new UniTaskCompletionSource<bool>();
            _closing = true;
            _operationCts?.Cancel();
            if (_busy && _idle != null) await _idle.Task;
            if (State == GameRoundState.Disposed)
            {
                _closeDone.TrySetResult(true);
                return true;
            }
            try
            {
                if (_session != null) await ExitSessionAsync();
                Dispose();
                _closeDone.TrySetResult(true);
                return true;
            }
            catch (Exception exception)
            {
                Fail(exception);
                _closing = false;
                _closeDone.TrySetResult(false);
                _closeDone = null;
                return false;
            }
        }

        /// <summary>
        /// 同步取消操作并兜底释放会话。
        /// </summary>
        public void Dispose()
        {
            if (State == GameRoundState.Disposed) return;
            _closing = true;
            _operationCts?.Cancel();
            if (_session != null)
            {
                IGameRoundSession session = _session;
                _session = null;
                session.Completed -= OnCompleted;
                try { session.Dispose(); }
                catch (Exception exception) { Log(exception); }
            }
            SetState(GameRoundState.Disposed);
        }

        /// <summary>
        /// 收到本局完成事件并立即开始结算。
        /// </summary>
        /// <param name="result">本局结果。</param>
        private void OnCompleted(GameRoundResult result)
        {
            if (_closing || _busy || State != GameRoundState.Playing || result.RoundId != CurrentRoundId || result.LevelId != CurrentLevelId) return;
            Result = result;
            Begin(CancellationToken.None);
            SetState(GameRoundState.Settling);
            try { _session?.SetPaused(true); }
            catch (Exception exception) { Log(exception); }
            CompleteAsync().Forget();
        }

        /// <summary>
        /// 完成事件触发的异步结算。
        /// </summary>
        /// <returns>异步结算操作。</returns>
        private async UniTaskVoid CompleteAsync()
        {
            try { await SettleCoreAsync(); }
            finally { End(); }
        }

        /// <summary>
        /// 重试当前结算。
        /// </summary>
        /// <param name="ct">重试取消令牌。</param>
        /// <returns>结算是否成功。</returns>
        private async UniTask<bool> SettleAsync(CancellationToken ct)
        {
            Begin(ct);
            SetState(GameRoundState.Settling);
            try { return await SettleCoreAsync(); }
            finally { End(); }
        }

        /// <summary>
        /// 退出会话并保存当前结果。
        /// </summary>
        /// <returns>结算是否成功。</returns>
        private async UniTask<bool> SettleCoreAsync()
        {
            try
            {
                if (!_sessionExited && _session != null) await ExitSessionAsync();
                if (_closing || _operationCts.IsCancellationRequested) return CancelSettlement();
                if (!_saved && _saveResult != null) await _saveResult(Result.Value, _operationCts.Token);
                if (_closing || _operationCts.IsCancellationRequested) return CancelSettlement();
                _saved = true;
                Error = null;
                SetState(GameRoundState.Results);
                return true;
            }
            catch (OperationCanceledException) when (_operationCts.IsCancellationRequested)
            {
                return CancelSettlement();
            }
            catch (Exception exception)
            {
                Fail(exception);
                return false;
            }
        }

        /// <summary>
        /// 将已取消的结算转为可重试失败态。
        /// </summary>
        /// <returns>始终返回 false。</returns>
        private bool CancelSettlement()
        {
            if (!_closing && State != GameRoundState.Disposed)
            {
                Error = "结算已取消，可重试。";
                SetState(GameRoundState.Failed);
            }
            return false;
        }

        /// <summary>
        /// 退出当前会话并释放资源。
        /// </summary>
        /// <returns>异步退出操作。</returns>
        private async UniTask ExitSessionAsync()
        {
            IGameRoundSession session = _session;
            if (session == null) return;
            if (!_sessionExited)
            {
                await session.ExitAsync(CancellationToken.None);
                if (_session != session) return;
                _sessionExited = true;
            }
            session.Completed -= OnCompleted;
            session.Dispose();
            _session = null;
        }

        /// <summary>
        /// 清理失败加载留下的会话。
        /// </summary>
        /// <returns>异步清理操作。</returns>
        private async UniTask CleanupSessionAsync()
        {
            if (_session == null) return;
            try { await ExitSessionAsync(); }
            catch (Exception exception)
            {
                Log(exception);
                ForceDisposeSession();
            }
        }

        /// <summary>
        /// 清理加载失败后的会话。
        /// </summary>
        /// <returns>异步清理操作。</returns>
        private async UniTask CleanupAfterLoadFailureAsync() => await CleanupSessionAsync();

        /// <summary>
        /// 同步兜底释放当前会话。
        /// </summary>
        private void ForceDisposeSession()
        {
            IGameRoundSession session = _session;
            _session = null;
            if (session == null) return;
            session.Completed -= OnCompleted;
            try { session.Dispose(); }
            catch (Exception exception) { Log(exception); }
        }

        /// <summary>
        /// 切换当前会话的暂停状态。
        /// </summary>
        /// <param name="paused">是否暂停。</param>
        /// <param name="expected">预期当前状态。</param>
        /// <param name="next">目标状态。</param>
        /// <returns>是否成功切换。</returns>
        private bool SetPaused(bool paused, GameRoundState expected, GameRoundState next)
        {
            if (_closing || _busy || State != expected || _session == null) return false;
            try
            {
                _session.SetPaused(paused);
                if (State != expected) return false;
                SetState(next);
                return true;
            }
            catch (Exception exception)
            {
                Fail(exception);
                return false;
            }
        }

        /// <summary>
        /// 开启独占转场。
        /// </summary>
        /// <param name="ct">调用方取消令牌。</param>
        private void Begin(CancellationToken ct)
        {
            _busy = true;
            _idle = new UniTaskCompletionSource();
            _operationCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        }

        /// <summary>
        /// 结束独占转场。
        /// </summary>
        private void End()
        {
            _operationCts?.Dispose();
            _operationCts = null;
            _busy = false;
            _idle?.TrySetResult();
        }

        /// <summary>
        /// 清空单局数据并返回菜单。
        /// </summary>
        private void ResetToMenu()
        {
            if (State == GameRoundState.Disposed) return;
            CurrentLevelId = 0;
            CurrentRoundId = null;
            Result = null;
            Error = null;
            _saved = false;
            _sessionExited = false;
            SetState(GameRoundState.Menu);
        }

        /// <summary>
        /// 记录异常并进入失败态。
        /// </summary>
        /// <param name="exception">失败异常。</param>
        private void Fail(Exception exception)
        {
            Log(exception);
            if (State == GameRoundState.Disposed) return;
            Error = exception.Message;
            SetState(GameRoundState.Failed);
        }

        /// <summary>
        /// 更新状态并隔离订阅者异常。
        /// </summary>
        /// <param name="state">目标状态。</param>
        private void SetState(GameRoundState state)
        {
            if (State == GameRoundState.Disposed || State == state) return;
            State = state;
            Action<GameRoundState> handlers = StateChanged;
            if (handlers == null) return;
            foreach (Delegate handler in handlers.GetInvocationList())
            {
                if (State != state) break;
                try { ((Action<GameRoundState>)handler)(state); }
                catch (Exception exception) { Log(exception); }
            }
        }

        /// <summary>
        /// 通过框架日志记录异常。
        /// </summary>
        /// <param name="exception">待记录异常。</param>
        private static void Log(Exception exception)
        {
            try { GameEntry.Get<ILogModule>()?.Error("GameRoundFlow", exception.ToString()); }
            catch { }
        }
    }
}
