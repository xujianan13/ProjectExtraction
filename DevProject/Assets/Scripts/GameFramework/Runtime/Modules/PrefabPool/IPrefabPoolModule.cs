using System.Threading;
using Cysharp.Threading.Tasks;

namespace GameFramework
{
    /// <summary>
    /// 管理具有独立所有权的 Prefab 实例池；资源加载和并发合并复用资源模块。
    /// </summary>
    public interface IPrefabPoolModule : IModule
    {
        /// <summary>
        /// 获取尚未关闭的实例池数量，不含加载中的请求。
        /// </summary>
        int Count { get; }

        /// <summary>
        /// 加载 Prefab 并创建独立实例池，由调用方在场景或玩法退出时释放。
        /// 同地址的多次调用得到独立池；底层加载和资源由资源模块共享。
        /// </summary>
        /// <param name="location">Prefab 的资源地址。</param>
        /// <param name="options">可选池设置；为空时使用默认设置。</param>
        /// <param name="packageName">资源包名；为空时使用调用时的默认包。</param>
        /// <param name="ct">只取消本次创建的令牌；模块关闭也会取消未完成请求。</param>
        /// <returns>已加载且可同步生成实例的池；模块不可用时返回空，取消或加载失败沿用资源模块异常契约。</returns>
        UniTask<PrefabPool> CreatePoolAsync(string location, PrefabPoolOptions options = null, string packageName = null,
            CancellationToken ct = default);
    }
}
