using System.Collections.Generic;
using System.Linq;
using GameLogic.Gameplay.Level;
using GameLogic.Project;
using GameLogic.Tables;
using NUnit.Framework;
using ProjectExtraction.Tests.Shared;
using UnityEngine;

namespace GameLogic.Tests.Editor.Level
{
    /// <summary>
    /// 验证固定房间序列、单次清房及重开收益账本。
    /// </summary>
    public sealed class RegionContentTests
    {
        /// <summary>
        /// 加载正式导表配置。
        /// </summary>
        [SetUp]
        public void LoadTables() => TestGameTables.LoadAll();

        /// <summary>
        /// 各原型关卡固定六房，只有最终房生成 Boss。
        /// </summary>
        [Test]
        public void ConfiguredLevelsUseSixRoomsAndOnlyFinalBoss()
        {
            var expected = new[] { ExpeditionRoomKind.Combat, ExpeditionRoomKind.Chest, ExpeditionRoomKind.Combat,
                ExpeditionRoomKind.Npc, ExpeditionRoomKind.Combat, ExpeditionRoomKind.Boss };
            foreach (CfgLevelsRow row in GameTables.CfgLevels.GetAll())
            {
                Assert.That(LevelContentConfig.TryGetLevel(row.ID, out LevelDefinition level), Is.True);
                Assert.That(LevelContentConfig.TryCreateRoomRequests("run", 17, level, out var requests, out string error), Is.True, error);
                Assert.That(requests.Select(value => value.RoomDefinitionId), Is.EqualTo(row.RoomIDs));
                Assert.That(requests.Select(value => value.RoomKind), Is.EqualTo(expected));
                foreach (RegionGenerationRequest request in requests)
                {
                    Assert.That(LevelContentConfig.TryCreateRoomMap(request.MapId, out RegionMapDefinition map, out error), Is.True, error);
                    Assert.That(RegionContentGenerator.TryGenerate(request, map, LevelContentConfig.CreateRegionContent(request),
                        out RegionPlan plan, out error), Is.True, error);
                    Assert.That(plan.SpawnSlots.Count(value => value.Kind == RegionEnemyKind.Boss),
                        Is.EqualTo(request.RoomKind == ExpeditionRoomKind.Boss ? 1 : 0));
                    Assert.That(plan.Copy().Request.RoomDefinitionId, Is.EqualTo(request.RoomDefinitionId));
                    Assert.That(plan.Copy().Request.RoomKind, Is.EqualTo(request.RoomKind));
                    Assert.That(plan.Chests.Count, Is.EqualTo(GameTables.CfgRooms.GetById(request.RoomDefinitionId).ChestCount));
                    if (request.RoomKind != ExpeditionRoomKind.Boss) continue;
                    RegionChestPlan chest = plan.Chests.Single();
                    Assert.That(chest.Kind, Is.EqualTo(RegionChestKind.BossReward));
                    Assert.That(chest.DefinitionId, Is.EqualTo(request.BossDefinitionId));
                    Assert.That(chest.Items, Is.Empty);
                    Assert.That(chest.Position, Is.EqualTo(map.Anchors.Single(anchor => anchor.Id == chest.AnchorId).Position));
                }
            }
        }

        /// <summary>
        /// 正式地图下进上出，长边方向一致且关键锚点可达。
        /// </summary>
        [Test]
        public void ConfiguredMapsAreLongAndUpperExitIsReachable()
        {
            foreach (CfgRoomsRow room in GameTables.CfgRooms.GetAll())
            {
                Assert.That(LevelContentConfig.TryCreateRoomMap(room.MapID, out RegionMapDefinition map, out string error), Is.True, error);
                Assert.That(map.Bounds.height, Is.GreaterThan(map.Bounds.width));
                Assert.That(map.Anchors.Single(value => value.Kind == RegionAnchorKind.Entry).Position.z,
                    Is.LessThan(map.Anchors.Single(value => value.Kind == RegionAnchorKind.Exit).Position.z));
                Assert.That(new RegionMapGeometry(map).TryValidateReachability(map.Anchors.Select(value => value.Position).ToArray(),
                    (float)GameTables.CfgPlayerCombat.GetById(1).Radius, (float)room.ReachabilityGridSpacing,
                    room.ReachabilityMaxCells, out error), Is.True, error);
            }
            Assert.That(LevelContentConfig.TryCreateRoomMap(999, out _, out _), Is.False);
        }

