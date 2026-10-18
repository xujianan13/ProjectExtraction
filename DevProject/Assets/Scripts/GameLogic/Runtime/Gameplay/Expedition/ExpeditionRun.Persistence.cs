using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using GameFramework;
using GameLogic.Gameplay.Combat;
using GameLogic.Gameplay.Level;
using GameLogic.Project;
using GameLogic.Tables;
using UnityEngine;

namespace GameLogic.Gameplay.Expedition
{
    public sealed partial class ExpeditionRun
    {
        public int RoomAdmissionSerial { get; private set; } = 1;
        public bool AdmissionPending { get; private set; } = true;

        /// <summary>
        /// 在完整业务边界生成独立检查点，战斗现场只保留玩家可继承状态。
        /// </summary>
        /// <param name="snapshot">独立的业务检查点。</param>
        /// <param name="error">不能保存的原因。</param>
        /// <returns>玩家和业务状态均成功捕获时为真。</returns>
        public bool TryCaptureState(out ExpeditionRunSnapshot snapshot, out string error)
        {
            snapshot = null;
            if (!World.TryCapturePlayerCheckpoint(Player, out CombatPlayerCheckpoint player, out error)) return false;
            var captured = new ExpeditionRunSnapshot
            {
                ConfigVersion = GameTables.CfgRunPersistence.GetById(1).ConfigVersion, RunId = RunId, LevelId = LevelId,
                Seed = Seed, LootLevel = LootLevel, CurrentRoomIndex = CurrentRoomIndex,
                RoomAdmissionSerial = RoomAdmissionSerial, AdmissionPending = AdmissionPending, ElapsedTicks = elapsedTicks,
                KillExperience = KillExperience, GoldSpent = GoldSpent, AugmentBaseAttackRange = augmentBaseAttackRange,
                LootRandomState = random.State, Requests = regionRequests.ConvertAll(value => value.Copy()),
                Rooms = regions.ConvertAll(ExpeditionRoomCheckpoint.Capture), EntryEquipment = entryEquipment.ConvertAll(value => value.Copy()),
                Inventory = inventory.ConvertAll(value => value.Copy()), Statistics = CaptureStatistics(), PlayerState = player,
                PendingPlayerDeathId = pendingPlayerDeathId, AdExtractionAuthorized = HasAdExtractionAuthorization,
                HasPendingOutcome = pendingOutcome != null, PendingOutcome = pendingOutcome?.Copy()
            };
            CaptureRewardState(captured);
            snapshot = captured;
            return true;
        }

        /// <summary>
        /// 恢复固定房间和业务账本，未完成战斗重建全部敌人并保留收益去重记录。
        /// </summary>
        /// <param name="session">当前账号会话。</param>
        /// <param name="snapshot">已经通过日志完整性检查的业务检查点。</param>
        /// <param name="run">等待场景绑定的运行对象。</param>
        /// <param name="error">版本或内容不兼容的原因。</param>
        /// <returns>玩家与业务状态合法时为真。</returns>
        public static bool TryRestore(ProjectSession session, ExpeditionRunSnapshot snapshot, out ExpeditionRun run, out string error)
        {
            bool restored = TryRestoreCheckpoint(session, snapshot, false, out run, out error);
            if (!restored && string.IsNullOrEmpty(error)) error = "关卡检查点的房间、玩家或收益事实不一致。";
            return restored;
        }

        /// <summary>
        /// 在重放账号账本前完整验证冷读取的检查点，终局记录也必须与房间及物品事实一致。
        /// </summary>
        /// <param name="session">对应活动关卡的账号会话。</param>
        /// <param name="snapshot">尚未同步到账号的已保存检查点。</param>
        /// <param name="error">检查点不合法的原因。</param>
        /// <returns>检查点全部事实合法且不修改账号时为真。</returns>
        internal static bool ValidateSavedCheckpoint(ProjectSession session, ExpeditionRunSnapshot snapshot, out string error)
        {
            if (!TryRestoreCheckpoint(session, snapshot, true, out var inspected, out error))
            {
                if (string.IsNullOrEmpty(error)) error = "已保存关卡的房间、玩家或收益事实不一致。";
                return false;
            }
            inspected.Dispose();
            return true;
        }

