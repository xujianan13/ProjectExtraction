using System;
using System.Collections.Generic;
using System.Linq;
using GameLogic.Gameplay.Level;
using GameLogic.Project;
using GameLogic.Tables;
using MiniGame.Editor;
using NUnit.Framework;
using ProjectExtraction.Tests.Shared;
using UnityEngine;

namespace GameLogic.Tests.Editor
{
    /// <summary>
    /// 验证 v2 混合物品池、收藏固定价值及区域静态指标。
    /// </summary>
    public sealed class ProjectLootBalanceTests
    {
        /// <summary>
        /// 为每项测试重新安装真实配置。
        /// </summary>
        [SetUp]
        public void LoadConfiguration() => TestGameTables.LoadAll();

        /// <summary>
        /// 恢复测试修改过的配置表。
        /// </summary>
        [TearDown]
        public void RestoreConfiguration() => TestGameTables.LoadAll();

        /// <summary>
        /// 真实配置全部池和物品等级的模板、品质及分类概率归一。
        /// </summary>
        [Test]
        public void AllConfiguredPoolsHaveNormalizedProbabilitiesAndSamples()
        {
            foreach (string poolId in GameTables.CfgLootPool.GetAll().Select(row => row.PoolID).Distinct())
                foreach (int level in GameTables.CfgLevels.GetAll().Select(row => row.LootLevel).Distinct())
                {
                    Assert.That(ProjectLootBalance.TryEvaluate(poolId, level, 2026, 128, out LootPoolEvaluation result, out string error), Is.True, error);
                    Assert.That(result.definitions.Sum(row => row.expectedProbability), Is.EqualTo(1d).Within(1e-10));
                    Assert.That(result.qualities.Sum(row => row.expectedProbability), Is.EqualTo(1d).Within(1e-10));
                    Assert.That(result.types.Sum(row => row.expectedProbability), Is.EqualTo(1d).Within(1e-10));
                    Assert.That(result.definitions.Sum(row => row.sampleCount), Is.EqualTo(128));
                    Assert.That(result.qualities.Sum(row => row.sampleCount), Is.EqualTo(128));
                    Assert.That(result.types.Sum(row => row.sampleCount), Is.EqualTo(128));
                }
        }

        /// <summary>
        /// 固定种子估值报告一致且不包含随机实例身份。
        /// </summary>
        [Test]
        public void FixedSeedProducesIdenticalReport()
        {
            string poolId = GameTables.CfgChestPool.GetAll()[0].LootPoolID;
            int level = GameTables.CfgLevels.GetAll()[0].LootLevel;
            Assert.That(ProjectLootBalance.TryEvaluate(poolId, level, 314159, 500, out var first, out string error), Is.True, error);
            Assert.That(ProjectLootBalance.TryEvaluate(poolId, level, 314159, 500, out var second, out error), Is.True, error);
            Assert.That(JsonUtility.ToJson(first), Is.EqualTo(JsonUtility.ToJson(second)));
        }

        /// <summary>
        /// 混合收藏池的理论均价独立按收藏固定价和其他物品品质权重计算。
        /// </summary>
        [Test]
        public void MixedCollectionPoolUsesFixedCollectionPriceAndQuality()
        {
            CfgLootPoolRow collectionEntry = GameTables.CfgLootPool.GetAll().First(row =>
                GameTables.CfgItemDefinitions.GetById(row.DefinitionId).Type == (int)ProjectItemType.Collection);
            string poolId = collectionEntry.PoolID;
            int level = Math.Max(collectionEntry.MinLevel, GameTables.CfgLevels.GetAll()[0].LootLevel);
            Assert.That(ProjectLootBalance.TryEvaluate(poolId, level, 77, 256, out var result, out string error), Is.True, error);
            Assert.That(result.expectedSellGold, Is.EqualTo(IndependentMean(poolId, level)).Within(1e-10));
            CfgCollectionsRow collection = GameTables.CfgCollections.GetById(GameTables.CfgItemDefinitions.GetById(collectionEntry.DefinitionId).CollectionID);
            double collectionProbability = result.definitions.Where(row =>
                GameTables.CfgItemDefinitions.GetById(row.id).Type == (int)ProjectItemType.Collection).Sum(row => row.expectedProbability);
            Assert.That(result.qualities.Single(row => row.id == collection.Quality.ToString()).expectedProbability, Is.GreaterThanOrEqualTo(collectionProbability));
        }

        /// <summary>
        /// 等级筛选去除模板后，剩余权重重新归一。
        /// </summary>
        [Test]
        public void LevelFilterRenormalizesEligibleDefinitions()
        {
            CfgLootPoolRow restricted = GameTables.CfgLootPool.GetAll().First();
            string definitionId = restricted.DefinitionId;
            restricted.MaxLevel = 1;
            Assert.That(ProjectLootBalance.TryEvaluate(restricted.PoolID, 2, 19, 128, out var result, out string error), Is.True, error);
            Assert.That(result.definitions.Any(row => row.id == definitionId), Is.False);
            Assert.That(result.expectedSellGold, Is.EqualTo(IndependentMean(restricted.PoolID, 2)).Within(1e-10));
        }

