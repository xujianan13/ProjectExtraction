using System.Threading;
using Cysharp.Threading.Tasks;

namespace GameFramework
{
    /// <summary>
    /// 玩法管理模块接口。负责启动、更新、停止多个可并存的玩法实例。
    /// </summary>
    public interface IGameplayModule : IModule
    {
        /// <summary>当前受管理的玩法实例数量。</summary>
        int Count { get; }

        /// <summary>
        /// 玩法是否由业务主动暂停。
        /// </summary>
        bool IsPaused { get; }

        /// <summary>
        /// 应用是否处于后台暂停状态。
        /// </summary>
        bool IsApplicationPaused { get; }

        /// <summary>
        /// 设置业务主动暂停状态；应用后台暂停另行叠加。
        /// </summary>
        /// <param name="paused">是否暂停玩法。</param>
        void SetPaused(bool paused);

        /// <summary>
        /// 设置应用后台暂停状态，不改变业务主动暂停状态。
        /// </summary>
        /// <param name="paused">应用是否进入后台。</param>
        void SetApplicationPaused(bool paused);

        /// <summary>启动玩法实例，进入流程异步执行。</summary>
        /// <param name="gameplay">玩法实例。</param>
        /// <param name="enterData">进入参数。</param>
        GameplayHandle Start(IGameplay gameplay, GameplayEnterData enterData = null);

        /// <summary>启动玩法实例并等待进入完成。</summary>
        /// <param name="gameplay">玩法实例。</param>
        /// <param name="enterData">进入参数。</param>
        /// <param name="ct">取消令牌。</param>
        UniTask<GameplayHandle> StartAsync(
            IGameplay gameplay,
            GameplayEnterData enterData = null,
            CancellationToken ct = default);

        /// <summary>停止并销毁指定玩法实例，退出流程异步执行。</summary>
        /// <param name="handle">玩法句柄。</param>
        void Stop(GameplayHandle handle);

        /// <summary>停止并销毁指定玩法实例。</summary>
        /// <param name="handle">玩法句柄。</param>
        /// <param name="ct">取消令牌。</param>
        UniTask StopAsync(GameplayHandle handle, CancellationToken ct = default);

        /// <summary>停止并销毁所有玩法实例，退出流程异步执行。</summary>
        void StopAll();

        /// <summary>停止并销毁所有玩法实例。</summary>
        /// <param name="ct">取消令牌。</param>
        UniTask StopAllAsync(CancellationToken ct = default);

        /// <summary>指定玩法是否仍受模块管理。</summary>
        /// <param name="handle">玩法句柄。</param>
        bool Contains(GameplayHandle handle);

        /// <summary>尝试获取玩法实例。</summary>
        /// <param name="handle">玩法句柄。</param>
        /// <param name="gameplay">玩法实例。</param>
        bool TryGetGameplay(GameplayHandle handle, out IGameplay gameplay);
    }
}
