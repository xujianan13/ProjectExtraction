using Cysharp.Threading.Tasks;
using UnityEngine;

namespace GameLogic.Project
{
    /// <summary>
    /// 将应用后台状态转发给项目流程，清除暂停期间残留的输入。
    /// </summary>
    public sealed class ProjectRunLifecycle : MonoBehaviour
    {
        private bool paused;
        private bool focused = true;

        /// <summary>
        /// 更新应用暂停状态。
        /// </summary>
        /// <param name="value">是否暂停。</param>
        private void OnApplicationPause(bool value)
        {
            paused = value;
            Publish();
        }

        /// <summary>
        /// 更新应用焦点状态。
        /// </summary>
        /// <param name="value">是否拥有焦点。</param>
        private void OnApplicationFocus(bool value)
        {
            focused = value;
            Publish();
        }

        /// <summary>
        /// 同步合并后的后台状态，异步保存和终局处理仍可继续。
        /// </summary>
        private void Publish()
        {
            ProjectRunCoordinator coordinator = ProjectServices.Coordinator;
            if (coordinator != null) coordinator.SetApplicationPausedAsync(paused || !focused).Forget();
        }
    }
}
