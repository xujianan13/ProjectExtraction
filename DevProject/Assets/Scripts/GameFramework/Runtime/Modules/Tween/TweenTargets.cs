using System;
using UnityEngine;
using UnityEngine.UI;

namespace GameFramework
{
    /// <summary>
    /// 常用 Unity 属性的补间片段工厂。
    /// </summary>
    public static class TweenTargets
    {
        /// <summary>
        /// 插值世界位置。
        /// </summary>
        /// <param name="target">目标变换。</param><param name="to">目标位置。</param><param name="duration">持续秒数。</param>
        /// <param name="ease">缓动类型。</param>
        /// <returns>位置片段。</returns>
        public static TweenClip MoveTo(this Transform target, Vector3 to, float duration, TweenEase ease = TweenEase.Linear)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            return Tween.To(target.position, to, duration, value => target.position = value, ease, target);
        }

        /// <summary>
        /// 插值局部位置。
        /// </summary>
        /// <param name="target">目标变换。</param><param name="to">目标位置。</param><param name="duration">持续秒数。</param>
        /// <param name="ease">缓动类型。</param>
        /// <returns>位置片段。</returns>
        public static TweenClip MoveLocalTo(this Transform target, Vector3 to, float duration, TweenEase ease = TweenEase.Linear)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            return Tween.To(target.localPosition, to, duration, value => target.localPosition = value, ease, target);
        }

        /// <summary>
        /// 插值局部缩放。
        /// </summary>
        /// <param name="target">目标变换。</param><param name="to">目标缩放。</param><param name="duration">持续秒数。</param>
        /// <param name="ease">缓动类型。</param>
        /// <returns>缩放片段。</returns>
        public static TweenClip ScaleTo(this Transform target, Vector3 to, float duration, TweenEase ease = TweenEase.Linear)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            return Tween.To(target.localScale, to, duration, value => target.localScale = value, ease, target);
        }

        /// <summary>
        /// 插值局部旋转。
        /// </summary>
        /// <param name="target">目标变换。</param><param name="to">目标旋转。</param><param name="duration">持续秒数。</param>
        /// <param name="ease">缓动类型。</param>
        /// <returns>旋转片段。</returns>
        public static TweenClip RotateLocalTo(this Transform target, Quaternion to, float duration, TweenEase ease = TweenEase.Linear)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            return Tween.To(target.localRotation, to, duration, value => target.localRotation = value, ease, target);
        }

        /// <summary>
        /// 插值界面锚点位置。
        /// </summary>
        /// <param name="target">目标界面变换。</param><param name="to">目标位置。</param><param name="duration">持续秒数。</param>
        /// <param name="ease">缓动类型。</param>
        /// <returns>位置片段。</returns>
        public static TweenClip AnchorTo(this RectTransform target, Vector2 to, float duration, TweenEase ease = TweenEase.Linear)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            return Tween.To(target.anchoredPosition, to, duration, value => target.anchoredPosition = value, ease, target);
        }

        /// <summary>
        /// 插值画布组透明度。
        /// </summary>
        /// <param name="target">目标画布组。</param><param name="to">目标透明度。</param><param name="duration">持续秒数。</param>
        /// <param name="ease">缓动类型。</param>
        /// <returns>透明度片段。</returns>
        public static TweenClip FadeTo(this CanvasGroup target, float to, float duration, TweenEase ease = TweenEase.Linear)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            return Tween.To(target.alpha, to, duration, value => target.alpha = value, ease, target);
        }

        /// <summary>
        /// 插值界面图形颜色。
        /// </summary>
        /// <param name="target">目标图形。</param><param name="to">目标颜色。</param><param name="duration">持续秒数。</param>
        /// <param name="ease">缓动类型。</param>
        /// <returns>颜色片段。</returns>
        public static TweenClip ColorTo(this Graphic target, Color to, float duration, TweenEase ease = TweenEase.Linear)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            return Tween.To(target.color, to, duration, value => target.color = value, ease, target);
        }

        /// <summary>
        /// 插值音频源音量。
        /// </summary>
        /// <param name="target">目标音频源。</param><param name="to">目标音量。</param><param name="duration">持续秒数。</param>
        /// <param name="ease">缓动类型。</param>
        /// <returns>音量片段。</returns>
        public static TweenClip VolumeTo(this AudioSource target, float to, float duration, TweenEase ease = TweenEase.Linear)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            return Tween.To(target.volume, to, duration, value => target.volume = value, ease, target);
        }
    }
}
