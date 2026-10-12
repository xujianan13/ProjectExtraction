using System.Collections.Generic;
using GameLogic.Gameplay.Combat;
using GameLogic.Gameplay.Level;
using GameLogic.Project;

namespace GameLogic.Gameplay.Expedition
{
    public sealed partial class ExpeditionRun
    {
        private ProjectRunStatistics statistics = new() { Version = ProjectRunStatistics.CurrentVersion };

        /// <summary>
        /// 在入场时冻结装备回收估值，后续购买和拾取不改写比较基准。
        /// </summary>
        /// <param name="items">本局入场装备快照。</param>
        private void InitializeStatistics(IReadOnlyList<ProjectItem> items)
        {
            statistics.TotalRegions = regionRequests.Count;
            statistics.EntryItemCount = items.Count;
            foreach (ProjectItem item in items) statistics.EntryItemSellValue += item.SellGold;
        }

        /// <summary>
        /// 返回独立的单局统计快照，区域停留与完成状态读取已保存的权威运行状态。
        /// </summary>
        /// <returns>截至最近一次完整战斗步进的统计及当前探索状态。</returns>
        public ProjectRunStatistics CaptureStatistics()
        {
            ProjectRunStatistics snapshot = statistics.Copy();
            snapshot.Regions.Clear();
            foreach (RegionRuntimeState region in regions)
            {
                snapshot.Regions.Add(new ProjectRegionStatistics
                {
                    RegionId = region.Plan.Request.RegionId, RegionIndex = region.Plan.Request.RegionIndex,
                    IsFinalRegion = region.Plan.Request.RegionIndex == regionRequests.Count - 1,
                    EffectiveTicks = region.EffectiveTick, Completed = region.BossRewardsCompleted
                });
            }
            return snapshot;
        }

        /// <summary>
        /// 累计战斗世界发布的实际伤害和治疗，忽略无效或非玩家相关事件。
        /// </summary>
        /// <param name="fact">本次步进首次排出的战斗事件。</param>
        private void RecordStatistics(CombatEvent fact)
        {
            if (fact.Value <= 0f || float.IsNaN(fact.Value) || float.IsInfinity(fact.Value)) return;
            if (fact.Kind == CombatEventKind.Damage)
            {
                if (fact.ActorId == 1 && fact.TargetActorId != 1) statistics.DamageDealt += fact.Value;
                if (fact.TargetActorId == 1) statistics.DamageTaken += fact.Value;
            }
            else if (fact.Kind == CombatEventKind.Heal && fact.TargetActorId == 1) statistics.HealingReceived += fact.Value;
        }

        /// <summary>
        /// 每次生命确认后记录真实职责、首次或复活分类，以及实际接纳的经验和进度。
        /// </summary>
        /// <param name="plan">已冻结的敌人槽位定义。</param>
        /// <param name="lifeSequence">本次已确认的生命序号。</param>
        /// <param name="experience">本次实际增加的打怪经验。</param>
        /// <param name="progress">本次实际接纳的技能进度。</param>
        private void RecordEnemyKill(RegionSpawnSlotPlan plan, int lifeSequence, int experience, int progress)
        {
            statistics.EnemyKills++;
            if (plan.Kind == RegionEnemyKind.Boss)
            {
                if (IsFinalRegion)
                {
                    statistics.FinalBossKills++;
                    statistics.FinalBossExperience += experience;
                }
                else
                {
                    statistics.RegionBossKills++;
                    statistics.RegionBossExperience += experience;
                }
                return;
            }
            ProjectEnemyLifeStatistics life = plan.Kind == RegionEnemyKind.Elite
                ? lifeSequence == 1 ? statistics.EliteFirst : statistics.EliteRespawn
                : lifeSequence == 1 ? statistics.NormalFirst : statistics.NormalRespawn;
            life.Kills++;
            life.Experience += experience;
            life.Progress += progress;
        }

        /// <summary>
        /// 校验活动统计和原区域生命确认数一致，拒绝从旧活动统计推算缺失分类。
        /// </summary>
        /// <param name="snapshot">已保存的完整活动关卡。</param>
        /// <returns>统计版本、区域时钟、死亡身份与实际经验一致时为真。</returns>
        private static bool ValidateStatisticsSnapshot(ExpeditionRunSnapshot snapshot)
        {
            ProjectRunStatistics stats = snapshot.Statistics;
            if (stats == null || !stats.HasRegionData || !ProjectRunStatistics.IsValid(stats) ||
                stats.TotalRegions != snapshot.Requests.Count || stats.Regions.Count != snapshot.Regions.Count ||
                stats.EarnedKillExperience != snapshot.KillExperience || snapshot.Progress == null ||
                snapshot.Progress.Threshold < 1 || snapshot.Progress.QualificationSerial < 0) return false;
            long progress = 0;
            foreach (ProjectEnemyLifeStatistics life in new[] { stats.NormalFirst, stats.NormalRespawn, stats.EliteFirst, stats.EliteRespawn })
            {
                if (progress > long.MaxValue - life.Progress) return false;
                progress += life.Progress;
            }
            if (progress / snapshot.Progress.Threshold != snapshot.Progress.QualificationSerial ||
                !snapshot.Progress.IsExhausted && progress % snapshot.Progress.Threshold != snapshot.Progress.RemainingProgress) return false;
            long normalFirst = 0, normalRespawn = 0, eliteFirst = 0, eliteRespawn = 0, regionBoss = 0, finalBoss = 0;
            foreach (RegionRuntimeState region in snapshot.Regions)
            {
                ProjectRegionStatistics row = stats.Regions.Find(value => value.RegionId == region.Plan.Request.RegionId);
                if (row == null || row.RegionIndex != region.Plan.Request.RegionIndex || row.EffectiveTicks != region.EffectiveTick ||
                    row.Completed != region.BossRewardsCompleted) return false;
                foreach (RegionSpawnSlotState slot in region.Slots)
                {
                    RegionSpawnSlotPlan plan = region.Plan.SpawnSlots.Find(value => value.SlotId == slot.SlotId);
                    if (plan.Kind == RegionEnemyKind.Boss)
                    {
                        if (region.Plan.Request.RegionIndex == snapshot.Requests.Count - 1) finalBoss += slot.LastProcessedLife;
                        else regionBoss += slot.LastProcessedLife;
                    }
                    else if (plan.Kind == RegionEnemyKind.Elite)
                    {
                        if (slot.LastProcessedLife > 0) eliteFirst++;
                        eliteRespawn += System.Math.Max(0, slot.LastProcessedLife - 1);
                    }
                    else
                    {
                        if (slot.LastProcessedLife > 0) normalFirst++;
                        normalRespawn += System.Math.Max(0, slot.LastProcessedLife - 1);
                    }
                }
            }
            return stats.NormalFirst.Kills == normalFirst && stats.NormalRespawn.Kills == normalRespawn &&
                stats.EliteFirst.Kills == eliteFirst && stats.EliteRespawn.Kills == eliteRespawn &&
                stats.RegionBossKills == regionBoss && stats.FinalBossKills == finalBoss;
        }
    }
}
