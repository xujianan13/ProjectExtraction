namespace GameFramework
{
    /// <summary>
    /// 可从对象池租出、并通过 <see cref="Release"/> 归还的实例。
    /// <see cref="BindPool"/> 由 <see cref="PoolModule"/> 在 Rent 时注入，业务侧勿调用。
    /// </summary>
    public interface IPooledObject
    {
        /// <summary>归还到租出时的对象池。</summary>
        void Release();

        /// <summary>由 PoolModule 在 Rent 时绑定归还目标，业务侧勿调用。</summary>
        /// <param name="poolModule">对象池模块。</param>
        /// <param name="name">租出来源池名称；null 或空字符串表示默认池。</param>
        void BindPool(IPoolModule poolModule, string name);
    }
}
