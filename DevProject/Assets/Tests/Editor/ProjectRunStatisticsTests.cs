using System;
using System.Collections.Generic;
using GameLogic.Project;
using NUnit.Framework;
using UnityEngine;

namespace ProjectExtraction.Tests
{
    /// <summary>
    /// 验证单局统计的冻结、兼容和收益估值。
    /// </summary>
    public sealed class ProjectRunStatisticsTests
    {
        /// <summary>
        /// 统计副本和 JSON 往返保留独立数据。
        /// </summary>
        [Test]
        public void StatisticsCopyAndJsonPreserveValues()
        {
            ProjectRunStatistics original = Statistics();
            ProjectRunStatistics copy = original.Copy();
            copy.EnemyKills = 8;
            copy.NormalFirst.Progress = 999;
            copy.NormalRespawn.Experience = 999;
            copy.EliteFirst.Kills = 999;
            copy.EliteRespawn.Progress = 999;
            copy.Regions[0].EffectiveTicks = 999;
            copy.Regions.Clear();
            Assert.AreEqual(3, original.EnemyKills);
            Assert.AreEqual(2, original.NormalFirst.Progress);
            Assert.AreEqual(0, original.NormalRespawn.Experience);
            Assert.AreEqual(0, original.EliteFirst.Kills);
            Assert.AreEqual(0, original.EliteRespawn.Progress);
            Assert.AreEqual(120, original.Regions[0].EffectiveTicks);
            Assert.AreEqual(2, original.Regions.Count);
            ProjectRunStatistics restored = JsonUtility.FromJson<ProjectRunStatistics>(JsonUtility.ToJson(original));
            Assert.IsTrue(restored.HasData);
            Assert.AreEqual(original.EntryItemSellValue, restored.EntryItemSellValue);
            Assert.AreEqual(original.DamageDealt, restored.DamageDealt);
            Assert.IsTrue(ProjectRunStatistics.IsValid(restored));
            Assert.AreEqual(JsonUtility.ToJson(original), JsonUtility.ToJson(restored));
        }

        /// <summary>
        /// 真实旧格式保留已有历史与入场估值，新维度明确未记录且不伪造分类零值。
        /// </summary>
        [Test]
        public void VersionOneHistoryPreservesRecordedValuesAndMarksNewFieldsMissing()
        {
            const string oldJson = "{\"Version\":1,\"TotalRooms\":5,\"VisitedRooms\":3,\"ClearedCombatRooms\":2," +
                "\"EnemyKills\":7,\"BossRoomKills\":1,\"RogueChoices\":2,\"EntryItemCount\":1,\"EntryItemSellValue\":50}";
            ProjectRunStatistics old = JsonUtility.FromJson<ProjectRunStatistics>(oldJson);
            Assert.IsTrue(ProjectRunStatistics.IsValid(old));
            Assert.IsTrue(old.HasData);
            Assert.IsFalse(old.HasRegionData);
            ProjectSaveData data = Running("old-recorded-stats");
            ProjectRunOutcome outcome = Outcome(data, true);
            outcome.Statistics = old;
            Assert.IsTrue(ProjectRunRecordRules.TryRecordOutcome(data, outcome, out string error), error);
            Assert.IsTrue(ProjectRunRecordRules.TrySettleOutcome(data, out error), error);
            data = RoundTrip(data);
            ProjectRunHistoryEntry record = data.RunHistory[0];
            Assert.AreEqual(7, record.Statistics.EnemyKills);
            Assert.AreEqual(3, record.Statistics.VisitedRooms);
            Assert.IsTrue(record.HasNetAssetValue);
            Assert.IsTrue(ProjectRunEconomy.Calculate(data.LastResult).HasEntrySnapshot);
            StringAssert.Contains("未记录", record.Statistics.Describe());
            StringAssert.DoesNotContain("普通首次 击杀 0", record.Statistics.Describe());
            ProjectRunHistorySummary summary = ProjectRunHistoryRules.Summarize(data.RunHistory)[0];
            Assert.AreEqual(1, summary.ExtractedCount);
            Assert.AreEqual(0, summary.RegionStatsSampleCount);
            Assert.AreEqual(0, summary.EarlyExtractionCount);
        }

