using System.Collections.Generic;
using GameLogic.Project;
using NUnit.Framework;
using UnityEngine;

namespace ProjectExtraction.Tests
{
    /// <summary>
    /// 验证主动放弃的持久化、装备保护与终局互斥。
    /// </summary>
    public sealed class ProjectAbandonTests
    {
        /// <summary>
        /// 放弃可在记录后重启恢复，保护出战装备、损失关卡背包物品且不重复结算或退体力。
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
            ProjectItem loot = entry.Copy();
            loot.Id = "abandon-loot";
            loot.Equipped = false;
            var outcome = new ProjectRunOutcome
            {
                RunId = "abandon-1", LevelId = 1, Seed = 42, RulesVersion = ProjectRunRecordRules.RulesVersion,
                ExitKind = ProjectRunExitKind.Abandoned, Items = new List<ProjectItem> { loot }
            };
            Assert.That(ProjectRunRecordRules.TryRecordOutcome(data, outcome, out error), Is.True, error);
            var codec = new ProjectSaveCodec();
            ProjectSaveData restored = codec.Decode(codec.Encode(data));
            Assert.That(ProjectSaveData.Validate(restored), Is.Null);
            Assert.That(restored.Outcome.ExitKind, Is.EqualTo(ProjectRunExitKind.Abandoned));
            Assert.That(ProjectRunRecordRules.TrySettleOutcome(restored, out error), Is.True, error);
            Assert.That(restored.Stamina, Is.EqualTo(stamina - PrototypeRules.EntryCost));
            Assert.That(restored.Experience, Is.EqualTo(0));
            Assert.That(restored.LastResult.RetainedItems[0].Id, Is.EqualTo(entry.Id));
            Assert.That(restored.Warehouse.Count, Is.EqualTo(1));
            Assert.That(restored.LastResult.LostItems.Count, Is.EqualTo(1));
            Assert.That(restored.LastResult.LostItems[0].Id, Is.EqualTo(loot.Id), "放弃只损失关卡背包物品。");
            Assert.That(ProjectSaveData.Validate(restored), Is.Null);
            string settled = codec.Encode(restored);
            Assert.That(ProjectRunRecordRules.TrySettleOutcome(restored, out error), Is.True, error);
            Assert.That(codec.Encode(restored), Is.EqualTo(settled));
            Assert.That(ProjectProfileRules.TryRecoverInterruptedRun(restored, "错误回退"), Is.False);
        }

        /// <summary>
        /// 无效终局类型被拒绝，原运行阶段保持不变。
        /// </summary>
        [Test]
        public void InvalidExitKindIsRejectedWithoutMutatingRun()
        {
            ProjectSaveData data = ProjectSaveData.CreateDefault();
            Assert.That(ProjectRunRecordRules.TryAdmit(data, "invalid", 1, 1, new ProjectItem[0], out string error), Is.True, error);
            Assert.That(ProjectRunRecordRules.TryMarkRunning(data, "invalid", out error), Is.True, error);
            var outcome = new ProjectRunOutcome
            {
                RunId = "invalid", LevelId = 1, Seed = 1, RulesVersion = ProjectRunRecordRules.RulesVersion,
                ExitKind = (ProjectRunExitKind)999
            };
            Assert.That(ProjectRunRecordRules.TryRecordOutcome(data, outcome, out error), Is.False);
            Assert.That(data.Stage, Is.EqualTo(ProjectRunStage.Running));
            Assert.That(data.Outcome, Is.Null);
        }

    }
}
