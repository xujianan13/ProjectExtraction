using System;
using System.Collections.Generic;
using System.Linq;
using GameLogic.Gameplay.Level;
using GameLogic.Project;
using NUnit.Framework;
using UnityEngine;

namespace GameLogic.Tests.Editor.Level
{
    /// <summary>
    /// 验证区域冻结、按生命刷新及存档结构的纯逻辑契约。
    /// </summary>
    public sealed class RegionContentTests
    {
        /// <summary>
        /// 同一候选重试的冻结物品及其身份保持一致。
        /// </summary>
        [Test]
        public void FrozenCandidateRetriesPreserveItemsAndIdentities()
        {
            RegionPlan first = Generate();
            RegionPlan second = Generate();
            Assert.That(JsonUtility.ToJson(second), Is.EqualTo(JsonUtility.ToJson(first)));
            Assert.That(first.Chests[0].Items[0].Id, Does.StartWith("run:region:1:chest:"));
            Assert.That(first.Npcs[0].Offers[0].Item.Id, Is.EqualTo(first.Npcs[0].Offers[0].Id));
        }

        /// <summary>
        /// 更换兼容 Boss 和地图只重新映射空间，不改变内容池抽样。
        /// </summary>
        [Test]
        public void CompatibleBossAndMapDoNotChangeContentSamples()
        {
            RegionPlan first = Generate();
            RegionMapDefinition map = Map();
            map.MapId = 2;
            map.Anchors.Add(Anchor("monster-extra", RegionAnchorKind.Monster, "monster", new Vector3(0, 0, -12), 3));
            RegionGenerationRequest request = Request();
            request.MapId = 2;
            request.BossDefinitionId = 42;
            request.BossEnemyId = 22;
            Assert.That(RegionContentGenerator.TryGenerate(request, map, Content(), out RegionPlan changed, out string error), Is.True, error);
            Assert.That(changed.SpawnSlots.Where(value => value.Kind != RegionEnemyKind.Boss).Select(value => value.EnemyId),
                Is.EqualTo(first.SpawnSlots.Where(value => value.Kind != RegionEnemyKind.Boss).Select(value => value.EnemyId)));
            Assert.That(JsonUtility.ToJson(changed.Chests[0].Items[0]), Is.EqualTo(JsonUtility.ToJson(first.Chests[0].Items[0])));
            Assert.That(JsonUtility.ToJson(changed.Npcs[0].Offers[0]), Is.EqualTo(JsonUtility.ToJson(first.Npcs[0].Offers[0])));
            Assert.That(changed.MonsterRandomState, Is.EqualTo(first.MonsterRandomState));
            Assert.That(changed.ChestRandomState, Is.EqualTo(first.ChestRandomState));
        }

        /// <summary>
        /// 不足的锚点和不匹配的地图版本必须失败，不悄悄减少内容。
        /// </summary>
        [Test]
        public void InsufficientCapacityAndWrongVersionFailExplicitly()
        {
            RegionMapDefinition map = Map();
            map.Anchors.RemoveAll(value => value.Kind == RegionAnchorKind.Elite);
            Assert.That(RegionContentGenerator.TryGenerate(Request(), map, Content(), out RegionPlan failed, out string error), Is.False);
            Assert.That(failed, Is.Null);
            Assert.That(error, Does.Contain("锚点"));
            RegionGenerationRequest request = Request();
            request.MapVersion = "missing";
            Assert.That(RegionContentGenerator.TryGenerate(request, Map(), Content(), out _, out _), Is.False);
        }

