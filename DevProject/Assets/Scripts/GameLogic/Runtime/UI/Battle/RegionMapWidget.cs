using System;
using System.Collections.Generic;
using GameLogic.Gameplay.Combat;
using GameLogic.Gameplay.Expedition;
using GameLogic.Gameplay.Level;
using GameLogic.UI.Common;
using UnityEngine;

namespace GameLogic.UI.Battle
{
    /// <summary>
    /// 以固定北向底图绘制整区或局部标记，数据来自区域只读投影。
    /// </summary>
    public sealed class RegionMapWidget : MonoBehaviour
    {
        private ExpeditionRun run;
        private bool local;
        private Action<string> select;
        private UnityEngine.UI.RawImage background;
        private RectTransform frame;
        private RegionMapRouteGraphic routeGraphic;
        private readonly List<Vector3> previewRoute = new();
        private Vector3 previewDestination;
        private bool hasPreview;
        private bool validPreview;
        private readonly Dictionary<string, UnityEngine.UI.Text> markers = new();
        private readonly HashSet<string> visible = new(StringComparer.Ordinal);
        private readonly List<string> stale = new();
        private float zoom = 1f;
        private Vector2 center = Vector2.one * 0.5f;
        private string selectedId;
        public RectTransform Frame => frame;

        /// <summary>
        /// 创建原生底图和可复用标记容器。
        /// </summary>
        /// <param name="run">当前单局。</param>
        /// <param name="local">是否绘制玩家周边局部图。</param>
        /// <param name="select">整图目标选择回调。</param>
        public void Configure(ExpeditionRun run, bool local, Action<string> select = null)
        {
            this.run = run;
            this.local = local;
            this.select = select;
            frame = ProjectUiFactory.Panel(transform, "MapFrame", new Color(0.03f, 0.06f, 0.08f, 1f));
            ProjectUiFactory.Stretch(frame, 0f);
            frame.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
            var image = new GameObject("Geometry", typeof(RectTransform), typeof(UnityEngine.UI.RawImage));
            image.transform.SetParent(frame, false);
            background = image.GetComponent<UnityEngine.UI.RawImage>();
            background.raycastTarget = false;
            ProjectUiFactory.Stretch((RectTransform)image.transform, 0f);
            var route = new GameObject("NavigationRoute", typeof(RectTransform), typeof(RegionMapRouteGraphic));
            route.transform.SetParent(frame, false);
            ProjectUiFactory.Stretch((RectTransform)route.transform, 0f);
            routeGraphic = route.GetComponent<RegionMapRouteGraphic>();
            routeGraphic.raycastTarget = false;
            if (!local)
            {
                var aspect = frame.gameObject.AddComponent<UnityEngine.UI.AspectRatioFitter>();
                aspect.aspectMode = UnityEngine.UI.AspectRatioFitter.AspectMode.FitInParent;
                aspect.aspectRatio = run.CurrentMap.Bounds.width / run.CurrentMap.Bounds.height;
            }
            Refresh();
        }

        /// <summary>
        /// 更新整图缩放与平移视口，保持底图和图标使用同一投影。
        /// </summary>
        /// <param name="zoom">大于等于一的缩放倍率。</param>
        /// <param name="center">全图归一化的视口中心。</param>
        public void SetViewport(float zoom, Vector2 center)
        {
            this.zoom = Mathf.Clamp(zoom, 1f, 5f);
            float half = 0.5f / this.zoom;
            this.center = new Vector2(Mathf.Clamp(center.x, half, 1f - half), Mathf.Clamp(center.y, half, 1f - half));
            Refresh();
        }

        /// <summary>
        /// 强调当前正在查看详情的稳定目标身份。
        /// </summary>
        /// <param name="id">目标身份，空值取消选择。</param>
        public void Select(string id) { selectedId = id; Refresh(); }

