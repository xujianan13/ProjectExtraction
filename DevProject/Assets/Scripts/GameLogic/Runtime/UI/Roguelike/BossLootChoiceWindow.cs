using System.Collections.Generic;
using System.Text;
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
    /// 展示 Boss 每轮三个冻结物品，先查看详情再领取一件。
    /// </summary>
    public sealed class BossLootChoiceWindow : GameWindow
    {
        private ExpeditionRun run;
        private UnityEngine.UI.Text title;
        private UnityEngine.UI.Text message;
        private UnityEngine.UI.Button claim;
        private RectTransform candidates;
        private RectTransform details;
        private string displayedOfferId;
        private int selectedIndex;
        private bool busy;

        /// <summary>
        /// 在安全区创建轮次说明、候选列表、可滚动详情和唯一领取按钮。
        /// </summary>
        protected override void BindComponents()
        {
            RectTransform safeArea = ProjectUiFactory.Fill(RootTransform, "BossLootSafeArea", Color.clear);
            RectTransform panel = ProjectUiFactory.Panel(safeArea, "BossLootPanel", new Color(0.055f, 0.09f, 0.14f, 0.98f));
            panel.anchorMin = new Vector2(0.04f, 0.04f);
            panel.anchorMax = new Vector2(0.96f, 0.96f);
            panel.offsetMin = panel.offsetMax = Vector2.zero;
            RectTransform layout = ProjectUiFactory.Vertical(panel, "BossLootLayout", 8f);
            ProjectUiFactory.Stretch(layout, 14f);
            title = ProjectUiFactory.Label(layout, "选择 Boss 战利品", 34, TextAnchor.MiddleCenter, Color.white);
            ProjectUiFactory.Height(title.rectTransform, 64f);
            UnityEngine.UI.Text guidance = ProjectUiFactory.Label(layout, "每轮领取一件。点选候选查看完整属性、词条与装备对比。",
                22, TextAnchor.MiddleLeft, new Color(0.82f, 0.91f, 0.95f));
            ProjectUiFactory.Height(guidance.rectTransform, 62f);
            UnityEngine.UI.ScrollRect listScroll = ProjectUiFactory.Scroll(layout, "BossCandidates", out candidates);
            UnityEngine.UI.LayoutElement listSize = listScroll.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            listSize.flexibleHeight = 0.8f;
            listSize.minHeight = 90f;
            UnityEngine.UI.ScrollRect detailScroll = ProjectUiFactory.Scroll(layout, "BossItemDetails", out details);
            UnityEngine.UI.LayoutElement detailSize = detailScroll.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            detailSize.flexibleHeight = 1.2f;
            detailSize.minHeight = 100f;
            claim = ProjectUiFactory.ActionButton(layout, "领取所选物品", Claim);
            ProjectUiFactory.Height((RectTransform)claim.transform, 70f);
            message = ProjectUiFactory.Label(layout, string.Empty, 21, TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.58f));
            ProjectUiFactory.Height(message.rectTransform, 58f);
        }

        /// <summary>
        /// 绑定本局奖励变化，并在该窗口内消费返回动作。
        /// </summary>
        /// <param name="args">携带当前单局的窗口参数。</param>
        protected override void Open(GameWindowOpenArgs args)
        {
            run = args.UserData as ExpeditionRun;
            displayedOfferId = null;
            selectedIndex = 0;
            busy = false;
            if (run == null) return;
            ExpeditionRun current = run;
            current.BossChoiceChanged += Refresh;
            OpenScope.Defer(() =>
            {
                current.BossChoiceChanged -= Refresh;
                if (ReferenceEquals(run, current)) run = null;
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
        /// 使用已经保存的候选刷新本轮，保存期间隐藏详情并禁用领取。
        /// </summary>
        private void Refresh()
        {
            if (run == null || Root == null) return;
            if (!run.IsRunning || !run.IsChoosingBoss && !run.IsSavingRun)
            {
                if (State == GameWindowState.Opened) CloseSelfAsync().Forget();
                return;
            }
            ProjectUiFactory.Clear(candidates);
            ProjectUiFactory.Clear(details);
            BossLootChoiceSnapshot choice = run.PendingBossChoice;
            if (choice == null)
            {
                title.text = "正在保存 Boss 奖励";
                message.text = "保存完成后继续，操作暂不可用。";
                claim.interactable = false;
                return;
            }
            if (displayedOfferId != choice.OfferId)
            {
                displayedOfferId = choice.OfferId;
                selectedIndex = 0;
            }
            title.text = "Boss 战利品 · 第 " + choice.Round + " / " + choice.TotalRounds + " 轮";
            message.text = run.RogueMessage;
            IReadOnlyList<ProjectItem> options = choice.Options;
            for (int index = 0; index < options.Count; index++)
            {
                int selected = index;
                ProjectItem item = options[index];
                string caption = (index == selectedIndex ? "已选 · " : string.Empty) + CandidateTitle(item) +
                    "\n等级 " + item.RequiredLevel + " · 售价 " + item.SellGold + " 金币";
                UnityEngine.UI.Button button = ProjectUiFactory.ActionButton(candidates, caption, () => SelectCandidate(selected));
                ProjectUiFactory.Height((RectTransform)button.transform, 98f);
                button.interactable = !busy && !run.IsSavingRun;
            }
            ProjectItem currentItem = options[selectedIndex];
            AddDetail(CandidateTitle(currentItem), ProjectEquipmentText.QualityColor(currentItem));
            if (currentItem.Type == ProjectItemType.Collection) AddDetail(CollectionDetails(currentItem), Color.white);
            else
            {
                AddDetail(ProjectEquipmentText.Details(currentItem), Color.white);
                AddDetail(ProjectEquipmentText.EquippedDetails(currentItem, run.Inventory), new Color(0.78f, 0.91f, 1f));
                if (run.TryPreviewEquipment(currentItem, out ProjectCharacterStats current, out ProjectCharacterStats preview))
                    AddDetail(ExpeditionEquipmentComparison.Format(current, preview), Color.white);
            }
            AddDetail("售价 " + currentItem.SellGold + " 金币", new Color(1f, 0.86f, 0.56f));
            claim.interactable = !busy && !run.IsSavingRun;
        }

        /// <summary>
        /// 查看某个冻结物品，不领取也不改变随机游标。
        /// </summary>
        /// <param name="index">本轮物品序号。</param>
        private void SelectCandidate(int index)
        {
            if (busy || run == null || run.IsSavingRun || run.PendingBossChoice == null) return;
            selectedIndex = index;
            Refresh();
        }

        /// <summary>
        /// 提交唯一领取动作，保存中保持窗口且不暴露未确认物品。
        /// </summary>
        private void Claim()
        {
            if (busy || run == null || run.IsSavingRun) return;
            BossLootChoiceSnapshot choice = run.PendingBossChoice;
            if (choice == null) return;
            busy = true;
            run.TryChooseBoss(choice.OfferId, choice.Revision, selectedIndex);
            busy = false;
            Refresh();
        }

        /// <summary>
        /// 生成不包含内部身份的候选标题。
        /// </summary>
        /// <param name="item">完整冻结物品。</param>
        /// <returns>品质、名称与装备或收藏类别。</returns>
        private static string CandidateTitle(ProjectItem item) => item.Type == ProjectItemType.Collection ?
            "[" + ProjectEquipmentRules.QualityName(item.Quality) + "] " + item.Name + " · 收藏" : ProjectEquipmentText.ColoredTitle(item);

        /// <summary>
        /// 展示收藏系列、来源和关联成就的完整配置说明。
        /// </summary>
        /// <param name="item">完整冻结收藏实例。</param>
        /// <returns>玩家可读的收藏详情。</returns>
        private static string CollectionDetails(ProjectItem item)
        {
            ProjectCollectionDefinition definition = ProjectV2RewardConfig.Collection(item.CollectionId);
            var text = new StringBuilder("来源：").Append(definition.Source)
                .Append("\n").Append(ProjectEquipmentText.CollectionRegistration(item)).Append("\n收藏用于系列成就，可出售。\n");
            IReadOnlyList<ProjectCollectionAchievementDefinition> achievements = ProjectV2RewardConfig.CollectionAchievements(out string error);
            if (error != null) return text.Append("收藏成就配置不可用。").ToString();
            foreach (ProjectCollectionAchievementDefinition achievement in achievements)
            {
                if (!achievement.RequiredCollectionIds.Contains(item.CollectionId)) continue;
                text.Append("\n关联成就：").Append(achievement.Name).Append("\n收集条件：");
                for (int index = 0; index < achievement.RequiredCollectionIds.Count; index++)
                {
                    if (index > 0) text.Append("、");
                    text.Append(ProjectV2RewardConfig.Collection(achievement.RequiredCollectionIds[index]).Name);
                }
                foreach (ProjectAttributeValue bonus in achievement.AttributeRewards)
                    text.Append("\n奖励：").Append(ProjectEquipmentText.ItemAttribute(bonus.Attribute, bonus.Value));
                text.AppendLine();
            }
            return text.ToString().TrimEnd();
        }

        /// <summary>
        /// 向滚动详情添加能根据换行及宽度自动计算高度的文本块。
        /// </summary>
        /// <param name="value">详情文本。</param>
        /// <param name="color">文字颜色。</param>
        private void AddDetail(string value, Color color)
        {
            UnityEngine.UI.Text label = ProjectUiFactory.Label(details, value, 23, TextAnchor.UpperLeft, color);
            UnityEngine.UI.LayoutElement size = label.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            size.minHeight = 42f;
        }
    }
}