        /// <summary>
        /// 存活组员不阻碍其他个体复活，未确认收益的生命不能被跳过。
        /// </summary>
        [Test]
        public void IndividualDeathAndRewardAcknowledgementGateRespawn()
        {
            RegionRuntimeState state = RunningState();
            RegionSpawnSlotState killed = state.Slots.First(value => state.Plan.SpawnSlots.Find(plan => plan.SlotId == value.SlotId).Kind == RegionEnemyKind.Normal);
            RegionSpawnSlotState survivor = state.Slots.First(value => value.SlotId != killed.SlotId &&
                state.Plan.SpawnSlots.Find(plan => plan.SlotId == value.SlotId).Kind == RegionEnemyKind.Normal);
            Assert.That(state.TryRecordDeath(killed.SlotId, killed.CurrentActorId, killed.LifeSequence), Is.True);
            Assert.That(state.TryRecordDeath(killed.SlotId, killed.CurrentActorId, killed.LifeSequence), Is.False);
            for (int tick = 0; tick < 6; tick++) Assert.That(state.AdvanceTick(_ => Safe()), Is.True);
            Assert.That(killed.WaitingReason, Is.EqualTo(RegionSpawnWaitReason.UnprocessedDeath));
            var requests = new List<RegionSpawnRequest>();
            Assert.That(state.GetSpawnRequests(requests), Is.Zero);
            Assert.That(state.TryConfirmDeathProcessed(killed.SlotId, 1), Is.True);
            Assert.That(state.TryConfirmDeathProcessed(killed.SlotId, 1), Is.False);
            for (int tick = 0; tick < 3; tick++) state.AdvanceTick(_ => Safe());
            Assert.That(state.GetSpawnRequests(requests), Is.EqualTo(1));
            Assert.That(requests[0].ExpectedLifeSequence, Is.EqualTo(1));
            Assert.That(state.GetSpawnRequests(requests), Is.EqualTo(1));
            Assert.That(killed.LifeSequence, Is.EqualTo(1));
            Assert.That(state.CommitRespawn(killed.SlotId, 1, 100), Is.True);
            Assert.That(state.CommitRespawn(killed.SlotId, 1, 101), Is.False);
            Assert.That(killed.LifeSequence, Is.EqualTo(2));
            Assert.That(survivor.LifeSequence, Is.EqualTo(1));
            Assert.That(survivor.Phase, Is.EqualTo(RegionSpawnPhase.Alive));
            Assert.That(state.TryValidate(Map(), out string error), Is.True, error);
        }

        /// <summary>
        /// 出生预警安全失效后重新等待，序列化恢复不会补跑离线时间。
        /// </summary>
        [Test]
        public void WarningRestartsAndRestorePreservesRemainingTicks()
        {
            RegionRuntimeState state = RunningState();
            RegionSpawnSlotState elite = state.Slots.First(value => state.Plan.SpawnSlots.Find(plan => plan.SlotId == value.SlotId).Kind == RegionEnemyKind.Elite);
            Assert.That(state.TryRecordDeath(elite.SlotId, elite.CurrentActorId, 1), Is.True);
            Assert.That(state.TryConfirmDeathProcessed(elite.SlotId, 1), Is.True);
            for (int tick = 0; tick < 4; tick++) state.AdvanceTick(_ => Safe());
            Assert.That(elite.Phase, Is.EqualTo(RegionSpawnPhase.Warning));
            state.AdvanceTick(_ => new RegionSpawnSafety { PositionAvailable = true, OutsideSafeDistance = false, OutsideCamera = true });
            Assert.That(elite.Phase, Is.EqualTo(RegionSpawnPhase.WaitingSafety));
            Assert.That(elite.WarningStartTick, Is.EqualTo(-1));
            string checkpoint = JsonUtility.ToJson(state);
            RegionRuntimeState restored = JsonUtility.FromJson<RegionRuntimeState>(checkpoint);
            Assert.That(restored.TryValidate(Map(), out string error), Is.True, error);
            Assert.That(restored.EffectiveTick, Is.EqualTo(state.EffectiveTick));
            Assert.That(restored.GetSpawnRequests(new List<RegionSpawnRequest>()), Is.Zero);
            restored.AdvanceTick(_ => Safe());
            Assert.That(restored.Slots.Find(value => value.SlotId == elite.SlotId).Phase, Is.EqualTo(RegionSpawnPhase.Warning));
            Assert.That(restored.GetSpawnRequests(new List<RegionSpawnRequest>()), Is.Zero);
        }

