using System.Collections.Generic;
using System.Text;
using Cysharp.Threading.Tasks;
using GameFramework;
using GameLogic.Project;
using GameLogic.UI.Common;
using UnityEngine;

namespace GameLogic.UI.Character
{
    /// <summary>
    /// 展示收藏图鉴、系列进度与已经持久化的成就属性来源。
    /// </summary>
    public sealed class CollectionWindow : GameWindow
    {
        private ProjectSession session;
        private RectTransform content;

        /// <summary>
        /// 创建适配安全区的可滚动图鉴与成就面板。
        /// </summary>
        protected override void Init()
        {
            RectTransform surface = ProjectUiFactory.Fill(RootTransform, "Collections", new Color(0.035f, 0.07f, 0.11f, 0.98f));
            RectTransform layout = ProjectUiFactory.Vertical(surface, "Layout", 12f);
            ProjectUiFactory.Stretch(layout, 24f);
            RectTransform header = ProjectUiFactory.Horizontal(layout, "Header", 12f);
            ProjectUiFactory.Height(header, 72f);
            var title = ProjectUiFactory.Label(header, "收藏与成就", 32, TextAnchor.MiddleLeft, Color.white);
            title.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().flexibleWidth = 1f;
            var close = ProjectUiFactory.ActionButton(header, "关闭", () => CloseSelfAsync().Forget());
            close.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().preferredWidth = 110f;
            ProjectUiFactory.Label(layout, "成功结束并将收藏带回仓库后登记。重复收藏可出售，已完成成就持续提供属性。", 22,
                TextAnchor.MiddleLeft, new Color(0.8f, 0.9f, 0.96f));
            var scroll = ProjectUiFactory.Scroll(layout, "Entries", out content);
            scroll.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().flexibleHeight = 1f;
        }

        /// <summary>
        /// 绑定唯一项目会话并监听已经保存的收藏变化。
        /// </summary>
        /// <param name="args">窗口打开参数。</param>
        protected override void Open(GameWindowOpenArgs args)
        {
            session = ProjectServices.Session;
            session.Changed += Refresh;
            OpenScope.Defer(() => session.Changed -= Refresh);
            Refresh();
        }

        /// <summary>
        /// 重建收藏状态、来源说明和成就条件，不修改玩家状态。
        /// </summary>
        private void Refresh()
        {
            ProjectUiFactory.Clear(content);
            var collected = new HashSet<string>(session.CollectedCollectionIds);
            var achieved = new HashSet<string>(session.AchievedCollectionIds);
            foreach (ProjectCollectionDefinition entry in ProjectRewardConfig.CollectionDefinitions())
            {
                RectTransform card = CreateCard("Collection_" + entry.Id);
                ProjectUiFactory.Label(card, (collected.Contains(entry.Id) ? "已收藏 · " : "未收藏 · ") + entry.Name, 27,
                    TextAnchor.MiddleLeft, ProjectEquipmentRules.QualityColor(entry.Quality));
                ProjectUiFactory.Label(card, $"来源：{entry.Source}\n重复售价：{entry.SellGold} 金币", 22,
                    TextAnchor.MiddleLeft, Color.white);
                ProjectUiFactory.Label(card, "仅在成功带回并实际入库后登记；死亡或放弃不会登记本局收藏。", 20,
                    TextAnchor.MiddleLeft, new Color(0.8f, 0.9f, 0.96f));
            }
            foreach (ProjectCollectionAchievementDefinition entry in ProjectRewardConfig.CollectionAchievements(out _))
            {
                RectTransform card = CreateCard("Achievement_" + entry.Id);
                int count = 0;
                var detail = new StringBuilder();
                foreach (string id in entry.RequiredCollectionIds)
                {
                    bool found = collected.Contains(id);
                    if (found) count++;
                    detail.Append(found ? "✓ " : "○ ").Append(ProjectRewardConfig.Collection(id).Name).Append('\n');
                }
                foreach (ProjectAttributeValue bonus in entry.AttributeRewards)
                    detail.Append("永久奖励：").Append(ProjectEquipmentText.FinalAttribute(bonus.Attribute, bonus.Value)).Append('\n');
                ProjectUiFactory.Label(card, $"{entry.Name} · {count}/{entry.RequiredCollectionIds.Count}" +
                    (achieved.Contains(entry.Id) ? " · 已生效" : " · 未完成"), 27, TextAnchor.MiddleLeft, new Color(1f, 0.8f, 0.4f));
                ProjectUiFactory.Label(card, detail.ToString().TrimEnd(), 22, TextAnchor.MiddleLeft, Color.white);
            }
        }

        /// <summary>
        /// 创建由内容决定高度的图鉴条目容器。
        /// </summary>
        /// <param name="name">条目的稳定对象名称。</param>
        /// <returns>用于放置文本的布局节点。</returns>
        private RectTransform CreateCard(string name)
        {
            RectTransform card = ProjectUiFactory.Vertical(content, name, 8f);
            card.GetComponent<UnityEngine.UI.Image>().color = new Color(0.09f, 0.15f, 0.2f, 1f);
            return card;
        }
    }
}
