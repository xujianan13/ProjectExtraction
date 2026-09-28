namespace GameFramework
{
    /// <summary>
    /// 场景切换模式。
    /// </summary>
    public enum SceneChangeMode
    {
        /// <summary>替换：清空场景栈，退出当前后进入目标场景。</summary>
        Replace = 0,

        /// <summary>压栈：将当前场景压入栈，退出后进入目标场景。</summary>
        Push = 1,

        /// <summary>出栈：退出当前场景，恢复栈顶场景。</summary>
        Pop = 2,
    }
}
