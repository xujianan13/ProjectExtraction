using System;
using System.Collections.Generic;
using GameLogic.Tables;

namespace GameLogic.Project
{
    /// <summary>
    /// 决定近战普攻形态的武器类别。
    /// </summary>
    public enum ProjectWeaponClass { Sword, AxeHammer, Polearm }

    /// <summary>
    /// 将当前装备和玩家技能配置转换为战斗技能入口。
    /// </summary>
    public static class ProjectWeaponCombat
    {
        /// <summary>
        /// 解析唯一已装备武器的普攻技能，空武器槽使用配置中的徒手入口。
        /// </summary>
        /// <param name="items">当前持有的物品与装备状态。</param>
        /// <param name="skillId">成功时返回对应的普攻技能编号。</param>
        /// <returns>物品、武器映射和技能配置均有效时为真。</returns>
        public static bool TryGetBasicSkill(IReadOnlyList<ProjectItem> items, out int skillId)
        {
            skillId = ProjectContentConfig.Player.SkillID;
            if (items == null) return false;
            bool found = false;
            foreach (ProjectItem item in items)
            {
                if (item == null) return false;
                if (!item.Equipped || item.Type != ProjectItemType.Weapon) continue;
                if (found) return false;
                CfgItemDefinitionsRow definition = ProjectRulesConfig.Item(item.DefinitionId);
                if (definition == null || definition.Type != (int)ProjectItemType.Weapon || definition.BasicSkillID <= 0 ||
                    !Enum.IsDefined(typeof(ProjectWeaponClass), definition.WeaponClass)) return false;
                skillId = definition.BasicSkillID;
                found = true;
            }
            return GameTables.CfgCombatSkills.GetById(skillId) != null;
        }

        /// <summary>
        /// 按普攻在前、普通技能保持配置顺序的规则创建独立技能列表。
        /// </summary>
        /// <param name="basicSkillId">已经解析并校验的普攻技能编号。</param>
        /// <returns>可交给战斗世界复制接纳的技能数组。</returns>
        public static int[] CreateSkillLoadout(int basicSkillId)
        {
            int[] configured = ProjectContentConfig.Player.SkillIDs;
            var result = new int[configured.Length + 1];
            result[0] = basicSkillId;
            Array.Copy(configured, 0, result, 1, configured.Length);
            return result;
        }

        /// <summary>
        /// 返回已经校验的武器类别的中文名称。
        /// </summary>
        /// <param name="weaponClass">源表中的武器类别。</param>
        /// <returns>玩家可读的类别名称，无效输入返回配置错误说明。</returns>
        public static string ClassName(int weaponClass) => (ProjectWeaponClass)weaponClass switch
        {
            ProjectWeaponClass.Sword => "刀剑",
            ProjectWeaponClass.AxeHammer => "斧锤",
            ProjectWeaponClass.Polearm => "长柄",
            _ => "武器类别配置无效"
        };
    }
}
