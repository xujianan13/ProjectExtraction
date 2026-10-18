using System;
using System.Collections.Generic;
using GameFramework;
using UnityEngine;

namespace GameLogic.Project
{
    /// <summary>
    /// 单局记录的持久阶段。
    /// </summary>
    public enum ProjectRunStage { None, Admitted, Running, OutcomeRecorded }

    /// <summary>
    /// 本局唯一终局原因，暂离主城不属于终局。
    /// </summary>
    public enum ProjectRunExitKind { Completed, Death, Abandoned, AdExtraction }

    /// <summary>
    /// 已冻结的单局事实与结算规则参数。
    /// </summary>
    [Serializable]
    public sealed class ProjectRunOutcome
    {
        public string RunId;
        public int LevelId;
        public int Seed;
        public int RulesVersion;
        public long Tick;
        public float ElapsedSeconds;
        public ProjectRunExitKind ExitKind;
        public int KillExperience;
        public int AwardedKillExperience;
        public int ExtractionExperience;
        public int GoldSpent;
        public ProjectRunStatistics Statistics;
        public List<ProjectItem> Items = new();
        public List<string> RetainedItemIds = new();

        /// <summary>
        /// 创建不共享物品集合的终局副本。
        /// </summary>
        /// <returns>独立终局副本。</returns>
        public ProjectRunOutcome Copy()
        {
            var copy = (ProjectRunOutcome)MemberwiseClone();
            copy.Statistics = Statistics?.Copy();
            copy.Items = Items?.ConvertAll(item => item?.Copy());
            copy.RetainedItemIds = RetainedItemIds == null ? null : new List<string>(RetainedItemIds);
            return copy;
        }
    }

    /// <summary>
    /// 已到账的单局结果；仅查看标记允许之后变化。
    /// </summary>
    [Serializable]
    public sealed class ProjectRunResult
    {
        public ProjectRunOutcome Outcome;
        public List<ProjectItem> RetainedItems = new();
        public List<ProjectItem> LostItems = new();
        public bool HasEntryItemsSnapshot;
        public List<ProjectItem> EntryItems = new();
        public int LevelBefore;
        public int LevelAfter;
        public int ExperienceBefore;
        public int ExperienceAfter;
        public int SkillPointsBefore;
        public int SkillPointsAfter;
        public bool HasSkillPointsRuleSnapshot;
        public int SkillPointsPerLevelApplied;
        public int GoldBefore;
        public int GoldAfter;
        public bool Viewed;

        /// <summary>
        /// 创建不共享终局或物品集合的结果副本。
        /// </summary>
        /// <returns>独立结果副本。</returns>
        public ProjectRunResult Copy()
        {
            var copy = (ProjectRunResult)MemberwiseClone();
            copy.Outcome = Outcome?.Copy();
            copy.RetainedItems = RetainedItems?.ConvertAll(item => item?.Copy());
            copy.LostItems = LostItems?.ConvertAll(item => item?.Copy());
            copy.EntryItems = EntryItems?.ConvertAll(item => item?.Copy());
            return copy;
        }
    }

    /// <summary>
    /// 处理 Unity JSON 对可空内联记录生成默认空对象的存档编解码器。
    /// </summary>
    public sealed class ProjectSaveCodec : ISaveCodec<ProjectSaveData>
    {
        /// <summary>
        /// 将已校验候选编码为项目存档正文。
        /// </summary>
        /// <param name="data">独立存档候选。</param>
        /// <returns>项目存档 JSON。</returns>
        public string Encode(ProjectSaveData data) => JsonUtility.ToJson(data);

        /// <summary>
        /// 解码并还原真正缺席的终局及历史结果。
        /// </summary>
        /// <param name="json">项目存档 JSON。</param>
        /// <returns>恢复可空记录语义的存档。</returns>
        public ProjectSaveData Decode(string json)
        {
            ProjectSaveData data = JsonUtility.FromJson<ProjectSaveData>(json);
            if (data == null) return null;
            data.HomeShopPurchases ??= new List<ProjectHomeShopPurchase>();
            data.RunHistory ??= new List<ProjectRunHistoryEntry>();
            data.CollectedCollectionIds ??= new List<string>();
            data.AchievedCollectionIds ??= new List<string>();
            if (data.Preferences == null || data.Preferences.Version == 0) data.Preferences = new ProjectPreferences();
            if (data.Stage != ProjectRunStage.OutcomeRecorded && EmptyOutcome(data.Outcome)) data.Outcome = null;
            if (EmptyResult(data.LastResult)) data.LastResult = null;
            return data;
        }