        /// <summary>
        /// 相同种子与房型的重试保持冻结内容及身份一致。
        /// </summary>
        /// <param name="kind">房间类型。</param>
        [TestCase(ExpeditionRoomKind.Combat)]
        [TestCase(ExpeditionRoomKind.Npc)]
        [TestCase(ExpeditionRoomKind.Chest)]
        [TestCase(ExpeditionRoomKind.Boss)]
        public void FrozenCandidateRetriesPreserveContents(ExpeditionRoomKind kind)
        {
            Assert.That(JsonUtility.ToJson(Generate(kind)), Is.EqualTo(JsonUtility.ToJson(Generate(kind))));
        }

        /// <summary>
        /// 锚点不足、成员容量不足或地图版本不一致时必须失败。
        /// </summary>
        [Test]
        public void InsufficientAnchorsCapacityAndWrongVersionFail()
        {
            RegionMapDefinition map = Map();
            map.Anchors.RemoveAll(value => value.Kind == RegionAnchorKind.Monster);
            Assert.That(RegionContentGenerator.TryGenerate(Request(), map, Content(), out RegionPlan failed, out string error), Is.False);
            Assert.That(failed, Is.Null);
            Assert.That(error, Does.Contain("锚点"));
            map = Map();
            map.Anchors.Single(value => value.Kind == RegionAnchorKind.Monster).Capacity = 1;
            Assert.That(RegionContentGenerator.TryGenerate(Request(), map, Content(), out _, out _), Is.False);
            RegionGenerationRequest request = Request();
            request.MapVersion = "missing";
            Assert.That(RegionContentGenerator.TryGenerate(request, Map(), Content(), out _, out _), Is.False);
        }

        /// <summary>
        /// 敌人被击败后不再周期出生，全部死亡与收益确认后才完成怪物房。
        /// </summary>
        [Test]
        public void DefeatedEnemiesNeverRespawnAndAllKillsCompleteRoom()
        {
            RegionRuntimeState state = RunningState();
            ConfirmKill(state, state.Slots[0]);
            Assert.That(state.TryConfirmDeathProcessed(state.Slots[0].SlotId, 1), Is.False);
            for (int tick = 0; tick < 600; tick++) state.AdvanceTick();
            Assert.That(state.Slots[0].Phase, Is.EqualTo(RegionSpawnPhase.Defeated));
            Assert.That(state.GetSpawnRequests(new List<RegionSpawnRequest>()), Is.Zero);
            Assert.That(state.ExitEnabled, Is.False);
            ConfirmKill(state, state.Slots[1]);
            Assert.That(state.ExitEnabled, Is.True);
            Assert.That(state.TryValidate(Map(), out string error), Is.True, error);
        }

        /// <summary>
        /// 未完成房间重开恢复敌人，并保留已领槽位账本避免重复奖励。
        /// </summary>
        [Test]
        public void ReplayPreservesRewardClaimedAndResetsEnemyInstances()
        {
            RegionRuntimeState state = RunningState();
            ConfirmKill(state, state.Slots[0]);
            Assert.That(state.CreateReplay(out RegionRuntimeState replay, out string error), Is.True, error);
            Assert.That(replay.Slots.Select(value => value.RewardClaimed), Is.EqualTo(new[] { true, false }));
            Assert.That(replay.Slots.All(value => value.Phase == RegionSpawnPhase.InitialSpawn && value.CurrentActorId == 0), Is.True);
            int newlyRewarded = 0;
            for (int index = 0; index < replay.Slots.Count; index++)
            {
                RegionSpawnSlotState slot = replay.Slots[index];
                Assert.That(replay.CommitInitialSpawn(slot.SlotId, 100 + index), Is.True);
                if (!slot.RewardClaimed) newlyRewarded++;
                ConfirmKill(replay, slot);
            }
            Assert.That(newlyRewarded, Is.EqualTo(1));
            Assert.That(replay.ExitEnabled, Is.True);
            Assert.That(replay.CreateReplay(out _, out _), Is.False);
            Assert.That(state.Slots[1].RewardClaimed, Is.False);
            RegionRuntimeState restored = JsonUtility.FromJson<RegionRuntimeState>(JsonUtility.ToJson(replay));
            Assert.That(restored.TryValidate(Map(), out error), Is.True, error);
            Assert.That(restored.Slots.All(value => value.RewardClaimed), Is.True);
        }

