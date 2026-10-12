using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using GameFramework;
using GameLogic.Gameplay.Expedition;
using GameLogic.Project;
using GameLogic.UI.Common;
using UnityEngine;

namespace GameLogic.UI.Battle
{
    /// <summary>
    /// 区域领奖完成后的出口选择，只提交明确的单向转换或带回请求。
    /// </summary>
    public sealed class RegionExitWindow : GameWindow
    {
        private ExpeditionRun run;
        private UnityEngine.UI.Text title;
        private UnityEngine.UI.Text summary;
        private UnityEngine.UI.Button bringBack;
        private UnityEngine.UI.Button continueRegion;
        private UnityEngine.UI.Button stay;

        /// <summary>
        /// 建立安全区内的三项出口操作。
        /// </summary>
        protected override void Init()
        {
            RectTransform safe = ProjectUiFactory.Fill(RootTransform, "ExitSafeArea", Color.clear);
            RectTransform panel = ProjectUiFactory.Panel(safe, "ExitPanel", new Color(0.05f, 0.1f, 0.14f, 0.98f));
            panel.anchorMin = new Vector2(0.06f, 0.1f);
            panel.anchorMax = new Vector2(0.94f, 0.9f);
            panel.offsetMin = panel.offsetMax = Vector2.zero;
            RectTransform layout = ProjectUiFactory.Vertical(panel, "ExitLayout", 12f);
            ProjectUiFactory.Stretch(layout, 22f);
            title = ProjectUiFactory.Label(layout, "区域探索完成", 34, TextAnchor.MiddleCenter, Color.white);
            ProjectUiFactory.Height(title.rectTransform, 64f);
            var scroll = ProjectUiFactory.Scroll(layout, "ExitActions", out RectTransform actions);
            var scrollSize = scroll.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            scrollSize.flexibleHeight = 1f;
            scrollSize.minHeight = 100f;
            summary = ProjectUiFactory.Label(actions, "", 25, TextAnchor.MiddleLeft, Color.white);
            summary.name = "PendingReturnSummary";
            ProjectUiFactory.Height(summary.rectTransform, 150f);
            bringBack = ProjectUiFactory.ActionButton(actions, "带回结束", BringBack);
            continueRegion = ProjectUiFactory.ActionButton(actions, "继续下一区域", ContinueRegion);
            stay = ProjectUiFactory.ActionButton(actions, "留在本区", () => CloseSelfAsync().Forget());
            foreach (var button in new[] { bringBack, continueRegion, stay }) ProjectUiFactory.Height((RectTransform)button.transform, 80f);
        }

        /// <summary>
        /// 绑定区域出口面板，关闭时释放面板暂停。
        /// </summary>
        /// <param name="args">包含单局的窗口参数。</param>
        protected override void Open(GameWindowOpenArgs args)
        {
            run = args.UserData as ExpeditionRun;
            if (run == null) return;
            run.Changed += Refresh;
            ExpeditionRun current = run;
            OpenScope.Defer(() =>
            {
                current.Changed -= Refresh;
                current.ClosePanel(ExpeditionPanelKind.ExtractionConfirmation);
                if (ReferenceEquals(run, current)) run = null;
            });
            Refresh();
        }

        /// <summary>
        /// 区分最终通关与区域继续入口，并在保存时锁定操作。
        /// </summary>
        private void Refresh()
        {
            if (run == null) return;
            if (!run.IsRunning || run.ActivePanel != ExpeditionPanelKind.ExtractionConfirmation)
            {
                if (State == GameWindowState.Opened) CloseSelfAsync().Forget();
                return;
            }
            title.text = run.IsFinalRegion ? "最终挑战完成" : "区域 " + (run.CurrentRegionIndex + 1) + " 探索完成";
            summary.text = run.IsSavingRun ? "正在保存本局进度，请稍候。" :
                run.IsFinalRegion ? "全部尾王奖励已领取。带回结束可携出背包和安全箱中的全部持有物。" :
                "带回结束：携出全部持有物并结束本局。\n继续深入：单向进入下一区域，风险和收益继续累积。\n留在本区：继续探索当前区域。";
            if (!run.IsSavingRun) summary.text = PendingReturnSummary() + "\n\n" + summary.text;
            ProjectUiFactory.Height(summary.rectTransform, ProjectEquipmentText.PreferredHeight(summary.text, 150f) + 36f);
            continueRegion.gameObject.SetActive(!run.IsFinalRegion);
            bool available = !run.IsSavingRun && run.CurrentRegion.ExitEnabled;
            bringBack.interactable = continueRegion.interactable = stay.interactable = available;
        }

        /// <summary>
        /// 以当前持有物和入场基线展示带回估值，不兑现金币或收藏登记。
        /// </summary>
        /// <returns>持有数量、回收估值、净变化及未登记收藏摘要。</returns>
        private string PendingReturnSummary()
        {
            IReadOnlyList<ProjectItem> items = run.Inventory;
            var registered = new HashSet<string>(ProjectServices.Session.CollectedCollectionIds, StringComparer.Ordinal);
            var newCollections = new HashSet<string>(StringComparer.Ordinal);
            long value = 0;
            foreach (ProjectItem item in items)
            {
                value += item.SellGold;
                if (item.Type == ProjectItemType.Collection && !registered.Contains(item.CollectionId)) newCollections.Add(item.CollectionId);
            }
            long net = value - run.CaptureStatistics().EntryItemSellValue - run.GoldSpent;
            return "待带回物品 " + items.Count + " 件 · 安全箱 " + run.SafeCount + " 件\n" +
                "回收估值 " + value + " 金币 · 净资产变化 " + net.ToString("+0;-0;0") + "\n" +
                "尚未登记收藏 " + newCollections.Count + " 种 · 本局击杀经验 " + run.KillExperience + "\n" +
                "估值仅供参考，带回不会直接发放对应金币。";
        }

        /// <summary>
        /// 明确提交正常带回结束请求。
        /// </summary>
        private void BringBack()
        {
            if (bringBack.interactable && run.TryConfirmExtraction()) CloseSelfAsync().Forget();
        }

        /// <summary>
        /// 提交单向进入下一探索区或最终场地的请求。
        /// </summary>
        private void ContinueRegion()
        {
            if (continueRegion.interactable && run.TryContinueRegion()) CloseSelfAsync().Forget();
        }
    }
}
