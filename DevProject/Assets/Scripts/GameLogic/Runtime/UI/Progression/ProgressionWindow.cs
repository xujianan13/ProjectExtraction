using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using GameFramework;
using GameLogic.Gameplay.Combat;
using GameLogic.Gameplay.Expedition;
using GameLogic.Gameplay.Level;
using GameLogic.Project;
using GameLogic.Tables;
using GameLogic.UI.Common;
using UnityEngine;

namespace GameLogic.UI.Progression
{
    /// <summary>
    /// 展示永久成长、技能点提升与线性房间进度。
    /// </summary>
    public sealed class ProgressionWindow : GameWindow
    {
        private ExpeditionRun run;
        private RectTransform entries;
        private UnityEngine.UI.Text summary;
        private UnityEngine.UI.Text feedback;
        private readonly List<CombatSkillSnapshot> skills = new();
        private bool upgrading;

        /// <summary>
        /// 创建安全区内可滚动的成长与房间列表。
        /// </summary>
        protected override void Init()
        {
            RectTransform safe = ProjectUiFactory.Fill(RootTransform, "ProgressionSafeArea", Color.clear);
            RectTransform panel = ProjectUiFactory.Panel(safe, "ProgressionPanel", new Color(0.055f, 0.11f, 0.16f, 0.98f));
            panel.anchorMin = new Vector2(0.05f, 0.06f);
            panel.anchorMax = new Vector2(0.95f, 0.94f);
            panel.offsetMin = panel.offsetMax = Vector2.zero;
            RectTransform layout = ProjectUiFactory.Vertical(panel, "Layout", 10f);
            ProjectUiFactory.Stretch(layout, 16f);
            RectTransform header = ProjectUiFactory.Horizontal(layout, "Header", 8f);
            ProjectUiFactory.Height(header, 68f);
            UnityEngine.UI.Text title = ProjectUiFactory.Label(header, "成长与房间进度", 32, TextAnchor.MiddleLeft, Color.white);
            title.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().flexibleWidth = 1f;
            UnityEngine.UI.Button close = ProjectUiFactory.ActionButton(header, "关闭", () => CloseSelfAsync().Forget());
            close.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().preferredWidth = 110f;
            summary = ProjectUiFactory.Label(layout, string.Empty, 25, TextAnchor.MiddleLeft, new Color(0.82f, 0.91f, 0.95f));
            ProjectUiFactory.Height(summary.rectTransform, 90f);
            UnityEngine.UI.ScrollRect scroll = ProjectUiFactory.Scroll(layout, "ProgressionEntries", out entries);
            UnityEngine.UI.LayoutElement size = scroll.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            size.flexibleHeight = 1f;
            size.minHeight = 100f;
            feedback = ProjectUiFactory.Label(layout, string.Empty, 22, TextAnchor.UpperLeft, new Color(1f, 0.85f, 0.58f));
            ProjectUiFactory.Height(feedback.rectTransform, 60f);
        }

        /// <summary>
        /// 绑定当前单局，并在关闭时释放成长面板暂停。
        /// </summary>
        /// <param name="args">包含当前单局的窗口参数。</param>
        protected override void Open(GameWindowOpenArgs args)
        {
            run = args.UserData as ExpeditionRun;
            upgrading = false;
            feedback.text = string.Empty;
            if (run == null) return;
            ExpeditionRun current = run;
            current.Changed += Refresh;
            OpenScope.Defer(() =>
            {
                current.Changed -= Refresh;
                current.ClosePanel(ExpeditionPanelKind.Progression);
                if (ReferenceEquals(run, current)) run = null;
            });
        }

        /// <summary>
        /// 首次显示时读取永久成长和房间快照。
        /// </summary>
        /// <param name="args">窗口显示参数。</param>
        protected override void Show(GameWindowOpenArgs args) => Refresh();

