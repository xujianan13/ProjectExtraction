using System;
using UnityEngine;

namespace GameFramework
{
    /// <summary>
    /// 在实例被外部销毁时立即通知所属对象池。
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class PrefabPoolDestroyBridge : MonoBehaviour
    {
        private Action _onDestroyed;

        /// <summary>
        /// 绑定当前池记录的销毁通知。
        /// </summary>
        /// <param name="onDestroyed">对象销毁时执行的清理动作。</param>
        internal void Bind(Action onDestroyed) => _onDestroyed = onDestroyed;

        /// <summary>
        /// 将 Unity 销毁事件传递给对象池。
        /// </summary>
        private void OnDestroy()
        {
            Action onDestroyed = _onDestroyed;
            _onDestroyed = null;
            onDestroyed?.Invoke();
        }
    }
}
