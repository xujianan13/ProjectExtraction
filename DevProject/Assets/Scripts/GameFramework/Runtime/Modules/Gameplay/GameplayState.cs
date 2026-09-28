namespace GameFramework
{
    /// <summary>
    /// 玩法实例运行状态。
    /// </summary>
    public enum GameplayState
    {
        /// <summary>空闲，尚未初始化或已正常退出。</summary>
        Idle,

        /// <summary>正在初始化。</summary>
        Initializing,

        /// <summary>已初始化，尚未进入。</summary>
        Ready,

        /// <summary>正在进入。</summary>
        Entering,

        /// <summary>运行中。</summary>
        Running,

        /// <summary>正在退出。</summary>
        Exiting,

        /// <summary>已销毁，不可再次使用。</summary>
        Destroyed,
    }
}