        /// <summary>
        /// 判断 Unity 内联解码产生的空终局壳。
        /// </summary>
        /// <param name="outcome">解码后的终局对象。</param>
        /// <returns>是否没有实际终局内容。</returns>
        private static bool EmptyOutcome(ProjectRunOutcome outcome) => outcome == null ||
            string.IsNullOrEmpty(outcome.RunId) && outcome.LevelId == 0 && outcome.Seed == 0 && outcome.RulesVersion == 0 &&
            outcome.Tick == 0 && outcome.ElapsedSeconds == 0 && outcome.KillExperience == 0 &&
            outcome.AwardedKillExperience == 0 && outcome.ExtractionExperience == 0 && outcome.GoldSpent == 0 &&
            (outcome.Statistics == null || outcome.Statistics.Version == 0 && ProjectRunStatistics.IsValid(outcome.Statistics)) &&
            (outcome.Items == null || outcome.Items.Count == 0) && (outcome.RetainedItemIds == null || outcome.RetainedItemIds.Count == 0);

        /// <summary>
        /// 判断 Unity 内联解码产生的空结果壳。
        /// </summary>
        /// <param name="result">解码后的结果对象。</param>
        /// <returns>是否没有实际结算内容。</returns>
        private static bool EmptyResult(ProjectRunResult result) => result == null || EmptyOutcome(result.Outcome) &&
            (result.RetainedItems == null || result.RetainedItems.Count == 0) &&
            (result.LostItems == null || result.LostItems.Count == 0) && result.LevelBefore == 0 && result.LevelAfter == 0 &&
            result.ExperienceBefore == 0 && result.ExperienceAfter == 0 && result.SkillPointsBefore == 0 &&
            result.SkillPointsAfter == 0 && result.GoldBefore == 0 && result.GoldAfter == 0 && !result.Viewed;
    }

    /// <summary>
    /// 入场、终局与结算的纯数据事务规则。
    /// </summary>
    public static class ProjectRunRecordRules
    {
        public const int RulesVersion = 2;

        /// <summary>
        /// 创建旧版存档到当前结构的迁移步骤。
        /// </summary>
        /// <returns>从版本一开始的迁移步骤。</returns>
        public static SaveMigration CreateMigration() => new(1, ValidateLegacyJson, MigrateLegacyJson);

        /// <summary>
        /// 校验旧版 JSON 的基础数据。
        /// </summary>
        /// <param name="json">旧版存档正文。</param>
        /// <returns>错误信息，通过时为空。</returns>
        public static string ValidateLegacyJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return "旧版存档为空。";
            ProjectSaveData data = new ProjectSaveCodec().Decode(json);
            if (data == null || data.Level < 1 || data.Experience < 0 || data.Stamina < 0 || data.Gold < 0 ||
                data.Diamonds < 0 || data.SkillPoints < 0 || data.Warehouse == null || data.EntryItems == null)
                return "旧版玩家数据无效。";
            data.Stage = string.IsNullOrEmpty(data.ActiveRunId) ? ProjectRunStage.None : ProjectRunStage.Running;
            data.ActiveRulesVersion = 0;
            data.ActiveLevelId = 0;
            data.Outcome = null;
            data.LastResult = null;
            return ProjectSaveData.Validate(data);
        }

        /// <summary>
        /// 保留旧版入场和摘要事实并转换为当前结构。
        /// </summary>
        /// <param name="json">旧版存档正文。</param>
        /// <returns>新版 JSON 正文。</returns>
        public static string MigrateLegacyJson(string json)
        {
            ProjectSaveData data = new ProjectSaveCodec().Decode(json);
            data.Stage = string.IsNullOrEmpty(data.ActiveRunId) ? ProjectRunStage.None : ProjectRunStage.Running;
            data.ActiveLevelId = 0;
            data.ActiveSeed = 0;
            data.ActiveRulesVersion = 0;
            data.Outcome = null;
            data.LastResult = null;
            return JsonUtility.ToJson(data);
        }

