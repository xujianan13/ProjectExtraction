using GameLogic.Gameplay.Combat;
using GameLogic.Tables;

namespace GameLogic.UI.Common
{
    /// <summary>
    /// 将技能配置和真实运行快照转换为玩家可读说明。
    /// </summary>
    public static class CombatSkillText
    {
        /// <summary>
        /// 返回技能当前动作、持续效果或剩余冷却的简短说明。
        /// </summary>
        /// <param name="skill">战斗世界输出的技能快照。</param>
        /// <returns>可用于战斗状态栏的文字。</returns>
        public static string State(CombatSkillSnapshot skill)
        {
            if (skill.IsCasting) return "施放中";
            if (skill.IsPersistentActive) return "持续";
            if (skill.CooldownTicks > 0) return (skill.CooldownTicks * CombatWorld.TickSeconds).ToString("0.0") + "s";
            return "待机";
        }

        /// <summary>
        /// 返回技能名称与当前状态。
        /// </summary>
        /// <param name="skill">战斗世界输出的技能快照。</param>
        /// <returns>不包含内部编号的简短技能信息。</returns>
        public static string Summary(CombatSkillSnapshot skill) => GameTables.CfgCombatSkills.GetById(skill.SkillId).Name + " " + State(skill);

        /// <summary>
        /// 返回用途分类与效果分类，避免将主动技能误解为手动操作。
        /// </summary>
        /// <param name="skill">包含两种分类的技能快照。</param>
        /// <returns>供技能详情使用的分类说明。</returns>
        public static string Categories(CombatSkillSnapshot skill)
        {
            string use = skill.SkillType == CombatSkillType.BasicAttack ? "普通攻击" :
                skill.SkillType == CombatSkillType.Normal ? "普通技能" : "特殊技能";
            string effect = skill.EffectClass == CombatSkillEffectClass.Active ? "主动" :
                skill.EffectClass == CombatSkillEffectClass.Auxiliary ? "辅助攻击" : "Buff";
            return use + " · " + effect;
        }
    }
}
