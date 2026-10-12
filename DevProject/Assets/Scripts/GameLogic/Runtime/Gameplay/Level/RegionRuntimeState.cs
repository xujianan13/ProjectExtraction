using System;
using System.Collections.Generic;

namespace GameLogic.Gameplay.Level
{
    /// <summary>
    /// 稳定槽位的生命身份和刷新状态，不复制活体战斗真值。
    /// </summary>
    [Serializable]
    public sealed class RegionSpawnSlotState
    {
        public string SlotId;
        public int LifeSequence = 1;
        public int CurrentActorId;
        public int LastProcessedLife;
        public RegionSpawnPhase Phase;
        public long DeathTick = -1;
        public long ReadyTick = -1;
        public long WarningStartTick = -1;
        public RegionSpawnWaitReason WaitingReason;

        /// <summary>
        /// 创建独立槽位状态副本。
        /// </summary>
        /// <returns>独立运行态。</returns>
        public RegionSpawnSlotState Copy() => (RegionSpawnSlotState)MemberwiseClone();
    }

    /// <summary>
    /// 宝箱开启与每件冻结物品的领取记录。
    /// </summary>
    [Serializable]
    public sealed class RegionChestState
    {
        public string Id;
        public bool Opened;
        public List<string> ClaimedItemIds = new();

        /// <summary>
        /// 创建独立领取状态副本。
        /// </summary>
        /// <returns>不共享领取列表的副本。</returns>
        public RegionChestState Copy() => new RegionChestState { Id = Id, Opened = Opened, ClaimedItemIds = new List<string>(ClaimedItemIds) };
    }

    /// <summary>
    /// NPC 访问与固定商品的售出记录。
    /// </summary>
    [Serializable]
    public sealed class RegionNpcState
    {
        public string Id;
        public bool Visited;
        public List<string> SoldOfferIds = new();

        /// <summary>
        /// 创建独立商店运行态。
        /// </summary>
        /// <returns>不共享售出列表的副本。</returns>
        public RegionNpcState Copy() => new RegionNpcState { Id = Id, Visited = Visited, SoldOfferIds = new List<string>(SoldOfferIds) };
    }

    /// <summary>
    /// 由统一会话提交的区域组合、交互和个体刷新事实。
    /// </summary>
    [Serializable]
    public sealed class RegionRuntimeState
    {
        public RegionPlan Plan;
        public long EffectiveTick;
        public bool BossDefeated;
        public bool BossRewardsCompleted;
        public bool ExitEnabled;
        public string TrackingTargetId;
        public List<RegionSpawnSlotState> Slots = new();
        public List<RegionChestState> Chests = new();
        public List<RegionNpcState> Npcs = new();

        /// <summary>
        /// 取得本区域出口的稳定追踪身份。
        /// </summary>
        public string ExitTargetId => Plan.Request.RunId + ":region:" + Plan.Request.RegionId + ":exit";

        /// <summary>
        /// 从有效冻结计划建立未激活状态，初生生命序号始终为一。
        /// </summary>
        /// <param name="plan">已经完成结构和空间校验的计划。</param>
        /// <param name="state">新建区域状态。</param>
        /// <param name="error">失败原因。</param>
        /// <returns>计划结构完整并创建成功时为真。</returns>
        public static bool TryCreate(RegionPlan plan, out RegionRuntimeState state, out string error)
        {
            state = null;
            error = null;
            if (plan == null) { error = "无法从缺失计划建立区域运行态。"; return false; }
            if (!plan.TryValidateStructure(out error)) return false;
            var created = new RegionRuntimeState { Plan = plan.Copy() };
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (RegionSpawnSlotPlan slot in plan.SpawnSlots)
            {
                if (slot == null || string.IsNullOrWhiteSpace(slot.SlotId) || !ids.Add(slot.SlotId))
                { error = "区域槽位身份无效。"; return false; }
                created.Slots.Add(new RegionSpawnSlotState { SlotId = slot.SlotId, Phase = RegionSpawnPhase.InitialSpawn });
            }
            foreach (RegionChestPlan chest in plan.Chests) created.Chests.Add(new RegionChestState { Id = chest.Id });
            foreach (RegionNpcPlan npc in plan.Npcs) created.Npcs.Add(new RegionNpcState { Id = npc.Id });
            state = created;
            return true;
        }