        /// <summary>
        /// 校验单局记录与已到账结果的结构。
        /// </summary>
        /// <param name="data">待检存档。</param>
        /// <param name="error">错误信息。</param>
        /// <returns>记录是否合法。</returns>
        public static bool ValidateRecord(ProjectSaveData data, out string error)
        {
            error = "单局阶段无效。";
            if (data == null || !Enum.IsDefined(typeof(ProjectRunStage), data.Stage)) return false;
            bool active = !string.IsNullOrEmpty(data.ActiveRunId);
            if (active != (data.Stage != ProjectRunStage.None)) return false;
            if (data.ActiveRunAwardedKillExperience < 0 || !active && (data.ActiveLevelId != 0 || data.ActiveSeed != 0 ||
                data.ActiveRulesVersion != 0 || data.ActiveRunVersion != 0 || data.ActiveRunAwardedKillExperience != 0 ||
                data.ActiveRoomAdmissionSerial != 0 || data.Outcome != null)) return false;
            if (active && data.ActiveRunVersion != 0 && data.ActiveRunVersion != 2 && data.ActiveRunVersion != 3) return false;
            if (active && (data.ActiveRunVersion == 3 ? data.ActiveRulesVersion != RulesVersion :
                data.ActiveRulesVersion != 0 && data.ActiveRulesVersion != 1)) return false;
            if (active && data.ActiveRunVersion == 3 && (data.ActiveLevelId < 1 || data.ActiveRoomAdmissionSerial < 1 ||
                data.ActiveRunEntryCost <= 0)) return false;
            if (data.Stage == ProjectRunStage.OutcomeRecorded)
            {
                if (!ValidOutcome(data.Outcome, data, out error)) return false;
            }
            else if (data.Outcome != null) return false;
            error = "已结算结果无效。";
            if (data.LastResult != null && !ValidResult(data.LastResult, data.LastSettledRunId)) return false;
            error = null;
            return true;
        }

        /// <summary>
        /// 一次扣除入场体力并托管携入物品。
        /// </summary>
        /// <param name="data">独立事务候选。</param>
        /// <param name="runId">框架单局身份。</param>
        /// <param name="levelId">已选关卡。</param>
        /// <param name="seed">固定地图种子。</param>
        /// <param name="entryItems">从仓库携入的实例。</param>
        /// <param name="error">失败原因。</param>
        /// <returns>是否完成入场变更。</returns>
        public static bool TryAdmit(ProjectSaveData data, string runId, int levelId, int seed, IReadOnlyList<ProjectItem> entryItems, out string error)
        {
            error = "入场资料无效。";
            if (data == null || string.IsNullOrWhiteSpace(runId) || levelId < 1 || entryItems == null || data.Warehouse == null ||
                data.EntryItems == null) return false;
            if (data.ActiveRunId == runId && data.Stage == ProjectRunStage.Admitted)
            {
                error = "重复入场参数与已保存记录不同。";
                if (data.ActiveRunVersion != 3 || data.ActiveLevelId != levelId || data.ActiveSeed != seed || data.ActiveRulesVersion != RulesVersion ||
                    data.ActiveRunEntryCost <= 0 || data.EntryItems.Count != entryItems.Count) return false;
                var savedItems = new Dictionary<string, ProjectItem>();
                foreach (ProjectItem saved in data.EntryItems)
                    if (saved == null || string.IsNullOrEmpty(saved.Id) || savedItems.ContainsKey(saved.Id)) return false;
                    else savedItems.Add(saved.Id, saved);
                foreach (ProjectItem item in entryItems)
                    if (item == null || !savedItems.TryGetValue(item.Id, out ProjectItem saved) ||
                        JsonUtility.ToJson(item) != JsonUtility.ToJson(saved) || !savedItems.Remove(item.Id)) return false;
                error = null;
                return true;
            }
            error = "已有未结束的单局。";
            if (!string.IsNullOrEmpty(data.ActiveRunId) || data.Stage != ProjectRunStage.None || data.Outcome != null) return false;
            error = "体力不足。";
            var profileRules = ProjectRulesConfig.Profile;
            if (profileRules == null || profileRules.EntryCost < 0 || profileRules.RoomEntryCost < 0 ||
                profileRules.EntryCost > int.MaxValue - profileRules.RoomEntryCost) return false;
            int entryCost = profileRules.EntryCost + profileRules.RoomEntryCost;
            if (entryCost <= 0 || data.Stamina < entryCost) return false;
            var chosen = new List<ProjectItem>();
            var ids = new HashSet<string>();
            error = "携入物品不在仓库或身份重复。";
            foreach (ProjectItem item in entryItems)
            {
                if (item == null || string.IsNullOrEmpty(item.Id) || !ids.Add(item.Id)) return false;
                ProjectItem owned = data.Warehouse.Find(value => value.Id == item.Id);
                if (owned == null || !owned.Equipped || owned.RequiredLevel > data.Level) return false;
                chosen.Add(owned.Copy());
            }
            data.Stamina -= entryCost;
            data.Warehouse.RemoveAll(item => ids.Contains(item.Id));
            data.EntryItems.AddRange(chosen);
            data.ActiveRunId = runId;
            data.ActiveRunEntryCost = entryCost;
            data.ActiveLevelId = levelId;
            data.ActiveSeed = seed;
            data.ActiveRulesVersion = RulesVersion;
            data.ActiveRunVersion = 3;
            data.ActiveRunAwardedKillExperience = 0;
            data.ActiveRoomAdmissionSerial = 1;
            data.Stage = ProjectRunStage.Admitted;
            error = null;
            return true;
        }

