using System.Collections.Generic;
using GameLogic.Gameplay.Combat;
using GameLogic.Gameplay.Level;
using GameLogic.Project;
using GameLogic.Tables;
using NUnit.Framework;
using ProjectExtraction.Tests.Shared;

namespace GameLogic.Tests.Editor.Level
{
    /// <summary>
    /// 验证 V3 房间请求、强度映射及跨表配置门禁。
    /// </summary>
    public sealed class LevelContentConfigTests
    {
        /// <summary>
        /// 为每项测试安装独立的真实配置表。
        /// </summary>
        [SetUp]
        public void LoadConfiguration() => TestGameTables.LoadAll();

        /// <summary>
        /// 恢复修改过的配置，避免影响其他程序集测试。
        /// </summary>
        [TearDown]
        public void RestoreConfiguration() => TestGameTables.LoadAll();

        /// <summary>
        /// 开局请求完整保存房间 Boss 绑定与独立内容种子。
        /// </summary>
        [Test]
        public void RequestsFreezeBossMapAndIndependentSeeds()
        {
            Assert.That(LevelContentConfig.TryGetLevel(2, out LevelDefinition level), Is.True);
            Assert.That(LevelContentConfig.TryCreateRoomRequests("run", 17, level, out var first, out string error), Is.True, error);
            Assert.That(LevelContentConfig.TryCreateRoomRequests("run", 17, level, out var second, out error), Is.True, error);
            Assert.That(first.Count, Is.EqualTo(level.Row.RoomIDs.Length));
            for (int index = 0; index < first.Count; index++)
            {
                RegionGenerationRequest request = first[index];
                CfgRoomsRow room = GameTables.CfgRooms.GetById(level.Row.RoomIDs[index]);
                Assert.That(request.RegionId, Is.EqualTo(index + 1));
                Assert.That(request.RegionIndex, Is.EqualTo(index));
                Assert.That(request.EnemyLevel, Is.EqualTo(level.LootLevel + room.EnemyLevelOffset));
                Assert.That(request.RoomKind, Is.EqualTo((ExpeditionRoomKind)room.Kind));
                Assert.That(request.LootLevel, Is.EqualTo(level.LootLevel));
                Assert.That(request.MapId, Is.EqualTo(room.MapID));
                Assert.That(request.MapVersion, Is.EqualTo(GameTables.CfgRoomMaps.GetById(room.MapID).Version));
                Assert.That(request.ConfigVersion, Is.EqualTo(GameTables.CfgRunPersistence.GetById(1).ConfigVersion));
                Assert.That(UnityEngine.JsonUtility.ToJson(request), Is.EqualTo(UnityEngine.JsonUtility.ToJson(second[index])));
                Assert.That(new HashSet<int> { request.MonsterSeed, request.EliteSeed, request.ChestSeed, request.NpcSeed }.Count, Is.EqualTo(4));
            }
            Assert.That(first[first.Count - 1].RoomKind, Is.EqualTo(ExpeditionRoomKind.Boss));
        }

        /// <summary>
        /// 最终房间只包含 Boss，不生成普通、精英、宝箱和商人。
        /// </summary>
        [Test]
        public void FinalRegionHasOnlyBossContent()
        {
            LevelContentConfig.TryGetLevel(1, out LevelDefinition level);
            Assert.That(LevelContentConfig.TryCreateRoomRequests("run", 1, level, out var requests, out string error), Is.True, error);
            RegionContentDefinition normal = LevelContentConfig.CreateRegionContent(requests[0]);
            RegionContentDefinition final = LevelContentConfig.CreateRegionContent(requests[requests.Count - 1]);
            Assert.That(normal.MonsterGroupCount, Is.EqualTo(1));
            Assert.That(normal.ChestCount + normal.NpcCount, Is.Zero);
            Assert.That(final.MonsterGroupCount + final.NpcCount, Is.Zero);
            Assert.That(final.ChestCount, Is.EqualTo(1));
            Assert.That(final.ChestCandidates, Is.Empty);
            Assert.That(final.BossRadius, Is.GreaterThan(0f));
        }

        /// <summary>
        /// Boss 箱数量缺失必须拒绝配置，不能在代码中补入默认箱子。
        /// </summary>
        [Test]
        public void RejectsMissingBossChestCount()
        {
            foreach (CfgRoomsRow room in GameTables.CfgRooms.GetAll())
                if (room.Kind == (int)ExpeditionRoomKind.Boss) room.ChestCount = 0;
            Assert.That(LevelContentConfig.Validate(out string error), Is.False);
            Assert.That(error, Does.Contain("宝箱数量"));
        }

