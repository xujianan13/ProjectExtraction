using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;
using GameLogic.Advertising;
using GameLogic.Project;

namespace GameLogic.Gameplay.Expedition
{
    /// <summary>
    /// 保存死亡选择并协调激励广告复活，只有玩家确认后才进入死亡结算。
    /// </summary>
    public sealed partial class ExpeditionRun
    {
        private const string DeathRevivePlacement = "death_revive";
        private int pendingPlayerDeathId;
        private CancellationTokenSource reviveAdCancellation;
        public bool IsAwaitingDeathChoice => pendingPlayerDeathId > 0;
        public bool IsReviveAdPending => reviveAdCancellation != null;
        public bool CanChooseDeath => IsAwaitingDeathChoice && IsRunning && IsReady && !disposed && !stopped && !paused &&
            !IsSavingRun && !session.HasUncommittedSave && !IsReviveAdPending && !IsExtractionAdPending && !GameEntry.IsInBackground &&
            ProjectServices.Coordinator.Phase == ProjectRunPhase.Playing;

        /// <summary>
        /// 在完整死亡事件边界冻结操作，并保存尚未结算的选择状态。
        /// </summary>
        /// <param name="deathId">本次玩家死亡的唯一事件身份。</param>
        private void BeginDeathChoice(int deathId)
        {
            pendingPlayerDeathId = deathId;
            ClearInteractionTargets();
            ClearPlayerInput();
            ActivePanel = ExpeditionPanelKind.DeathChoice;
            BeginDeathPresentation();
            Message = "你已倒下，可选择看广告原地满血复活，或结算返回。";
            SaveMutation(Message);
            Changed?.Invoke();
        }

        /// <summary>
        /// 续关时重新展示未处理的死亡选择，不再次发放击杀奖励。
        /// </summary>
        private void RestoreDeathChoice()
        {
            ActivePanel = ExpeditionPanelKind.DeathChoice;
            ClearPlayerInput();
            BeginDeathPresentation();
            Message = "你已倒下，可选择看广告原地满血复活，或结算返回。";
        }

        /// <summary>
        /// 窗口被关闭后重新打开当前死亡选择，战斗始终保持冻结。
        /// </summary>
        /// <returns>当前死亡选择窗口已打开或成功重开时为真。</returns>
        public bool TryOpenDeathChoice()
        {
            if (!IsAwaitingDeathChoice || !IsRunning || disposed || stopped) return false;
            SetActivePanel(ExpeditionPanelKind.DeathChoice);
            return true;
        }

        /// <summary>
        /// 玩家明确放弃复活后按既有死亡规则冻结终局并结算返回。
        /// </summary>
        /// <returns>本次死亡结算被流程接受时为真。</returns>
        public bool TryConfirmDeath()
        {
            if (!CanChooseDeath) return false;
            return ProjectServices.Coordinator.TryEndRun(this, ProjectRunExitKind.Death);
        }

        /// <summary>
        /// 请求激励广告，完成后保存同一角色原地满血的完整状态；每次死亡均可再次请求。
        /// </summary>
        /// <param name="ct">当前窗口或调用方的取消令牌。</param>
        /// <returns>广告完成且复活已进入保存流程时为真，持久确认前仍禁止战斗推进。</returns>
        public async UniTask<bool> RequestAdReviveAsync(CancellationToken ct = default)
        {
            if (!CanChooseDeath || ct.IsCancellationRequested) return false;
            int deathId = pendingPlayerDeathId;
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
            reviveAdCancellation = cancellation;
            Message = "正在等待广告完成。";
            Changed?.Invoke();
            RewardedAdResult result;
            try
            {
                result = await ProjectServices.RewardedAds.ShowRewardedAsync(DeathRevivePlacement, cancellation.Token);
            }
            finally
            {
                reviveAdCancellation = null;
            }
            if (disposed || stopped || pendingPlayerDeathId != deathId) return false;
            if (!result.IsCompleted || cancellation.IsCancellationRequested || !CanChooseDeath)
            {
                Message = result.Status == RewardedAdStatus.Failed ? "广告未完成，请重试或选择结算返回。" : "复活已取消，可重新选择。";
                if (result.Status == RewardedAdStatus.Failed)
                    GameEntry.Get<ILogModule>().Error("Advertising", "复活广告失败：" + result.Error);
                Changed?.Invoke();
                return false;
            }
            if (!RefreshPermanentAttributes() || !World.TryRevivePlayer(Player))
            {
                HandleFault("玩家复活状态无法恢复，已保留死亡选择存档。");
                return false;
            }
            pendingPlayerDeathId = 0;
            deathPresentationStarted = false;
            deathPresentationElapsed = 0f;
            ClearPlayerInput();
            SaveMutation("已原地满血复活，本局进度与物品保留。", () => SetActivePanel(ExpeditionPanelKind.None));
            Changed?.Invoke();
            return true;
        }

        /// <summary>
        /// 后台、场景解绑或单局结束时撤销尚未完成的复活授权。
        /// </summary>
        private void CancelReviveAd()
        {
            if (reviveAdCancellation != null && !reviveAdCancellation.IsCancellationRequested) reviveAdCancellation.Cancel();
        }
    }
}
