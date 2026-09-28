using System;
using UnityEngine;

namespace GameFramework
{
    /// <summary>
    /// 承载主场景中的框架预制体，并在游戏启动时初始化通用模块。
    /// </summary>
    [DefaultExecutionOrder(-10000)]
    [DisallowMultipleComponent]
    public sealed class FrameworkBootstrap : MonoBehaviour
    {
        private const string LogTag = "FrameworkBootstrap";
        private static FrameworkBootstrap _active;

        private GameEntry _entry;
        private bool _started;

        /// <summary>
        /// 保留唯一的框架预制体实例，并启动默认模块。
        /// </summary>
        private void Awake()
        {
            if (_active != null && _active != this)
            {
                Destroy(gameObject);
                return;
            }

            if (GameEntry.HasHost || GameEntry.IsRunning)
            {
                Debug.LogError("[FrameworkBootstrap] 已存在 GameEntry 宿主，无法接管框架启动。");
                enabled = false;
                return;
            }

            _active = this;
            DontDestroyOnLoad(gameObject);
            try
            {
                _entry = GameEntry.Bootstrap();
                GameEntry.RegisterDefaults();
                GameEntry.Get<ILogModule>()?.AddSink(new UnityConsoleSink());
                GameEntry.StartFramework();
                _started = true;
            }
            catch (Exception exception)
            {
                var message = $"框架初始化失败：{exception}";
                GameEntry.Get<ILogModule>()?.Error(LogTag, message);
                Debug.LogError($"[FrameworkBootstrap] {message}");

                GameEntry.StopFramework();
                if (_entry != null) Destroy(_entry.gameObject);
                _entry = null;
                _active = null;
                Destroy(gameObject);
            }
        }

        /// <summary>
        /// 销毁框架预制体时停止模块并释放 GameEntry 宿主。
        /// </summary>
        private void OnDestroy()
        {
            if (_active != this) return;
            if (_started) GameEntry.StopFramework();
            if (_entry != null) Destroy(_entry.gameObject);
            _entry = null;
            _active = null;
        }
    }
}
