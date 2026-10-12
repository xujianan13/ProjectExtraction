using System;
using System.Collections.Generic;
using System.Linq;
using GameLogic.Project;
using GameLogic.Tables;
using NUnit.Framework;

namespace ProjectExtraction.Tests
{
    /// <summary>
    /// 验证正式装备配置的权重、属性冻结和商店可达性。
    /// </summary>
    public sealed class ProjectContentExpansionTests
    {
        private static readonly string[] WeaponIds = { "ruin_heavy_blade", "ruin_poleblade", "guard_blade" };
        private static readonly string[] PoolIds = { "default", "treasure", "merchant" };

        /// <summary>
        /// 基础掉落池保留武器、护甲和普通物品的权重，收藏使用独立类别。
        /// </summary>
        [Test]
        public void LootPoolsPreserveCategoryWeights()
        {
            int[][] expected = { new[] { 4, 4, 4 }, new[] { 12, 8, 20 }, new[] { 8, 12, 16 } };
            for (int index = 0; index < PoolIds.Length; index++)
            {
                var actual = new int[3];
                foreach (CfgLootPoolRow row in GameTables.CfgLootPool.GetAll().Where(row => row.PoolID == PoolIds[index]))
                {
                    CfgItemDefinitionsRow definition = GameTables.CfgItemDefinitions.GetById(row.DefinitionId);
                    Assert.That(definition, Is.Not.Null);
                    if (definition.Type == (int)ProjectItemType.Collection) continue;
                    int category = definition.Type == (int)ProjectItemType.Weapon ? 0 :
                        ProjectEquipmentRules.IsArmor((ProjectItemType)definition.Type) ? 1 : 2;
                    actual[category] += row.Weight;
                }
                CollectionAssert.AreEqual(expected[index], actual, PoolIds[index]);
            }
        }

        /// <summary>
        /// 新武器与旧短刃形成明确的攻击、射程及生存取舍，冻结属性可独立复制。
        /// </summary>
        [Test]
        public void NewWeaponsFreezeDistinctTradeoffs()
        {
            ProjectItem blade = Create("ruin_weapon", 11);
            ProjectItem heavy = Create("ruin_heavy_blade", 11);
            ProjectItem pole = Create("ruin_poleblade", 11);
            ProjectItem guard = Create("guard_blade", 11);
            Assert.That(heavy.Damage, Is.GreaterThan(blade.Damage));
            Assert.That(heavy.Range, Is.LessThan(blade.Range));
            Assert.That(pole.Damage, Is.LessThan(blade.Damage));
            Assert.That(pole.Range, Is.GreaterThan(blade.Range));
            Assert.That(guard.BaseAttributes.Any(value => value.Attribute == ProjectAttributeType.MaxHealth && value.Value > 0), Is.True);
            Assert.That(guard.BaseAttributes.Any(value => value.Attribute == ProjectAttributeType.Defense && value.Value > 0), Is.True);
            ProjectItem duplicate = Create("guard_blade", 11);
            Assert.That(duplicate.Id, Is.Not.EqualTo(guard.Id));
            Assert.That(duplicate.Damage, Is.EqualTo(guard.Damage));
            Assert.That(duplicate.Range, Is.EqualTo(guard.Range));
            CollectionAssert.AreEqual(guard.BaseAttributes.Select(value => value.Value).ToArray(),
                duplicate.BaseAttributes.Select(value => value.Value).ToArray());
            ProjectItem copy = guard.Copy();
            copy.BaseAttributes[0].Value += 100;
            Assert.That(guard.BaseAttributes[0].Value, Is.Not.EqualTo(copy.BaseAttributes[0].Value));
            foreach (string id in WeaponIds) Assert.That(ProjectEquipmentRules.ValidateItem(Create(id, 19)), Is.Null);
        }

        /// <summary>
        /// 基础物品池能按权重抽到三种武器，商店商品可按固定种子生成。
        /// </summary>
        [Test]
        public void NewWeaponsAreReachableFromPoolsAndHomeShop()
        {
            foreach (string poolId in PoolIds)
            {
                var seen = new HashSet<string>();
                var random = new System.Random(2026);
                for (int draw = 0; draw < 3000 && seen.Count < WeaponIds.Length; draw++)
                {
                    ProjectItem item = ProjectEquipmentRules.CreateLootFromPool(random, 1, poolId);
                    Assert.That(item, Is.Not.Null, poolId);
                    if (WeaponIds.Contains(item.DefinitionId)) seen.Add(item.DefinitionId);
                }
                CollectionAssert.IsSubsetOf(WeaponIds, seen, poolId);
            }
            string[] offers = { "heavy_blade", "poleblade", "guard_blade" };
            for (int index = 0; index < offers.Length; index++)
            {
                CfgHomeShopRow row = ProjectHomeShopConfig.Get(offers[index]);
                Assert.That(row, Is.Not.Null, offers[index]);
                Assert.That(row.DefinitionId, Is.EqualTo(WeaponIds[index]));
                Assert.That(row.RequiredLevel, Is.EqualTo(1));
                Assert.That(row.ItemLevel, Is.EqualTo(1));
                Assert.That(row.StockLimit, Is.LessThan(0), "常备商品应使用不限购库存。");
                Assert.That(ProjectEquipmentRules.CreateConfiguredItem(new System.Random(row.ItemSeed), row.ItemLevel,
                    (ProjectItemQuality)row.Quality, row.DefinitionId), Is.Not.Null);
            }
        }

        /// <summary>
        /// 以正式模板和指定种子创建普通武器。
        /// </summary>
        /// <param name="definitionId">武器定义编号。</param>
        /// <param name="seed">冻结属性的随机种子。</param>
        /// <returns>已经生成的独立物品。</returns>
        private static ProjectItem Create(string definitionId, int seed) =>
            ProjectEquipmentRules.CreateConfiguredItem(new System.Random(seed), 1, ProjectItemQuality.Common, definitionId);
    }
}
