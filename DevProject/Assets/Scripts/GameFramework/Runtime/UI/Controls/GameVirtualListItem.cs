using UnityEngine;

namespace GameFramework
{
    /// <summary>
    /// 虚拟列表条目基类。每次绑定拥有独立作用域，重用前会停止并释放。
    /// </summary>
    public class GameVirtualListItem : MonoBehaviour
    {
        /// <summary>
        /// 当前数据索引；未绑定时为 -1。
        /// </summary>
        public int Index { get; private set; } = -1;

        /// <summary>
        /// 当前绑定作用域；异步回调写入视图前须检查 IsActive。
        /// </summary>
        public LifetimeScope BindingScope { get; private set; }

        /// <summary>
        /// 绑定一条数据。
        /// </summary>
        /// <param name="index">数据索引。</param>
        /// <param name="scope">本次绑定独占的作用域。</param>
        public virtual void Bind(int index, LifetimeScope scope)
        {
        }

        /// <summary>
        /// 解除当前数据绑定并清除可见内容。
        /// </summary>
        public virtual void Unbind()
        {
        }

        /// <summary>
        /// 将条目绑定到一条数据。
        /// </summary>
        /// <param name="index">数据索引。</param>
        internal void Attach(int index)
        {
            Detach();
            Index = index;
            BindingScope = new LifetimeScope($"VirtualListItem:{index}");
            try
            {
                Bind(index, BindingScope);
            }
            catch
            {
                Detach();
                throw;
            }
        }

        /// <summary>
        /// 停止异步工作并解除数据绑定。
        /// </summary>
        internal void Detach()
        {
            if (BindingScope == null) return;
            LifetimeScope scope = BindingScope;
            scope.Stop();
            LifetimeScope.CleanSafely(Unbind);
            scope.Dispose();
            BindingScope = null;
            Index = -1;
        }
        /// <summary>
        /// 防止条目被外部直接销毁后作用域仍存活。
        /// </summary>
        protected virtual void OnDestroy() => Detach();
    }
}
