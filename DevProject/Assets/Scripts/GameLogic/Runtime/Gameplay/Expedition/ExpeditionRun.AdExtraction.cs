using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;
using GameLogic.Advertising;
using GameLogic.Project;

namespace GameLogic.Gameplay.Expedition
{
    public sealed partial class ExpeditionRun
    {
        private const string AdExtractionPlacement = "expedition_extract";
        private CancellationTokenSource extractionAdCancellation;
        public bool HasAdExtractionAuthorization { get; private set; }
        public int AdExtractionItemLimit => PrototypeRules.AdExtractionItemLimit;
        public IReadOnlyList<ProjectItem> AdExtractionCandidates => Inventory;
        public bool IsExtractionAdPending => extractionAdCancellation != null;
        public bool CanSuspend => CanUseLeaveActions &&
            (ActivePanel == ExpeditionPanelKind.Pause || ActivePanel == ExpeditionPanelKind.DeathChoice);
        public bool CanRequestAdExtraction => CanSuspend && !HasAdExtractionAuthorization;
        private bool CanConfirmAdExtraction => CanUseLeaveActions && HasAdExtractionAuthorization &&
            ActivePanel == ExpeditionPanelKind.AdExtraction;
        private bool CanUseLeaveActions => IsRunning && IsReady && !disposed && !stopped && !paused && !IsSavingRun &&
            !session.HasUncommittedSave && !IsReviveAdPending && !IsExtractionAdPending && !GameEntry.IsInBackground &&
            ProjectServices.Coordinator.Phase == ProjectRunPhase.Playing;

        /// <summary>
        /// 重新打开已确认广告资格对应的关卡背包选择，不重复播放广告。
        /// </summary>
        /// <returns>选择面板可用且已经打开时为真。</returns>
        public bool TryOpenAdExtraction()
        {
            if (!HasAdExtractionAuthorization || !CanUseLeaveActions ||
                ActivePanel != ExpeditionPanelKind.Pause && ActivePanel != ExpeditionPanelKind.DeathChoice &&
                ActivePanel != ExpeditionPanelKind.AdExtraction) return false;
            SetActivePanel(ExpeditionPanelKind.AdExtraction);
            return true;
        }

        /// <summary>
        /// 保存广告完成资格后展示本关物品选择，取消、失败或迟到回调不授予资格。
        /// </summary>
        /// <param name="ct">调用方可选取消令牌。</param>
        /// <returns>广告完成资格及本局检查点确认后为真。</returns>
        public async UniTask<bool> RequestAdExtractionAsync(CancellationToken ct = default)
        {
            if (!CanRequestAdExtraction || ct.IsCancellationRequested) return false;
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
            extractionAdCancellation = cancellation;
            Message = "正在等待撤离广告完成。";
            Changed?.Invoke();
            RewardedAdResult result;
            try { result = await ProjectServices.RewardedAds.ShowRewardedAsync(AdExtractionPlacement, cancellation.Token); }
            finally { extractionAdCancellation = null; }
            if (disposed || stopped) return false;
            if (!result.IsCompleted || cancellation.IsCancellationRequested || !CanRequestAdExtraction)
            {
                Message = result.Status == RewardedAdStatus.Failed ? "撤离广告未完成，可重试。" : "广告撤离已取消。";
                if (result.Status == RewardedAdStatus.Failed)
                    GameEntry.Get<ILogModule>().Error("Advertising", "撤离广告失败：" + result.Error);
                Changed?.Invoke();
                return false;
            }
            HasAdExtractionAuthorization = true;
            committing = true;
            bool saved = await ProjectServices.Coordinator.SaveRunBoundaryAsync(this, ProjectRunPhase.Playing);
            if (disposed || stopped) return false;
            committing = false;
            if (!saved) return false;
            Message = "请选择最多 " + AdExtractionItemLimit + " 件关卡物品带回。";
            return TryOpenAdExtraction();
        }

        /// <summary>
        /// 提交广告撤离的稳定物品身份，由统一结算归还携入装备和选中物品。
        /// </summary>
        /// <param name="itemIds">选中的零至上限件关卡物品身份。</param>
        /// <returns>选择合法且终局已被接纳时为真。</returns>
        public bool TryConfirmAdExtraction(IReadOnlyList<string> itemIds) => CanConfirmAdExtraction && itemIds != null &&
            ProjectServices.Coordinator.TryEndRun(this, ProjectRunExitKind.AdExtraction, itemIds);

        /// <summary>
        /// 后台、场景解绑或单局结束时撤销尚未完成的撤离广告。
        /// </summary>
        private void CancelExtractionAd()
        {
            if (extractionAdCancellation != null && !extractionAdCancellation.IsCancellationRequested) extractionAdCancellation.Cancel();
        }
    }
}
