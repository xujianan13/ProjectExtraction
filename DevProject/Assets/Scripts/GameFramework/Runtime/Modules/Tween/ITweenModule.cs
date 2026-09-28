using System.Threading;

namespace GameFramework
{
    /// <summary>
    /// 管理活动补间的模块接口。
    /// </summary>
    public interface ITweenModule : IModule
    {
        /// <summary>
        /// 当前仍在播放或暂停的补间数。
        /// </summary>
        int ActiveCount { get; }

        /// <summary>
        /// 播放片段；作用域停止、令牌取消或模块关闭时结束播放。
        /// </summary>
        /// <param name="clip">待播放片段。</param><param name="scope">可选的对象使用期作用域。</param>
        /// <param name="cancellationToken">可选取消令牌。</param><param name="unscaledTime">是否使用非缩放时间。</param>
        /// <returns>可暂停、取消及等待的播放句柄。</returns>
        TweenHandle Play(TweenClip clip, LifetimeScope scope = null, CancellationToken cancellationToken = default, bool unscaledTime = false);

        /// <summary>
        /// 取消所有活动播放。
        /// </summary>
        void CancelAll();
    }
}
