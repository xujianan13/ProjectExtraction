using System;
using System.Collections.Generic;

namespace GameLogic.Project
{
    /// <summary>
    /// 已保存单局的终局类别。
    /// </summary>
    public enum ProjectRunHistoryOutcome { Extracted = 0, Died = 1, Abandoned = 2, Completed = 3, AdExtraction = 4 }

    /// <summary>
    /// 单局战绩快照；不保留物品实例或可变配置引用。
    /// </summary>
    [Serializable]
    public sealed class ProjectRunHistoryEntry
    {
        public string RunId;
        public int LevelId;
        public long EndUtc;
        public float ElapsedSeconds;
        public ProjectRunHistoryOutcome Outcome;
        public ProjectRunStatistics Statistics;
        public int KillExperience;
        public int ExtractionExperience;
        public int GoldSpent;
        public long RetainedSellValue;
        public long LostSellValue;
        public long EntrySellValue;
        public long NetAssetValue;
        public bool HasNetAssetValue;

        /// <summary>
        /// 创建独立的战绩副本。
        /// </summary>
        /// <returns>独立战绩。</returns>
        public ProjectRunHistoryEntry Copy()
        {
            var copy = (ProjectRunHistoryEntry)MemberwiseClone();
            copy.Statistics = Statistics?.Copy();
            return copy;
        }
    }

    /// <summary>
    /// 最近保留战绩的关卡汇总。
    /// </summary>
    public sealed class ProjectRunHistorySummary
    {
        public int LevelId;
        public int Count;
        public int ExtractedCount;
        public int RegionStatsSampleCount;
        public int EarlyExtractionCount;
        public int CompletedCount;
        public int AdExtractionCount;
        public double AverageElapsedSeconds;
        public int NetAssetSampleCount;
        public double AverageNetAssetValue;
    }

    /// <summary>
    /// 单局战绩的快照、去重、裁剪与汇总规则。
    /// </summary>
    public static class ProjectRunHistoryRules
    {
        /// <summary>
        /// 从同一结算回执构建冻结战绩。
        /// </summary>
        /// <param name="result">已结算回执。</param>
        /// <param name="endUtc">结算时的 UTC 秒。</param>
        /// <returns>冻结的紧凑战绩。</returns>
        public static ProjectRunHistoryEntry Create(ProjectRunResult result, long endUtc)
        {
            ProjectRunOutcome outcome = result.Outcome;
            ProjectRunEconomySummary economy = ProjectRunEconomy.Calculate(result);
            return new ProjectRunHistoryEntry
            {
                RunId = outcome.RunId, LevelId = outcome.LevelId, EndUtc = endUtc, ElapsedSeconds = outcome.ElapsedSeconds,
                Outcome = outcome.ExitKind == ProjectRunExitKind.Completed ? ProjectRunHistoryOutcome.Completed :
                    outcome.ExitKind == ProjectRunExitKind.AdExtraction ? ProjectRunHistoryOutcome.AdExtraction :
                    outcome.ExitKind == ProjectRunExitKind.Abandoned ? ProjectRunHistoryOutcome.Abandoned : ProjectRunHistoryOutcome.Died,
                Statistics = outcome.Statistics?.HasData == true ? outcome.Statistics.Copy() : null,
                KillExperience = outcome.KillExperience, ExtractionExperience = outcome.ExtractionExperience,
                GoldSpent = outcome.GoldSpent, RetainedSellValue = economy.RetainedSellValue, LostSellValue = economy.LostSellValue,
                EntrySellValue = economy.EntrySellValue, NetAssetValue = economy.NetAssetValue, HasNetAssetValue = economy.HasEntrySnapshot
            };
        }

        /// <summary>
        /// 在已结算候选中追加一次，并按当前容量裁剪最旧记录。
        /// </summary>
        /// <param name="candidate">与收益共用的事务候选。</param>
        /// <param name="result">本局结算回执。</param>
        /// <param name="endUtc">结束 UTC 秒。</param>
        /// <param name="limit">配置中的保留上限。</param>
        /// <returns>是否接受本次追加或同一单局重试。</returns>
        public static bool Append(ProjectSaveData candidate, ProjectRunResult result, long endUtc, int limit)
        {
            if (candidate == null || result?.Outcome == null || !Enum.IsDefined(typeof(ProjectRunExitKind), result.Outcome.ExitKind) ||
                string.IsNullOrWhiteSpace(result.Outcome.RunId) ||
                endUtc < 0 || endUtc > 253402300799L || limit < 1 || limit > 100) return false;
            candidate.RunHistory ??= new List<ProjectRunHistoryEntry>();
            if (Validate(candidate.RunHistory) != null) return false;
            if (candidate.RunHistory.Exists(entry => entry.RunId == result.Outcome.RunId)) return true;
            ProjectRunHistoryEntry created = Create(result, endUtc);
            if (Validate(new[] { created }) != null) return false;
            candidate.RunHistory.Add(created);
            while (candidate.RunHistory.Count > limit) candidate.RunHistory.RemoveAt(0);
            return true;
        }

