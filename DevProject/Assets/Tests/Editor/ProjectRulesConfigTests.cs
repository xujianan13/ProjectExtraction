using GameLogic.Project;
using NUnit.Framework;

namespace ProjectExtraction.Tests
{
    /// <summary>
    /// 局外配置跨表引用与旧实例冻结规则。
    /// </summary>
    public sealed class ProjectRulesConfigTests
    {
        /// <summary>
        /// 验证完整表通过门禁，而缺失默认掉落池被拒绝。
        /// </summary>
        [Test]
        public void RejectsMissingDefaultLootPool()
        {
            Assert.That(ProjectRulesConfig.Validate(out string initialError), Is.True, initialError);
            var profile = ProjectRulesConfig.Profile;
            string original = profile.DefaultLootPoolID;
            try
            {
                profile.DefaultLootPoolID = "missing_pool_for_test";
                Assert.That(ProjectRulesConfig.Validate(out string error), Is.False);
                Assert.That(error, Does.Contain("默认掉落池"));
            }
            finally
            {
                profile.DefaultLootPoolID = original;
            }
        }

        /// <summary>
        /// 验证已保存装备属性独立于后来变更的物品模板。
        /// </summary>
        [Test]
        public void FrozenItemStatsDoNotFollowTemplate()
        {
            ProjectItem item = ProjectEquipmentRules.CreateEquipment(new System.Random(17), ProjectItemType.Weapon, 3,
                ProjectItemQuality.Rare);
            Assert.That(item, Is.Not.Null);
            Assert.That(item.DefinitionId, Is.Not.Empty);
            float before = ProjectEquipmentRules.GetItemAttributes(item)[0].Value;
            var template = ProjectRulesConfig.Item(item.DefinitionId);
            double original = template.AttackBase;
            try
            {
                template.AttackBase += 500;
                Assert.That(ProjectEquipmentRules.GetItemAttributes(item)[0].Value, Is.EqualTo(before));
                Assert.That(ProjectEquipmentRules.ValidateItem(item), Is.Null);
            }
            finally
            {
                template.AttackBase = original;
            }
        }
        /// <summary>
        /// 无效金币价格和技能点累计溢出在加载前被拒绝。
        /// </summary>
        [Test]
        public void RejectsNegativePriceAndSkillPointOverflow()
        {
            var profile = ProjectRulesConfig.Profile;
            int originalPrice = profile.NpcItemPrice;
            int originalPoints = profile.SkillPointsPerLevel;
            try
            {
                profile.NpcItemPrice = -1;
                Assert.That(ProjectRulesConfig.Validate(out _), Is.False);
                profile.NpcItemPrice = originalPrice;
                profile.SkillPointsPerLevel = int.MaxValue;
                Assert.That(ProjectRulesConfig.Validate(out _), Is.False);
            }
            finally
            {
                profile.NpcItemPrice = originalPrice;
                profile.SkillPointsPerLevel = originalPoints;
            }
        }

        /// <summary>
        /// 词条最终数值和模板射程必须在可冻结的存档范围内。
        /// </summary>
        [Test]
        public void RejectsOversizedAffixAndNonFiniteRange()
        {
            var quality = ProjectRulesConfig.Quality(ProjectItemQuality.Rare);
            var template = ProjectRulesConfig.Item("ruin_weapon");
            double originalScale = quality.ScaleMax;
            double originalRange = template.RangeBase;
            try
            {
                quality.ScaleMax = 1000000;
                Assert.That(ProjectRulesConfig.Validate(out string affixError), Is.False);
                Assert.That(affixError, Does.Contain("词条生成值"));
                quality.ScaleMax = originalScale;
                template.RangeBase = double.PositiveInfinity;
                Assert.That(ProjectRulesConfig.Validate(out _), Is.False);
            }
            finally
            {
                quality.ScaleMax = originalScale;
                template.RangeBase = originalRange;
            }
        }
        /// <summary>
        /// 降低等级上限并调整经验曲线后，高等级旧档的门槛显示仍不能溢出。
        /// </summary>
        [Test]
        public void HistoricalLevelExperienceThresholdDoesNotOverflow()
        {
            var profile = ProjectRulesConfig.Profile;
            int maximum = profile.MaxLevel, growth = profile.ExperiencePerLevel;
            try
            {
                profile.MaxLevel = 5;
                profile.ExperiencePerLevel = 100000000;
                Assert.That(ProjectRulesConfig.Validate(out string error), Is.True, error);
                Assert.That(PrototypeRules.ExperienceRequired(1000), Is.EqualTo(int.MaxValue));
            }
            finally
            {
                profile.MaxLevel = maximum;
                profile.ExperiencePerLevel = growth;
            }
        }
    }
}