        /// <summary>
        /// 宝箱与商店只转移冻结物品一次，副本与恢复数据不会共享列表。
        /// </summary>
        [Test]
        public void InteractionsAndDeepCopyPreserveUniqueOwnership()
        {
            RegionRuntimeState state = RunningState();
            RegionChestPlan chest = state.Plan.Chests[0];
            RegionNpcPlan npc = state.Plan.Npcs[0];
            Assert.That(state.TryClaimChestItem(chest.Id, chest.Items[0].Id), Is.False);
            Assert.That(state.TryOpenChest(chest.Id), Is.True);
            Assert.That(state.TryClaimChestItem(chest.Id, chest.Items[0].Id), Is.True);
            Assert.That(state.TryClaimChestItem(chest.Id, chest.Items[0].Id), Is.False);
            Assert.That(state.TrySellNpcOffer(npc.Id, npc.Offers[0].Id), Is.True);
            Assert.That(state.TrySellNpcOffer(npc.Id, npc.Offers[0].Id), Is.False);
            Assert.That(state.TrySetTrackingTarget(chest.Id), Is.True);
            RegionRuntimeState copy = state.Copy();
            copy.Plan.Chests[0].Items[0].Name = "changed";
            copy.Chests[0].ClaimedItemIds.Clear();
            Assert.That(state.Plan.Chests[0].Items[0].Name, Is.Not.EqualTo("changed"));
            Assert.That(state.Chests[0].ClaimedItemIds.Count, Is.EqualTo(1));
            RegionRuntimeState restored = JsonUtility.FromJson<RegionRuntimeState>(JsonUtility.ToJson(state));
            Assert.That(restored.TryValidate(Map(), out string error), Is.True, error);
            restored.Chests[0].ClaimedItemIds.Add(chest.Items[0].Id);
            Assert.That(restored.TryValidate(Map(), out _), Is.False);
            RegionMapDefinition wrongMap = Map();
            wrongMap.Version = "next";
            Assert.That(state.TryValidate(wrongMap, out _), Is.False);
        }

        /// <summary>
        /// Boss 死亡与领奖完成分别确认，Boss 从不进入复活请求。
        /// </summary>
        [Test]
        public void BossCompletionSurvivesContinuedRespawnClock()
        {
            RegionRuntimeState state = RunningState();
            RegionSpawnSlotState boss = state.Slots.First(value => state.Plan.SpawnSlots.Find(plan => plan.SlotId == value.SlotId).Kind == RegionEnemyKind.Boss);
            Assert.That(state.TryCompleteBossRewards(), Is.False);
            Assert.That(state.TryRecordDeath(boss.SlotId, boss.CurrentActorId, 1), Is.True);
            Assert.That(state.ExitEnabled, Is.False);
            Assert.That(state.TryConfirmDeathProcessed(boss.SlotId, 1), Is.True);
            Assert.That(state.TryCompleteBossRewards(), Is.True);
            for (int tick = 0; tick < 10; tick++) state.AdvanceTick(_ => Safe());
            Assert.That(state.ExitEnabled, Is.True);
            Assert.That(state.GetSpawnRequests(new List<RegionSpawnRequest>()), Is.Zero);
            Assert.That(state.TryValidate(Map(), out string error), Is.True, error);
        }

        /// <summary>
        /// 目标连通性与 ECS 扩张矩形占位规则一致。
        /// </summary>
        [Test]
        public void GeometryRejectsBlockedRouteAndUsesExpandedRectangles()
        {
            RegionMapDefinition map = Map();
            map.Obstacles.Add(new RegionMapObstacle { Min = new Vector2(-40, -1), Max = new Vector2(40, 1) });
            var geometry = new RegionMapGeometry(map);
            Assert.That(geometry.IsBlocked(new Vector3(0, 0, 1.2f), 0.5f), Is.True);
            Assert.That(geometry.TryValidateReachability(new[] { new Vector3(0, 0, -20), new Vector3(0, 0, 20) },
                0.5f, 1f, 10000, out _), Is.False);
            Assert.That(RegionContentGenerator.TryGenerate(Request(), map, Content(), out _, out _), Is.False);
        }

