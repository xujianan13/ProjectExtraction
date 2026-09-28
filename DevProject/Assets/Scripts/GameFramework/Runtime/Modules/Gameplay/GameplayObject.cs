using System;
using System.Threading;

namespace GameFramework
{
    /// <summary>
    /// 玩法内部对象基类，提供 Root 与局部对象查询能力。
    /// </summary>
    public abstract class GameplayObject
    {
        /// <summary>
        /// 所属玩法根节点。
        /// </summary>
        public GameplayRoot Root { get; private set; }

        /// <summary>
        /// 所属玩法本轮运行的作用域；在 Enter 中登记本轮行为，初始化阶段为空。
        /// 异步操作须捕获此对象，避免旧任务读取新一轮作用域。
        /// </summary>
        protected LifetimeScope GameplayScope => Root?.GameplayScope;

        /// <summary>
        /// 所属玩法本轮运行的取消令牌；开始退出即取消。
        /// </summary>
        protected CancellationToken GameplayToken => GameplayScope?.Token ?? default;

        /// <summary>
        /// 玩法名称。
        /// </summary>
        protected string GameplayName => Root?.Name;

        /// <summary>
        /// 框架日志模块。
        /// </summary>
        protected ILogModule Log => GameEntry.Get<ILogModule>();

        /// <summary>
        /// 绑定到玩法根节点。
        /// </summary>
        /// <param name="root">玩法根节点。</param>
        /// <exception cref="ArgumentNullException">root 为 null。</exception>
        internal void Attach(GameplayRoot root)
        {
            Root = root ?? throw new ArgumentNullException(nameof(root));
        }

        /// <summary>
        /// 获取玩法内功能模块。
        /// </summary>
        /// <typeparam name="T">功能模块类型。</typeparam>
        /// <returns>对应的功能模块；未找到时为空。</returns>
        protected T GetFeature<T>() where T : GameplayFeature
        {
            return Root.GetFeature<T>();
        }

        /// <summary>
        /// 获取玩法内数据模型。
        /// </summary>
        /// <typeparam name="T">数据模型类型。</typeparam>
        /// <returns>对应的数据模型；未找到时为空。</returns>
        protected T GetModel<T>() where T : GameplayModel
        {
            return Root.GetModel<T>();
        }
    }
}