        /// <summary>
        /// 校验历史条目、有限数值与单局身份唯一性。
        /// </summary>
        /// <param name="entries">存档中的历史列表。</param>
        /// <returns>错误信息，合法时为空。</returns>
        public static string Validate(IReadOnlyList<ProjectRunHistoryEntry> entries)
        {
            if (entries == null) return null;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (ProjectRunHistoryEntry entry in entries)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.RunId) || !ids.Add(entry.RunId) ||
                    entry.LevelId < 1 || entry.EndUtc < 0 || entry.EndUtc > 253402300799L || float.IsNaN(entry.ElapsedSeconds) ||
                    float.IsInfinity(entry.ElapsedSeconds) || entry.ElapsedSeconds < 0 ||
                    !Enum.IsDefined(typeof(ProjectRunHistoryOutcome), entry.Outcome) ||
                    entry.KillExperience < 0 || entry.ExtractionExperience < 0 || entry.GoldSpent < 0 ||
                    entry.RetainedSellValue < 0 || entry.LostSellValue < 0 || entry.EntrySellValue < 0 ||
                    !ProjectRunStatistics.IsValid(entry.Statistics) ||
                    entry.Outcome == ProjectRunHistoryOutcome.Extracted && entry.Statistics?.IsFinalCompletion == true ||
                    entry.HasNetAssetValue && (entry.Statistics?.HasData != true ||
                        entry.EntrySellValue != entry.Statistics.EntryItemSellValue ||
                        entry.EntrySellValue > long.MaxValue - entry.GoldSpent ||
                        entry.NetAssetValue != entry.RetainedSellValue - (entry.EntrySellValue + entry.GoldSpent)) ||
                    !entry.HasNetAssetValue && (entry.EntrySellValue != 0 || entry.NetAssetValue != 0))
                    return "单局历史战绩无效。";
            }
            return null;
        }

        /// <summary>
        /// 只汇总当前保留的最近战绩；缺失入场基准者不进入资产均值。
        /// </summary>
        /// <param name="entries">保留的战绩列表。</param>
        /// <returns>按关卡编号排序的汇总。</returns>
        public static List<ProjectRunHistorySummary> Summarize(IReadOnlyList<ProjectRunHistoryEntry> entries)
        {
            var map = new SortedDictionary<int, ProjectRunHistorySummary>();
            var elapsed = new Dictionary<int, double>();
            var net = new Dictionary<int, double>();
            if (entries != null) foreach (ProjectRunHistoryEntry entry in entries)
            {
                if (entry == null) continue;
                if (!map.TryGetValue(entry.LevelId, out ProjectRunHistorySummary row))
                {
                    row = new ProjectRunHistorySummary { LevelId = entry.LevelId };
                    map.Add(entry.LevelId, row);
                }
                row.Count++;
                if (entry.Outcome == ProjectRunHistoryOutcome.Extracted || entry.Outcome == ProjectRunHistoryOutcome.Completed ||
                    entry.Outcome == ProjectRunHistoryOutcome.AdExtraction) row.ExtractedCount++;
                if (entry.Outcome == ProjectRunHistoryOutcome.Completed) row.CompletedCount++;
                if (entry.Outcome == ProjectRunHistoryOutcome.AdExtraction) row.AdExtractionCount++;
                if (entry.Statistics?.HasRegionData == true)
                {
                    row.RegionStatsSampleCount++;
                    if (entry.Outcome == ProjectRunHistoryOutcome.Extracted) row.EarlyExtractionCount++;
                }
                elapsed[entry.LevelId] = (elapsed.TryGetValue(entry.LevelId, out double priorElapsed) ? priorElapsed : 0) + entry.ElapsedSeconds;
                if (!entry.HasNetAssetValue) continue;
                row.NetAssetSampleCount++;
                net[entry.LevelId] = (net.TryGetValue(entry.LevelId, out double priorNet) ? priorNet : 0) + entry.NetAssetValue;
            }
            var summaries = new List<ProjectRunHistorySummary>();
            foreach (ProjectRunHistorySummary row in map.Values)
            {
                row.AverageElapsedSeconds = elapsed[row.LevelId] / row.Count;
                if (row.NetAssetSampleCount > 0) row.AverageNetAssetValue = net[row.LevelId] / row.NetAssetSampleCount;
                summaries.Add(row);
            }
            return summaries;
        }
    }
}
