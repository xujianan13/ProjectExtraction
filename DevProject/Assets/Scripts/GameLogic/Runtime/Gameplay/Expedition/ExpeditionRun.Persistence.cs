using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using GameFramework;
using GameLogic.Gameplay.Combat;
using GameLogic.Gameplay.Level;
using GameLogic.Project;
using GameLogic.Tables;

namespace GameLogic.Gameplay.Expedition
{
    public sealed partial class ExpeditionRun
    {
        /// <summary>
        /// 在完整逻辑边界生成独立候选，快照不包含窗口或表现对象。
        /// </summary>
        /// <param name="snapshot">完整运行快照。</param>
        /// <param name="error">不能保存的原因。</param>
        /// <returns>战斗和业务状态均处于完整边界时为真。</returns>
        public bool TryCaptureState(out ExpeditionRunSnapshot snapshot, out string error)
        {
            snapshot = null;
            if (!World.TryExportState(out CombatWorldSnapshot world, out error)) return false;
            var captured = new ExpeditionRunSnapshot
            {
                ConfigVersion = GameTables.CfgRunPersistence.GetById(1).ConfigVersion, RunId = RunId, LevelId = LevelId,
                Seed = Seed, LootLevel = LootLevel, CurrentRegionIndex = CurrentRegionIndex, NextActorId = nextActorId,
                KillExperience = KillExperience, GoldSpent = GoldSpent, AugmentBaseAttackRange = augmentBaseAttackRange,
                LootRandomState = random.State, RegionInitialized = CurrentRegion != null,
                Requests = regionRequests.ConvertAll(value => value.Copy()), Regions = regions.ConvertAll(value => value.Copy()),
                Inventory = inventory.ConvertAll(value => value.Copy()),
                Drops = drops.ConvertAll(value => value.Copy()), Statistics = CaptureStatistics(), World = world,
                PendingPlayerDeathId = pendingPlayerDeathId, HasPendingOutcome = pendingOutcome != null, PendingOutcome = pendingOutcome?.Copy()
            };
            CaptureRewardState(captured);
            snapshot = captured;
            return true;
        }

