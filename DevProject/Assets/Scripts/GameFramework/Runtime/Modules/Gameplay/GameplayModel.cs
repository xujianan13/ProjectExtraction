using System.Threading;
using Cysharp.Threading.Tasks;

namespace GameFramework
{
    /// <summary>
    /// 玩法内数据模型基类，用于保存某个玩法实例的运行数据和状态。
    /// </summary>
    public abstract class GameplayModel : GameplayObject
    {
        /// <summary>初始化数据模型。</summary>
        /// <param name="ct">取消令牌。</param>
        public virtual UniTask InitAsync(CancellationToken ct = default) => UniTask.CompletedTask;

        /// <summary>进入数据模型。</summary>
        /// <param name="enterData">玩法进入参数。</param>
        /// <param name="ct">取消令牌。</param>
        public virtual UniTask EnterAsync(GameplayEnterData enterData = null, CancellationToken ct = default)
            => UniTask.CompletedTask;

        /// <summary>退出数据模型。</summary>
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

        /// <summary>销毁数据模型。</summary>
        public virtual void Destroy() { }
    }

    /// <summary>
    /// 强类型玩法内数据模型基类，业务侧可直接访问具体玩法 Root。
    /// </summary>
    /// <typeparam name="TRoot">具体玩法根节点类型。</typeparam>
    public abstract class GameplayModel<TRoot> : GameplayModel where TRoot : GameplayRoot<TRoot>
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
