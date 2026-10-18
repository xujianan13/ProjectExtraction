using System.Collections.Generic;
using GameLogic.Gameplay.Combat;
using GameLogic.Gameplay.Level;
using GameLogic.Project;
using UnityEngine;

namespace GameLogic.Gameplay.Expedition
{
    public sealed partial class ExpeditionRun
    {
        private ProjectRunStatistics statistics = new() { Version = ProjectRunStatistics.CurrentVersion };

        /// <summary>
        /// 冻结携入装备估值，关卡背包所得和后续购买不改写入场基准。
        /// </summary>
        /// <param name="items">本局受保护携入装备。</param>
        private void InitializeStatistics(IReadOnlyList<ProjectItem> items)
        {
            statistics.TotalRooms = regionRequests.Count;
            statistics.EntryItemCount = items.Count;
            foreach (var item in items) statistics.EntryItemSellValue += item.SellGold;
        }

        /// <summary>
        /// 从房间完成、唯一发奖和领取交易事实生成独立统计，不累计重建敌人的重复收益。
        /// </summary>
        /// <returns>截至最近完整业务边界的房间与战斗统计。</returns>
        public ProjectRunStatistics CaptureStatistics()
        {
            var snapshot = statistics.Copy();
            snapshot.Rooms.Clear();
            foreach (var region in regions)
            {
                var rewarded = new List<string>();
                foreach (var slot in region.Slots) if (slot.RewardClaimed) rewarded.Add(slot.SlotId);
                if (!ProjectRunStatistics.TryCreateRoomStatistics(region.Plan, rewarded, region.ExitEnabled, region.EffectiveTick,
                    region.Chests, region.Npcs, out var row))
                {
                    HandleFault("房间收益账本与固定计划不一致，无法保存统计。");
                    return snapshot;
                }
                snapshot.Rooms.Add(row);
            }
            snapshot.Rooms.Sort((left, right) => left.RoomIndex.CompareTo(right.RoomIndex));
            snapshot.RecalculateRoomTotals();
            return snapshot;
        }

        /// <summary>
        /// 累计实际战斗伤害与治疗，重复重建不会重放旧战斗事件。
        /// </summary>
        /// <param name="fact">首次排出的战斗事实。</param>
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
        /// 接收首次发奖通知，最终击杀和经验统计始终从槽位账本重新计算。
        /// </summary>
        /// <param name="plan">已经提交收益的固定槽位计划。</param>
        /// <param name="lifeSequence">房间内本次临时生命序号，不作为重复奖励依据。</param>
        /// <param name="experience">本次永久经验增量，最终按固定计划复核。</param>
        /// <param name="progress">兼容现有通知签名，新版本不再产生随机三选一进度。</param>
        private void RecordEnemyKill(RegionSpawnSlotPlan plan, int lifeSequence, int experience, int progress)
        {
            var snapshot = CaptureStatistics();
            statistics.EnemyKills = snapshot.EnemyKills;
            statistics.NormalFirst = snapshot.NormalFirst;
            statistics.EliteFirst = snapshot.EliteFirst;
            statistics.FinalBossKills = snapshot.FinalBossKills;
            statistics.FinalBossExperience = snapshot.FinalBossExperience;
            statistics.BossRoomKills = snapshot.BossRoomKills;
        }

        /// <summary>
        /// 按独立房间检查点复核全部已发奖槽位、经验、交互及有效时间，拒绝旧活动统计。
        /// </summary>
        /// <param name="snapshot">待恢复的新版本业务检查点。</param>
        /// <returns>统计与稳定房间事实逐项一致时为真。</returns>
        public static bool ValidateStatisticsSnapshot(ExpeditionRunSnapshot snapshot)
        {
            if (snapshot == null || snapshot.Version != 3 || snapshot.Statistics == null || !snapshot.Statistics.HasRoomData ||
                !ProjectRunStatistics.IsValid(snapshot.Statistics) || snapshot.Rooms == null || snapshot.Requests == null ||
                snapshot.EntryEquipment == null) return false;
            var stats = snapshot.Statistics;
            if (stats.TotalRooms != snapshot.Requests.Count || stats.Rooms.Count != snapshot.Rooms.Count ||
                stats.EarnedKillExperience != snapshot.KillExperience || snapshot.ElapsedTicks < 0 ||
                snapshot.CurrentRoomIndex < 0 || snapshot.CurrentRoomIndex >= stats.TotalRooms) return false;
            long entryValue = 0;
            foreach (var item in snapshot.EntryEquipment)
            {
                if (item == null || item.SellGold < 0) return false;
                entryValue += item.SellGold;
            }
            if (stats.EntryItemCount != snapshot.EntryEquipment.Count || stats.EntryItemSellValue != entryValue) return false;
            long elapsed = 0;
            for (int index = 0; index < snapshot.Rooms.Count; index++)
            {
                var room = snapshot.Rooms[index];
                if (room == null || room.EffectiveTicks < 0 || elapsed > long.MaxValue - room.EffectiveTicks ||
                    !ProjectRunStatistics.TryCreateRoomStatistics(room.Plan, room.RewardedSlotIds, room.Completed, room.EffectiveTicks,
                        room.Chests, room.Npcs, out var expected)) return false;
                elapsed += room.EffectiveTicks;
                if (room.BossDefeated != (expected.BossKills == 1) || room.BossRewardsCompleted && !room.BossDefeated ||
                    expected.Kind == ExpeditionRoomKind.Boss && room.Completed && !room.BossRewardsCompleted) return false;
                if (JsonUtility.ToJson(expected) != JsonUtility.ToJson(stats.Rooms[index])) return false;
            }
            return elapsed == snapshot.ElapsedTicks;
        }
    }
}
