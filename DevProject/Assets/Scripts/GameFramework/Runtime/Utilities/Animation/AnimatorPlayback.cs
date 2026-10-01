using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameFramework
{
    /// <summary>
    /// 单个 Animator 层的播放快照。归一化时间在循环播放时处于零到一之间，非循环播放到达一时标记完成。
    /// </summary>
    public readonly struct AnimationPlaybackState
    {
        /// <summary>
        /// 控制器中的状态名称。
        /// </summary>
        public string StateName { get; }
        /// <summary>
        /// 当前归一化时间。
        /// </summary>
        public float NormalizedTime { get; }
        /// <summary>
        /// 本地时间播放倍率。
        /// </summary>
        public float Speed { get; }
        /// <summary>
        /// 是否循环播放。
        /// </summary>
        public bool Loop { get; }
        /// <summary>
        /// 是否由调用者显式设置采样时间。
        /// </summary>
        public bool ExternalClock { get; }
        /// <summary>
        /// 非循环播放是否已经到达末尾。
        /// </summary>
        public bool Completed { get; }

        /// <summary>
        /// 创建不可变的播放快照。
        /// </summary>
        /// <param name="stateName">控制器中的状态名称。</param>
        /// <param name="normalizedTime">当前归一化时间。</param>
        /// <param name="speed">本地时间播放倍率。</param>
        /// <param name="loop">是否循环播放。</param>
        /// <param name="externalClock">是否由调用者显式设置采样时间。</param>
        /// <param name="completed">非循环播放是否已经完成。</param>
        public AnimationPlaybackState(string stateName, float normalizedTime, float speed, bool loop, bool externalClock, bool completed)
        {
            StateName = stateName;
            NormalizedTime = normalizedTime;
            Speed = speed;
            Loop = loop;
            ExternalClock = externalClock;
            Completed = completed;
        }
    }

    /// <summary>
    /// 以显式时间驱动 Unity Animator。每层独立保存状态，不接管对象或动画资源的生命周期。
    /// </summary>
    public sealed class AnimatorPlayback : IDisposable
    {
        private sealed class Track
        {
            public string Name;
            public int Hash;
            public float Time;
            public float Speed;
            public float Length;
            public bool Loop;
            public bool ExternalClock;
        }

        private readonly Animator _animator;
        private readonly float _originalSpeed;
        private readonly AnimatorCullingMode _originalCullingMode;
        private readonly Dictionary<int, Track> _tracks = new Dictionary<int, Track>();
        private bool _paused;
        private bool _disposed;

        /// <summary>
        /// 包装一个 Animator，并在控制期间关闭 Unity 自动时间推进和剔除。
        /// </summary>
        /// <param name="animator">待控制的 Animator；空引用生成无效播放器。</param>
        public AnimatorPlayback(Animator animator)
        {
            _animator = animator;
            if (!_animator) return;
            _originalSpeed = animator.speed;
            _originalCullingMode = animator.cullingMode;
            animator.speed = 0f;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }

        /// <summary>
        /// Animator 和控制器仍可用，且播放器尚未释放。
        /// </summary>
        public bool IsValid => !_disposed && _animator && _animator.runtimeAnimatorController;

        /// <summary>
        /// 是否暂停本地时间推进；显式 TrySample 仍可定位姿态。
        /// </summary>
        public bool IsPaused => _paused;

        /// <summary>
        /// 从指定归一化时间播放控制器中的状态；失败时保留原有层状态。
        /// </summary>
        /// <param name="stateName">控制器中的状态名称，可使用完整层路径。</param>
        /// <param name="loop">是否在末尾循环。</param>
        /// <param name="speed">非负本地时间倍率。</param>
        /// <param name="layer">控制器层索引。</param>
        /// <param name="startNormalizedTime">非负起始归一化时间。</param>
        /// <returns>状态存在且参数有效时为真。</returns>
        public bool TryPlay(string stateName, bool loop = false, float speed = 1f, int layer = 0, float startNormalizedTime = 0f)
        {
            if (!TryValidate(stateName, layer) || !IsNonNegativeFinite(speed) || !IsNonNegativeFinite(startNormalizedTime)) return false;
            int hash = Animator.StringToHash(stateName);
            if (!_animator.HasState(layer, hash)) return false;
            float time = loop ? Mathf.Repeat(startNormalizedTime, 1f) : Mathf.Min(startNormalizedTime, 1f);
            _animator.speed = 1f;
            _animator.Play(hash, layer, time);
            _animator.Update(0f);
            float length = _animator.GetCurrentAnimatorStateInfo(layer).length;
            _animator.speed = 0f;
            if (!_tracks.TryGetValue(layer, out Track track))
            {
                track = new Track();
                _tracks.Add(layer, track);
            }
            track.Name = stateName;
            track.Hash = hash;
            track.Time = time;
            track.Speed = speed;
            track.Length = length > 0f && IsFinite(length) ? length : 1f;
            track.Loop = loop;
            track.ExternalClock = false;
            return true;
        }

        /// <summary>
        /// 立即定位到指定状态和时间，并切换为外部时钟；暂停不妨碍显式定位。
        /// </summary>
        /// <param name="stateName">控制器中的状态名称，可使用完整层路径。</param>
        /// <param name="normalizedTime">非负归一化时间。</param>
        /// <param name="layer">控制器层索引。</param>
        /// <returns>状态存在且参数有效时为真。</returns>
        public bool TrySample(string stateName, float normalizedTime, int layer = 0)
        {
            if (!TryValidate(stateName, layer) || !IsNonNegativeFinite(normalizedTime)) return false;
            int hash = Animator.StringToHash(stateName);
            if (!_animator.HasState(layer, hash)) return false;
            float time = Mathf.Min(normalizedTime, 1f);
            _animator.speed = 1f;
            _animator.Play(hash, layer, time);
            _animator.Update(0f);
            float length = _animator.GetCurrentAnimatorStateInfo(layer).length;
            _animator.speed = 0f;
            if (!_tracks.TryGetValue(layer, out Track track))
            {
                track = new Track();
                _tracks.Add(layer, track);
            }
            track.Name = stateName;
            track.Hash = hash;
            track.Time = time;
            track.Speed = 1f;
            track.Length = length > 0f && IsFinite(length) ? length : 1f;
            track.Loop = false;
            track.ExternalClock = true;
            return true;
        }

        /// <summary>
        /// 按秒推进所有本地时钟层；外部时钟层和暂停状态保持当前姿态。
        /// </summary>
        /// <param name="deltaTime">非负且有限的秒数。</param>
        public void Advance(float deltaTime)
        {
            if (!IsValid || _paused || !IsNonNegativeFinite(deltaTime)) return;
            bool changed = false;
            foreach (KeyValuePair<int, Track> entry in _tracks)
            {
                Track track = entry.Value;
                if (track.ExternalClock || track.Speed == 0f || (!track.Loop && track.Time >= 1f)) continue;
                double next = track.Time + (double)deltaTime * track.Speed / track.Length;
                track.Time = track.Loop ? (float)(next % 1d) : (float)Math.Min(next, 1d);
                _animator.Play(track.Hash, entry.Key, track.Time);
                changed = true;
            }
            if (changed) _animator.Update(0f);
        }

        /// <summary>
        /// 设置本地时钟暂停状态。显式 TrySample 仍可设置新姿态。
        /// </summary>
        /// <param name="paused">是否暂停本地时间推进。</param>
        public void SetPaused(bool paused)
        {
            if (IsValid) _paused = paused;
        }

        /// <summary>
        /// 调整已有层的本地时间倍率。
        /// </summary>
        /// <param name="speed">非负播放倍率。</param>
        /// <param name="layer">控制器层索引。</param>
        /// <returns>层已有播放状态且参数有效时为真。</returns>
        public bool TrySetSpeed(float speed, int layer = 0)
        {
            if (!IsValid || !IsNonNegativeFinite(speed) || layer < 0 || layer >= _animator.layerCount || !_tracks.TryGetValue(layer, out Track track)) return false;
            track.Speed = speed;
            return true;
        }

        /// <summary>
        /// 获取指定层的播放快照。
        /// </summary>
        /// <param name="layer">控制器层索引。</param>
        /// <param name="state">获取成功时的播放快照。</param>
        /// <returns>指定层已有播放状态时为真。</returns>
        public bool TryGetState(int layer, out AnimationPlaybackState state)
        {
            state = default;
            if (!IsValid || layer < 0 || layer >= _animator.layerCount || !_tracks.TryGetValue(layer, out Track track)) return false;
            state = new AnimationPlaybackState(track.Name, track.Time, track.Speed, track.Loop, track.ExternalClock, !track.Loop && track.Time >= 1f);
            return true;
        }

        /// <summary>
        /// 停止跟踪全部层并保持当前姿态。
        /// </summary>
        public void Stop()
        {
            _tracks.Clear();
        }

        /// <summary>
        /// 清除跟踪并将 Animator 重置到控制器初始姿态。
        /// </summary>
        public void Reset()
        {
            _tracks.Clear();
            _paused = false;
            if (!IsValid) return;
            _animator.Rebind();
            _animator.Update(0f);
            _animator.speed = 0f;
        }

        /// <summary>
        /// 释放播放控制并恢复 Animator 原有速度和剔除模式；不销毁 Animator 或动画资源。
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _tracks.Clear();
            if (!_animator) return;
            _animator.speed = _originalSpeed;
            _animator.cullingMode = _originalCullingMode;
        }

        /// <summary>
        /// 检查状态名及层号是否可用于当前 Animator。
        /// </summary>
        /// <param name="stateName">待检查的状态名。</param>
        /// <param name="layer">待检查的层号。</param>
        /// <returns>输入和目标均有效时为真。</returns>
        private bool TryValidate(string stateName, int layer)
        {
            return IsValid && !string.IsNullOrWhiteSpace(stateName) && layer >= 0 && layer < _animator.layerCount;
        }

        /// <summary>
        /// 检查浮点数是否有限。
        /// </summary>
        /// <param name="value">待检查的数值。</param>
        /// <returns>数值有限时为真。</returns>
        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        /// <summary>
        /// 检查浮点数是否有限且非负。
        /// </summary>
        /// <param name="value">待检查的数值。</param>
        /// <returns>数值有限且非负时为真。</returns>
        private static bool IsNonNegativeFinite(float value) => IsFinite(value) && value >= 0f;
    }
}