        /// <summary>
        /// 宝箱全部领取后才能离房，重复领取和副本修改不改变原归属。
        /// </summary>
        [Test]
        public void ChestRequiresAllClaimsAndCopiesPreserveOwnership()
        {
            RegionRuntimeState state = RunningState(ExpeditionRoomKind.Chest);
            RegionChestPlan chest = state.Plan.Chests[0];
            Assert.That(state.ExitEnabled, Is.False);
            Assert.That(state.TryClaimChestItem(chest.Id, chest.Items[0].Id), Is.False);
            Assert.That(state.TryOpenChest(chest.Id), Is.True);
            Assert.That(state.TryClaimChestItem(chest.Id, chest.Items[0].Id), Is.True);
            Assert.That(state.TryClaimChestItem(chest.Id, chest.Items[0].Id), Is.False);
            Assert.That(state.ExitEnabled, Is.False);
            Assert.That(state.TryClaimChestItem(chest.Id, chest.Items[1].Id), Is.True);
            Assert.That(state.ExitEnabled, Is.True);
            RegionRuntimeState copy = state.Copy();
            copy.Plan.Chests[0].Items[0].Name = "changed";
            copy.Chests[0].ClaimedItemIds.Clear();
            Assert.That(state.Plan.Chests[0].Items[0].Name, Is.Not.EqualTo("changed"));
            Assert.That(state.Chests[0].ClaimedItemIds.Count, Is.EqualTo(2));
            state.Chests[0].ClaimedItemIds.Add(chest.Items[0].Id);
            Assert.That(state.TryValidate(Map(), out _), Is.False);
        }

        /// <summary>
        /// NPC 房可以离开，商品只确认一次。
        /// </summary>
        [Test]
        public void NpcRoomCanDepartAndRejectsDuplicatePurchase()
        {
            RegionRuntimeState state = RunningState(ExpeditionRoomKind.Npc);
            Assert.That(state.Slots, Is.Empty);
            Assert.That(state.ExitEnabled, Is.True);
            RegionNpcPlan npc = state.Plan.Npcs[0];
            Assert.That(state.TrySellNpcOffer(npc.Id, npc.Offers[0].Id), Is.True);
            Assert.That(state.TrySellNpcOffer(npc.Id, npc.Offers[0].Id), Is.False);
            Assert.That(state.TryValidate(Map(), out string error), Is.True, error);
        }

        /// <summary>
        /// Boss 击败后才可开箱，开启后才可确认全领奖，恢复仍保持箱子与完成门禁。
        /// </summary>
        [Test]
        public void BossRewardGateAndInactiveRestoreRemainConsistent()
        {
            RegionRuntimeState state = RunningState(ExpeditionRoomKind.Boss);
            RegionChestPlan chest = state.Plan.Chests.Single();
            Assert.That(chest.Kind, Is.EqualTo(RegionChestKind.BossReward));
            Assert.That(chest.Items, Is.Empty);
            Assert.That(state.Copy().Plan.Chests.Single().Kind, Is.EqualTo(chest.Kind));
            Assert.That(state.TryOpenChest(chest.Id), Is.False);
            Assert.That(state.TryCompleteBossRewards(), Is.False);
            state.Chests[0].Opened = true;
            Assert.That(state.TryValidate(Map(), out _), Is.False, "未击败 Boss 的箱子不能已经开启。");
            state.Chests[0].Opened = false;
            ConfirmKill(state, state.Slots[0]);
            Assert.That(state.ExitEnabled, Is.False);
            Assert.That(state.CreateReplay(out _, out _), Is.False);
            Assert.That(state.TryCompleteBossRewards(), Is.False, "击败但未开箱不能完成领奖。");
            Assert.That(state.TryOpenChest(chest.Id), Is.True);
            Assert.That(state.TryOpenChest(chest.Id), Is.False);
            Assert.That(state.TryClaimChestItem(chest.Id, "boss-item"), Is.False, "Boss 候选物品不能经普通箱重复领取。");
            RegionRuntimeState restored = JsonUtility.FromJson<RegionRuntimeState>(JsonUtility.ToJson(state));
            Assert.That(restored.TryValidate(Map(), out string restoreError), Is.True, restoreError);
            Assert.That(restored.BossDefeated, Is.True);
            Assert.That(restored.Chests.Single().Opened, Is.True);
            Assert.That(restored.BossRewardsCompleted, Is.False);
            Assert.That(state.TryCompleteBossRewards(), Is.True);
            Assert.That(state.TryCompleteBossRewards(), Is.False);
            Assert.That(state.ExitEnabled, Is.True);
            state.Slots[0].CurrentActorId = 0;
            state.Slots[0].DeathTick = -1;
            Assert.That(state.TryValidate(Map(), out string error), Is.True, error);
            state.Chests[0].Opened = false;
            Assert.That(state.TryValidate(Map(), out _), Is.False, "全部领奖不能缺失开箱事实。");
            state.Chests[0].Opened = true;
            state.Slots[0].Phase = RegionSpawnPhase.Alive;
            Assert.That(state.TryValidate(Map(), out _), Is.False);
        }

