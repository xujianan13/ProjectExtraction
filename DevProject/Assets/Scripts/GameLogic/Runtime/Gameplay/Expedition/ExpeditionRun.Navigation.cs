using System.Collections.Generic;
using GameLogic.Gameplay.Combat;
using GameLogic.Gameplay.Level;
using GameLogic.Project;
using UnityEngine;

namespace GameLogic.Gameplay.Expedition
{
    public sealed partial class ExpeditionRun
    {
        private readonly List<Vector3> navigationPath = new();
        private readonly List<Vector3> navigationDisplayRoute = new();
        private int navigationWaypoint;
        private int navigationRoomId;
        private float navigationApproachDistance;
        private bool navigationWasDisplaced;
        private float navigationBestRemaining;
        private float navigationStuckSeconds;

        /// <summary>
        /// 是否持有本次运行的导航指令，模态面板和后台暂停期间仍保留指令。
        /// </summary>
        public bool IsNavigating { get; private set; }

        /// <summary>
        /// 选点时的目的地，标记移动后不转为自动追击。
        /// </summary>
        public Vector3 NavigationDestination { get; private set; }

        /// <summary>
        /// 当前玩家位置和剩余路点组成的只读显示路线，取消后为空。
        /// </summary>
        public IReadOnlyList<Vector3> NavigationRoute => navigationDisplayRoute;

        /// <summary>
        /// 在地图面板或正常探索中预览路线，不改变移动、追踪或保存状态。
        /// </summary>
        /// <param name="destination">玩家选中的地图世界坐标。</param>
        /// <param name="approachTarget">是否在标记附近停下，避免走入交互物或怪物中心。</param>
        /// <param name="route">接收包含玩家起点和停步位置的路线。</param>
        /// <param name="error">不能前往时的可读原因。</param>
        /// <returns>路线已生成时为真。</returns>
        public bool TryPreviewNavigation(Vector3 destination, bool approachTarget, List<Vector3> route, out string error)
        {
            route.Clear();
            error = null;
            if (!CanManagePanel || ActivePanel != ExpeditionPanelKind.None && ActivePanel != ExpeditionPanelKind.Map)
            { error = "当前无法规划路线，请返回探索后重试。"; return false; }
            if (!World.TryFindNavigationPath(Player, destination, (float)ProjectContentConfig.Navigation.CornerClearance, route))
            { error = "该位置在可行走范围外、被障碍阻挡或无法到达，请重新选点。"; return false; }
            if (approachTarget) TrimNavigationApproach(route, GetNavigationApproachDistance(destination));
            return true;
        }

        /// <summary>
        /// 接纳新的目的地并替换上一条路线，地图关闭后由战斗逻辑帧开始移动。
        /// </summary>
        /// <param name="destination">本次选点的世界坐标。</param>
        /// <param name="approachTarget">是否在标记附近停下。</param>
        /// <param name="error">未能开始时的可读原因。</param>
        /// <returns>新导航指令已接纳时为真。</returns>
        public bool TryStartNavigation(Vector3 destination, bool approachTarget, out string error)
        {
            var route = new List<Vector3>();
            if (!TryPreviewNavigation(destination, approachTarget, route, out error)) return false;
            ClearPlayerInput();
            navigationPath.Clear();
            navigationPath.AddRange(route);
            navigationWaypoint = 1;
            navigationRoomId = World.ActiveRoomId;
            navigationApproachDistance = approachTarget ? GetNavigationApproachDistance(destination) : 0f;
            navigationWasDisplaced = false;
            NavigationDestination = destination;
            IsNavigating = true;
            ResetNavigationProgress(route[0]);
            RefreshNavigationRoute(route[0]);
            Message = "正在自动前往，操作摇杆或方向键可停止。";
            if (Vector3.Distance(route[0], route[route.Count - 1]) <= (float)ProjectContentConfig.Navigation.ArrivalDistance)
                EndNavigation("已在目的地附近。");
            Changed?.Invoke();
            return true;
        }

        /// <summary>
        /// 主动停止自动移动，保留现有地图追踪目标。
        /// </summary>
        public void CancelNavigation()
        {
            if (!IsNavigating) return;
            EndNavigation("已停止自动前往。");
            Changed?.Invoke();
        }

