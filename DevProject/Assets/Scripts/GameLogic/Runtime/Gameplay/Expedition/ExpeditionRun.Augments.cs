using System.Collections.Generic;
using GameLogic.Gameplay.Combat;
using GameLogic.Gameplay.Level;
using GameLogic.Gameplay.Roguelike;
using GameLogic.Project;

namespace GameLogic.Gameplay.Expedition
{
    public sealed partial class ExpeditionRun
    {
        private readonly float augmentBaseAttackRange;
        private readonly System.Random roomRewardRandom;
        private readonly HashSet<int> rewardedRooms = new();
        private readonly Queue<string> roomRewardSources = new();
        private List<CombatAugmentDefinition> equipmentAugments = new();

        /// <summary>
        /// 当前单局已经兑现的升级快照，物品奖励不列为角色强化。
        /// </summary>
        public IReadOnlyList<RogueUpgradeSnapshot> RogueUpgrades => rogueChoices.Upgrades;

        /// <summary>
        /// 从初始装备安装高级词条，旧装备没有词条时保持空组。
        /// </summary>
        /// <returns>配置和战斗组均成功初始化时为真。</returns>
        private bool InitializeEquipmentAugments()
        {
            if (!TryBuildEquipmentAugments(inventory, out List<CombatAugmentDefinition> definitions) ||
                !World.TryReplaceAugments(Player, "equipment", definitions)) return false;
            equipmentAugments = definitions;
            return true;
        }

        /// <summary>
        /// 转换当前穿戴装备的冻结词条，物品身份参与内部冷却身份。
        /// </summary>
        /// <param name="items">候选装备快照。</param>
        /// <param name="definitions">对应的战斗强化快照。</param>
        /// <returns>全部已装备物品和词条可用时为真。</returns>
        private bool TryBuildEquipmentAugments(IReadOnlyList<ProjectItem> items, out List<CombatAugmentDefinition> definitions)
        {
            definitions = new List<CombatAugmentDefinition>();
            foreach (ProjectItem item in items)
            {
                if (!item.Equipped) continue;
                if (ProjectEquipmentRules.ValidateItem(item) != null) return false;
                if (item.AdvancedAffixes == null) continue;
                foreach (ProjectAdvancedAffix affix in item.AdvancedAffixes)
                {
                    if (!ProjectAugmentConfig.TryCreate(affix.AugmentId, item.Id + ":" + affix.DefinitionId, affix.Value,
                        augmentBaseAttackRange, out CombatAugmentDefinition definition)) return false;
                    definitions.Add(definition);
                }
            }
            return true;
        }

        /// <summary>
        /// 在同一同步事务内提交基础属性和装备强化，不修改角色生命恢复或冷却。
        /// </summary>
        /// <param name="proposed">已经生成的装备槽候选。</param>
        /// <param name="spec">对应基础属性。</param>
        /// <returns>两部分都更新成功时为真。</returns>
        private bool TryApplyEquipment(IReadOnlyList<ProjectItem> proposed, CombatActorSpec spec)
        {
            if (!World.TryPreviewBaseAttributes(Player, spec, out _) ||
                !TryBuildEquipmentAugments(proposed, out List<CombatAugmentDefinition> definitions) ||
                !World.TryReplaceAugments(Player, "equipment", definitions)) return false;
            if (!World.TryReplaceBaseAttributes(Player, spec))
            {
                World.TryReplaceAugments(Player, "equipment", equipmentAugments);
                return false;
            }
            equipmentAugments = definitions;
            return true;
        }

        /// <summary>
        /// 使用新等级替换对应强化来源，其他强化和装备来源保持独立。
        /// </summary>
        /// <param name="reward">尚未提交等级、准备兑现的选项。</param>
        /// <returns>整个强化组已原子更新时为真。</returns>
        private bool ApplyCombatAugmentReward(RogueRewardDefinition reward)
        {
            var definitions = new List<CombatAugmentDefinition>();
            foreach (RogueUpgradeSnapshot upgrade in RogueUpgrades)
            {
                if (upgrade.Kind != RogueRewardKind.CombatAugment || upgrade.Id == reward.Id) continue;
                if (!ProjectAugmentConfig.TryCreate(upgrade.AugmentId, upgrade.Id, upgrade.Amount * upgrade.Rank,
                    augmentBaseAttackRange, out CombatAugmentDefinition prior)) return false;
                definitions.Add(prior);
            }
            if (!ProjectAugmentConfig.TryCreate(reward.AugmentId, reward.Id, reward.Amount * reward.Rank,
                augmentBaseAttackRange, out CombatAugmentDefinition next)) return false;
            definitions.Add(next);
            if (!World.TryReplaceAugments(Player, "rogue", definitions)) return false;
            Message = "本局强化：" + reward.Title + " Lv." + reward.Rank;
            return true;
        }

        /// <summary>
        /// 打开本局强化总览，使用独立面板暂停并清除残留输入。
        /// </summary>
        /// <returns>总览已打开或成功打开时为真。</returns>
        public bool TryOpenRogueOverview()
        {
            if (ActivePanel == ExpeditionPanelKind.RogueOverview && CanManagePanel) return true;
            if (!CanInteract || !CanManagePanel) return false;
            SetActivePanel(ExpeditionPanelKind.RogueOverview);
            return true;
        }

        /// <summary>
        /// 首次清房时冻结奖励抽取结果，回访和重复事件不能再次抽奖。
        /// </summary>
        /// <param name="room">已确认清空敌人的房间。</param>
        private void QueueRoomReward(LevelRoomState room)
        {
            if (!rewardedRooms.Add(room.RoomId)) return;
            var policy = ProjectRulesConfig.RoguePolicy;
            bool reward = room.Type == LevelRoomType.Boss ? policy.BossClearReward :
                room.Type == LevelRoomType.Mob && roomRewardRandom.NextDouble() < policy.MobClearChance;
            if (reward) roomRewardSources.Enqueue("clear:" + room.RoomId);
        }

        /// <summary>
        /// 在终局仲裁之后展示清房奖励，成功创建才移出队列。
        /// </summary>
        private void BeginPendingRoomReward()
        {
            if (roomRewardSources.Count > 0 && TryBeginRogueChoice(roomRewardSources.Peek())) roomRewardSources.Dequeue();
        }
    }
}
