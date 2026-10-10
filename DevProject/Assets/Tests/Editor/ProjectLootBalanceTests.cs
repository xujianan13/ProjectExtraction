using System;
using System.Linq;
using GameLogic.Tables;
using MiniGame.Editor;
using NUnit.Framework;
using UnityEngine;

namespace ProjectExtraction.Tests
{
    /// <summary>
    /// 验证掉落经济报告的概率、售价和固定种子样本与正式配置一致。
    /// </summary>
    public sealed class ProjectLootBalanceTests
    {
        private static readonly string[] PoolIds = { "default", "treasure", "merchant", "boss" };

        /// <summary>
        /// 四个正式掉落池在三个关卡都生成归一概率及完整样本计数。
        /// </summary>
        [Test]
        public void AllPoolsAndLevelsHaveNormalizedProbabilitiesAndCompleteSamples()
        {
            foreach (string poolId in PoolIds)
                foreach (int level in new[] { 1, 3, 6 })
                {
                    Assert.That(ProjectLootBalance.TryEvaluate(poolId, level, 2026, 128, out LootPoolEvaluation result, out string error),
                        Is.True, poolId + "/" + level + ": " + error);
                    Assert.That(result.poolId, Is.EqualTo(poolId));
                    Assert.That(result.level, Is.EqualTo(level));
                    Assert.That(result.seed, Is.EqualTo(2026));
                    Assert.That(result.sampleCount, Is.EqualTo(128));
                    Assert.That(result.definitions.Sum(row => row.expectedProbability), Is.EqualTo(1).Within(1e-10));
                    Assert.That(result.qualities.Sum(row => row.expectedProbability), Is.EqualTo(1).Within(1e-10));
                    Assert.That(result.types.Sum(row => row.expectedProbability), Is.EqualTo(1).Within(1e-10));
                    Assert.That(result.definitions.Sum(row => row.sampleCount), Is.EqualTo(128));
                    Assert.That(result.qualities.Sum(row => row.sampleCount), Is.EqualTo(128));
                    Assert.That(result.types.Sum(row => row.sampleCount), Is.EqualTo(128));
                    Assert.That(result.sampleMinSellGold, Is.LessThanOrEqualTo(result.sampleAverageSellGold));
                    Assert.That(result.sampleAverageSellGold, Is.LessThanOrEqualTo(result.sampleMaxSellGold));
                }
        }

        /// <summary>
        /// 同一配置和种子得到可序列化的稳定报告，不暴露随机生成的物品身份。
        /// </summary>
        [Test]
        public void FixedSeedProducesIdenticalSerializableReport()
        {
            Assert.That(ProjectLootBalance.TryEvaluate("treasure", 2, 314159, 500, out LootPoolEvaluation first, out string error), Is.True, error);
            Assert.That(ProjectLootBalance.TryEvaluate("treasure", 2, 314159, 500, out LootPoolEvaluation second, out error), Is.True, error);
            string json = JsonUtility.ToJson(first);
            Assert.That(json, Is.EqualTo(JsonUtility.ToJson(second)));
            Assert.That(json, Does.Contain("expectedSellGold"));
            Assert.That(json, Does.Not.Contain("Guid").And.Not.Contain("guid"));
        }

