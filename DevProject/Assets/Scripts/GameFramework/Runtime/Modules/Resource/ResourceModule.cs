using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.SceneManagement;
using YooAsset;

namespace GameFramework
{
    /// <summary>
    /// 资源模块默认实现。统一封装 YooAsset 3.x：管理全局生命周期、转发加载请求、把 YooAsset 句柄包成 <see cref="ResourceHandle{T}"/>。
    /// 异步加载通过 Completed 回调桥接到 UniTask，支持 CancellationToken 取消。
    /// </summary>
    public sealed partial class ResourceModule : ModuleBase, IResourceModule
    {
        private const string Tag = "Resource";

        /// <inheritdoc />
        public override int Priority => -50;

        /// <inheritdoc />
        public string DefaultPackageName { get; private set; }

        /// <inheritdoc />
        public bool IsInitialized => _managedInitialized && (_initializedPackages.Count > 0 || !string.IsNullOrEmpty(DefaultPackageName));

        private ILogModule _log;
        private readonly HashSet<string> _initializedPackages = new(StringComparer.Ordinal);

        /// <inheritdoc />
        public override void OnInit()
        {
            _log = GameEntry.Get<ILogModule>();
            // 无热更启动壳的独立测试或工具场景仍允许框架自行初始化 YooAsset。
            if (!YooAssets.IsInitialized)
            {
                YooAssets.Initialize();
            }
            InitializeManagedResources();
        }

        /// <inheritdoc />
        public override void OnShutdown()
        {
            ShutdownManagedResources();
            // YooAsset 全局生命周期由 BootLogic 持有，模块卸载只解除默认包绑定。
            DefaultPackageName = null;
            _initializedPackages.Clear();
            _downloadSupport.Clear();
            _log = null;
        }

        /// <inheritdoc />
        public async UniTask<bool> InitializePackageAsync(string packageName, Func<ResourcePackage, UniTask> initializer, bool asDefault = true)
        {
            if (string.IsNullOrEmpty(packageName)) throw new ArgumentNullException(nameof(packageName));
            if (initializer == null) throw new ArgumentNullException(nameof(initializer));
            if (!_managedInitialized || _managedShuttingDown)
            {
                _log?.Error(Tag, $"资源模块未运行，拒绝初始化资源包 '{packageName}'。");
                return false;
            }

            var package = YooAssets.ContainsPackage(packageName)
                ? YooAssets.GetPackage(packageName)
                : YooAssets.CreatePackage(packageName);

            try
            {
                await initializer(package);
            }
            catch (Exception ex)
            {
                if (_log != null && _log.IsEnabled(LogLevel.Error))
                    _log.Error(Tag, $"InitializePackage '{packageName}' failed: {ex}");
                return false;
            }

            if (!_managedInitialized || _managedShuttingDown || package.InitializeStatus != EOperationStatus.Succeeded)
            {
                _log?.Error(Tag, $"资源包 '{packageName}' 未成功初始化，状态：{package.InitializeStatus}。");
                return false;
            }

            _initializedPackages.Add(packageName);
            if (asDefault && !TrySetDefaultPackage(packageName))
                return false;
            if (_log != null && _log.IsEnabled(LogLevel.Info))
                _log.Info(Tag, $"Package '{packageName}' initialized (default={asDefault}).");
            return true;
        }

        /// <inheritdoc />
        public bool TryGetPackage(string packageName, out ResourcePackage package)
            => YooAssets.TryGetPackage(packageName, out package);

        /// <inheritdoc />
        public bool BindDefaultPackage(string packageName)
        {
            if (string.IsNullOrWhiteSpace(packageName))
                throw new ArgumentException("内容包名称不能为空。", nameof(packageName));
            if (!_managedInitialized || _managedShuttingDown)
            {
                _log?.Error(Tag, $"资源模块未运行，拒绝绑定默认包 '{packageName}'。");
                return false;
            }
            if (!YooAssets.TryGetPackage(packageName, out ResourcePackage package) || !package.PackageValid)
            {
                _log?.Error(Tag, $"Package '{packageName}' is not initialized and cannot become default.");
                return false;
            }

            if (!TrySetDefaultPackage(packageName))
                return false;
            _initializedPackages.Add(packageName);
            _log?.Info(Tag, $"Package '{packageName}' bound as default.");
            return true;
        }

        /// <inheritdoc />
        public ResourceHandle<T> LoadSync<T>(string location) where T : UnityEngine.Object
            => LoadSync<T>(DefaultPackageName, location);

        /// <inheritdoc />
        public ResourceHandle<T> LoadSync<T>(string packageName, string location) where T : UnityEngine.Object
        {
            var pkg = RequirePackage(packageName);
            var handle = pkg.LoadAssetSync<T>(location);
            return new ResourceHandle<T>(handle);
        }

        /// <inheritdoc />
        public UniTask<ResourceHandle<T>> LoadAsync<T>(string location, CancellationToken ct = default) where T : UnityEngine.Object
            => LoadAsync<T>(DefaultPackageName, location, ct);

        /// <inheritdoc />
        public async UniTask<ResourceHandle<T>> LoadAsync<T>(string packageName, string location, CancellationToken ct = default) where T : UnityEngine.Object
        {
            var pkg = RequirePackage(packageName);
            var handle = pkg.LoadAssetAsync<T>(location);
            try
            {
                await YooAssetAwaitExtensions.AwaitAsync(handle, ct);
            }
            catch (OperationCanceledException)
            {
                // 加载中取消：释放底层句柄避免泄漏
                handle.Release();
                throw;
            }
            return new ResourceHandle<T>(handle);
        }

        /// <inheritdoc />
        public async UniTask<SceneHandle> LoadSceneAsync(string location, LoadSceneMode mode = LoadSceneMode.Single, CancellationToken ct = default)
        {
            var pkg = RequirePackage(DefaultPackageName);
            var handle = pkg.LoadSceneAsync(location, mode);
            try
            {
                await YooAssetAwaitExtensions.AwaitAsync(handle, ct);
            }
            catch (OperationCanceledException)
            {
                // YooAsset 会等待进行中的加载结束后再卸载，避免取消切换时遗留场景句柄。
                _ = handle.UnloadSceneAsync();
                throw;
            }
            return handle;
        }

        /// <inheritdoc />
        public UniTask UnloadUnusedAssetsAsync()
        {
            if (string.IsNullOrEmpty(DefaultPackageName)) return UniTask.CompletedTask;
            return UnloadUnusedAssetsAsync(DefaultPackageName);
        }

        /// <inheritdoc />
        public async UniTask UnloadUnusedAssetsAsync(string packageName)
        {
            if (!YooAssets.TryGetPackage(packageName, out var pkg)) return;
            var op = pkg.UnloadUnusedAssetsAsync();
            await YooAssetAwaitExtensions.AwaitAsync(op);
        }

        private ResourcePackage RequirePackage(string packageName)
        {
            if (string.IsNullOrEmpty(packageName))
                throw new InvalidOperationException("Resource module has no default package; call InitializePackageAsync first.");
            if (!YooAssets.TryGetPackage(packageName, out var pkg))
                throw new InvalidOperationException($"Resource package '{packageName}' not initialized.");
            return pkg;
        }
    }
}
