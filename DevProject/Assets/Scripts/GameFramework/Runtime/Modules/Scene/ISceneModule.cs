using System.Threading;
using Cysharp.Threading.Tasks;

namespace GameFramework
{
    /// <summary>
    /// 逻辑场景管理模块。负责场景注册、切换（替换 / 压栈 / 出栈）、预加载与当前场景 Update 驱动。
    /// 业务通过 <see cref="GameEntry.Get{ISceneModule}"/> 访问；具体场景实现放在 GameLogic 层。
    /// </summary>
    public interface ISceneModule : IModule
    {
        /// <summary>当前激活的场景；无则 null。</summary>
        IScene CurrentScene { get; }

        /// <summary>场景栈深度（不含当前场景）。</summary>
        int StackDepth { get; }

        /// <summary>是否正在执行切换流程。</summary>
        bool IsChanging { get; }

        /// <summary>注册场景。同名重复注册会写错误日志并忽略。</summary>
        void RegisterScene(IScene scene);

        /// <summary>按名称注销场景。若场景正在激活或位于栈中则拒绝。</summary>
        void UnregisterScene(string name);

        /// <summary>注销场景实例。若场景正在激活或位于栈中则拒绝。</summary>
        void UnregisterScene(IScene scene);

        /// <summary>按名称获取已注册场景；未找到返回 null。</summary>
        IScene GetScene(string name);

        /// <summary>预加载场景（Init + OnPreload），不进入场景。</summary>
        UniTask PreloadSceneAsync(string name, CancellationToken ct = default);

        /// <summary>预加载场景（Init + OnPreload），不进入场景。</summary>
        UniTask PreloadSceneAsync(IScene scene, CancellationToken ct = default);

        /// <summary>退出当前场景（不进入新场景、不操作栈）。</summary>
        void ExitCurrentScene();

        /// <summary>退出当前场景（异步）。</summary>
        UniTask ExitCurrentSceneAsync(CancellationToken ct = default);

        /// <summary>替换当前场景（清空场景栈）。</summary>
        void ChangeScene(string name, SceneEnterData enterData = null);

        /// <summary>替换当前场景（清空场景栈）。</summary>
        void ChangeScene(IScene scene, SceneEnterData enterData = null);

        /// <summary>替换当前场景（异步，清空场景栈）。</summary>
        UniTask ChangeSceneAsync(string name, SceneEnterData enterData = null, CancellationToken ct = default);

        /// <summary>替换当前场景（异步，清空场景栈）。</summary>
        UniTask ChangeSceneAsync(IScene scene, SceneEnterData enterData = null, CancellationToken ct = default);

        /// <summary>压栈并进入新场景（保留当前场景以便 Pop 恢复）。</summary>
        void PushScene(string name, SceneEnterData enterData = null);

        /// <summary>压栈并进入新场景。</summary>
        void PushScene(IScene scene, SceneEnterData enterData = null);

        /// <summary>压栈并进入新场景（异步）。</summary>
        UniTask PushSceneAsync(string name, SceneEnterData enterData = null, CancellationToken ct = default);

        /// <summary>压栈并进入新场景（异步）。</summary>
        UniTask PushSceneAsync(IScene scene, SceneEnterData enterData = null, CancellationToken ct = default);

        /// <summary>退出当前场景并恢复栈顶场景。</summary>
        void PopScene();

        /// <summary>退出当前场景并恢复栈顶场景（异步）。</summary>
        UniTask PopSceneAsync(CancellationToken ct = default);
    }
}