        /// <summary>
        /// 同池重复模板合并概率，不影响实际样本总数。
        /// </summary>
        [Test]
        public void DuplicateDefinitionsCombineWeights()
        {
            string poolId = GameTables.CfgLootPool.GetAll()[0].PoolID;
            CfgLootPoolRow[] rows = GameTables.CfgLootPool.GetAll().Where(row => row.PoolID == poolId).ToArray();
            rows[1].DefinitionId = rows[0].DefinitionId;
            Assert.That(ProjectLootBalance.TryEvaluate(poolId, 1, 101, 128, out var result, out string error), Is.True, error);
            int combined = rows.Where(row => row.DefinitionId == rows[0].DefinitionId).Sum(row => row.Weight);
            Assert.That(result.definitions.Single(row => row.id == rows[0].DefinitionId).expectedProbability,
                Is.EqualTo((double)combined / rows.Sum(row => row.Weight)).Within(1e-10));
            Assert.That(result.definitions.Sum(row => row.sampleCount), Is.EqualTo(128));
        }

        /// <summary>
        /// 区域报告按真实首次冻结的怪群、精英和 Boss 轮数计算。
        /// </summary>
        [Test]
        public void RegionEvaluationUsesGeneratedPlanAndConfiguredContributions()
        {
            LevelContentConfig.TryGetLevel(1, out LevelDefinition level);
            Assert.That(LevelContentConfig.TryCreateRegionRequests("balance-test", 7, level, out var requests, out string error), Is.True, error);
            var pools = new Dictionary<string, LootPoolEvaluation>();
            foreach (string id in GameTables.CfgLootPool.GetAll().Select(row => row.PoolID).Distinct())
            {
                Assert.That(ProjectLootBalance.TryEvaluate(id, level.LootLevel, 7, 64, out var value, out error), Is.True, error);
                pools.Add(id, value);
            }
            foreach (RegionGenerationRequest request in requests)
            {
                RegionMapDefinition map = ProjectRegionAssetsBuilder.CreateDefinition(request.MapId);
                Assert.That(RegionContentGenerator.TryGenerate(request, map, LevelContentConfig.CreateRegionContent(request), out var plan, out error), Is.True, error);
                Assert.That(ProjectBalanceReport.TryEvaluateRegion(plan, pools, out var result, out error), Is.True, error);
                CfgRogueProgressRow progress = GameTables.CfgRogueProgress.GetById(level.Row.ProgressRuleID);
                Assert.That(result.initialWeightedSkillProgress,
                    Is.EqualTo((long)result.normalEnemies * progress.NormalContribution + (long)result.eliteEnemies * progress.EliteContribution));
                Assert.That(result.bosses, Is.EqualTo(1));
                Assert.That(result.bossRewardRounds,
                    Is.EqualTo(GameTables.CfgBossRewards.GetById(GameTables.CfgBosses.GetById(request.BossDefinitionId).RewardID).Rounds));
                Assert.That(result.expectedMerchantNetGold, Is.EqualTo(result.expectedMerchantResaleGold - result.merchantPurchaseGold));
                if (request.RegionIndex == level.Row.RegionCount)
                    Assert.That(result.normalEnemies + result.eliteEnemies + result.chestItems + result.merchantOffers, Is.Zero);
            }
        }

        /// <summary>
        /// 无效参数和缺失模板不产生部分报告。
        /// </summary>
        [Test]
        public void InvalidArgumentsAndMissingDefinitionAreRejected()
        {
            AssertRejected(null, 1, 1);
            AssertRejected("missing-pool", 1, 1);
            AssertRejected(GameTables.CfgLootPool.GetAll()[0].PoolID, 0, 1);
            AssertRejected(GameTables.CfgLootPool.GetAll()[0].PoolID, 1, 0);
            CfgLootPoolRow row = GameTables.CfgLootPool.GetAll()[0];
            row.DefinitionId = "missing-definition";
            AssertRejected(row.PoolID, 1, 1);
        }

        /// <summary>
        /// 独立计算混合池的加权回收均价。
        /// </summary>
        /// <param name="poolId">物品池身份。</param>
        /// <param name="level">物品等级。</param>
        /// <returns>当前可用条目的加权均价。</returns>
        private static double IndependentMean(string poolId, int level)
        {
            CfgLootPoolRow[] rows = GameTables.CfgLootPool.GetAll().Where(row => row.PoolID == poolId && row.Weight > 0 &&
                row.MinLevel <= level && level <= row.MaxLevel).ToArray();
            CfgLootQualitiesRow[] qualities = GameTables.CfgLootQualities.GetAll().Where(row => row.PoolID == poolId).ToArray();
            double meanQuality = qualities.Sum(row => (double)row.Quality * row.Weight) / qualities.Sum(row => row.Weight);
            return rows.Sum(row =>
            {
                CfgItemDefinitionsRow definition = GameTables.CfgItemDefinitions.GetById(row.DefinitionId);
                double value = definition.Type == (int)ProjectItemType.Collection ? GameTables.CfgCollections.GetById(definition.CollectionID).SellGold :
                    definition.SellBase + (double)level * definition.SellPerLevel +
                    (definition.SellRandomExclusive > 0 ? (definition.SellRandomExclusive - 1) / 2d : 0d) + meanQuality * definition.SellPerQuality;
                return value * row.Weight;
            }) / rows.Sum(row => row.Weight);
        }

        /// <summary>
        /// 断言失败时返回明确错误且没有评估结果。
        /// </summary>
        /// <param name="poolId">池身份。</param>
        /// <param name="level">物品等级。</param>
        /// <param name="samples">样本数量。</param>
        private static void AssertRejected(string poolId, int level, int samples)
        {
            Assert.That(ProjectLootBalance.TryEvaluate(poolId, level, 1, samples, out var result, out string error), Is.False);
            Assert.That(result, Is.Null);
            Assert.That(error, Is.Not.Empty);
        }
    }
}
