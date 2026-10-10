using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using GameFramework;
using GameLogic.Gameplay.Level;
using GameLogic.Project;
using GameLogic.Tables;
using UnityEditor;
using UnityEngine;

namespace MiniGame.Editor
{
    /// <summary>
    /// 从已导出的真实配置和固定种子地图生成可重复的成长经济静态评估。
    /// </summary>
    public static class ProjectBalanceReport
    {
        private static readonly ILogModule EditorLog = CreateEditorLog();
        private const int FirstSeed = 1;
        private const int LastSeed = 100;
        private const int LootSeed = 20261003;
        private const int LootSampleCount = 2048;
        private const string OutputDirectory = "verification/run-statistics-balance";
        private const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags StaticProperties = BindingFlags.Static | BindingFlags.Public;

        [Serializable]
        private sealed class Sample
        {
            public int levelId;
            public int lootLevel;
            public int seed;
            public int rooms;
            public int enemies;
            public int bossRoomEnemies;
            public double enemyHealth;
            public long killExperience;
            public int extractionExperience;
            public double expectedEnemyLootCount;
            public double expectedEnemyLootSellGold;
            public int chestLootCount;
            public double expectedChestLootSellGold;
            public double expectedFreeLootSellGold;
            public int merchantOffers;
            public long merchantPurchaseGold;
            public double expectedMerchantResaleGold;
            public double expectedMerchantNetGold;
        }

        [Serializable]
        private sealed class Range
        {
            public double min;
            public double avg;
            public double max;
        }

        [Serializable]
        private sealed class LevelSummary
        {
            public int levelId;
            public int lootLevel;
            public string name;
            public int requiredLevel;
            public int seedCount;
            public int extractionExperience;
            public Range rooms;
            public Range enemies;
            public Range bossRoomEnemies;
            public Range enemyHealth;
            public Range killExperience;
            public Range expectedEnemyLootCount;
            public Range expectedEnemyLootSellGold;
            public Range chestLootCount;
            public Range expectedChestLootSellGold;
            public Range expectedFreeLootSellGold;
            public Range merchantOffers;
            public Range merchantPurchaseGold;
            public Range expectedMerchantResaleGold;
            public Range expectedMerchantNetGold;
        }

        [Serializable]
        private sealed class ShopEntry
        {
            public string id;
            public string name;
            public int requiredLevel;
            public int priceGold;
            public int stockLimit;
            public int sellGold;
            public int purchaseResaleGapGold;
        }

        [Serializable]
        private sealed class PlayerBaseline
        {
            public int startingLevel;
            public int startingExperience;
            public int startingGold;
            public int experienceBase;
            public int experiencePerLevel;
            public int entryStaminaCost;
            public int staminaCap;
            public double baseMaxHealth;
            public double unarmedAttack;
            public double baseAttackRange;
        }

        [Serializable]
        private sealed class Report
        {
            public string scope = "配置与固定种子地图采样；假设清理全部敌人、开启全部宝箱，所得物品全部拾取并成功携出。非胜率、耗时或实际收益模拟。";
            public string dropResaleValue = "按等级过滤的掉落池、品质权重和随机售价计算理论回收期望；另用真实物品生成入口固定种子采样。地图收益不含三选一物品、死亡、未拾取或入场装备损失。";
            public string merchantValue = "NPC 估值假设购买全图全部商品并成功携出出售；差额为理论回收期望减买入成本，未假设玩家具有足够金币。局外商品列出固定实例实际回收价。";
            public string configSha256;
            public int firstSeed = FirstSeed;
            public int lastSeed = LastSeed;
            public int seedCount = LastSeed - FirstSeed + 1;
            public PlayerBaseline player;
            public ShopEntry[] shop;
            public LootPoolEvaluation[] lootPools;
            public LevelSummary[] levels;
            public Sample[] samples;
        }

        /// <summary>
        /// 导出固定配置与地图种子的静态评估报告，可从菜单或批处理 executeMethod 调用。
        /// </summary>
        [MenuItem("Tools/ProjectExtraction/Export Balance Report")]
        public static void Export()
        {
            if (!TryExport(out string error))
            {
                (GameEntry.Get<ILogModule>() ?? EditorLog).Error("ProjectBalanceReport", error);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }
            (GameEntry.Get<ILogModule>() ?? EditorLog).Info("ProjectBalanceReport", "已导出 " + OutputDirectory + "/balance-report.json 与 .csv");
        }

