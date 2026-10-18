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
