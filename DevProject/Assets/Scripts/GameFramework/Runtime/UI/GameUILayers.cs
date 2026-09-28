namespace GameFramework
{
    /// <summary>
    /// 业务窗口的标准层级；数值较大的窗口显示在较上方。
    /// </summary>
    public static class GameUILayers
    {
        /// <summary>
        /// 常驻血条、操作按钮和状态信息。
        /// </summary>
        public const int Hud = 0;

        /// <summary>
        /// 主菜单、背包和设置等完整页面。
        /// </summary>
        public const int Screen = 100;

        /// <summary>
        /// 业务弹窗和短流程面板。
        /// </summary>
        public const int Popup = 200;

        /// <summary>
        /// 新手引导和全屏交互指引。
        /// </summary>
        public const int Guide = 300;
    }
}
