using System;
using System.Collections.Generic;
using System.Linq;
using GameLogic.Gameplay.Level;
using GameLogic.Project;
using GameLogic.Tables;
using NUnit.Framework;
using UnityEngine;

namespace ProjectExtraction.Tests
{
    /// <summary>
    /// 验证第八阶段正式配置在关卡生成和物品抽选中的覆盖。
    /// </summary>
    public sealed class ProjectContentExpansionTests
    {
        private static readonly string[] WeaponIds = { "ruin_heavy_blade", "ruin_poleblade", "guard_blade" };
        private static readonly string[] PoolIds = { "default", "treasure", "merchant", "boss" };

        /// <summary>
        /// 三个关卡在固定种子集内均生成新布局和六个遭遇，且同种子内容稳定。
        /// </summary>
        [Test]
        public void ExpandedRoomsGenerateAndAreActuallySelected()
        {
            Assert.That(LevelContentConfig.Validate(out string error), Is.True, error);
            var layouts = new HashSet<int>();
            var encounters = new HashSet<int>();
            for (int levelId = 1; levelId <= 3; levelId++)
            {
                Assert.That(LevelContentConfig.TryGetLevel(levelId, out LevelDefinition level), Is.True);
                var levelLayouts = new HashSet<int>();
                var levelEncounters = new HashSet<int>();
                for (int seed = 0; seed < 100; seed++)
                {
                    Assert.That(LevelGenerator.TryGenerate(seed, level, out LevelRunState first), Is.True,
                        "关卡 " + levelId + " 种子 " + seed);
                    Assert.That(LevelGenerator.TryGenerate(seed, level, out LevelRunState second), Is.True);
                    foreach (LevelRoomState room in first.Rooms)
                    {
                        Assert.That(second.TryGetRoom(room.RoomId, out LevelRoomState counterpart), Is.True);
                        Assert.That(counterpart.Plan.TemplateID, Is.EqualTo(room.Plan.TemplateID));
                        Assert.That(counterpart.Plan.EncounterID, Is.EqualTo(room.Plan.EncounterID));
                        Assert.That(counterpart.Exits.OrderBy(exit => exit.Key).ToArray(),
                            Is.EqualTo(room.Exits.OrderBy(exit => exit.Key).ToArray()));
                        if (room.IsStart || room.Type == LevelRoomType.Boss)
                            Assert.That(room.Plan.Definition.Layout.ID, Is.Not.EqualTo(8).And.Not.EqualTo(9));
                        if (room.Type != LevelRoomType.Mob || room.IsStart) continue;
                        int layoutId = room.Plan.Definition.Layout.ID;
                        if (layoutId == 8 || layoutId == 9)
                        {
                            layouts.Add(layoutId);
                            levelLayouts.Add(layoutId);
                            encounters.Add(room.Plan.EncounterID);
                            levelEncounters.Add(room.Plan.EncounterID);
                        }
                        Assert.That(room.Plan.Spawns.All(group => levelId > 1 || group.EnemyID != 6), Is.True);
                    }
                }
                TestContext.Progress.WriteLine("{\"level\":" + levelId + ",\"seeds\":100,\"layouts\":[" + string.Join(",", levelLayouts.OrderBy(id => id)) +
                    "],\"encounters\":[" + string.Join(",", levelEncounters.OrderBy(id => id)) + "]}");
                CollectionAssert.IsSubsetOf(new[] { 8, 9 }, levelLayouts, "关卡 " + levelId + " 的布局覆盖不足。");
                CollectionAssert.IsSubsetOf(new[] { 12 + levelId - 1, 15 + levelId - 1 }, levelEncounters,
                    "关卡 " + levelId + " 的遭遇覆盖不足。");
            }
            CollectionAssert.IsSubsetOf(new[] { 12, 13, 14, 15, 16, 17 }, encounters);
            CollectionAssert.IsSubsetOf(new[] { 8, 9 }, layouts);
        }

        /// <summary>
        /// 四个掉落池新增武器后仍保留武器、护甲和普通物品的类别权重比例。
        /// </summary>
        [Test]
        public void LootPoolsPreserveCategoryWeights()
        {
            int[][] expected = { new[] { 4, 4, 4 }, new[] { 12, 8, 20 }, new[] { 8, 12, 16 }, new[] { 16, 16, 8 } };
            for (int index = 0; index < PoolIds.Length; index++)
            {
                var actual = new int[3];
                foreach (CfgLootPoolRow row in GameTables.CfgLootPool.GetAll().Where(row => row.PoolID == PoolIds[index]))
                {
                    CfgItemDefinitionsRow definition = GameTables.CfgItemDefinitions.GetById(row.DefinitionId);
                    Assert.That(definition, Is.Not.Null);
                    int category = definition.Type == (int)ProjectItemType.Weapon ? 0 :
                        ProjectEquipmentRules.IsArmor((ProjectItemType)definition.Type) ? 1 : 2;
                    actual[category] += row.Weight;
                }
                CollectionAssert.AreEqual(expected[index], actual, PoolIds[index]);
            }
        }

        /// <summary>
        /// 新遭遇的敌人构成按关卡增强，玩家基础技能保持不变。
        /// </summary>
        [Test]
        public void ExpandedEncounterCompositionMatchesProgression()
        {
            for (int levelId = 1; levelId <= 3; levelId++)
            {
                int[] encounterIds = { 11 + levelId, 14 + levelId };
                foreach (int encounterId in encounterIds)
                {
                    var spawns = GameTables.CfgSpawns.GetAll().Where(row => row.EncounterID == encounterId).ToArray();
                    Assert.That(spawns, Is.Not.Empty, "遭遇 " + encounterId);
                    Assert.That(spawns.Any(row => row.EnemyID == 5), Is.True);
                    Assert.That(spawns.Any(row => row.EnemyID == 6), Is.EqualTo(levelId > 1));
                    if (levelId == 3) Assert.That(spawns.Sum(row => row.Count), Is.EqualTo(4));
                }
            }
            Assert.That(GameTables.CfgEnemies.GetById(5).SkillID, Is.EqualTo(8));
            Assert.That(GameTables.CfgEnemies.GetById(6).SkillID, Is.EqualTo(9));
            Assert.That(GameTables.CfgPlayerCombat.GetById(1).SkillID, Is.EqualTo(1));
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
        /// 四个正式掉落池均能按权重实际抽到三种新武器，商店商品可按固定种子生成。
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
