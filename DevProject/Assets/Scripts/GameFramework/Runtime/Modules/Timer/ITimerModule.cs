using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace GameFramework
{
    /// <summary>
    /// 定时器模块接口，默认使用游戏缩放时间，按需使用非缩放时间。
    /// </summary>
    public interface ITimerModule : IModule
    {
        /// <summary>
        /// 延时后执行一次，非正延时在下一帧执行。
        /// </summary>
        /// <param name="delay">等待秒数。</param>
        /// <param name="callback">到期回调。</param>
        /// <returns>可取消的定时器句柄。</returns>
        TimerHandle Once(float delay, Action callback);

        /// <summary>
        /// 使用指定时间源延时执行一次。
        /// </summary>
        /// <param name="delay">等待秒数。</param>
        /// <param name="callback">到期回调。</param>
        /// <param name="unscaledTime">是否使用非缩放时间并在游戏暂停时继续执行。</param>
        /// <returns>可取消的定时器句柄。</returns>
        TimerHandle Once(float delay, Action callback, bool unscaledTime);

        /// <summary>
        /// 按间隔执行回调。
        /// </summary>
        /// <param name="interval">两次执行之间的秒数。</param>
        /// <param name="callback">到期回调。</param>
        /// <param name="repeatCount">执行次数；负一表示持续执行。</param>
        /// <returns>可取消的定时器句柄。</returns>
        TimerHandle Loop(float interval, Action callback, int repeatCount = -1);

        /// <summary>
        /// 使用指定时间源按间隔执行回调。
        /// </summary>
        /// <param name="interval">两次执行之间的秒数。</param>
        /// <param name="callback">到期回调。</param>
        /// <param name="repeatCount">执行次数；负一表示持续执行。</param>
        /// <param name="unscaledTime">是否使用非缩放时间并在游戏暂停时继续执行。</param>
        /// <returns>可取消的定时器句柄。</returns>
        TimerHandle Loop(float interval, Action callback, int repeatCount, bool unscaledTime);

        /// <summary>
        /// 下一帧执行一次；回调中创建的下一帧任务不会在当前帧执行。
        /// </summary>
        /// <param name="callback">下一帧回调。</param>
        /// <returns>可取消的定时器句柄。</returns>
        TimerHandle NextFrame(Action callback);

        /// <summary>
        /// 使用指定暂停行为在下一帧执行一次。
        /// </summary>
        /// <param name="callback">下一帧回调。</param>
        /// <param name="unscaledTime">游戏暂停时是否继续执行。</param>
        /// <returns>可取消的定时器句柄。</returns>
        TimerHandle NextFrame(Action callback, bool unscaledTime);

        /// <summary>
        /// 异步等待指定秒数；取消或模块关闭时结束等待。
        /// </summary>
        /// <param name="delay">等待秒数。</param>
        /// <param name="ct">调用方取消令牌。</param>
        /// <returns>等待任务。</returns>
        UniTask DelayAsync(float delay, CancellationToken ct = default);

        /// <summary>
        /// 使用指定时间源异步等待。
        /// </summary>
        /// <param name="delay">等待秒数。</param>
        /// <param name="ct">调用方取消令牌。</param>
        /// <param name="unscaledTime">是否使用非缩放时间并在游戏暂停时继续执行。</param>
        /// <returns>等待任务。</returns>
        UniTask DelayAsync(float delay, CancellationToken ct, bool unscaledTime);

        /// <summary>
        /// 异步等待下一帧；取消或模块关闭时结束等待。
        /// </summary>
        /// <param name="ct">调用方取消令牌。</param>
        /// <returns>等待任务。</returns>
        UniTask NextFrameAsync(CancellationToken ct = default);

        /// <summary>
        /// 使用指定暂停行为异步等待下一帧。
        /// </summary>
        /// <param name="ct">调用方取消令牌。</param>
        /// <param name="unscaledTime">游戏暂停时是否继续执行。</param>
        /// <returns>等待任务。</returns>
        UniTask NextFrameAsync(CancellationToken ct, bool unscaledTime);

        /// <summary>
        /// 取消指定定时器。
        /// </summary>
        /// <param name="handle">待取消句柄。</param>
        /// <returns>是否取消了活动定时器。</returns>
        bool Cancel(TimerHandle handle);

        /// <summary>
        /// 取消所有定时器。
        /// </summary>
        void CancelAll();
    }
}
