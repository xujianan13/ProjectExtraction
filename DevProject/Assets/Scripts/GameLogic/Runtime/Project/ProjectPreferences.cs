using System;
using GameFramework;
using UnityEngine;

namespace GameLogic.Project
{
    /// <summary>
    /// 本地玩家偏好，与战斗数值配置独立保存。
    /// </summary>
    [Serializable]
    public sealed class ProjectPreferences
    {
        private static int? defaultQualityLevel;

        public int Version = 1;
        public float MusicVolume = 1f;
        public float SoundVolume = 1f;
        public int QualityLevel = -1;
        public int GuideVersion;

        /// <summary>
        /// 创建独立的偏好副本。
        /// </summary>
        /// <returns>独立偏好副本。</returns>
        public ProjectPreferences Copy() => (ProjectPreferences)MemberwiseClone();

        /// <summary>
        /// 校验存档偏好；旧存档没有此字段时视为默认偏好。
        /// </summary>
        /// <param name="value">待校验的偏好。</param>
        /// <returns>错误描述；有效时为空。</returns>
        public static string Validate(ProjectPreferences value)
        {
            if (value == null) return null;
            if (value.Version != 1) return "偏好版本无效。";
            if (float.IsNaN(value.MusicVolume) || float.IsInfinity(value.MusicVolume) || value.MusicVolume < 0f || value.MusicVolume > 1f ||
                float.IsNaN(value.SoundVolume) || float.IsInfinity(value.SoundVolume) || value.SoundVolume < 0f || value.SoundVolume > 1f)
                return "音量必须在 0 到 1 之间。";
            if (value.QualityLevel < -1 || value.GuideVersion < 0) return "偏好数值无效。";
            return null;
        }

        /// <summary>
        /// 将已经确认的偏好应用到音频通道、Unity 质量设置与项目目标帧率。
        /// </summary>
        public void Apply()
        {
            if (Validate(this) != null) return;
            IAudioModule audio = GameEntry.Get<IAudioModule>();
            audio?.SetChannelVolume(AudioChannel.Music, MusicVolume);
            audio?.SetChannelVolume(AudioChannel.Sfx, SoundVolume);
            if (!defaultQualityLevel.HasValue) defaultQualityLevel = QualitySettings.GetQualityLevel();
            int level = QualityLevel < 0 ? defaultQualityLevel.Value : QualityLevel;
            if (level >= 0 && level < QualitySettings.names.Length && QualitySettings.GetQualityLevel() != level)
                QualitySettings.SetQualityLevel(level, true);
            ApplyFrameRate();
        }

        /// <summary>
        /// 在画质切换后重新应用配置帧率，桌面仅在刷新率可整除时启用垂直同步。
        /// </summary>
        private static void ApplyFrameRate()
        {
            int target = ProjectContentConfig.Display.TargetFrameRate;
            Application.targetFrameRate = target;
            QualitySettings.vSyncCount = 0;
            if (Application.isEditor || Application.isMobilePlatform || Application.platform == RuntimePlatform.WebGLPlayer) return;
            double ratio = Screen.currentResolution.refreshRateRatio.value / target;
            int divisor = (int)Math.Round(ratio);
            if (divisor >= 1 && divisor <= 4 && Math.Abs(ratio - divisor) <= 0.01) QualitySettings.vSyncCount = divisor;
        }
    }
}
