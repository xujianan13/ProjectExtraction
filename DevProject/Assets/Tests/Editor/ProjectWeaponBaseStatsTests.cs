using System;
using System.Linq;
using GameLogic.Project;
using GameLogic.Tables;
using NUnit.Framework;

namespace ProjectExtraction.Tests
{
    /// <summary>
    /// 验证武器基础属性生成及既有模板的随机序列兼容性。
    /// </summary>
    public sealed class ProjectWeaponBaseStatsTests
    {
        /// <summary>
        /// 旧短刃保持原有数值、属性数量和后续随机状态。
        /// </summary>
        [Test]
        public void ExistingBladeKeepsSeededRollSequence()
        {
            CfgItemDefinitionsRow definition = GameTables.CfgItemDefinitions.GetById("ruin_weapon");
            Assert.That(definition, Is.Not.Null);
            Assert.That(definition.HealthBase + definition.HealthPerLevel + definition.HealthRandomExclusive, Is.Zero);
            Assert.That(definition.DefenseBase + definition.DefensePerLevel + definition.DefenseRandomMin +
                definition.DefenseRandomExclusive, Is.Zero);
            const int level = 3;
            var actualRandom = new Random(2026);
            var expectedRandom = new Random(2026);
            int expectedSell = definition.SellBase + level * definition.SellPerLevel +
                (definition.SellRandomExclusive > 0 ? expectedRandom.Next(definition.SellRandomExclusive) : 0);
            float expectedAttack = (float)(definition.AttackBase + level * definition.AttackPerLevel +
                (definition.AttackRandomExclusive > 0 ? expectedRandom.Next(definition.AttackRandomExclusive) : 0));
            float expectedRange = (float)(definition.RangeBase + expectedRandom.NextDouble() * definition.RangeRandom);

            ProjectItem item = ProjectEquipmentRules.CreateConfiguredItem(actualRandom, level, ProjectItemQuality.Common, definition.ID);

            Assert.That(item, Is.Not.Null);
            Assert.That(item.SellGold, Is.EqualTo(expectedSell));
            Assert.That(item.Damage, Is.EqualTo(expectedAttack));
            Assert.That(item.Range, Is.EqualTo(expectedRange));
            Assert.That(item.BaseAttributes.Count, Is.EqualTo(1));
            Assert.That(item.BaseAttributes[0].Attribute, Is.EqualTo(ProjectAttributeType.Attack));
            Assert.That(item.BaseAttributes[0].Value, Is.EqualTo(expectedAttack));
            Assert.That(actualRandom.Next(), Is.EqualTo(expectedRandom.Next()));
            double originalHealth = definition.HealthBase;
            try
            {
                definition.HealthBase = 50;
                Assert.That(ProjectEquipmentRules.GetItemAttributes(item).Count, Is.EqualTo(1));
                Assert.That(item.BaseAttributes[0].Value, Is.EqualTo(expectedAttack));
            }
            finally
            {
                definition.HealthBase = originalHealth;
            }
        }

        /// <summary>
        /// 守卫刀同时冻结攻击、生命和防御，并计入装备后的角色属性。
        /// </summary>
        [Test]
        public void GuardBladeGrantsConfiguredHealthAndDefense()
        {
            CfgItemDefinitionsRow definition = GameTables.CfgItemDefinitions.GetById("guard_blade");
            Assert.That(definition, Is.Not.Null);
            const int level = 2;
            var actualRandom = new Random(2317);
            var expectedRandom = new Random(2317);
            if (definition.SellRandomExclusive > 0) expectedRandom.Next(definition.SellRandomExclusive);
            float expectedAttack = (float)(definition.AttackBase + level * definition.AttackPerLevel +
                (definition.AttackRandomExclusive > 0 ? expectedRandom.Next(definition.AttackRandomExclusive) : 0));
            float expectedRange = (float)(definition.RangeBase + expectedRandom.NextDouble() * definition.RangeRandom);
            float expectedHealth = (float)(definition.HealthBase + level * definition.HealthPerLevel +
                (definition.HealthRandomExclusive > 0 ? expectedRandom.Next(definition.HealthRandomExclusive) : 0));
            float expectedDefense = (float)(definition.DefenseBase + level * definition.DefensePerLevel +
                (definition.DefenseRandomExclusive > definition.DefenseRandomMin ?
                    expectedRandom.Next(definition.DefenseRandomMin, definition.DefenseRandomExclusive) : definition.DefenseRandomMin));

            ProjectItem item = ProjectEquipmentRules.CreateConfiguredItem(actualRandom, level, ProjectItemQuality.Common, definition.ID);

            Assert.That(item, Is.Not.Null);
            Assert.That(item.BaseAttributes.Count, Is.EqualTo(3));
            Assert.That(item.BaseAttributes.Single(value => value.Attribute == ProjectAttributeType.Attack).Value, Is.EqualTo(expectedAttack));
            Assert.That(item.BaseAttributes.Single(value => value.Attribute == ProjectAttributeType.MaxHealth).Value, Is.EqualTo(expectedHealth));
            Assert.That(item.BaseAttributes.Single(value => value.Attribute == ProjectAttributeType.Defense).Value, Is.EqualTo(expectedDefense));
            Assert.That(item.Range, Is.EqualTo(expectedRange));
            Assert.That(actualRandom.Next(), Is.EqualTo(expectedRandom.Next()));
            Assert.That(ProjectEquipmentRules.ValidateItem(item), Is.Null);
            item.Equipped = true;
            ProjectCharacterStats unarmed = ProjectEquipmentRules.Calculate(null, 0, 0, 0);
            ProjectCharacterStats equipped = ProjectEquipmentRules.Calculate(new[] { item }, 0, 0, 0);
            Assert.That(equipped.MaxHealth - unarmed.MaxHealth, Is.EqualTo(expectedHealth));
            Assert.That(equipped.Defense - unarmed.Defense, Is.EqualTo(expectedDefense));
        }
    }
}