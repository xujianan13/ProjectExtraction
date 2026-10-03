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
            Assert.AreEqual(3, original.EnemyKills);
            ProjectRunStatistics restored = JsonUtility.FromJson<ProjectRunStatistics>(JsonUtility.ToJson(original));
            Assert.IsTrue(restored.HasData);
            Assert.AreEqual(original.EntryItemSellValue, restored.EntryItemSellValue);
            Assert.AreEqual(original.DamageDealt, restored.DamageDealt);
            Assert.IsTrue(ProjectRunStatistics.IsValid(restored));
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
                () => statistics.Version = 2, () => statistics.TotalRooms = 0, () => statistics.VisitedRooms = 0,
                () => statistics.VisitedRooms = 5, () => statistics.ClearedCombatRooms = 3,
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
        [TestCase(false, false, 2100000000L, 2100000000L, 1999999990L)]
        [TestCase(false, true, 2100000000L, 2100000000L, 1999999990L)]
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
            outcome.SafeItemIds.Add(loot.Id);
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
            Version = ProjectRunStatistics.CurrentVersion, TotalRooms = 4, VisitedRooms = 2,
            ClearedCombatRooms = 1, EnemyKills = 3, BossRoomKills = 1, RogueChoices = 1,
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
