using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;
using UnityEngine.SceneManagement;

namespace GameLogic.Scaffold
{
    /// <summary>
    /// 项目手写启动入口；按需要在这里扩展业务准备。
    /// </summary>
    public static class ProjectStartup
    {
        /// <summary>
        /// 项目逻辑首场景；最小模板由脚手架配置 Home。
        /// </summary>
        public static string StartupScene => ProjectContent.StartupScene;

        /// <summary>
        /// 进入项目逻辑首场景。
        /// </summary>
        /// <param name="ct">启动取消令牌。</param>
        /// <returns>场景进入任务。</returns>
        public static UniTask StartAsync(CancellationToken ct)
        {
            if (ProjectContent.StartupUsesUnityScene)
            {
                Scene host = SceneManager.GetSceneByName("[ScaffoldHost]");
                if (!host.IsValid() || !host.isLoaded) host = SceneManager.CreateScene("[ScaffoldHost]");
                SceneManager.SetActiveScene(host);
            }
            ISceneModule scenes = GameEntry.Get<ISceneModule>();
            return scenes.ChangeSceneAsync(StartupScene, ct: ct);
        }
    }
}
