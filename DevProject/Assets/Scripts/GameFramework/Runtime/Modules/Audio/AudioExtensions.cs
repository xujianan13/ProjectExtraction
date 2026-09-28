using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace GameFramework
{
    /// <summary>
    /// 为音频模块提供位置、跟随和生命周期作用域播放入口。
    /// </summary>
    public static class AudioExtensions
    {
        /// <summary>
        /// 在固定世界坐标播放三维音频。
        /// </summary>
        /// <param name="audio">音频模块。</param>
        /// <param name="address">音频资源地址。</param>
        /// <param name="position">播放位置的世界坐标。</param>
        /// <param name="channel">播放通道。</param>
        /// <param name="options">其他播放配置；不会修改传入实例。</param>
        /// <param name="ct">调用方取消令牌。</param>
        /// <returns>播放句柄；播放失败时返回无效句柄。</returns>
        public static UniTask<AudioHandle> PlayAtAsync(this IAudioModule audio, string address, Vector3 position,
            AudioChannel channel = AudioChannel.Sfx, AudioPlayOptions options = null, CancellationToken ct = default)
        {
            if (audio == null) throw new ArgumentNullException(nameof(audio));
            AudioPlayOptions copy = options?.Clone() ?? new AudioPlayOptions();
            copy.SpatialBlend = 1f;
            copy.FollowTarget = null;
            copy.Position = position;
            return audio.PlayAsync(address, channel, copy, ct);
        }

        /// <summary>
        /// 跟随目标播放三维音频；目标无效时返回无效句柄。
        /// </summary>
        /// <param name="audio">音频模块。</param>
        /// <param name="address">音频资源地址。</param>
        /// <param name="target">跟随目标。</param>
        /// <param name="channel">播放通道。</param>
        /// <param name="options">其他播放配置；不会修改传入实例。</param>
        /// <param name="ct">调用方取消令牌。</param>
        /// <returns>播放句柄；目标无效或播放失败时返回无效句柄。</returns>
        public static UniTask<AudioHandle> PlayFollowAsync(this IAudioModule audio, string address, Transform target,
            AudioChannel channel = AudioChannel.Sfx, AudioPlayOptions options = null, CancellationToken ct = default)
        {
            if (audio == null) throw new ArgumentNullException(nameof(audio));
            ct.ThrowIfCancellationRequested();
            if (target == null) return UniTask.FromResult(default(AudioHandle));
            AudioPlayOptions copy = options?.Clone() ?? new AudioPlayOptions();
            copy.SpatialBlend = 1f;
            copy.FollowTarget = target;
            return audio.PlayAsync(address, channel, copy, ct);
        }

        /// <summary>
        /// 将音频播放交由生命周期作用域持有。
        /// </summary>
        /// <param name="scope">持有本次播放的作用域。</param>
        /// <param name="audio">音频模块。</param>
        /// <param name="address">音频资源地址。</param>
        /// <param name="channel">播放通道。</param>
        /// <param name="options">其他播放配置；不会修改传入实例。</param>
        /// <param name="ct">调用方取消令牌。</param>
        /// <returns>播放句柄；作用域失活或播放失败时返回无效句柄。</returns>
        public static UniTask<AudioHandle> PlayAudioAsync(this LifetimeScope scope, IAudioModule audio, string address,
            AudioChannel channel, AudioPlayOptions options = null, CancellationToken ct = default)
        {
            if (scope == null) throw new ArgumentNullException(nameof(scope));
            if (audio == null) throw new ArgumentNullException(nameof(audio));
            AudioPlayOptions copy = options?.Clone() ?? new AudioPlayOptions();
            copy.Owner = scope;
            return audio.PlayAsync(address, channel, copy, ct);
        }
    }
}