        /// <summary>
        /// 统一验证并重建检查点，终局检查只供恢复入口校验，不开放运行。
        /// </summary>
        /// <param name="session">当前账号会话。</param>
        /// <param name="snapshot">日志读取的业务检查点。</param>
        /// <param name="allowOutcome">是否允许仅验证已经冻结的终局。</param>
        /// <param name="run">完成验证的临时运行对象。</param>
        /// <param name="error">检查点错误说明。</param>
        /// <returns>全部玩家和业务事实合法时为真。</returns>
        private static bool TryRestoreCheckpoint(ProjectSession session, ExpeditionRunSnapshot snapshot, bool allowOutcome,
            out ExpeditionRun run, out string error)
        {
            run = null;
            error = "关卡检查点身份、版本或结构无效，原记录已保留。";
            CfgRunPersistenceRow persistence = GameTables.CfgRunPersistence.GetById(1);
            if (session == null || snapshot == null || snapshot.Version != 3 || snapshot.ConfigVersion != persistence.ConfigVersion ||
                string.IsNullOrWhiteSpace(snapshot.RunId) || snapshot.PlayerState == null || snapshot.HasPendingOutcome && !allowOutcome ||
                snapshot.RoomAdmissionSerial < 1 || snapshot.ElapsedTicks < 0 ||
                snapshot.KillExperience < 0 || snapshot.GoldSpent < 0 || snapshot.GoldSpent > session.Gold ||
                !RegionValueRules.Finite(snapshot.AugmentBaseAttackRange) || snapshot.AugmentBaseAttackRange <= 0 ||
                snapshot.AugmentBaseAttackRange > ProjectEquipmentRules.MaxAttackRange ||
                !LevelContentConfig.TryGetLevel(snapshot.LevelId, out LevelDefinition level) || snapshot.LootLevel != level.LootLevel ||
                snapshot.Requests == null || snapshot.Rooms == null || snapshot.Statistics == null ||
                snapshot.CurrentRoomIndex < 0 || snapshot.CurrentRoomIndex >= snapshot.Requests.Count ||
                !LevelContentConfig.TryCreateRoomRequests(snapshot.RunId, snapshot.Seed, level, out var expected, out error) ||
                expected.Count != snapshot.Requests.Count || !ProjectRewardConfig.Validate(out error)) return false;
            for (int index = 0; index < expected.Count; index++)
                if (snapshot.Requests[index] == null || JsonUtility.ToJson(expected[index]) != JsonUtility.ToJson(snapshot.Requests[index]))
                { error = "固定房间顺序或配置身份已变化，不能恢复旧检查点。"; return false; }
            var restoredRooms = new List<RegionRuntimeState>();
            var roomIndices = new HashSet<int>();
            foreach (ExpeditionRoomCheckpoint checkpoint in snapshot.Rooms)
            {
                if (checkpoint?.Plan?.Request == null) return false;
                int index = checkpoint.Plan.Request.RegionIndex;
                if (index < 0 || index > snapshot.CurrentRoomIndex || !roomIndices.Add(index) ||
                    JsonUtility.ToJson(checkpoint.Plan.Request) != JsonUtility.ToJson(expected[index]) ||
                    index < snapshot.CurrentRoomIndex && !checkpoint.Completed || !checkpoint.TryRestore(out var room, out error)) return false;
                restoredRooms.Add(room);
            }
            for (int index = 0; index < snapshot.CurrentRoomIndex; index++)
                if (!roomIndices.Contains(index)) { error = "已经通过的房间缺少完成记录。"; return false; }
            if (!roomIndices.Contains(snapshot.CurrentRoomIndex) && !snapshot.AdmissionPending)
            { error = "当前已入场房间缺少内容记录。"; return false; }
            if (!ValidateSnapshotItems(snapshot, out error) || !ValidateStatisticsSnapshot(snapshot)) return false;
            if (!ValidateCheckpointOutcome(snapshot, out error)) return false;
            if (session.ActiveRunId == snapshot.RunId && !SameItems(snapshot.EntryEquipment, session.EntryItems))
            { error = "携入装备与账号托管记录不一致。"; return false; }
            CombatPlayerCheckpoint playerState = snapshot.PlayerState;
            if (!ProjectWeaponCombat.TryGetBasicSkill(snapshot.EntryEquipment, out int skill) ||
                !MatchesSkillLoadout(playerState.Spec, skill) || !MatchesSkillLoadout(playerState.BaseSpec, skill) ||
                snapshot.PendingPlayerDeathId < 0 || snapshot.PendingPlayerDeathId > playerState.DeathIdWatermark ||
                (snapshot.PendingPlayerDeathId > 0) != (playerState.Health <= 0))
            { error = "玩家技能、生命或死亡选择记录不一致。"; return false; }
            if (!PersistentRandom.TryRestore(snapshot.LootRandomState, out var restoredRandom)) return false;
            var created = new ExpeditionRun(session, snapshot.RunId, level, snapshot.Seed, expected, snapshot.EntryEquipment);
            created.CurrentRoomIndex = snapshot.CurrentRoomIndex;
            created.RoomAdmissionSerial = snapshot.RoomAdmissionSerial;
            created.AdmissionPending = snapshot.AdmissionPending;
            created.elapsedTicks = snapshot.ElapsedTicks;
            created.KillExperience = snapshot.KillExperience;
            created.GoldSpent = snapshot.GoldSpent;
            created.pendingPlayerDeathId = snapshot.PendingPlayerDeathId;
            created.HasAdExtractionAuthorization = snapshot.AdExtractionAuthorized;
            created.augmentBaseAttackRange = snapshot.AugmentBaseAttackRange;
            created.random = restoredRandom;
            created.inventory.AddRange(snapshot.Inventory.ConvertAll(value => value.Copy()));
            created.regions.AddRange(restoredRooms);
            created.statistics = snapshot.Statistics.Copy();
            var world = created.CreateRoomWorld(expected[snapshot.CurrentRoomIndex].RegionId);
            Vector3 entry = created.CurrentRoom == null ? Vector3.zero : created.CurrentRoom.Plan.EntryPosition;
            if (!world.TryRestorePlayerCheckpoint(playerState, expected[snapshot.CurrentRoomIndex].RegionId, entry,
                out CombatEntityRef player, out error))
            { world.Dispose(); created.Dispose(); return false; }
            created.World.Dispose();
            created.World = world;
            created.Player = player;
            if (!created.RestoreRewardState(snapshot, out error) ||
                !created.TryBuildEquipmentAugments(created.entryEquipment, out created.equipmentAugments) ||
                !created.RefreshPermanentAttributes())
            { created.Dispose(); return false; }
            run = created;
            error = null;
            return true;
        }