        /// <summary>
        /// 创建可以独立修改、审查和提交的完整区域候选。
        /// </summary>
        /// <returns>包含独立计划与运行态的副本。</returns>
        public RegionRuntimeState Copy() => new RegionRuntimeState
        {
            Plan = Plan.Copy(), EffectiveTick = EffectiveTick, BossDefeated = BossDefeated, BossRewardsCompleted = BossRewardsCompleted,
            ExitEnabled = ExitEnabled, TrackingTargetId = TrackingTargetId, Slots = Slots.ConvertAll(value => value.Copy()),
            Chests = Chests.ConvertAll(value => value.Copy()), Npcs = Npcs.ConvertAll(value => value.Copy())
        };

        /// <summary>
        /// 接纳一次与当前槽位生命完全匹配的死亡，重复事件无效。
        /// </summary>
        /// <param name="slotId">稳定生成槽位。</param>
        /// <param name="actorId">死亡生命的战斗身份。</param>
        /// <param name="lifeSequence">死亡生命序号。</param>
        /// <returns>首次记录当前生命死亡时为真。</returns>
        public bool TryRecordDeath(string slotId, int actorId, int lifeSequence)
        {
            RegionSpawnSlotState slot = Slots.Find(value => value.SlotId == slotId);
            if (slot == null || slot.Phase != RegionSpawnPhase.Alive || slot.CurrentActorId != actorId || slot.LifeSequence != lifeSequence) return false;
            RegionSpawnSlotPlan definition = SlotPlan(slotId);
            RegionRespawnRule rule = Rule(definition.Kind);
            if (definition.Kind != RegionEnemyKind.Boss && EffectiveTick > long.MaxValue - rule.DelayTicks) return false;
            slot.DeathTick = EffectiveTick;
            slot.WarningStartTick = -1;
            if (definition.Kind == RegionEnemyKind.Boss)
            {
                slot.Phase = RegionSpawnPhase.Defeated;
                slot.ReadyTick = -1;
                slot.WaitingReason = RegionSpawnWaitReason.None;
                BossDefeated = true;
            }
            else
            {
                slot.Phase = RegionSpawnPhase.DeadWaiting;
                slot.ReadyTick = EffectiveTick + rule.DelayTicks;
                slot.WaitingReason = RegionSpawnWaitReason.Countdown;
            }
            return true;
        }

        /// <summary>
        /// 收益各项全部应用后确认当前死亡生命，禁止先去重后补发。
        /// </summary>
        /// <param name="slotId">稳定生成槽位。</param>
        /// <param name="lifeSequence">已经完整消费的生命序号。</param>
        /// <returns>首次确认当前死亡收益时为真。</returns>
        public bool TryConfirmDeathProcessed(string slotId, int lifeSequence)
        {
            RegionSpawnSlotState slot = Slots.Find(value => value.SlotId == slotId);
            if (slot == null || slot.Phase == RegionSpawnPhase.Alive || slot.Phase == RegionSpawnPhase.InitialSpawn ||
                slot.LifeSequence != lifeSequence || slot.LastProcessedLife != lifeSequence - 1) return false;
            slot.LastProcessedLife = lifeSequence;
            return true;
        }