        /// <summary>
        /// 精英死亡标记返回归属点，局部地图保留追踪箭头且不绘制普通怪。
        /// </summary>
        [Test]
        public void ProjectionUsesLivePositionsAndStableDeadHomeMarkers()
        {
            RegionRuntimeState state = RunningState();
            RegionSpawnSlotPlan elitePlan = state.Plan.SpawnSlots.First(value => value.Kind == RegionEnemyKind.Elite);
            RegionSpawnSlotState elite = state.Slots.Find(value => value.SlotId == elitePlan.SlotId);
            state.TrySetTrackingTarget(elite.SlotId);
            Vector3 movingPosition = new Vector3(9, 0, 8);
            RegionMapProjection alive = RegionMapProjection.Build(state, Map(), Vector3.zero, Vector3.forward, _ => movingPosition);
            Assert.That(alive.Markers.Single(value => value.Id == elite.SlotId).Position, Is.EqualTo(movingPosition));
            Assert.That(alive.Markers.Count, Is.EqualTo(6));
            state.TryRecordDeath(elite.SlotId, elite.CurrentActorId, 1);
            RegionMapProjection dead = RegionMapProjection.Build(state, Map(), Vector3.zero, Vector3.forward, _ => movingPosition);
            RegionMapMarker marker = dead.Markers.Single(value => value.Id == elite.SlotId);
            Assert.That(marker.Position, Is.EqualTo(elitePlan.HomePosition));
            Assert.That(marker.IsTracked, Is.True);
            Assert.That(marker.RemainingTicks, Is.EqualTo(4));
            RegionMapPoint point = RegionMapProjection.ProjectLocal(new Vector3(20, 0, 0), Vector3.zero, 5);
            Assert.That(point.IsInside, Is.False);
            Assert.That(point.NormalizedPosition, Is.EqualTo(new Vector2(1, 0.5f)));
            Assert.That(RegionMapProjection.ProjectWorld(Map(), Vector3.zero).NormalizedPosition, Is.EqualTo(Vector2.one * 0.5f));
        }

        /// <summary>
        /// 生成测试区域并校验结果。
        /// </summary>
        /// <returns>有效冻结计划。</returns>
        private static RegionPlan Generate()
        {
            Assert.That(RegionContentGenerator.TryGenerate(Request(), Map(), Content(), out RegionPlan plan, out string error), Is.True, error);
            return plan;
        }

        /// <summary>
        /// 生成已提交初生身份的测试运行态。
        /// </summary>
        /// <returns>已经创建全部初始生命的运行态。</returns>
        private static RegionRuntimeState RunningState()
        {
            Assert.That(RegionRuntimeState.TryCreate(Generate(), out RegionRuntimeState state, out string error), Is.True, error);
            var requests = new List<RegionSpawnRequest>();
            state.GetSpawnRequests(requests);
            int actorId = 2;
            foreach (RegionSpawnRequest request in requests) Assert.That(state.CommitInitialSpawn(request.SlotId, actorId++), Is.True);
            return state;
        }

        /// <summary>
        /// 构建全部安全条件满足的检测结果。
        /// </summary>
        /// <returns>安全出生结果。</returns>
        private static RegionSpawnSafety Safe() => new RegionSpawnSafety
        {
            PositionAvailable = true, OutsideSafeDistance = true, OutsideCamera = true
        };

        /// <summary>
        /// 构建测试开局请求。
        /// </summary>
        /// <returns>稳定身份及独立随机种子。</returns>
        private static RegionGenerationRequest Request() => new RegionGenerationRequest
        {
            RunId = "run", RegionId = 1, RegionIndex = 0, LevelId = 1, LootLevel = 1, EnemyLevel = 1, BossDefinitionId = 10,
            BossEnemyId = 20, BossEnemyLevel = 1, BossAnchorId = "boss", MapId = 1, MapVersion = "test", ConfigVersion = "test",
            MonsterSeed = 1, EliteSeed = 2, ChestSeed = 3, NpcSeed = 4
        };

