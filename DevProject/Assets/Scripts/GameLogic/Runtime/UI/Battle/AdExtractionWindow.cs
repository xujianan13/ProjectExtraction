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
    /// 在广告资格确认后选择本关带出物品，携入装备由结算自动保留。
    /// </summary>
    public sealed class AdExtractionWindow : GameWindow
    {
        private ExpeditionRun run;
        private RectTransform entries;
        private UnityEngine.UI.Text summary;
        private UnityEngine.UI.Text feedback;
        private UnityEngine.UI.Button confirm;
        private UnityEngine.UI.Button close;
        private readonly HashSet<string> selectedIds = new();
        private bool selectionInitialized;

        /// <summary>
        /// 创建适配安全区的选择列表、规则说明与确认入口。
        /// </summary>
        protected override void Init()
        {
            RectTransform safe = ProjectUiFactory.Fill(RootTransform, "AdExtractionSafeArea", Color.clear);
            RectTransform panel = ProjectUiFactory.Panel(safe, "AdExtractionPanel", new Color(0.045f, 0.09f, 0.14f, 0.98f));
            panel.anchorMin = new Vector2(0.04f, 0.04f);
            panel.anchorMax = new Vector2(0.96f, 0.96f);
            panel.offsetMin = panel.offsetMax = Vector2.zero;
            RectTransform layout = ProjectUiFactory.Vertical(panel, "Layout", 10f);
            ProjectUiFactory.Stretch(layout, 18f);
            RectTransform header = ProjectUiFactory.Horizontal(layout, "Header", 8f);
            ProjectUiFactory.Height(header, 64f);
            UnityEngine.UI.Text title = ProjectUiFactory.Label(header, "撤离 · 关卡背包", 32, TextAnchor.MiddleLeft, Color.white);
            title.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().flexibleWidth = 1f;
            close = ProjectUiFactory.ActionButton(header, "返回", ReturnToPrevious);
            close.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().preferredWidth = 160f;
            summary = ProjectUiFactory.Label(layout, string.Empty, 24, TextAnchor.UpperLeft, new Color(0.84f, 0.93f, 1f));
            ProjectUiFactory.Height(summary.rectTransform, 112f);
            UnityEngine.UI.ScrollRect scroll = ProjectUiFactory.Scroll(layout, "Candidates", out entries);
            UnityEngine.UI.LayoutElement size = scroll.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            size.flexibleHeight = 1f;
            size.minHeight = 80f;
            feedback = ProjectUiFactory.Label(layout, string.Empty, 22, TextAnchor.UpperLeft, new Color(1f, 0.85f, 0.58f));
            ProjectUiFactory.Height(feedback.rectTransform, 64f);
            confirm = ProjectUiFactory.ActionButton(layout, "确认撤离", Confirm);
            ProjectUiFactory.Height((RectTransform)confirm.transform, 80f);
        }

        /// <summary>
        /// 绑定本局与当前选择，关闭窗口时只释放本轮选择面板。
        /// </summary>
        /// <param name="args">包含当前单局的窗口参数。</param>
        protected override void Open(GameWindowOpenArgs args)
        {
            run = args.UserData as ExpeditionRun;
            selectedIds.Clear();
            selectionInitialized = false;
            if (run == null) return;
            ExpeditionRun current = run;
            current.Changed += Refresh;
            OpenScope.Defer(() =>
            {
                current.Changed -= Refresh;
                current.ClosePanel(ExpeditionPanelKind.AdExtraction);
                if (ReferenceEquals(run, current)) run = null;
            });
            IInputModule input = GameEntry.Get<IInputModule>();
            if (input != null)
            {
                System.IDisposable back = input.RegisterBackHandler(() => { ReturnToPrevious(); return true; }, 250);
                OpenScope.Defer(back.Dispose);
            }
            Refresh();
        }

        /// <summary>
        /// 按权威物品候选刷新勾选状态和数量门禁。
        /// </summary>
        private void Refresh()
        {
            if (run == null) return;
            if (!run.IsRunning || run.ActivePanel != ExpeditionPanelKind.AdExtraction)
            {
                if (State == GameWindowState.Opened) CloseSelfAsync().Forget();
                return;
            }
            IReadOnlyList<ProjectItem> candidates = run.AdExtractionCandidates;
            var candidateIds = new HashSet<string>();
            foreach (ProjectItem item in candidates) candidateIds.Add(item.Id);
            selectedIds.RemoveWhere(id => !candidateIds.Contains(id));
            int limit = run.AdExtractionItemLimit;
            bool selectAll = candidates.Count < limit;
            if (!selectionInitialized)
            {
                if (selectAll) foreach (ProjectItem item in candidates) selectedIds.Add(item.Id);
                selectionInitialized = true;
            }
            summary.text = "已选择 " + selectedIds.Count + " / " + limit + " 件关卡背包物品\n出战装备保留，不占名额。\n" +
                (selectAll ? "不足上限时默认全选，可取消勾选。" : "最多选择 " + limit + " 件，其余关卡背包物品将丢失。");
            ProjectUiFactory.Clear(entries);
            bool available = run.HasAdExtractionAuthorization && !run.IsSavingRun;
            foreach (ProjectItem item in candidates)
            {
                string id = item.Id;
                bool chosen = selectedIds.Contains(id);
                string caption = (chosen ? "[✓] " : "[ ] ") + ProjectEquipmentText.Title(item);
                UnityEngine.UI.Button button = ProjectUiFactory.ActionButton(entries, caption, () => Toggle(id));
                button.interactable = available && (chosen || selectedIds.Count < limit);
                button.GetComponentInChildren<UnityEngine.UI.Text>().color = ProjectEquipmentText.QualityColor(item);
                ProjectUiFactory.Height((RectTransform)button.transform, 72f);
                UnityEngine.UI.Text details = ProjectUiFactory.Label(entries, ProjectEquipmentText.Details(item), 22, TextAnchor.UpperLeft, Color.white);
                details.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().minHeight = 70f;
            }
            if (candidates.Count == 0)
            {
                UnityEngine.UI.Text empty = ProjectUiFactory.Label(entries, "关卡背包为空，可直接撤离，出战装备保留。", 25, TextAnchor.MiddleLeft, Color.white);
                empty.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().minHeight = 90f;
            }
            confirm.interactable = available && selectedIds.Count <= limit;
            close.interactable = !run.IsSavingRun;
            feedback.text = run.IsSavingRun ? "正在保存，请稍候。" : run.Message;
        }

        /// <summary>
        /// 根据物品稳定身份切换本次撤离选择。
        /// </summary>
        /// <param name="id">候选物品身份。</param>
        private void Toggle(string id)
        {
            if (run == null || run.IsSavingRun || !run.HasAdExtractionAuthorization) return;
            if (!selectedIds.Remove(id) && selectedIds.Count < run.AdExtractionItemLimit) selectedIds.Add(id);
            Refresh();
        }

        /// <summary>
        /// 提交冻结的物品身份，由单局执行一次终局结算。
        /// </summary>
        private void Confirm()
        {
            if (run == null || !confirm.interactable) return;
            if (!run.TryConfirmAdExtraction(new List<string>(selectedIds))) feedback.text = run.Message;
        }

        /// <summary>
        /// 关闭选择并由单局恢复之前的处理面板，保留已经完成的广告资格。
        /// </summary>
        private void ReturnToPrevious()
        {
            if (run == null || !close.interactable) return;
            run.ClosePanel(ExpeditionPanelKind.AdExtraction);
            CloseSelfAsync().Forget();
        }
    }
}