        /// <summary>
        /// 推进一个完整的区域有效帧，调用方在暂停、离区和保存门禁时不调用。
        /// </summary>
        /// <param name="safety">主协调器提供的合法占位、玩家距离和相机结果。</param>
        /// <returns>有效时钟成功推进时为真。</returns>
        public bool AdvanceTick(Func<RegionSpawnSlotPlan, RegionSpawnSafety> safety)
        {
            if (EffectiveTick == long.MaxValue || safety == null) return false;
            EffectiveTick++;
            foreach (RegionSpawnSlotState slot in Slots)
            {
                if (slot.Phase == RegionSpawnPhase.InitialSpawn || slot.Phase == RegionSpawnPhase.Alive || slot.Phase == RegionSpawnPhase.Defeated) continue;
                if (slot.Phase == RegionSpawnPhase.DeadWaiting)
                {
                    if (EffectiveTick < slot.ReadyTick) continue;
                    slot.Phase = RegionSpawnPhase.WaitingSafety;
                }
                if (slot.LastProcessedLife != slot.LifeSequence)
                {
                    slot.Phase = RegionSpawnPhase.WaitingSafety;
                    slot.WaitingReason = RegionSpawnWaitReason.UnprocessedDeath;
                    slot.WarningStartTick = -1;
                    continue;
                }
                RegionSpawnSlotPlan definition = SlotPlan(slot.SlotId);
                RegionSpawnSafety availability = safety(definition);
                if (!availability.IsSafe)
                {
                    slot.Phase = RegionSpawnPhase.WaitingSafety;
                    slot.WaitingReason = availability.Reason;
                    slot.WarningStartTick = -1;
                    continue;
                }
                slot.WaitingReason = RegionSpawnWaitReason.None;
                if (slot.Phase == RegionSpawnPhase.WaitingSafety)
                {
                    slot.Phase = RegionSpawnPhase.Warning;
                    slot.WarningStartTick = EffectiveTick;
                }
                if (slot.Phase == RegionSpawnPhase.Warning && EffectiveTick - slot.WarningStartTick >= Rule(definition.Kind).WarningTicks)
                    slot.Phase = RegionSpawnPhase.ReadyToSpawn;
            }
            return true;
        }

        /// <summary>
        /// 读取待出生请求，重复读取不会创建实体或递增生命。
        /// </summary>
        /// <param name="destination">接收请求的列表，进入时清空。</param>
        /// <returns>请求数量。</returns>
        public int GetSpawnRequests(List<RegionSpawnRequest> destination)
        {
            destination.Clear();
            foreach (RegionSpawnSlotState slot in Slots)
            {
                bool initial = slot.Phase == RegionSpawnPhase.InitialSpawn;
                if (!initial && slot.Phase != RegionSpawnPhase.ReadyToSpawn || !initial && slot.LifeSequence == int.MaxValue) continue;
                RegionSpawnSlotPlan definition = SlotPlan(slot.SlotId);
                destination.Add(new RegionSpawnRequest
                {
                    SlotId = slot.SlotId, ExpectedLifeSequence = slot.LifeSequence, IsInitial = initial, EnemyId = definition.EnemyId,
                    EnemyLevel = definition.EnemyLevel, Kind = definition.Kind, Position = definition.HomePosition
                });
            }
            return destination.Count;
        }

        /// <summary>
        /// 在初生 ECS 实体创建成功后关联全局唯一战斗身份。
        /// </summary>
        /// <param name="slotId">稳定生成槽位。</param>
        /// <param name="actorId">主协调器分配且已经创建的全局身份。</param>
        /// <returns>初生关联首次提交成功时为真。</returns>
        public bool CommitInitialSpawn(string slotId, int actorId)
        {
            RegionSpawnSlotState slot = Slots.Find(value => value.SlotId == slotId);
            if (slot == null || slot.Phase != RegionSpawnPhase.InitialSpawn || slot.LifeSequence != 1 ||
                slot.CurrentActorId != 0 || !ActorIdAvailable(actorId)) return false;
            slot.CurrentActorId = actorId;
            slot.Phase = RegionSpawnPhase.Alive;
            return true;
        }

        /// <summary>
        /// 在复活 ECS 实体创建成功后提交新生命，失败重试保持原请求。
        /// </summary>
        /// <param name="slotId">稳定生成槽位。</param>
        /// <param name="expectedLifeSequence">请求所依据的旧生命序号。</param>
        /// <param name="newActorId">本次新分配且已经创建的全局身份。</param>
        /// <returns>新生命首次提交成功时为真。</returns>
        public bool CommitRespawn(string slotId, int expectedLifeSequence, int newActorId)
        {
            RegionSpawnSlotState slot = Slots.Find(value => value.SlotId == slotId);
            if (slot == null || slot.Phase != RegionSpawnPhase.ReadyToSpawn || slot.LifeSequence != expectedLifeSequence ||
                expectedLifeSequence == int.MaxValue || slot.LastProcessedLife != expectedLifeSequence ||
                newActorId <= slot.CurrentActorId || !ActorIdAvailable(newActorId)) return false;
            slot.LifeSequence++;
            slot.CurrentActorId = newActorId;
            slot.Phase = RegionSpawnPhase.Alive;
            slot.DeathTick = -1;
            slot.ReadyTick = -1;
            slot.WarningStartTick = -1;
            slot.WaitingReason = RegionSpawnWaitReason.None;
            return true;
        }

