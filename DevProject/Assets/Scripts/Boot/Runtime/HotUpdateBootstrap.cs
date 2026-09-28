using System;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;
using UnityEngine;
using UnityEngine.SceneManagement;
using YooAsset;

namespace MiniGame.Boot
{
    /// <summary>
    /// 在框架启动后显示 Loading 场景，更新内容包并进入热更游戏。
    /// </summary>
    [DefaultExecutionOrder(-9000)]
    [DisallowMultipleComponent]
    public sealed partial class HotUpdateBootstrap : MonoBehaviour
    {
        private static HotUpdateBootstrap active;

        /// <summary>
        /// 小游戏平台 SDK 安装后注入的真实存储实现。
        /// </summary>
        public static IPlatformSaveStore PlatformSaveStore { get; set; }
        private CancellationTokenSource lifetime;
        private ContentUpdateService updateService;
        private YooAssetUpdateBackend updateBackend;
        private IVersionStateStore versionStateStore;
        private LoadingSceneView view;
        private HotUpdateBootSettings settings;
        private Scene hostScene;
        private bool retryRequested;
        private bool restoredLastGood;
        private bool useLastGoodThisLaunch;

        /// <summary>
        /// 热更入口是否已经正常返回。
        /// </summary>
        public bool HasLaunchedGame { get; private set; }

        /// <summary>
        /// 保留唯一启动实例。
        /// </summary>
        private void Awake()
        {
            if (active != null && active != this)
            {
                Destroy(gameObject);
                return;
            }

            active = this;
            lifetime = new CancellationTokenSource();
            hostScene = gameObject.scene;
            DontDestroyOnLoad(gameObject);
        }

        /// <summary>
        /// 启动加载场景及包更新流程。
        /// </summary>
        private void Start()
        {
            if (active != this) return;
            RunAsync(lifetime.Token).Forget(exception =>
            {
                if (exception is OperationCanceledException) return;
                GameEntry.Get<ILogModule>()?.Error("HotUpdate", $"启动流程意外结束：{exception}");
                Debug.LogError($"[HotUpdate] 启动流程意外结束：{exception}");
            });
        }

