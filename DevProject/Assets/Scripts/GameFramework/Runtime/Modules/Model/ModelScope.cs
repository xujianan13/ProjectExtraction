namespace GameFramework
{
    /// <summary>模型所持数据的所有者作用域，用于在身份边界变化时选择性重置。</summary>
    public enum ModelScope
    {
        /// <summary>应用全程共享的数据；仅显式重置应用作用域时清理。</summary>
        Application = 0,

        /// <summary>随账号变化的数据；重置账号时同时清理玩家作用域。</summary>
        Account = 1,

        /// <summary>随逻辑服或玩家变化的数据。</summary>
        Player = 2
    }
}
