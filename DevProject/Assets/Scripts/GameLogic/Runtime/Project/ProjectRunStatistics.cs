using System;
using System.Collections.Generic;

namespace GameLogic.Project
{
    /// <summary>
    /// 单局冻结统计；版本零表示旧档未记录统计。
    /// </summary>
    [Serializable]
    public sealed class ProjectRunStatistics
    {
        public const int CurrentVersion = 1;

        public int Version;
        public int TotalRooms;
        public int VisitedRooms;
        public int ClearedCombatRooms;
        public int EnemyKills;
        public int BossRoomKills;
        public int RogueChoices;
        public int EntryItemCount;
        public long EntryItemSellValue;
        public double DamageDealt;
        public double DamageTaken;
        public double HealingReceived;

        public bool HasData => Version == CurrentVersion;

        /// <summary>
        /// 创建不共享引用的统计副本。
        /// </summary>
        /// <returns>独立统计副本。</returns>
        public ProjectRunStatistics Copy() => (ProjectRunStatistics)MemberwiseClone();

        /// <summary>
        /// 校验统计版本、计数和有限数值；空记录与全零旧记录表示无统计。
        /// </summary>
        /// <param name="statistics">待校验的统计。</param>
        /// <returns>统计是否有效。</returns>
        public static bool IsValid(ProjectRunStatistics statistics)
        {
            if (statistics == null) return true;
            if (statistics.Version == 0)
                return statistics.TotalRooms == 0 && statistics.VisitedRooms == 0 && statistics.ClearedCombatRooms == 0 &&
                    statistics.EnemyKills == 0 && statistics.BossRoomKills == 0 && statistics.RogueChoices == 0 &&
                    statistics.EntryItemCount == 0 && statistics.EntryItemSellValue == 0 && statistics.DamageDealt == 0 &&
                    statistics.DamageTaken == 0 && statistics.HealingReceived == 0;
            if (statistics.Version != CurrentVersion || statistics.TotalRooms < 1 || statistics.VisitedRooms < 1 ||
                statistics.VisitedRooms > statistics.TotalRooms || statistics.ClearedCombatRooms < 0 ||
                statistics.ClearedCombatRooms > statistics.VisitedRooms || statistics.EnemyKills < 0 ||
                statistics.BossRoomKills < 0 || statistics.BossRoomKills > statistics.EnemyKills ||
                statistics.RogueChoices < 0 || statistics.EntryItemCount < 0 || statistics.EntryItemSellValue < 0 ||
                statistics.EntryItemSellValue > (long)statistics.EntryItemCount * int.MaxValue) return false;
            return IsFiniteNonnegative(statistics.DamageDealt) && IsFiniteNonnegative(statistics.DamageTaken) &&
                IsFiniteNonnegative(statistics.HealingReceived);
        }

        /// <summary>
        /// 校验测量数值为有限非负数。
        /// </summary>
        /// <param name="value">待校验数值。</param>
        /// <returns>数值是否有效。</returns>
        private static bool IsFiniteNonnegative(double value) => !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0;
    }

    /// <summary>
    /// 已结算物品的回收估值及入场快照对比。
    /// </summary>
    public readonly struct ProjectRunEconomySummary
    {
        public long RetainedSellValue { get; }
        public long LostSellValue { get; }
        public long EntrySellValue { get; }
        public long NetAssetValue { get; }
        public bool HasEntrySnapshot { get; }

        /// <summary>
        /// 创建只读收益明细。
        /// </summary>
        /// <param name="retainedSellValue">保留物品回收估值。</param>
        /// <param name="lostSellValue">丢失物品回收估值。</param>
        /// <param name="entrySellValue">入场物品回收估值。</param>
        /// <param name="netAssetValue">扣除入场估值及金币支出的净资产变化。</param>
        /// <param name="hasEntrySnapshot">是否记录入场快照。</param>
        public ProjectRunEconomySummary(long retainedSellValue, long lostSellValue, long entrySellValue, long netAssetValue, bool hasEntrySnapshot)
        {
            RetainedSellValue = retainedSellValue;
            LostSellValue = lostSellValue;
            EntrySellValue = entrySellValue;
            NetAssetValue = netAssetValue;
            HasEntrySnapshot = hasEntrySnapshot;
        }
    }

    /// <summary>
    /// 依据冻结结果计算只读回收估值。
    /// </summary>
    public static class ProjectRunEconomy
    {
        /// <summary>
        /// 计算保留、丢失和净资产估值，不修改钱包或结算。
        /// </summary>
        /// <param name="result">已结算结果。</param>
        /// <returns>收益明细；旧记录没有可用的净资产对比。</returns>
        public static ProjectRunEconomySummary Calculate(ProjectRunResult result)
        {
            long retained = SumSellValue(result?.RetainedItems);
            long lost = SumSellValue(result?.LostItems);
            bool hasEntry = result?.Outcome?.Statistics?.HasData == true;
            long entry = hasEntry ? result.Outcome.Statistics.EntryItemSellValue : 0;
            long net = hasEntry ? retained - entry - result.Outcome.GoldSpent : 0;
            return new ProjectRunEconomySummary(retained, lost, entry, net, hasEntry);
        }

        /// <summary>
        /// 求冻结物品的回收估值总和。
        /// </summary>
        /// <param name="items">冻结物品集合。</param>
        /// <returns>回收估值总和。</returns>
        private static long SumSellValue(IEnumerable<ProjectItem> items)
        {
            long total = 0;
            if (items == null) return total;
            foreach (ProjectItem item in items) if (item != null) total += item.SellGold;
            return total;
        }
    }
}
