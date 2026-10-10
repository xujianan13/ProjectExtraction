using System;
using System.Collections.Generic;
using GameLogic.Project;
using NUnit.Framework;
using UnityEngine;

namespace ProjectExtraction.Tests
{
    /// <summary>
    /// 验证最近单局战绩的事务、快照和汇总边界。
    /// </summary>
    public sealed class ProjectRunHistoryTests
    {
        /// <summary>
        /// 战绩副本及 JSON 往返不共享统计对象。
        /// </summary>
        [Test]
        public void HistoryCopyAndRoundTripAreIndependent()
        {
            ProjectSaveData original = ProjectSaveData.CreateDefault();
            original.RunHistory.Add(Entry("one", 1, ProjectRunHistoryOutcome.Extracted, true));
            ProjectSaveData copy = original.Copy();
            copy.RunHistory[0].Statistics.EnemyKills = 9;
            Assert.AreEqual(2, original.RunHistory[0].Statistics.EnemyKills);
            var codec = new ProjectSaveCodec();
            ProjectSaveData restored = codec.Decode(codec.Encode(original));
            Assert.IsNull(ProjectSaveData.Validate(restored));
            Assert.AreEqual(2, restored.RunHistory[0].Statistics.EnemyKills);
        }

        /// <summary>
        /// 无历史字段的旧档从空集合开始，不补造最近结果。
        /// </summary>
        [Test]
        public void LegacySaveHasEmptyHistory()
        {
            string json = new ProjectSaveCodec().Encode(ProjectSaveData.CreateDefault());
            json = json.Replace(",\"RunHistory\":[]", "");
            ProjectSaveData restored = new ProjectSaveCodec().Decode(json);
            Assert.IsNotNull(restored.RunHistory);
            Assert.IsEmpty(restored.RunHistory);
            Assert.IsNull(ProjectSaveData.Validate(restored));
        }

        /// <summary>
        /// 同一结算候选记录战绩，旧已提交快照保持原状；重复追加不复制。
        /// </summary>
        [Test]
        public void SettlementCandidateIsolationAndRetryDeduplication()
        {
            ProjectSaveData committed = ProjectSaveData.CreateDefault();
            ProjectSaveData candidate = committed.Copy();
            Assert.IsTrue(ProjectRunRecordRules.TryAdmit(candidate, "history-run", 1, 17,
                new[] { candidate.Warehouse[0].Copy() }, out _));
            Assert.IsTrue(ProjectRunRecordRules.TryMarkRunning(candidate, "history-run", out _));
            var outcome = new ProjectRunOutcome
            {
                RunId = "history-run", LevelId = 1, Seed = 17, RulesVersion = ProjectRunRecordRules.RulesVersion,
                Extracted = true, Items = new List<ProjectItem> { candidate.EntryItems[0].Copy() }
            };
            Assert.IsTrue(ProjectRunRecordRules.TryRecordOutcome(candidate, outcome, out _));
            Assert.IsTrue(ProjectRunRecordRules.TrySettleOutcome(candidate, out _));
            Assert.IsEmpty(committed.RunHistory);
            Assert.AreEqual(1, candidate.RunHistory.Count);
            Assert.IsTrue(ProjectRunRecordRules.TrySettleOutcome(candidate, out _));
            Assert.AreEqual(1, candidate.RunHistory.Count);
            Assert.IsNull(ProjectSaveData.Validate(candidate));
        }

        /// <summary>
        /// 改小容量后的下一次追加仅保留最新记录。
        /// </summary>
        [Test]
        public void AppendTrimsOldestAfterLimitChanges()
        {
            var data = new ProjectSaveData();
            data.RunHistory.Add(Entry("one", 1, ProjectRunHistoryOutcome.Died, false));
            data.RunHistory.Add(Entry("two", 1, ProjectRunHistoryOutcome.Extracted, false));
            Assert.IsTrue(ProjectRunHistoryRules.Append(data, Result("three"), 1000, 2));
            Assert.AreEqual(2, data.RunHistory.Count);
            Assert.AreEqual("two", data.RunHistory[0].RunId);
            Assert.AreEqual("three", data.RunHistory[1].RunId);
            Assert.IsTrue(ProjectRunHistoryRules.Append(data, Result("four"), 1001, 1));
            Assert.AreEqual(1, data.RunHistory.Count);
            Assert.AreEqual("four", data.RunHistory[0].RunId);
        }