        /// <summary>
        /// 验证终局与检查点的累计数值、物品、广告授权和房间完成证据一致。
        /// </summary>
        /// <param name="snapshot">完整业务检查点。</param>
        /// <param name="error">终局不一致的原因。</param>
        /// <returns>终局缺席或其全部事实吻合时为真。</returns>
        private static bool ValidateCheckpointOutcome(ExpeditionRunSnapshot snapshot, out string error)
        {
            error = "终局与房间检查点不一致。";
            ProjectRunOutcome outcome = snapshot.PendingOutcome;
            if (!snapshot.HasPendingOutcome)
                return outcome == null || string.IsNullOrEmpty(outcome.RunId) && outcome.RulesVersion == 0 && outcome.KillExperience == 0;
            if (outcome == null || outcome.RunId != snapshot.RunId || outcome.LevelId != snapshot.LevelId || outcome.Seed != snapshot.Seed ||
                outcome.RulesVersion != ProjectRunRecordRules.RulesVersion || outcome.Tick != snapshot.ElapsedTicks ||
                outcome.KillExperience != snapshot.KillExperience || outcome.AwardedKillExperience != snapshot.KillExperience ||
                outcome.GoldSpent != snapshot.GoldSpent || outcome.Items == null || !SameItems(outcome.Items, snapshot.Inventory) ||
                JsonUtility.ToJson(outcome.Statistics) != JsonUtility.ToJson(snapshot.Statistics) ||
                !ProjectRunRecordRules.TryClassifyItems(outcome, snapshot.EntryEquipment, out _, out _, out error)) return false;
            if (outcome.ExitKind == ProjectRunExitKind.Completed)
            {
                ExpeditionRoomCheckpoint final = snapshot.Rooms.Find(value => value.Plan.Request.RegionIndex == snapshot.CurrentRoomIndex);
                if (snapshot.CurrentRoomIndex != snapshot.Requests.Count - 1 || final == null || !final.BossRewardsCompleted ||
                    snapshot.PendingPlayerDeathId != 0 || outcome.ExtractionExperience != PrototypeRules.ExtractionExperience) return false;
            }
            else if (outcome.ExtractionExperience != 0 ||
                outcome.ExitKind == ProjectRunExitKind.Death && snapshot.PendingPlayerDeathId <= 0 ||
                outcome.ExitKind == ProjectRunExitKind.AdExtraction && !snapshot.AdExtractionAuthorized) return false;
            error = null;
            return true;
        }