        /// <summary>
        /// 最终领奖完成使用追加的通关枚举，仍正常携回全部物品和撤离经验并保持幂等。
        /// </summary>
        [Test]
        public void FinalCompletionRemainsSuccessfulSettlementAndDistinctHistory()
        {
            ProjectSaveData data = Running("complete-v2");
            ProjectRunOutcome outcome = Outcome(data, true);
            ProjectRunStatistics statistics = Statistics();
            statistics.RegionBossKills = 3;
            statistics.FinalBossKills = 1;
            statistics.FinalBossExperience = 123;
            statistics.EnemyKills = 6;
            statistics.BossLootRounds = 6;
            statistics.Regions.Add(new ProjectRegionStatistics { RegionId = 3, RegionIndex = 2, Completed = true });
            statistics.Regions.Add(new ProjectRegionStatistics { RegionId = 4, RegionIndex = 3, IsFinalRegion = true, Completed = true });
            statistics.Regions[1].Completed = true;
            outcome.Statistics = statistics;
            outcome.KillExperience = outcome.AwardedKillExperience = 123;
            outcome.ExtractionExperience = 15;
            Assert.IsTrue(ProjectRunRecordRules.TryRecordOutcome(data, outcome, out string error), error);
            Assert.IsTrue(ProjectRunRecordRules.TrySettleOutcome(data, out error), error);
            data = RoundTrip(data);
            Assert.IsTrue(data.LastResult.Outcome.Extracted);
            Assert.AreEqual(outcome.Items.Count, data.LastResult.RetainedItems.Count);
            Assert.IsEmpty(data.LastResult.LostItems);
            Assert.AreEqual(15, data.LastResult.Outcome.ExtractionExperience);
            Assert.AreEqual(ProjectRunHistoryOutcome.Completed, data.RunHistory[0].Outcome);
            Assert.AreEqual(4, data.RunHistory[0].Statistics.CompletedRegions);
            ProjectRunHistorySummary summary = ProjectRunHistoryRules.Summarize(data.RunHistory)[0];
            Assert.AreEqual(1, summary.CompletedCount);
            Assert.AreEqual(1, summary.ExtractedCount);
            Assert.AreEqual(0, summary.EarlyExtractionCount);
            string frozen = JsonUtility.ToJson(data);
            Assert.IsTrue(ProjectRunRecordRules.TrySettleOutcome(data, out error), error);
            Assert.AreEqual(frozen, JsonUtility.ToJson(data));
            ProjectRunHistoryEntry invalid = data.RunHistory[0].Copy();
            invalid.Outcome = ProjectRunHistoryOutcome.Extracted;
            Assert.IsNotNull(ProjectRunHistoryRules.Validate(new[] { invalid }));
            invalid.Outcome = ProjectRunHistoryOutcome.Completed;
            invalid.Statistics.Regions[3].Completed = false;
            Assert.IsNotNull(ProjectRunHistoryRules.Validate(new[] { invalid }));
        }

        /// <summary>
        /// 旧记录表示统计缺席，版本零仅接受全零数据。
        /// </summary>
        [Test]
        public void LegacyStatisticsRemainAbsent()
        {
            Assert.IsTrue(ProjectRunStatistics.IsValid(null));
            var empty = new ProjectRunStatistics();
            Assert.IsFalse(empty.HasData);
            Assert.IsTrue(ProjectRunStatistics.IsValid(empty));
            empty.EnemyKills = 1;
            Assert.IsFalse(ProjectRunStatistics.IsValid(empty));
            ProjectSaveData data = Running("legacy-stats");
            ProjectRunOutcome outcome = Outcome(data, true);
            Assert.IsTrue(ProjectRunRecordRules.TryRecordOutcome(data, outcome, out _));
            Assert.IsTrue(ProjectRunRecordRules.TrySettleOutcome(data, out _));
            ProjectSaveData restored = RoundTrip(data);
            Assert.IsFalse(restored.LastResult.Outcome.Statistics?.HasData == true);
            Assert.IsFalse(ProjectRunEconomy.Calculate(restored.LastResult).HasEntrySnapshot);
        }

