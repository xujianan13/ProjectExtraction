using Cysharp.Threading.Tasks;
using GameFramework;
using GameLogic.Gameplay.Expedition;
using GameLogic.Gameplay.Roguelike;
using GameLogic.UI.Common;
using UnityEngine;

namespace GameLogic.UI.Roguelike
{
    /// <summary>
    /// 只读展示本局已兑现强化及其等级的模态窗口。
    /// </summary>
    public sealed class RogueOverviewWindow : GameWindow
    {
        private ExpeditionRun run;
        private RectTransform entries;
        private UnityEngine.UI.Text summary;

        /// <summary>
        /// 创建安全区内的标题、滚动强化列表与关闭入口。
        /// </summary>
        protected override void Init()
        {
            RectTransform safeArea = ProjectUiFactory.Fill(RootTransform, "RogueOverviewSafeArea", Color.clear);
            RectTransform panel = ProjectUiFactory.Panel(safeArea, "RogueOverviewPanel", new Color(0.055f, 0.11f, 0.16f, 0.98f));
            Place(panel, new Vector2(0.05f, 0.06f), new Vector2(0.95f, 0.94f));
            RectTransform layout = ProjectUiFactory.Vertical(panel, "Layout", 10f);
            ProjectUiFactory.Stretch(layout, 16f);
            RectTransform header = ProjectUiFactory.Horizontal(layout, "Header", 8f);
            ProjectUiFactory.Height(header, 68f);
            UnityEngine.UI.Text title = ProjectUiFactory.Label(header, "本局强化总览", 34, TextAnchor.MiddleLeft, Color.white);
            title.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().flexibleWidth = 1f;
            UnityEngine.UI.Button close = ProjectUiFactory.ActionButton(header, "关闭", () => CloseSelfAsync().Forget());
            close.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().preferredWidth = 110f;
            summary = ProjectUiFactory.Label(layout, string.Empty, 22, TextAnchor.MiddleLeft, new Color(0.82f, 0.91f, 0.95f));
            ProjectUiFactory.Height(summary.rectTransform, 62f);
            UnityEngine.UI.ScrollRect scroll = ProjectUiFactory.Scroll(layout, "Upgrades", out entries);
            UnityEngine.UI.LayoutElement size = scroll.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            size.flexibleHeight = 1f;
            size.minHeight = 100f;
        }

        /// <summary>
        /// 绑定本局变化，并在关闭时释放仅由总览拥有的面板状态。
        /// </summary>
        /// <param name="args">包含当前单局的窗口参数。</param>
        protected override void Open(GameWindowOpenArgs args)
        {
            run = args.UserData as ExpeditionRun;
            if (run == null) return;
            ExpeditionRun current = run;
            current.Changed += Refresh;
            OpenScope.Defer(() =>
            {
                current.Changed -= Refresh;
                current.ClosePanel(ExpeditionPanelKind.RogueOverview);
                if (ReferenceEquals(run, current)) run = null;
            });
        }

        /// <summary>
        /// 首次显示时读取已兑现的强化快照。
        /// </summary>
        /// <param name="args">窗口显示参数。</param>
        protected override void Show(GameWindowOpenArgs args) => Refresh();

        /// <summary>
        /// 从只读快照刷新已获得强化列表。
        /// </summary>
        private void Refresh()
        {
            if (run == null) return;
            if (run.ActivePanel != ExpeditionPanelKind.RogueOverview || !run.IsRunning)
            {
                if (State == GameWindowState.Opened) CloseSelfAsync().Forget();
                return;
            }
            var upgrades = run.RogueUpgrades;
            summary.text = "已获得强化 " + upgrades.Count + " 种 · 随机战利品不计入此列表";
            ProjectUiFactory.Clear(entries);
            if (upgrades.Count == 0)
            {
                UnityEngine.UI.Text empty = ProjectUiFactory.Label(entries, "本局尚未获得强化", 24, TextAnchor.MiddleCenter, Color.white);
                ProjectUiFactory.Height(empty.rectTransform, 90f);
                return;
            }
            foreach (RogueUpgradeSnapshot upgrade in upgrades)
            {
                string rank = "Lv." + upgrade.Rank + (upgrade.MaxRank > 0 ? " / " + upgrade.MaxRank : string.Empty);
                string caption = upgrade.Title + " · " + rank + "\n" + upgrade.Description;
                UnityEngine.UI.Text entry = ProjectUiFactory.Label(entries, caption, 24, TextAnchor.MiddleLeft, Color.white);
                ProjectUiFactory.Height(entry.rectTransform, ProjectEquipmentText.PreferredHeight(caption, 112f) + 32f);
            }
        }

        /// <summary>
        /// 将面板约束在屏幕安全区内。
        /// </summary>
        /// <param name="rect">目标矩形。</param>
        /// <param name="min">左下锚点。</param>
        /// <param name="max">右上锚点。</param>
        private static void Place(RectTransform rect, Vector2 min, Vector2 max)
        {
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