        /// <summary>
        /// 以配置字段独立计算品质及模板均价，核对所有正式池的理论售价。
        /// </summary>
        [Test]
        public void ExpectedSellGoldMatchesIndependentCurrentTableCalculation()
        {
            CfgItemDefinitionsRow[] definitions = GameTables.CfgItemDefinitions.GetAll().Where(row => !row.StarterEquipped).ToArray();
            Assert.That(definitions.Length, Is.EqualTo(6));
            Assert.That(definitions.All(row => row.SellBase == 8 && row.SellPerLevel == 4 &&
                row.SellRandomExclusive == 8 && row.SellPerQuality == 6), Is.True);
            foreach (string poolId in PoolIds)
                foreach (int level in new[] { 1, 3, 6 })
                {
                    CfgLootQualitiesRow[] qualities = GameTables.CfgLootQualities.GetAll().Where(row => row.PoolID == poolId).ToArray();
                    double weightedQuality = qualities.Sum(row => (double)row.Quality * row.Weight) / qualities.Sum(row => row.Weight);
                    double expected = 8 + level * 4 + (8 - 1) / 2.0 + weightedQuality * 6;
                    Assert.That(ProjectLootBalance.TryEvaluate(poolId, level, 77, 64, out LootPoolEvaluation result, out string error),
                        Is.True, error);
                    Assert.That(result.expectedSellGold, Is.EqualTo(expected).Within(1e-10), poolId + "/" + level);
                }
        }

        /// <summary>
        /// 每个掉落池的品质分布使用各自权重。
        /// </summary>
        [Test]
        public void PoolsUseTheirOwnQualityWeights()
        {
            foreach (string poolId in PoolIds)
            {
                Assert.That(ProjectLootBalance.TryEvaluate(poolId, 3, 19, 64, out LootPoolEvaluation result, out string error),
                    Is.True, error);
                CfgLootQualitiesRow[] configured = GameTables.CfgLootQualities.GetAll().Where(row => row.PoolID == poolId).ToArray();
                double total = configured.Sum(row => row.Weight);
                foreach (CfgLootQualitiesRow row in configured)
                    Assert.That(result.qualities.Single(frequency => frequency.id == row.Quality.ToString()).expectedProbability,
                        Is.EqualTo(row.Weight / total).Within(1e-10), poolId + "/" + row.Quality);
            }
        }

        /// <summary>
        /// 拒绝空池、未知池、非法等级和非正样本量，且不给出伪报告。
        /// </summary>
        [Test]
        public void InvalidArgumentsAndMissingPoolAreRejected()
        {
            AssertRejected(null, 1, 1);
            AssertRejected(" ", 1, 1);
            AssertRejected("missing-pool", 1, 1);
            AssertRejected("default", 0, 1);
            AssertRejected("default", 1001, 1);
            AssertRejected("default", 1, 0);
            AssertRejected("default", 1, -1);
            CfgLootPoolRow invalid = GameTables.CfgLootPool.GetAll()
                .Single(row => row.PoolID == "default" && row.DefinitionId == "ruin_heavy_blade");
            string originalDefinitionId = invalid.DefinitionId;
            try
            {
                invalid.DefinitionId = null;
                AssertRejected("default", 1, 1);
            }
            finally
            {
                invalid.DefinitionId = originalDefinitionId;
            }
        }

        /// <summary>
        /// 等级区间剔除一项后，模板概率按剩余权重重新归一，恢复原配置字段。
        /// </summary>
        [Test]
        public void LevelFilterRenormalizesRemainingDefinitions()
        {
            CfgLootPoolRow restricted = GameTables.CfgLootPool.GetAll()
                .Single(row => row.PoolID == "default" && row.DefinitionId == "ruin_heavy_blade");
            int originalMaxLevel = restricted.MaxLevel;
            try
            {
                restricted.MaxLevel = 1;
                Assert.That(ProjectLootBalance.TryEvaluate("default", 1, 19, 100, out LootPoolEvaluation first, out string error),
                    Is.True, error);
                Assert.That(ProjectLootBalance.TryEvaluate("default", 2, 19, 100, out LootPoolEvaluation second, out error),
                    Is.True, error);
                int eligibleWeight = GameTables.CfgLootPool.GetAll().Where(row => row.PoolID == "default" &&
                    row.Weight > 0 && row.MinLevel <= 2 && row.MaxLevel >= 2).Sum(row => row.Weight);
                Assert.That(first.definitions.Single(row => row.id == restricted.DefinitionId).expectedProbability, Is.GreaterThan(0));
                Assert.That(second.definitions.Any(row => row.id == restricted.DefinitionId), Is.False);
                Assert.That(second.definitions.Single(row => row.id == "ruin_armor").expectedProbability,
                    Is.EqualTo(4.0 / eligibleWeight).Within(1e-10));
                Assert.That(second.definitions.Sum(row => row.sampleCount), Is.EqualTo(100));
            }
            finally
            {
                restricted.MaxLevel = originalMaxLevel;
            }
        }