        /// <summary>
        /// 标记已经获准开始模拟的单局。
        /// </summary>
        /// <param name="data">独立事务候选。</param>
        /// <param name="runId">当前单局身份。</param>
        /// <param name="error">失败原因。</param>
        /// <returns>是否完成阶段变更。</returns>
        public static bool TryMarkRunning(ProjectSaveData data, string runId, out string error)
        {
            error = "开始授权与当前单局不匹配。";
            if (data == null || string.IsNullOrEmpty(runId) || data.ActiveRunId != runId ||
                data.ActiveRunVersion != 3 || data.ActiveRulesVersion != RulesVersion) return false;
            if (data.Stage == ProjectRunStage.Running) { error = null; return true; }
            if (data.Stage != ProjectRunStage.Admitted) return false;
            data.Stage = ProjectRunStage.Running;
            error = null;
            return true;
        }

        /// <summary>
        /// 冻结终局事实，不在本阶段发放收益。
        /// </summary>
        /// <param name="data">独立事务候选。</param>
        /// <param name="outcome">单局终局事实。</param>
        /// <param name="error">失败原因。</param>
        /// <returns>是否记录或确认了同一终局。</returns>
        public static bool TryRecordOutcome(ProjectSaveData data, ProjectRunOutcome outcome, out string error)
        {
            error = "终局不属于当前运行的单局。";
            if (data == null || data.ActiveRunVersion != 3 ||
                data.Stage != ProjectRunStage.Running && data.Stage != ProjectRunStage.OutcomeRecorded) return false;
            if (!ValidOutcome(outcome, data, out error)) return false;
            if (data.Stage == ProjectRunStage.OutcomeRecorded)
            {
                error = "已有不同的终局记录。";
                if (JsonUtility.ToJson(data.Outcome) != JsonUtility.ToJson(outcome)) return false;
                error = null;
                return true;
            }
            data.Outcome = outcome.Copy();
            data.Stage = ProjectRunStage.OutcomeRecorded;
            error = null;
            return true;
        }

