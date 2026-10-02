using Cysharp.Threading.Tasks;
using GameFramework;
using GameLogic.Gameplay.Expedition;
using GameLogic.Gameplay.Roguelike;
using GameLogic.Project;
using GameLogic.UI.Common;
using UnityEngine;

namespace GameLogic.UI.Roguelike
{
    /// <summary>
    /// 展示当前单局的三选一强化并阻止在选定前退出。
    /// </summary>
    public sealed class RogueChoiceWindow : GameWindow
    {
        private ExpeditionRun run;
        private UnityEngine.UI.Text title;
        private UnityEngine.UI.Text message;
        private RectTransform cards;
        private RectTransform actions;
        private UnityEngine.UI.LayoutElement actionsSize;
        private bool busy;

        /// <summary>
        /// 创建安全区内的说明、纵向滚动卡片及可选功能入口。
        /// </summary>
        protected override void BindComponents()
        {
            RectTransform safeArea = ProjectUiFactory.Fill(RootTransform, "RogueChoiceSafeArea", Color.clear);
            RectTransform panel = ProjectUiFactory.Panel(safeArea, "RogueChoicePanel", new Color(0.055f, 0.11f, 0.16f, 0.98f));
            Place(panel, new Vector2(0.05f, 0.08f), new Vector2(0.95f, 0.92f));
            RectTransform layout = ProjectUiFactory.Vertical(panel, "ChoiceLayout", 10f);
            ProjectUiFactory.Stretch(layout, 16f);
            title = ProjectUiFactory.Label(layout, "选择本局强化", 38, TextAnchor.MiddleCenter, Color.white);
            ProjectUiFactory.Height(title.rectTransform, 72f);
            UnityEngine.UI.Text guidance = ProjectUiFactory.Label(layout,
                "战斗已暂停。属性强化仅在本局生效；战利品遵循原有撤离与死亡规则。点击一项立即生效。",
                23, TextAnchor.MiddleLeft, new Color(0.82f, 0.91f, 0.95f));
            ProjectUiFactory.Height(guidance.rectTransform, 80f);
            UnityEngine.UI.ScrollRect scroll = ProjectUiFactory.Scroll(layout, "ChoiceCards", out cards);
            UnityEngine.UI.LayoutElement scrollSize = scroll.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            scrollSize.flexibleHeight = 1f;
            scrollSize.minHeight = 120f;
            actions = ProjectUiFactory.Vertical(layout, "ChoiceActions", 8f);
            actionsSize = actions.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            actions.gameObject.SetActive(false);
            message = ProjectUiFactory.Label(layout, string.Empty, 20, TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.58f));
            ProjectUiFactory.Height(message.rectTransform, 50f);
        }

        /// <summary>
        /// 订阅单局选择变化，并在窗口存续期间消费系统返回动作。
        /// </summary>
        /// <param name="args">携带当前单局的打开参数。</param>
        protected override void Open(GameWindowOpenArgs args)
        {
            run = args.UserData as ExpeditionRun;
            busy = false;
            if (run == null) return;
            run.RogueChoiceChanged += Refresh;
            OpenScope.Defer(() =>
            {
                run.RogueChoiceChanged -= Refresh;
                run = null;
            });
            IInputModule input = GameEntry.Get<IInputModule>();
            if (input != null)
            {
                System.IDisposable back = input.RegisterBackHandler(() => true, 200);
                OpenScope.Defer(() => back.Dispose());
            }
            Refresh();
        }

