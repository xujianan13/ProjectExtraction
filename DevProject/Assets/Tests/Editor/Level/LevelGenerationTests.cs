using System;
using System.Collections.Generic;
using System.Linq;
using GameLogic.Gameplay.Level;
using NUnit.Framework;
using UnityEngine;

namespace GameLogic.Tests.Editor.Level
{
    /// <summary>
    /// 验证首版地图生成、玩家投影与单局状态持久性。
    /// </summary>
    public sealed class LevelGenerationTests
    {
        /// <summary>
        /// 多个固定种子均生成连通、双向且内容完整的地图。
        /// </summary>
        /// <param name="seed">生成种子。</param>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(17)]
        [TestCase(2026)]
        public void GeneratedGraphIsConnectedAndBidirectional(int seed)
        {
            Assert.That(LevelGenerator.TryGenerate(seed, out LevelRunState run), Is.True);
            Assert.That(run.Rooms.Count, Is.EqualTo(PrototypeLevelConfig.RoomCount));
            Assert.That(run.Rooms.Count(room => room.IsStart), Is.EqualTo(1));
            Assert.That(run.Rooms.Count(room => room.Type == LevelRoomType.Boss), Is.EqualTo(1));
            Assert.That(run.Rooms.Count(room => room.Type == LevelRoomType.Npc), Is.EqualTo(1));
            Assert.That(run.Rooms.Count(room => room.Type == LevelRoomType.Loot), Is.EqualTo(PrototypeLevelConfig.LootRoomCount));

            var reached = new HashSet<int> { run.StartRoomId };
            var pending = new Queue<int>();
            pending.Enqueue(run.StartRoomId);
            int exitCount = 0;
            foreach (LevelRoomState room in run.Rooms)
            {
                exitCount += room.Exits.Count;
                foreach (var exit in room.Exits)
                {
                    Assert.That(run.TryGetRoom(exit.Value, out LevelRoomState neighbor), Is.True);
                    Assert.That(neighbor.TryGetExit(LevelGenerator.Opposite(exit.Key), out int backId), Is.True);
                    Assert.That(backId, Is.EqualTo(room.RoomId));
                    Vector2Int delta = neighbor.GridPosition - room.GridPosition;
                    Assert.That(Mathf.Abs(delta.x) + Mathf.Abs(delta.y), Is.EqualTo(1));
                }
            }

            while (pending.Count > 0)
            {
                Assert.That(run.TryGetRoom(pending.Dequeue(), out LevelRoomState room), Is.True);
                foreach (int neighborId in room.Exits.Values)
                {
                    if (reached.Add(neighborId)) pending.Enqueue(neighborId);
                }
            }
            Assert.That(reached.Count, Is.EqualTo(run.Rooms.Count));
            int bossId = run.Rooms.Single(room => room.Type == LevelRoomType.Boss).RoomId;
            var withoutBoss = new HashSet<int> { run.StartRoomId };
            var remaining = new Queue<int>();
            remaining.Enqueue(run.StartRoomId);
            while (remaining.Count > 0)
            {
                Assert.That(run.TryGetRoom(remaining.Dequeue(), out LevelRoomState room), Is.True);
                foreach (int neighborId in room.Exits.Values)
                {
                    if (neighborId != bossId && withoutBoss.Add(neighborId)) remaining.Enqueue(neighborId);
                }
            }
            Assert.That(withoutBoss.Count, Is.EqualTo(run.Rooms.Count - 1));
            int rings = exitCount / 2 - (run.Rooms.Count - 1);
            Assert.That(rings, Is.InRange(PrototypeLevelConfig.ExtraConnectionMin, PrototypeLevelConfig.ExtraConnectionMax));
            Assert.That(run.Rooms.Any(room => room.HasExtractionCandidate && room.Type == LevelRoomType.Mob && !room.IsStart), Is.True);
        }