        /// <summary>
        /// 提交一次宝箱开启，具体物品继续保持冻结身份。
        /// </summary>
        /// <param name="chestId">宝箱身份。</param>
        /// <returns>首次开启时为真。</returns>
        public bool TryOpenChest(string chestId)
        {
            RegionChestState chest = Chests.Find(value => value.Id == chestId);
            if (chest == null || chest.Opened) return false;
            chest.Opened = true;
            return true;
        }

        /// <summary>
        /// 在物品归属转移成功后提交一件宝箱物品的领取标记。
        /// </summary>
        /// <param name="chestId">宝箱身份。</param>
        /// <param name="itemId">冻结物品身份。</param>
        /// <returns>首次领取且属于该宝箱时为真。</returns>
        public bool TryClaimChestItem(string chestId, string itemId)
        {
            RegionChestState chest = Chests.Find(value => value.Id == chestId);
            RegionChestPlan plan = Plan.Chests.Find(value => value.Id == chestId);
            if (chest == null || !chest.Opened || chest.ClaimedItemIds.Contains(itemId) || !plan.Items.Exists(value => value.Id == itemId)) return false;
            chest.ClaimedItemIds.Add(itemId);
            return true;
        }

        /// <summary>
        /// 记录 NPC 访问状态。
        /// </summary>
        /// <param name="npcId">NPC 身份。</param>
        /// <returns>首次访问时为真。</returns>
        public bool TryVisitNpc(string npcId)
        {
            RegionNpcState npc = Npcs.Find(value => value.Id == npcId);
            if (npc == null || npc.Visited) return false;
            npc.Visited = true;
            return true;
        }

        /// <summary>
        /// 在扣款与交货候选完整后提交商品售出状态。
        /// </summary>
        /// <param name="npcId">NPC 身份。</param>
        /// <param name="offerId">冻结商品身份。</param>
        /// <returns>首次售出且商品属于该 NPC 时为真。</returns>
        public bool TrySellNpcOffer(string npcId, string offerId)
        {
            RegionNpcState npc = Npcs.Find(value => value.Id == npcId);
            RegionNpcPlan plan = Plan.Npcs.Find(value => value.Id == npcId);
            if (npc == null || npc.SoldOfferIds.Contains(offerId) || !plan.Offers.Exists(value => value.Id == offerId)) return false;
            npc.Visited = true;
            npc.SoldOfferIds.Add(offerId);
            return true;
        }

        /// <summary>
        /// 在 Boss 奖励全部确认后开启离开操作。
        /// </summary>
        /// <returns>首次提交领奖完成时为真。</returns>
        public bool TryCompleteBossRewards()
        {
            if (!BossDefeated || BossRewardsCompleted) return false;
            BossRewardsCompleted = true;
            ExitEnabled = true;
            return true;
        }

        /// <summary>
        /// 设置一个合法的关键点追踪目标，空身份用于主动取消。
        /// </summary>
        /// <param name="targetId">稳定地图目标身份。</param>
        /// <returns>目标合法时为真。</returns>
        public bool TrySetTrackingTarget(string targetId)
        {
            if (!string.IsNullOrEmpty(targetId) && !HasTrackingTarget(targetId)) return false;
            TrackingTargetId = targetId;
            return true;
        }