        /// <summary>
        /// 比较携入物品与托管快照，顺序不影响唯一实例归属。
        /// </summary>
        /// <param name="left">关卡记录的携入物品。</param>
        /// <param name="right">账号托管记录。</param>
        /// <returns>所有实例及属性均一致时为真。</returns>
        private static bool SameItems(IReadOnlyList<ProjectItem> left, IReadOnlyList<ProjectItem> right)
        {
            if (left.Count != right.Count) return false;
            foreach (ProjectItem item in left)
            {
                bool found = false;
                foreach (ProjectItem other in right)
                    if (item.Id == other.Id && JsonUtility.ToJson(item) == JsonUtility.ToJson(other)) { found = true; break; }
                if (!found) return false;
            }
            return true;
        }

        /// <summary>
        /// 校验携入装备、关卡背包和未领取候选的唯一归属。
        /// </summary>
        /// <param name="snapshot">需要校验的业务检查点。</param>
        /// <param name="error">物品归属错误说明。</param>
        /// <returns>没有复制实例或损坏物品时为真。</returns>
        private static bool ValidateSnapshotItems(ExpeditionRunSnapshot snapshot, out string error)
        {
            error = "关卡物品归属数据无效。";
            if (snapshot.EntryEquipment == null || snapshot.Inventory == null || snapshot.BossLoot?.Batches == null) return false;
            var owned = new HashSet<string>(StringComparer.Ordinal);
            var origins = new HashSet<string>(StringComparer.Ordinal);
            var equipped = new HashSet<ProjectItemType>();
            foreach (ProjectItem item in snapshot.EntryEquipment)
                if (ProjectEquipmentRules.ValidateItem(item) != null || !item.Equipped || !owned.Add(item.Id) || !equipped.Add(item.Type)) return false;
            foreach (ProjectItem item in snapshot.Inventory)
                if (ProjectEquipmentRules.ValidateItem(item) != null || item.Equipped || !owned.Add(item.Id)) return false;
            foreach (ExpeditionRoomCheckpoint room in snapshot.Rooms)
            {
                foreach (RegionChestPlan chest in room.Plan.Chests)
                {
                    RegionChestState state = room.Chests.Find(value => value.Id == chest.Id);
                    if (state == null) return false;
                    foreach (ProjectItem item in chest.Items)
                        if (!origins.Add(item.Id) || !state.ClaimedItemIds.Contains(item.Id) && !owned.Add(item.Id)) return false;
                }
                foreach (RegionNpcPlan npc in room.Plan.Npcs)
                {
                    RegionNpcState state = room.Npcs.Find(value => value.Id == npc.Id);
                    if (state == null) return false;
                    foreach (RegionNpcOfferPlan offer in npc.Offers)
                        if (!origins.Add(offer.Item.Id) || !state.SoldOfferIds.Contains(offer.Id) && !owned.Add(offer.Item.Id)) return false;
                }
            }
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
        /// 保存已完成的业务变化，确认前保持交互门禁。
        /// </summary>
        /// <param name="message">保存成功后公布的结果。</param>
        /// <param name="confirmed">确认后仅执行一次的后续操作。</param>
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
        /// 等待冻结检查点和账号账本共同确认，不从变化后的内存重建重试候选。
        /// </summary>
        /// <param name="snapshot">冻结的业务检查点。</param>
        /// <param name="message">成功说明。</param>
        /// <param name="confirmed">确认后的后续操作。</param>
        /// <returns>完整事务等待任务。</returns>
        private async UniTask CommitMutationAsync(ExpeditionRunSnapshot snapshot, string message, Action confirmed)
        {
            bool accepted = await ProjectServices.Coordinator.SaveSnapshotBarrierAsync(snapshot, ProjectRunPhase.Playing);
            if (!accepted || disposed) return;
            if (!RefreshPermanentAttributes()) { HandleFault("永久成长属性未能应用。"); return; }
            committing = false;
            Message = message;
            confirmed?.Invoke();
            PublishRewardState();
            Changed?.Invoke();
        }
    }
}
