using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using GameFramework;
using GameLogic.Project;
using GameLogic.Tables;
using GameLogic.UI.Common;
using UnityEngine;

namespace GameLogic.UI.Guide
{
    /// <summary>
    /// 可重复阅读的分页新手指南。
    /// </summary>
    public sealed class ProjectGuideWindow : GameWindow
    {
        private ProjectSession session;
        private IReadOnlyList<CfgGuideRow> pages;
        private UnityEngine.UI.ScrollRect scroll;
        private UnityEngine.UI.Text progress;
        private UnityEngine.UI.Text pageTitle;
        private UnityEngine.UI.Text pageBody;
        private UnityEngine.UI.Text feedback;
        private UnityEngine.UI.Button previousButton;
        private UnityEngine.UI.Button nextButton;
        private UnityEngine.UI.Button understoodButton;
        private UnityEngine.UI.Button retryButton;
        private int pageIndex;
        private bool operating;
        private bool ownPending;
        private string loadError;

        /// <summary>
        /// 创建安全区内的文本卡片、进度与翻页控件。
        /// </summary>
        protected override void Init()
        {
            RectTransform surface = ProjectUiFactory.Fill(RootTransform, "GuideContent", new Color(0.035f, 0.07f, 0.11f, 0.98f));
            RectTransform layout = ProjectUiFactory.Vertical(surface, "Layout", 12f);
            ProjectUiFactory.Stretch(layout, 22f);
            RectTransform header = ProjectUiFactory.Horizontal(layout, "Header", 10f);
            ProjectUiFactory.Height(header, 76f);
            UnityEngine.UI.Text heading = ProjectUiFactory.Label(header, "玩法指南", 32, TextAnchor.MiddleLeft, Color.white);
            heading.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().flexibleWidth = 1f;
            UnityEngine.UI.Button close = ProjectUiFactory.ActionButton(header, "关闭", () => CloseSelfAsync().Forget());
            close.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().preferredWidth = 110f;
            progress = ProjectUiFactory.Label(layout, string.Empty, 22, TextAnchor.MiddleLeft, new Color(0.75f, 0.89f, 0.96f));
            ProjectUiFactory.Height(progress.rectTransform, 45f);

            scroll = ProjectUiFactory.Scroll(layout, "GuideCard", out RectTransform content);
            UnityEngine.UI.LayoutElement scrollSize = scroll.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            scrollSize.flexibleHeight = 1f;
            scrollSize.minHeight = 130f;
            pageTitle = ProjectUiFactory.Label(content, string.Empty, 30, TextAnchor.MiddleLeft, Color.white);
            ProjectUiFactory.Height(pageTitle.rectTransform, 74f);
            pageBody = ProjectUiFactory.Label(content, string.Empty, 25, TextAnchor.UpperLeft, new Color(0.89f, 0.95f, 0.98f));
            ProjectUiFactory.Height(pageBody.rectTransform, 530f);

            feedback = ProjectUiFactory.Label(layout, string.Empty, 20, TextAnchor.MiddleLeft, new Color(1f, 0.81f, 0.52f));
            ProjectUiFactory.Height(feedback.rectTransform, 58f);
            RectTransform actions = ProjectUiFactory.Horizontal(layout, "Actions", 10f);
            ProjectUiFactory.Height(actions, 82f);
            previousButton = ProjectUiFactory.ActionButton(actions, "上一项", () => Turn(-1));
            nextButton = ProjectUiFactory.ActionButton(actions, "下一项", () => Turn(1));
            understoodButton = ProjectUiFactory.ActionButton(actions, "已了解", () => CompleteAsync().Forget());
            retryButton = ProjectUiFactory.ActionButton(layout, "重试保存", () => RetryAsync().Forget());
            ProjectUiFactory.Height(retryButton.GetComponent<RectTransform>(), 68f);
        }

        /// <summary>
        /// 读取当前配置及会话，监听存档状态。
        /// </summary>
        /// <param name="args">窗口打开参数。</param>
        protected override void Open(GameWindowOpenArgs args)
        {
            session = ProjectServices.Session;
            pages = ProjectGuideConfig.All();
            pageIndex = 0;
            if (!ProjectGuideConfig.Validate(out loadError)) feedback.text = loadError;
            if (session == null) feedback.text = "项目数据尚未就绪。";
            else
            {
                session.Changed += Refresh;
                OpenScope.Defer(() => session.Changed -= Refresh);
            }
        }

        /// <summary>
        /// 显示当前指南页。
        /// </summary>
        /// <param name="args">窗口显示参数。</param>
        protected override void Show(GameWindowOpenArgs args) => Refresh();

        /// <summary>
        /// 更新文本卡片及翻页、保存控件。
        /// </summary>
        private void Refresh()
        {
            bool valid = loadError == null && pages != null && pages.Count > 0;
            progress.text = valid ? (pageIndex + 1) + " / " + pages.Count : "指南不可用";
            if (valid)
            {
                bool titleReady = ProjectGuideConfig.TryFormat(pages[pageIndex].Title, out string title, out string titleError);
                bool bodyReady = ProjectGuideConfig.TryFormat(pages[pageIndex].Body, out string body, out string bodyError);
                valid = titleReady && bodyReady;
                pageTitle.text = valid ? title : string.Empty;
                pageBody.text = valid ? body : titleReady ? bodyError : titleError;
                if (!valid) feedback.text = pageBody.text;
            }
            else { pageTitle.text = string.Empty; pageBody.text = loadError; }
            previousButton.interactable = valid && pageIndex > 0 && !operating;
            nextButton.gameObject.SetActive(valid && pageIndex < pages.Count - 1);
            nextButton.interactable = valid && !operating;
            understoodButton.gameObject.SetActive(valid && pageIndex == pages.Count - 1);
            understoodButton.interactable = valid && session != null && !operating && !ownPending && session.CanEditPreferences;
            retryButton.gameObject.SetActive(ownPending && session?.HasPendingSave == true);
            retryButton.interactable = ownPending && session?.HasPendingSave == true && !operating;
        }

        /// <summary>
        /// 翻到前后相邻的卡片并回到顶部。
        /// </summary>
        /// <param name="delta">页码增量。</param>
        private void Turn(int delta)
        {
            if (pages == null || operating) return;
            pageIndex = Mathf.Clamp(pageIndex + delta, 0, pages.Count - 1);
            scroll.verticalNormalizedPosition = 1f;
            Refresh();
        }

        /// <summary>
        /// 在用户读完末页时记录已了解版本。
        /// </summary>
        /// <returns>异步保存操作。</returns>
        private async UniTaskVoid CompleteAsync()
        {
            if (session == null || operating || !understoodButton.interactable) return;
            operating = true;
            Refresh();
            ProjectOperationResult result = await session.MarkGuideSeenAsync(1);
            if (Root == null || State != GameWindowState.Opened) return;
            operating = false;
            ownPending = !result.Success && session.HasPendingSave;
            feedback.text = result.Message;
            Refresh();
            if (result.Success) await CloseSelfAsync();
        }

        /// <summary>
        /// 重试本窗口未完成的指南版本保存。
        /// </summary>
        /// <returns>异步保存操作。</returns>
        private async UniTaskVoid RetryAsync()
        {
            if (session == null || operating || !ownPending || !session.HasPendingSave) return;
            operating = true;
            Refresh();
            ProjectOperationResult result = await session.RetryPendingSaveAsync();
            if (Root == null || State != GameWindowState.Opened) return;
            operating = false;
            ownPending = !result.Success && session.HasPendingSave;
            feedback.text = result.Message;
            Refresh();
            if (result.Success) await CloseSelfAsync();
        }
    }
}
