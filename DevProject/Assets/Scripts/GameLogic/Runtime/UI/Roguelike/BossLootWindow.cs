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
    /// 只读展示 Boss 宝箱的全部冻结掉落，通过一个按钮一次领取。
    /// </summary>
    public sealed class BossLootWindow : GameWindow
    {
        private ExpeditionRun run;
        private UnityEngine.UI.Text title;
        private UnityEngine.UI.Text message;
        private UnityEngine.UI.Button claim;
        private UnityEngine.UI.Button close;
        private RectTransform items;
        private bool busy;

        /// <summary>
        /// 在安全区创建关闭入口、可滚动的全部物品详情和唯一领取按钮。
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
            RectTransform header = ProjectUiFactory.Horizontal(layout, "BossLootHeader", 8f);
            ProjectUiFactory.Height(header, 64f);
            title = ProjectUiFactory.Label(header, "Boss 战利品", 34, TextAnchor.MiddleLeft, Color.white);
            title.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().flexibleWidth = 1f;
            title.resizeTextForBestFit = true;
            title.resizeTextMinSize = 22;
            title.resizeTextMaxSize = 34;
            close = ProjectUiFactory.ActionButton(header, "关闭", CloseRewards);
            close.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().preferredWidth = 110f;
            UnityEngine.UI.Text guidance = ProjectUiFactory.Label(layout,
                "一次领取宝箱内全部物品。可关闭稍后领取，领完后前往出口确认带回。", 22, TextAnchor.MiddleLeft, new Color(0.82f, 0.91f, 0.95f));
            ProjectUiFactory.Height(guidance.rectTransform, 62f);
            UnityEngine.UI.ScrollRect scroll = ProjectUiFactory.Scroll(layout, "BossLootItems", out items);
            UnityEngine.UI.LayoutElement scrollSize = scroll.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            scrollSize.flexibleHeight = 1f;
            scrollSize.minHeight = 100f;
            claim = ProjectUiFactory.ActionButton(layout, "领取", Claim);
            ProjectUiFactory.Height((RectTransform)claim.transform, 70f);
            message = ProjectUiFactory.Label(layout, string.Empty, 21, TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.58f));
            ProjectUiFactory.Height(message.rectTransform, 58f);
        }

        /// <summary>
        /// 绑定宝箱变化，关闭时释放领奖暂停并保留未领取的整批掉落。
        /// </summary>
        /// <param name="args">携带当前单局的窗口参数。</param>
        protected override void Open(GameWindowOpenArgs args)
        {
            run = args.UserData as ExpeditionRun;
            busy = false;
            if (run == null) return;
            ExpeditionRun current = run;
            current.BossLootChanged += Refresh;
            OpenScope.Defer(() =>
            {
                current.BossLootChanged -= Refresh;
                current.CloseBossLoot();
                if (ReferenceEquals(run, current)) run = null;
            });
            IInputModule input = GameEntry.Get<IInputModule>();
            if (input != null)
            {
                System.IDisposable back = input.RegisterBackHandler(() => { CloseRewards(); return true; }, 200);
                OpenScope.Defer(() => back.Dispose());
            }
            Refresh();
        }

        /// <summary>
        /// 展示全部只读物品，保存期间保留当前详情并禁用领取和关闭。
        /// </summary>
        private void Refresh()
        {
            if (run == null || Root == null) return;
            if (!run.IsRunning || !run.IsClaimingBossLoot && !run.IsSavingRun)
            {
                if (State == GameWindowState.Opened) CloseSelfAsync().Forget();
                return;
            }
            close.interactable = claim.interactable = !busy && !run.IsSavingRun;
            BossLootSnapshot loot = run.PendingBossLoot;
            if (loot == null)
            {
                title.text = "正在保存 Boss 奖励";
                message.text = "保存完成后继续，操作暂不可用。";
                claim.interactable = false;
                return;
            }
            title.text = "Boss 战利品 · " + loot.Items.Count + " 件";
            message.text = run.RewardMessage;
            ProjectUiFactory.Clear(items);
            foreach (ProjectItem item in loot.Items)
            {
                RectTransform card = ProjectUiFactory.Vertical(items, "BossLootItem_" + item.Id, 6f);
                card.GetComponent<UnityEngine.UI.Image>().color = new Color(0.1f, 0.17f, 0.23f, 0.96f);
                card.GetComponent<UnityEngine.UI.VerticalLayoutGroup>().padding = new RectOffset(12, 12, 10, 10);
                string itemTitle = item.Type == ProjectItemType.Collection ?
                    "[" + ProjectEquipmentRules.QualityName(item.Quality) + "] " + item.Name + " · 收藏" : ProjectEquipmentText.ColoredTitle(item);
                AddDetail(card, itemTitle, ProjectEquipmentText.QualityColor(item));
                if (item.Type == ProjectItemType.Collection) AddDetail(card, CollectionDetails(item), Color.white);
                else
                {
                    AddDetail(card, ProjectEquipmentText.Details(item), Color.white);
                    AddDetail(card, ProjectEquipmentText.EquippedDetails(item, run.EntryEquipment), new Color(0.78f, 0.91f, 1f));
                }
                AddDetail(card, "等级 " + item.RequiredLevel + " · 售价 " + item.SellGold + " 金币\n领取后收入关卡背包，带回主城后可使用。",
                    new Color(1f, 0.86f, 0.56f));
            }
        }

        /// <summary>
        /// 一次提交整批物品，保存中保持窗口且不允许重复领取。
        /// </summary>
        private void Claim()
        {
            if (busy || run == null || run.IsSavingRun) return;
            BossLootSnapshot loot = run.PendingBossLoot;
            if (loot == null) return;
            busy = true;
            run.TryClaimBossLoot(loot.BatchId, loot.Revision);
            busy = false;
            Refresh();
        }

        /// <summary>
        /// 在没有保存事务时关闭窗口，作用域清理释放领奖暂停并保留整批掉落。
        /// </summary>
        private void CloseRewards()
        {
            if (busy || run == null || run.IsSavingRun) return;
            CloseSelfAsync().Forget();
        }

        /// <summary>
        /// 展示收藏系列、来源和关联成就的完整配置说明。
        /// </summary>
        /// <param name="item">完整冻结收藏实例。</param>
        /// <returns>玩家可读的收藏详情。</returns>
        private static string CollectionDetails(ProjectItem item)
        {
            ProjectCollectionDefinition definition = ProjectRewardConfig.Collection(item.CollectionId);
            var text = new StringBuilder("来源：").Append(definition.Source)
                .Append("\n").Append(ProjectEquipmentText.CollectionRegistration(item)).Append("\n收藏用于系列成就，可出售。\n");
            IReadOnlyList<ProjectCollectionAchievementDefinition> achievements = ProjectRewardConfig.CollectionAchievements(out string error);
            if (error != null) return text.Append("收藏成就配置不可用。").ToString();
            foreach (ProjectCollectionAchievementDefinition achievement in achievements)
            {
                if (!achievement.RequiredCollectionIds.Contains(item.CollectionId)) continue;
                text.Append("\n关联成就：").Append(achievement.Name).Append("\n收集条件：");
                for (int index = 0; index < achievement.RequiredCollectionIds.Count; index++)
                {
                    if (index > 0) text.Append("、");
                    text.Append(ProjectRewardConfig.Collection(achievement.RequiredCollectionIds[index]).Name);
                }
                foreach (ProjectAttributeValue bonus in achievement.AttributeRewards)
                    text.Append("\n奖励：").Append(ProjectEquipmentText.ItemAttribute(bonus.Attribute, bonus.Value));
                text.AppendLine();
            }
            return text.ToString().TrimEnd();
        }

        /// <summary>
        /// 向只读卡片添加按换行及宽度自动计算高度的文本。
        /// </summary>
        /// <param name="parent">当前物品卡片。</param>
        /// <param name="value">详情文本。</param>
        /// <param name="color">文字颜色。</param>
        private static void AddDetail(Transform parent, string value, Color color)
        {
            UnityEngine.UI.Text label = ProjectUiFactory.Label(parent, value, 23, TextAnchor.UpperLeft, color);
            label.supportRichText = true;
            label.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().minHeight = 42f;
        }
    }
}
