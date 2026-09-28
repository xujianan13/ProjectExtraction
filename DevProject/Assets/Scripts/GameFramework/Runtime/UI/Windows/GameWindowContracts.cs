using System;

namespace GameFramework
{
    /// <summary>
    /// 窗口实例的运行状态。
    /// </summary>
    public enum GameWindowState : byte
    {
        /// <summary>
        /// 尚未绑定实例。
        /// </summary>
        None = 0,

        /// <summary>
        /// 已关闭并处于缓存中。
        /// </summary>
        Closed = 1,

        /// <summary>
        /// 正在显示。
        /// </summary>
        Opened = 2,

        /// <summary>
        /// 已销毁，不能再次使用。
        /// </summary>
        Destroyed = 3
    }

    /// <summary>
    /// 窗口关闭后的实例缓存策略。
    /// </summary>
    public enum GameWindowCacheMode : byte
    {
        /// <summary>
        /// 关闭后立即销毁实例并释放 Prefab 租约。
        /// </summary>
        Immediate = 0,

        /// <summary>
        /// 关闭后进入 LRU，达到超时或容量上限时销毁。
        /// </summary>
        Timed = 1,

        /// <summary>
        /// 关闭后常驻内存，仅在作用域退出或服务卸载时销毁。
        /// </summary>
        Resident = 2
    }

    /// <summary>
    /// 窗口所属的逻辑作用域。
    /// </summary>
    public enum GameWindowScope : byte
    {
        /// <summary>
        /// 跨场景存在，直到 UI 服务卸载。
        /// </summary>
        Global = 0,

        /// <summary>
        /// 场景作用域，退出场景时销毁。
        /// </summary>
        Scene = 1,

        /// <summary>
        /// 游戏会话作用域，结束会话时销毁。
        /// </summary>
        Session = 2
    }

    /// <summary>
    /// 窗口关闭原因，供窗口逻辑区分用户操作与系统回收。
    /// </summary>
    public enum GameWindowCloseReason : byte
    {
        /// <summary>
        /// 用户或业务主动关闭。
        /// </summary>
        User = 0,

        /// <summary>
        /// 点击窗口外区域关闭。
        /// </summary>
        OutsideClick = 1,

        /// <summary>
        /// 关联规则触发关闭。
        /// </summary>
        Association = 2,

        /// <summary>
        /// 关闭全部窗口。
        /// </summary>
        CloseAll = 3,

        /// <summary>
        /// 逻辑作用域退出。
        /// </summary>
        ScopeExit = 4,

        /// <summary>
        /// 窗口服务卸载。
        /// </summary>
        Shutdown = 5
    }

    /// <summary>
    /// 一次窗口打开请求携带的业务参数。
    /// </summary>
    public sealed class GameWindowOpenArgs
    {
        /// <summary>
        /// 创建打开参数。
        /// </summary>
        /// <param name="userData">传递给窗口的业务数据；窗口自行校验具体类型。</param>
        /// <param name="playSound">是否播放本次直接打开的配置音效。</param>
        public GameWindowOpenArgs(object userData = null, bool playSound = true)
        {
            UserData = userData;
            PlaySound = playSound;
        }

        /// <summary>
        /// 业务自定义数据。
        /// </summary>
        public object UserData { get; }

        /// <summary>
        /// 是否播放打开音效。
        /// </summary>
        public bool PlaySound { get; }

        internal static GameWindowOpenArgs Association { get; } = new(null, false);
    }

    /// <summary>
    /// 窗口管理器的运行参数。
    /// </summary>
    public sealed class GameWindowManagerSettings
    {
        /// <summary>
        /// 创建默认设置。
        /// </summary>
        /// <param name="maxClosedWindows">允许保留的非 Resident 已关闭窗口数量。</param>
        /// <param name="sortingOrder">窗口根 Canvas 的排序值。</param>
        public GameWindowManagerSettings(int maxClosedWindows = 8, int sortingOrder = 32740)
        {
            if (maxClosedWindows < 0)
                throw new ArgumentOutOfRangeException(nameof(maxClosedWindows));
            MaxClosedWindows = maxClosedWindows;
            SortingOrder = sortingOrder;
        }

        /// <summary>
        /// 非 Resident 已关闭窗口的 LRU 容量。
        /// </summary>
        public int MaxClosedWindows { get; }

        /// <summary>
        /// 窗口根 Canvas 的排序值。
        /// </summary>
        public int SortingOrder { get; }
    }
}