        /// <summary>
        /// 创建供独立编辑器批处理使用的项目日志接口。
        /// </summary>
        /// <returns>带 Unity 控制台输出的日志模块。</returns>
        private static ILogModule CreateEditorLog()
        {
            var log = new LogModule();
            log.AddSink(new UnityConsoleSink());
            return log;
        }

        /// <summary>
        /// 读取配置并生成 JSON 与 CSV；遇到缺失或无效配置时返回明确失败。
        /// </summary>
        /// <param name="error">失败原因，成功时为空。</param>
        /// <returns>两份报告均已写入时为真。</returns>
        public static bool TryExport(out string error)
        {
            if (EditorApplication.isPlaying) { error = "Play 模式不能导出报告。"; return false; }
            if (!TryLoadTables(out string hash, out var tables, out error)) return false;
            var previous = new Dictionary<PropertyInfo, object>();
            foreach (KeyValuePair<PropertyInfo, object> entry in tables) previous.Add(entry.Key, entry.Key.GetValue(null));
            try
            {
                foreach (KeyValuePair<PropertyInfo, object> entry in tables) entry.Key.GetSetMethod(true).Invoke(null, new[] { entry.Value });
                if (!ProjectContentConfig.Validate(out error)) return false;

                var levels = new List<CfgLevelsRow>(GameTables.CfgLevels.GetAll());
                levels.Sort((a, b) => a.ID.CompareTo(b.ID));
                var shops = new List<CfgHomeShopRow>(GameTables.CfgHomeShop.GetAll());
                shops.Sort((a, b) => string.CompareOrdinal(a.ID, b.ID));
                CfgProfileRow profile = ProjectRulesConfig.Profile;
                var report = new Report
                {
                    configSha256 = hash,
                    player = new PlayerBaseline
                    {
                        startingLevel = profile.StartingLevel, startingExperience = profile.StartingExperience,
                        startingGold = profile.StartingGold, experienceBase = profile.ExperienceBase,
                        experiencePerLevel = profile.ExperiencePerLevel, entryStaminaCost = profile.EntryCost,
                        staminaCap = profile.StaminaCap, baseMaxHealth = profile.BaseMaxHealth,
                        unarmedAttack = profile.UnarmedAttack, baseAttackRange = profile.BaseAttackRange
                    }
                };
                var entries = new List<ShopEntry>();
                foreach (CfgHomeShopRow shop in shops)
                {
                    ProjectItem item = ProjectEquipmentRules.CreateConfiguredItem(new System.Random(shop.ItemSeed), shop.ItemLevel,
                        (ProjectItemQuality)shop.Quality, shop.DefinitionId);
                    if (item == null) { error = "局外商品无法生成：" + shop.ID; return false; }
                    entries.Add(new ShopEntry { id = shop.ID, name = shop.Name, requiredLevel = shop.RequiredLevel,
                        priceGold = shop.PriceGold, stockLimit = shop.StockLimit, sellGold = item.SellGold,
                        purchaseResaleGapGold = shop.PriceGold - item.SellGold });
                }
                report.shop = entries.ToArray();

                var samples = new List<Sample>();
                var summaries = new List<LevelSummary>();
                var lootPools = new List<LootPoolEvaluation>();
                foreach (CfgLevelsRow row in levels)
                {
                    if (!LevelContentConfig.TryGetLevel(row.ID, out LevelDefinition level))
                    { error = "关卡配置无法解析：" + row.ID; return false; }
                    if (!TryEvaluatePools(level.LootLevel, out var pools, out error)) return false;
                    lootPools.AddRange(pools.Values);
                    int start = samples.Count;
                    for (int seed = FirstSeed; seed <= LastSeed; seed++)
                    {
                        if (!LevelGenerator.TryGenerate(seed, level, out LevelRunState run))
                        { error = "地图生成失败：level=" + row.ID + ", seed=" + seed; return false; }
                        var sample = new Sample { levelId = row.ID, lootLevel = level.LootLevel, seed = seed, rooms = run.Rooms.Count,
                            extractionExperience = profile.ExtractionExperience };
                        foreach (LevelRoomState room in run.Rooms)
                        {
                            if (room.Plan == null)
                            { error = "房间计划缺失：level=" + row.ID + ", seed=" + seed + ", room=" + room.RoomId; return false; }
                            foreach (SpawnDefinition spawn in room.Plan.Spawns)
                            {
                                if (!LevelContentConfig.TryGetEnemy(spawn.EnemyID, out EnemyDefinition enemy))
                                { error = "敌人引用缺失：" + spawn.EnemyID; return false; }
                                sample.enemies += spawn.Count;
                                if (room.Type == LevelRoomType.Boss) sample.bossRoomEnemies += spawn.Count;
                                sample.enemyHealth += (double)enemy.HealthAt(row.ID) * spawn.Count;
                                sample.killExperience += (long)enemy.ExperienceAt(row.ID) * spawn.Count;
                                if (!pools.TryGetValue(enemy.DropPoolID, out LootPoolEvaluation dropPool))
                                { error = "敌人掉落池不覆盖关卡：" + enemy.DropPoolID + "/" + row.ID; return false; }
                                double expectedCount = (double)enemy.DropChance * spawn.Count;
                                sample.expectedEnemyLootCount += expectedCount;
                                sample.expectedEnemyLootSellGold += expectedCount * dropPool.expectedSellGold;
                            }
                            if (room.Type != LevelRoomType.Loot && room.Type != LevelRoomType.Npc) continue;
                            if (!pools.TryGetValue(room.Plan.LootPoolID, out LootPoolEvaluation roomPool))
                            { error = "房间物品池不覆盖关卡：" + room.Plan.LootPoolID + "/" + row.ID; return false; }
                            if (room.Type == LevelRoomType.Loot)
                            {
                                sample.chestLootCount++;
                                sample.expectedChestLootSellGold += roomPool.expectedSellGold;
                            }
                            else
                            {
                                sample.merchantOffers += room.Plan.NpcStock;
                                sample.merchantPurchaseGold += (long)room.Plan.NpcStock * room.Plan.NpcPrice;
                                sample.expectedMerchantResaleGold += room.Plan.NpcStock * roomPool.expectedSellGold;
                            }
                        }
                        sample.expectedFreeLootSellGold = sample.expectedEnemyLootSellGold + sample.expectedChestLootSellGold;
                        sample.expectedMerchantNetGold = sample.expectedMerchantResaleGold - sample.merchantPurchaseGold;
                        samples.Add(sample);
                    }
                    summaries.Add(Summarize(row, samples, start));
                }
                report.samples = samples.ToArray();
                report.levels = summaries.ToArray();
                report.lootPools = lootPools.ToArray();

                string root = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
                string directory = Path.Combine(root, OutputDirectory);
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, "balance-report.json"), JsonUtility.ToJson(report, true) + "\n", new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(directory, "balance-report.csv"), BuildCsv(report), new UTF8Encoding(false));
                error = null;
                return true;
            }
            finally
            {
                foreach (KeyValuePair<PropertyInfo, object> entry in previous) entry.Key.GetSetMethod(true).Invoke(null, new[] { entry.Value });
            }
        }

        /// <summary>
        /// 按稳定顺序评估当前关卡能够抽取的全部掉落池。
        /// </summary>
        /// <param name="level">掉落物品等级。</param>
        /// <param name="pools">按池编号排序的评估结果。</param>
        /// <param name="error">失败原因。</param>
        /// <returns>全部池评估成功时为真。</returns>
        private static bool TryEvaluatePools(int level, out SortedDictionary<string, LootPoolEvaluation> pools, out string error)
        {
            pools = new SortedDictionary<string, LootPoolEvaluation>(StringComparer.Ordinal);
            var ids = new SortedSet<string>(StringComparer.Ordinal);
            foreach (CfgLootPoolRow row in GameTables.CfgLootPool.GetAll())
                if (row.Weight > 0 && level >= row.MinLevel && level <= row.MaxLevel) ids.Add(row.PoolID);
            foreach (string id in ids)
            {
                if (!ProjectLootBalance.TryEvaluate(id, level, LootSeed, LootSampleCount, out LootPoolEvaluation result, out error))
                    return false;
                pools.Add(id, result);
            }
            error = null;
            return true;
        }

        /// <summary>
        /// 对同一关卡的固定种子样本计算逐项最小值、均值和最大值。
        /// </summary>
        /// <param name="row">关卡配置。</param>
        /// <param name="samples">全部已生成样本。</param>
        /// <param name="start">此关卡首个样本下标。</param>
        /// <returns>关卡统计摘要。</returns>
        private static LevelSummary Summarize(CfgLevelsRow row, List<Sample> samples, int start)
        {
            int count = samples.Count - start;
            return new LevelSummary
            {
                levelId = row.ID, lootLevel = row.LootLevel, name = row.Name, requiredLevel = row.RequiredLevel, seedCount = count,
                extractionExperience = ProjectRulesConfig.Profile.ExtractionExperience,
                rooms = Measure(samples, start, s => s.rooms), enemies = Measure(samples, start, s => s.enemies),
                bossRoomEnemies = Measure(samples, start, s => s.bossRoomEnemies), enemyHealth = Measure(samples, start, s => s.enemyHealth),
                killExperience = Measure(samples, start, s => s.killExperience),
                expectedEnemyLootCount = Measure(samples, start, s => s.expectedEnemyLootCount),
                expectedEnemyLootSellGold = Measure(samples, start, s => s.expectedEnemyLootSellGold),
                chestLootCount = Measure(samples, start, s => s.chestLootCount),
                expectedChestLootSellGold = Measure(samples, start, s => s.expectedChestLootSellGold),
                expectedFreeLootSellGold = Measure(samples, start, s => s.expectedFreeLootSellGold),
                merchantOffers = Measure(samples, start, s => s.merchantOffers),
                merchantPurchaseGold = Measure(samples, start, s => s.merchantPurchaseGold),
                expectedMerchantResaleGold = Measure(samples, start, s => s.expectedMerchantResaleGold),
                expectedMerchantNetGold = Measure(samples, start, s => s.expectedMerchantNetGold)
            };
        }

        /// <summary>
        /// 测量一项样本值的范围与算术平均值。
        /// </summary>
        /// <param name="samples">全部样本。</param>
        /// <param name="start">当前关卡起始下标。</param>
        /// <param name="selector">指标读取函数。</param>
        /// <returns>最小值、平均值和最大值。</returns>
        private static Range Measure(List<Sample> samples, int start, Func<Sample, double> selector)
        {
            double min = double.MaxValue, max = double.MinValue, total = 0;
            int count = samples.Count - start;
            for (int i = start; i < samples.Count; i++)
            {
                double value = selector(samples[i]);
                min = Math.Min(min, value);
                max = Math.Max(max, value);
                total += value;
            }
            return new Range { min = min, avg = total / count, max = max };
        }

        /// <summary>
        /// 生成含摘要、样本、商店和玩家基线的稳定 CSV。
        /// </summary>
        /// <param name="report">完整报告。</param>
        /// <returns>UTF-8 CSV 文本。</returns>
        private static string BuildCsv(Report report)
        {
            var csv = new StringBuilder();
            csv.AppendLine("section,levelId,seed,name,metric,min,avg,max,value,configSha256");
            foreach (LevelSummary level in report.levels)
            {
                AppendRange(csv, report.configSha256, level, "rooms", level.rooms);
                AppendRange(csv, report.configSha256, level, "enemies", level.enemies);
                AppendRange(csv, report.configSha256, level, "bossRoomEnemies", level.bossRoomEnemies);
                AppendRange(csv, report.configSha256, level, "enemyHealth", level.enemyHealth);
                AppendRange(csv, report.configSha256, level, "killExperience", level.killExperience);
                AppendRange(csv, report.configSha256, level, "expectedEnemyLootCount", level.expectedEnemyLootCount);
                AppendRange(csv, report.configSha256, level, "expectedEnemyLootSellGold", level.expectedEnemyLootSellGold);
                AppendRange(csv, report.configSha256, level, "chestLootCount", level.chestLootCount);
                AppendRange(csv, report.configSha256, level, "expectedChestLootSellGold", level.expectedChestLootSellGold);
                AppendRange(csv, report.configSha256, level, "expectedFreeLootSellGold", level.expectedFreeLootSellGold);
                AppendRange(csv, report.configSha256, level, "merchantOffers", level.merchantOffers);
                AppendRange(csv, report.configSha256, level, "merchantPurchaseGold", level.merchantPurchaseGold);
                AppendRange(csv, report.configSha256, level, "expectedMerchantResaleGold", level.expectedMerchantResaleGold);
                AppendRange(csv, report.configSha256, level, "expectedMerchantNetGold", level.expectedMerchantNetGold);
                AppendValue(csv, "summary", level.levelId, "", level.name, "extractionExperience", level.extractionExperience, report.configSha256);
                AppendValue(csv, "summary", level.levelId, "", level.name, "requiredLevel", level.requiredLevel, report.configSha256);
                AppendValue(csv, "summary", level.levelId, "", level.name, "lootLevel", level.lootLevel, report.configSha256);
            }
            foreach (Sample sample in report.samples)
            {
                string seed = sample.seed.ToString(CultureInfo.InvariantCulture);
                AppendValue(csv, "sample", sample.levelId, seed, "", "lootLevel", sample.lootLevel, report.configSha256);
                AppendValue(csv, "sample", sample.levelId, seed, "", "rooms", sample.rooms, report.configSha256);
                AppendValue(csv, "sample", sample.levelId, seed, "", "enemies", sample.enemies, report.configSha256);
                AppendValue(csv, "sample", sample.levelId, seed, "", "bossRoomEnemies", sample.bossRoomEnemies, report.configSha256);
                AppendValue(csv, "sample", sample.levelId, seed, "", "enemyHealth", sample.enemyHealth, report.configSha256);
                AppendValue(csv, "sample", sample.levelId, seed, "", "killExperience", sample.killExperience, report.configSha256);
                AppendValue(csv, "sample", sample.levelId, seed, "", "extractionExperience", sample.extractionExperience, report.configSha256);
                AppendValue(csv, "sample", sample.levelId, seed, "", "expectedEnemyLootCount", sample.expectedEnemyLootCount, report.configSha256);
                AppendValue(csv, "sample", sample.levelId, seed, "", "expectedEnemyLootSellGold", sample.expectedEnemyLootSellGold, report.configSha256);
                AppendValue(csv, "sample", sample.levelId, seed, "", "chestLootCount", sample.chestLootCount, report.configSha256);
                AppendValue(csv, "sample", sample.levelId, seed, "", "expectedChestLootSellGold", sample.expectedChestLootSellGold, report.configSha256);
                AppendValue(csv, "sample", sample.levelId, seed, "", "expectedFreeLootSellGold", sample.expectedFreeLootSellGold, report.configSha256);
                AppendValue(csv, "sample", sample.levelId, seed, "", "merchantOffers", sample.merchantOffers, report.configSha256);
                AppendValue(csv, "sample", sample.levelId, seed, "", "merchantPurchaseGold", sample.merchantPurchaseGold, report.configSha256);
                AppendValue(csv, "sample", sample.levelId, seed, "", "expectedMerchantResaleGold", sample.expectedMerchantResaleGold, report.configSha256);
                AppendValue(csv, "sample", sample.levelId, seed, "", "expectedMerchantNetGold", sample.expectedMerchantNetGold, report.configSha256);
            }
            foreach (LootPoolEvaluation pool in report.lootPools)
            {
                string seed = pool.seed.ToString(CultureInfo.InvariantCulture);
                AppendValue(csv, "lootPool", pool.level, seed, pool.poolId, "sampleCount", pool.sampleCount, report.configSha256);
                AppendValue(csv, "lootPool", pool.level, seed, pool.poolId, "expectedSellGold", pool.expectedSellGold, report.configSha256);
                AppendValue(csv, "lootPool", pool.level, seed, pool.poolId, "sampleMinSellGold", pool.sampleMinSellGold, report.configSha256);
                AppendValue(csv, "lootPool", pool.level, seed, pool.poolId, "sampleAverageSellGold", pool.sampleAverageSellGold, report.configSha256);
                AppendValue(csv, "lootPool", pool.level, seed, pool.poolId, "sampleMaxSellGold", pool.sampleMaxSellGold, report.configSha256);
                AppendFrequencies(csv, report.configSha256, pool, "definition", pool.definitions);
                AppendFrequencies(csv, report.configSha256, pool, "quality", pool.qualities);
                AppendFrequencies(csv, report.configSha256, pool, "type", pool.types);
            }
            foreach (ShopEntry shop in report.shop)
            {
                AppendValue(csv, "shop", 0, "", shop.id + ": " + shop.name, "priceGold", shop.priceGold, report.configSha256);
                AppendValue(csv, "shop", 0, "", shop.id + ": " + shop.name, "requiredLevel", shop.requiredLevel, report.configSha256);
                AppendValue(csv, "shop", 0, "", shop.id + ": " + shop.name, "stockLimit", shop.stockLimit, report.configSha256);
                AppendValue(csv, "shop", 0, "", shop.id + ": " + shop.name, "sellGold", shop.sellGold, report.configSha256);
                AppendValue(csv, "shop", 0, "", shop.id + ": " + shop.name, "purchaseResaleGapGold", shop.purchaseResaleGapGold, report.configSha256);
            }
            AppendValue(csv, "player", 0, "", "", "startingGold", report.player.startingGold, report.configSha256);
            AppendValue(csv, "player", 0, "", "", "startingLevel", report.player.startingLevel, report.configSha256);
            AppendValue(csv, "player", 0, "", "", "experienceBase", report.player.experienceBase, report.configSha256);
            AppendValue(csv, "player", 0, "", "", "experiencePerLevel", report.player.experiencePerLevel, report.configSha256);
            AppendValue(csv, "player", 0, "", "", "entryStaminaCost", report.player.entryStaminaCost, report.configSha256);
            AppendValue(csv, "player", 0, "", "", "baseMaxHealth", report.player.baseMaxHealth, report.configSha256);
            AppendValue(csv, "player", 0, "", "", "unarmedAttack", report.player.unarmedAttack, report.configSha256);
            AppendValue(csv, "scope", 0, "", "", "description", report.scope, report.configSha256);
            AppendValue(csv, "scope", 0, "", "", "dropResaleValue", report.dropResaleValue, report.configSha256);
            AppendValue(csv, "scope", 0, "", "", "merchantValue", report.merchantValue, report.configSha256);
            return csv.ToString();
        }

        /// <summary>
        /// 向 CSV 写入物品模板、品质或类型的理论概率与实际采样次数。
        /// </summary>
        /// <param name="csv">输出缓冲区。</param>
        /// <param name="hash">配置哈希。</param>
        /// <param name="pool">掉落池评估。</param>
        /// <param name="category">分布类别。</param>
        /// <param name="frequencies">稳定排序的分布项。</param>
        private static void AppendFrequencies(StringBuilder csv, string hash, LootPoolEvaluation pool, string category, LootFrequency[] frequencies)
        {
            string seed = pool.seed.ToString(CultureInfo.InvariantCulture);
            foreach (LootFrequency frequency in frequencies)
            {
                string name = pool.poolId + "/" + frequency.id + ": " + frequency.name;
                AppendValue(csv, "loot-" + category, pool.level, seed, name, "expectedProbability", frequency.expectedProbability, hash);
                AppendValue(csv, "loot-" + category, pool.level, seed, name, "sampleCount", frequency.sampleCount, hash);
            }
        }

        /// <summary>
        /// 向 CSV 添加一项摘要范围。
        /// </summary>
        /// <param name="csv">输出缓冲区。</param>
        /// <param name="hash">配置哈希。</param>
        /// <param name="level">关卡摘要。</param>
        /// <param name="metric">指标名称。</param>
        /// <param name="range">指标范围。</param>
        private static void AppendRange(StringBuilder csv, string hash, LevelSummary level, string metric, Range range)
        {
            csv.Append("summary,").Append(level.levelId).Append(",,").Append(Escape(level.name)).Append(',').Append(metric).Append(',')
                .Append(Number(range.min)).Append(',').Append(Number(range.avg)).Append(',').Append(Number(range.max))
                .Append(",,").Append(hash).AppendLine();
        }

        /// <summary>
        /// 向 CSV 添加单值记录。
        /// </summary>
        /// <param name="csv">输出缓冲区。</param>
        /// <param name="section">记录类别。</param>
        /// <param name="levelId">关卡编号，无关时为零。</param>
        /// <param name="seed">种子文本。</param>
        /// <param name="name">显示名称。</param>
        /// <param name="metric">指标名称。</param>
        /// <param name="value">指标值。</param>
        /// <param name="hash">配置哈希。</param>
        private static void AppendValue(StringBuilder csv, string section, int levelId, string seed, string name,
            string metric, object value, string hash)
        {
            csv.Append(section).Append(',').Append(levelId).Append(',').Append(seed).Append(',').Append(Escape(name)).Append(',')
                .Append(metric).Append(",,,,").Append(Escape(Convert.ToString(value, CultureInfo.InvariantCulture)))
                .Append(',').Append(hash).AppendLine();
        }

        /// <summary>
        /// 使用固定区域性表示数字。
        /// </summary>
        /// <param name="value">数值。</param>
        /// <returns>十进制文本。</returns>
        private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);

        /// <summary>
        /// 转义 CSV 文本字段。
        /// </summary>
        /// <param name="value">原文本。</param>
        /// <returns>可写入 CSV 的字段。</returns>
        private static string Escape(string value) => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";

        /// <summary>
        /// 以生成表结构加载已导出的 JSON，并计算稳定内容哈希。
        /// </summary>
        /// <param name="hash">全部表 JSON 的 SHA-256。</param>
        /// <param name="tables">完成构造但尚未安装的配置表。</param>
        /// <param name="error">失败原因。</param>
        /// <returns>全部表加载成功时为真。</returns>
        private static bool TryLoadTables(out string hash, out Dictionary<PropertyInfo, object> tables, out string error)
        {
            hash = null;
            tables = new Dictionary<PropertyInfo, object>();
            string directory = Path.Combine(Application.dataPath, "GameResources", "Config", "Tables");
            PropertyInfo[] properties = typeof(GameTables).GetProperties(StaticProperties);
            Array.Sort(properties, (a, b) => string.CompareOrdinal(a.Name, b.Name));
            using (SHA256 sha = SHA256.Create())
            using (var content = new MemoryStream())
            {
                int loaded = 0;
                foreach (PropertyInfo property in properties)
                {
                    Type tableType = property.PropertyType;
                    if (!tableType.Name.EndsWith("Table", StringComparison.Ordinal) ||
                        tableType.GetMethod("GetAll", BindingFlags.Instance | BindingFlags.Public) == null) continue;
                    string path = Path.Combine(directory, "Table_" + property.Name + ".json");
                    if (!File.Exists(path)) { error = "配置表 JSON 不存在：" + path; return false; }
                    Type envelopeType = tableType.GetNestedType("Envelope", BindingFlags.NonPublic);
                    FieldInfo rowsField = tableType.GetField("rows", InstanceFields);
                    FieldInfo byIdField = tableType.GetField("byId", InstanceFields);
                    FieldInfo envelopeRows = envelopeType?.GetField("rows", InstanceFields);
                    MethodInfo setter = property.GetSetMethod(true);
                    if (envelopeType == null || rowsField == null || byIdField == null || envelopeRows == null || setter == null)
                    { error = "配置表生成结构不匹配：" + property.Name; return false; }
                    byte[] bytes = File.ReadAllBytes(path);
                    byte[] nameBytes = Encoding.UTF8.GetBytes(property.Name + "\n");
                    content.Write(nameBytes, 0, nameBytes.Length);
                    content.Write(bytes, 0, bytes.Length);
                    content.WriteByte(0);
                    object envelope = JsonUtility.FromJson(Encoding.UTF8.GetString(bytes), envelopeType);
                    Array rows = envelope == null ? null : envelopeRows.GetValue(envelope) as Array;
                    if (rows == null) { error = "配置表缺少 rows：" + property.Name; return false; }
                    object table = Activator.CreateInstance(tableType);
                    rowsField.SetValue(table, rows);
                    var byId = byIdField.GetValue(table) as IDictionary;
                    FieldInfo idField = rows.GetType().GetElementType()?.GetField("ID", BindingFlags.Instance | BindingFlags.Public);
                    if (byId == null || idField == null) { error = "配置表 ID 结构不匹配：" + property.Name; return false; }
                    foreach (object row in rows)
                    {
                        if (row == null) { error = "配置表包含空行：" + property.Name; return false; }
                        object id = idField.GetValue(row);
                        if (byId.Contains(id)) { error = "配置表 ID 重复：" + property.Name + "/" + id; return false; }
                        byId.Add(id, row);
                    }
                    tables.Add(property, table);
                    loaded++;
                }
                if (loaded == 0) { error = "未发现已导出的配置表。"; return false; }
                byte[] digest = sha.ComputeHash(content.ToArray());
                var hex = new StringBuilder(digest.Length * 2);
                foreach (byte value in digest) hex.Append(value.ToString("x2", CultureInfo.InvariantCulture));
                hash = hex.ToString();
            }
            error = null;
            return true;
        }
    }
}
