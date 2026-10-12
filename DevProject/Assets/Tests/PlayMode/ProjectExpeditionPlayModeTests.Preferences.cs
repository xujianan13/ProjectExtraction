using System;
using System.Collections;
using Cysharp.Threading.Tasks;
using GameFramework;
using GameLogic.Project;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MiniGame.Tests
{
    /// <summary>
    /// 验证偏好保存、失败重试和音频即时应用。
    /// </summary>
    public sealed partial class ProjectExpeditionPlayModeTests
    {
        /// <summary>
        /// 切换最低和最高画质并恢复偏好后，仍沿用配置目标帧率与编辑器同步策略。
        /// </summary>
        /// <returns>使用内存存档应用并还原画质偏好的逐帧测试。</returns>
        [UnityTest]
        public IEnumerator Preferences_QualityChangesPreserveConfiguredFrameRate() => UniTask.ToCoroutine(async () =>
        {
            int originalQuality = QualitySettings.GetQualityLevel();
            int originalTarget = Application.targetFrameRate;
            int originalVSync = QualitySettings.vSyncCount;
            try
            {
                await SetupAsync(new MemorySaveBackend());
                ProjectSession session = ProjectServices.Session;
                ProjectPreferences originalPreferences = session.Preferences;
                int target = ProjectContentConfig.Display.TargetFrameRate;
                Assert.That(QualitySettings.names.Length, Is.GreaterThan(1));
                foreach (int level in new[] { 0, QualitySettings.names.Length - 1 })
                {
                    ProjectPreferences draft = session.Preferences;
                    draft.QualityLevel = level;
                    ProjectOperationResult changed = await session.UpdatePreferencesAsync(draft);
                    Assert.That(changed.Success, Is.True, changed.Message);
                    Assert.That(QualitySettings.GetQualityLevel(), Is.EqualTo(level));
                    Assert.That(Application.targetFrameRate, Is.EqualTo(target));
                    if (Application.isEditor) Assert.That(QualitySettings.vSyncCount, Is.Zero);
                }
                ProjectOperationResult restored = await session.UpdatePreferencesAsync(originalPreferences);
                Assert.That(restored.Success, Is.True, restored.Message);
                Assert.That(session.Preferences.QualityLevel, Is.EqualTo(originalPreferences.QualityLevel));
                Assert.That(Application.targetFrameRate, Is.EqualTo(target));
                if (Application.isEditor) Assert.That(QualitySettings.vSyncCount, Is.Zero);
            }
            finally
            {
                QualitySettings.SetQualityLevel(originalQuality, true);
                Application.targetFrameRate = originalTarget;
                QualitySettings.vSyncCount = originalVSync;
            }
        });

        /// <summary>
        /// 写入失败时音量保持旧值，重试确认后生效并可跨会话恢复。
        /// </summary>
        /// <returns>保存和重新打开会话的逐帧测试。</returns>
        [UnityTest]
        public IEnumerator Preferences_FailedSaveAppliesOnlyAfterRetryAndReopen() => UniTask.ToCoroutine(async () =>
        {
            var backend = new FaultSaveBackend();
            await SetupRealHomeAsync(backend);
            ProjectSession session = ProjectServices.Session;
            IAudioModule audio = GameEntry.Get<IAudioModule>();
            Assert.That(session.CanEditPreferences, Is.True);
            ProjectPreferences draft = session.Preferences;
            draft.MusicVolume = 0.3f;
            draft.SoundVolume = 0.6f;
            backend.FailWrites = true;
            LogAssert.ignoreFailingMessages = true;
            ProjectOperationResult failed = await session.UpdatePreferencesAsync(draft);
            Assert.That(failed.Success, Is.False);
            Assert.That(session.HasPendingSave, Is.True);
            Assert.That(session.Preferences.MusicVolume, Is.EqualTo(1f));
            Assert.That(audio.GetChannelVolume(AudioChannel.Music), Is.EqualTo(1f));

            backend.FailWrites = false;
            ProjectOperationResult accepted = await session.RetryPendingSaveAsync();
            Assert.That(accepted.Success, Is.True);
            Assert.That(session.Preferences.MusicVolume, Is.EqualTo(0.3f));
            Assert.That(audio.GetChannelVolume(AudioChannel.Music), Is.EqualTo(0.3f));
            Assert.That(audio.GetChannelVolume(AudioChannel.Sfx), Is.EqualTo(0.6f));
            draft.MusicVolume = 0.9f;
            Assert.That(session.Preferences.MusicVolume, Is.EqualTo(0.3f));

            await ProjectServices.Coordinator.CloseAsync();
            GameEntry.StopFramework();
            await SetupRealHomeAsync(backend);
            Assert.That(ProjectServices.Session.Preferences.MusicVolume, Is.EqualTo(0.3f));
            Assert.That(GameEntry.Get<IAudioModule>().GetChannelVolume(AudioChannel.Sfx), Is.EqualTo(0.6f));
        });
    }
}