        /// <summary>
        /// 恢复同一配置版本和身份的完整关卡，不重抽区域或奖励。
        /// </summary>
        /// <param name="session">当前账号唯一会话。</param>
        /// <param name="snapshot">已通过日志完整性检查的快照。</param>
        /// <param name="run">等待绑定资源的恢复单局。</param>
        /// <param name="error">版本或内容不兼容的原因。</param>
        /// <returns>完整世界和奖励队列恢复成功时为真。</returns>
        public static bool TryRestore(ProjectSession session, ExpeditionRunSnapshot snapshot, out ExpeditionRun run, out string error)
        {
            run = null;
            error = "运行存档身份、版本或结构无效。";
            CfgRunPersistenceRow persistence = GameTables.CfgRunPersistence?.GetById(1);
            if (session == null || persistence == null || snapshot == null || snapshot.Version != 1 ||
                snapshot.ConfigVersion != persistence.ConfigVersion ||
                string.IsNullOrWhiteSpace(snapshot.RunId) || snapshot.World == null ||
                snapshot.HasPendingOutcome || snapshot.World.Actors == null || snapshot.World.Rooms == null ||
                float.IsNaN(snapshot.AugmentBaseAttackRange) || float.IsInfinity(snapshot.AugmentBaseAttackRange) ||
                snapshot.AugmentBaseAttackRange <= 0 || snapshot.AugmentBaseAttackRange > ProjectEquipmentRules.MaxAttackRange ||
                !LevelContentConfig.TryGetLevel(snapshot.LevelId, out LevelDefinition level) || snapshot.LootLevel != level.LootLevel ||
                snapshot.Requests == null || snapshot.Requests.Count != level.Row.RegionCount + 1 || snapshot.Regions == null ||
                snapshot.CurrentRegionIndex < 0 || snapshot.CurrentRegionIndex >= snapshot.Requests.Count ||
                snapshot.NextActorId <= snapshot.World.HighestActorId || snapshot.KillExperience < 0 || snapshot.GoldSpent < 0 ||
                snapshot.GoldSpent > session.Gold || snapshot.Statistics == null || !ValidateSnapshotItems(snapshot, out error) ||
                !ProjectV2RewardConfig.Validate(out error)) return false;
            foreach (CombatActorState actor in snapshot.World.Actors) if (actor == null) return false;
            var regionIds = new HashSet<int>();
            for (int index = 0; index < snapshot.Requests.Count; index++)
            {
                RegionGenerationRequest request = snapshot.Requests[index];
                CfgRegionMapsRow map = request != null ? GameTables.CfgRegionMaps.GetById(request.MapId) : null;
                CfgBossesRow boss = request != null ? GameTables.CfgBosses.GetById(request.BossDefinitionId) : null;
                if (request == null || request.RunId != snapshot.RunId || request.RegionIndex != index || request.LevelId != snapshot.LevelId ||
                    request.ConfigVersion != snapshot.ConfigVersion || map == null || map.Version != request.MapVersion || boss == null ||
                    boss.MapID != request.MapId || boss.EnemyID != request.BossEnemyId || !regionIds.Add(request.RegionId))
                { error = "已冻结的Boss、地图或内容版本不可用。"; return false; }
            }
            int activeRegionId = snapshot.Requests[snapshot.CurrentRegionIndex].RegionId;
            CombatActorState savedPlayer = snapshot.World.Actors.Find(value => value.Spec.ActorId == 1);
            if (snapshot.World.ActiveRoomId != activeRegionId || savedPlayer == null || !savedPlayer.Spec.IsPlayer ||
                savedPlayer.Spec.RoomId != activeRegionId ||
                snapshot.World.PlayerKey != savedPlayer.Key)
            {
                error = "活动区域与玩家战斗身份不一致。";
                return false;
            }
            if (!ProjectWeaponCombat.TryGetBasicSkill(snapshot.Inventory, out int basicSkillId) ||
                !MatchesSkillLoadout(savedPlayer.Spec, basicSkillId) || !MatchesSkillLoadout(savedPlayer.BaseSpec, basicSkillId))
            {
                error = "玩家已装备武器与保存的技能列表不一致。";
                return false;
            }
            bool playerDead = savedPlayer.Health <= 0 && savedPlayer.DeadPending && savedPlayer.DeathPublished;
            if (snapshot.PendingPlayerDeathId < 0 || snapshot.PendingPlayerDeathId > snapshot.World.NextDeathId ||
                (snapshot.PendingPlayerDeathId > 0) != playerDead)
            {
                error = "玩家生命与待确认死亡状态不一致。";
                return false;
            }
            regionIds.Clear();
            var actorIds = new HashSet<int> { 1 };
            foreach (RegionRuntimeState region in snapshot.Regions)
            {
                if (region?.Plan?.Request == null || !region.Plan.TryValidateStructure(out error) || region.Slots == null ||
                    !regionIds.Add(region.Plan.Request.RegionId) || region.Plan.Request.RegionIndex > snapshot.CurrentRegionIndex ||
                    region.Plan.Request.RegionIndex < 0 || region.Plan.Request.RegionIndex >= snapshot.Requests.Count ||
                    UnityEngine.JsonUtility.ToJson(region.Plan.Request) != UnityEngine.JsonUtility.ToJson(snapshot.Requests[region.Plan.Request.RegionIndex])) return false;
                var slotIds = new HashSet<string>(StringComparer.Ordinal);
                if (region.Slots.Count != region.Plan.SpawnSlots.Count) return false;
                foreach (RegionSpawnSlotState slot in region.Slots)
                {
                    CombatActorState actor = slot == null ? null : snapshot.World.Actors.Find(value => value.Spec.ActorId == slot.CurrentActorId);
                    if (slot == null || !slotIds.Add(slot.SlotId) || !region.Plan.SpawnSlots.Exists(value => value.SlotId == slot.SlotId) ||
                        slot.CurrentActorId <= 1 || !actorIds.Add(slot.CurrentActorId) || actor == null || actor.Spec.IsPlayer ||
                        actor.Spec.RoomId != region.Plan.Request.RegionId ||
                        !Enum.IsDefined(typeof(RegionSpawnPhase), slot.Phase) || slot.Phase == RegionSpawnPhase.InitialSpawn ||
                        (slot.Phase == RegionSpawnPhase.Alive ? actor.Health <= 0 || actor.DeadPending || actor.DeathPublished :
                            actor.Health > 0 || !actor.DeadPending || !actor.DeathPublished))
                    { error = "敌人生命与区域槽位无法一一对应。"; return false; }
                }
            }
            if (snapshot.RegionInitialized != regionIds.Contains(snapshot.Requests[snapshot.CurrentRegionIndex].RegionId) ||
                actorIds.Count != snapshot.World.Actors.Count) { error = "区域生成记录与战斗角色不一致。"; return false; }
            if (!ValidateStatisticsSnapshot(snapshot))
            { error = "活动关卡统计版本、生命分类或区域时钟不一致，保留原存档且不自动迁移。"; return false; }
            if (!PersistentRandom.TryRestore(snapshot.LootRandomState, out PersistentRandom restoredRandom)) return false;
            if (!CombatWorld.TryRestore(CombatContentConfig.CreateCatalog(snapshot.AugmentBaseAttackRange), snapshot.World,
                out CombatWorld restoredWorld, out error)) return false;
            if (!restoredWorld.TryFindActor(1, out CombatEntityRef player))
            {
                restoredWorld.Dispose();
                return false;
            }
            var created = new ExpeditionRun(session, snapshot.RunId, level, snapshot.Seed, snapshot.Requests, snapshot.Inventory, null, null);
            CombatWorld initialWorld = created.World;
            created.World = restoredWorld;
            created.Player = player;
            initialWorld.Dispose();
            created.CurrentRegionIndex = snapshot.CurrentRegionIndex;
            created.nextActorId = snapshot.NextActorId;
            created.KillExperience = snapshot.KillExperience;
            created.GoldSpent = snapshot.GoldSpent;
            created.pendingPlayerDeathId = snapshot.PendingPlayerDeathId;
            created.augmentBaseAttackRange = snapshot.AugmentBaseAttackRange;
            created.random = restoredRandom;
            created.regions.AddRange(snapshot.Regions.ConvertAll(value => value.Copy()));
            created.drops.AddRange(snapshot.Drops.ConvertAll(value => value.Copy()));
            created.statistics = snapshot.Statistics.Copy();
            foreach (RegionRuntimeState region in created.regions)
                foreach (RegionSpawnSlotState slot in region.Slots)
                {
                    RegionSpawnSlotPlan plan = region.Plan.SpawnSlots.Find(value => value.SlotId == slot.SlotId);
                    if (plan == null || !LevelContentConfig.TryGetEnemy(plan.EnemyId, out EnemyDefinition enemy)) { created.Dispose(); return false; }
                    created.enemyDefinitions.Add(slot.CurrentActorId, enemy);
                }
            if (!created.RestoreRewardState(snapshot, out error) ||
                !created.TryBuildEquipmentAugments(created.inventory, out created.equipmentAugments)) { created.Dispose(); return false; }
            restoredWorld.ErrorLogger = reason => GameEntry.Get<ILogModule>().Error("Combat", reason);
            run = created;
            error = null;
            return true;
        }