        /// <summary>
        /// 记录重试及结算回读保留统计，外部修改不能改变冻结快照。
        /// </summary>
        [Test]
        public void RecordedAndSettledStatisticsSurviveRetryAndReload()
        {
            ProjectSaveData data = Running("stats-retry");
            ProjectRunOutcome outcome = Outcome(data, true);
            outcome.Statistics = Statistics();
            Assert.IsTrue(ProjectRunRecordRules.TryRecordOutcome(data, outcome, out _));
            outcome.Statistics.EnemyKills = 2;
            Assert.AreEqual(3, data.Outcome.Statistics.EnemyKills);
            data = RoundTrip(data);
            Assert.IsTrue(ProjectRunRecordRules.TryRecordOutcome(data, data.Outcome.Copy(), out _));
            Assert.IsTrue(ProjectRunRecordRules.TrySettleOutcome(data, out _));
            data = RoundTrip(data);
            Assert.AreEqual(3, data.LastResult.Outcome.Statistics.EnemyKills);
            ProjectSaveData copy = data.Copy();
            copy.LastResult.Outcome.Statistics.EnemyKills = 1;
            Assert.AreEqual(3, data.LastResult.Outcome.Statistics.EnemyKills);
            string saved = new ProjectSaveCodec().Encode(data);
            Assert.IsTrue(ProjectRunRecordRules.TrySettleOutcome(data, out _));
            Assert.AreEqual(saved, new ProjectSaveCodec().Encode(data));
        }

        /// <summary>
        /// 拒绝未知版本、无效计数和非有限伤害，且不写入终局。
        /// </summary>
        [Test]
        public void InvalidStatisticsRejectOutcome()
        {
            ProjectSaveData data = Running("invalid-stats");
            ProjectRunOutcome outcome = Outcome(data, true);
            ProjectRunStatistics statistics = Statistics();
            outcome.Statistics = statistics;
            Action[] corrupt =
            {
                () => statistics.Version = 3, () => statistics.TotalRegions = 0, () => statistics.Regions = null,
                () => statistics.Regions[1].RegionIndex = 5, () => statistics.RegionBossKills = 3,
                () => statistics.Regions[1].RegionId = 1, () => statistics.Regions[0].EffectiveTicks = -1,
                () => statistics.NormalFirst.Kills = 1, () => statistics.NormalFirst.Experience = (long)int.MaxValue + 1,
                () => statistics.RegionBossExperience = long.MaxValue, () => statistics.FinalBossExperience = long.MaxValue,
                () => statistics.EnemyKills = -1, () => statistics.BossRoomKills = 4,
                () => statistics.RogueChoices = -1, () => statistics.EntryItemCount = -1,
                () => statistics.EntryItemSellValue = -1, () => statistics.EntryItemSellValue = long.MaxValue,
                () => statistics.DamageDealt = double.NaN,
                () => statistics.DamageTaken = double.PositiveInfinity, () => statistics.HealingReceived = -1
            };
            foreach (Action change in corrupt)
            {
                statistics = Statistics();
                outcome.Statistics = statistics;
                change();
                Assert.IsFalse(ProjectRunRecordRules.TryRecordOutcome(data, outcome, out _));
                Assert.IsNull(data.Outcome);
            }
        }

        /// <summary>
        /// 空入场装备对应零回收快照，并允许计算战利品收益。
        /// </summary>
        [Test]
        public void EmptyEntrySnapshotIsValid()
        {
            ProjectRunStatistics statistics = Statistics();
            statistics.EntryItemCount = 0;
            statistics.EntryItemSellValue = 0;
            Assert.IsTrue(ProjectRunStatistics.IsValid(statistics));
            var result = new ProjectRunResult
            {
                Outcome = new ProjectRunOutcome { GoldSpent = 5, Statistics = statistics },
                RetainedItems = new List<ProjectItem> { new ProjectItem { SellGold = 40 } }
            };
            ProjectRunEconomySummary summary = ProjectRunEconomy.Calculate(result);
            Assert.IsTrue(summary.HasEntrySnapshot);
            Assert.AreEqual(35, summary.NetAssetValue);
            statistics.EntryItemSellValue = 1;
            Assert.IsFalse(ProjectRunStatistics.IsValid(statistics));
        }

