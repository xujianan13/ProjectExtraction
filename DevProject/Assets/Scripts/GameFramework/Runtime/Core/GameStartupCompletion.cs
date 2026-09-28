using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace GameFramework
{
    /// <summary>
    /// 热更程序完成加载后由宿主等待的项目启动收尾结果。
    /// </summary>
    public readonly struct GameStartupCompletionResult
    {
        public bool Success { get; }
        public string Message { get; }
        public bool CanRetry { get; }

        /// <summary>
        /// 创建项目启动收尾结果。
        /// </summary>
        /// <param name="success">是否完成。</param>
        /// <param name="message">失败说明。</param>
        /// <param name="canRetry">是否允许仅重试收尾。</param>
        public GameStartupCompletionResult(bool success, string message = null, bool canRetry = false)
        {
            Success = success;
            Message = message ?? string.Empty;
            CanRetry = canRetry;
        }
    }

    /// <summary>
    /// 项目数据阻止启动时交给宿主展示的错误。
    /// </summary>
    public sealed class GameStartupDataException : Exception
    {
        /// <summary>
        /// 创建项目数据启动错误。
        /// </summary>
        /// <param name="message">玩家可读的错误说明。</param>
        public GameStartupDataException(string message) : base(message) { }
    }

    /// <summary>
    /// 宿主与热更程序共享的单次启动收尾回调。
    /// </summary>
    public static class GameStartupCompletion
    {
        private static Func<CancellationToken, UniTask<GameStartupCompletionResult>> callback;

        /// <summary>
        /// 注册当前游戏启动的收尾操作。
        /// </summary>
        /// <param name="completion">收尾回调。</param>
        /// <returns>注册是否成功。</returns>
        public static bool Register(Func<CancellationToken, UniTask<GameStartupCompletionResult>> completion)
        {
            if (completion == null || callback != null) return false;
            callback = completion;
            return true;
        }

        /// <summary>
        /// 执行项目收尾；没有注册回调时视为完成。
        /// </summary>
        /// <param name="ct">启动取消令牌。</param>
        /// <returns>项目收尾结果。</returns>
        public static UniTask<GameStartupCompletionResult> CompleteAsync(CancellationToken ct)
            => callback == null ? UniTask.FromResult(new GameStartupCompletionResult(true)) : callback(ct);

        /// <summary>
        /// 清除当前启动的回调。
        /// </summary>
        public static void Clear() => callback = null;

        /// <summary>
        /// 禁用域重载时清理上一次运行的静态引用。
        /// </summary>
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => Clear();
    }
}
