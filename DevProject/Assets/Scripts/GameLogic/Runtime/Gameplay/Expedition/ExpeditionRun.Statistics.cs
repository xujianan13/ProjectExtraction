using System.Collections.Generic;
using GameLogic.Gameplay.Combat;
using GameLogic.Gameplay.Level;
using GameLogic.Project;

namespace GameLogic.Gameplay.Expedition
{
    public sealed partial class ExpeditionRun
    {
        private readonly ProjectRunStatistics statistics = new() { Version = ProjectRunStatistics.CurrentVersion };

        /// <summary>
        /// 在入场时冻结装备回收估值，后续购买和拾取不改写比较基准。
        /// </summary>
        /// <param name="items">本局入场装备快照。</param>
        private void InitializeStatistics(IReadOnlyList<ProjectItem> items)
        {
            statistics.TotalRooms = Maps.Rooms.Count;
            statistics.EntryItemCount = items.Count;
            foreach (ProjectItem item in items) statistics.EntryItemSellValue += item.SellGold;
        }

        /// <summary>
        /// 返回独立的单局统计快照，房间回访不会重复计数。
        /// </summary>
        /// <returns>截至最近一次完整战斗步进的统计及当前探索状态。</returns>
        public ProjectRunStatistics CaptureStatistics()
        {
            ProjectRunStatistics snapshot = statistics.Copy();
            foreach (LevelRoomState room in Maps.Rooms)
            {
                if (!room.IsVisited) continue;
                snapshot.VisitedRooms++;
                if (room.IsCleared && (room.Type == LevelRoomType.Mob || room.Type == LevelRoomType.Boss)) snapshot.ClearedCombatRooms++;
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
        /// 在敌人死亡首次发放奖励时记录击杀，并按生成房间归类 Boss 房击杀。
        /// </summary>
        /// <param name="roomId">敌人死亡的房间身份。</param>
        private void RecordEnemyKill(int roomId)
        {
            statistics.EnemyKills++;
            if (Maps.TryGetRoom(roomId, out LevelRoomState room) && room.Type == LevelRoomType.Boss) statistics.BossRoomKills++;
        }
    }
}
