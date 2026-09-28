using System.Threading;
using Cysharp.Threading.Tasks;

namespace GameFramework
{
    /// <summary>
    /// 玩法内功能模块基类，用于封装某个玩法实例内的局部服务或系统。
    /// </summary>
    public abstract class GameplayFeature : GameplayObject
    {
        /// <summary>初始化功能模块。</summary>
        /// <param name="ct">取消令牌。</param>
        public virtual UniTask InitAsync(CancellationToken ct = default) => UniTask.CompletedTask;

        /// <summary>进入功能模块。</summary>
        /// <param name="enterData">玩法进入参数。</param>
        /// <param name="ct">取消令牌。</param>
        public virtual UniTask EnterAsync(GameplayEnterData enterData = null, CancellationToken ct = default)
            => UniTask.CompletedTask;

        /// <summary>退出功能模块。</summary>
        /// <param name="ct">取消令牌。</param>
        public virtual UniTask ExitAsync(CancellationToken ct = default) => UniTask.CompletedTask;

        /// <summary>每帧更新。</summary>
        /// <param name="deltaTime">距上一帧的时间间隔（秒）。</param>
        public virtual void Update(float deltaTime) { }

        /// <summary>固定帧更新。</summary>
        /// <param name="deltaTime">固定帧时间间隔（秒）。</param>
        public virtual void FixedUpdate(float deltaTime) { }

        /// <summary>帧末更新。</summary>
        /// <param name="deltaTime">距上一帧的时间间隔（秒）。</param>
        public virtual void LateUpdate(float deltaTime) { }

        /// <summary>强制退出时的同步清理。</summary>
        public virtual void ForceExit() { }

        /// <summary>销毁功能模块。</summary>
        public virtual void Destroy() { }
    }

    /// <summary>
    /// 强类型玩法内功能模块基类，业务侧可直接访问具体玩法 Root。
    /// </summary>
    /// <typeparam name="TRoot">具体玩法根节点类型。</typeparam>
    public abstract class GameplayFeature<TRoot> : GameplayFeature where TRoot : GameplayRoot<TRoot>
    {
        /// <summary>所属具体玩法根节点。</summary>
        public new TRoot Root => (TRoot)base.Root;

        /// <summary>获取玩法内强类型功能模块。</summary>
        /// <typeparam name="T">功能模块类型。</typeparam>
        protected new T GetFeature<T>() where T : GameplayFeature<TRoot>
        {
            return Root.GetFeature<T>();
        }

        /// <summary>获取玩法内强类型数据模型。</summary>
        /// <typeparam name="T">数据模型类型。</typeparam>
        protected new T GetModel<T>() where T : GameplayModel<TRoot>
        {
            return Root.GetModel<T>();
        }
    }
}
