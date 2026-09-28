using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;

namespace GameLogic.Scaffold
{
    /// <summary>
    /// GameMain 的稳定脚手架接入点。
    /// </summary>
    public static class ProjectScaffoldBridge
    {
        /// <summary>
        /// 项目配置的逻辑首场景。
        /// </summary>
        public static string StartupScene => global::GameLogic.Scaffold.ProjectContent.StartupScene;

        /// <summary>
        /// 向框架登记项目内容。
        /// </summary>
        /// <param name="scenes">场景模块。</param>
        /// <param name="ui">窗口模块。</param>
        public static void Register(ISceneModule scenes, IUIModule ui)
            => global::GameLogic.Scaffold.ProjectContent.Register(scenes, ui);

        /// <summary>
        /// 进入项目逻辑首场景。
        /// </summary>
        /// <param name="ct">启动取消令牌。</param>
        /// <returns>场景进入任务。</returns>
        public static UniTask StartAsync(CancellationToken ct)
            => global::GameLogic.Scaffold.ProjectStartup.StartAsync(ct);
    }
}