        /// <summary>
        /// Boss 奖励箱无需普通物品池和工厂，缺少锚点或混入普通物品必须失败。
        /// </summary>
        [Test]
        public void BossChestUsesConfiguredAnchorWithoutDuplicatingRewardItems()
        {
            RegionContentDefinition content = Content(ExpeditionRoomKind.Boss);
            content.ChestCandidates.Clear();
            content.ItemFactory = null;
            Assert.That(RegionContentGenerator.TryGenerate(Request(ExpeditionRoomKind.Boss), Map(), content,
                out RegionPlan plan, out string error), Is.True, error);
            Assert.That(plan.Chests.Single().DefinitionId, Is.EqualTo(plan.Request.BossDefinitionId));
            RegionPlan invalid = plan.Copy();
            invalid.Chests[0].DefinitionId++;
            Assert.That(invalid.TryValidateStructure(out _), Is.False);
            invalid = plan.Copy();
            invalid.Chests[0].Kind = RegionChestKind.Loot;
            Assert.That(invalid.TryValidateStructure(out _), Is.False);
            invalid = plan.Copy();
            invalid.Chests[0].Items.Add(Generate(ExpeditionRoomKind.Chest).Chests[0].Items[0]);
            Assert.That(invalid.TryValidateStructure(out _), Is.False);
            RegionMapDefinition map = Map();
            map.Anchors.RemoveAll(anchor => anchor.Kind == RegionAnchorKind.Chest);
            Assert.That(RegionContentGenerator.TryGenerate(Request(ExpeditionRoomKind.Boss), map, content, out _, out _), Is.False);
        }

        /// <summary>
        /// 横跨房间的障碍使出口不可达，内容生成必须拒绝该地图。
        /// </summary>
        [Test]
        public void GeometryRejectsBlockedUpperExit()
        {
            RegionMapDefinition map = Map();
            map.Obstacles.Add(new RegionMapObstacle { Min = new Vector2(-10, 4), Max = new Vector2(10, 6) });
            var geometry = new RegionMapGeometry(map);
            Assert.That(geometry.IsBlocked(new Vector3(0, 0, 6.2f), 0.5f), Is.True);
            Assert.That(geometry.TryValidateReachability(new[] { new Vector3(0, 0, -25), new Vector3(0, 0, 25) },
                0.5f, 1f, 10000, out _), Is.False);
            Assert.That(RegionContentGenerator.TryGenerate(Request(), map, Content(), out _, out _), Is.False);
        }

        /// <summary>
        /// 生成并验证指定房型的测试计划。
        /// </summary>
        /// <param name="kind">房间类型。</param>
        /// <returns>有效冻结计划。</returns>
        private static RegionPlan Generate(ExpeditionRoomKind kind = ExpeditionRoomKind.Combat)
        {
            Assert.That(RegionContentGenerator.TryGenerate(Request(kind), Map(), Content(kind), out RegionPlan plan, out string error), Is.True, error);
            return plan;
        }

        /// <summary>
        /// 提交指定房型全部初始战斗身份。
        /// </summary>
        /// <param name="kind">房间类型。</param>
        /// <returns>已经激活的运行态。</returns>
        private static RegionRuntimeState RunningState(ExpeditionRoomKind kind = ExpeditionRoomKind.Combat)
        {
            Assert.That(RegionRuntimeState.TryCreate(Generate(kind), out RegionRuntimeState state, out string error), Is.True, error);
            int actor = 2;
            foreach (RegionSpawnSlotState slot in state.Slots) Assert.That(state.CommitInitialSpawn(slot.SlotId, actor++), Is.True);
            return state;
        }

        /// <summary>
        /// 确认当前敌人死亡与收益账本。
        /// </summary>
        /// <param name="state">目标房间。</param>
        /// <param name="slot">存活敌人槽位。</param>
        private static void ConfirmKill(RegionRuntimeState state, RegionSpawnSlotState slot)
        {
            Assert.That(state.TryRecordDeath(slot.SlotId, slot.CurrentActorId, slot.LifeSequence), Is.True);
            Assert.That(state.TryConfirmDeathProcessed(slot.SlotId, slot.LifeSequence), Is.True);
        }

