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
    /// 从真实 V3 配置和固定种子房间计划计算初生战斗与物品经济的静态报告。
    /// </summary>
    public static class ProjectBalanceReport
    {
        private const int FirstSeed = 1;
        private const int LastSeed = 100;
        private const int LootSeed = 20261003;
        private const int LootSampleCount = 2048;
        private const string OutputDirectory = "verification/v3-room-balance";
        private const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags StaticProperties = BindingFlags.Static | BindingFlags.Public;
        private static readonly ILogModule EditorLog = CreateEditorLog();

        /// <summary>
        /// 单份真实区域计划的静态指标，不模拟玩家胜率或选择策略。
        /// </summary>
        [Serializable]
        public sealed class RegionEvaluation
        {
            public int regionIndex;
            public int mapId;
            public int bossId;
            public int enemyLevel;
            public int monsterGroups;
            public int normalEnemies;
            public int eliteEnemies;
            public int bosses;
            public double initialEnemyHealth;
            public long initialKillExperience;
            public int bossRewardItems;
            public int minimumEquipmentItems;
            public int minimumCollectionItems;
            public int chestItems;
            public double expectedChestResaleGold;
            public long frozenChestResaleGold;
            public int merchantOffers;
            public long merchantPurchaseGold;
            public double expectedMerchantResaleGold;
            public double expectedMerchantNetGold;
            public long frozenMerchantResaleGold;
        }

        [Serializable]
        private sealed class Sample
        {
            public int levelId;
            public int lootLevel;
            public int seed;
            public int regions;
            public int monsterGroups;
            public int normalEnemies;
            public int eliteEnemies;
            public int bosses;
            public double initialEnemyHealth;
            public long initialKillExperience;
            public int bossRewardItems;
            public int chestItems;
            public double expectedChestResaleGold;
            public long frozenChestResaleGold;
            public int merchantOffers;
            public long merchantPurchaseGold;
            public double expectedMerchantResaleGold;
            public double expectedMerchantNetGold;
            public RegionEvaluation[] regionSamples;
        }

        [Serializable]
        private sealed class MetricRange
        {
            public string metric;
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
            public int sampleCount;
            public MetricRange[] metrics;
        }

        [Serializable]
        private sealed class Report
        {
            public string scope = "V3配置与固定种子静态房间计划；初生全清、宝箱整箱领取、NPC商品全买并成功携出。未模拟胜率、耗时、死亡损失或玩家决策。";
            public string rewardScope = "普通和精英只计击杀经验；Boss单独计配置实际掉落件数及最低装备/收藏组成，不将最低组成当作额外物品。Boss物品售价未纳入普通宝箱或商店估值。";
            public string merchantScope = "箱子和NPC理论回收价来自当前等级物品池；冻结实例另列实际售价。NPC净回收为理论售价减买入成本，不假设玩家有足够金币。";
            public string configSha256;
            public int firstSeed = FirstSeed;
            public int lastSeed = LastSeed;
            public int lootSampleCount = LootSampleCount;
            public LootPoolEvaluation[] lootPools;
            public LevelSummary[] levels;
            public Sample[] samples;
        }

        /// <summary>
        /// 导出真实 V3 房间内容的 JSON 与 CSV 静态报告。
        /// </summary>
        [MenuItem("Tools/ProjectExtraction/Export Balance Report")]
        public static void Export()
        {
            ILogModule log = GameEntry.Get<ILogModule>() ?? EditorLog;
            if (!TryExport(out string error))
            {
                log.Error("ProjectBalanceReport", error);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }
            log.Info("ProjectBalanceReport", "已导出 " + OutputDirectory + "/balance-report.json 与 .csv");
        }

        /// <summary>
        /// 创建独立编辑器操作所需的日志接口。
        /// </summary>
        /// <returns>具有 Unity 输出接收器的日志模块。</returns>
        private static ILogModule CreateEditorLog()
        {
            var log = new LogModule();
            log.AddSink(new UnityConsoleSink());
            return log;
        }

        /// <summary>
        /// 安装已导出表集、生成真实计划并写报告，最后恢复编辑器原配置引用。
        /// </summary>
        /// <param name="error">首个失败原因。</param>
        /// <returns>完整报告已经写入时为真。</returns>
        public static bool TryExport(out string error)
        {
            if (EditorApplication.isPlaying) { error = "Play 模式不能导出报告。"; return false; }
            if (!TryLoadTables(out string hash, out var tables, out error)) return false;
            var previous = new Dictionary<PropertyInfo, object>();
            foreach (var entry in tables) previous.Add(entry.Key, entry.Key.GetValue(null));
            try
            {
                foreach (var entry in tables) entry.Key.GetSetMethod(true).Invoke(null, new[] { entry.Value });
                if (!ProjectContentConfig.Validate(out error)) return false;
                var report = new Report { configSha256 = hash };
                var samples = new List<Sample>();
                var summaries = new List<LevelSummary>();
                var lootPools = new List<LootPoolEvaluation>();
                var levels = new List<CfgLevelsRow>(GameTables.CfgLevels.GetAll());
                levels.Sort((a, b) => a.ID.CompareTo(b.ID));
                foreach (CfgLevelsRow row in levels)
                {
                    LevelContentConfig.TryGetLevel(row.ID, out LevelDefinition level);
                    if (!TryEvaluatePools(level.LootLevel, out var pools, out error)) return false;
                    lootPools.AddRange(pools.Values);
                    int start = samples.Count;
                    for (int seed = FirstSeed; seed <= LastSeed; seed++)
                    {
                        if (!LevelContentConfig.TryCreateRoomRequests("balance:" + row.ID + ":" + seed, seed, level, out var requests, out error)) return false;
                        var regions = new List<RegionEvaluation>();
                        foreach (RegionGenerationRequest request in requests)
                        {
                            RegionMapDefinition map = ProjectRegionAssetsBuilder.CreateDefinition(request.MapId);
                            if (map == null || !RegionContentGenerator.TryGenerate(request, map, LevelContentConfig.CreateRegionContent(request), out RegionPlan plan, out error))
                            { error = error ?? "地图构建清单不包含配置地图：" + request.MapId; return false; }
                            if (!TryEvaluateRegion(plan, pools, out RegionEvaluation region, out error)) return false;
                            regions.Add(region);
                        }
                        samples.Add(SummarizeRun(level, seed, regions));
                    }
                    summaries.Add(SummarizeLevel(row, samples, start));
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
                foreach (var entry in previous) entry.Key.GetSetMethod(true).Invoke(null, new[] { entry.Value });
            }
        }

        /// <summary>
        /// 对完整冻结房间计划计算配置期望与初生收益。
        /// </summary>
        /// <param name="plan">已验证的真实区域计划。</param>
        /// <param name="pools">当前物品等级的池估值。</param>
        /// <param name="result">区域指标。</param>
        /// <param name="error">失败原因。</param>
        /// <returns>全部物品池和业务引用完整时为真。</returns>
        public static bool TryEvaluateRegion(RegionPlan plan, IReadOnlyDictionary<string, LootPoolEvaluation> pools,
            out RegionEvaluation result, out string error)
        {
            result = null;
            error = null;
            if (plan == null || !plan.TryValidateStructure(out error) || pools == null) return false;
            CfgLevelsRow level = GameTables.CfgLevels.GetById(plan.Request.LevelId);
            bool hasBoss = plan.Request.RoomKind == ExpeditionRoomKind.Boss;
            CfgBossesRow boss = hasBoss ? GameTables.CfgBosses.GetById(plan.Request.BossDefinitionId) : null;
            CfgBossRewardsRow reward = hasBoss ? GameTables.CfgBossRewards.GetById(boss.RewardID) : null;
            var measured = new RegionEvaluation
            {
                regionIndex = plan.Request.RegionIndex, mapId = plan.Request.MapId, bossId = hasBoss ? boss.ID : 0,
                enemyLevel = plan.Request.EnemyLevel, bossRewardItems = hasBoss ? reward.ItemCount : 0,
                minimumEquipmentItems = hasBoss ? reward.MinEquipmentItems : 0,
                minimumCollectionItems = hasBoss ? reward.MinCollectionItems : 0
            };
            var groups = new HashSet<string>(StringComparer.Ordinal);
            foreach (RegionSpawnSlotPlan slot in plan.SpawnSlots)
            {
                LevelContentConfig.TryGetEnemy(slot.EnemyId, out EnemyDefinition enemy);
                measured.initialEnemyHealth += enemy.HealthAt(slot.EnemyLevel);
                measured.initialKillExperience += enemy.ExperienceAt(slot.EnemyLevel);
                if (slot.Kind == RegionEnemyKind.Boss) measured.bosses++;
                else if (slot.Kind == RegionEnemyKind.Elite) measured.eliteEnemies++;
                else { measured.normalEnemies++; groups.Add(slot.GroupId); }
            }
            measured.monsterGroups = groups.Count;
            foreach (RegionChestPlan chest in plan.Chests)
            {
                if (chest.Kind == RegionChestKind.BossReward) continue;
                CfgChestPoolRow row = GameTables.CfgChestPool.GetById(chest.DefinitionId);
                if (!pools.TryGetValue(row.LootPoolID, out LootPoolEvaluation pool)) { error = "宝箱物品池估值缺失：" + row.LootPoolID; return false; }
                measured.chestItems += chest.Items.Count;
                measured.expectedChestResaleGold += chest.Items.Count * pool.expectedSellGold;
                foreach (ProjectItem item in chest.Items) measured.frozenChestResaleGold += item.SellGold;
            }
            foreach (RegionNpcPlan npc in plan.Npcs)
            {
                CfgNpcPoolRow row = GameTables.CfgNpcPool.GetById(npc.DefinitionId);
                if (!pools.TryGetValue(row.LootPoolID, out LootPoolEvaluation pool)) { error = "商人物品池估值缺失：" + row.LootPoolID; return false; }
                measured.merchantOffers += npc.Offers.Count;
                measured.expectedMerchantResaleGold += npc.Offers.Count * pool.expectedSellGold;
                foreach (RegionNpcOfferPlan offer in npc.Offers)
                {
                    measured.merchantPurchaseGold += offer.Price;
                    measured.frozenMerchantResaleGold += offer.Item.SellGold;
                }
            }
            measured.expectedMerchantNetGold = measured.expectedMerchantResaleGold - measured.merchantPurchaseGold;
            result = measured;
            return true;
        }

        /// <summary>
        /// 合计一个局次的初生与冻结物品指标。
        /// </summary>
        /// <param name="level">关卡身份。</param>
        /// <param name="seed">固定局次种子。</param>
        /// <param name="regions">按请求顺序排列的区域。</param>
        /// <returns>整局静态样本。</returns>
        private static Sample SummarizeRun(LevelDefinition level, int seed, List<RegionEvaluation> regions)
        {
            var result = new Sample { levelId = level.ID, lootLevel = level.LootLevel, seed = seed, regions = regions.Count, regionSamples = regions.ToArray() };
            foreach (RegionEvaluation region in regions)
            {
                result.monsterGroups += region.monsterGroups;
                result.normalEnemies += region.normalEnemies;
                result.eliteEnemies += region.eliteEnemies;
                result.bosses += region.bosses;
                result.initialEnemyHealth += region.initialEnemyHealth;
                result.initialKillExperience += region.initialKillExperience;
                result.bossRewardItems += region.bossRewardItems;
                result.chestItems += region.chestItems;
                result.expectedChestResaleGold += region.expectedChestResaleGold;
                result.frozenChestResaleGold += region.frozenChestResaleGold;
                result.merchantOffers += region.merchantOffers;
                result.merchantPurchaseGold += region.merchantPurchaseGold;
                result.expectedMerchantResaleGold += region.expectedMerchantResaleGold;
                result.expectedMerchantNetGold += region.expectedMerchantNetGold;
            }
            return result;
        }

        /// <summary>
        /// 取得当前物品等级的全部正式物品池估值。
        /// </summary>
        /// <param name="level">物品等级。</param>
        /// <param name="pools">稳定排序的估值。</param>
        /// <param name="error">失败原因。</param>
        /// <returns>全部估值成功时为真。</returns>
        private static bool TryEvaluatePools(int level, out SortedDictionary<string, LootPoolEvaluation> pools, out string error)
        {
            pools = new SortedDictionary<string, LootPoolEvaluation>(StringComparer.Ordinal);
            var ids = new SortedSet<string>(StringComparer.Ordinal);
            foreach (CfgLootPoolRow row in GameTables.CfgLootPool.GetAll())
                if (row.Weight > 0 && level >= row.MinLevel && level <= row.MaxLevel) ids.Add(row.PoolID);
            foreach (string id in ids)
            {
                if (!ProjectLootBalance.TryEvaluate(id, level, LootSeed, LootSampleCount, out LootPoolEvaluation result, out error)) return false;
                pools.Add(id, result);
            }
            error = null;
            return true;
        }

        /// <summary>
        /// 汇总同关卡固定种子样本的全部数值指标。
        /// </summary>
        /// <param name="level">关卡行。</param>
        /// <param name="samples">已经生成的样本。</param>
        /// <param name="start">当前关卡首个样本。</param>
        /// <returns>每项指标的范围和均值。</returns>
        private static LevelSummary SummarizeLevel(CfgLevelsRow level, List<Sample> samples, int start)
        {
            var metrics = new List<MetricRange>();
            foreach (FieldInfo field in NumericFields(typeof(Sample)))
            {
                var metric = new MetricRange { metric = field.Name, min = double.MaxValue, max = double.MinValue };
                for (int index = start; index < samples.Count; index++)
                {
                    double value = Convert.ToDouble(field.GetValue(samples[index]), CultureInfo.InvariantCulture);
                    metric.min = Math.Min(metric.min, value);
                    metric.max = Math.Max(metric.max, value);
                    metric.avg += value;
                }
                metric.avg /= samples.Count - start;
                metrics.Add(metric);
            }
            return new LevelSummary { levelId = level.ID, lootLevel = level.LootLevel, name = level.Name, sampleCount = samples.Count - start, metrics = metrics.ToArray() };
        }

        /// <summary>
        /// 按名称稳定列出报告的数值字段。
        /// </summary>
        /// <param name="type">报告记录类型。</param>
        /// <returns>有限数值指标的字段清单。</returns>
        private static List<FieldInfo> NumericFields(Type type)
        {
            var result = new List<FieldInfo>();
            foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                if (field.FieldType == typeof(int) || field.FieldType == typeof(long) || field.FieldType == typeof(double)) result.Add(field);
            result.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            return result;
        }

        /// <summary>
        /// 导出整局与每区域的稳定数值 CSV。
        /// </summary>
        /// <param name="report">完整报告。</param>
        /// <returns>带配置哈希的 CSV。</returns>
        private static string BuildCsv(Report report)
        {
            var csv = new StringBuilder("section,levelId,seed,regionIndex,metric,min,avg,max,value,configSha256\n");
            foreach (LevelSummary level in report.levels)
                foreach (MetricRange metric in level.metrics)
                    csv.Append("summary,").Append(level.levelId).Append(",,,").Append(metric.metric).Append(',')
                        .Append(Number(metric.min)).Append(',').Append(Number(metric.avg)).Append(',').Append(Number(metric.max)).Append(",,").Append(report.configSha256).AppendLine();
            foreach (Sample sample in report.samples)
            {
                AppendMetrics(csv, "sample", sample.levelId, sample.seed, "", sample, report.configSha256);
                foreach (RegionEvaluation region in sample.regionSamples)
                    AppendMetrics(csv, "region", sample.levelId, sample.seed, region.regionIndex.ToString(CultureInfo.InvariantCulture), region, report.configSha256);
            }
            return csv.ToString();
        }

        /// <summary>
        /// 写入一份记录的所有数值字段。
        /// </summary>
        /// <param name="csv">CSV缓冲区。</param>
        /// <param name="section">记录类型。</param>
        /// <param name="levelId">关卡身份。</param>
        /// <param name="seed">固定种子。</param>
        /// <param name="regionIndex">区域序号文本。</param>
        /// <param name="record">指标记录。</param>
        /// <param name="hash">配置哈希。</param>
        private static void AppendMetrics(StringBuilder csv, string section, int levelId, int seed, string regionIndex, object record, string hash)
        {
            foreach (FieldInfo field in NumericFields(record.GetType()))
                csv.Append(section).Append(',').Append(levelId).Append(',').Append(seed).Append(',').Append(regionIndex).Append(',')
                    .Append(field.Name).Append(",,,,").Append(Convert.ToString(field.GetValue(record), CultureInfo.InvariantCulture)).Append(',').Append(hash).AppendLine();
        }

        /// <summary>
        /// 使用稳定区域性表示双精度数值。
        /// </summary>
        /// <param name="value">数值。</param>
        /// <returns>无精度损失的十进制文本。</returns>
        private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);

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
