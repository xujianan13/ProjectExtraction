using System.Threading;
using Cysharp.Threading.Tasks;

namespace GameFramework
{
    /// <summary>
    /// 逻辑场景契约（非 Unity 场景）。由业务层实现具体场景（登录、大世界、战斗等），
    /// 通过 <see cref="ISceneModule"/> 注册与切换。
    /// </summary>
    public interface IScene
    {
        /// <summary>场景唯一名称，用作注册与切换的 Key。</summary>
        string Name { get; }

        /// <summary>是否已完成一次性初始化（<see cref="InitSceneAsync"/>）。</summary>
        bool IsInited { get; }

        /// <summary>是否已完成预加载（<see cref="PreloadSceneAsync"/>）。</summary>
        bool IsPreloaded { get; }

        /// <summary>是否正在进入。</summary>
        bool IsEntering { get; }

        /// <summary>是否正在退出。</summary>
        bool IsExiting { get; }

        /// <summary>是否已激活（Enter 完成且未 Exit）。</summary>
        bool IsActive { get; }

        /// <summary>当前运行状态。</summary>
        SceneState State { get; }

        /// <summary>一次性初始化，重复调用无副作用。</summary>
        UniTask InitSceneAsync(CancellationToken ct = default);

        /// <summary>预加载（Init + 资源预热），重复调用无副作用。</summary>
        UniTask PreloadSceneAsync(CancellationToken ct = default);

        /// <summary>进入场景；由 SceneModule 在切换流程中调用。</summary>
        UniTask EnterSceneAsync(SceneEnterData enterData = null, CancellationToken ct = default);

        /// <summary>退出场景；由 SceneModule 在切换流程中调用。</summary>
        UniTask ExitSceneAsync(CancellationToken ct = default);

        /// <summary>强制退出：取消进行中的异步并立即清理，用于异常恢复或模块 Shutdown。</summary>
        void ForceExit();

        /// <summary>每帧更新，仅当前激活场景收到驱动。</summary>
        void Update(float deltaTime);
    }
}
