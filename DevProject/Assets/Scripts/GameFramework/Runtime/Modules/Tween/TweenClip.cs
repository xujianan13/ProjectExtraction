using System;
using UnityEngine;

namespace GameFramework
{
    /// <summary>
    /// 补间缓动类型。
    /// </summary>
    public enum TweenEase { Linear, InQuad, OutQuad, InOutQuad, InCubic, OutCubic, InOutCubic, SmoothStep }

    /// <summary>
    /// 可重复播放的不可变补间片段；运行状态由播放句柄持有。
    /// </summary>
    public abstract class TweenClip
    {
        /// <summary>
        /// 单轮时长，单位为秒；无限循环返回正无穷。
        /// </summary>
        public abstract float Duration { get; }

        /// <summary>
        /// 将片段求值到指定时间。
        /// </summary>
        /// <param name="time">本轮经过的秒数。</param><param name="previousTime">前一次求值秒数，负一表示首次。</param>
        /// <param name="handle">播放句柄。</param>
        internal abstract void Evaluate(float time, float previousTime, TweenHandle handle);

        /// <summary>
        /// 判断绑定对象是否仍可写入。
        /// </summary>
        /// <returns>绑定对象是否有效。</returns>
        internal abstract bool TargetsAlive();
    }

    /// <summary>
    /// 无场景状态的补间片段工厂。
    /// </summary>
    public static class Tween
    {
        /// <summary>
        /// 创建浮点补间。
        /// </summary>
        /// <param name="from">起始值。</param><param name="to">目标值。</param><param name="duration">持续秒数。</param>
        /// <param name="setter">写入值的操作。</param><param name="ease">缓动类型。</param><param name="target">可选的 Unity 生命周期目标。</param>
        /// <returns>浮点补间片段。</returns>
        public static TweenClip To(float from, float to, float duration, Action<float> setter, TweenEase ease = TweenEase.Linear, UnityEngine.Object target = null)
            => Value(from, to, duration, setter, Mathf.LerpUnclamped, ease, target);

        /// <summary>
        /// 创建二维向量补间。
        /// </summary>
        /// <param name="from">起始值。</param><param name="to">目标值。</param><param name="duration">持续秒数。</param>
        /// <param name="setter">写入值的操作。</param><param name="ease">缓动类型。</param><param name="target">可选的 Unity 生命周期目标。</param>
        /// <returns>二维向量补间片段。</returns>
        public static TweenClip To(Vector2 from, Vector2 to, float duration, Action<Vector2> setter, TweenEase ease = TweenEase.Linear, UnityEngine.Object target = null)
            => Value(from, to, duration, setter, Vector2.LerpUnclamped, ease, target);

        /// <summary>
        /// 创建三维向量补间。
        /// </summary>
        /// <param name="from">起始值。</param><param name="to">目标值。</param><param name="duration">持续秒数。</param>
        /// <param name="setter">写入值的操作。</param><param name="ease">缓动类型。</param><param name="target">可选的 Unity 生命周期目标。</param>
        /// <returns>三维向量补间片段。</returns>
        public static TweenClip To(Vector3 from, Vector3 to, float duration, Action<Vector3> setter, TweenEase ease = TweenEase.Linear, UnityEngine.Object target = null)
            => Value(from, to, duration, setter, Vector3.LerpUnclamped, ease, target);

        /// <summary>
        /// 创建颜色补间。
        /// </summary>
        /// <param name="from">起始值。</param><param name="to">目标值。</param><param name="duration">持续秒数。</param>
        /// <param name="setter">写入值的操作。</param><param name="ease">缓动类型。</param><param name="target">可选的 Unity 生命周期目标。</param>
        /// <returns>颜色补间片段。</returns>
        public static TweenClip To(Color from, Color to, float duration, Action<Color> setter, TweenEase ease = TweenEase.Linear, UnityEngine.Object target = null)
            => Value(from, to, duration, setter, Color.LerpUnclamped, ease, target);

        /// <summary>
        /// 创建旋转补间，按最短球面路径旋转。
        /// </summary>
        /// <param name="from">起始值。</param><param name="to">目标值。</param><param name="duration">持续秒数。</param>
        /// <param name="setter">写入值的操作。</param><param name="ease">缓动类型。</param><param name="target">可选的 Unity 生命周期目标。</param>
        /// <returns>旋转补间片段。</returns>
        public static TweenClip To(Quaternion from, Quaternion to, float duration, Action<Quaternion> setter, TweenEase ease = TweenEase.Linear, UnityEngine.Object target = null)
            => Value(from, to, duration, setter, Quaternion.SlerpUnclamped, ease, target);

        /// <summary>
        /// 创建纯等待片段。
        /// </summary>
        /// <param name="duration">等待秒数。</param>
        /// <returns>等待片段。</returns>
        public static TweenClip Delay(float duration) => new DelayClip(ValidateDuration(duration));

