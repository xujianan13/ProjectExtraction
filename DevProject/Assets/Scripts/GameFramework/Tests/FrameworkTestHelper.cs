namespace GameFramework.Tests
{
    /// <summary>
    /// 通过公开关闭入口重置框架，避免模块和时间缩放在测试间泄漏。
    /// </summary>
    internal static class FrameworkTestHelper
    {
        /// <summary>
        /// 卸载所有模块并清空注册表，恢复至未初始化状态。
        /// </summary>
        public static void ResetFramework()
        {
            GameEntry.StopFramework();
        }

        /// <summary>注册 LogModule 并启动框架（多数模块测试的最小依赖）。</summary>
        public static void SetupWithLog()
        {
            ResetFramework();
            GameEntry.Register<ILogModule>(new LogModule());
            GameEntry.StartFramework();
        }

        /// <summary>注册 Log + Event + Timer 并启动框架。</summary>
        public static void SetupWithLogEventTimer()
        {
            ResetFramework();
            GameEntry.Register<ILogModule>(new LogModule());
            GameEntry.Register<IEventModule>(new EventModule());
            GameEntry.Register<ITimerModule>(new TimerModule());
            GameEntry.StartFramework();
        }

        /// <summary>注册场景模块所需依赖并启动框架。</summary>
        public static void SetupForSceneTests()
        {
            ResetFramework();
            GameEntry.Register<ILogModule>(new LogModule());
            GameEntry.Register<IEventModule>(new EventModule());
            GameEntry.Register<ISceneModule>(new SceneModule());
            GameEntry.StartFramework();
        }
    }
}
