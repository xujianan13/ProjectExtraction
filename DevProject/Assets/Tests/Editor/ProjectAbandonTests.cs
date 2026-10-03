using System.Collections.Generic;
using GameLogic.Project;
using NUnit.Framework;
using UnityEngine;

namespace ProjectExtraction.Tests
{
    /// <summary>
    /// 验证放弃标记的持久化、死亡损失规则与旧档兼容。
    /// </summary>
    public sealed class ProjectAbandonTests
    {
        /// <summary>
        /// 放弃可在记录后重启恢复，只带回安全箱且不重复结算或退体力。
        /// </summary>
        [Test]
        public void RecordedAbandonSurvivesReloadAndSettlesExactlyOnce()
        {
            ProjectSaveData data = ProjectSaveData.CreateDefault();
            int stamina = data.Stamina;
            ProjectItem entry = data.Warehouse[0].Copy();
            entry.Locked = true;
            data.Warehouse[0].Locked = true;
            Assert.That(ProjectRunRecordRules.TryAdmit(data, "abandon-1", 1, 42, new[] { entry }, out string error), Is.True, error);
            Assert.That(ProjectRunRecordRules.TryMarkRunning(data, "abandon-1", out error), Is.True, error);
            ProjectItem safe = entry.Copy();
            safe.Id = "safe-loot";
            safe.Equipped = false;
            var outcome = new ProjectRunOutcome
            {
                RunId = "abandon-1", LevelId = 1, Seed = 42, RulesVersion = ProjectRunRecordRules.RulesVersion,
                Abandoned = true, Items = new List<ProjectItem> { entry, safe }, SafeItemIds = new List<string> { safe.Id }
            };
            Assert.That(ProjectRunRecordRules.TryRecordOutcome(data, outcome, out error), Is.True, error);
            var codec = new ProjectSaveCodec();
            ProjectSaveData restored = codec.Decode(codec.Encode(data));
            Assert.That(ProjectSaveData.Validate(restored), Is.Null);
            Assert.That(restored.Outcome.Abandoned, Is.True);
            Assert.That(ProjectRunRecordRules.TrySettleOutcome(restored, out error), Is.True, error);
            Assert.That(restored.Stamina, Is.EqualTo(stamina - PrototypeRules.EntryCost));
            Assert.That(restored.Experience, Is.EqualTo(0));
            Assert.That(restored.LastResult.RetainedItems.Count, Is.EqualTo(1));
            Assert.That(restored.LastResult.RetainedItems[0].Id, Is.EqualTo(safe.Id));
            Assert.That(restored.LastResult.LostItems[0].Id, Is.EqualTo(entry.Id), "锁定不能防止放弃损失。");
            Assert.That(ProjectSaveData.Validate(restored), Is.Null);
            string settled = codec.Encode(restored);
            Assert.That(ProjectRunRecordRules.TrySettleOutcome(restored, out error), Is.True, error);
            Assert.That(codec.Encode(restored), Is.EqualTo(settled));
            Assert.That(ProjectProfileRules.TryRecoverInterruptedRun(restored, "错误回退"), Is.False);
        }

        /// <summary>
        /// 终局不能同时表示撤离与放弃，拒绝后保持原运行阶段。
        /// </summary>
        [Test]
        public void ExtractedAbandonIsRejectedWithoutMutatingRun()
        {
            ProjectSaveData data = ProjectSaveData.CreateDefault();
            Assert.That(ProjectRunRecordRules.TryAdmit(data, "invalid", 1, 1, new ProjectItem[0], out string error), Is.True, error);
            Assert.That(ProjectRunRecordRules.TryMarkRunning(data, "invalid", out error), Is.True, error);
            var outcome = new ProjectRunOutcome
            {
                RunId = "invalid", LevelId = 1, Seed = 1, RulesVersion = ProjectRunRecordRules.RulesVersion,
                Extracted = true, Abandoned = true
            };
            Assert.That(ProjectRunRecordRules.TryRecordOutcome(data, outcome, out error), Is.False);
            Assert.That(data.Stage, Is.EqualTo(ProjectRunStage.Running));
            Assert.That(data.Outcome, Is.Null);
        }

        /// <summary>
        /// 第一版旧档迁移前校验也采用兼容偏好默认值。
        /// </summary>
        [Test]
        public void LegacyMigrationAcceptsMissingPreferenceAndLockFields()
        {
            ProjectSaveData data = ProjectSaveData.CreateDefault();
            string json = JsonUtility.ToJson(data).Replace(",\"Preferences\":" + JsonUtility.ToJson(data.Preferences), string.Empty)
                .Replace(",\"Locked\":false", string.Empty);
            Assert.That(ProjectRunRecordRules.ValidateLegacyJson(json), Is.Null);
            ProjectSaveData migrated = new ProjectSaveCodec().Decode(ProjectRunRecordRules.MigrateLegacyJson(json));
            Assert.That(ProjectSaveData.Validate(migrated), Is.Null);
            Assert.That(migrated.Preferences.MusicVolume, Is.EqualTo(1f));
            Assert.That(migrated.Warehouse[0].Locked, Is.False);
        }
    }
}
