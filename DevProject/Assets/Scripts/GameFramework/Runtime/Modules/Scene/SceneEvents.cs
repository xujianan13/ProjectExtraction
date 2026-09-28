namespace GameFramework
{
    /// <summary>场景模块相关事件 Id。</summary>
    public static class SceneEvents
    {
        /// <summary>逻辑场景切换完成。</summary>
        public static readonly EventId Changed = 20001;
    }

    /// <summary>场景切换完成事件载荷。</summary>
    public readonly struct SceneChangedEvent
    {
        /// <summary>切换前的场景名；无则 null。</summary>
        public readonly string FromName;

        /// <summary>切换后的场景名；退出到空场景时为 null。</summary>
        public readonly string ToName;

        /// <summary>本次切换模式。</summary>
        public readonly SceneChangeMode Mode;

        /// <summary>切换完成后的场景栈深度（不含当前场景）。</summary>
        public readonly int StackDepth;

        /// <summary>创建场景切换事件。</summary>
        public SceneChangedEvent(string fromName, string toName, SceneChangeMode mode, int stackDepth)
        {
            FromName = fromName;
            ToName = toName;
            Mode = mode;
            StackDepth = stackDepth;
        }
    }
}