        /// <summary>
        /// 无效容量、空候选和损坏的历史不会修改候选。
        /// </summary>
        [Test]
        public void AppendRejectsInvalidBoundaryInputs()
        {
            var data = new ProjectSaveData();
            Assert.IsFalse(ProjectRunHistoryRules.Append(null, Result("one"), 1000, 1));
            Assert.IsFalse(ProjectRunHistoryRules.Append(data, Result("one"), 1000, -1));
            Assert.IsEmpty(data.RunHistory);
            data.RunHistory.Add(null);
            Assert.IsFalse(ProjectRunHistoryRules.Append(data, Result("one"), 1000, 1));
            Assert.AreEqual(1, data.RunHistory.Count);
        }

        /// <summary>
        /// 汇总分母包括死亡与放弃，资产均值只计算有入场基准的样本。
        /// </summary>
        [Test]
        public void MixedOutcomesUseOnlyEligibleNetSamples()
        {
            var entries = new List<ProjectRunHistoryEntry>
            {
                Entry("one", 2, ProjectRunHistoryOutcome.Extracted, true),
                Entry("two", 2, ProjectRunHistoryOutcome.Died, false),
                Entry("three", 2, ProjectRunHistoryOutcome.Abandoned, true)
            };
            List<ProjectRunHistorySummary> summaries = ProjectRunHistoryRules.Summarize(entries);
            Assert.AreEqual(1, summaries.Count);
            Assert.AreEqual(3, summaries[0].Count);
            Assert.AreEqual(1, summaries[0].ExtractedCount);
            Assert.AreEqual(2, summaries[0].NetAssetSampleCount);
            Assert.AreEqual(90, summaries[0].AverageElapsedSeconds);
            Assert.AreEqual(15, summaries[0].AverageNetAssetValue);
        }

        /// <summary>
        /// 拒绝重复身份、非有限或负数数值。
        /// </summary>
        [Test]
        public void InvalidHistoryIsRejected()
        {
            var entries = new List<ProjectRunHistoryEntry> { Entry("one", 1, ProjectRunHistoryOutcome.Extracted, true) };
            entries.Add(entries[0].Copy());
            Assert.IsNotNull(ProjectRunHistoryRules.Validate(entries));
            entries.RemoveAt(1);
            entries[0].ElapsedSeconds = float.NaN;
            Assert.IsNotNull(ProjectRunHistoryRules.Validate(entries));
            entries[0].ElapsedSeconds = 90;
            entries[0].RetainedSellValue = -1;
            Assert.IsNotNull(ProjectRunHistoryRules.Validate(entries));
            entries[0].RetainedSellValue = long.MaxValue;
            entries[0].EntrySellValue = long.MaxValue;
            entries[0].GoldSpent = int.MaxValue;
            Assert.IsNotNull(ProjectRunHistoryRules.Validate(entries));
        }

        /// <summary>
        /// 创建可计算或缺失资产基准的测试战绩。
        /// </summary>
        /// <param name="runId">单局身份。</param>
        /// <param name="levelId">关卡编号。</param>
        /// <param name="outcome">终局类别。</param>
        /// <param name="hasNet">是否存在资产基准。</param>
        /// <returns>有效测试战绩。</returns>
        private static ProjectRunHistoryEntry Entry(string runId, int levelId, ProjectRunHistoryOutcome outcome, bool hasNet) => new()
        {
            RunId = runId, LevelId = levelId, EndUtc = 1000, ElapsedSeconds = 90, Outcome = outcome,
            Statistics = hasNet ? new ProjectRunStatistics
            {
                Version = 1, TotalRooms = 2, VisitedRooms = 1, EnemyKills = 2, EntryItemCount = 1, EntryItemSellValue = 5
            } : null,
            RetainedSellValue = hasNet ? 20 : 0, EntrySellValue = hasNet ? 5 : 0,
            NetAssetValue = hasNet ? 15 : 0, HasNetAssetValue = hasNet
        };

        /// <summary>
        /// 创建最小结算回执。
        /// </summary>
        /// <param name="runId">单局身份。</param>
        /// <returns>具有终局事实的回执。</returns>
        private static ProjectRunResult Result(string runId) => new()
        {
            Outcome = new ProjectRunOutcome { RunId = runId, LevelId = 1, ElapsedSeconds = 90 }
        };
    }
}
