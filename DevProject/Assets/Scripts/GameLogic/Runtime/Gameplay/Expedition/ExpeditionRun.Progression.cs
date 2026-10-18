using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using GameLogic.Project;

namespace GameLogic.Gameplay.Expedition
{
    public sealed partial class ExpeditionRun
    {
        public int PlayerExperience => session.Experience;
        public int ExperienceToNextLevel => session.ExperienceToNextLevel;
        public int SkillPoints => session.SkillPoints;
        public IReadOnlyList<ProjectSkill> PermanentSkills => session.Skills;
        public bool CanUpgradePermanentSkills => ActivePanel == ExpeditionPanelKind.Progression && CanManagePanel;

        /// <summary>
        /// 打开永久成长面板，技能升级使用账号技能点并立即影响本局。
        /// </summary>
        /// <returns>成长面板已经打开或成功进入时为真。</returns>
        public bool TryOpenProgression()
        {
            if (CanUpgradePermanentSkills) return true;
            if (!CanInteract || !CanManagePanel) return false;
            SetActivePanel(ExpeditionPanelKind.Progression);
            return true;
        }

        /// <summary>
        /// 保存技能点消费后同步玩家属性，再确认本局检查点。
        /// </summary>
        /// <param name="skillId">配置中的永久技能身份。</param>
        /// <returns>技能升级及关卡检查点均确认后的结果。</returns>
        public async UniTask<ProjectOperationResult> UpgradePermanentSkillAsync(string skillId)
        {
            if (!CanUpgradePermanentSkills) return new ProjectOperationResult(false, "当前不能升级技能。");
            committing = true;
            ClearPlayerInput();
            ProjectOperationResult result = await session.UpgradeRunSkillAsync(this, skillId);
            if (!result.Success)
            {
                committing = false;
                Message = result.Message;
                if (session.HasPendingSave)
                    await ProjectServices.Coordinator.SaveBarrierAsync(() => session.RetryPendingSaveAsync(), ProjectRunPhase.Playing);
                else { Changed?.Invoke(); return result; }
                committing = true;
            }
            if (disposed || stopped) return new ProjectOperationResult(false, "关卡已经结束。");
            if (!RefreshPermanentAttributes()) { HandleFault("技能已保存，但战斗属性同步失败。"); return new ProjectOperationResult(false, Message); }
            bool saved = await ProjectServices.Coordinator.SaveRunBoundaryAsync(this, ProjectRunPhase.Playing);
            if (disposed) return new ProjectOperationResult(false, "关卡已经结束。");
            committing = false;
            Message = saved ? "技能升级成功，已在本局生效。" : "技能已保存，关卡检查点等待恢复。";
            Changed?.Invoke();
            return new ProjectOperationResult(saved, Message);
        }

        /// <summary>
        /// 根据固定携入装备和最新永久成长同步玩家基础属性，不回复生命或重置技能冷却。
        /// </summary>
        /// <returns>玩家基础属性已成功同步时为真。</returns>
        internal bool RefreshPermanentAttributes()
        {
            if (!ProjectWeaponCombat.TryGetBasicSkill(entryEquipment, out int basicSkillId)) return false;
            var attributes = EquipmentSpec(entryEquipment);
            attributes.BasicSkillId = basicSkillId;
            attributes.SkillIds = ProjectWeaponCombat.CreateSkillLoadout(basicSkillId);
            return World.TryRefreshPlayerBaseAttributes(Player, attributes);
        }
    }
}