        /// <summary>
        /// 从终点倒序查找路线离开目标接近圆的位置，在已验证线段上求停点并删除后续路点。
        /// 起点已经位于接近圆内时保留重复的起终点，直接按到达处理。
        /// </summary>
        /// <param name="route">至少含起点和终点的有效路线。</param>
        /// <param name="distance">选点时确定的正数目标接近距离。</param>
        private static void TrimNavigationApproach(List<Vector3> route, float distance)
        {
            Vector3 destination = route[route.Count - 1];
            var center = new Vector2(destination.x, destination.z);
            float radiusSquared = distance * distance;
            Vector3 origin = route[0];
            if ((new Vector2(origin.x, origin.z) - center).sqrMagnitude <= radiusSquared)
            {
                route.Clear();
                route.Add(origin);
                route.Add(origin);
                return;
            }
            for (int index = route.Count - 2; index >= 0; index--)
            {
                Vector3 start = route[index];
                var offset = new Vector2(start.x, start.z) - center;
                if (offset.sqrMagnitude < radiusSquared) continue;
                Vector3 end = route[index + 1];
                var delta = new Vector2(end.x - start.x, end.z - start.z);
                float a = delta.sqrMagnitude;
                float b = Vector2.Dot(offset, delta);
                float c = offset.sqrMagnitude - radiusSquared;
                float entry = Mathf.Clamp01((-b - Mathf.Sqrt(Mathf.Max(0f, b * b - a * c))) / a);
                route[index + 1] = Vector3.Lerp(start, end, entry);
                route.RemoveRange(index + 2, route.Count - index - 2);
                return;
            }
        }

        /// <summary>
        /// 为存活怪物标记保留双方占位间距，其余地图标记使用配置中的交互接近距离。
        /// </summary>
        /// <param name="destination">地图暂停期间选中的目标坐标。</param>
        /// <returns>按选点时目标体型冻结的接近距离。</returns>
        private float GetNavigationApproachDistance(Vector3 destination)
        {
            float distance = (float)ProjectContentConfig.Navigation.TargetApproachDistance;
            foreach (RegionMapMarker marker in GetMapProjection().Markers)
            {
                if (marker.Status != RegionMapMarkerStatus.Alive ||
                    marker.Kind != RegionMapMarkerKind.Boss && marker.Kind != RegionMapMarkerKind.Elite) continue;
                Vector2 offset = new Vector2(marker.Position.x - destination.x, marker.Position.z - destination.z);
                if (offset.sqrMagnitude > 0.00000001f) continue;
                float occupied = (float)(ProjectContentConfig.Player.Radius + enemyDefinitions[marker.CurrentActorId].Row.Radius +
                    ProjectContentConfig.Navigation.CornerClearance);
                distance = Mathf.Max(distance, occupied);
            }
            return distance;
        }

        /// <summary>
        /// 在首个逻辑帧前和每个完整帧后更新移动输入，补帧时也逐个路点转向。
        /// </summary>
        /// <param name="manual">本次渲染帧采样的手动移动输入。</param>
        private void PreparePlayerMovement(Vector2 manual)
        {
            if (manual.sqrMagnitude > 0f && IsNavigating) EndNavigation("已切换为手动移动。");
            if (!IsNavigating) { World.SetMove(Player, manual); return; }
            if (!World.TryGetActor(Player, out CombatActorSnapshot player) || player.Dead || player.RoomId != navigationRoomId ||
                !World.TryGetMovementStatus(Player, out _, out bool displaced))
            { EndNavigation(null); return; }
            if (displaced)
            {
                navigationWasDisplaced = true;
                World.SetMove(Player, Vector2.zero);
                return;
            }
            if (navigationWasDisplaced)
            {
                navigationWasDisplaced = false;
                if (!World.TryFindNavigationPath(Player, NavigationDestination, (float)ProjectContentConfig.Navigation.CornerClearance, navigationPath))
                { EndNavigation("战斗位移后无法继续前往，请重新选点。"); return; }
                if (navigationApproachDistance > 0f) TrimNavigationApproach(navigationPath, navigationApproachDistance);
                navigationWaypoint = 1;
                navigationBestRemaining = RemainingNavigationDistance(player.Position);
            }
            while (navigationWaypoint < navigationPath.Count - 1 &&
                (navigationPath[navigationWaypoint] - player.Position).sqrMagnitude <= 0.00000001f) navigationWaypoint++;
            if (NavigationHasArrived(player.Position)) { EndNavigation("已到达目的地附近。"); return; }
            World.SetNavigationWaypoint(Player, navigationPath[navigationWaypoint]);
            RefreshNavigationRoute(player.Position);
        }

