using System.Threading;
using Cysharp.Threading.Tasks;

namespace GameFramework
{
    /// <summary>
    /// 玩法实例接口。一个实例代表大世界、战斗或小游戏等可独立进入和退出的玩法上下文。
    /// </summary>
    public interface IGameplay
    {
        /// <summary>玩法唯一名称，主要用于日志和调试。</summary>
        string Name { get; }

        /// <summary>当前运行状态。</summary>
        GameplayState State { get; }

        /// <summary>是否已完成初始化。</summary>
        bool IsInited { get; }

        /// <summary>是否正在进入。</summary>
        bool IsEntering { get; }

        /// <summary>是否正在退出。</summary>
        bool IsExiting { get; }

        /// <summary>是否正在运行。</summary>
        bool IsActive { get; }

        /// <summary>是否已销毁。</summary>
        bool IsDestroyed { get; }

        /// <summary>一次性初始化，重复调用无副作用。</summary>
        /// <param name="ct">取消令牌。</param>
        UniTask InitGameplayAsync(CancellationToken ct = default);

        /// <summary>进入玩法。</summary>
        /// <param name="enterData">进入参数。</param>
        /// <param name="ct">取消令牌。</param>
        UniTask EnterGameplayAsync(GameplayEnterData enterData = null, CancellationToken ct = default);

        /// <summary>退出玩法。</summary>
        /// <param name="ct">取消令牌。</param>
        UniTask ExitGameplayAsync(CancellationToken ct = default);

        /// <summary>强制退出，取消进行中的异步并同步清理运行态。</summary>
        void ForceExit();

        /// <summary>销毁玩法实例，销毁后不可再次使用。</summary>
        void DestroyGameplay();

        /// <summary>每帧更新，仅运行中的玩法会收到有效驱动。</summary>
        /// <param name="deltaTime">距上一帧的时间间隔（秒）。</param>
        void Update(float deltaTime);
    }
}
