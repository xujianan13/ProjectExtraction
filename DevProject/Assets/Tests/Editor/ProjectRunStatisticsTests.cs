using System;
using System.Collections.Generic;
using GameLogic.Gameplay.Expedition;
using GameLogic.Gameplay.Level;
using GameLogic.Project;
using GameLogic.Tables;
using NUnit.Framework;
using ProjectExtraction.Tests.Shared;
using UnityEngine;

namespace ProjectExtraction.Tests
{
    /// <summary>
    /// 线性房间统计、唯一发奖账本、历史兼容和冻结收益估值的纯数据验证。
    /// </summary>
    public sealed class ProjectRunStatisticsTests
    {
        /// <summary>
        /// 真实 Boss 奖励箱从未开到开启和领奖完成均可生成合法统计，不计普通宝箱或重复累计 Boss 收益。
        /// </summary>
        [Test]
        public void BossRewardChestOpeningPreservesValidUniqueBossStatistics()
        {
            TestGameTables.LoadAll();
            Assert.That(LevelContentConfig.TryGetLevel(1, out var level), Is.True);
            Assert.That(LevelContentConfig.TryCreateRoomRequests("boss-chest-statistics", 31, level, out var requests, out string error), Is.True, error);
            var request = requests[requests.Count - 1];
            Assert.That(request.RoomKind, Is.EqualTo(ExpeditionRoomKind.Boss));
            Assert.That(LevelContentConfig.TryCreateRoomMap(request.MapId, out var map, out error), Is.True, error);
            Assert.That(RegionContentGenerator.TryGenerate(request, map, LevelContentConfig.CreateRegionContent(request), out var plan, out error),
                Is.True, error);
            Assert.That(RegionRuntimeState.TryCreate(plan, out var room, out error), Is.True, error);
            Assert.That(plan.Chests.Count, Is.EqualTo(1));
            Assert.That(plan.Chests[0].Kind, Is.EqualTo(RegionChestKind.BossReward));
            Assert.That(plan.Chests[0].Items, Is.Empty, "Boss 候选由独立多轮奖励会话提供。");
            var rewarded = new List<string>();
            for (int index = 0; index < room.Slots.Count; index++)
            {
                var slot = room.Slots[index];
                Assert.That(room.CommitInitialSpawn(slot.SlotId, index + 2), Is.True);
                Assert.That(room.TryRecordDeath(slot.SlotId, index + 2, slot.LifeSequence), Is.True);
                Assert.That(room.TryConfirmDeathProcessed(slot.SlotId, slot.LifeSequence), Is.True);
                rewarded.Add(slot.SlotId);
            }
            Assert.That(room.BossDefeated, Is.True);
            Assert.That(ProjectRunStatistics.TryCreateRoomStatistics(plan, rewarded, room.ExitEnabled, room.EffectiveTick,
                room.Chests, room.Npcs, out var unopened), Is.True);
            Assert.That(room.TryOpenChest(plan.Chests[0].Id), Is.True);
            Assert.That(ProjectRunStatistics.TryCreateRoomStatistics(plan, rewarded, room.ExitEnabled, room.EffectiveTick,
                room.Chests, room.Npcs, out var opened), Is.True, "开启 Boss 奖励箱后保存统计不能触发恢复流程。");
            Assert.That(room.TryCompleteBossRewards(), Is.True);
            Assert.That(ProjectRunStatistics.TryCreateRoomStatistics(plan, rewarded, room.ExitEnabled, room.EffectiveTick,
                room.Chests, room.Npcs, out var completed), Is.True);
            foreach (var row in new[] { unopened, opened, completed })
            {
                Assert.That(row.OpenedChests, Is.Zero);
                Assert.That(row.ClaimedChestItems, Is.Zero);
                Assert.That(row.BossKills, Is.EqualTo(1));
                Assert.That(row.BossExperience, Is.EqualTo(unopened.BossExperience));
            }
            var statistics = CompletedSnapshot().Statistics;
            statistics.Rooms[statistics.Rooms.Count - 1] = completed;
            statistics.BossLootRounds = ProjectRewardConfig.BossReward(GameTables.CfgBosses.GetById(request.BossDefinitionId).RewardID).Rounds;
            statistics.RecalculateRoomTotals();
            string confirmed = JsonUtility.ToJson(statistics);
            statistics.RecalculateRoomTotals();
            Assert.That(ProjectRunStatistics.IsValid(statistics), Is.True);
            Assert.That(statistics.FinalBossKills, Is.EqualTo(1));
            Assert.That(statistics.BossRoomKills, Is.EqualTo(1));
            Assert.That(JsonUtility.ToJson(statistics), Is.EqualTo(confirmed));
            room.Chests[0].ClaimedItemIds.Add("foreign-boss-chest-item");
            Assert.That(ProjectRunStatistics.TryCreateRoomStatistics(plan, rewarded, room.ExitEnabled, room.EffectiveTick,
                room.Chests, room.Npcs, out _), Is.False, "排除普通宝箱计数不能跳过 Boss 箱领取身份校验。");
        }