        /// <summary>
        /// 只按实际执行的战斗帧检测前进进度，暂停、零速和击退不消耗堵路等待时间。
        /// </summary>
        private void ObserveNavigationTick()
        {
            if (!IsNavigating) return;
            if (!World.TryGetActor(Player, out CombatActorSnapshot player) || player.Dead || player.RoomId != navigationRoomId ||
                !World.TryGetMovementStatus(Player, out float speed, out bool displaced))
            { EndNavigation(null); return; }
            RefreshNavigationRoute(player.Position);
            float remaining = RemainingNavigationDistance(player.Position);
            float progressThreshold = (float)ProjectContentConfig.Navigation.ProgressDistance;
            if (speed > 0f) progressThreshold = Mathf.Min(progressThreshold, speed * CombatWorld.TickSeconds * 0.5f);
            if (navigationBestRemaining - remaining >= progressThreshold)
            {
                navigationBestRemaining = remaining;
                navigationStuckSeconds = 0f;
                navigationWasDisplaced |= displaced;
                return;
            }
            if (displaced || navigationWasDisplaced || speed <= 0f)
            {
                navigationWasDisplaced |= displaced;
                return;
            }
            if (NavigationHasArrived(player.Position)) { EndNavigation("已到达目的地附近。"); return; }
            navigationStuckSeconds += CombatWorld.TickSeconds;
            if (navigationStuckSeconds >= (float)ProjectContentConfig.Navigation.StuckSeconds)
                EndNavigation("前方被阻挡，已停止自动前往，请手动移动或重新选点。");
        }

        /// <summary>
        /// 检查最终线段是否已到达停步范围，不跨越尚未经过的拐角判定到达。
        /// </summary>
        /// <param name="position">ECS 玩家实际位置。</param>
        /// <returns>最后一段已到达配置距离内时为真。</returns>
        private bool NavigationHasArrived(Vector3 position) => navigationWaypoint == navigationPath.Count - 1 &&
            Vector3.Distance(position, navigationPath[navigationWaypoint]) <= (float)ProjectContentConfig.Navigation.ArrivalDistance;

        /// <summary>
        /// 计算沿剩余路点的距离，用于识别碰撞阻挡造成的持续停滞。
        /// </summary>
        /// <param name="position">玩家实际位置。</param>
        /// <returns>剩余折线路程。</returns>
        private float RemainingNavigationDistance(Vector3 position)
        {
            float distance = Vector3.Distance(position, navigationPath[navigationWaypoint]);
            for (int index = navigationWaypoint + 1; index < navigationPath.Count; index++)
                distance += Vector3.Distance(navigationPath[index - 1], navigationPath[index]);
            return distance;
        }

        /// <summary>
        /// 在接纳新指令时记录进度基线并清空堵路计时。
        /// </summary>
        /// <param name="position">玩家实际位置。</param>
        private void ResetNavigationProgress(Vector3 position)
        {
            navigationBestRemaining = RemainingNavigationDistance(position);
            navigationStuckSeconds = 0f;
        }

        /// <summary>
        /// 重用显示列表，把走过的路线从大小地图中移除。
        /// </summary>
        /// <param name="position">玩家实际位置。</param>
        private void RefreshNavigationRoute(Vector3 position)
        {
            navigationDisplayRoute.Clear();
            navigationDisplayRoute.Add(position);
            for (int index = navigationWaypoint; index < navigationPath.Count; index++) navigationDisplayRoute.Add(navigationPath[index]);
        }

        /// <summary>
        /// 清理临时导航及其移动意图，不更改暂停原因或自动攻击。
        /// </summary>
        /// <param name="message">需要显示的结束说明，空值保留当前业务提示。</param>
        private void EndNavigation(string message)
        {
            if (!IsNavigating) return;
            IsNavigating = false;
            navigationPath.Clear();
            navigationDisplayRoute.Clear();
            navigationWasDisplaced = false;
            navigationStuckSeconds = 0f;
            World.SetMove(Player, Vector2.zero);
            if (!string.IsNullOrEmpty(message)) Message = message;
        }
    }
}
