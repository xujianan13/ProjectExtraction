using System.Collections.Generic;
using GameLogic.Project;
using NUnit.Framework;

namespace ProjectExtraction.Tests
{
    /// <summary>
    /// 验证历史入场和结算记录独立于现行平衡表。
    /// </summary>
    public sealed class ProjectRunConfigCompatibilityTests
    {
        /// <summary>
        /// 配置改变后仍按已保存扣费验证并退回活动局。
        /// </summary>
        [Test]
        public void ActiveRunKeepsSavedEntryCost()
        {
            var profile = ProjectRulesConfig.Profile;
            int original = profile.EntryCost;
            ProjectSaveData data = ProjectSaveData.CreateDefault();
            Assert.IsTrue(ProjectRunRecordRules.TryAdmit(data, "saved-cost", 1, 11,
                new List<ProjectItem> { data.Warehouse[0].Copy() }, out _));
            int paid = data.ActiveRunEntryCost;
            try
            {
                profile.EntryCost = paid + 3;
                Assert.IsNull(ProjectSaveData.Validate(data));
                Assert.IsTrue(ProjectRunRecordRules.TryAdmit(data, "saved-cost", 1, 11,
                    new List<ProjectItem> { data.EntryItems[0].Copy() }, out _));
                Assert.IsTrue(ProjectProfileRules.TryRecoverInterruptedRun(data, "测试"));
                Assert.AreEqual(ProjectRulesConfig.Profile.StaminaCap, data.Stamina);
            }
            finally
            {
                profile.EntryCost = original;
            }
        }

        /// <summary>
        /// 旧活动已扣十一点时保持原始扣费证据，重读和重复准入不改价，取消仍退回实际十一点。
        /// </summary>
        [Test]
        public void PreviouslyPaidElevenPointAdmissionKeepsItsSavedAmount()
        {
            ProjectSaveData data = ProjectSaveData.CreateDefault();
            int stamina = data.Stamina;
            Assert.That(ProjectRunRecordRules.TryAdmit(data, "previous-eleven", 1, 15,
                data.Warehouse.FindAll(item => item.Equipped), out string error), Is.True, error);
            data.ActiveRunEntryCost = 11;
            data.Stamina = stamina - 11;
            var codec = new ProjectSaveCodec();
            data = codec.Decode(codec.Encode(data));
            Assert.That(ProjectSaveData.Validate(data), Is.Null);
            Assert.That(data.ActiveRunEntryCost, Is.EqualTo(11));
            Assert.That(ProjectRunRecordRules.TryAdmit(data, "previous-eleven", 1, 15,
                data.EntryItems.ConvertAll(item => item.Copy()), out error), Is.True, error);
            Assert.That(data.ActiveRunEntryCost, Is.EqualTo(11));
            Assert.That(data.Stamina, Is.EqualTo(stamina - 11));
            Assert.That(ProjectProfileRules.TryRecoverInterruptedRun(data, "取消旧准入"), Is.True);
            Assert.That(data.Stamina, Is.EqualTo(stamina));
        }

        /// <summary>
        /// 结算回执冻结当时的技能点规则，后续调整配置不改变已到账结果。
        /// </summary>
        [Test]
        public void ReceiptKeepsSkillPointRuleAfterBalanceChange()
        {
            var profile = ProjectRulesConfig.Profile;
            int original = profile.SkillPointsPerLevel;
            ProjectSaveData data = ProjectSaveData.CreateDefault();
            Assert.IsTrue(ProjectRunRecordRules.TryAdmit(data, "saved-points", 1, 13,
                new List<ProjectItem> { data.Warehouse[0].Copy() }, out _));
            Assert.IsTrue(ProjectRunRecordRules.TryMarkRunning(data, "saved-points", out _));
            var outcome = new ProjectRunOutcome
            {
                RunId = data.ActiveRunId, LevelId = data.ActiveLevelId, Seed = data.ActiveSeed,
                RulesVersion = ProjectRunRecordRules.RulesVersion, ExitKind = ProjectRunExitKind.Completed,
                KillExperience = PrototypeRules.ExperienceRequired(data.Level),
                AwardedKillExperience = PrototypeRules.ExperienceRequired(data.Level),
                Items = new List<ProjectItem>()
            };
            Assert.IsTrue(ProjectRunRecordRules.TryRecordOutcome(data, outcome, out _));
            Assert.IsTrue(ProjectRunRecordRules.TrySettleOutcome(data, out _));
            Assert.IsTrue(data.LastResult.HasSkillPointsRuleSnapshot);
            try
            {
                profile.SkillPointsPerLevel = original + 2;
                Assert.IsNull(ProjectSaveData.Validate(data));
                data.LastResult.HasSkillPointsRuleSnapshot = false;
                data.LastResult.SkillPointsPerLevelApplied = 0;
                Assert.IsNull(ProjectSaveData.Validate(data));
            }
            finally
            {
                profile.SkillPointsPerLevel = original;
            }
        }
    }
}
