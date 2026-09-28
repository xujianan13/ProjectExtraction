namespace GameFramework
{
    /// <summary>
    /// 可池化对象契约。实现此接口的类型在 <see cref="ObjectPool{T}"/> 租出 / 归还时自动回调。
    /// 也可通过构造池时传入 <c>Action&lt;T&gt;</c> 委托达到同样效果，二者可叠加。
    /// </summary>
    public interface IPoolable
    {
        /// <summary>从池中租出后调用，用于重置为可用状态。</summary>
        void OnRent();

        /// <summary>归还池前调用，用于清理引用或复位字段。</summary>
        void OnReturn();
    }
}
