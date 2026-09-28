using System;
using UnityEngine;
using UnityEngine.Audio;

namespace GameFramework
{
    /// <summary>
    /// 单次音频播放配置；音频模块在调用时复制配置。
    /// </summary>
    [Serializable]
    public sealed class AudioPlayOptions
    {
        /// <summary>
        /// 是否循环播放。
        /// </summary>
        public bool Loop = false;

        /// <summary>
        /// 相对主音量和通道音量的音量，默认值为 1。
        /// </summary>
        public float Volume = 1f;

        /// <summary>
        /// 播放速度和音高，默认值为 1。
        /// </summary>
        public float Pitch = 1f;

        /// <summary>
        /// 空间混合比例，0 为二维，1 为三维。
        /// </summary>
        public float SpatialBlend = 0f;

        /// <summary>
        /// 不跟随目标时的世界坐标。
        /// </summary>
        public Vector3 Position;

        /// <summary>
        /// 跟随的目标变换组件。
        /// </summary>
        public Transform FollowTarget;

        /// <summary>
        /// 跟随目标本地坐标系中的偏移，使用 TransformPoint 转换为世界坐标。
        /// </summary>
        public Vector3 FollowOffset;

        /// <summary>
        /// 目标销毁时是否停止播放。
        /// </summary>
        public bool StopWhenTargetDestroyed = true;

        /// <summary>
        /// 三维音频最小距离。
        /// </summary>
        public float MinDistance = 1f;

        /// <summary>
        /// 三维音频最大距离。
        /// </summary>
        public float MaxDistance = 50f;

        /// <summary>
        /// 距离衰减模式。
        /// </summary>
        public AudioRolloffMode RolloffMode = AudioRolloffMode.Logarithmic;

        /// <summary>
        /// 自定义距离衰减曲线；仅在自定义衰减模式下使用。
        /// </summary>
        public AnimationCurve CustomRolloff;

        /// <summary>
        /// 多普勒效果强度。
        /// </summary>
        public float DopplerLevel = 1f;

        /// <summary>
        /// 三维音频扩散角度。
        /// </summary>
        public float Spread = 0f;

        /// <summary>
        /// 声源优先级，数值越小优先级越高。
        /// </summary>
        public int Priority = 128;

        /// <summary>
        /// 播放开始时的淡入秒数。
        /// </summary>
        public float FadeInSeconds = 0f;

        /// <summary>
        /// 本次播放的输出组；为 null 时使用通道默认输出组。
        /// </summary>
        public AudioMixerGroup OutputGroup;

        /// <summary>
        /// 持有本次播放的生命周期作用域；不参与 Unity 序列化。
        /// </summary>
        public LifetimeScope Owner { get; set; }

        /// <summary>
        /// 复制配置和自定义衰减曲线，供单次播放独立使用。
        /// </summary>
        /// <returns>与当前配置内容相同的独立配置。</returns>
        internal AudioPlayOptions Clone()
        {
            AnimationCurve curve = null;
            if (CustomRolloff != null)
            {
                curve = new AnimationCurve(CustomRolloff.keys)
                {
                    preWrapMode = CustomRolloff.preWrapMode,
                    postWrapMode = CustomRolloff.postWrapMode,
                };
            }

            return new AudioPlayOptions
            {
                Loop = Loop, Volume = Volume, Pitch = Pitch,
                SpatialBlend = SpatialBlend, Position = Position,
                FollowTarget = FollowTarget, FollowOffset = FollowOffset, StopWhenTargetDestroyed = StopWhenTargetDestroyed,
                MinDistance = MinDistance, MaxDistance = MaxDistance, RolloffMode = RolloffMode, CustomRolloff = curve,
                DopplerLevel = DopplerLevel, Spread = Spread, Priority = Priority,
                FadeInSeconds = FadeInSeconds, OutputGroup = OutputGroup, Owner = Owner,
            };
        }
    }
}