        /// <summary>
        /// 显示加载场景并循环处理可重试的错误。
        /// </summary>
        /// <param name="ct">启动取消令牌。</param>
        /// <returns>热更入口完成后的异步操作。</returns>
        private async UniTask RunAsync(CancellationToken ct)
        {
            if (!GameEntry.IsRunning) throw new InvalidOperationException("请先在主场景放置 Framework.prefab。");
            RegisterDiagnostics();
            AsyncOperation load = SceneManager.LoadSceneAsync("Loading", LoadSceneMode.Additive);
            if (load == null) throw new InvalidOperationException("Loading 场景未加入 Build Settings。");
            await load.ToUniTask(cancellationToken: ct);
            view = LoadingSceneView.Instance;
            if (view == null) throw new InvalidOperationException("Loading 场景缺少 LoadingSceneView。");

            settings = Resources.Load<HotUpdateBootSettings>("HotUpdateBootSettings");
            if (settings == null) throw new InvalidOperationException("Resources 中缺少 HotUpdateBootSettings.asset。");
            ConfigureSaveBackend();
            string stateScope = $"{ApplicationIdentity.GameId}.{Application.version}.{settings.Channel}.{settings.ResolvePlayMode()}.{Application.platform}";
            versionStateStore = new PlayerPrefsVersionStateStore(stateScope);
            updateBackend = new YooAssetUpdateBackend(settings, settings, versionStateStore);
            updateService = new ContentUpdateService(updateBackend);
            updateService.ProgressChanged += OnUpdateProgress;
            updateService.Initialize();

            while (!ct.IsCancellationRequested)
            {
                view.HideFailure();
                view.SetProgress(0f);
                view.SetStatus("正在检查游戏内容…");
                try
                {
                    await EnsureLastGoodRestoredAsync(ct);
                    await UpdateGamePackageAsync(ct);

                    view.SetStatus("正在启动游戏…");
                    bool hasStartupScene = await LaunchGameAsync(ct);
                    PackageVersionState state = versionStateStore.Load(HotUpdatePackageNames.Game);
                    if (state.HasPending) versionStateStore.CommitReady(HotUpdatePackageNames.Game, state.PendingVersion);
                    await CompleteGameStartupAsync(ct);
                    HasLaunchedGame = true;
                    view.SetProgress(1f);
                    GameEntry.Get<ILogModule>()?.Info("HotUpdate", "GamePackage 已激活，热更程序已启动。");
                    updateService.ProgressChanged -= OnUpdateProgress;
                    updateService.Dispose();
                    updateService = null;
                    Scene loadingScene = SceneManager.GetSceneByName("Loading");
                    if (loadingScene.IsValid() && loadingScene.isLoaded)
                        await SceneManager.UnloadSceneAsync(loadingScene).ToUniTask(cancellationToken: ct);
                    if (hasStartupScene && hostScene.IsValid() && hostScene.isLoaded)
                        await SceneManager.UnloadSceneAsync(hostScene).ToUniTask(cancellationToken: ct);
                    return;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return;
                }
                catch (GameStartupDataException exception)
                {
                    GameEntry.Get<ILogModule>()?.Error("HotUpdate", exception.ToString());
                    view.SetStatus("玩家存档尚未就绪");
                    view.ShowFailure(exception.Message, false);
                    return;
                }
                catch (SaveStartupFailureException exception)
                {
                    GameEntry.Get<ILogModule>()?.Error("HotUpdate", exception.ToString());
                    view.SetStatus("玩家存档尚未就绪");
                    view.ShowFailure(exception.Message, false);
                    return;
                }
                catch (Exception exception)
                {
                    string message = exception.InnerException?.Message ?? exception.Message;
                    GameEntry.Get<ILogModule>()?.Error("HotUpdate", exception.ToString());
                    PackageVersionState state = versionStateStore.Load(HotUpdatePackageNames.Game);
                    UpdateErrorCode errorCode = exception is UpdateException updateException ? updateException.ErrorCode : UpdateErrorCode.Unknown;
                    bool canRetry = exception is not UpdateException updateError || updateError.CanRetry;
                    if (state.HasPending)
                    {
                        if (HotUpdateLauncher.RequiresRestart || !settings.AllowCachedPackageFallback ||
                            string.IsNullOrEmpty(state.LastGoodVersion))
                        {
                            versionStateStore.Rollback(HotUpdatePackageNames.Game, errorCode, HotUpdateLauncher.RequiresRestart || !canRetry);
                        }
                        else
                        {
                            UpdateExecutionResult restored = await updateBackend.RestoreLastGoodAsync(HotUpdatePackageNames.Game, ct: ct);
                            if (restored.IsSuccess)
                            {
                                versionStateStore.Rollback(HotUpdatePackageNames.Game, errorCode, true);
                                useLastGoodThisLaunch = true;
                                view.SetStatus("新版本启动失败，正在恢复上次可用版本…");
                                continue;
                            }

                            restoredLastGood = false;
                            message = $"{message}\n恢复上次可用版本失败：{restored.ErrorMessage}";
                            canRetry = restored.CanRetry;
                        }
                    }

                    if (HotUpdateLauncher.RequiresRestart)
                    {
#if UNITY_EDITOR
                        view.SetStatus("热更程序启动失败，请重新进入 Play Mode");
                        view.ShowFailure($"{message}\n热更入口已执行，请停止并重新进入 Play Mode。", false);
#else
                        view.SetStatus("热更程序启动失败，请重启游戏");
                        view.ShowFailure($"{message}\n热更 DLL 已加载，请重启游戏。", false);
#endif
                        return;
                    }

                    view.ShowFailure(message, canRetry);
                    if (!canRetry) return;
                    await WaitForRetryAsync(ct);
                }
            }
        }

        /// <summary>
        /// 阻止将存档故障交给热更 DLL 重载错误分支。
        /// </summary>
        private sealed class SaveStartupFailureException : Exception
        {
            /// <summary>
            /// 创建不可继续的存档启动错误。
            /// </summary>
            /// <param name="message">玩家可读的错误说明。</param>
            public SaveStartupFailureException(string message) : base(message) { }
        }
        /// <summary>
        /// 按宿主配置选择持久化后端，小游戏必须显式选平台 SDK。
        /// </summary>
        private void ConfigureSaveBackend()
        {
            ISaveModule save = GameEntry.Get<ISaveModule>();
            if (save == null) throw new InvalidOperationException("框架未注册玩家存档模块。");
            SaveBackendMode mode = settings.SaveBackend;
            if (mode == SaveBackendMode.Auto)
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                throw new InvalidOperationException("WebGL 构建必须明确选择浏览器 IndexedDB 或小游戏平台 SDK 存档后端。");
#else
                mode = SaveBackendMode.File;
#endif
            }

