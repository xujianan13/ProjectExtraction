using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using GameLogic.Gameplay.Expedition;
using GameLogic.Tables;

namespace GameLogic.Project
{
    public sealed partial class ProjectSession
    {
        /// <summary>
        /// 首次开关卡连同第一房需要支付的总计体力。
        /// </summary>
        public long RequiredEntryStamina => (long)ProjectRulesConfig.Profile.EntryCost + ProjectRulesConfig.Profile.RoomEntryCost;

        /// <summary>
        /// 当前账号可否支付下一次房间准入体力。
        /// </summary>
        internal bool CanAffordRoomEntry => !disposed && Candidate().Stamina >= ProjectRulesConfig.Profile.RoomEntryCost;

        /// <summary>
        /// 当前活动关卡已经入账的房间准入序号。
        /// </summary>
        internal int ActiveRoomAdmissionSerial => profile.ActiveRoomAdmissionSerial;

        /// <summary>
        /// 当前活动关卡已经永久入账的累计击杀经验。
        /// </summary>
        internal int ActiveRunAwardedKillExperience => profile.ActiveRunAwardedKillExperience;

        /// <summary>
        /// 当前活动关卡携入装备的独立副本。
        /// </summary>
        internal IReadOnlyList<ProjectItem> EntryItems => profile.EntryItems.ConvertAll(item => item.Copy());

        /// <summary>
        /// 将已保存到关卡日志的累计经验和房间准入水位确认到永久账号。
        /// </summary>
        /// <param name="snapshot">已经完成关卡日志保存的业务快照。</param>
        /// <returns>永久入账确认结果，同一水位重复调用不会重复发奖或扣费。</returns>
        internal UniTask<ProjectOperationResult> CommitRunProgressAsync(ExpeditionRunSnapshot snapshot)
        {
            if (disposed || saving || profileSaveWaiting || pending != null) return Rejected("上一操作尚未保存。");
            ProjectSaveData candidate = Candidate();
            if (!TryApplyRunProgress(candidate, snapshot, out string error)) return Rejected(error);
            if (candidate.ActiveRunAwardedKillExperience == profile.ActiveRunAwardedKillExperience &&
                candidate.ActiveRoomAdmissionSerial == profile.ActiveRoomAdmissionSerial)
                return UniTask.FromResult(new ProjectOperationResult(true, "关卡成长与房间准入已确认。"));
            return CommitAsync(candidate, "关卡成长与房间准入已保存。");
        }

        /// <summary>
        /// 在当前关卡事务中持久化永久技能升级，战斗属性由主协调器在确认后刷新。
        /// </summary>
        /// <param name="run">当前已经绑定的关卡运行对象。</param>
        /// <param name="skillId">永久技能身份。</param>
        /// <returns>技能点扣除与等级提升的保存确认结果。</returns>
        internal UniTask<ProjectOperationResult> UpgradeRunSkillAsync(ExpeditionRun run, string skillId)
        {
            if (disposed || saving || profileSaveWaiting || pending != null || run == null || !ReferenceEquals(CurrentRun, run) ||
                profile.ActiveRunId != run.RunId || profile.ActiveRunVersion != 3 || profile.Stage != ProjectRunStage.Running)
                return Rejected("当前不能在关卡中升级永久技能。");
            ProjectSaveData candidate = Candidate();
            if (!TryApplySkillUpgrade(candidate, skillId, out string error)) return Rejected(error);
            return CommitAsync(candidate, "技能升级成功。");
        }

        /// <summary>
        /// 在独立账号候选上原子应用关卡账本，任何拒绝均保留候选原值。
        /// </summary>
        /// <param name="candidate">已验证的独立账号候选。</param>
        /// <param name="snapshot">关卡日志中已经确认的累计业务事实。</param>
        /// <param name="error">拒绝原因，成功时为空。</param>
        /// <returns>身份、水位、经验和体力均满足入账契约时为真。</returns>
        private static bool TryApplyRunProgress(ProjectSaveData candidate, ExpeditionRunSnapshot snapshot, out string error)
        {
            error = "关卡成长账本身份或版本无效。";
            if (snapshot == null || snapshot.Version != 3 || string.IsNullOrWhiteSpace(snapshot.RunId) ||
                candidate.ActiveRunVersion != 3 || candidate.ActiveRunId != snapshot.RunId ||
                candidate.Stage != ProjectRunStage.Admitted && candidate.Stage != ProjectRunStage.Running) return false;
            error = "关卡成长账本的经验或准入序号发生回退或跳跃。";
            int currentSerial = candidate.ActiveRoomAdmissionSerial;
            if (snapshot.KillExperience < candidate.ActiveRunAwardedKillExperience || snapshot.RoomAdmissionSerial < 1 ||
                snapshot.RoomAdmissionSerial < currentSerial || (long)snapshot.RoomAdmissionSerial > (long)currentSerial + 1) return false;
            int delta = snapshot.KillExperience - candidate.ActiveRunAwardedKillExperience;
            if (candidate.Experience > int.MaxValue - delta)
            { error = "永久经验数值超过可保存范围。"; return false; }
            int roomCost = snapshot.RoomAdmissionSerial == currentSerial ? 0 : ProjectRulesConfig.Profile.RoomEntryCost;
            if (candidate.Stamina < roomCost)
            { error = "体力不足，需要 " + roomCost + " 点体力进入房间。"; return false; }
            candidate.Stamina -= roomCost;
            ProjectProfileRules.AddExperience(candidate, delta);
            candidate.ActiveRunAwardedKillExperience = snapshot.KillExperience;
            candidate.ActiveRoomAdmissionSerial = snapshot.RoomAdmissionSerial;
            error = null;
            return true;
        }

        /// <summary>
        /// 在独立账号候选上应用局内外共用的永久技能升级规则。
        /// </summary>
        /// <param name="candidate">已验证的独立账号候选。</param>
        /// <param name="skillId">永久技能身份。</param>
        /// <param name="error">拒绝原因，成功时为空。</param>
        /// <returns>技能有效且点数足够时为真。</returns>
        private static bool TryApplySkillUpgrade(ProjectSaveData candidate, string skillId, out string error)
        {
            int rank = skillId == "power" ? candidate.PowerRank : skillId == "vitality" ? candidate.VitalityRank :
                skillId == "agility" ? candidate.AgilityRank : -1;
            CfgSkillsRow skill = ProjectRulesConfig.Skill(skillId);
            error = "技能不存在或已经满级。";
            if (rank < 0 || skill == null || rank >= skill.MaxRank) return false;
            int cost = skill.CostBase + rank * skill.CostPerRank;
            error = "技能点不足。";
            if (candidate.SkillPoints < cost) return false;
            candidate.SkillPoints -= cost;
            if (skillId == "power") candidate.PowerRank++;
            else if (skillId == "vitality") candidate.VitalityRank++;
            else candidate.AgilityRank++;
            error = null;
            return true;
        }
    }
}