        /// <summary>
        /// 检查恢复数据的组合、时钟、生命及交互记录一致性。
        /// </summary>
        /// <param name="map">与保存版本匹配的地图描述。</param>
        /// <param name="error">失败原因。</param>
        /// <returns>可以续接该区域时为真。</returns>
        public bool TryValidate(RegionMapDefinition map, out string error)
        {
            error = null;
            if (Plan == null || !Plan.TryValidate(map, out error)) return false;
            if (EffectiveTick < 0 || Slots == null || Chests == null || Npcs == null || Slots.Count != Plan.SpawnSlots.Count ||
                Chests.Count != Plan.Chests.Count || Npcs.Count != Plan.Npcs.Count || BossRewardsCompleted && !BossDefeated ||
                ExitEnabled != BossRewardsCompleted || !string.IsNullOrEmpty(TrackingTargetId) && !HasTrackingTarget(TrackingTargetId))
            { error = "区域运行态结构、目标或完成状态无效。"; return false; }
            var slotIds = new HashSet<string>(StringComparer.Ordinal);
            var actorIds = new HashSet<int>();
            bool defeatedBoss = false;
            foreach (RegionSpawnSlotState slot in Slots)
            {
                if (slot == null || !slotIds.Add(slot.SlotId) || !Plan.SpawnSlots.Exists(value => value.SlotId == slot.SlotId) ||
                    slot.LifeSequence <= 0 || slot.LastProcessedLife < 0 || slot.LastProcessedLife > slot.LifeSequence ||
                    slot.LastProcessedLife < slot.LifeSequence - 1 || !Enum.IsDefined(typeof(RegionSpawnPhase), slot.Phase) ||
                    !Enum.IsDefined(typeof(RegionSpawnWaitReason), slot.WaitingReason) || !ValidateSlot(slot, actorIds))
                { error = "区域槽位生命、计时或战斗身份无效。"; return false; }
                if (SlotPlan(slot.SlotId).Kind == RegionEnemyKind.Boss) defeatedBoss = slot.Phase == RegionSpawnPhase.Defeated;
            }
            if (BossDefeated != defeatedBoss) { error = "Boss 完成事实与槽位不一致。"; return false; }
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (RegionChestState chest in Chests)
            {
                if (chest == null || !ids.Add(chest.Id)) { error = "宝箱运行身份重复或无效。"; return false; }
                RegionChestPlan definition = Plan.Chests.Find(value => value.Id == chest.Id);
                if (definition == null || chest.ClaimedItemIds == null || !chest.Opened && chest.ClaimedItemIds.Count != 0 ||
                    !ValidateClaims(chest.ClaimedItemIds, definition.Items.ConvertAll(value => value.Id)))
                { error = "宝箱领取状态无效。"; return false; }
            }
            ids.Clear();
            foreach (RegionNpcState npc in Npcs)
            {
                if (npc == null || !ids.Add(npc.Id)) { error = "NPC 运行身份重复或无效。"; return false; }
                RegionNpcPlan definition = Plan.Npcs.Find(value => value.Id == npc.Id);
                if (definition == null || npc.SoldOfferIds == null || !npc.Visited && npc.SoldOfferIds.Count != 0 ||
                    !ValidateClaims(npc.SoldOfferIds, definition.Offers.ConvertAll(value => value.Id)))
                { error = "NPC 售出状态无效。"; return false; }
            }
            return true;
        }

