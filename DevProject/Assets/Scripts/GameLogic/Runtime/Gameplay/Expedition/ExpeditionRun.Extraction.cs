using System;
using System.Collections.Generic;
using GameLogic.Gameplay.Combat;
using GameLogic.Project;

namespace GameLogic.Gameplay.Expedition
{
    public sealed partial class ExpeditionRun
    {
        /// <summary>
        /// 在已完成的中间房出口打开继续前进确认。
        /// </summary>
        /// <returns>确认面板已打开时为真。</returns>
        private bool TryOpenExtractionConfirmation()
        {
            if (IsFinalRoom || !CanInteract || !CanManagePanel || !IsPlayerAtExtraction()) return false;
            Message = "当前房间已完成，可以进入下一房。";
            SetActivePanel(ExpeditionPanelKind.ExtractionConfirmation);
            return true;
        }

        /// <summary>
        /// 检查玩家仍在当前房间已解锁的出口范围。
        /// </summary>
        /// <returns>房间状态和距离均符合要求时为真。</returns>
        private bool IsPlayerAtExtraction() => CurrentRoom.ExitEnabled && World.TryGetActor(Player, out CombatActorSnapshot player) &&
            player.RoomId == CurrentRoom.Plan.Request.RegionId && (player.Position - CurrentRoom.Plan.ExitPosition).sqrMagnitude <=
            CurrentRoom.Plan.InteractionRadius * CurrentRoom.Plan.InteractionRadius;

        /// <summary>
        /// 校验离场资格后冻结唯一终局，失败不会中止当前运行。
        /// </summary>
        /// <param name="exitKind">当前已满足条件的离场类型。</param>
        /// <param name="retainedItemIds">广告撤离选择的关卡背包实例身份。</param>
        /// <returns>合法终局的独立副本，资格无效时为空。</returns>
        public ProjectRunOutcome CreateOutcome(ProjectRunExitKind exitKind, IReadOnlyList<string> retainedItemIds = null)
        {
            if (disposed || stopped || !IsRunning || !Enum.IsDefined(typeof(ProjectRunExitKind), exitKind)) return null;
            if (pendingOutcome != null) return pendingOutcome.ExitKind == exitKind ? pendingOutcome.Copy() : null;
            if (exitKind == ProjectRunExitKind.Completed && (!IsFinalRoom || !CurrentRoom.BossRewardsCompleted || IsAwaitingDeathChoice) ||
                exitKind == ProjectRunExitKind.Death && !CanChooseDeath ||
                exitKind == ProjectRunExitKind.Abandoned && !CanAbandon ||
                exitKind == ProjectRunExitKind.AdExtraction && (!CanConfirmAdExtraction || retainedItemIds == null)) return null;
            var candidate = new ProjectRunOutcome
            {
                RunId = RunId, LevelId = LevelId, Seed = Seed, RulesVersion = ProjectRunRecordRules.RulesVersion,
                Tick = elapsedTicks, ElapsedSeconds = elapsedTicks * CombatWorld.TickSeconds, ExitKind = exitKind,
                KillExperience = KillExperience, AwardedKillExperience = KillExperience,
                ExtractionExperience = exitKind == ProjectRunExitKind.Completed ? PrototypeRules.ExtractionExperience : 0,
                GoldSpent = GoldSpent, Statistics = CaptureStatistics(), Items = inventory.ConvertAll(value => value.Copy()),
                RetainedItemIds = retainedItemIds == null ? new List<string>() : new List<string>(retainedItemIds)
            };
            if (!ProjectRunRecordRules.TryClassifyItems(candidate, entryEquipment, out _, out _, out string error))
            { Message = error; return null; }
            pendingOutcome = candidate;
            return candidate.Copy();
        }
    }
}