        /// <summary>
        /// 创建依次执行的组合片段。
        /// </summary>
        /// <param name="clips">按顺序执行的片段。</param>
        /// <returns>串行组合片段。</returns>
        public static TweenClip Sequence(params TweenClip[] clips) => new CompositeClip(clips, false);

        /// <summary>
        /// 创建同时执行的组合片段。
        /// </summary>
        /// <param name="clips">同时执行的片段。</param>
        /// <returns>并行组合片段。</returns>
        public static TweenClip Parallel(params TweenClip[] clips) => new CompositeClip(clips, true);

        /// <summary>
        /// 重复片段，奇数轮可反向求值。
        /// </summary>
        /// <param name="clip">要重复的片段。</param><param name="count">总轮数；负一表示无限循环。</param>
        /// <param name="yoyo">奇数轮是否反向。</param>
        /// <returns>循环片段。</returns>
        public static TweenClip Loop(TweenClip clip, int count = -1, bool yoyo = false) => new LoopClip(clip, count, yoyo);

        /// <summary>
        /// 校验时长。
        /// </summary>
        /// <param name="duration">秒数。</param>
        /// <returns>校验后的秒数。</returns>
        private static float ValidateDuration(float duration)
        {
            if (float.IsNaN(duration) || float.IsInfinity(duration) || duration < 0f) throw new ArgumentOutOfRangeException(nameof(duration));
            return duration;
        }

        /// <summary>
        /// 创建具体类型的值片段。
        /// </summary>
        /// <typeparam name="T">插值值类型。</typeparam>
        /// <param name="from">起始值。</param><param name="to">目标值。</param><param name="duration">持续秒数。</param>
        /// <param name="setter">写入操作。</param><param name="lerp">插值操作。</param><param name="ease">缓动类型。</param>
        /// <param name="target">可选的 Unity 生命周期目标。</param>
        /// <returns>值片段。</returns>
        private static TweenClip Value<T>(T from, T to, float duration, Action<T> setter, Func<T, T, float, T> lerp, TweenEase ease, UnityEngine.Object target)
        {
            if (setter == null) throw new ArgumentNullException(nameof(setter));
            if (!Enum.IsDefined(typeof(TweenEase), ease)) throw new ArgumentOutOfRangeException(nameof(ease));
            return new ValueClip<T>(from, to, ValidateDuration(duration), setter, lerp, ease, target);
        }

        /// <summary>
        /// 求缓动后的进度。
        /// </summary>
        /// <param name="ease">缓动类型。</param><param name="progress">零到一的原始进度。</param>
        /// <returns>缓动后的进度。</returns>
        private static float Ease(TweenEase ease, float progress)
        {
            switch (ease)
            {
                case TweenEase.InQuad: return progress * progress;
                case TweenEase.OutQuad: return progress * (2f - progress);
                case TweenEase.InOutQuad: return progress < 0.5f ? 2f * progress * progress : 1f - 2f * (1f - progress) * (1f - progress);
                case TweenEase.InCubic: return progress * progress * progress;
                case TweenEase.OutCubic: return 1f - Mathf.Pow(1f - progress, 3f);
                case TweenEase.InOutCubic: return progress < 0.5f ? 4f * progress * progress * progress : 1f - 4f * Mathf.Pow(1f - progress, 3f);
                case TweenEase.SmoothStep: return progress * progress * (3f - 2f * progress);
                default: return progress;
            }
        }

        private sealed class ValueClip<T> : TweenClip
        {
            private readonly T _from;
            private readonly T _to;
            private readonly float _duration;
            private readonly Action<T> _setter;
            private readonly Func<T, T, float, T> _lerp;
            private readonly TweenEase _ease;
            private readonly UnityEngine.Object _target;
            private readonly bool _hasTarget;

            /// <summary>
            /// 保存不可变插值配置。
            /// </summary>
            /// <param name="from">起始值。</param><param name="to">目标值。</param><param name="duration">持续秒数。</param>
            /// <param name="setter">写入操作。</param><param name="lerp">插值操作。</param><param name="ease">缓动类型。</param>
            /// <param name="target">可选的 Unity 生命周期目标。</param>
            internal ValueClip(T from, T to, float duration, Action<T> setter, Func<T, T, float, T> lerp, TweenEase ease, UnityEngine.Object target)
            {
                _from = from;
                _to = to;
                _duration = duration;
                _setter = setter;
                _lerp = lerp;
                _ease = ease;
                _target = target;
                _hasTarget = !ReferenceEquals(target, null);
            }

            /// <inheritdoc />
            public override float Duration => _duration;

            /// <inheritdoc />
            internal override bool TargetsAlive() => !_hasTarget || _target != null;

            /// <inheritdoc />
            internal override void Evaluate(float time, float previousTime, TweenHandle handle)
            {
                if (!handle.CanWrite()) return;
                float progress = _duration == 0f ? 1f : Mathf.Clamp01(time / _duration);
                _setter(_lerp(_from, _to, Ease(_ease, progress)));
            }
        }

        private sealed class DelayClip : TweenClip
        {
            private readonly float _duration;

