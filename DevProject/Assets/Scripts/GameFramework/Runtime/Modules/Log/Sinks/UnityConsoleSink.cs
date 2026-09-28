using UnityEngine;

namespace GameFramework
{
    /// <summary>
    /// Unity 控制台 Sink：把日志按级别映射到 UnityEngine.Debug 的 Log / LogWarning / LogError。
    /// 开发期默认启用，发布版可按需移除。
    /// </summary>
    public sealed class UnityConsoleSink : ILogSink
    {
        /// <inheritdoc />
        public void Write(LogLevel level, string tag, string message)
        {
            switch (level)
            {
                case LogLevel.Warn:
                    UnityEngine.Debug.LogWarning($"[{tag}] {message}");
                    break;
                case LogLevel.Error:
                case LogLevel.Fatal:
                    UnityEngine.Debug.LogError($"[{tag}] {message}");
                    break;
                default:
                    UnityEngine.Debug.Log($"[{tag}] {message}");
                    break;
            }
        }

        /// <inheritdoc />
        public void Flush() { }
    }
}