        /// <summary>
        /// 校验实际持有容器，拒绝复制实例或损坏物品。
        /// </summary>
        /// <param name="snapshot">需要校验的快照。</param>
        /// <param name="error">容器错误说明。</param>
        /// <returns>每个活跃物品实例只有一个归属时为真。</returns>
        private static bool ValidateSnapshotItems(ExpeditionRunSnapshot snapshot, out string error)
        {
            error = "局内物品归属数据无效。";
            if (snapshot.Inventory == null || snapshot.Drops == null) return false;
            var owned = new HashSet<string>(StringComparer.Ordinal);
            var origins = new HashSet<string>(StringComparer.Ordinal);
            var equipped = new HashSet<ProjectItemType>();
            foreach (ProjectItem item in snapshot.Inventory)
                if (ProjectEquipmentRules.ValidateItem(item) != null || !owned.Add(item.Id) || item.Equipped && !equipped.Add(item.Type)) return false;
            foreach (ExpeditionDrop drop in snapshot.Drops)
                if (drop == null || !RegionValueRules.Finite(drop.Position) ||
                    !snapshot.Requests.Exists(value => value != null && value.RegionId == drop.RoomId) ||
                    ProjectEquipmentRules.ValidateItem(drop.Item) != null || !origins.Add(drop.Item.Id) ||
                    !drop.Collected && !owned.Add(drop.Item.Id)) return false;
            foreach (RegionRuntimeState region in snapshot.Regions)
            {
                if (region?.Plan == null || !region.Plan.TryValidateStructure(out error) || region.Chests == null || region.Npcs == null ||
                    region.Chests.Count != region.Plan.Chests.Count || region.Npcs.Count != region.Plan.Npcs.Count) return false;
                var chestStates = new Dictionary<string, RegionChestState>(StringComparer.Ordinal);
                foreach (RegionChestState state in region.Chests)
                    if (state == null || string.IsNullOrWhiteSpace(state.Id) || state.ClaimedItemIds == null ||
                        !chestStates.TryAdd(state.Id, state) || !state.Opened && state.ClaimedItemIds.Count > 0) return false;
                foreach (RegionChestPlan chest in region.Plan.Chests)
                {
                    if (!chestStates.TryGetValue(chest.Id, out RegionChestState state)) return false;
                    var claims = new HashSet<string>(StringComparer.Ordinal);
                    foreach (string id in state.ClaimedItemIds)
                        if (string.IsNullOrWhiteSpace(id) || !claims.Add(id) || !chest.Items.Exists(value => value.Id == id)) return false;
                    foreach (ProjectItem item in chest.Items)
                        if (!origins.Add(item.Id) || !claims.Contains(item.Id) && !owned.Add(item.Id)) return false;
                }
                var npcStates = new Dictionary<string, RegionNpcState>(StringComparer.Ordinal);
                foreach (RegionNpcState state in region.Npcs)
                    if (state == null || string.IsNullOrWhiteSpace(state.Id) || state.SoldOfferIds == null ||
                        !npcStates.TryAdd(state.Id, state) || !state.Visited && state.SoldOfferIds.Count > 0) return false;
                foreach (RegionNpcPlan npc in region.Plan.Npcs)
                {
                    if (!npcStates.TryGetValue(npc.Id, out RegionNpcState state)) return false;
                    var sold = new HashSet<string>(StringComparer.Ordinal);
                    foreach (string id in state.SoldOfferIds)
                        if (string.IsNullOrWhiteSpace(id) || !sold.Add(id) || !npc.Offers.Exists(value => value.Id == id)) return false;
                    foreach (RegionNpcOfferPlan offer in npc.Offers)
                        if (!origins.Add(offer.Item.Id) || !sold.Contains(offer.Id) && !owned.Add(offer.Item.Id)) return false;
                }
            }
            if (snapshot.BossLoot?.Batches == null) return false;
            foreach (var batch in snapshot.BossLoot.Batches)
            {
                if (batch?.Rounds == null) return false;
                foreach (var round in batch.Rounds)
                {
                    if (round?.Options == null || round.SelectedIndex < -1 || round.SelectedIndex >= round.Options.Count) return false;
                    for (int index = 0; index < round.Options.Count; index++)
                    {
                        ProjectItem item = round.Options[index];
                        if (ProjectEquipmentRules.ValidateItem(item) != null || !origins.Add(item.Id) ||
                            index != round.SelectedIndex && !owned.Add(item.Id)) return false;
                    }
                }
            }
            error = null;
            return true;
        }

