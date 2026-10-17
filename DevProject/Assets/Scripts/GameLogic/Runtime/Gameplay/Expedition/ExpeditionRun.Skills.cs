using GameLogic.Gameplay.Combat;
using GameLogic.Project;

namespace GameLogic.Gameplay.Expedition
{
    public sealed partial class ExpeditionRun
    {
        /// <summary>
        /// 按已装备武器和玩家配置安装首轮可玩的技能列表。
        /// </summary>
        /// <returns>全部技能可由当前战斗世界接纳时为真。</returns>
        private bool InitializeCombatSkills() => ProjectWeaponCombat.TryGetBasicSkill(inventory, out int basicSkillId) &&
            World.TrySetSkillLoadout(Player, basicSkillId, ProjectWeaponCombat.CreateSkillLoadout(basicSkillId));

        /// <summary>
        /// 校验恢复的技能入口与当前版本的装备和玩家配置一致，不重新执行技能初始化。
        /// </summary>
        /// <param name="spec">快照保存的角色技能入口。</param>
        /// <param name="basicSkillId">由当前装备解析出的普攻编号。</param>
        /// <returns>普攻和有序技能列表全部一致时为真。</returns>
        private static bool MatchesSkillLoadout(CombatActorSpec spec, int basicSkillId)
        {
            int[] expected = ProjectWeaponCombat.CreateSkillLoadout(basicSkillId);
            if (spec.BasicSkillId != basicSkillId || spec.SkillIds == null || spec.SkillIds.Length != expected.Length) return false;
            for (int index = 0; index < expected.Length; index++)
                if (spec.SkillIds[index] != expected[index]) return false;
            return true;
        }
    }
}