        /// <summary>
        /// 统计副本和 JSON 往返独立保存房间身份、领取事实与汇总。
        /// </summary>
        [Test]
        public void StatisticsCopyAndJsonPreserveIndependentRoomData()
        {
            var snapshot = CompletedSnapshot();
            var original = snapshot.Statistics;
            var copy = original.Copy();
            copy.Rooms[0].RewardedSlotIds.Clear();
            copy.Rooms[0].EffectiveTicks = 999;
            copy.NormalFirst.Experience = 999;
            Assert.That(original.Rooms[0].RewardedSlotIds.Count, Is.EqualTo(1));
            Assert.That(original.Rooms[0].EffectiveTicks, Is.EqualTo(120));
            var restored = JsonUtility.FromJson<ProjectRunStatistics>(JsonUtility.ToJson(original));
            Assert.That(restored.HasRoomData, Is.True);
            Assert.That(ProjectRunStatistics.IsValid(restored), Is.True);
            Assert.That(JsonUtility.ToJson(restored), Is.EqualTo(JsonUtility.ToJson(original)));
            StringAssert.Contains("宝箱 1", restored.Describe());
            StringAssert.Contains("有效发奖击杀", restored.Describe());
            StringAssert.DoesNotContain("普通复活", restored.Describe());
            StringAssert.DoesNotContain("区域尾王", restored.Describe());
            StringAssert.DoesNotContain("技能选择", restored.Describe());
        }

        /// <summary>
        /// 六房完成与最终 Boss 唯一收益可生成新规则结果，重试结算不会重复入库或记录战绩。
        /// </summary>
        [Test]
        public void SixRoomCompletionProducesValidSettlementAndHistory()
        {
            var snapshot = CompletedSnapshot();
            Assert.That(ExpeditionRun.ValidateStatisticsSnapshot(snapshot), Is.True);
            Assert.That(snapshot.Statistics.CompletedRooms, Is.EqualTo(6));
            Assert.That(snapshot.Statistics.ClearedCombatRooms, Is.EqualTo(3));
            Assert.That(snapshot.Statistics.FinalBossKills, Is.EqualTo(1));
            Assert.That(snapshot.Statistics.EnemyKills, Is.EqualTo(4));
            Assert.That(snapshot.Statistics.IsFinalCompletion, Is.True);
            var data = ProjectSaveData.CreateDefault();
            Assert.That(ProjectRunRecordRules.TryAdmit(data, "six-room", 1, 17, new[] { data.Warehouse[0].Copy() }, out _), Is.True);
            Assert.That(ProjectRunRecordRules.TryMarkRunning(data, "six-room", out _), Is.True);
            snapshot.Statistics.EntryItemSellValue = data.EntryItems[0].SellGold;
            var outcome = new ProjectRunOutcome
            {
                RunId = data.ActiveRunId, LevelId = 1, Seed = 17, RulesVersion = ProjectRunRecordRules.RulesVersion,
                ExitKind = ProjectRunExitKind.Completed, KillExperience = snapshot.KillExperience,
                AwardedKillExperience = snapshot.KillExperience, Statistics = snapshot.Statistics,
                Items = new List<ProjectItem> { new() { Id = "earned-item", Name = "本关所得", RequiredLevel = 1 } }
            };
            Assert.That(ProjectRunRecordRules.TryRecordOutcome(data, outcome, out string error), Is.True, error);
            Assert.That(ProjectRunRecordRules.TrySettleOutcome(data, out error), Is.True, error);
            Assert.That(ProjectSaveData.Validate(data), Is.Null);
            Assert.That(data.LastResult.RetainedItems.Count, Is.EqualTo(2));
            Assert.That(data.LastResult.LostItems, Is.Empty);
            Assert.That(data.RunHistory[0].Outcome, Is.EqualTo(ProjectRunHistoryOutcome.Completed));
            string frozen = new ProjectSaveCodec().Encode(data);
            Assert.That(ProjectRunRecordRules.TrySettleOutcome(data, out error), Is.True, error);
            Assert.That(new ProjectSaveCodec().Encode(data), Is.EqualTo(frozen));
        }

