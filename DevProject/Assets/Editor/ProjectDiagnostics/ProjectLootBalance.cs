using System;
using System.Collections.Generic;
using System.Globalization;
using GameLogic.Project;
using GameLogic.Tables;

namespace MiniGame.Editor
{
    /// <summary>
    /// 掉落池中一个模板、品质或类型的理论概率与实际出现次数。
    /// </summary>
    [Serializable]
    public sealed class LootFrequency
    {
        public string id;
        public string name;
        public double expectedProbability;
        public int sampleCount;
    }

    /// <summary>
    /// 指定等级掉落池的理论售价与固定种子采样结果。
    /// </summary>
    [Serializable]
    public sealed class LootPoolEvaluation
    {
        public string poolId;
        public int level;
        public int seed;
        public int sampleCount;
        public double expectedSellGold;
        public int sampleMinSellGold;
        public double sampleAverageSellGold;
        public int sampleMaxSellGold;
        public LootFrequency[] definitions;
        public LootFrequency[] qualities;
        public LootFrequency[] types;
    }

    /// <summary>
    /// 根据已加载的游戏配置计算掉落经济理论值并调用真实规则采样。
    /// </summary>
    public static class ProjectLootBalance
    {
        private const int MaxSamples = 100000;

        /// <summary>
        /// 评估指定掉落池的模板、品质、类型及出售金币分布。
        /// </summary>
        /// <param name="poolId">掉落池身份。</param>
        /// <param name="level">掉落物品等级。</param>
        /// <param name="seed">采样随机种子。</param>
        /// <param name="sampleCount">实际生成次数。</param>
        /// <param name="result">成功时返回完整评估。</param>
        /// <param name="error">失败原因，成功时为空。</param>
        /// <returns>配置和每个样本都有效时为真。</returns>
        public static bool TryEvaluate(string poolId, int level, int seed, int sampleCount, out LootPoolEvaluation result, out string error)
        {
            result = null;
            error = null;
            if (string.IsNullOrWhiteSpace(poolId) || level < 1 || level > PrototypeRules.LegacySkillRankLimit ||
                sampleCount < 1 || sampleCount > MaxSamples)
            { error = "掉落评估参数无效：池身份、等级或样本数超出范围。"; return false; }
            if (GameTables.CfgLootPool == null || GameTables.CfgItemDefinitions == null || GameTables.CfgQualities == null ||
                GameTables.CfgLootQualities == null ||
                GameTables.CfgAffixes == null || GameTables.CfgAdvancedAffixes == null || GameTables.CfgCombatAugments == null ||
                GameTables.CfgProfile == null || ProjectRulesConfig.Profile == null)
            { error = "掉落评估所需配置表未加载。"; return false; }

            var definitionWeights = new Dictionary<string, long>(StringComparer.Ordinal);
            var definitions = new Dictionary<string, CfgItemDefinitionsRow>(StringComparer.Ordinal);
            var typeWeights = new Dictionary<int, long>();
            long totalWeight = 0;
            foreach (CfgLootPoolRow row in GameTables.CfgLootPool.GetAll())
            {
                if (row == null) { error = "掉落池包含空配置行。"; return false; }
                if (row.PoolID != poolId || level < row.MinLevel || level > row.MaxLevel || row.Weight <= 0) continue;
                if (string.IsNullOrWhiteSpace(row.DefinitionId))
                { error = "掉落模板引用为空：" + row.ID; return false; }
                CfgItemDefinitionsRow definition = ProjectRulesConfig.Item(row.DefinitionId);
                if (definition == null || string.IsNullOrWhiteSpace(definition.ID) ||
                    string.IsNullOrWhiteSpace(definition.Name) || !IsSupportedType(definition.Type) ||
                    definition.SellBase < 0 || definition.SellPerLevel < 0 || definition.SellRandomExclusive < 0 ||
                    definition.SellPerQuality < 0)
                { error = "掉落模板引用或字段无效：" + row.ID + "/" + row.DefinitionId; return false; }
                long maximumSell = (long)definition.SellBase + (long)level * definition.SellPerLevel +
                    Math.Max(0, definition.SellRandomExclusive - 1) + (long)(int)ProjectItemQuality.Artifact * definition.SellPerQuality;
                if (maximumSell > int.MaxValue)
                { error = "掉落模板售价超出整数范围：" + definition.ID; return false; }
                totalWeight += row.Weight;
                if (totalWeight > int.MaxValue)
                { error = "掉落池总权重超出整数范围：" + poolId; return false; }
                definitionWeights.TryGetValue(definition.ID, out long definitionWeight);
                definitionWeights[definition.ID] = definitionWeight + row.Weight;
                definitions[definition.ID] = definition;
                typeWeights.TryGetValue(definition.Type, out long typeWeight);
                typeWeights[definition.Type] = typeWeight + row.Weight;
            }
            if (totalWeight == 0) { error = "掉落池在等级 " + level + " 没有正权重条目：" + poolId; return false; }

            var qualityWeights = new Dictionary<int, long>();
            var qualityNames = new Dictionary<int, string>();
            long totalQualityWeight = 0;
            long weightedQualityIds = 0;
            int affixCount = GameTables.CfgAffixes.GetAll().Count;
            foreach (CfgLootQualitiesRow row in GameTables.CfgLootQualities.GetAll())
            {
                if (row == null) { error = "掉落池品质表包含空行。"; return false; }
                if (row.PoolID != poolId) continue;
                CfgQualitiesRow definition = ProjectRulesConfig.Quality((ProjectItemQuality)row.Quality);
                if (definition == null || !Enum.IsDefined(typeof(ProjectItemQuality), row.Quality) ||
                    string.IsNullOrWhiteSpace(definition.Name) || row.Weight < 0 ||
                    definition.AffixCount < 0 || definition.AffixCount > affixCount || qualityWeights.ContainsKey(row.Quality))
                { error = "掉落池品质配置字段、枚举或唯一性无效：" + poolId; return false; }
                qualityWeights.Add(row.Quality, row.Weight);
                qualityNames.Add(row.Quality, definition.Name);
                totalQualityWeight += row.Weight;
                weightedQualityIds += (long)row.Quality * row.Weight;
            }
            if (qualityWeights.Count != Enum.GetValues(typeof(ProjectItemQuality)).Length ||
                totalQualityWeight <= 0 || totalQualityWeight > int.MaxValue)
            { error = "掉落池品质配置不完整或权重无效：" + poolId; return false; }

            var definitionRows = new List<LootFrequency>();
            var qualityRows = new List<LootFrequency>();
            var typeRows = new List<LootFrequency>();
            double meanQuality = (double)weightedQualityIds / totalQualityWeight;
            double expectedSell = 0;
            var definitionIds = new List<string>(definitionWeights.Keys);
            definitionIds.Sort(StringComparer.Ordinal);
            foreach (string definitionId in definitionIds)
            {
                CfgItemDefinitionsRow definition = definitions[definitionId];
                double probability = (double)definitionWeights[definitionId] / totalWeight;
                double meanRandom = definition.SellRandomExclusive > 0 ? (definition.SellRandomExclusive - 1) / 2d : 0;
                expectedSell += probability * (definition.SellBase + (double)level * definition.SellPerLevel + meanRandom +
                    meanQuality * definition.SellPerQuality);
                definitionRows.Add(new LootFrequency { id = definitionId, name = definition.Name, expectedProbability = probability });
            }
            foreach (KeyValuePair<int, long> entry in qualityWeights)
                qualityRows.Add(new LootFrequency { id = entry.Key.ToString(CultureInfo.InvariantCulture), name = qualityNames[entry.Key],
                    expectedProbability = (double)entry.Value / totalQualityWeight });
            foreach (KeyValuePair<int, long> entry in typeWeights)
                typeRows.Add(new LootFrequency { id = entry.Key.ToString(CultureInfo.InvariantCulture), name = ((ProjectItemType)entry.Key).ToString(),
                    expectedProbability = (double)entry.Value / totalWeight });
            definitionRows.Sort((a, b) => string.CompareOrdinal(a.id, b.id));
            qualityRows.Sort((a, b) => string.CompareOrdinal(a.id, b.id));
            typeRows.Sort((a, b) => string.CompareOrdinal(a.id, b.id));

            var definitionSamples = new Dictionary<string, LootFrequency>(StringComparer.Ordinal);
            var qualitySamples = new Dictionary<int, LootFrequency>();
            var typeSamples = new Dictionary<int, LootFrequency>();
            foreach (LootFrequency frequency in definitionRows) definitionSamples.Add(frequency.id, frequency);
            foreach (LootFrequency frequency in qualityRows) qualitySamples.Add(int.Parse(frequency.id, CultureInfo.InvariantCulture), frequency);
            foreach (LootFrequency frequency in typeRows) typeSamples.Add(int.Parse(frequency.id, CultureInfo.InvariantCulture), frequency);
            var random = new Random(seed);
            long sampleTotal = 0;
            int sampleMin = int.MaxValue, sampleMax = int.MinValue;
            for (int index = 0; index < sampleCount; index++)
            {
                ProjectItem item = ProjectEquipmentRules.CreateLootFromPool(random, level, poolId);
                if (item == null || !definitionSamples.TryGetValue(item.DefinitionId ?? "", out LootFrequency definitionFrequency) ||
                    !qualitySamples.TryGetValue((int)item.Quality, out LootFrequency qualityFrequency) ||
                    !typeSamples.TryGetValue((int)item.Type, out LootFrequency typeFrequency) || item.SellGold < 0)
                { error = "掉落生成失败或返回未配置数据：" + poolId + "，样本 " + index; return false; }
                definitionFrequency.sampleCount++;
                qualityFrequency.sampleCount++;
                typeFrequency.sampleCount++;
                sampleTotal += item.SellGold;
                sampleMin = Math.Min(sampleMin, item.SellGold);
                sampleMax = Math.Max(sampleMax, item.SellGold);
            }
            result = new LootPoolEvaluation
            {
                poolId = poolId, level = level, seed = seed, sampleCount = sampleCount, expectedSellGold = expectedSell,
                sampleMinSellGold = sampleMin, sampleAverageSellGold = (double)sampleTotal / sampleCount,
                sampleMaxSellGold = sampleMax, definitions = definitionRows.ToArray(), qualities = qualityRows.ToArray(),
                types = typeRows.ToArray()
            };
            return true;
        }

        /// <summary>
        /// 判断配置分类是否能由掉落生成器创建。
        /// </summary>
        /// <param name="type">模板中的分类编号。</param>
        /// <returns>分类属于可生成的武器、护甲或普通物品时为真。</returns>
        private static bool IsSupportedType(int type) => type == (int)ProjectItemType.Weapon ||
            type == (int)ProjectItemType.Armor || type == (int)ProjectItemType.Item;
    }
}

