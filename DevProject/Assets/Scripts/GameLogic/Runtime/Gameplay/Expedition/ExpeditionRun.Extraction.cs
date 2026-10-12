using System.Collections.Generic;
using GameLogic.Gameplay.Combat;
using GameLogic.Project;

namespace GameLogic.Gameplay.Expedition
{
    public sealed partial class ExpeditionRun
    {
        /// <summary>
        /// 在区域已完成的出口打开带回或深入选择。
        /// </summary>
        /// <returns>确认面板已打开时为真。</returns>
        private bool TryOpenExtractionConfirmation()
        {
            if (!CanInteract || !CanManagePanel || !IsPlayerAtExtraction()) return false;
            Message = IsFinalRegion ? "全部Boss奖励已领取，可以通关带回。" : "带回全部持有物，或承担风险继续深入。";
            SetActivePanel(ExpeditionPanelKind.ExtractionConfirmation);
            return true;
        }

        /// <summary>
        /// 明确确认后正常结束本关并携出全部物品。
        /// </summary>
        /// <returns>终局请求已提交时为真。</returns>
        public bool TryConfirmExtraction()
        {
            if (ActivePanel != ExpeditionPanelKind.ExtractionConfirmation || !CanManagePanel || !IsPlayerAtExtraction()) return false;
            EndRun(true);
            return true;
        }

        /// <summary>
        /// 检查玩家仍在当前区域已解锁的出口范围。
        /// </summary>
        /// <returns>区域和距离均符合要求时为真。</returns>
        private bool IsPlayerAtExtraction() => CurrentRegion.ExitEnabled && World.TryGetActor(Player, out CombatActorSnapshot player) &&
            player.RoomId == CurrentRegion.Plan.Request.RegionId && (player.Position - CurrentRegion.Plan.ExitPosition).sqrMagnitude <=
            CurrentRegion.Plan.InteractionRadius * CurrentRegion.Plan.InteractionRadius;

        /// <summary>
        /// 生成带回物品的独立计划，死亡或主动放弃不保留物品。
        /// </summary>
        /// <param name="extracted">是否正常带回。</param>
        /// <returns>实际保留的物品副本。</returns>
        public IReadOnlyList<ProjectItem> GetRetainedItems(bool extracted)
        {
            var retained = new List<ProjectItem>();
            if (extracted) foreach (ProjectItem item in inventory) retained.Add(item.Copy());
            return retained;
        }

        /// <summary>
        /// 从冻结单局创建唯一终局，并纳入下一个运行存档修订。
        /// </summary>
        /// <param name="extracted">是否正常带回。</param>
        /// <param name="abandoned">是否主动放弃。</param>
        /// <returns>完整终局计划副本。</returns>
        public ProjectRunOutcome CreateOutcome(bool extracted, bool abandoned = false)
        {
            if (!stopped || disposed) return null;
            pendingOutcome = new ProjectRunOutcome
            {
                RunId = RunId, LevelId = LevelId, Seed = Seed, RulesVersion = ProjectRunRecordRules.RulesVersion,
                Tick = World.TickIndex, ElapsedSeconds = World.TickIndex * CombatWorld.TickSeconds,
                Extracted = extracted, Abandoned = abandoned, KillExperience = KillExperience,
                AwardedKillExperience = extracted || PrototypeRules.KeepKillExperienceOnDeath ? KillExperience : 0,
                ExtractionExperience = extracted ? PrototypeRules.ExtractionExperience : 0, GoldSpent = GoldSpent,
                Statistics = CaptureStatistics(), Items = inventory.ConvertAll(value => value.Copy())
            };
            return pendingOutcome.Copy();
        }
    }
}