        /// <summary>
        /// 构建测试地图资源描述。
        /// </summary>
        /// <returns>完整可达地图及候选锚点。</returns>
        private static RegionMapDefinition Map() => new RegionMapDefinition
        {
            MapId = 1, Version = "test", Bounds = new Rect(-40, -40, 80, 80), Anchors = new List<RegionMapAnchor>
            {
                Anchor("entry", RegionAnchorKind.Entry, "", new Vector3(0, 0, -30), 1),
                Anchor("exit", RegionAnchorKind.Exit, "", new Vector3(0, 0, 30), 1),
                Anchor("boss", RegionAnchorKind.Boss, "", new Vector3(0, 0, 20), 1),
                Anchor("monster-a", RegionAnchorKind.Monster, "monster", new Vector3(-20, 0, -10), 3),
                Anchor("monster-b", RegionAnchorKind.Monster, "monster", new Vector3(20, 0, -10), 3),
                Anchor("elite-a", RegionAnchorKind.Elite, "elite", new Vector3(-20, 0, 15), 1),
                Anchor("elite-b", RegionAnchorKind.Elite, "elite", new Vector3(20, 0, 15), 1),
                Anchor("chest-a", RegionAnchorKind.Chest, "chest", new Vector3(-25, 0, 0), 1),
                Anchor("chest-b", RegionAnchorKind.Chest, "chest", new Vector3(-15, 0, 0), 1),
                Anchor("npc", RegionAnchorKind.Npc, "npc", new Vector3(25, 0, 0), 1)
            }, NoSpawnZones = new List<RegionNoSpawnZone> { new RegionNoSpawnZone { Center = new Vector3(0, 0, -30), Radius = 2 } }
        };

        /// <summary>
        /// 创建测试锚点。
        /// </summary>
        /// <param name="id">稳定身份。</param>
        /// <param name="kind">锚点用途。</param>
        /// <param name="tag">内容匹配标签。</param>
        /// <param name="position">世界位置。</param>
        /// <param name="capacity">可容纳成员数量。</param>
        /// <returns>测试锚点。</returns>
        private static RegionMapAnchor Anchor(string id, RegionAnchorKind kind, string tag, Vector3 position, int capacity) =>
            new RegionMapAnchor { Id = id, Kind = kind, Tag = tag, Position = position, Capacity = capacity };

        /// <summary>
        /// 构建只存在于测试程序集的内容规则。
        /// </summary>
        /// <returns>可验证冻结及刷新机制的配置。</returns>
        private static RegionContentDefinition Content() => new RegionContentDefinition
        {
            MonsterGroupCount = 2, EliteCount = 1, ChestCount = 1, NpcCount = 1, MonsterAnchorTag = "monster", EliteAnchorTag = "elite",
            ChestAnchorTag = "chest", NpcAnchorTag = "npc", BossRadius = 1, PlayerRadius = 0.5f, InteractionRadius = 2,
            ReachabilityGridSpacing = 2, ReachabilityMaxCells = 10000, MaxSpawnSlots = 30,
            NormalRespawn = new RegionRespawnRule { DelayTicks = 3, WarningTicks = 2, SafeDistance = 12, ExperienceMultiplier = 1 },
            EliteRespawn = new RegionRespawnRule { DelayTicks = 4, WarningTicks = 2, SafeDistance = 12, ExperienceMultiplier = 1 },
            MonsterEncounters = new List<RegionEncounterCandidate>
            {
                new RegionEncounterCandidate { Id = 1, Weight = 1, MinLevel = 1, MaxLevel = 2, Members = new List<RegionSpawnMemberDefinition>
                    { new RegionSpawnMemberDefinition { EnemyId = 1, Count = 2, StartOffset = new Vector3(-1, 0, 0), StepOffset = new Vector3(2, 0, 0), Radius = 0.4f } } },
                new RegionEncounterCandidate { Id = 2, Weight = 2, MinLevel = 1, MaxLevel = 2, Members = new List<RegionSpawnMemberDefinition>
                    { new RegionSpawnMemberDefinition { EnemyId = 2, Count = 2, StartOffset = new Vector3(-1, 0, 0), StepOffset = new Vector3(2, 0, 0), Radius = 0.4f } } }
            }, EliteCandidates = new List<RegionEliteCandidate>
            {
                new RegionEliteCandidate { Id = 1, EnemyId = 3, Weight = 1, MinLevel = 1, MaxLevel = 2, Radius = 0.7f }
            }, ChestCandidates = new List<RegionChestCandidate>
            {
                new RegionChestCandidate { Id = 1, Weight = 1, LootPoolId = "chest", ItemCount = 1 }
            }, NpcCandidates = new List<RegionNpcCandidate>
            {
                new RegionNpcCandidate { Id = 1, Weight = 1, LootPoolId = "merchant", Price = 10, Stock = 1 }
            }, ItemFactory = (random, level, pool) => new ProjectItem
            {
                Id = "factory-owned", Name = pool + random.Next(), Type = ProjectItemType.Item, RequiredLevel = level, SellGold = 1
            }
        };
    }
}
