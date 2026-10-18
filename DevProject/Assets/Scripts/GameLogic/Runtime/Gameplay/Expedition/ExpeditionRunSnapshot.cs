using System;
using System.Collections.Generic;
using GameLogic.Gameplay.Combat;
using GameLogic.Gameplay.Level;
using GameLogic.Gameplay.Roguelike;
using GameLogic.Project;

namespace GameLogic.Gameplay.Expedition
{
    /// <summary>
    /// 房间的固定内容与已确认业务事实，不保存敌人生命、位置或攻击现场。
    /// </summary>
    [Serializable]
    public sealed class ExpeditionRoomCheckpoint
    {
        public RegionPlan Plan;
        public bool Completed;
        public bool BossDefeated;
        public bool BossRewardsCompleted;
        public long EffectiveTicks;
        public List<string> RewardedSlotIds = new();
        public List<RegionChestState> Chests = new();
        public List<RegionNpcState> Npcs = new();

        /// <summary>
        /// 从运行房间提取稳定内容、已发奖对象和交互提交记录。
        /// </summary>
        /// <param name="room">已经消费战斗事件的运行房间。</param>
        /// <returns>与当前房间隔离的业务检查点。</returns>
        public static ExpeditionRoomCheckpoint Capture(RegionRuntimeState room)
        {
            var result = new ExpeditionRoomCheckpoint
            {
                Plan = room.Plan.Copy(), Completed = room.ExitEnabled, BossDefeated = room.BossDefeated,
                BossRewardsCompleted = room.BossRewardsCompleted, EffectiveTicks = room.EffectiveTick,
                Chests = room.Chests.ConvertAll(value => value.Copy()), Npcs = room.Npcs.ConvertAll(value => value.Copy())
            };
            foreach (RegionSpawnSlotState slot in room.Slots)
                if (slot.RewardClaimed) result.RewardedSlotIds.Add(slot.SlotId);
            return result;
        }

        /// <summary>
        /// 根据业务检查点重建房间，未完成战斗的所有敌人重新生成且保留发奖去重记录。
        /// </summary>
        /// <param name="room">等待建立战斗世界的房间。</param>
        /// <param name="error">检查点或地图无效的原因。</param>
        /// <returns>业务事实合法且重建成功时为真。</returns>
        public bool TryRestore(out RegionRuntimeState room, out string error)
        {
            room = null;
            error = "房间检查点结构不完整。";
            if (Plan == null || EffectiveTicks < 0 || RewardedSlotIds == null || Chests == null || Npcs == null ||
                BossRewardsCompleted && !BossDefeated || !RegionRuntimeState.TryCreate(Plan, out var restored, out error)) return false;
            var rewarded = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in RewardedSlotIds)
                if (string.IsNullOrWhiteSpace(id) || !rewarded.Add(id) || !Plan.SpawnSlots.Exists(slot => slot.SlotId == id)) return false;
            restored.EffectiveTick = EffectiveTicks;
            restored.BossDefeated = BossDefeated;
            restored.BossRewardsCompleted = BossRewardsCompleted;
            restored.ExitEnabled = Completed;
            foreach (RegionSpawnSlotState slot in restored.Slots)
            {
                slot.RewardClaimed = rewarded.Contains(slot.SlotId);
                if (!Completed && !BossDefeated) continue;
                if (!slot.RewardClaimed) { error = "已完成房间存在未确认的敌人收益。"; return false; }
                slot.Phase = RegionSpawnPhase.Defeated;
                slot.LastProcessedLife = 1;
            }
            foreach (RegionChestState chest in Chests)
                if (chest == null || chest.ClaimedItemIds == null) return false;
            foreach (RegionNpcState npc in Npcs)
                if (npc == null || npc.SoldOfferIds == null) return false;
            restored.Chests = Chests.ConvertAll(value => value.Copy());
            restored.Npcs = Npcs.ConvertAll(value => value.Copy());
            if (!LevelContentConfig.TryCreateRoomMap(Plan.Request.MapId, out var map, out error) || !restored.TryValidate(map, out error)) return false;
            room = restored;
            return true;
        }
    }

    /// <summary>
    /// V3 关卡检查点，只保存玩家、固定房间计划和已经提交的业务事实。
    /// </summary>
    [Serializable]
    public sealed class ExpeditionRunSnapshot
    {
        public int Version = 3;
        public string ConfigVersion;
        public string RunId;
        public int LevelId;
        public int Seed;
        public int LootLevel;
        public int CurrentRoomIndex;
        public int RoomAdmissionSerial = 1;
        public bool AdmissionPending = true;
        public long ElapsedTicks;
        public int KillExperience;
        public int GoldSpent;
        public float AugmentBaseAttackRange;
        public long LootRandomState;
        public long BossRandomState;
        public List<RegionGenerationRequest> Requests = new();
        public List<ExpeditionRoomCheckpoint> Rooms = new();
        public List<ProjectItem> EntryEquipment = new();
        public List<ProjectItem> Inventory = new();
        public List<int> PendingBossRegions = new();
        public BossLootState BossLoot;
        public ProjectRunStatistics Statistics;
        public int PendingPlayerDeathId;
        public bool AdExtractionAuthorized;
        public bool HasPendingOutcome;
        public ProjectRunOutcome PendingOutcome;
        public CombatPlayerCheckpoint PlayerState;
    }
}