        /// <summary>
        /// 按当前选择代次重建卡片，旧按钮引用不再能提交奖励。
        /// </summary>
        private void Refresh()
        {
            if (run == null || Root == null) return;
            RogueChoiceSnapshot choice = run.PendingRogueChoice;
            if (!run.IsRunning || choice == null)
            {
                if (State == GameWindowState.Opened) CloseSelfAsync().Forget();
                return;
            }
            title.text = "选择本局强化";
            message.text = run.RogueMessage;
            ProjectUiFactory.Clear(cards);
            ProjectUiFactory.Clear(actions);
            string offerId = choice.OfferId;
            int revision = choice.Revision;
            foreach (RogueChoiceOption option in choice.Options)
            {
                int index = option.Index;
                bool locked = option.IsExtra && !choice.ExtraUnlocked;
                string caption = option.Reward.Title + "\n" + option.Reward.Description;
                if (option.Reward.Kind != RogueRewardKind.RandomLoot)
                {
                    int current = option.Reward.Rank - 1;
                    string limit = option.Reward.MaxRank > 0 ? " / " + option.Reward.MaxRank : string.Empty;
                    caption += "\n当前 Lv." + current + " → 下一层 Lv." + option.Reward.Rank + limit;
                }
                if (locked) caption += "\n第 4 项 · " +
                    (busy ? "正在确认授权" : run.CanRequestExtraRogueChoice ? "授权后可选择" : "尚未开放");
                UnityEngine.UI.Button button = ProjectUiFactory.ActionButton(cards, caption, () => Choose(offerId, revision, index));
                ProjectUiFactory.Height((RectTransform)button.transform, option.Reward.Kind == RogueRewardKind.RandomLoot ? 130f : 155f);
                button.interactable = !busy && !locked;
            }
            bool canUnlock = !choice.ExtraUnlocked && run.CanRequestExtraRogueChoice;
            if (canUnlock)
            {
                UnityEngine.UI.Button unlock = ProjectUiFactory.ActionButton(actions, "第 4 项 · 未解锁（请求授权）",
                    () => RequestExtraAsync(offerId, revision).Forget());
                ProjectUiFactory.Height((RectTransform)unlock.transform, 70f);
                unlock.interactable = !busy;
            }
            bool canRefresh = choice.CanRefresh;
            if (canRefresh)
            {
                UnityEngine.UI.Button refresh = ProjectUiFactory.ActionButton(actions, "刷新选项", () => RefreshChoice(offerId, revision));
                ProjectUiFactory.Height((RectTransform)refresh.transform, 70f);
                refresh.interactable = !busy;
            }
            int actionCount = (canUnlock ? 1 : 0) + (canRefresh ? 1 : 0);
            actions.gameObject.SetActive(actionCount > 0);
            actionsSize.minHeight = actionsSize.preferredHeight = actionCount * 70f + Mathf.Max(0, actionCount - 1) * 8f;
        }

        /// <summary>
        /// 选择当前代次的一项奖励。
        /// </summary>
        /// <param name="offerId">卡片对应的选择身份。</param>
        /// <param name="revision">卡片对应的选择代次。</param>
        /// <param name="index">卡片对应的选项序号。</param>
        private void Choose(string offerId, int revision, int index)
        {
            if (busy || run == null || !run.IsChoosingRogue) return;
            busy = true;
            if (run.TryChooseRogue(offerId, revision, index))
            {
                CloseSelfAsync().Forget();
                return;
            }
            busy = false;
            Refresh();
        }

        /// <summary>
        /// 请求当前代次刷新，成功后由单局事件生成新卡片。
        /// </summary>
        /// <param name="offerId">选择身份。</param>
        /// <param name="revision">选择代次。</param>
        private void RefreshChoice(string offerId, int revision)
        {
            if (busy || run == null || !run.IsChoosingRogue) return;
            busy = true;
            run.TryRefreshRogue(offerId, revision);
            busy = false;
            Refresh();
        }

        /// <summary>
        /// 通过单局授权提供者请求第四项，不在界面侧直接授权。
        /// </summary>
        /// <param name="offerId">选择身份。</param>
        /// <param name="revision">选择代次。</param>
        /// <returns>异步授权操作。</returns>
        private async UniTaskVoid RequestExtraAsync(string offerId, int revision)
        {
            if (busy || run == null || !run.IsChoosingRogue || !run.CanRequestExtraRogueChoice) return;
            busy = true;
            Refresh();
            await run.RequestExtraRogueChoiceAsync(offerId, revision);
            if (State != GameWindowState.Opened || run == null) return;
            busy = false;
            Refresh();
        }

        /// <summary>
        /// 按屏幕比例定位内容。
        /// </summary>
        /// <param name="rect">待布局节点。</param>
        /// <param name="minimum">左下锚点。</param>
        /// <param name="maximum">右上锚点。</param>
        private static void Place(RectTransform rect, Vector2 minimum, Vector2 maximum)
        {
            rect.anchorMin = minimum;
            rect.anchorMax = maximum;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