        /// <summary>
        /// 显示本次选点的路线或不可达终点。
        /// </summary>
        /// <param name="route">已经规划的路线，失败时为空列表。</param>
        /// <param name="destination">选点终点或路线实际终点。</param>
        /// <param name="valid">路线是否可达。</param>
        public void SetNavigationPreview(IReadOnlyList<Vector3> route, Vector3 destination, bool valid)
        {
            previewRoute.Clear();
            for (int i = 0; i < route.Count; i++) previewRoute.Add(route[i]);
            previewDestination = destination;
            hasPreview = true;
            validPreview = valid;
            Refresh();
        }

        /// <summary>
        /// 清除本次选点预览，保留运行中的导航路线。
        /// </summary>
        public void ClearNavigationPreview()
        {
            previewRoute.Clear();
            hasPreview = false;
            Refresh();
        }

        /// <summary>
        /// 将屏幕点击还原为当前底图视口中的世界坐标。
        /// </summary>
        /// <param name="screenPosition">点击的屏幕坐标。</param>
        /// <param name="camera">事件系统使用的画布摄像机，覆盖画布时为空。</param>
        /// <param name="world">成功时的世界位置。</param>
        /// <returns>点击处于实际地图框和地图边界内时为真。</returns>
        public bool TryScreenToWorld(Vector2 screenPosition, Camera camera, out Vector3 world)
        {
            world = default;
            if (!TryScreenToViewport(frame, screenPosition, camera, out Vector2 position)) return false;
            return RegionMapProjection.TryUnprojectViewport(run.CurrentMap, background.uvRect, position,
                run.CurrentRegion.Plan.EntryPosition.y, out world);
        }

        /// <summary>
        /// 将屏幕点击转换到实际地图框，拒绝宽高适配后的留白点击。
        /// </summary>
        /// <param name="frame">实际显示地图的矩形。</param>
        /// <param name="screenPosition">点击的屏幕坐标。</param>
        /// <param name="camera">事件系统使用的画布摄像机，覆盖画布时为空。</param>
        /// <param name="position">成功时的归一化视口坐标。</param>
        /// <returns>点击位于地图矩形闭合边界内时为真。</returns>
        public static bool TryScreenToViewport(RectTransform frame, Vector2 screenPosition, Camera camera, out Vector2 position)
        {
            position = default;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(frame, screenPosition, camera, out Vector2 localPosition)) return false;
            Rect rect = frame.rect;
            if (rect.width <= 0f || rect.height <= 0f || localPosition.x < rect.xMin || localPosition.x > rect.xMax ||
                localPosition.y < rect.yMin || localPosition.y > rect.yMax) return false;
            position = new Vector2((localPosition.x - rect.xMin) / rect.width, (localPosition.y - rect.yMin) / rect.height);
            return true;
        }

        /// <summary>
        /// 按实际渲染帧更新活体位置和有效倒计时，不推进玩法。
        /// </summary>
        private void LateUpdate() => Refresh();

