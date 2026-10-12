using System;
using System.Collections.Generic;
using System.Text;
using GameLogic.Gameplay.Combat;

namespace GameLogic.Project
{
    /// <summary>
    /// 单类敌人的一次生命收益统计，只累计已经确认的真实击杀与收益。
    /// </summary>
    [Serializable]
    public sealed class ProjectEnemyLifeStatistics
    {
        public int Kills;
        public long Progress;
        public long Experience;

        /// <summary>
        /// 创建独立的生命收益统计副本。
        /// </summary>
        /// <returns>不共享可变对象的统计副本。</returns>
        public ProjectEnemyLifeStatistics Copy() => (ProjectEnemyLifeStatistics)MemberwiseClone();
    }

    /// <summary>
    /// 已生成区域的有效停留时间与真实领奖完成状态。
    /// </summary>
    [Serializable]
    public sealed class ProjectRegionStatistics
    {
        public int RegionId;
        public int RegionIndex;
        public bool IsFinalRegion;
        public long EffectiveTicks;
        public bool Completed;

        /// <summary>
        /// 创建独立区域统计，后续恢复或切区不改变冻结记录。
        /// </summary>
        /// <returns>独立区域统计副本。</returns>
        public ProjectRegionStatistics Copy() => (ProjectRegionStatistics)MemberwiseClone();
    }

    /// <summary>
    /// 单局冻结统计；版本一保留旧历史原值，版本二记录区域与生命分类，版本零表示未记录。
    /// </summary>
    [Serializable]
    public sealed class ProjectRunStatistics
    {
        public const int CurrentVersion = 2;

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
        public int TotalRegions;
        public int RegionBossKills;
        public int FinalBossKills;
        public int BossLootRounds;
        public long RegionBossExperience;
        public long FinalBossExperience;
        public ProjectEnemyLifeStatistics NormalFirst = new();
        public ProjectEnemyLifeStatistics NormalRespawn = new();
        public ProjectEnemyLifeStatistics EliteFirst = new();
        public ProjectEnemyLifeStatistics EliteRespawn = new();
        public List<ProjectRegionStatistics> Regions = new();

        public bool HasData => Version == 1 || Version == CurrentVersion;
        public bool HasRegionData => Version == CurrentVersion;
        public int VisitedRegions => Regions.Count;
        public int CompletedRegions => Regions.FindAll(region => region.Completed).Count;
        public bool IsFinalCompletion => HasRegionData && Regions.Exists(region => region.IsFinalRegion && region.Completed);
        public long EarnedKillExperience => NormalFirst.Experience + NormalRespawn.Experience + EliteFirst.Experience +
            EliteRespawn.Experience + RegionBossExperience + FinalBossExperience;

        /// <summary>
        /// 创建不共享引用的统计副本。
        /// </summary>
        /// <returns>独立统计副本。</returns>
        public ProjectRunStatistics Copy()
        {
            var copy = (ProjectRunStatistics)MemberwiseClone();
            copy.NormalFirst = NormalFirst?.Copy();
            copy.NormalRespawn = NormalRespawn?.Copy();
            copy.EliteFirst = EliteFirst?.Copy();
            copy.EliteRespawn = EliteRespawn?.Copy();
            copy.Regions = Regions?.ConvertAll(region => region?.Copy());
            return copy;
        }

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
                    statistics.DamageTaken == 0 && statistics.HealingReceived == 0 && NoRegionData(statistics);
            if (!statistics.HasData || statistics.EnemyKills < 0 || statistics.RogueChoices < 0 || statistics.EntryItemCount < 0 ||
                statistics.EntryItemSellValue < 0 || statistics.EntryItemSellValue > (long)statistics.EntryItemCount * int.MaxValue ||
                !IsFiniteNonnegative(statistics.DamageDealt) || !IsFiniteNonnegative(statistics.DamageTaken) ||
                !IsFiniteNonnegative(statistics.HealingReceived)) return false;
            if (statistics.Version == 1)
                return statistics.TotalRooms >= 1 && statistics.VisitedRooms >= 1 && statistics.VisitedRooms <= statistics.TotalRooms &&
                    statistics.ClearedCombatRooms >= 0 && statistics.ClearedCombatRooms <= statistics.VisitedRooms &&
                    statistics.BossRoomKills >= 0 && statistics.BossRoomKills <= statistics.EnemyKills && NoRegionData(statistics);
            if (statistics.TotalRooms != 0 || statistics.VisitedRooms != 0 || statistics.ClearedCombatRooms != 0 ||
                statistics.BossRoomKills != 0 || statistics.TotalRegions < 1 || statistics.RegionBossKills < 0 ||
                statistics.RegionBossKills >= statistics.TotalRegions || statistics.FinalBossKills < 0 || statistics.FinalBossKills > 1 ||
                statistics.BossLootRounds < 0 || !ValidLife(statistics.NormalFirst) || !ValidLife(statistics.NormalRespawn) ||
                !ValidLife(statistics.EliteFirst) || !ValidLife(statistics.EliteRespawn) || statistics.RegionBossExperience < 0 ||
                statistics.RegionBossExperience > int.MaxValue || statistics.FinalBossExperience < 0 ||
                statistics.FinalBossExperience > int.MaxValue || statistics.Regions == null || statistics.Regions.Count > statistics.TotalRegions) return false;
            long kills = (long)statistics.NormalFirst.Kills + statistics.NormalRespawn.Kills + statistics.EliteFirst.Kills +
                statistics.EliteRespawn.Kills + statistics.RegionBossKills + statistics.FinalBossKills;
            if (kills != statistics.EnemyKills || statistics.EarnedKillExperience > int.MaxValue ||
                statistics.RegionBossKills == 0 && statistics.RegionBossExperience != 0 ||
                statistics.FinalBossKills == 0 && statistics.FinalBossExperience != 0) return false;
            var ids = new HashSet<int>();
            for (int index = 0; index < statistics.Regions.Count; index++)
            {
                ProjectRegionStatistics region = statistics.Regions[index];
                if (region == null || region.RegionId <= 0 || !ids.Add(region.RegionId) || region.RegionIndex != index ||
                    region.EffectiveTicks < 0 || region.IsFinalRegion != (index == statistics.TotalRegions - 1) ||
                    region.IsFinalRegion && region.Completed && statistics.FinalBossKills != 1) return false;
            }
            return statistics.CompletedRegions <= statistics.RegionBossKills + statistics.FinalBossKills;
        }

