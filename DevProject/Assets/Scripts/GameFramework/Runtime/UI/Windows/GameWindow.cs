using System;
using Cysharp.Threading.Tasks;

namespace GameFramework
{
    /// <summary>
    /// 窗口逻辑基类。具体窗口可用 partial class 分隔生命周期和组件引用绑定。
    /// 本类型不是 MonoBehaviour，确保资源实例化不会抢先触发业务生命周期。
    /// </summary>
    public abstract partial class GameWindow
    {
        private GameWindowManager _owner;

        /// <summary>
        /// 窗口实例从初始化到最终销毁的作用域。
        /// </summary>
        protected LifetimeScope InstanceScope { get; private set; }

        /// <summary>
        /// 当前打开轮次的作用域；窗口关闭后为空。
        /// </summary>
        protected LifetimeScope OpenScope { get; private set; }

        /// <summary>
        /// 当前窗口配置；仅在 Init 及之后可用。
        /// </summary>
        public GameWindowConfig Config { get; private set; }

        /// <summary>
        /// 窗口当前状态。
        /// </summary>
        public GameWindowState State { get; private set; }

        /// <summary>
        /// 窗口 ID；尚未初始化时为零。
        /// </summary>
        public int Id => Config?.Id ?? 0;

        /// <summary>
        /// 窗口实例化并完成组件绑定后调用一次。
        /// </summary>
        protected virtual void Init()
        {
        }

        /// <summary>
        /// 每轮从关闭转为打开时调用一次，用于登记本轮订阅和句柄。
        /// </summary>
        /// <param name="args">本轮打开参数。</param>
        protected virtual void Open(GameWindowOpenArgs args)
        {
        }

        /// <summary>
        /// 每次打开或重复聚焦窗口时调用。
        /// </summary>
        /// <param name="args">本次打开参数。</param>
        protected virtual void Show(GameWindowOpenArgs args)
        {
        }

        /// <summary>
        /// 每次从打开状态转为关闭状态时调用。
        /// </summary>
        /// <param name="reason">关闭原因。</param>
        protected virtual void Close(GameWindowCloseReason reason)
        {
        }

        /// <summary>
        /// 窗口真正销毁前调用一次；此后组件引用不再可用。
        /// </summary>
        protected virtual void Destroy()
        {
        }

        /// <summary>
        /// 窗口逻辑请求关闭自身。
        /// </summary>
        /// <param name="reason">窗口关闭原因。</param>
        /// <returns>自身窗口是否关闭。</returns>
        protected UniTask<bool> CloseSelfAsync(GameWindowCloseReason reason = GameWindowCloseReason.User)
        {
            if (_owner == null || Id == 0) return UniTask.FromResult(false);
            return _owner.CloseAsync(Id, reason);
        }

        /// <summary>
        /// 绑定窗口实例并执行一次初始化。
        /// </summary>
        /// <param name="owner">窗口管理器。</param>
        /// <param name="config">窗口配置。</param>
        /// <param name="root">窗口根对象。</param>
        internal void InitializeInternal(GameWindowManager owner, GameWindowConfig config, UnityEngine.GameObject root)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (Config != null || State != GameWindowState.None)
                throw new InvalidOperationException("窗口实例不能重复初始化。");

            _owner = owner;
            Config = config;
            InstanceScope = new LifetimeScope($"Window:{config.Id}");
            AttachComponentsInternal(root);
            Init();
            State = GameWindowState.Closed;
        }

        /// <summary>
        /// 执行窗口显示回调并更新状态。
        /// </summary>
        /// <param name="args">窗口打开参数。</param>
        internal void ShowInternal(GameWindowOpenArgs args)
        {
            if (State != GameWindowState.Closed && State != GameWindowState.Opened)
                throw new InvalidOperationException($"窗口 {Id} 当前状态不能打开：{State}");
            if (InstanceScope == null || !InstanceScope.IsActive)
                throw new InvalidOperationException($"窗口 {Id} 实例作用域已停止，不能重新打开。");
            args ??= new GameWindowOpenArgs();
            bool firstOpen = State == GameWindowState.Closed;
            if (firstOpen) OpenScope = InstanceScope.CreateChild($"WindowOpen:{Id}");
            try
            {
                if (firstOpen) Open(args);
                Show(args);
                State = GameWindowState.Opened;
            }
            catch
            {
                if (firstOpen)
                {
                    OpenScope?.Stop();
                    OpenScope?.Dispose();
                    OpenScope = null;
                }
                throw;
            }
        }

        /// <summary>
        /// 执行窗口关闭回调并更新状态。
        /// </summary>
        /// <param name="reason">窗口关闭原因。</param>
        internal void CloseInternal(GameWindowCloseReason reason)
        {
            if (State != GameWindowState.Opened) return;
            OpenScope?.Stop();
            try
            {
                Close(reason);
            }
            finally
            {
                OpenScope?.Dispose();
                OpenScope = null;
                State = GameWindowState.Closed;
            }
        }

        /// <summary>
        /// 请求销毁后立即停止异步业务，销毁回调仍由管理器按原流程执行。
        /// </summary>
        internal void StopScopesInternal()
        {
            OpenScope?.Stop();
            InstanceScope?.Stop();
        }

        /// <summary>
        /// 执行销毁回调并清理组件引用。
        /// </summary>
        internal void DestroyInternal()
        {
            if (State == GameWindowState.Destroyed) return;
            StopScopesInternal();
            try
            {
                Destroy();
            }
            finally
            {
                OpenScope?.Dispose();
                OpenScope = null;
                InstanceScope?.Dispose();
                InstanceScope = null;
                State = GameWindowState.Destroyed;
                ClearComponentsInternal();
                Config = null;
                _owner = null;
            }
        }
    }
}
