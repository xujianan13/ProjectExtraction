using Cysharp.Threading.Tasks;
using GameFramework;
using GameLogic.Gameplay.Expedition;
using GameLogic.Gameplay.Level;
using GameLogic.Tables;
using GameLogic.UI.Common;
using UnityEngine;

namespace GameLogic.UI.Battle
{
    /// <summary>
    /// 模态区域大地图，提供目标状态、平移缩放与稳定身份追踪。
    /// </summary>
    public sealed class ExpeditionMapWindow : GameWindow
    {
        private ExpeditionRun run;
        private RectTransform viewport;
        private RegionMapWidget map;
        private RegionMapGesture gesture;
        private UnityEngine.UI.Text title;
        private UnityEngine.UI.Text detail;
        private UnityEngine.UI.Button track;
        private UnityEngine.UI.Button clear;
        private UnityEngine.UI.Button close;
        private UnityEngine.UI.Button plus;
        private UnityEngine.UI.Button minus;
        private UnityEngine.UI.Button reset;
        private UnityEngine.UI.Button focus;
        private string selectedId;

        /// <summary>
        /// 在安全区内创建地图、缩放工具和目标详情。
        /// </summary>
        protected override void Init()
        {
            RectTransform safe = ProjectUiFactory.Fill(RootTransform, "MapSafeArea", Color.clear);
            RectTransform panel = ProjectUiFactory.Panel(safe, "MapPanel", new Color(0.04f, 0.08f, 0.11f, 0.98f));
            panel.anchorMin = new Vector2(0.04f, 0.04f);
            panel.anchorMax = new Vector2(0.96f, 0.96f);
            panel.offsetMin = panel.offsetMax = Vector2.zero;
            RectTransform layout = ProjectUiFactory.Vertical(panel, "MapLayout", 10f);
            ProjectUiFactory.Stretch(layout, 18f);
            title = ProjectUiFactory.Label(layout, "区域地图 · 北向固定", 30, TextAnchor.MiddleCenter, Color.white);
            ProjectUiFactory.Height(title.rectTransform, 54f);
            viewport = ProjectUiFactory.Panel(layout, "MapViewport", Color.clear);
            var viewportSize = viewport.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            viewportSize.flexibleHeight = 1f;
            viewportSize.minHeight = 120f;
            RectTransform tools = ProjectUiFactory.Horizontal(layout, "ViewTools", 8f);
            ProjectUiFactory.Height(tools, 56f);
            minus = ProjectUiFactory.ActionButton(tools, "缩小", () => gesture.ZoomBy(1f / 1.4f));
            plus = ProjectUiFactory.ActionButton(tools, "放大", () => gesture.ZoomBy(1.4f));
            reset = ProjectUiFactory.ActionButton(tools, "全图", () => gesture.ResetView());
            focus = ProjectUiFactory.ActionButton(tools, "定位玩家", FocusPlayer);
            detail = ProjectUiFactory.Label(layout, "点击目标查看详情。拖动 / 双指 / 滚轮调整地图。", 22, TextAnchor.MiddleLeft, Color.white);
            ProjectUiFactory.Height(detail.rectTransform, 116f);
            RectTransform targets = ProjectUiFactory.Horizontal(layout, "TargetActions", 8f);
            ProjectUiFactory.Height(targets, 60f);
            track = ProjectUiFactory.ActionButton(targets, "追踪目标", Track);
            clear = ProjectUiFactory.ActionButton(targets, "取消追踪", ClearTracking);
            close = ProjectUiFactory.ActionButton(layout, "返回探索", () => CloseSelfAsync().Forget());
            ProjectUiFactory.Height((RectTransform)close.transform, 64f);
        }

        /// <summary>
        /// 绑定当前区域，关闭时只释放地图自身暂停。
        /// </summary>
        /// <param name="args">包含单局的窗口参数。</param>
        protected override void Open(GameWindowOpenArgs args)
        {
            run = args.UserData as ExpeditionRun;
            if (run == null) return;
            ProjectUiFactory.Clear(viewport);
            map = viewport.gameObject.AddComponent<RegionMapWidget>();
            map.Configure(run, false, SelectTarget);
            gesture = map.Frame.gameObject.AddComponent<RegionMapGesture>();
            gesture.Configure(map, () => run != null && !run.IsSavingRun);
            selectedId = run.CurrentRegion.TrackingTargetId;
            run.Changed += Refresh;
            ExpeditionRun current = run;
            OpenScope.Defer(() =>
            {
                current.Changed -= Refresh;
                current.ClosePanel(ExpeditionPanelKind.Map);
                if (map != null) Object.Destroy(map);
                if (ReferenceEquals(run, current)) run = null;
            });
            Refresh();
        }