        /// <summary>
        /// 只接受真实击杀支撑的非负收益，不从历史缺项推算。
        /// </summary>
        /// <param name="life">生命分类收益。</param>
        /// <returns>分类计数与收益合法时为真。</returns>
        private static bool ValidLife(ProjectEnemyLifeStatistics life) => life != null && life.Kills >= 0 && life.Progress >= 0 &&
            life.Experience >= 0 && life.Experience <= int.MaxValue && (life.Kills > 0 || life.Progress == 0 && life.Experience == 0);

        /// <summary>
        /// 校验旧统计没有伪造新增区域分类。
        /// </summary>
        /// <param name="statistics">旧版本统计。</param>
        /// <returns>所有新增字段均缺席或为空时为真。</returns>
        private static bool NoRegionData(ProjectRunStatistics statistics) => statistics.TotalRegions == 0 &&
            statistics.RegionBossKills == 0 && statistics.FinalBossKills == 0 && statistics.BossLootRounds == 0 &&
            statistics.RegionBossExperience == 0 && statistics.FinalBossExperience == 0 &&
            EmptyLife(statistics.NormalFirst) && EmptyLife(statistics.NormalRespawn) && EmptyLife(statistics.EliteFirst) &&
            EmptyLife(statistics.EliteRespawn) && (statistics.Regions == null || statistics.Regions.Count == 0);

        /// <summary>
        /// 判断历史缺席的分类统计是否没有任何新增计数。
        /// </summary>
        /// <param name="life">可缺席的历史分类。</param>
        /// <returns>未记录任何分类收益时为真。</returns>
        private static bool EmptyLife(ProjectEnemyLifeStatistics life) => life == null || life.Kills == 0 && life.Progress == 0 && life.Experience == 0;

        /// <summary>
        /// 使用真实版本生成只读表现说明，旧记录明确标记区域分类未记录。
        /// </summary>
        /// <returns>可以在结果和历史滚动区显示的统计说明。</returns>
        public string Describe()
        {
            if (!HasData) return "本局未记录完整表现统计。";
            if (!HasRegionData) return $"历史房间 {VisitedRooms}/{TotalRooms} · 击杀 {EnemyKills} · 技能选择 {RogueChoices}\n" +
                "v2 区域、生命分类、Boss 领奖轮次及提前带回/通关未记录。\n" +
                $"造成伤害 {DamageDealt:0.#} · 承受伤害 {DamageTaken:0.#} · 实际治疗 {HealingReceived:0.#}";
            var text = new StringBuilder($"探索区域 {VisitedRegions}/{TotalRegions} · 已完成区域 {CompletedRegions}\n" +
                $"击杀 {EnemyKills} · 区域尾王 {RegionBossKills} · 最终 Boss {FinalBossKills}\n技能选择 {RogueChoices} · Boss 领奖 {BossLootRounds} 轮\n");
            AppendLife(text, "普通首次", NormalFirst);
            AppendLife(text, "普通复活", NormalRespawn);
            AppendLife(text, "精英首次", EliteFirst);
            AppendLife(text, "精英复活", EliteRespawn);
            foreach (ProjectRegionStatistics region in Regions)
                text.Append(region.IsFinalRegion ? "最终场地" : "区域 " + (region.RegionIndex + 1)).Append(" 有效停留 ")
                    .Append((region.EffectiveTicks * (double)CombatWorld.TickSeconds).ToString("0.#"))
                    .Append(" 秒 · ").Append(region.Completed ? "已完成" : "未完成").Append('\n');
            return text.Append($"造成伤害 {DamageDealt:0.#} · 承受伤害 {DamageTaken:0.#} · 实际治疗 {HealingReceived:0.#}").ToString();
        }

        /// <summary>
        /// 输出一次生命分类的真实击杀、获得进度和经验。
        /// </summary>
        /// <param name="text">统计说明缓冲区。</param>
        /// <param name="name">分类中文名称。</param>
        /// <param name="life">已经记录的真实收益。</param>
        private static void AppendLife(StringBuilder text, string name, ProjectEnemyLifeStatistics life) =>
            text.Append(name).Append(" 击杀 ").Append(life.Kills).Append(" · 进度 ").Append(life.Progress)
                .Append(" · 经验 ").Append(life.Experience).Append('\n');

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