        /// <summary>
        /// 根据已冻结记录一次结算物品、经验和金币。
        /// </summary>
        /// <param name="data">独立事务候选。</param>
        /// <param name="error">失败原因。</param>
        /// <returns>是否结算或确认已结算的同一局。</returns>
        public static bool TrySettleOutcome(ProjectSaveData data, out string error)
        {
            error = "没有可结算的终局。";
            if (data == null) return false;
            if (data.Stage == ProjectRunStage.None && data.LastResult != null && data.LastSettledRunId == data.LastResult.Outcome?.RunId)
            {
                error = null;
                return true;
            }
            if (data.Stage != ProjectRunStage.OutcomeRecorded || !ValidOutcome(data.Outcome, data, out error)) return false;
            ProjectRunOutcome outcome = data.Outcome;
            error = "本局金币支出或经验超过可表示范围。";
            int remainingKillExperience = outcome.KillExperience - data.ActiveRunAwardedKillExperience;
            if (remainingKillExperience < 0 || remainingKillExperience > int.MaxValue - outcome.ExtractionExperience) return false;
            int awarded = remainingKillExperience + outcome.ExtractionExperience;
            int possibleLevels = Math.Max(0, ProjectRulesConfig.Profile.MaxLevel - data.Level);
            if (data.Gold < outcome.GoldSpent || data.Experience > int.MaxValue - awarded ||
                (long)data.SkillPoints + (long)possibleLevels * PrototypeRules.SkillPointsPerLevel > int.MaxValue) return false;
            var warehouseIds = new HashSet<string>();
            foreach (ProjectItem item in data.Warehouse) warehouseIds.Add(item.Id);
            if (!TryClassifyItems(outcome, data.EntryItems, out var retained, out var lost, out error)) return false;
            foreach (ProjectItem item in retained)
                if (warehouseIds.Contains(item.Id))
                {
                    error = "结算物品已存在仓库。";
                    return false;
                }
            var receipt = new ProjectRunResult
            {
                Outcome = outcome.Copy(), RetainedItems = retained.ConvertAll(item => item.Copy()), LostItems = lost.ConvertAll(item => item.Copy()),
                HasEntryItemsSnapshot = true, EntryItems = data.EntryItems.ConvertAll(item => item.Copy()),
                LevelBefore = data.Level, ExperienceBefore = data.Experience, SkillPointsBefore = data.SkillPoints,
                HasSkillPointsRuleSnapshot = true, SkillPointsPerLevelApplied = PrototypeRules.SkillPointsPerLevel, GoldBefore = data.Gold
            };
            if (!ProjectRunHistoryRules.Append(data, receipt, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), ProjectRulesConfig.Profile.RunHistoryLimit))
            {
                error = "单局历史记录参数无效。";
                return false;
            }
            foreach (ProjectItem returned in retained)
                if (returned.Equipped)
                    foreach (ProjectItem stored in data.Warehouse)
                        if (ProjectEquipmentRules.IsArmor(returned.Type) ? ProjectEquipmentRules.IsArmor(stored.Type) : returned.Type == stored.Type)
                            stored.Equipped = false;
            data.Warehouse.AddRange(retained);
            data.Gold -= outcome.GoldSpent;
            ProjectProfileRules.AddExperience(data, awarded);
            receipt.LevelAfter = data.Level;
            receipt.ExperienceAfter = data.Experience;
            receipt.SkillPointsAfter = data.SkillPoints;
            receipt.GoldAfter = data.Gold;
            data.LastSettledRunId = outcome.RunId;
            data.LastRunSummary = outcome.ExitKind == ProjectRunExitKind.Completed ? "最终通关，已带回全部物品。" :
                outcome.ExitKind == ProjectRunExitKind.AdExtraction ? "广告撤离，携入装备与所选本关物品已带回。" :
                outcome.ExitKind == ProjectRunExitKind.Abandoned ? "已放弃本局，携入装备已退回，本关获得物品丢失，体力不退还。" :
                "角色死亡，携入装备已退回，本关获得物品丢失。";
            data.LastResult = receipt;
            data.EntryItems.Clear();
            data.ActiveRunId = "";
            data.ActiveRunEntryCost = 0;
            data.ActiveLevelId = 0;
            data.ActiveSeed = 0;
            data.ActiveRulesVersion = 0;
            data.ActiveRunVersion = 0;
            data.ActiveRunAwardedKillExperience = 0;
            data.ActiveRoomAdmissionSerial = 0;
            data.Outcome = null;
            data.Stage = ProjectRunStage.None;
            error = null;
            return true;
        }

        /// <summary>
        /// 按唯一终局分类携入装备与独立关卡背包；携入装备始终归还，广告只选择本关所得实例。
        /// </summary>
        /// <param name="outcome">冻结终局及关卡背包中的本关所得物。</param>
        /// <param name="entryItems">本次入场托管的原始实例证据。</param>
        /// <param name="retained">成功时返回带回仓库的独立物品副本。</param>
        /// <param name="lost">成功时返回本关损失的独立物品副本。</param>
        /// <param name="error">失败原因，成功时为空。</param>
        /// <returns>物品身份完整且保留与损失形成互斥完整分区时为真。</returns>
        public static bool TryClassifyItems(ProjectRunOutcome outcome, IReadOnlyList<ProjectItem> entryItems,
            out List<ProjectItem> retained, out List<ProjectItem> lost, out string error)
        {
            retained = new List<ProjectItem>();
            lost = new List<ProjectItem>();
            error = "终局类别、物品集合或携入证据无效。";
            if (outcome == null || !Enum.IsDefined(typeof(ProjectRunExitKind), outcome.ExitKind) || outcome.Items == null ||
                outcome.RetainedItemIds == null || entryItems == null) return false;
            var items = new Dictionary<string, ProjectItem>(StringComparer.Ordinal);
            foreach (var item in outcome.Items)
            {
                if (ProjectEquipmentRules.ValidateItem(item) != null || item.Equipped || !items.TryAdd(item.Id, item)) return false;
            }
            error = "携入实例重复、无效或混入关卡背包。";
            var entryIds = new HashSet<string>(StringComparer.Ordinal);
            var equippedSlots = new HashSet<ProjectItemType>();
            foreach (var item in entryItems)
            {
                if (ProjectEquipmentRules.ValidateItem(item) != null || !entryIds.Add(item.Id) || items.ContainsKey(item.Id)) return false;
                if (!item.Equipped) continue;
                var slot = ProjectEquipmentRules.IsArmor(item.Type) ? ProjectItemType.Armor : ProjectItemType.Weapon;
                if (!equippedSlots.Add(slot)) return false;
            }
            error = "广告选取包含重复、外来或受保护携入实例，或者当前终局不允许额外选取。";
            var selectedIds = new HashSet<string>(StringComparer.Ordinal);
            if (outcome.ExitKind != ProjectRunExitKind.AdExtraction && outcome.RetainedItemIds.Count != 0) return false;
            if (outcome.ExitKind == ProjectRunExitKind.AdExtraction)
            {
                var rules = ProjectRulesConfig.Profile;
                if (rules == null || rules.AdExtractionItemLimit < 0 || outcome.RetainedItemIds.Count > rules.AdExtractionItemLimit) return false;
                foreach (var id in outcome.RetainedItemIds)
                    if (string.IsNullOrWhiteSpace(id) || !selectedIds.Add(id) || entryIds.Contains(id) || !items.ContainsKey(id)) return false;
            }
            foreach (var item in entryItems) retained.Add(item.Copy());
            foreach (var item in outcome.Items)
                if (outcome.ExitKind == ProjectRunExitKind.Completed || selectedIds.Contains(item.Id))
                    retained.Add(item.Copy());
                else lost.Add(item.Copy());
            error = null;
            return true;
        }

        /// <summary>
        /// 单独确认已保存结果在返场后被查看。
        /// </summary>
        /// <param name="data">独立事务候选。</param>
        /// <param name="runId">结果所属单局。</param>
        /// <param name="error">失败原因。</param>
        /// <returns>是否设置或确认了查看标记。</returns>
        public static bool TryAcknowledgeResult(ProjectSaveData data, string runId, out string error)
        {
            error = "待查看结果与单局不匹配。";
            if (data == null || data.LastResult?.Outcome == null || data.LastResult.Outcome.RunId != runId ||
                data.LastSettledRunId != runId || data.Stage != ProjectRunStage.None) return false;
            data.LastResult.Viewed = true;
            error = null;
            return true;
        }

        /// <summary>
        /// 校验历史回执的明细分区与数值范围。
        /// </summary>
        /// <param name="result">已保存结果。</param>
        /// <param name="settledRunId">最近结算身份。</param>
        /// <returns>结果是否完整。</returns>
        private static bool ValidResult(ProjectRunResult result, string settledRunId)
        {
            ProjectRunOutcome outcome = result.Outcome;
            if (outcome == null || outcome.RunId != settledRunId || result.RetainedItems == null || result.LostItems == null ||
                !result.HasEntryItemsSnapshot || result.EntryItems == null ||
                result.LevelBefore < 1 || result.LevelAfter < result.LevelBefore || result.ExperienceBefore < 0 ||
                result.ExperienceAfter < 0 || result.SkillPointsBefore < 0 || result.SkillPointsAfter < result.SkillPointsBefore ||
                result.GoldBefore < 0 || result.GoldAfter < 0 || result.GoldBefore < outcome.GoldSpent ||
                result.GoldAfter != result.GoldBefore - outcome.GoldSpent ||
                result.SkillPointsPerLevelApplied < 0) return false;
            int gainedLevels = result.LevelAfter - result.LevelBefore;
            int gainedPoints = result.SkillPointsAfter - result.SkillPointsBefore;
            if (result.HasSkillPointsRuleSnapshot)
            {
                if ((long)gainedLevels * result.SkillPointsPerLevelApplied != gainedPoints) return false;
            }
            else if (gainedLevels == 0 ? gainedPoints != 0 : gainedPoints % gainedLevels != 0) return false;
            var identity = new ProjectSaveData
            {
                ActiveRunId = outcome.RunId, ActiveLevelId = outcome.LevelId, ActiveSeed = outcome.Seed,
                ActiveRulesVersion = outcome.RulesVersion, ActiveRunVersion = 3, EntryItems = result.EntryItems
            };
            if (outcome.LevelId < 1 || !ValidOutcome(outcome, identity, out _)) return false;
            var retained = new Dictionary<string, ProjectItem>();
            var lost = new Dictionary<string, ProjectItem>();
            foreach (ProjectItem item in result.RetainedItems)
                if (item == null || string.IsNullOrEmpty(item.Id) || retained.ContainsKey(item.Id)) return false;
                else retained.Add(item.Id, item);
            foreach (ProjectItem item in result.LostItems)
                if (item == null || string.IsNullOrEmpty(item.Id) || retained.ContainsKey(item.Id) || lost.ContainsKey(item.Id)) return false;
                else lost.Add(item.Id, item);
            if (retained.Count + lost.Count != outcome.Items.Count + result.EntryItems.Count) return false;
            if (!TryClassifyItems(outcome, result.EntryItems, out var expectedRetained, out var expectedLost, out _)) return false;
            if (retained.Count != expectedRetained.Count || lost.Count != expectedLost.Count) return false;
            foreach (ProjectItem item in expectedRetained)
            {
                if (!retained.TryGetValue(item.Id, out ProjectItem actual) || JsonUtility.ToJson(item) != JsonUtility.ToJson(actual)) return false;
            }
            foreach (ProjectItem item in expectedLost)
                if (!lost.TryGetValue(item.Id, out ProjectItem actual) || JsonUtility.ToJson(item) != JsonUtility.ToJson(actual)) return false;
            return true;
        }

        /// <summary>
        /// 校验终局事实只含当前单局和有效物品身份。
        /// </summary>
        /// <param name="outcome">终局事实。</param>
        /// <param name="data">当前入场记录。</param>
        /// <param name="error">失败原因。</param>
        /// <returns>终局是否合法。</returns>
        private static bool ValidOutcome(ProjectRunOutcome outcome, ProjectSaveData data, out string error)
        {
            error = "终局身份或规则版本不匹配。";
            if (outcome == null || data.ActiveRunVersion != 3 || outcome.RunId != data.ActiveRunId || data.ActiveRulesVersion != RulesVersion ||
                outcome.LevelId != data.ActiveLevelId || outcome.Seed != data.ActiveSeed || outcome.RulesVersion != RulesVersion) return false;
            error = "终局时间或奖励参数无效。";
            if (outcome.Tick < 0 || float.IsNaN(outcome.ElapsedSeconds) || float.IsInfinity(outcome.ElapsedSeconds) ||
                outcome.ElapsedSeconds < 0 || outcome.KillExperience < 0 || outcome.AwardedKillExperience != outcome.KillExperience ||
                data.ActiveRunAwardedKillExperience < 0 || data.ActiveRunAwardedKillExperience > outcome.KillExperience ||
                outcome.ExtractionExperience < 0 || outcome.GoldSpent < 0 ||
                (outcome.ExitKind != ProjectRunExitKind.Completed && outcome.ExtractionExperience != 0) ||
                outcome.Items == null) return false;
            error = "终局统计无效。";
            if (!ProjectRunStatistics.IsValid(outcome.Statistics)) return false;
            if (!TryClassifyItems(outcome, data.EntryItems, out _, out _, out error)) return false;
            error = null;
            return true;
        }
    }
}