        /// <summary>
        /// 同一生成种子每次得到相同的地图类型、出口和撤离候选。
        /// </summary>
        [Test]
        public void SameSeedProducesSamePlan()
        {
            Assert.That(LevelGenerator.TryGenerate(77, out LevelRunState first), Is.True);
            Assert.That(LevelGenerator.TryGenerate(77, out LevelRunState second), Is.True);
            Assert.That(first.StartRoomId, Is.EqualTo(second.StartRoomId));
            foreach (LevelRoomState room in first.Rooms)
            {
                Assert.That(second.TryGetRoom(room.RoomId, out LevelRoomState counterpart), Is.True);
                Assert.That(counterpart.Type, Is.EqualTo(room.Type));
                Assert.That(counterpart.HasExtractionCandidate, Is.EqualTo(room.HasExtractionCandidate));
                Assert.That(counterpart.Exits.OrderBy(exit => exit.Key).ToArray(), Is.EqualTo(room.Exits.OrderBy(exit => exit.Key).ToArray()));
            }
        }

        /// <summary>
        /// 小地图只暴露已访问内容与邻接未知轮廓，完全不包含撤离字段。
        /// </summary>
        [Test]
        public void MinimapDoesNotExposeExtraction()
        {
            Assert.That(LevelGenerator.TryGenerate(11, out LevelRunState run), Is.True);
            LevelMinimapProjection projection = run.GetMinimapProjection();
            Assert.That(projection.Rooms.Count, Is.GreaterThan(1));
            Assert.That(projection.Rooms.Count(room => room.IsCurrent), Is.EqualTo(1));
            Assert.That(projection.Rooms.Where(room => !room.IsVisited).All(room => !room.Type.HasValue && !room.IsCleared.HasValue), Is.True);
            Assert.That(typeof(LevelMinimapRoom).GetFields().Any(field => field.Name.Contains("Extraction")), Is.False);
            Assert.That(typeof(LevelMinimapProjection).GetProperties().Any(property => property.Name.Contains("Extraction")), Is.False);
        }

        /// <summary>
        /// 未清图允许往返，回访不会清除箱子、NPC 库存与撤离条件状态。
        /// </summary>
        [Test]
        public void FreeTransitionPreservesRuntimeState()
        {
            Assert.That(LevelGenerator.TryGenerate(23, out LevelRunState run), Is.True);
            Assert.That(run.TryGetRoom(run.StartRoomId, out LevelRoomState start), Is.True);
            RoomDirection exitDirection = start.Exits.Keys.First();
            Assert.That(run.TryGetDestination(exitDirection, out int neighborId), Is.True);
            Assert.That(start.IsCleared, Is.False);
            Assert.That(run.TryCommitTransition(neighborId), Is.True);
            Assert.That(run.TryGetRoom(neighborId, out LevelRoomState neighbor), Is.True);
            Assert.That(neighbor.IsVisited, Is.True);
            Assert.That(run.TryCommitTransition(start.RoomId), Is.True);
            Assert.That(run.CurrentRoomId, Is.EqualTo(start.RoomId));
            Assert.That(start.IsCleared, Is.False);

            LevelRoomState extraction = run.Rooms.First(room => room.HasExtractionCandidate && room.ExtractionRequiresClear);
            Assert.That(extraction.IsExtractionVisible, Is.False);
            extraction.MarkEnemiesSpawned();
            extraction.MarkCleared();
            Assert.That(extraction.IsExtractionVisible, Is.True);
            Assert.That(extraction.EnemiesSpawned, Is.True);

            LevelRoomState npc = run.Rooms.Single(room => room.Type == LevelRoomType.Npc);
            Assert.That(npc.TrySetNpcStock("test-offer", 2), Is.True);
            Assert.That(npc.TryGetNpcStock("test-offer", out int stock), Is.True);
            Assert.That(stock, Is.EqualTo(2));
            LevelRoomState loot = run.Rooms.First(room => room.Type == LevelRoomType.Loot);
            loot.MarkChestOpened();
            Assert.That(loot.ChestOpened, Is.True);
        }
    }
}
