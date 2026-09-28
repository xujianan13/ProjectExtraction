using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace GameFramework
{
    /// <summary>
    /// 逻辑场景管理模块默认实现。支持 Replace / Push / Pop 三种切换模式与场景预加载。
    /// 切换期间的新请求会覆盖待处理请求（last wins），被覆盖的异步调用以取消结束。
    /// </summary>
    public sealed class SceneModule : ModuleBase, ISceneModule
    {
        private const string Tag = "Scene";

        /// <inheritdoc />
        public override int Priority => 50;

        private readonly Dictionary<string, IScene> _scenes = new(16);
        private readonly Stack<SceneStackEntry> _sceneStack = new(8);
        private SceneChangeRequest _currentRequest;
        private SceneChangeRequest _processingRequest;
        private bool _isChanging;
        private SceneEnterData _currentEnterData;
        private bool _isShutdown;

        private ILogModule _log;
        private IEventModule _events;

        /// <inheritdoc />
        public IScene CurrentScene { get; private set; }

        /// <inheritdoc />
        public int StackDepth => _sceneStack.Count;

        /// <inheritdoc />
        public bool IsChanging => _isChanging;

        /// <inheritdoc />
        public override void OnInit()
        {
            _isShutdown = false;
            _log = GameEntry.Get<ILogModule>();
            _events = GameEntry.Get<IEventModule>();
        }

        /// <inheritdoc />
        public override void OnShutdown()
        {
            _isShutdown = true;
            CancelPendingRequest();
            _processingRequest?.LinkedCts?.Cancel();
            var enteringRequest = _processingRequest;
            var enteringScene = enteringRequest?.EnteringScene;
            if (enteringScene != null)
                enteringRequest.EnteringSceneCleaned = true;
            CurrentScene?.ForceExit();
            if (enteringScene != CurrentScene)
                enteringScene?.ForceExit();
            CurrentScene = null;
            _currentEnterData = null;
            _sceneStack.Clear();
            _events = null;
            _log = null;
        }

        /// <inheritdoc />
        /// <param name="deltaTime">距上一帧的时间间隔。</param>
        public override void OnUpdate(float deltaTime)
        {
            if (GameEntry.IsGameplayPaused) return;
            CurrentScene?.Update(deltaTime);
        }

        /// <inheritdoc />
        public void RegisterScene(IScene scene)
        {
            if (scene == null) throw new ArgumentNullException(nameof(scene));
            if (_scenes.ContainsKey(scene.Name))
            {
                _log?.Error(Tag, $"RegisterScene failed, \"{scene.Name}\" already exists.");
                return;
            }

            _scenes.Add(scene.Name, scene);
        }

        /// <inheritdoc />
        public void UnregisterScene(string name)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("name is null or empty.", nameof(name));
            if (!_scenes.TryGetValue(name, out var scene))
            {
                _log?.Error(Tag, $"UnregisterScene failed, \"{name}\" not found.");
                return;
            }

            UnregisterScene(scene);
        }

        /// <inheritdoc />
        public void UnregisterScene(IScene scene)
        {
            if (scene == null) throw new ArgumentNullException(nameof(scene));
            if (!_scenes.TryGetValue(scene.Name, out var registered) || registered != scene)
            {
                _log?.Error(Tag, $"UnregisterScene failed, \"{scene.Name}\" not found.");
                return;
            }

            if (IsSceneInUse(scene))
            {
                _log?.Error(Tag, $"UnregisterScene failed, \"{scene.Name}\" is active or in stack.");
                return;
            }

            _scenes.Remove(scene.Name);
        }

        /// <inheritdoc />
        public IScene GetScene(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            return _scenes.TryGetValue(name, out var scene) ? scene : null;
        }

        /// <inheritdoc />
        public UniTask PreloadSceneAsync(string name, CancellationToken ct = default)
        {
            if (!TryResolveScene(name, out var scene))
                return UniTask.FromException(new InvalidOperationException($"Scene \"{name}\" not found."));
            return PreloadSceneAsync(scene, ct);
        }

        /// <inheritdoc />
        public async UniTask PreloadSceneAsync(IScene scene, CancellationToken ct = default)
        {
            if (!TryValidateRegisteredScene(scene))
                throw new InvalidOperationException($"Scene \"{scene?.Name}\" not registered.");
            await scene.PreloadSceneAsync(ct);
        }

        /// <inheritdoc />
        public void ExitCurrentScene()
        {
            ExitCurrentSceneAsync().Forget(OnFlowFailed);
        }

        /// <inheritdoc />
        /// <param name="ct">取消令牌。</param>
        /// <returns>场景退出任务。</returns>
        public async UniTask ExitCurrentSceneAsync(CancellationToken ct = default)
        {
            var completion = new UniTaskCompletionSource();
            EnqueueChange(SceneChangeMode.Replace, null, null, completion, ct, true);
            await completion.Task;
        }

        /// <inheritdoc />
        public void ChangeScene(string name, SceneEnterData enterData = null)
        {
            if (!TryResolveScene(name, out var scene)) return;
            ChangeScene(scene, enterData);
        }

        /// <inheritdoc />
        public void ChangeScene(IScene scene, SceneEnterData enterData = null)
        {
            if (!TryValidateRegisteredScene(scene)) return;
            EnqueueChange(SceneChangeMode.Replace, scene, enterData, null, CancellationToken.None);
        }

        /// <inheritdoc />
        public UniTask ChangeSceneAsync(string name, SceneEnterData enterData = null, CancellationToken ct = default)
        {
            if (!TryResolveScene(name, out var scene))
                return UniTask.FromException(new InvalidOperationException($"Scene \"{name}\" not found."));
            return ChangeSceneAsync(scene, enterData, ct);
        }

        /// <inheritdoc />
        public UniTask ChangeSceneAsync(IScene scene, SceneEnterData enterData = null, CancellationToken ct = default)
        {
            if (!TryValidateRegisteredScene(scene))
                return UniTask.FromException(new InvalidOperationException($"Scene \"{scene?.Name}\" not registered."));
            return EnqueueChangeAsync(SceneChangeMode.Replace, scene, enterData, ct);
        }

        /// <inheritdoc />
        public void PushScene(string name, SceneEnterData enterData = null)
        {
            if (!TryResolveScene(name, out var scene)) return;
            PushScene(scene, enterData);
        }

        /// <inheritdoc />
        public void PushScene(IScene scene, SceneEnterData enterData = null)
        {
            if (!TryValidateRegisteredScene(scene)) return;
            EnqueueChange(SceneChangeMode.Push, scene, enterData, null, CancellationToken.None);
        }

        /// <inheritdoc />
        public UniTask PushSceneAsync(string name, SceneEnterData enterData = null, CancellationToken ct = default)
        {
            if (!TryResolveScene(name, out var scene))
                return UniTask.FromException(new InvalidOperationException($"Scene \"{name}\" not found."));
            return PushSceneAsync(scene, enterData, ct);
        }

        /// <inheritdoc />
        public UniTask PushSceneAsync(IScene scene, SceneEnterData enterData = null, CancellationToken ct = default)
        {
            if (!TryValidateRegisteredScene(scene))
                return UniTask.FromException(new InvalidOperationException($"Scene \"{scene?.Name}\" not registered."));
            return EnqueueChangeAsync(SceneChangeMode.Push, scene, enterData, ct);
        }

        /// <inheritdoc />
        public void PopScene()
        {
            EnqueueChange(SceneChangeMode.Pop, null, null, null, CancellationToken.None);
        }

        /// <inheritdoc />
        public UniTask PopSceneAsync(CancellationToken ct = default)
        {
            return EnqueueChangeAsync(SceneChangeMode.Pop, null, null, ct);
        }

        private UniTask EnqueueChangeAsync(SceneChangeMode mode, IScene scene, SceneEnterData enterData, CancellationToken ct)
        {
            var tcs = new UniTaskCompletionSource();
            EnqueueChange(mode, scene, enterData, tcs, ct);
            return tcs.Task;
        }

        /// <summary>
        /// 将场景切换或退出请求加入串行队列。
        /// </summary>
        /// <param name="mode">切换模式。</param>
        /// <param name="scene">目标场景；退出或弹栈时为空。</param>
        /// <param name="enterData">进入参数。</param>
        /// <param name="completion">异步调用的完成源。</param>
        /// <param name="ct">取消令牌。</param>
        /// <param name="isExit">是否为仅退出当前场景的请求。</param>
        private void EnqueueChange(
            SceneChangeMode mode,
            IScene scene,
            SceneEnterData enterData,
            UniTaskCompletionSource completion,
            CancellationToken ct,
            bool isExit = false)
        {
            if (_isShutdown)
            {
                completion?.TrySetCanceled();
                return;
            }
            if (mode == SceneChangeMode.Pop && _sceneStack.Count == 0)
            {
                var ex = new InvalidOperationException("PopScene failed, scene stack is empty.");
                _log?.Error(Tag, ex.Message);
                completion?.TrySetException(ex);
                return;
            }

            if (mode != SceneChangeMode.Pop && !isExit && scene == null)
                throw new ArgumentNullException(nameof(scene));

            if (!isExit && mode == SceneChangeMode.Replace
                && !_isChanging
                && CurrentScene == scene
                && scene.IsActive
                && _sceneStack.Count == 0)
            {
                _log?.Info(Tag, $"ChangeScene skipped, \"{scene.Name}\" is already active.");
                completion?.TrySetResult();
                return;
            }

            CancelPendingRequest();
            _processingRequest?.LinkedCts?.Cancel();

            var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _currentRequest = new SceneChangeRequest
            {
                Mode = mode,
                TargetScene = scene,
                EnterData = enterData,
                Completion = completion,
                LinkedCts = linkedCts,
                IsExit = isExit,
            };

            if (!_isChanging)
                ProcessChangeQueueAsync().Forget(OnFlowFailed);
        }

        /// <summary>
        /// 按顺序处理场景请求。
        /// </summary>
        /// <returns>队列处理任务。</returns>
        private async UniTask ProcessChangeQueueAsync()
        {
            _isChanging = true;
            try
            {
                while (_currentRequest != null)
                {
                    var request = _currentRequest;
                    _currentRequest = null;
                    _processingRequest = request;
                    var token = request.Token;
                    var fromName = CurrentScene?.Name;
                    IScene enteringTarget = null;

                    try
                    {
                        if (request.Mode == SceneChangeMode.Replace && !request.IsExit)
                            _sceneStack.Clear();
                        else if (request.Mode == SceneChangeMode.Push && CurrentScene != null && CurrentScene.IsActive)
                            _sceneStack.Push(new SceneStackEntry(CurrentScene, _currentEnterData));

                        await PerformSceneExitAsync(token);

                        if (TryConsumeSupersedingRequest(request))
                            continue;

                        if (request.IsExit)
                        {
                            token.ThrowIfCancellationRequested();
                            PublishSceneChanged(fromName, null, SceneChangeMode.Replace);
                            request.Completion?.TrySetResult();
                            continue;
                        }

                        var target = request.TargetScene;
                        var enterData = request.EnterData;

                        if (request.Mode == SceneChangeMode.Pop)
                        {
                            if (_sceneStack.Count == 0)
                                throw new InvalidOperationException("PopScene failed, scene stack is empty.");

                            var entry = _sceneStack.Pop();
                            target = entry.Scene;
                            enterData = entry.EnterData;
                        }

                        enteringTarget = target;
                        request.EnteringScene = target;
                        await PerformSceneEnterAsync(target, enterData, token);
                        token.ThrowIfCancellationRequested();
                        _currentEnterData = enterData;

                        if (TryConsumeSupersedingRequest(request))
                            continue;

                        PublishSceneChanged(fromName, target.Name, request.Mode);
                        request.Completion?.TrySetResult();
                    }
                    catch (OperationCanceledException oce)
                    {
                        CleanupAfterFailedChange(enteringTarget, request.EnteringSceneCleaned);
                        if (_currentRequest == null)
                            request.Completion?.TrySetCanceled(oce.CancellationToken);
                        else
                            request.Completion?.TrySetCanceled();
                    }
                    catch (Exception ex)
                    {
                        _log?.Error(Tag, $"Scene change ({request.Mode}) failed: {ex}");
                        CleanupAfterFailedChange(enteringTarget, request.EnteringSceneCleaned);

                        if (_currentRequest == null)
                            request.Completion?.TrySetException(ex);
                        else
                            request.Completion?.TrySetCanceled();
                    }
                    finally
                    {
                        request.Dispose();
                        if (_processingRequest == request)
                            _processingRequest = null;
                    }
                }
            }
            finally
            {
                _isChanging = false;
                if (_currentRequest != null)
                    ProcessChangeQueueAsync().Forget(OnFlowFailed);
            }
        }

        private bool TryConsumeSupersedingRequest(SceneChangeRequest processed)
        {
            if (_currentRequest == null) return false;
            processed.Completion?.TrySetCanceled();
            return true;
        }

        /// <summary>
        /// 清理切换失败时的当前场景和进入目标。
        /// </summary>
        /// <param name="failedTarget">实际进入失败的场景。</param>
        /// <param name="targetAlreadyCleaned">进入目标是否已由模块关闭流程清理。</param>
        private void CleanupAfterFailedChange(IScene failedTarget, bool targetAlreadyCleaned)
        {
            var current = CurrentScene;
            if (current != null && (current != failedTarget || !targetAlreadyCleaned))
                current.ForceExit();
            CurrentScene = null;
            _currentEnterData = null;
            if (failedTarget != current && !targetAlreadyCleaned)
                failedTarget?.ForceExit();
        }

        private async UniTask PerformSceneExitAsync(CancellationToken ct)
        {
            if (CurrentScene == null) return;

            var exiting = CurrentScene;
            try
            {
                await exiting.ExitSceneAsync(ct);
            }
            catch (Exception ex)
            {
                _log?.Error(Tag, $"ExitScene \"{exiting.Name}\" failed: {ex}");
                exiting.ForceExit();
                throw;
            }
            finally
            {
                if (CurrentScene == exiting)
                {
                    CurrentScene = null;
                    _currentEnterData = null;
                }
            }
        }

        /// <summary>
        /// 初始化并进入目标场景。
        /// </summary>
        /// <param name="target">目标场景。</param>
        /// <param name="enterData">进入参数。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>进入任务。</returns>
        private async UniTask PerformSceneEnterAsync(IScene target, SceneEnterData enterData, CancellationToken ct)
        {
            await target.InitSceneAsync(ct);
            ct.ThrowIfCancellationRequested();
            await target.EnterSceneAsync(enterData, ct);
            ct.ThrowIfCancellationRequested();
            CurrentScene = target;
        }

        private void PublishSceneChanged(string fromName, string toName, SceneChangeMode mode)
        {
            _events?.Publish(SceneEvents.Changed, new SceneChangedEvent(fromName, toName, mode, _sceneStack.Count));
            _log?.Info(Tag, $"Scene changed ({mode}): {fromName ?? "(none)"} -> {toName ?? "(none)"}, stack={_sceneStack.Count}");
        }

        private void CancelPendingRequest()
        {
            if (_currentRequest == null) return;
            _currentRequest.LinkedCts?.Cancel();
            _currentRequest.Completion?.TrySetCanceled();
            _currentRequest.Dispose();
            _currentRequest = null;
        }

        private bool IsSceneInUse(IScene scene)
        {
            if (CurrentScene == scene) return true;
            foreach (var entry in _sceneStack)
            {
                if (entry.Scene == scene) return true;
            }
            return false;
        }

        private bool TryResolveScene(string name, out IScene scene)
        {
            scene = GetScene(name);
            if (scene != null) return true;
            _log?.Error(Tag, $"Scene \"{name}\" not found.");
            return false;
        }

        private bool TryValidateRegisteredScene(IScene scene)
        {
            if (scene == null)
            {
                _log?.Error(Tag, "Scene is null.");
                return false;
            }

            if (_scenes.TryGetValue(scene.Name, out var registered) && registered == scene)
                return true;

            _log?.Error(Tag, $"Scene \"{scene.Name}\" not registered.");
            return false;
        }

        private void OnFlowFailed(Exception ex)
        {
            if (ex is OperationCanceledException) return;
            _log?.Error(Tag, $"Scene flow failed: {ex}");
        }

        private readonly struct SceneStackEntry
        {
            public readonly IScene Scene;
            public readonly SceneEnterData EnterData;

            public SceneStackEntry(IScene scene, SceneEnterData enterData)
            {
                Scene = scene;
                EnterData = enterData;
            }
        }

        private sealed class SceneChangeRequest
        {
            public SceneChangeMode Mode;
            public IScene TargetScene;
            public SceneEnterData EnterData;
            public UniTaskCompletionSource Completion;
            public CancellationTokenSource LinkedCts;
            public bool IsExit;
            public IScene EnteringScene;
            public bool EnteringSceneCleaned;

            public CancellationToken Token => LinkedCts?.Token ?? CancellationToken.None;

            public void Dispose()
            {
                LinkedCts?.Dispose();
                LinkedCts = null;
            }
        }
    }
}