        /// <summary>
        /// 撤离、死亡与放弃按冻结分区估值，重复读取不改变钱包。
        /// </summary>
        /// <param name="extracted">是否正常撤离。</param>
        /// <param name="abandoned">是否主动放弃。</param>
        /// <param name="retained">预期保留物品回收估值。</param>
        /// <param name="lost">预期丢失物品回收估值。</param>
        /// <param name="net">预期净资产变化。</param>
        [TestCase(true, false, 4200000000L, 0L, 4099999990L)]
        [TestCase(false, false, 0L, 4200000000L, -100000010L)]
        [TestCase(false, true, 0L, 4200000000L, -100000010L)]
        public void EconomyUsesFrozenItemsWithoutChangingSettlement(bool extracted, bool abandoned, long retained, long lost, long net)
        {
            ProjectSaveData data = Running("economy-" + extracted + abandoned);
            ProjectRunOutcome outcome = Outcome(data, extracted);
            outcome.Abandoned = abandoned;
            outcome.GoldSpent = 10;
            outcome.Items[0].SellGold = 2100000000;
            ProjectItem loot = outcome.Items[0].Copy();
            loot.Id = "valuable-loot";
            loot.Equipped = false;
            loot.SellGold = 2100000000;
            outcome.Items.Add(loot);
            outcome.Statistics = Statistics();
            outcome.Statistics.EntryItemSellValue = 100000000;
            Assert.IsTrue(ProjectRunRecordRules.TryRecordOutcome(data, outcome, out _));
            Assert.IsTrue(ProjectRunRecordRules.TrySettleOutcome(data, out _));
            int wallet = data.Gold;
            ProjectRunEconomySummary summary = ProjectRunEconomy.Calculate(data.LastResult);
            Assert.IsTrue(summary.HasEntrySnapshot);
            Assert.AreEqual(retained, summary.RetainedSellValue);
            Assert.AreEqual(lost, summary.LostSellValue);
            Assert.AreEqual(100000000, summary.EntrySellValue);
            Assert.AreEqual(net, summary.NetAssetValue);
            Assert.AreEqual(wallet, data.Gold);
            Assert.IsTrue(ProjectRunRecordRules.TrySettleOutcome(data, out _));
            Assert.AreEqual(wallet, data.Gold);
        }

        /// <summary>
        /// 创建有效的单局统计。
        /// </summary>
        /// <returns>包含入场快照的统计。</returns>
        private static ProjectRunStatistics Statistics() => new()
        {
            Version = ProjectRunStatistics.CurrentVersion, TotalRegions = 4, EnemyKills = 3, RegionBossKills = 1, RogueChoices = 1,
            NormalFirst = new ProjectEnemyLifeStatistics { Kills = 2, Progress = 2 },
            Regions = new List<ProjectRegionStatistics>
            {
                new() { RegionId = 1, RegionIndex = 0, EffectiveTicks = 120, Completed = true },
                new() { RegionId = 2, RegionIndex = 1, EffectiveTicks = 60 }
            },
            EntryItemCount = 1, EntryItemSellValue = 50, DamageDealt = 12.5, DamageTaken = 4, HealingReceived = 2
        };

        /// <summary>
        /// 创建已进入运行阶段的单局。
        /// </summary>
        /// <param name="runId">单局身份。</param>
        /// <returns>独立事务候选。</returns>
        private static ProjectSaveData Running(string runId)
        {
            ProjectSaveData data = ProjectSaveData.CreateDefault();
            Assert.IsTrue(ProjectRunRecordRules.TryAdmit(data, runId, 1, 17, new[] { data.Warehouse[0].Copy() }, out _));
            Assert.IsTrue(ProjectRunRecordRules.TryMarkRunning(data, runId, out _));
            return data;
        }

        /// <summary>
        /// 创建当前单局终局。
        /// </summary>
        /// <param name="data">运行记录。</param>
        /// <param name="extracted">是否撤离。</param>
        /// <returns>待冻结终局。</returns>
        private static ProjectRunOutcome Outcome(ProjectSaveData data, bool extracted) => new()
        {
            RunId = data.ActiveRunId, LevelId = data.ActiveLevelId, Seed = data.ActiveSeed,
            RulesVersion = ProjectRunRecordRules.RulesVersion, Extracted = extracted,
            Items = new List<ProjectItem> { data.EntryItems[0].Copy() }
        };

        /// <summary>
        /// 通过项目编解码器往返并校验存档。
        /// </summary>
        /// <param name="data">待写入存档。</param>
        /// <returns>重新读取的存档。</returns>
        private static ProjectSaveData RoundTrip(ProjectSaveData data)
        {
            var codec = new ProjectSaveCodec();
            Assert.IsNull(ProjectSaveData.Validate(data));
            ProjectSaveData restored = codec.Decode(codec.Encode(data));
            Assert.IsNull(ProjectSaveData.Validate(restored));
            return restored;
        }
    }
}
