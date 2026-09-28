using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace GameFramework
{
    /// <summary>
    /// 玩法管理模块默认实现。支持多个玩法实例同时运行，并由框架 Update 统一驱动。
    /// </summary>
    public sealed class GameplayModule : ModuleBase, IGameplayModule
    {
        private const string Tag = "Gameplay";

        /// <inheritdoc />
        public override int Priority => 60;

        private int _nextId = 1;
        private readonly List<Entry> _entries = new(16);
        private ILogModule _log;
        private int _generation;
        private bool _isPaused;
        private bool _isApplicationPaused;

        /// <inheritdoc />
        public bool IsPaused => _isPaused;

        /// <inheritdoc />
        public bool IsApplicationPaused => _isApplicationPaused;

        /// <inheritdoc />
        /// <param name="paused">是否暂停玩法。</param>
        public void SetPaused(bool paused)
        {
            _isPaused = paused;
            GameEntry.RefreshGameplayPause();
        }

        /// <inheritdoc />
        /// <param name="paused">应用是否处于后台暂停状态。</param>
        public void SetApplicationPaused(bool paused)
        {
            _isApplicationPaused = paused;
            GameEntry.RefreshGameplayPause();
        }

        /// <inheritdoc />
        public int Count => _entries.Count;

        /// <inheritdoc />
        public override void OnInit()
        {
            _generation++;
            _log = GameEntry.Get<ILogModule>();
        }

        /// <summary>
        /// 驱动运行中玩法的普通帧阶段。
        /// </summary>
        /// <param name="deltaTime">当前帧的时间间隔。</param>
        public override void OnUpdate(float deltaTime) => DispatchPhase(deltaTime, UpdatePhase.Update);

        /// <summary>
        /// 驱动玩法固定帧阶段。
        /// </summary>
        /// <param name="deltaTime">固定帧时间间隔（秒）。</param>
        public override void OnFixedUpdate(float deltaTime) => DispatchPhase(deltaTime, UpdatePhase.Fixed);

        /// <summary>
        /// 驱动玩法帧末阶段。
        /// </summary>
        /// <param name="deltaTime">距上一帧的时间间隔（秒）。</param>
        public override void OnLateUpdate(float deltaTime) => DispatchPhase(deltaTime, UpdatePhase.Late);

        /// <summary>
        /// 在当前生命周期和暂停状态有效时派发指定玩法阶段。
        /// </summary>
        /// <param name="deltaTime">当前阶段的时间间隔。</param>
        /// <param name="phase">待派发阶段。</param>
        private void DispatchPhase(float deltaTime, UpdatePhase phase)
        {
            int generation = _generation;
            for (int i = 0; i < _entries.Count && _generation == generation && CanDispatch();)
            {
                Entry entry = _entries[i];
                if (!entry.IsStopping && entry.Gameplay.IsActive)
                {
                    try
                    {
                        if (phase == UpdatePhase.Update) entry.Gameplay.Update(deltaTime);
                        else if (phase == UpdatePhase.Fixed && entry.Gameplay is IFixedUpdateGameplay fixedGameplay)
                            fixedGameplay.FixedUpdate(deltaTime);
                        else if (phase == UpdatePhase.Late && entry.Gameplay is ILateUpdateGameplay lateGameplay)
                            lateGameplay.LateUpdate(deltaTime);
                    }
                    catch (Exception ex)
                    {
                        _log?.Error(Tag, $"Gameplay \"{entry.Gameplay.Name}\" {phase} update failed: {ex}");
                        ForceStopEntry(entry);
                    }
                }

                if (i < _entries.Count && ReferenceEquals(_entries[i], entry)) i++;
            }
        }

        /// <summary>
        /// 检查框架及玩法是否仍可继续当前阶段。
        /// </summary>
        /// <returns>当前阶段是否可继续派发。</returns>
        private bool CanDispatch() => GameEntry.IsRunning && GameEntry.ApplicationScope?.IsActive == true &&
            !_isPaused && !_isApplicationPaused && !GameEntry.IsInBackground;

        private enum UpdatePhase { Update, Fixed, Late }

        /// <inheritdoc />
        public override void OnShutdown()
        {
            _generation++;
            for (int i = _entries.Count - 1; i >= 0; i--)
                ForceShutdownEntry(_entries[i]);
            _isPaused = false;
            _isApplicationPaused = false;
            _log = null;
        }

        /// <inheritdoc />
        public GameplayHandle Start(IGameplay gameplay, GameplayEnterData enterData = null)
        {
            if (gameplay == null) throw new ArgumentNullException(nameof(gameplay));

            var entry = AddEntry(gameplay);
            StartEntryAsync(entry, enterData, CancellationToken.None).Forget(OnAsyncFailed);
            return new GameplayHandle(entry.Id);
        }

        /// <inheritdoc />
        public async UniTask<GameplayHandle> StartAsync(
            IGameplay gameplay,
            GameplayEnterData enterData = null,
            CancellationToken ct = default)
        {
            if (gameplay == null) throw new ArgumentNullException(nameof(gameplay));

            var entry = AddEntry(gameplay);
            await StartEntryAsync(entry, enterData, ct);
            return new GameplayHandle(entry.Id);
        }

        /// <inheritdoc />
        public void Stop(GameplayHandle handle)
        {
            StopAsync(handle).Forget(OnAsyncFailed);
        }

        /// <inheritdoc />
        public async UniTask StopAsync(GameplayHandle handle, CancellationToken ct = default)
        {
            if (!TryFindEntry(handle, out var entry)) return;

            await StopEntry(entry, ct);
        }

        /// <inheritdoc />
        public void StopAll()
        {
            StopAllAsync().Forget(OnAsyncFailed);
        }

        /// <inheritdoc />
        public async UniTask StopAllAsync(CancellationToken ct = default)
        {
            while (_entries.Count > 0)
            {
                var entry = _entries[_entries.Count - 1];
                await StopAsync(new GameplayHandle(entry.Id), ct);
            }
        }

        /// <inheritdoc />
        public bool Contains(GameplayHandle handle)
        {
            return TryFindEntry(handle, out _);
        }

        /// <inheritdoc />
        public bool TryGetGameplay(GameplayHandle handle, out IGameplay gameplay)
        {
            if (TryFindEntry(handle, out var entry))
            {
                gameplay = entry.Gameplay;
                return true;
            }

            gameplay = null;
            return false;
        }

        private Entry AddEntry(IGameplay gameplay)
        {
            if (IsManaged(gameplay))
                throw new InvalidOperationException($"Gameplay \"{gameplay.Name}\" is already managed.");
            if (gameplay.IsDestroyed)
                throw new InvalidOperationException($"Gameplay \"{gameplay.Name}\" has been destroyed.");

            var entry = new Entry(_nextId++, gameplay);
            _entries.Add(entry);
            return entry;
        }

        /// <summary>
        /// 初始化并进入玩法。
        /// </summary>
        /// <param name="entry">玩法条目。</param>
        /// <param name="enterData">进入参数。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>启动流程任务。</returns>
        private async UniTask StartEntryAsync(Entry entry, GameplayEnterData enterData, CancellationToken ct)
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(entry.LifecycleCts.Token, ct);
            try
            {
                await entry.Gameplay.InitGameplayAsync(linkedCts.Token);
                linkedCts.Token.ThrowIfCancellationRequested();
                await entry.Gameplay.EnterGameplayAsync(enterData, linkedCts.Token);
                linkedCts.Token.ThrowIfCancellationRequested();
                _log?.Info(Tag, $"Gameplay \"{entry.Gameplay.Name}\" started.");
            }
            catch
            {
                if (!entry.IsStopping)
                    ForceStopEntry(entry);
                throw;
            }
            finally
            {
                entry.StartCompletion.TrySetResult();
            }
        }

        private bool TryFindEntry(GameplayHandle handle, out Entry entry)
        {
            if (!handle.IsValid)
            {
                entry = null;
                return false;
            }

            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].Id != handle.Id) continue;
                entry = _entries[i];
                return true;
            }

            entry = null;
            return false;
        }

        private bool IsManaged(IGameplay gameplay)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].Gameplay == gameplay) return true;
            }
            return false;
        }

        /// <summary>
        /// 启动或复用玩法的停止任务。
        /// </summary>
        /// <param name="entry">待停止的玩法条目。</param>
        /// <param name="ct">退出操作的取消令牌。</param>
        /// <returns>共享的停止任务。</returns>
        private UniTask StopEntry(Entry entry, CancellationToken ct)
        {
            if (entry.StopCompletion != null) return entry.StopCompletion.Task;

            entry.IsStopping = true;
            entry.StopCompletion = new UniTaskCompletionSource();
            entry.LifecycleCts.Cancel();
            StopEntryAsync(entry, ct).Forget(OnAsyncFailed);
            return entry.StopCompletion.Task;
        }

        /// <summary>
        /// 等待启动完成后执行退出和销毁。
        /// </summary>
        /// <param name="entry">待停止的玩法条目。</param>
        /// <param name="ct">退出操作的取消令牌。</param>
        /// <returns>停止流程任务。</returns>
        private async UniTask StopEntryAsync(Entry entry, CancellationToken ct)
        {
            try
            {
                await entry.StartCompletion.Task;
                if (entry.IsShutdown) return;
                if (entry.Gameplay.IsActive)
                    await entry.Gameplay.ExitGameplayAsync(ct);
                else
                    entry.Gameplay.ForceExit();
                if (entry.IsShutdown) return;
            }
            catch (Exception ex)
            {
                entry.StopFailure = ex;
            }
            finally
            {
                if (!entry.IsShutdown)
                {
                    try
                    {
                        entry.Gameplay.DestroyGameplay();
                    }
                    catch (Exception ex)
                    {
                        entry.StopFailure ??= ex;
                    }
                    finally
                    {
                        RemoveEntry(entry);
                        if (entry.StopFailure == null)
                            entry.StopCompletion.TrySetResult();
                        else
                            entry.StopCompletion.TrySetException(entry.StopFailure);
                    }
                }
            }
        }

        /// <summary>
        /// 模块关闭时同步取消并销毁玩法。
        /// </summary>
        /// <param name="entry">待关闭的玩法条目。</param>
        private void ForceShutdownEntry(Entry entry)
        {
            entry.IsShutdown = true;
            entry.IsStopping = true;
            entry.LifecycleCts.Cancel();
            try
            {
                entry.Gameplay.ForceExit();
            }
            catch (Exception ex)
            {
                entry.StopFailure ??= ex;
            }

            try
            {
                entry.Gameplay.DestroyGameplay();
            }
            catch (Exception ex)
            {
                entry.StopFailure ??= ex;
            }

            RemoveEntry(entry);
            if (entry.StopFailure != null && entry.StopCompletion == null)
                _log?.Error(Tag, $"Gameplay \"{entry.Gameplay.Name}\" shutdown failed: {entry.StopFailure}");
            if (entry.StopFailure == null)
                entry.StopCompletion?.TrySetResult();
            else
                entry.StopCompletion?.TrySetException(entry.StopFailure);
        }

        /// <summary>
        /// 同步强制停止更新失败的玩法。
        /// </summary>
        /// <param name="entry">玩法条目。</param>
        private void ForceStopEntry(Entry entry)
        {
            if (entry.IsStopping) return;
            entry.IsStopping = true;
            entry.LifecycleCts.Cancel();
            entry.Gameplay.ForceExit();
            entry.Gameplay.DestroyGameplay();
            RemoveEntry(entry);
        }

        private void RemoveEntry(Entry entry)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i] != entry) continue;

                _entries.RemoveAt(i);
                entry.Dispose();
                return;
            }
        }

        private void OnAsyncFailed(Exception ex)
        {
            if (ex is OperationCanceledException) return;
            _log?.Error(Tag, $"Gameplay async flow failed: {ex}");
        }

        private sealed class Entry : IDisposable
        {
            public readonly int Id;
            public readonly IGameplay Gameplay;
            public readonly CancellationTokenSource LifecycleCts = new();
            public bool IsStopping;
            public bool IsShutdown;
            public readonly UniTaskCompletionSource StartCompletion = new();
            public UniTaskCompletionSource StopCompletion;
            public Exception StopFailure;

            public Entry(int id, IGameplay gameplay)
            {
                Id = id;
                Gameplay = gameplay;
            }

            public void Dispose()
            {
                LifecycleCts.Dispose();
            }
        }
    }
}
