using System.Collections.Generic;
using GameLogic.Gameplay.Combat;
using GameLogic.Project;

namespace GameLogic.Gameplay.Expedition
{
    public sealed partial class ExpeditionRun
    {
        private float augmentBaseAttackRange;
        private List<CombatAugmentDefinition> equipmentAugments = new();

        /// <summary>
        /// 从初始装备安装高级词条，旧装备没有词条时保持空组。
        /// </summary>
        /// <returns>配置和战斗组均成功初始化时为真。</returns>
        private bool InitializeEquipmentAugments()
        {
            if (!TryBuildEquipmentAugments(entryEquipment, out List<CombatAugmentDefinition> definitions) ||
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

    }
}