        /// <summary>
        /// 相同发奖账本重建与多次汇总不增加击杀或永久经验。
        /// </summary>
        [Test]
        public void RewardedSlotsRemainIdempotentAcrossCheckpointRoundTrip()
        {
            var original = CompletedSnapshot();
            var restored = JsonUtility.FromJson<ExpeditionRunSnapshot>(JsonUtility.ToJson(original));
            restored.Statistics.RecalculateRoomTotals();
            restored.Statistics.RecalculateRoomTotals();
            Assert.That(ExpeditionRun.ValidateStatisticsSnapshot(restored), Is.True);
            Assert.That(restored.Statistics.EnemyKills, Is.EqualTo(original.Statistics.EnemyKills));
            Assert.That(restored.Statistics.EarnedKillExperience, Is.EqualTo(original.KillExperience));
            Assert.That(restored.Statistics.NormalRespawn.Kills, Is.Zero);
            Assert.That(restored.Statistics.EliteRespawn.Kills, Is.Zero);
        }

        /// <summary>
        /// 重复或外来槽位、篡改经验、完成和时间的活动检查点均被拒绝。
        /// </summary>
        /// <param name="corruption">需要破坏的业务事实。</param>
        [TestCase("duplicate-slot")]
        [TestCase("foreign-slot")]
        [TestCase("experience")]
        [TestCase("completion")]
        [TestCase("elapsed")]
        [TestCase("old-version")]
        public void SnapshotValidationRejectsCorruptedBusinessFacts(string corruption)
        {
            var snapshot = CompletedSnapshot();
            if (corruption == "duplicate-slot") snapshot.Rooms[0].RewardedSlotIds.Add(snapshot.Rooms[0].RewardedSlotIds[0]);
            if (corruption == "foreign-slot") snapshot.Rooms[0].RewardedSlotIds[0] = "unknown";
            if (corruption == "experience") snapshot.KillExperience++;
            if (corruption == "completion") snapshot.Rooms[0].Completed = false;
            if (corruption == "elapsed") snapshot.ElapsedTicks++;
            if (corruption == "old-version") snapshot.Statistics.Version = 2;
            Assert.That(ExpeditionRun.ValidateStatisticsSnapshot(snapshot), Is.False);
        }

        /// <summary>
        /// 宝箱领取和商人售出必须属于固定计划且不重复，不能只篡改统计数量。
        /// </summary>
        /// <param name="duplicateChest">是否破坏宝箱领取，否则破坏商人交易。</param>
        [TestCase(true)]
        [TestCase(false)]
        public void SnapshotValidationRejectsDuplicateChestOrNpcFacts(bool duplicateChest)
        {
            var snapshot = CompletedSnapshot();
            if (duplicateChest) snapshot.Rooms[3].Chests[0].ClaimedItemIds.Add("chest-item");
            else snapshot.Rooms[2].Npcs[0].SoldOfferIds.Add("offer");
            Assert.That(ExpeditionRun.ValidateStatisticsSnapshot(snapshot), Is.False);
        }