        /// <summary>
        /// 从权威成长与房间计划刷新显示，不创建随机强化候选。
        /// </summary>
        private void Refresh()
        {
            if (run == null) return;
            if (!run.IsRunning || run.ActivePanel != ExpeditionPanelKind.Progression)
            {
                if (State == GameWindowState.Opened) CloseSelfAsync().Forget();
                return;
            }
            summary.text = "永久等级 " + run.PlayerLevel + " · 经验 " + run.PlayerExperience + " / " + run.ExperienceToNextLevel +
                "\n可用技能点 " + run.SkillPoints + " · 房间 " + (run.CurrentRoomIndex + 1) + " / " + run.RoomCount;
            ProjectUiFactory.Clear(entries);
            AddEntry("永久技能 · 升级后立即生效，结束本局仍保留");
            foreach (ProjectSkill skill in run.PermanentSkills)
            {
                string id = skill.Id;
                string description = skill.Name + "  " + skill.Rank + " / " + skill.MaxRank + "\n当前效果：" + skill.CurrentEffect;
                if (skill.Rank < skill.MaxRank) description += "\n下一效果：" + skill.NextEffect + " · 消耗 " + skill.NextCost + " 技能点";
                AddEntry(description);
                UnityEngine.UI.Button upgrade = ProjectUiFactory.ActionButton(entries, "提升 " + skill.Name, () => UpgradeAsync(id).Forget());
                ProjectUiFactory.Height((RectTransform)upgrade.transform, 68f);
                upgrade.interactable = !upgrading && run.CanUpgradePermanentSkills && skill.Rank < skill.MaxRank && run.SkillPoints >= skill.NextCost;
            }
            AddEntry("战斗技能 · 按当前顺序自动施放");
            run.World.GetSkillsSnapshot(run.Player, skills);
            foreach (CombatSkillSnapshot skill in skills)
                AddEntry(CombatSkillText.Summary(skill) + "\n" + CombatSkillText.Categories(skill) + "\n" +
                    GameTables.CfgCombatSkills.GetById(skill.SkillId).Description);
            AddEntry("关卡房间");
            foreach (var room in run.GetRoomOverview())
            {
                string state = room.IsCurrent ? "当前" : room.IsPassed ? "已通过" : "尚未进入";
                string description = "房间 " + (room.RoomIndex + 1) + " · " + RoomName(room.Kind) + " · " + state + "\n" + room.Name;
                if (room.IsFinal) description += "\n最终 Boss：" + room.BossName + "\n弱点与应对：" + room.Weakness +
                    "\n收藏预览：" + string.Join("、", room.CollectionPreviews);
                AddEntry(description);
            }
            if (upgrading) feedback.text = "正在保存技能提升。";
            else if (run.IsSavingRun) feedback.text = "正在保存进度，请稍候。";
        }

        /// <summary>
        /// 使用已保存技能点升级永久技能，并保留本轮异步请求的窗口归属。
        /// </summary>
        /// <param name="id">永久技能标识。</param>
        /// <returns>技能提升任务。</returns>
        private async UniTaskVoid UpgradeAsync(string id)
        {
            if (run == null || upgrading || !run.CanUpgradePermanentSkills) return;
            ExpeditionRun current = run;
            LifetimeScope scope = OpenScope;
            upgrading = true;
            Refresh();
            ProjectOperationResult result = await current.UpgradePermanentSkillAsync(id);
            if (!scope.IsActive || !ReferenceEquals(OpenScope, scope) || !ReferenceEquals(run, current)) return;
            upgrading = false;
            feedback.text = result.Message;
            Refresh();
        }

        /// <summary>
        /// 添加由布局按可用宽度换行的说明。
        /// </summary>
        /// <param name="text">玩家可读的成长或房间说明。</param>
        private void AddEntry(string text)
        {
            UnityEngine.UI.Text label = ProjectUiFactory.Label(entries, text, 26, TextAnchor.UpperLeft, Color.white);
            label.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().minHeight = 44f;
        }

        /// <summary>
        /// 将房间类型转换为玩家可读名称。
        /// </summary>
        /// <param name="kind">房间类型。</param>
        /// <returns>房间中文名称。</returns>
        private static string RoomName(ExpeditionRoomKind kind) => kind == ExpeditionRoomKind.Combat ? "怪物房" :
            kind == ExpeditionRoomKind.Npc ? "商人房" : kind == ExpeditionRoomKind.Chest ? "宝箱房" : "Boss 房";
    }
}