            ISaveBackend backend;
            switch (mode)
            {
                case SaveBackendMode.File:
#if UNITY_WEBGL && !UNITY_EDITOR
                    throw new InvalidOperationException("WebGL 不能使用本机文件存档后端。");
#else
                    backend = new FileSaveBackend(Path.Combine(Application.persistentDataPath, "Saves"));
                    break;
#endif
                case SaveBackendMode.BrowserIndexedDb:
#if UNITY_WEBGL && !UNITY_EDITOR
                    backend = new IndexedDbSaveBackend();
                    break;
#else
                    throw new InvalidOperationException("浏览器 IndexedDB 后端只适用于标准 WebGL 构建。");
#endif
                case SaveBackendMode.PlatformSdk:
                    backend = new PlatformSaveBackend(PlatformSaveStore);
                    break;
                default:
                    throw new InvalidOperationException("未知玩家存档后端。");
            }
            SaveResult result = save.ConfigureBackend(backend);
            if (!result.Success) throw new InvalidOperationException($"玩家存档后端配置失败（{result.Error}）：{result.Message}");
        }

        /// <summary>
        /// 内容包确认后只重试项目启动收尾，不重新执行热更入口。
        /// </summary>
        /// <param name="ct">启动取消令牌。</param>
        /// <returns>玩家存档等项目数据已就绪的异步操作。</returns>
        private async UniTask CompleteGameStartupAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                view.HideFailure();
                view.SetStatus("正在准备玩家存档…");
                GameStartupCompletionResult result;
                try
                {
                    result = await GameStartupCompletion.CompleteAsync(ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    GameEntry.Get<ILogModule>()?.Error("SaveStartup", exception.ToString());
                    result = new GameStartupCompletionResult(false, $"玩家存档启动失败：{exception.Message}");
                }
                if (result.Success)
                {
                    GameStartupCompletion.Clear();
                    return;
                }

                view.SetStatus("玩家存档尚未就绪");
                view.ShowFailure(result.Message, result.CanRetry);
                if (!result.CanRetry)
                {
                    GameStartupCompletion.Clear();
                    throw new SaveStartupFailureException(result.Message);
                }
                await WaitForRetryAsync(ct);
            }
            ct.ThrowIfCancellationRequested();
        }

        /// <summary>
        /// 冷启动时先恢复上次确认可用的内容清单。
        /// </summary>
        /// <param name="ct">启动取消令牌。</param>
        /// <returns>旧版清单恢复完成后的异步操作。</returns>
        private async UniTask EnsureLastGoodRestoredAsync(CancellationToken ct)
        {
            if (restoredLastGood) return;
            PackageVersionState state = versionStateStore.Load(HotUpdatePackageNames.Game);
            if (string.IsNullOrEmpty(state.LastGoodVersion) && settings.ResolvePlayMode() != HotUpdatePlayMode.EditorSimulate)
            {
                string builtinVersion = await settings.LoadBuiltinVersionAsync(HotUpdatePackageNames.Game, ct);
                if (!string.IsNullOrWhiteSpace(builtinVersion))
                    state = versionStateStore.InitializeCurrent(HotUpdatePackageNames.Game, builtinVersion);
            }
            if (string.IsNullOrEmpty(state.LastGoodVersion))
            {
                restoredLastGood = true;
                return;
            }

            UpdateExecutionResult restored = await updateBackend.RestoreLastGoodAsync(HotUpdatePackageNames.Game, ct: ct);
            if (!restored.IsSuccess)
            {
                ct.ThrowIfCancellationRequested();
                if (await CanUseBuiltinUpgradeAsync(state, ct))
                {
                    restoredLastGood = true;
                    return;
                }
                throw new UpdateException(restored.ErrorCode, $"恢复上次可用版本失败：{restored.ErrorMessage}", restored.CanRetry);
            }

            if (state.HasPending)
                versionStateStore.Rollback(HotUpdatePackageNames.Game, UpdateErrorCode.ActivationFailed, state.PendingAttemptCount > 0);
            restoredLastGood = true;
        }

        /// <summary>
        /// 旧内置清单已随安装包替换时，允许标准更新流程验证并激活新版内置包。
        /// </summary>
        /// <param name="state">升级前保存的版本状态。</param>
        /// <param name="ct">启动取消令牌。</param>
        /// <returns>可以检查新版内置包时返回 true。</returns>
        private async UniTask<bool> CanUseBuiltinUpgradeAsync(PackageVersionState state, CancellationToken ct)
        {
            if (settings.ResolvePlayMode() != HotUpdatePlayMode.Offline || state.HasPending) return false;
            string builtinVersion = await settings.LoadBuiltinVersionAsync(HotUpdatePackageNames.Game, ct);
            if (string.IsNullOrWhiteSpace(builtinVersion) ||
                string.Equals(builtinVersion, state.LastGoodVersion, StringComparison.Ordinal) ||
                string.Equals(builtinVersion, state.RejectedVersion, StringComparison.Ordinal)) return false;

            GameEntry.Get<ILogModule>()?.Warn("HotUpdate", $"旧版 {state.LastGoodVersion} 无法恢复，检查新版内置包 {builtinVersion}。");
            return true;
        }

        /// <summary>
        /// 检查、下载并立即激活 GamePackage。
        /// </summary>
        /// <param name="ct">启动取消令牌。</param>
        /// <returns>内容更新完成后的异步操作。</returns>
        private async UniTask UpdateGamePackageAsync(CancellationToken ct)
        {
            if (useLastGoodThisLaunch)
            {
                useLastGoodThisLaunch = false;
                if (!TryUseCachedPackage())
                    throw new UpdateException(UpdateErrorCode.ActivationFailed, "上次可用版本尚未恢复。", true);
                return;
            }

            UpdatePackageRequest request = settings.CreateUpdateRequest(Application.version);
            UpdatePlan plan;
            try
            {
                plan = await updateService.CheckAsync(request, ct);
            }
            catch (Exception exception) when (exception is not OperationCanceledException && TryUseCachedPackage())
            {
                GameEntry.Get<ILogModule>()?.Warn("HotUpdate", $"更新检查失败，继续使用上次可用版本：{exception.Message}");
                return;
            }

            PackageVersionState state = versionStateStore.Load(HotUpdatePackageNames.Game);
            if (plan.HasUpdate && string.Equals(plan.RemoteVersion, state.RejectedVersion, StringComparison.Ordinal))
            {
                if (TryUseCachedPackage())
                {
                    GameEntry.Get<ILogModule>()?.Warn("HotUpdate", $"候选版本 {plan.RemoteVersion} 曾启动失败，继续使用上次可用版本。");
                    return;
                }
                throw new UpdateException(UpdateErrorCode.HostApplicationIncompatible, $"候选版本 {plan.RemoteVersion} 曾启动失败。", false);
            }

            if (plan.HasUpdate) versionStateStore.StagePending(HotUpdatePackageNames.Game, plan.RemoteVersion);
            UpdateExecutionResult result = await updateService.ExecuteAsync(plan, ct);
            if (result.Status == UpdateExecutionStatus.NoUpdate || result.Status == UpdateExecutionStatus.Succeeded)
            {
                if (plan.HasUpdate) versionStateStore.IncrementPendingAttempt(HotUpdatePackageNames.Game);
                return;
            }

            if (plan.HasUpdate) versionStateStore.Rollback(HotUpdatePackageNames.Game, result.ErrorCode);
            if (TryUseCachedPackage())
            {
                GameEntry.Get<ILogModule>()?.Warn("HotUpdate", $"更新失败，继续使用上次可用版本：{result.ErrorMessage}");
                return;
            }

            UpdateErrorCode code = result.ErrorCode == UpdateErrorCode.None ? UpdateErrorCode.ActivationFailed : result.ErrorCode;
            string message = result.RequiresRestart ? "内容更新已准备，请重启游戏。" : $"游戏内容更新失败：{result.ErrorMessage}";
            throw new UpdateException(code, message, result.CanRetry);
        }

        /// <summary>
        /// 判断当前活动内容包是否为上次确认可用版本。
        /// </summary>
        /// <returns>允许回退且旧包仍处于可用状态时返回 true。</returns>
        private bool TryUseCachedPackage()
        {
            if (!settings.AllowCachedPackageFallback ||
                !YooAssets.TryGetPackage(HotUpdatePackageNames.Game, out ResourcePackage package) || !package.PackageValid)
                return false;
            string lastGood = versionStateStore.Load(HotUpdatePackageNames.Game).LastGoodVersion;
            return string.IsNullOrEmpty(lastGood) || string.Equals(package.GetPackageVersion(), lastGood, StringComparison.Ordinal);
        }

        /// <summary>
        /// 读取包级配置并调用 GameLogic 入口。
        /// </summary>
        /// <param name="ct">启动取消令牌。</param>
        /// <returns>是否进入内容包指定的启动场景。</returns>
        private async UniTask<bool> LaunchGameAsync(CancellationToken ct)
        {
            IResourceModule resources = GameEntry.Get<IResourceModule>();
            if (resources == null || !resources.BindDefaultPackage(HotUpdatePackageNames.Game))
                throw new InvalidOperationException("无法绑定已激活的 GamePackage。");
            resources.ConfigureDownloadSupport(HotUpdatePackageNames.Game, settings.ResolvePlayMode() != HotUpdatePlayMode.Web);

            ResourceHandle<TextAsset> handle = await resources.LoadAsync<TextAsset>(HotUpdatePackageLocations.GameSettings, ct);
            PackageRuntimeSettings packageSettings;
            try
            {
                if (handle.Status != EOperationStatus.Succeeded || handle.Asset == null)
                    throw new InvalidOperationException("GamePackage 缺少 GamePackageSettings.json。");
                string packageJson = handle.Asset.text;
                if (updateBackend.ActivatedSettingsJson != null &&
                    !string.Equals(packageJson, updateBackend.ActivatedSettingsJson, StringComparison.Ordinal))
                    throw new UpdateException(UpdateErrorCode.VerificationFailed, "候选包内配置与激活前检查的配置不一致。", false);
                packageSettings = PackageRuntimeSettings.FromJsonStrict(packageJson);
            }
            finally
            {
                handle.Release();
            }

            if (!packageSettings.TryValidate(1, 1, Application.version, out string error))
                throw new InvalidOperationException($"GamePackage 配置无效：{error}");
            if (!YooAssets.TryGetPackage(HotUpdatePackageNames.Game, out ResourcePackage package) || !package.PackageValid)
                throw new InvalidOperationException("GamePackage 未激活。");
            for (int i = 0; i < packageSettings.RequiredLocations.Count; i++)
            {
                string location = packageSettings.RequiredLocations[i];
                if (!package.IsLocationValid(location)) throw new InvalidOperationException($"GamePackage 缺少必需资源：{location}");
            }

            await HotUpdateLauncher.LaunchAsync(packageSettings, resources, ct);
            return !string.IsNullOrWhiteSpace(packageSettings.StartupSceneLocation);
        }

        /// <summary>
        /// 将更新进度映射到 Loading 界面。
        /// </summary>
        /// <param name="progress">当前内容更新进度。</param>
        private void OnUpdateProgress(UpdateProgress progress)
        {
            diagnosticProgress = progress;
            hasDiagnosticProgress = true;
            if (view == null) return;
            switch (progress.Phase)
            {
                case UpdateProgressPhase.InitializePackage: view.SetStatus("正在初始化游戏内容…"); break;
                case UpdateProgressPhase.RequestPackageVersion: view.SetStatus("正在检查新版本…"); break;
                case UpdateProgressPhase.LoadPackageManifest: view.SetStatus("正在读取更新清单…"); break;
                case UpdateProgressPhase.PrepareDownload: view.SetStatus("正在准备下载…"); break;
                case UpdateProgressPhase.Download: view.SetStatus($"正在下载游戏内容 {progress.DownloadedCount}/{progress.TotalCount}"); break;
                case UpdateProgressPhase.Verify: view.SetStatus("正在校验内容…"); break;
                case UpdateProgressPhase.Activate: view.SetStatus("正在激活新版本…"); break;
                case UpdateProgressPhase.Completed: view.SetStatus("内容更新完成"); break;
            }

            view.SetProgress(progress.NormalizedProgress);
        }

        /// <summary>
        /// 等待玩家在 Loading 界面点击重试。
        /// </summary>
        /// <param name="ct">等待取消令牌。</param>
        /// <returns>点击重试后的异步操作。</returns>
        private async UniTask WaitForRetryAsync(CancellationToken ct)
        {
            retryRequested = false;
            view.RetryRequested += OnRetryRequested;
            try
            {
                await UniTask.WaitUntil(() => retryRequested, cancellationToken: ct);
            }
            finally
            {
                if (view != null) view.RetryRequested -= OnRetryRequested;
            }
        }

        /// <summary>
        /// 标记玩家请求重新检查内容包。
        /// </summary>
        private void OnRetryRequested() => retryRequested = true;

        /// <summary>
        /// 停止更新服务并释放取消令牌。
        /// </summary>
        private void OnDestroy()
        {
            if (active != this) return;
            diagnosticLifetime?.Dispose();
            diagnosticLifetime = null;
            lifetime?.Cancel();
            GameStartupCompletion.Clear();
            if (view != null) view.RetryRequested -= OnRetryRequested;
            if (updateService != null)
            {
                updateService.ProgressChanged -= OnUpdateProgress;
                updateService.Dispose();
                updateService = null;
            }

            lifetime?.Dispose();
            lifetime = null;
            active = null;
        }
    }
}