        /// <summary>
        /// 同步目标详情和保存期间的操作门禁。
        /// </summary>
        private void Refresh()
        {
            if (run == null) return;
            if (!run.IsRunning || run.ActivePanel != ExpeditionPanelKind.Map)
            {
                if (State == GameWindowState.Opened) CloseSelfAsync().Forget();
                return;
            }
            title.text = (run.IsFinalRegion ? "最终场地" : "区域 " + (run.CurrentRegionIndex + 1) + " / " + run.RegionCount) + " · 北向固定";
            RegionMapProjection projection = run.GetMapProjection();
            RegionMapMarker selected = projection?.Markers.Find(value => value.Id == selectedId);
            detail.text = selected == null ? "点击目标查看详情。拖动 / 双指 / 滚轮调整地图。" :
                DescribeTarget(selected, projection.Markers.Find(value => value.Kind == RegionMapMarkerKind.Player).Position);
            if (selected?.Kind == RegionMapMarkerKind.Boss)
            {
                CfgBossesRow boss = GameTables.CfgBosses.GetById(run.CurrentRegion.Plan.Request.BossDefinitionId);
                detail.text += "\n" + boss.WeaknessDescription;
            }
            else if (selected?.Kind == RegionMapMarkerKind.Elite)
            {
                int enemyId = run.CurrentRegion.Plan.SpawnSlots.Find(value => value.SlotId == selected.Id).EnemyId;
                detail.text += "\n" + GameTables.CfgEnemies.GetById(enemyId).WeaknessDescription;
            }
            bool available = !run.IsSavingRun;
            track.interactable = available && selected != null && selected.Kind != RegionMapMarkerKind.Player;
            clear.interactable = available && !string.IsNullOrEmpty(run.CurrentRegion.TrackingTargetId);
            close.interactable = plus.interactable = minus.interactable = reset.interactable = focus.interactable = available;
            map.Select(selectedId);
        }

        /// <summary>
        /// 从当前位置重新定位地图，不移动角色或改变追踪目标。
        /// </summary>
        private void FocusPlayer()
        {
            if (!focus.interactable) return;
            RegionMapMarker player = run.GetMapProjection().Markers.Find(value => value.Kind == RegionMapMarkerKind.Player);
            gesture.FocusOn(RegionMapProjection.ProjectWorld(run.CurrentMap, player.Position).NormalizedPosition);
        }

        /// <summary>
        /// 显示目标名称、状态与明确标注的直线距离。
        /// </summary>
        /// <param name="target">当前选中的地图目标。</param>
        /// <param name="playerPosition">玩家战斗真值位置。</param>
        /// <returns>目标详情及直线距离说明。</returns>
        private string DescribeTarget(RegionMapMarker target, Vector3 playerPosition)
        {
            RegionPlan plan = run.CurrentRegion.Plan;
            string name = target.Kind == RegionMapMarkerKind.Boss ? GameTables.CfgBosses.GetById(plan.Request.BossDefinitionId).Name :
                target.Kind == RegionMapMarkerKind.Elite ? GameTables.CfgEnemies.GetById(plan.SpawnSlots.Find(value => value.SlotId == target.Id).EnemyId).Name :
                target.Kind == RegionMapMarkerKind.Chest ? "区域宝箱 " + (plan.Chests.FindIndex(value => value.Id == target.Id) + 1) :
                target.Kind == RegionMapMarkerKind.Npc ? "区域商人 " + (plan.Npcs.FindIndex(value => value.Id == target.Id) + 1) : "区域出口";
            float distance = Vector2.Distance(new Vector2(playerPosition.x, playerPosition.z), new Vector2(target.Position.x, target.Position.z));
            return name + " · 直线距离 " + distance.ToString("0.0") + " 米\n" + RegionMapWidget.Describe(target);
        }

        /// <summary>
        /// 切换正在查看详情的目标。
        /// </summary>
        /// <param name="id">稳定目标身份。</param>
        private void SelectTarget(string id) { selectedId = id; Refresh(); }

        /// <summary>
        /// 保存所选目标的追踪身份。
        /// </summary>
        private void Track()
        {
            if (track.interactable && run.TryTrackMapTarget(selectedId)) Refresh();
        }

        /// <summary>
        /// 清除已保存的目标追踪。
        /// </summary>
        private void ClearTracking()
        {
            if (clear.interactable && run.TryTrackMapTarget(string.Empty)) Refresh();
        }
    }
}
