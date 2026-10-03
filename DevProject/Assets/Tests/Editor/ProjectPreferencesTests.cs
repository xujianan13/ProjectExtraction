using GameLogic.Project;
using NUnit.Framework;
using UnityEngine;

namespace ProjectExtraction.Tests
{
    /// <summary>
    /// 验证偏好复制、旧档默认与输入边界。
    /// </summary>
    public sealed class ProjectPreferencesTests
    {
        /// <summary>
        /// 保存候选和已确认快照不得共享偏好对象。
        /// </summary>
        [Test]
        public void CopyKeepsPreferenceSnapshotIndependent()
        {
            ProjectSaveData data = ProjectSaveData.CreateDefault();
            data.Preferences.MusicVolume = 0.4f;
            ProjectSaveData copy = data.Copy();
            copy.Preferences.MusicVolume = 0.8f;
            Assert.That(data.Preferences.MusicVolume, Is.EqualTo(0.4f));
            Assert.That(copy.Preferences.MusicVolume, Is.EqualTo(0.8f));
            Assert.That(ProjectSaveData.Validate(copy), Is.Null);
        }

        /// <summary>
        /// 缺少偏好字段的第三版旧档仍使用默认音量与默认画质。
        /// </summary>
        [Test]
        public void LegacyJsonWithoutPreferencesReceivesDefaults()
        {
            var codec = new ProjectSaveCodec();
            ProjectSaveData data = ProjectSaveData.CreateDefault();
            string json = codec.Encode(data);
            string field = ",\"Preferences\":" + JsonUtility.ToJson(data.Preferences);
            Assert.That(json, Does.Contain(field));
            ProjectSaveData decoded = codec.Decode(json.Replace(field, string.Empty));
            Assert.That(decoded.Preferences.Version, Is.EqualTo(1));
            Assert.That(decoded.Preferences.MusicVolume, Is.EqualTo(1f));
            Assert.That(decoded.Preferences.SoundVolume, Is.EqualTo(1f));
            Assert.That(decoded.Preferences.QualityLevel, Is.EqualTo(-1));
            Assert.That(ProjectSaveData.Validate(decoded), Is.Null);

            ProjectSaveData shell = codec.Decode(json.Replace(JsonUtility.ToJson(data.Preferences), "{\"Version\":0}"));
            Assert.That(shell.Preferences.Version, Is.EqualTo(1));
            Assert.That(shell.Preferences.MusicVolume, Is.EqualTo(1f));
            Assert.That(shell.Preferences.SoundVolume, Is.EqualTo(1f));
        }

        /// <summary>
        /// 非有限音量、越界值与未知版本不得写入存档。
        /// </summary>
        [Test]
        public void InvalidPreferenceValuesAreRejected()
        {
            Assert.That(ProjectPreferences.Validate(null), Is.Null);
            var value = new ProjectPreferences { MusicVolume = float.NaN };
            Assert.That(ProjectPreferences.Validate(value), Is.Not.Null);
            value.MusicVolume = 1f;
            value.SoundVolume = 1.1f;
            Assert.That(ProjectPreferences.Validate(value), Is.Not.Null);
            value.SoundVolume = 1f;
            value.Version = 0;
            Assert.That(ProjectPreferences.Validate(value), Is.Not.Null);
        }
    }
}
