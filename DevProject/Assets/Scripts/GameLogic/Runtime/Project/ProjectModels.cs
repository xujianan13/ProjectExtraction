using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameLogic.Project
{
    /// <summary>
    /// 仓库互斥展示分类。
    /// </summary>
    public enum ProjectItemType { Weapon = 0, Equipment = 1, Item = 2, Armor = 3 }

    /// <summary>
    /// 独立物品实例；随机数值只在生成时写入。
    /// </summary>
    [Serializable]
    public sealed class ProjectItem
    {
        public string Id;
        public string Name;
        public ProjectItemType Type;
        public int RequiredLevel;
        public int SellGold;
        public float Damage;
        public float Range;
        public int BonusHealth;
        public bool Equipped;
        public ProjectItemQuality Quality;
        public int EquipmentVersion;
        public List<ProjectAttributeValue> BaseAttributes = new();
        public List<ProjectEquipmentAffix> Affixes = new();

        /// <summary>
        /// 创建不共享可写字段的实例副本。
        /// </summary>
        /// <returns>独立物品副本。</returns>
        public ProjectItem Copy()
        {
            var copy = (ProjectItem)MemberwiseClone();
            copy.BaseAttributes = BaseAttributes?.ConvertAll(value => value?.Copy());
            copy.Affixes = Affixes?.ConvertAll(value => value?.Copy());
            return copy;
        }
    }

    /// <summary>
    /// 技能界面使用的成长快照。
    /// </summary>
    public sealed class ProjectSkill
    {
        public string Id;
        public string Name;
        public string Description;
        public string CurrentEffect;
        public string NextEffect;
        public int Rank;
        public int MaxRank;
        public int NextCost;
    }

    /// <summary>
    /// 局外关卡选择条目。
    /// </summary>
    public sealed class ProjectLevelOption
    {
        public int Id;
        public int RequiredLevel;
        public string Name;
        public string Description;
    }

    /// <summary>
    /// 局外异步交易的明确结果。
    /// </summary>
    public readonly struct ProjectOperationResult
    {
        public bool Success { get; }
        public string Message { get; }

        /// <summary>
        /// 创建操作结果。
        /// </summary>
        /// <param name="success">操作是否已经成功提交。</param>
        /// <param name="message">可展示的结果信息。</param>
        public ProjectOperationResult(bool success, string message)
        {
            Success = success;
            Message = message;
        }
    }

    /// <summary>
    /// 首版可调测试规则，不代表正式平衡配置。
    /// </summary>
    public static class PrototypeRules
    {
        public const int StaminaCap = 100;
        public const int EntryCost = 10;
        public const int RecoverySeconds = 360;
        public const int SupplyAmount = 40;
        public const int GameUtcOffsetHours = 8;
        public const int SafeSlots = 2;
        public const int SkillMaxRank = 5;
        public const int SkillPointsPerLevel = 1;
        public const int ExtractionExperience = 40;
        public const bool KeepKillExperienceOnDeath = true;
        public const int NpcItemPrice = 20;

        /// <summary>
        /// 计算当前等级升到下级所需经验。
        /// </summary>
        /// <param name="level">当前角色等级。</param>
        /// <returns>本级经验门槛。</returns>
        public static int ExperienceRequired(int level) => 100 + (Math.Max(1, level) - 1) * 50;

        /// <summary>
        /// 取得首批关卡配置。
        /// </summary>
        /// <returns>独立关卡条目。</returns>
        public static ProjectLevelOption[] CreateLevels() => new[]
        {
            new ProjectLevelOption { Id = 1, RequiredLevel = 1, Name = "荒野遗迹", Description = "探索相连区域，寻找战利品与撤离点" },
            new ProjectLevelOption { Id = 2, RequiredLevel = 3, Name = "深层遗迹", Description = "更强的敌人与更高等级的装备" },
            new ProjectLevelOption { Id = 3, RequiredLevel = 6, Name = "禁地", Description = "高风险区域" }
        };

        /// <summary>
        /// 按给定随机源生成稳定物品实例。
        /// </summary>
        /// <param name="random">本局独立随机源。</param>
        /// <param name="level">物品等级。</param>
        /// <returns>已经固定随机属性的实例。</returns>
        public static ProjectItem CreateLoot(System.Random random, int level) => ProjectEquipmentRules.CreateLoot(random, level);
    }

    /// <summary>
    /// 单一项目存档；仓库与钱包、成长在同一次快照中提交。
    /// </summary>
    [Serializable]
    public sealed class ProjectSaveData
    {
        public int Level = 1;
        public int Experience;
        public int Stamina = PrototypeRules.StaminaCap;
        public int Gold = 50;
        public int Diamonds;
        public int SkillPoints;
        public int PowerRank;
        public int VitalityRank;
        public int AgilityRank;
        public long ClockUtc;
        public long RecoveryUtc;
        public string LastSupplyKey = "";
        public string ActiveRunId = "";
        public int ActiveRunEntryCost;
        public string LastSettledRunId = "";
        public string LastRunSummary = "";
        public ProjectRunStage Stage;
        public int ActiveLevelId;
        public int ActiveSeed;
        public int ActiveRulesVersion;
        public ProjectRunOutcome Outcome;
        public ProjectRunResult LastResult;
        public List<ProjectItem> Warehouse = new();
        public List<ProjectItem> EntryItems = new();

        /// <summary>
        /// 创建新档和唯一的新手武器。
        /// </summary>
        /// <returns>新玩家数据。</returns>
        public static ProjectSaveData CreateDefault()
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var result = new ProjectSaveData { ClockUtc = now, RecoveryUtc = now };
            result.Warehouse.Add(new ProjectItem
            {
                Id = Guid.NewGuid().ToString("N"), Name = "练习短刃", Type = ProjectItemType.Weapon,
                RequiredLevel = 1, Damage = 16, Range = 1.9f, SellGold = 8, Equipped = true,
                Quality = ProjectItemQuality.Common, EquipmentVersion = ProjectEquipmentRules.EquipmentVersion,
                BaseAttributes = new List<ProjectAttributeValue>
                {
                    new ProjectAttributeValue { Attribute = ProjectAttributeType.Attack, Value = 16 }
                }
            });
            return result;
        }

        /// <summary>
        /// 创建事务候选的深拷贝。
        /// </summary>
        /// <returns>不共享物品列表的存档。</returns>
        public ProjectSaveData Copy()
        {
            var copy = (ProjectSaveData)MemberwiseClone();
            copy.Warehouse = Warehouse.ConvertAll(item => item.Copy());
            copy.EntryItems = EntryItems.ConvertAll(item => item.Copy());
            copy.Outcome = Outcome?.Copy();
            copy.LastResult = LastResult?.Copy();
            return copy;
        }

        /// <summary>
        /// 校验存档完整性，失败时保留原存档而不覆盖为默认值。
        /// </summary>
        /// <param name="data">待校验数据。</param>
        /// <returns>错误信息，通过时为空。</returns>
        public static string Validate(ProjectSaveData data)
        {
            if (data == null || data.Level < 1 || data.Level > 1000 || data.Experience < 0 || data.Stamina < 0 || data.Gold < 0 ||
                data.Diamonds < 0 || data.SkillPoints < 0 || data.Warehouse == null || data.EntryItems == null)
                return "玩家数据字段无效。";
            if (data.PowerRank < 0 || data.PowerRank > PrototypeRules.SkillMaxRank || data.VitalityRank < 0 ||
                data.VitalityRank > PrototypeRules.SkillMaxRank || data.AgilityRank < 0 || data.AgilityRank > PrototypeRules.SkillMaxRank)
                return "技能等级无效。";
            var ids = new HashSet<string>();
            var equippedSlots = new HashSet<ProjectItemType>();
            foreach (ProjectItem item in data.Warehouse)
                if (!ValidItem(item, ids, equippedSlots)) return "仓库物品无效、身份重复或装备槽冲突。";
            foreach (ProjectItem item in data.EntryItems)
                if (!ValidItem(item, ids, equippedSlots)) return "入场物品无效、身份重复或装备槽冲突。";
            if (data.ActiveRunEntryCost < 0 || (string.IsNullOrEmpty(data.ActiveRunId) &&
                (data.EntryItems.Count > 0 || data.ActiveRunEntryCost != 0))) return "入场托管状态无效。";
            if (!ProjectRunRecordRules.ValidateRecord(data, out string recordError)) return recordError;
            return null;
        }

        /// <summary>
        /// 检查物品字段、实例身份与跨容器装备槽。
        /// </summary>
        /// <param name="item">物品实例。</param>
        /// <param name="ids">已经出现的实例身份。</param>
        /// <param name="equippedSlots">已经占用的装备槽。</param>
        /// <returns>物品是否合法。</returns>
        private static bool ValidItem(ProjectItem item, HashSet<string> ids, HashSet<ProjectItemType> equippedSlots)
        {
            if (ProjectEquipmentRules.ValidateItem(item) != null || !ids.Add(item.Id)) return false;
            if (!item.Equipped || item.Type == ProjectItemType.Item) return true;
            ProjectItemType slot = ProjectEquipmentRules.IsArmor(item.Type) ? ProjectItemType.Armor : ProjectItemType.Weapon;
            return equippedSlots.Add(slot);
        }
    }

    /// <summary>
    /// 可独立测试的成长与时间规则。
    /// </summary>
    public static class ProjectProfileRules
    {
        /// <summary>
        /// 判断保存回执是否覆盖了本次成功标记的修订号。
        /// </summary>
        /// <param name="marked">本次候选是否已经成功标记为脏。</param>
        /// <param name="revision">本次候选的修订号。</param>
        /// <param name="target">回执等待的修订号。</param>
        /// <param name="saved">回执已保存的修订号。</param>
        /// <param name="success">框架保存结果是否成功。</param>
        /// <returns>候选是否已经获得持久化确认。</returns>
        public static bool CanAcceptSaveReceipt(bool marked, long revision, long target, long saved, bool success) =>
            marked && revision > 0 && success && target >= revision && saved >= revision;

        /// <summary>
        /// 将异常中断的原入场资源退回候选存档，并丢弃未提交的单局收益。
        /// </summary>
        /// <param name="candidate">独立事务候选。</param>
        /// <param name="reason">可显示的异常原因。</param>
        /// <returns>是否完成单局回退。</returns>
        public static bool TryRecoverInterruptedRun(ProjectSaveData candidate, string reason)
        {
            if (candidate == null || string.IsNullOrEmpty(candidate.ActiveRunId) || candidate.Outcome != null ||
                candidate.Stage == ProjectRunStage.OutcomeRecorded) return false;
            int cost = candidate.ActiveRunEntryCost > 0 ? candidate.ActiveRunEntryCost : PrototypeRules.EntryCost;
            if (candidate.Stamina > int.MaxValue - cost) return false;
            foreach (ProjectItem item in candidate.EntryItems) candidate.Warehouse.Add(item.Copy());
            candidate.EntryItems.Clear();
            candidate.Stamina += cost;
            candidate.LastSettledRunId = candidate.ActiveRunId;
            candidate.ActiveRunId = "";
            candidate.ActiveRunEntryCost = 0;
            candidate.Stage = ProjectRunStage.None;
            candidate.ActiveLevelId = 0;
            candidate.ActiveSeed = 0;
            candidate.ActiveRulesVersion = 0;
            candidate.LastResult = null;
            candidate.LastRunSummary = string.IsNullOrWhiteSpace(reason) ?
                "关卡异常中断：已退回入场装备与体力，未结算战利品。" :
                "关卡异常中断（" + reason + "）：已退回入场装备与体力，未结算战利品。";
            return true;
        }

        /// <summary>
        /// 按游戏时区处理五点重置与六分钟恢复；满体力不积攒恢复时间。
        /// </summary>
        /// <param name="data">待更新候选存档。</param>
        /// <param name="utc">当前 UTC 秒。</param>
        public static void AdvanceClock(ProjectSaveData data, long utc)
        {
            if (utc <= data.ClockUtc) return;
            var offset = TimeSpan.FromHours(PrototypeRules.GameUtcOffsetHours);
            DateTimeOffset local = DateTimeOffset.FromUnixTimeSeconds(utc).ToOffset(offset);
            var reset = new DateTimeOffset(local.Year, local.Month, local.Day, 5, 0, 0, offset);
            if (local < reset) reset = reset.AddDays(-1);
            long resetUtc = reset.ToUnixTimeSeconds();
            if (data.ClockUtc < resetUtc)
            {
                data.Stamina = PrototypeRules.StaminaCap;
                data.RecoveryUtc = resetUtc;
            }
            if (data.Stamina >= PrototypeRules.StaminaCap) data.RecoveryUtc = utc;
            else
            {
                long elapsed = Math.Max(0, utc - data.RecoveryUtc);
                int recovered = (int)Math.Min(PrototypeRules.StaminaCap, elapsed / PrototypeRules.RecoverySeconds);
                data.Stamina = Math.Min(PrototypeRules.StaminaCap, data.Stamina + recovered);
                data.RecoveryUtc += recovered * PrototypeRules.RecoverySeconds;
                if (data.Stamina == PrototypeRules.StaminaCap) data.RecoveryUtc = utc;
            }
            data.ClockUtc = utc;
        }

        /// <summary>
        /// 取得当前可领取补给的唯一日历时段；窗口在下一补给时段结束。
        /// </summary>
        /// <param name="utc">当前 UTC 秒。</param>
        /// <returns>十二点、十八点或零点补给的身份。</returns>
        public static string SupplyKey(long utc)
        {
            DateTimeOffset local = DateTimeOffset.FromUnixTimeSeconds(utc).ToOffset(TimeSpan.FromHours(PrototypeRules.GameUtcOffsetHours));
            int hour = local.Hour >= 18 ? 18 : local.Hour >= 12 ? 12 : 0;
            return local.ToString("yyyyMMdd") + "-" + hour;
        }

        /// <summary>
        /// 按各级门槛升级并逐级发放技能点。
        /// </summary>
        /// <param name="data">本次事务候选。</param>
        /// <param name="experience">新增经验。</param>
        public static void AddExperience(ProjectSaveData data, int experience)
        {
            data.Experience += Math.Max(0, experience);
            while (data.Level < 1000 && data.Experience >= PrototypeRules.ExperienceRequired(data.Level))
            {
                data.Experience -= PrototypeRules.ExperienceRequired(data.Level);
                data.Level++;
                data.SkillPoints += PrototypeRules.SkillPointsPerLevel;
            }
        }
    }
}
