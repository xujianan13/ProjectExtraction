using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.Audio;

namespace GameFramework
{
    /// <summary>
    /// 加载和播放音频，并管理各通道的音量、声源与路由。
    /// </summary>
    public interface IAudioModule : IModule
    {
        /// <summary>
        /// 主音量，范围为 0 到 1；变更立即影响所有正在播放的声源。
        /// </summary>
        float MasterVolume { get; set; }

        /// <summary>
        /// 读取通道音量。
        /// </summary>
        /// <param name="channel">目标通道。</param>
        /// <returns>通道音量。</returns>
        float GetChannelVolume(AudioChannel channel);

        /// <summary>
        /// 设置通道音量，范围为 0 到 1；变更立即影响该通道的声源。
        /// </summary>
        /// <param name="channel">目标通道。</param>
        /// <param name="volume">目标音量。</param>
        void SetChannelVolume(AudioChannel channel, float volume);

        /// <summary>
        /// 以默认二维配置异步播放音频。
        /// </summary>
        /// <param name="address">音频资源地址。</param>
        /// <param name="channel">播放通道。</param>
        /// <param name="loop">是否循环播放。</param>
        /// <param name="ct">调用方取消令牌；取消时抛出取消异常。</param>
        /// <returns>播放句柄；加载失败、通道满额或内部终止时返回无效句柄。</returns>
        UniTask<AudioHandle> PlayAsync(string address, AudioChannel channel, bool loop = false, CancellationToken ct = default);

        /// <summary>
        /// 按指定配置异步播放音频，配置在调用时复制。
        /// </summary>
        /// <param name="address">音频资源地址。</param>
        /// <param name="channel">播放通道。</param>
        /// <param name="options">播放配置；为 null 时采用默认二维配置。</param>
        /// <param name="ct">调用方取消令牌；取消时抛出取消异常。</param>
        /// <returns>播放句柄；配置无效、加载失败、通道满额或内部终止时返回无效句柄。</returns>
        UniTask<AudioHandle> PlayAsync(string address, AudioChannel channel, AudioPlayOptions options, CancellationToken ct = default);

        /// <summary>
        /// 判断句柄对应的音频是否仍活动，暂停时仍返回 true。
        /// </summary>
        /// <param name="handle">播放句柄。</param>
        /// <returns>句柄对应音频仍有效时返回 true。</returns>
        bool IsPlaying(AudioHandle handle);

        /// <summary>
        /// 获取通道中加载或播放中的音频数量。
        /// </summary>
        /// <param name="channel">目标通道。</param>
        /// <returns>当前活动音频数量。</returns>
        int GetActiveCount(AudioChannel channel);

        /// <summary>
        /// 获取通道并发声源上限。
        /// </summary>
        /// <param name="channel">目标通道。</param>
        /// <returns>当前声源上限。</returns>
        int GetVoiceLimit(AudioChannel channel);

        /// <summary>
        /// 设置通道并发声源上限；调低时立即终止最早的超限声音。
        /// </summary>
        /// <param name="channel">目标通道。</param>
        /// <param name="maxVoices">上限，必须大于零。</param>
        /// <param name="policy">满额时处理新播放请求的策略。</param>
        /// <returns>通道和上限有效且设置成功时返回 true。</returns>
        bool SetVoiceLimit(AudioChannel channel, int maxVoices, AudioVoiceOverflowPolicy policy = AudioVoiceOverflowPolicy.StealOldest);

        /// <summary>
        /// 获取通道默认的混音器输出组。
        /// </summary>
        /// <param name="channel">目标通道。</param>
        /// <returns>通道输出组；未设置时返回 null。</returns>
        AudioMixerGroup GetChannelMixerGroup(AudioChannel channel);

        /// <summary>
        /// 设置通道默认的混音器输出组。
        /// </summary>
        /// <param name="channel">目标通道。</param>
        /// <param name="group">输出组；null 表示不指定。</param>
        void SetChannelMixerGroup(AudioChannel channel, AudioMixerGroup group);

        /// <summary>
        /// 将指定音频渐变到目标音量，可在渐变完成后停止。
        /// </summary>
        /// <param name="handle">播放句柄。</param>
        /// <param name="volume">目标音量。</param>
        /// <param name="duration">渐变秒数。</param>
        /// <param name="stopWhenComplete">完成渐变后是否停止音频。</param>
        /// <returns>句柄有效且已安排渐变时返回 true。</returns>
        bool FadeTo(AudioHandle handle, float volume, float duration, bool stopWhenComplete = false);

        /// <summary>
        /// 停止指定句柄并释放音频资源；失效句柄静默忽略。
        /// </summary>
        /// <param name="handle">播放句柄。</param>
        void Stop(AudioHandle handle);

        /// <summary>
        /// 渐出或立即停止指定句柄；失效句柄静默忽略。
        /// </summary>
        /// <param name="handle">播放句柄。</param>
        /// <param name="fadeOutSeconds">渐出秒数；非正数表示立即停止。</param>
        void Stop(AudioHandle handle, float fadeOutSeconds);

        /// <summary>
        /// 停止指定通道内的所有音频。
        /// </summary>
        /// <param name="channel">目标通道。</param>
        void StopChannel(AudioChannel channel);

        /// <summary>
        /// 停止所有通道的音频。
        /// </summary>
        void StopAll();

        /// <summary>
        /// 暂停所有正在播放的音频。
        /// </summary>
        void PauseAll();

        /// <summary>
        /// 恢复业务暂停的音频。
        /// </summary>
        void ResumeAll();

        /// <summary>
        /// 业务是否主动暂停全部音频。
        /// </summary>
        bool IsPaused { get; }

        /// <summary>
        /// 应用是否因切后台暂停全部音频。
        /// </summary>
        bool IsApplicationPaused { get; }

        /// <summary>
        /// 设置应用后台暂停状态，不改变业务设置的暂停状态。
        /// </summary>
        /// <param name="paused">应用是否进入后台。</param>
        void SetApplicationPaused(bool paused);
    }
}