        /// <summary>
        /// 将当前完整帧加入唯一保存泵；达到预算后由入口冻结后续模拟。
        /// </summary>
        /// <returns>候选已接纳时为真。</returns>
        private bool QueueTickSnapshot()
        {
            if (!TryCaptureState(out ExpeditionRunSnapshot snapshot, out string error)) { HandleFault(error); return false; }
            if (session.EnqueueRunState(snapshot)) return true;
            HandleFault("运行快照未进入保存队列，已停止后续模拟。");
            return false;
        }

        /// <summary>
        /// 为已校验并完成的业务变化保存完整候选，确认前保持交互门禁。
        /// </summary>
        /// <param name="message">保存成功后公布的结果。</param>
        /// <param name="confirmed">持久确认后仅执行一次的后续操作。</param>
        private void SaveMutation(string message, Action confirmed = null)
        {
            committing = true;
            ClearPlayerInput();
            events.Clear();
            World.DrainEvents(events);
            foreach (CombatEvent fact in events) { RecordStatistics(fact); CombatEventPublished?.Invoke(fact); }
            if (!TryCaptureState(out ExpeditionRunSnapshot snapshot, out string error)) { HandleFault(error); return; }
            CommitMutationAsync(snapshot, message, confirmed).Forget();
        }

        /// <summary>
        /// 等待原候选持久确认或全局重试，不从变化后的内存重新生成候选。
        /// </summary>
        /// <param name="snapshot">冻结的完整候选。</param>
        /// <param name="message">成功说明。</param>
        /// <param name="confirmed">确认后的后续操作。</param>
        /// <returns>完整事务等待任务。</returns>
        private async UniTask CommitMutationAsync(ExpeditionRunSnapshot snapshot, string message, Action confirmed)
        {
            bool accepted = await ProjectServices.Coordinator.SaveSnapshotBarrierAsync(snapshot, ProjectRunPhase.Playing);
            if (!accepted || disposed) return;
            committing = false;
            Message = message;
            confirmed?.Invoke();
            PublishRewardState();
            Changed?.Invoke();
        }
    }
}