        /// <summary>
        /// 同池重复模板合并权重，模板售价不同后理论均价仍按条目权重计算。
        /// </summary>
        [Test]
        public void DuplicateDefinitionCombinesWeightsAndUsesWeightedSellMean()
        {
            CfgLootPoolRow redirected = GameTables.CfgLootPool.GetAll()
                .Single(row => row.PoolID == "default" && row.DefinitionId == "ruin_heavy_blade");
            CfgItemDefinitionsRow expensive = GameTables.CfgItemDefinitions.GetById("ruin_weapon");
            string originalDefinitionId = redirected.DefinitionId;
            int originalSellBase = expensive.SellBase;
            try
            {
                redirected.DefinitionId = expensive.ID;
                expensive.SellBase = originalSellBase + 37;
                CfgLootPoolRow[] eligible = GameTables.CfgLootPool.GetAll()
                    .Where(row => row.PoolID == "default" && row.MinLevel <= 2 && row.MaxLevel >= 2 && row.Weight > 0).ToArray();
                int totalWeight = eligible.Sum(row => row.Weight);
                int duplicateWeight = eligible.Where(row => row.DefinitionId == expensive.ID).Sum(row => row.Weight);
                CfgLootQualitiesRow[] qualities = GameTables.CfgLootQualities.GetAll().Where(row => row.PoolID == "default").ToArray();
                double qualityMean = qualities.Sum(row => (double)row.Quality * row.Weight) / qualities.Sum(row => row.Weight);
                double expected = eligible.Sum(row =>
                {
                    CfgItemDefinitionsRow definition = GameTables.CfgItemDefinitions.GetById(row.DefinitionId);
                    double randomMean = definition.SellRandomExclusive > 0 ? (definition.SellRandomExclusive - 1) / 2.0 : 0;
                    return row.Weight * (definition.SellBase + 2.0 * definition.SellPerLevel +
                        randomMean + qualityMean * definition.SellPerQuality);
                }) / totalWeight;

                Assert.That(ProjectLootBalance.TryEvaluate("default", 2, 101, 300, out LootPoolEvaluation result, out string error),
                    Is.True, error);
                Assert.That(duplicateWeight, Is.EqualTo(2));
                Assert.That(result.definitions.Count(row => row.id == expensive.ID), Is.EqualTo(1));
                Assert.That(result.definitions.Length, Is.EqualTo(5));
                Assert.That(result.definitions.Single(row => row.id == expensive.ID).expectedProbability,
                    Is.EqualTo((double)duplicateWeight / totalWeight).Within(1e-10));
                Assert.That(result.expectedSellGold, Is.EqualTo(expected).Within(1e-10));
            }
            finally
            {
                redirected.DefinitionId = originalDefinitionId;
                expensive.SellBase = originalSellBase;
            }
        }

        /// <summary>
        /// 断言无效输入返回错误而不产生报告。
        /// </summary>
        /// <param name="poolId">候选掉落池。</param>
        /// <param name="level">候选等级。</param>
        /// <param name="sampleCount">候选样本数。</param>
        private static void AssertRejected(string poolId, int level, int sampleCount)
        {
            Assert.That(ProjectLootBalance.TryEvaluate(poolId, level, 1, sampleCount, out LootPoolEvaluation result, out string error), Is.False);
            Assert.That(result, Is.Null);
            Assert.That(error, Is.Not.Null.And.Not.Empty);
        }
    }
}