        /// <summary>
        /// 敌人数值、角色和弱点直接读取新配置。
        /// </summary>
        [Test]
        public void EnemyValuesFollowTableChanges()
        {
            CfgEnemiesRow row = GameTables.CfgEnemies.GetById(1);
            row.Attack += 5;
            row.ExperienceBase += 100;
            Assert.That(LevelContentConfig.TryGetEnemy(row.ID, out EnemyDefinition enemy), Is.True);
            Assert.That(enemy.Row.Attack, Is.EqualTo(row.Attack));
            Assert.That(enemy.ExperienceAt(2), Is.EqualTo(row.ExperienceBase + row.ExperiencePerLevel * 2));
            Assert.That(enemy.HealthAt(2), Is.EqualTo((float)(row.BaseHealth + row.HealthPerLevel * 2)));
            Assert.That(enemy.Role, Is.EqualTo((RegionEnemyKind)row.Role));
            Assert.That(enemy.CreateVulnerabilities().Length, Is.EqualTo(row.VulnerableEffectIDs.Length));
        }

        /// <summary>
        /// 局次身份或配置缺失时不产生部分请求。
        /// </summary>
        [Test]
        public void RejectsInvalidRunIdentityAndMissingRoomDefinition()
        {
            LevelContentConfig.TryGetLevel(1, out LevelDefinition level);
            Assert.That(LevelContentConfig.TryCreateRoomRequests("", 1, level, out var requests, out _), Is.False);
            Assert.That(requests, Is.Null);
            level.Row.RoomIDs[0] = -1;
            Assert.That(LevelContentConfig.TryCreateRoomRequests("run", 1, level, out requests, out string error), Is.False);
            Assert.That(requests, Is.Null);
            Assert.That(error, Does.Contain("房间顺序"));
        }

        /// <summary>
        /// 相对锚点的组内出生位置重叠时拒绝内容配置。
        /// </summary>
        [Test]
        public void RejectsOverlappingSpawnPositions()
        {
            CfgSpawnsRow row = GameTables.CfgSpawns.GetAll()[0];
            row.Count = 2;
            row.StepX = 0;
            row.StepZ = 0;
            row.AlternateZ = 0;
            Assert.That(LevelContentConfig.Validate(out string error), Is.False);
            Assert.That(error, Does.Contain("刷怪点相互重叠"));
        }

        /// <summary>
        /// 商人商品池缺失时拒绝房间配置，不回退默认物品。
        /// </summary>
        [Test]
        public void RejectsMissingNpcLootPool()
        {
            GameTables.CfgNpcPool.GetAll()[0].LootPoolID = "missing-pool";
            Assert.That(LevelContentConfig.Validate(out string error), Is.False);
            Assert.That(error, Does.Contain("商人"));
        }

        /// <summary>
        /// 非伤害效果不能充当敌人伤害弱点。
        /// </summary>
        [Test]
        public void RejectsInvalidDamageVulnerability()
        {
            GameTables.CfgEnemies.GetAll()[0].VulnerableEffectIDs = new[] { -1 };
            Assert.That(LevelContentConfig.Validate(out string error), Is.False);
            Assert.That(error, Does.Contain("伤害弱点"));
        }

        /// <summary>
        /// 物品等级与关卡编号分离，房间强度按物品等级递增。
        /// </summary>
        [Test]
        public void IntensityUsesLootLevelRatherThanLevelId()
        {
            LevelContentConfig.TryGetLevel(3, out LevelDefinition level);
            Assert.That(level.ID, Is.Not.EqualTo(level.LootLevel));
            Assert.That(LevelContentConfig.TryCreateRoomRequests("run", 1, level, out var requests, out string error), Is.True, error);
            Assert.That(requests[0].EnemyLevel, Is.EqualTo(level.LootLevel));
            Assert.That(requests[requests.Count - 1].EnemyLevel, Is.EqualTo(level.LootLevel + GameTables.CfgRooms.GetById(level.Row.RoomIDs[level.Row.RoomIDs.Length - 1]).EnemyLevelOffset));
        }

        /// <summary>
        /// 无效存档预算会被项目配置总门禁拒绝。
        /// </summary>
        [Test]
        public void RejectsInvalidPersistenceBudget()
        {
            GameTables.CfgRunPersistence.GetById(1).MaxPendingTicks = 0;
            Assert.That(ProjectContentConfig.Validate(out string error), Is.False);
            Assert.That(error, Does.Contain("存档预算"));
        }
    }
}