        /// <summary>
        /// 新汇总拒绝未知版本、非有限伤害、旧复活进度、房间越序与跨房重复发奖身份。
        /// </summary>
        [Test]
        public void CurrentStatisticsRejectInvalidCountsAndMixedOldProgress()
        {
            Action<ProjectRunStatistics>[] changes =
            {
                stats => stats.Version = 4, stats => stats.TotalRooms = 0, stats => stats.Rooms = null,
                stats => stats.Rooms[1].RoomIndex = 5, stats => stats.Rooms[1].RoomId = stats.Rooms[0].RoomId,
                stats => stats.Rooms[1].RewardedSlotIds[0] = stats.Rooms[0].RewardedSlotIds[0],
                stats => stats.Rooms[0].Completed = false, stats => stats.EnemyKills++,
                stats => stats.NormalFirst.Experience++, stats => stats.NormalRespawn.Kills = 1,
                stats => stats.RogueChoices = 1, stats => stats.EntryItemSellValue = -1,
                stats => stats.DamageDealt = double.NaN, stats => stats.DamageTaken = double.PositiveInfinity
            };
            foreach (var change in changes)
            {
                var stats = CompletedSnapshot().Statistics;
                change(stats);
                Assert.That(ProjectRunStatistics.IsValid(stats), Is.False);
            }
        }

        /// <summary>
        /// 版本一和版本二历史保持只读，不伪造成新版活动房间统计。
        /// </summary>
        [Test]
        public void LegacyStatisticsRemainReadableWithoutRoomDetails()
        {
            const string oldJson = "{\"Version\":1,\"TotalRooms\":5,\"VisitedRooms\":3,\"ClearedCombatRooms\":2," +
                "\"EnemyKills\":7,\"BossRoomKills\":1,\"RogueChoices\":2,\"EntryItemCount\":1,\"EntryItemSellValue\":50}";
            var old = JsonUtility.FromJson<ProjectRunStatistics>(oldJson);
            Assert.That(ProjectRunStatistics.IsValid(old), Is.True);
            Assert.That(old.HasData, Is.True);
            Assert.That(old.HasRoomData, Is.False);
            StringAssert.Contains("未记录", old.Describe());
            var regional = new ProjectRunStatistics
            {
                Version = 2, TotalRegions = 2, EnemyKills = 1,
                NormalFirst = new ProjectEnemyLifeStatistics { Kills = 1, Experience = 10 },
                Regions = new List<ProjectRegionStatistics> { new() { RegionId = 1, RegionIndex = 0 } }
            };
            Assert.That(ProjectRunStatistics.IsValid(regional), Is.True);
            Assert.That(regional.HasRegionData, Is.True);
            Assert.That(regional.HasRoomData, Is.False);
            Assert.That(ProjectRunStatistics.IsValid(null), Is.True);
            Assert.That(ProjectRunStatistics.IsValid(new ProjectRunStatistics()), Is.True);
        }

        /// <summary>
        /// 回收估值按携入保护和本关损失分类计算，读取不会修改任何结算集合。
        /// </summary>
        /// <param name="completed">是否正常通关。</param>
        /// <param name="expectedRetained">预期带回估值。</param>
        /// <param name="expectedLost">预期损失估值。</param>
        /// <param name="expectedNet">预期相对入场的净资产变化。</param>
        [TestCase(true, 140L, 0L, 30L)]
        [TestCase(false, 100L, 40L, -10L)]
        public void EconomyUsesFrozenProtectedEntryAndEarnedBag(bool completed, long expectedRetained, long expectedLost, long expectedNet)
        {
            var stats = CompletedSnapshot().Statistics;
            stats.EntryItemSellValue = 100;
            var result = new ProjectRunResult
            {
                Outcome = new ProjectRunOutcome { ExitKind = completed ? ProjectRunExitKind.Completed : ProjectRunExitKind.Death,
                    Statistics = stats, GoldSpent = 10 },
                RetainedItems = new List<ProjectItem> { new() { SellGold = 100 } }
            };
            var earned = new ProjectItem { SellGold = 40 };
            if (completed) result.RetainedItems.Add(earned);
            else result.LostItems.Add(earned);
            string frozen = JsonUtility.ToJson(result);
            var summary = ProjectRunEconomy.Calculate(result);
            Assert.That(summary.RetainedSellValue, Is.EqualTo(expectedRetained));
            Assert.That(summary.LostSellValue, Is.EqualTo(expectedLost));
            Assert.That(summary.NetAssetValue, Is.EqualTo(expectedNet));
            Assert.That(JsonUtility.ToJson(result), Is.EqualTo(frozen));
        }