        /// <summary>
        /// 将底图裁切与所有目标投影到当前视口。
        /// </summary>
        public void Refresh()
        {
            if (run == null || run.CurrentMap == null || run.CurrentRegion == null || background == null) return;
            if (run.IsSavingRun) return;
            RegionMapProjection projection = run.GetMapProjection();
            if (projection == null) return;
            RegionMapDefinition map = run.CurrentMap;
            background.texture = run.MapTexture;
            Rect uv;
            Vector3 playerPosition = run.CurrentRegion.Plan.EntryPosition;
            foreach (RegionMapMarker marker in projection.Markers)
                if (marker.Kind == RegionMapMarkerKind.Player) { playerPosition = marker.Position; break; }
            float radius = Mathf.Min(map.Bounds.width, map.Bounds.height) * 0.2f;
            if (local)
            {
                Vector2 playerUv = RegionMapProjection.ProjectWorld(map, playerPosition).NormalizedPosition;
                var half = new Vector2(radius / map.Bounds.width, radius / map.Bounds.height);
                uv = new Rect(playerUv - half, half * 2f);
            }
            else uv = new Rect(center - Vector2.one * (0.5f / zoom), Vector2.one / zoom);
            background.uvRect = uv;
            IReadOnlyList<Vector3> navigationRoute = run.NavigationRoute;
            Vector3 navigationEnd = navigationRoute.Count > 0 ? navigationRoute[navigationRoute.Count - 1] : run.NavigationDestination;
            routeGraphic.SetRoutes(map, uv, navigationRoute, navigationEnd, run.IsNavigating,
                previewRoute, previewDestination, hasPreview, validPreview, local ? 3f : 5f);
            visible.Clear();
            foreach (RegionMapMarker marker in projection.Markers)
            {
                Vector2 point = RegionMapProjection.ProjectWorld(map, marker.Position).NormalizedPosition;
                point = new Vector2((point.x - uv.xMin) / uv.width, (point.y - uv.yMin) / uv.height);
                bool inside = point.x >= 0f && point.x <= 1f && point.y >= 0f && point.y <= 1f;
                bool showDirection = marker.IsTracked || local && marker.Kind == RegionMapMarkerKind.Boss && marker.Status != RegionMapMarkerStatus.Complete;
                if (!inside && !showDirection) continue;
                bool edge = !inside;
                Vector2 direction = point - Vector2.one * 0.5f;
                if (edge)
                {
                    point = Vector2.one * 0.5f + direction * (0.45f / Mathf.Max(Mathf.Abs(direction.x), Mathf.Abs(direction.y)));
                }
                if (!markers.TryGetValue(marker.Id, out UnityEngine.UI.Text label))
                {
                    label = ProjectUiFactory.Label(frame, "", local ? 21 : 26, TextAnchor.MiddleCenter, Color.white);
                    label.name = "Marker_" + marker.Id;
                    label.rectTransform.sizeDelta = new Vector2(local ? 32 : 54, local ? 32 : 54);
                    if (!local && marker.Kind != RegionMapMarkerKind.Player)
                    {
                        label.raycastTarget = true;
                        var markerButton = label.gameObject.AddComponent<RegionMapMarkerButton>();
                        markerButton.targetGraphic = label;
                        string id = marker.Id;
                        markerButton.onClick.AddListener(() => select?.Invoke(id));
                    }
                    markers.Add(marker.Id, label);
                }
                visible.Add(marker.Id);
                label.gameObject.SetActive(true);
                label.rectTransform.anchorMin = point;
                label.rectTransform.anchorMax = point;
                label.rectTransform.anchoredPosition = Vector2.zero;
                label.rectTransform.localRotation = Quaternion.identity;
                label.text = edge ? "▲" : Caption(marker);
                bool countdown = marker.Kind == RegionMapMarkerKind.Elite && marker.Status == RegionMapMarkerStatus.Countdown;
                label.fontSize = countdown ? local ? 13 : 18 : local ? 21 : 26;
                label.rectTransform.sizeDelta = new Vector2(local ? 48f : 64f, local ? 44f : 64f);
                label.color = marker.Id == selectedId ? Color.white : MarkerColor(marker);
                if (edge)
                {
                    label.rectTransform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f);
                }
                else if (marker.Kind == RegionMapMarkerKind.Player)
                    label.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -RegionMapProjection.HeadingDegrees(marker.Forward, map.NorthYawDegrees));
                if (!local && label.TryGetComponent(out UnityEngine.UI.Button button)) button.interactable = !run.IsSavingRun;
            }
            stale.Clear();
            foreach (var pair in markers) if (!visible.Contains(pair.Key)) stale.Add(pair.Key);
            foreach (string id in stale)
            {
                markers[id].gameObject.SetActive(false);
                Destroy(markers[id].gameObject);
                markers.Remove(id);
            }
        }

        /// <summary>
        /// 生成目标状态的可读说明。
        /// </summary>
        /// <param name="marker">只读目标快照。</param>
        /// <returns>目标类型、状态与有效时间说明。</returns>
        public static string Describe(RegionMapMarker marker)
        {
            string type = marker.Kind == RegionMapMarkerKind.Elite ? "精英" : marker.Kind == RegionMapMarkerKind.Boss ? "区域尾王" :
                marker.Kind == RegionMapMarkerKind.Chest ? "宝箱" : marker.Kind == RegionMapMarkerKind.Npc ? "商人" : "区域出口";
            string state = marker.Status == RegionMapMarkerStatus.Alive ? "存活" : marker.Status == RegionMapMarkerStatus.Complete ? "已完成" :
                marker.Status == RegionMapMarkerStatus.Unopened ? "未开启" : marker.Status == RegionMapMarkerStatus.Opened ? "已开启" :
                marker.Status == RegionMapMarkerStatus.Visited ? "已访问" : marker.Status == RegionMapMarkerStatus.SoldOut ? "已售罄" :
                marker.Status == RegionMapMarkerStatus.Locked ? "领奖后解锁" : marker.Status == RegionMapMarkerStatus.Warning ? "复活预警" :
                marker.Status == RegionMapMarkerStatus.Waiting ? WaitReason(marker.WaitingReason) :
                marker.Status == RegionMapMarkerStatus.Countdown ? "复活倒计时" : "可交互";
            string time = marker.Status == RegionMapMarkerStatus.Countdown || marker.Status == RegionMapMarkerStatus.Warning ?
                " · " + Mathf.CeilToInt(marker.RemainingTicks * CombatWorld.TickSeconds) + "秒" : string.Empty;
            string stock = marker.Kind == RegionMapMarkerKind.Npc || marker.Kind == RegionMapMarkerKind.Chest ? " · 剩余 " + marker.RemainingItems : string.Empty;
            return type + " · " + state + time + stock + (marker.IsTracked ? " · 正在追踪" : string.Empty);
        }

        /// <summary>
        /// 将等待原因转换为目标详情。
        /// </summary>
        /// <param name="reason">复活安全等待原因。</param>
        /// <returns>可读原因。</returns>
        private static string WaitReason(RegionSpawnWaitReason reason) => reason == RegionSpawnWaitReason.CameraVisible ? "等待离开镜头" :
            reason == RegionSpawnWaitReason.UnsafeDistance ? "等待玩家远离" : reason == RegionSpawnWaitReason.Occupied ? "等待出生点空闲" : "等待复活条件";

        /// <summary>
        /// 为目标选择类型图形和状态符号。
        /// </summary>
        /// <param name="marker">目标快照。</param>
        /// <returns>北向图标文字。</returns>
        private static string Caption(RegionMapMarker marker) => marker.Kind == RegionMapMarkerKind.Player ? "▲" :
            marker.Kind == RegionMapMarkerKind.Elite ? marker.Status == RegionMapMarkerStatus.Alive ? "◆" :
                marker.Status == RegionMapMarkerStatus.Countdown ? "◷\n" + Mathf.CeilToInt(marker.RemainingTicks * CombatWorld.TickSeconds) : "◷" :
            marker.Kind == RegionMapMarkerKind.Boss ? marker.Status == RegionMapMarkerStatus.Complete ? "✓" : "王" :
            marker.Kind == RegionMapMarkerKind.Chest ? marker.Status == RegionMapMarkerStatus.Complete ? "✓" : "箱" :
            marker.Kind == RegionMapMarkerKind.Npc ? "商" : marker.Status == RegionMapMarkerStatus.Locked ? "锁" : "出";

        /// <summary>
        /// 根据类型和完成状态区分地图标记。
        /// </summary>
        /// <param name="marker">目标快照。</param>
        /// <returns>可读图标颜色。</returns>
        private static Color MarkerColor(RegionMapMarker marker) => marker.IsTracked ? new Color(0.3f, 1f, 1f) :
            marker.Status == RegionMapMarkerStatus.Complete || marker.Status == RegionMapMarkerStatus.SoldOut ? new Color(0.65f, 0.7f, 0.7f) :
            marker.Kind == RegionMapMarkerKind.Boss ? new Color(1f, 0.35f, 0.3f) :
            marker.Kind == RegionMapMarkerKind.Elite ? new Color(1f, 0.65f, 0.25f) :
            marker.Kind == RegionMapMarkerKind.Chest ? new Color(1f, 0.9f, 0.35f) :
            marker.Kind == RegionMapMarkerKind.Exit ? new Color(0.4f, 1f, 0.5f) : Color.white;
    }
}
