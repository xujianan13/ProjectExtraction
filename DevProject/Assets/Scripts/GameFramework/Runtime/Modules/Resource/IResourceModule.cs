using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.SceneManagement;
using YooAsset;

namespace GameFramework
{
    /// <summary>
    /// 资源模块接口。包装 YooAsset：业务通过 Address 加载，框架内部分发到具体 <see cref="ResourcePackage"/>。
    /// 初始化策略（EditorSimulate / Offline / Host / Web）由业务通过 <see cref="InitializePackageAsync"/> 的回调决定。
    /// </summary>
    public interface IResourceModule : IModule
    {
        /// <summary>默认资源包名。<see cref="InitializePackageAsync"/> 传 asDefault=true 时设置。</summary>
        string DefaultPackageName { get; }

        /// <summary>
        /// 模块是否已初始化或绑定至少一个包；资源可加载性还要求该包已激活清单。
        /// </summary>
        bool IsInitialized { get; }

        /// <summary>业务资源缓存的 LRU 预算。</summary>
        ResourceCacheSettings CacheSettings { get; }

        /// <summary>
        /// 创建（或复用）并初始化资源包。initializer 由业务实现：构造 InitializePackageOptions 并 await package.InitializePackageAsync(options)。
        /// </summary>
        /// <param name="packageName">包名。</param>
        /// <param name="initializer">业务侧初始化回调。</param>
        /// <param name="asDefault">是否设为默认包。</param>
        /// <returns>初始化是否成功。</returns>
        UniTask<bool> InitializePackageAsync(string packageName, Func<ResourcePackage, UniTask> initializer, bool asDefault = true);

        /// <summary>尝试获取指定包，便于业务做更新版本 / 预下载等高级操作。</summary>
        bool TryGetPackage(string packageName, out ResourcePackage package);

        /// <summary>将已经由启动更新流程初始化的内容包绑定为默认资源包。</summary>
        /// <param name="packageName">已经初始化且有效的包名。</param>
        /// <returns>绑定是否成功。</returns>
        bool BindDefaultPackage(string packageName);

        /// <summary>同步加载（默认包）。仅在 Editor / OfflinePlayMode 下推荐。</summary>
        ResourceHandle<T> LoadSync<T>(string location) where T : UnityEngine.Object;

        /// <summary>同步加载（指定包）。</summary>
        ResourceHandle<T> LoadSync<T>(string packageName, string location) where T : UnityEngine.Object;

        /// <summary>异步加载（默认包）。</summary>
        UniTask<ResourceHandle<T>> LoadAsync<T>(string location, CancellationToken ct = default) where T : UnityEngine.Object;

        /// <summary>异步加载（指定包）。</summary>
        UniTask<ResourceHandle<T>> LoadAsync<T>(string packageName, string location, CancellationToken ct = default) where T : UnityEngine.Object;

        /// <summary>
        /// 获取默认包资源的独立业务租约。相同包、激活代次、地址和类型的并发请求只触发一次底层加载。
        /// </summary>
        UniTask<ResourceLease<T>> AcquireAsync<T>(
            string location,
            ResourceLoadOptions options = default,
            IProgress<float> progress = null,
            CancellationToken ct = default) where T : UnityEngine.Object;

        /// <summary>获取指定包资源的独立业务租约。</summary>
        UniTask<ResourceLease<T>> AcquireAsync<T>(
            string packageName,
            string location,
            ResourceLoadOptions options = default,
            IProgress<float> progress = null,
            CancellationToken ct = default) where T : UnityEngine.Object;

        /// <summary>仅在默认包资源已经完成加载时取得租约，不触发 IO。</summary>
        bool TryAcquire<T>(string location, out ResourceLease<T> lease) where T : UnityEngine.Object;

        /// <summary>
        /// 以回调方式获取默认包资源。该方法复用异步租约入口，单个票据取消不会中断其他调用方共享的底层加载。
        /// </summary>
        ResourceLoadTicket<T> Load<T>(
            string location,
            Action<ResourceLease<T>> onCompleted,
            Action<ResourceLoadError> onFailed,
            Action<float> onProgress = null,
            ResourceLoadOptions options = default,
            CancellationToken ct = default) where T : UnityEngine.Object;

        /// <summary>加载默认包中的资源组，并返回统一生命周期租约。</summary>
        UniTask<ResourceGroupLease> AcquireGroupAsync(
            ResourceGroupDefinition definition,
            IProgress<float> progress = null,
            CancellationToken ct = default);

        /// <summary>
        /// 将指定地址或标签关联的 Bundle 及依赖预下载到 YooAsset 缓存，不加载资源对象。
        /// </summary>
        /// <param name="request">预下载目标和下载设置。</param>
        /// <param name="progress">下载进度接收方。</param>
        /// <param name="ct">本次下载的取消令牌。</param>
        /// <returns>包含成功、失败、取消或不支持状态的结果。</returns>
        UniTask<ResourceDownloadResult> DownloadAsync(ResourceDownloadRequest request, IProgress<ResourceDownloadProgress> progress = null, CancellationToken ct = default);
        /// <summary>
        /// 声明指定包是否支持可持久化的 Bundle 预下载；包初始化模式由启动流程确定。
        /// </summary>
        /// <param name="packageName">已初始化的包名。</param>
        /// <param name="supported">是否支持落盘预下载。</param>
        void ConfigureDownloadSupport(string packageName, bool supported);
        /// <summary>尝试立即淘汰默认包中的零引用、非常驻缓存资源。</summary>
        bool TryEvict<T>(string location) where T : UnityEngine.Object;

        /// <summary>
        /// 按 LRU 预算或全部可淘汰模式释放缓存句柄，并批量执行一次 YooAsset 未使用资源清理。
        /// </summary>
        UniTask<int> TrimAsync(bool evictAllEligible, CancellationToken ct = default);

        /// <summary>异步加载场景（默认包）。返回 YooAsset 的 SceneHandle，业务可通过它访问 AllowSceneActivation 等高级 API。</summary>
        UniTask<SceneHandle> LoadSceneAsync(string location, LoadSceneMode mode = LoadSceneMode.Single, CancellationToken ct = default);

        /// <summary>异步释放默认包内未引用的资源。</summary>
        UniTask UnloadUnusedAssetsAsync();

        /// <summary>异步释放指定包内未引用的资源。</summary>
        UniTask UnloadUnusedAssetsAsync(string packageName);
    }
}