        /// <summary>
        /// 构造六房全部完成的纯业务检查点，不创建战斗世界或任何临时演员。
        /// </summary>
        /// <returns>有三间怪物房、NPC、宝箱和最终 Boss 的一致检查点。</returns>
        private static ExpeditionRunSnapshot CompletedSnapshot()
        {
            var snapshot = new ExpeditionRunSnapshot { RunId = "six-room", LevelId = 1, CurrentRoomIndex = 5 };
            snapshot.EntryEquipment.Add(new ProjectItem { Id = "entry", SellGold = 50 });
            var kinds = new[] { ExpeditionRoomKind.Combat, ExpeditionRoomKind.Combat, ExpeditionRoomKind.Npc,
                ExpeditionRoomKind.Chest, ExpeditionRoomKind.Combat, ExpeditionRoomKind.Boss };
            int enemyId = GameTables.CfgEnemies.GetAll()[0].ID;
            var stats = new ProjectRunStatistics { Version = ProjectRunStatistics.CurrentVersion, TotalRooms = 6,
                EntryItemCount = 1, EntryItemSellValue = 50, DamageDealt = 12.5, DamageTaken = 4, HealingReceived = 2 };
            for (int index = 0; index < kinds.Length; index++)
            {
                var plan = new RegionPlan { Request = new RegionGenerationRequest
                { RunId = "six-room", RegionId = index + 1, RegionIndex = index, RoomKind = kinds[index] } };
                var room = new ExpeditionRoomCheckpoint { Plan = plan, Completed = true, EffectiveTicks = 120 };
                if (kinds[index] == ExpeditionRoomKind.Combat || kinds[index] == ExpeditionRoomKind.Boss)
                {
                    string id = "room-" + index + "-enemy";
                    plan.SpawnSlots.Add(new RegionSpawnSlotPlan { SlotId = id, EnemyId = enemyId, EnemyLevel = 1,
                        Kind = kinds[index] == ExpeditionRoomKind.Boss ? RegionEnemyKind.Boss : index == 1 ? RegionEnemyKind.Elite : RegionEnemyKind.Normal });
                    room.RewardedSlotIds.Add(id);
                    room.BossDefeated = room.BossRewardsCompleted = kinds[index] == ExpeditionRoomKind.Boss;
                }
                if (kinds[index] == ExpeditionRoomKind.Chest)
                {
                    plan.Chests.Add(new RegionChestPlan { Id = "chest", Items = new List<ProjectItem> { new() { Id = "chest-item" } } });
                    room.Chests.Add(new RegionChestState { Id = "chest", Opened = true, ClaimedItemIds = new List<string> { "chest-item" } });
                }
                if (kinds[index] == ExpeditionRoomKind.Npc)
                {
                    plan.Npcs.Add(new RegionNpcPlan { Id = "npc", Offers = new List<RegionNpcOfferPlan> { new() { Id = "offer" } } });
                    room.Npcs.Add(new RegionNpcState { Id = "npc", Visited = true, SoldOfferIds = new List<string> { "offer" } });
                }
                Assert.That(ProjectRunStatistics.TryCreateRoomStatistics(plan, room.RewardedSlotIds, room.Completed, room.EffectiveTicks,
                    room.Chests, room.Npcs, out var row), Is.True);
                stats.Rooms.Add(row);
                snapshot.Rooms.Add(room);
                snapshot.Requests.Add(plan.Request);
                snapshot.ElapsedTicks += room.EffectiveTicks;
            }
            stats.RecalculateRoomTotals();
            snapshot.Statistics = stats;
            snapshot.KillExperience = (int)stats.EarnedKillExperience;
            Assert.That(ProjectRunStatistics.IsValid(stats), Is.True);
            return snapshot;
        }
    }
}
