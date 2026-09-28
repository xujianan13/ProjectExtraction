namespace GameFramework
{
    /// <summary>模型重置原因，用于区分退出、身份切换和会话失效。</summary>
    public enum ModelResetReason
    {
        /// <summary>用户主动退出当前账号。</summary>
        Logout = 0,

        /// <summary>当前账号发生变化。</summary>
        AccountChanged = 1,

        /// <summary>当前逻辑服或玩家发生变化。</summary>
        PlayerChanged = 2,

        /// <summary>游戏会话已确定失效，不能继续使用当前玩家数据。</summary>
        SessionInvalidated = 3
    }
}
