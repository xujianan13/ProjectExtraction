using System.Collections.Generic;
using GameLogic.Gameplay.Level;
using GameLogic.Tables;
using NUnit.Framework;

namespace GameLogic.Tests.Editor.Level
{
    /// <summary>
    /// 验证关卡内容表的生成参数、敌人数值与无效内容门禁。
    /// </summary>
    public sealed class LevelContentConfigTests
    {
        /// <summary>
        /// 修改网格尺寸后，指定关卡生成对应数量且连通的房间。
        /// </summary>
        [Test]
        public void GridDimensionsDriveConnectedRoomCount()
        {
            CfgLevelsRow row = GameTables.CfgLevels.GetById(2);
            int originalWidth = row.GridWidth;
            int originalHeight = row.GridHeight;
            try
            {
                row.GridWidth = 4;
                row.GridHeight = 3;
                Assert.That(LevelContentConfig.Validate(out string error), Is.True, error);
                Assert.That(LevelContentConfig.TryGetLevel(row.ID, out LevelDefinition level), Is.True);
                Assert.That(LevelGenerator.TryGenerate(17, level, out LevelRunState run), Is.True);
                Assert.That(run.Rooms.Count, Is.EqualTo(12));
                var reached = new HashSet<int> { run.StartRoomId };
                var pending = new Queue<int>();
                pending.Enqueue(run.StartRoomId);
                while (pending.Count > 0)
                {
                    Assert.That(run.TryGetRoom(pending.Dequeue(), out LevelRoomState room), Is.True);
                    foreach (int neighborId in room.Exits.Values) if (reached.Add(neighborId)) pending.Enqueue(neighborId);
                }
                Assert.That(reached.Count, Is.EqualTo(run.Rooms.Count));
            }
            finally
            {
                row.GridWidth = originalWidth;
                row.GridHeight = originalHeight;
            }
        }

        /// <summary>
        /// 敌人攻击与击杀经验读取当前内容表数值。
        /// </summary>
        [Test]
        public void EnemyValuesFollowTableChanges()
        {
            CfgEnemiesRow row = GameTables.CfgEnemies.GetById(1);
            double originalAttack = row.Attack;
            int originalExperience = row.ExperienceBase;
            try
            {
                row.Attack = originalAttack + 5;
                row.ExperienceBase = originalExperience + 100;
                Assert.That(LevelContentConfig.TryGetEnemy(row.ID, out EnemyDefinition enemy), Is.True);
                Assert.That(enemy.Row.Attack, Is.EqualTo(originalAttack + 5));
                Assert.That(enemy.ExperienceAt(2), Is.EqualTo(originalExperience + 100 + row.ExperiencePerLevel * 2));
            }
            finally
            {
                row.Attack = originalAttack;
                row.ExperienceBase = originalExperience;
            }
        }

        /// <summary>
        /// 同房间刷怪点重叠时拒绝内容配置。
        /// </summary>
        [Test]
        public void RejectsOverlappingSpawnPositions()
        {
            CfgSpawnsRow row = GameTables.CfgSpawns.GetById(2);
            double originalStepX = row.StepX;
            try
            {
                row.StepX = 0;
                Assert.That(LevelContentConfig.Validate(out string error), Is.False);
                Assert.That(error, Does.Contain("刷怪点相互重叠"));
            }
            finally
            {
                row.StepX = originalStepX;
            }
        }

        /// <summary>
        /// 横贯房间的极薄障碍会被整段路径检测到。
        /// </summary>
        [Test]
        public void RejectsThinBarrierAcrossRoom()
        {
            CfgObstaclesRow obstacle = GameTables.CfgObstacles.GetById(3);
            double minX = obstacle.MinX;
            double maxX = obstacle.MaxX;
            double minZ = obstacle.MinZ;
            double maxZ = obstacle.MaxZ;
            try
            {
                obstacle.MinX = -11;
                obstacle.MaxX = 11;
                obstacle.MinZ = 1.01;
                obstacle.MaxZ = 1.02;
                Assert.That(LevelContentConfig.Validate(out string error), Is.False);
                Assert.That(error, Does.Contain("封路"));
            }
            finally
            {
                obstacle.MinX = minX;
                obstacle.MaxX = maxX;
                obstacle.MinZ = minZ;
                obstacle.MaxZ = maxZ;
            }
        }

        /// <summary>
        /// NPC 房间缺少自身商品掉落池时拒绝启动内容。
        /// </summary>
        [Test]
        public void RejectsMissingNpcLootPool()
        {
            CfgRoomTemplatesRow row = GameTables.CfgRoomTemplates.GetById(106);
            string original = row.LootPoolID;
            try
            {
                row.LootPoolID = "missing-pool";
                Assert.That(LevelContentConfig.Validate(out string error), Is.False);
                Assert.That(error, Does.Contain("房间模板无效"));
            }
            finally
            {
                row.LootPoolID = original;
            }
        }

        /// <summary>
        /// 关卡等级没有正权重可抽掉落项时拒绝内容配置。
        /// </summary>
        [Test]
        public void RejectsUnreachableLootLevel()
        {
            var pool = new List<CfgLootPoolRow>();
            var originalMaxLevels = new List<int>();
            foreach (CfgLootPoolRow row in GameTables.CfgLootPool.GetAll())
            {
                if (row.PoolID != "default") continue;
                pool.Add(row);
                originalMaxLevels.Add(row.MaxLevel);
            }
            Assert.That(pool.Count, Is.GreaterThan(0));
            try
            {
                foreach (CfgLootPoolRow row in pool) row.MaxLevel = 2;
                Assert.That(LevelContentConfig.Validate(out string error), Is.False);
                Assert.That(error, Does.Contain("关卡配置无效"));
            }
            finally
            {
                for (int index = 0; index < pool.Count; index++) pool[index].MaxLevel = originalMaxLevels[index];
            }
        }
    }
}
