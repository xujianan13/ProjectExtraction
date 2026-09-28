namespace GameFramework
{
    /// <summary>
    /// 实例上的可选生命周期回调，用于重置血量、订阅和其他游戏状态；子节点组件同样参与。
    /// </summary>
    public interface IPrefabPoolCallbacks
    {
        /// <summary>
        /// 实例已摆放并激活，在交给调用方之前初始化本轮游戏状态。
        /// </summary>
        /// <param name="instance">仅代表本轮使用的实例句柄。</param>
        void OnSpawned(PrefabInstance instance);

        /// <summary>
        /// 实例停用、回收或销毁前释放本轮状态；此时旧句柄已失效。
        /// </summary>
        void OnDespawned();
    }
}