        /// <summary>
        /// 创建带独立种子的测试请求。
        /// </summary>
        /// <param name="kind">房间类型。</param>
        /// <returns>稳定请求。</returns>
        private static RegionGenerationRequest Request(ExpeditionRoomKind kind = ExpeditionRoomKind.Combat) => new RegionGenerationRequest
        {
            RunId = "run", RegionId = 1, RegionIndex = 0, LevelId = 1, RoomDefinitionId = 1, RoomKind = kind, LootLevel = 1, EnemyLevel = 1,
            BossDefinitionId = kind == ExpeditionRoomKind.Boss ? 10 : 0, BossEnemyId = kind == ExpeditionRoomKind.Boss ? 20 : 0,
            BossEnemyLevel = kind == ExpeditionRoomKind.Boss ? 1 : 0, BossAnchorId = kind == ExpeditionRoomKind.Boss ? "boss" : null,
            MapId = 1, MapVersion = "test", ConfigVersion = "test", MonsterSeed = 1, EliteSeed = 2, ChestSeed = 3, NpcSeed = 4
        };

        /// <summary>
        /// 创建仅用于测试的下进上出长条几何。
        /// </summary>
        /// <returns>各房型共用的有效地图。</returns>
        private static RegionMapDefinition Map() => new RegionMapDefinition
        {
            MapId = 1, Version = "test", Bounds = new Rect(-10, -30, 20, 60), Anchors = new List<RegionMapAnchor>
            {
                Anchor("entry", RegionAnchorKind.Entry, "", -25, 1), Anchor("exit", RegionAnchorKind.Exit, "", 25, 1),
                Anchor("boss", RegionAnchorKind.Boss, "", 15, 1), Anchor("monster", RegionAnchorKind.Monster, "monster", -5, 2),
                Anchor("chest", RegionAnchorKind.Chest, "chest", 0, 1), Anchor("npc", RegionAnchorKind.Npc, "npc", 0, 1)
            }
        };

        /// <summary>
        /// 创建测试空间锚点。
        /// </summary>
        /// <param name="id">稳定身份。</param>
        /// <param name="kind">锚点用途。</param>
        /// <param name="tag">内容标签。</param>
        /// <param name="z">纵向位置。</param>
        /// <param name="capacity">成员容量。</param>
        /// <returns>独立锚点。</returns>
        private static RegionMapAnchor Anchor(string id, RegionAnchorKind kind, string tag, float z, int capacity) =>
            new RegionMapAnchor { Id = id, Kind = kind, Tag = tag, Position = new Vector3(0, 0, z), Capacity = capacity };

        /// <summary>
        /// 构建只存在于测试程序集的房型内容和物品工厂。
        /// </summary>
        /// <param name="kind">房间类型。</param>
        /// <returns>有效测试内容。</returns>
        private static RegionContentDefinition Content(ExpeditionRoomKind kind = ExpeditionRoomKind.Combat) => new RegionContentDefinition
        {
            MonsterGroupCount = kind == ExpeditionRoomKind.Combat ? 1 : 0,
            ChestCount = kind == ExpeditionRoomKind.Chest || kind == ExpeditionRoomKind.Boss ? 1 : 0,
            NpcCount = kind == ExpeditionRoomKind.Npc ? 1 : 0, MonsterAnchorTag = "monster", ChestAnchorTag = "chest", NpcAnchorTag = "npc",
            BossRadius = kind == ExpeditionRoomKind.Boss ? 1 : 0, PlayerRadius = 0.5f, InteractionRadius = 2,
            ReachabilityGridSpacing = 1, ReachabilityMaxCells = 10000, MaxSpawnSlots = 30,
            MonsterEncounters = new List<RegionEncounterCandidate>
            {
                new RegionEncounterCandidate { Id = 1, Weight = 1, MinLevel = 1, MaxLevel = 2,
                    Members = new List<RegionSpawnMemberDefinition> { new RegionSpawnMemberDefinition
                    { EnemyId = 1, Count = 2, StartOffset = new Vector3(-1, 0, 0), StepOffset = new Vector3(2, 0, 0), Radius = 0.4f } } }
            },
            ChestCandidates = new List<RegionChestCandidate> { new RegionChestCandidate { Id = 1, Weight = 1, LootPoolId = "chest", ItemCount = 2 } },
            NpcCandidates = new List<RegionNpcCandidate> { new RegionNpcCandidate { Id = 1, Weight = 1, LootPoolId = "merchant", Price = 10, Stock = 1 } },
            ItemFactory = (random, level, pool) => new ProjectItem
            { Id = "factory", Name = pool + random.Next(), Type = ProjectItemType.Item, RequiredLevel = level, SellGold = 1 }
        };
    }
}