        /// <summary>
        /// 检查单槽生命与阶段字段的精确关系。
        /// </summary>
        /// <param name="slot">运行槽位。</param>
        /// <param name="actorIds">本区域已经出现的战斗身份。</param>
        /// <returns>当前阶段的所有字段一致时为真。</returns>
        private bool ValidateSlot(RegionSpawnSlotState slot, HashSet<int> actorIds)
        {
            bool initial = slot.Phase == RegionSpawnPhase.InitialSpawn;
            if (initial) return slot.LifeSequence == 1 && slot.CurrentActorId == 0 && slot.LastProcessedLife == 0 &&
                slot.DeathTick == -1 && slot.ReadyTick == -1 && slot.WarningStartTick == -1 && slot.WaitingReason == RegionSpawnWaitReason.None;
            if (slot.CurrentActorId <= 1 || !actorIds.Add(slot.CurrentActorId)) return false;
            if (slot.Phase == RegionSpawnPhase.Alive) return slot.LastProcessedLife == slot.LifeSequence - 1 &&
                slot.DeathTick == -1 && slot.ReadyTick == -1 && slot.WarningStartTick == -1 && slot.WaitingReason == RegionSpawnWaitReason.None;
            if (slot.DeathTick < 0 || slot.DeathTick > EffectiveTick) return false;
            RegionEnemyKind kind = SlotPlan(slot.SlotId).Kind;
            if (kind == RegionEnemyKind.Boss) return slot.Phase == RegionSpawnPhase.Defeated && slot.LifeSequence == 1 &&
                slot.ReadyTick == -1 && slot.WarningStartTick == -1 && slot.WaitingReason == RegionSpawnWaitReason.None;
            RegionRespawnRule rule = Rule(kind);
            if (slot.Phase == RegionSpawnPhase.Defeated || slot.DeathTick > long.MaxValue - rule.DelayTicks ||
                slot.ReadyTick != slot.DeathTick + rule.DelayTicks) return false;
            if (slot.Phase == RegionSpawnPhase.DeadWaiting) return slot.ReadyTick > EffectiveTick && slot.WarningStartTick == -1 &&
                slot.WaitingReason == RegionSpawnWaitReason.Countdown;
            if (slot.ReadyTick > EffectiveTick) return false;
            if (slot.Phase == RegionSpawnPhase.WaitingSafety) return slot.WarningStartTick == -1 &&
                slot.WaitingReason != RegionSpawnWaitReason.None && slot.WaitingReason != RegionSpawnWaitReason.Countdown;
            if (slot.LastProcessedLife != slot.LifeSequence || slot.WaitingReason != RegionSpawnWaitReason.None ||
                slot.WarningStartTick < slot.ReadyTick || slot.WarningStartTick > EffectiveTick) return false;
            long elapsed = EffectiveTick - slot.WarningStartTick;
            return slot.Phase == RegionSpawnPhase.Warning ? elapsed < rule.WarningTicks :
                slot.Phase == RegionSpawnPhase.ReadyToSpawn && elapsed >= rule.WarningTicks;
        }

        /// <summary>
        /// 检查领取记录属于冻结内容且没有重复。
        /// </summary>
        /// <param name="claims">已消费身份。</param>
        /// <param name="available">冻结可用身份。</param>
        /// <returns>所有领取记录有效时为真。</returns>
        private static bool ValidateClaims(List<string> claims, List<string> available)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in claims) if (!ids.Add(id) || !available.Contains(id)) return false;
            return true;
        }

        /// <summary>
        /// 判断追踪身份是否属于当前区域关键目标。
        /// </summary>
        /// <param name="id">追踪身份。</param>
        /// <returns>目标存在时为真。</returns>
        private bool HasTrackingTarget(string id) => id == ExitTargetId || Plan.SpawnSlots.Exists(value => value.Kind != RegionEnemyKind.Normal && value.SlotId == id) ||
            Plan.Chests.Exists(value => value.Id == id) || Plan.Npcs.Exists(value => value.Id == id);

        /// <summary>
        /// 检查新战斗身份在当前区域未被使用，跨区域唯一性由主协调器保证。
        /// </summary>
        /// <param name="actorId">新分配身份。</param>
        /// <returns>区域内未使用且不是玩家身份时为真。</returns>
        private bool ActorIdAvailable(int actorId) => actorId > 1 && !Slots.Exists(value => value.CurrentActorId == actorId);

        /// <summary>
        /// 读取已经按不变量保证存在的槽位定义。
        /// </summary>
        /// <param name="slotId">槽位身份。</param>
        /// <returns>槽位计划。</returns>
        private RegionSpawnSlotPlan SlotPlan(string slotId) => Plan.SpawnSlots.Find(value => value.SlotId == slotId);

        /// <summary>
        /// 取得敌人类别对应的刷新规则，Boss 调用者不读取该返回值。
        /// </summary>
        /// <param name="kind">敌人类别。</param>
        /// <returns>普通或精英刷新规则。</returns>
        private RegionRespawnRule Rule(RegionEnemyKind kind) => kind == RegionEnemyKind.Elite ? Plan.EliteRespawn : Plan.NormalRespawn;
    }
}
