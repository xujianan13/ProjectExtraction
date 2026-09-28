namespace GameFramework
{
    /// <summary>
    /// 逻辑场景运行状态（非 UnityEngine.SceneManagement.Scene）。
    /// </summary>
    public enum SceneState
    {
        /// <summary>未初始化或已完全退出。</summary>
        Idle = 0,

        /// <summary>已激活，正在运行。</summary>
        Running = 1,

        /// <summary>正在进入。</summary>
        Entering = 2,

        /// <summary>正在退出。</summary>
        Exiting = 3,
    }
}