            /// <summary>
            /// 保存等待秒数。
            /// </summary>
            /// <param name="duration">等待秒数。</param>
            internal DelayClip(float duration) => _duration = duration;

            /// <inheritdoc />
            public override float Duration => _duration;

            /// <inheritdoc />
            internal override bool TargetsAlive() => true;

            /// <inheritdoc />
            internal override void Evaluate(float time, float previousTime, TweenHandle handle) { }
        }

        private sealed class CompositeClip : TweenClip
        {
            private readonly TweenClip[] _clips;
            private readonly bool _parallel;
            private readonly float _duration;

            /// <summary>
            /// 复制片段列表并计算组合时长。
            /// </summary>
            /// <param name="clips">组合片段。</param><param name="parallel">是否并行。</param>
            internal CompositeClip(TweenClip[] clips, bool parallel)
            {
                if (clips == null) throw new ArgumentNullException(nameof(clips));
                _clips = (TweenClip[])clips.Clone();
                _parallel = parallel;
                foreach (TweenClip clip in _clips)
                {
                    if (clip == null) throw new ArgumentException("片段不能为 null。", nameof(clips));
                    _duration = parallel ? Mathf.Max(_duration, clip.Duration) : _duration + clip.Duration;
                }
            }

            /// <inheritdoc />
            public override float Duration => _duration;

            /// <inheritdoc />
            internal override bool TargetsAlive()
            {
                foreach (TweenClip clip in _clips) if (!clip.TargetsAlive()) return false;
                return true;
            }

            /// <inheritdoc />
            internal override void Evaluate(float time, float previousTime, TweenHandle handle)
            {
                if (_parallel)
                {
                    foreach (TweenClip clip in _clips)
                    {
                        if (!handle.CanWrite()) return;
                        clip.Evaluate(Mathf.Min(time, clip.Duration), previousTime < 0f ? -1f : Mathf.Min(previousTime, clip.Duration), handle);
                    }
                    return;
                }

                if (previousTime > time)
                {
                    float rewindCursor = _duration;
                    for (int i = _clips.Length - 1; i >= 0; i--)
                    {
                        TweenClip clip = _clips[i];
                        rewindCursor -= clip.Duration;
                        if (!handle.CanWrite()) return;
                        if (previousTime >= rewindCursor && time < rewindCursor) clip.Evaluate(0f, previousTime - rewindCursor, handle);
                    }
                }
                float cursor = 0f;
                foreach (TweenClip clip in _clips)
                {
                    if (!handle.CanWrite()) return;
                    float local = time - cursor;
                    if (local >= 0f) clip.Evaluate(Mathf.Min(local, clip.Duration), previousTime < 0f ? -1f : previousTime - cursor, handle);
                    cursor += clip.Duration;
                }
            }
        }

        private sealed class LoopClip : TweenClip
        {
            private readonly TweenClip _clip;
            private readonly int _count;
            private readonly bool _yoyo;

            /// <summary>
            /// 保存循环配置。
            /// </summary>
            /// <param name="clip">循环片段。</param><param name="count">总轮数。</param><param name="yoyo">是否往返。</param>
            internal LoopClip(TweenClip clip, int count, bool yoyo)
            {
                _clip = clip ?? throw new ArgumentNullException(nameof(clip));
                if (count == 0 || count < -1 || float.IsInfinity(clip.Duration) || count == -1 && clip.Duration <= 0f)
                    throw new ArgumentOutOfRangeException(nameof(count));
                if (count > 0 && (double)clip.Duration * count > float.MaxValue)
                    throw new ArgumentOutOfRangeException(nameof(count));
                _count = count;
                _yoyo = yoyo;
            }

            /// <inheritdoc />
            public override float Duration => _count < 0 ? float.PositiveInfinity : _clip.Duration * _count;

            /// <inheritdoc />
            internal override bool TargetsAlive() => _clip.TargetsAlive();

            /// <inheritdoc />
            internal override void Evaluate(float time, float previousTime, TweenHandle handle)
            {
                float unit = _clip.Duration;
                if (unit == 0f) { _clip.Evaluate(0f, previousTime < 0f ? -1f : 0f, handle); return; }
                double cycle = Math.Floor((double)time / unit);
                if (_count > 0 && cycle >= _count) cycle = _count - 1;
                float local = _count > 0 && time >= Duration ? unit : (float)(time - cycle * unit);
                if (_yoyo && cycle % 2d != 0d) local = unit - local;
                float prior = cycle > 0d ? unit : -1f;
                if (previousTime >= 0f)
                {
                    double previousCycle = Math.Floor((double)previousTime / unit);
                    if (_count > 0 && previousCycle >= _count) previousCycle = _count - 1;
                    prior = _count > 0 && previousTime >= Duration ? unit : (float)(previousTime - previousCycle * unit);
                    if (_yoyo && previousCycle % 2d != 0d) prior = unit - prior;
                    if (previousCycle != cycle) prior = unit;
                }
                _clip.Evaluate(local, prior, handle);
            }
        }
    }
}
