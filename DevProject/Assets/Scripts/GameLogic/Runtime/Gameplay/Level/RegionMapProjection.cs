using System.Collections.Generic;
using System;
using UnityEngine;

namespace GameLogic.Gameplay.Level
{
    /// <summary>
    /// 地图中显示的关键目标类别，不绘制普通怪散点。
    /// </summary>
    public enum RegionMapMarkerKind { Player, Elite, Boss, Chest, Npc, Exit }

    /// <summary>
    /// 地图标记当前的可见业务状态。
    /// </summary>
    public enum RegionMapMarkerStatus { Alive, Countdown, Waiting, Warning, Complete, Unopened, Opened, Visited, SoldOut, Locked, Available }

    /// <summary>
    /// 只读投影的一份独立关键目标快照。
    /// </summary>
    public sealed class RegionMapMarker
    {
        public string Id;
        public RegionMapMarkerKind Kind;
        public RegionMapMarkerStatus Status;
        public Vector3 Position;
        public Vector3 Forward;
        public int LifeSequence;
        public int CurrentActorId;
        public long RemainingTicks;
        public int RemainingItems;
        public RegionSpawnWaitReason WaitingReason;
        public bool IsTracked;
    }

    /// <summary>
    /// 全图或玩家周边投影的一点，边缘方向可用于追踪箭头。
    /// </summary>
    public struct RegionMapPoint
    {
        public Vector2 NormalizedPosition;
        public Vector2 EdgeDirection;
        public bool IsInside;
    }

    /// <summary>
    /// 从已生成区域及活体位置构建的地图快照，不控制战斗或暂停。
    /// </summary>
    public sealed class RegionMapProjection
    {
        public int MapId;
        public string MapVersion;
        public Rect Bounds;
        public long EffectiveTick;
        public List<RegionMapMarker> Markers = new();

        /// <summary>
        /// 读取有效运行态并构建全区域关键点快照。
        /// </summary>
        /// <param name="state">已完成恢复校验的当前区域。</param>
        /// <param name="map">版本匹配的地图描述。</param>
        /// <param name="playerPosition">玩家战斗真值坐标。</param>
        /// <param name="playerForward">玩家战斗真值朝向。</param>
        /// <param name="livePositionResolver">按活体 ActorId 读取战斗位置，调用契约保证存在。</param>
        /// <returns>不共享可写运行记录的投影快照。</returns>
        public static RegionMapProjection Build(RegionRuntimeState state, RegionMapDefinition map, Vector3 playerPosition,
            Vector3 playerForward, Func<int, Vector3> livePositionResolver)
        {
            var projection = new RegionMapProjection
            {
                MapId = map.MapId, MapVersion = map.Version, Bounds = map.Bounds, EffectiveTick = state.EffectiveTick
            };
            projection.Markers.Add(new RegionMapMarker
            {
                Id = state.Plan.Request.RunId + ":player", Kind = RegionMapMarkerKind.Player, Status = RegionMapMarkerStatus.Alive,
                Position = playerPosition, Forward = playerForward, CurrentActorId = 1
            });
            foreach (RegionSpawnSlotPlan definition in state.Plan.SpawnSlots)
            {
                if (definition.Kind == RegionEnemyKind.Normal) continue;
                RegionSpawnSlotState slot = state.Slots.Find(value => value.SlotId == definition.SlotId);
                bool alive = slot.Phase == RegionSpawnPhase.Alive;
                RegionRespawnRule rule = definition.Kind == RegionEnemyKind.Elite ? state.Plan.EliteRespawn : state.Plan.NormalRespawn;
                long remaining = slot.Phase == RegionSpawnPhase.DeadWaiting ? Math.Max(0, slot.ReadyTick - state.EffectiveTick) :
                    slot.Phase == RegionSpawnPhase.Warning ? Math.Max(0, rule.WarningTicks - (state.EffectiveTick - slot.WarningStartTick)) : 0;
                projection.Markers.Add(new RegionMapMarker
                {
                    Id = definition.SlotId, Kind = definition.Kind == RegionEnemyKind.Boss ? RegionMapMarkerKind.Boss : RegionMapMarkerKind.Elite,
                    Status = SpawnStatus(slot.Phase), Position = alive ? livePositionResolver(slot.CurrentActorId) : definition.HomePosition,
                    LifeSequence = slot.LifeSequence, CurrentActorId = slot.CurrentActorId, RemainingTicks = remaining,
                    WaitingReason = slot.WaitingReason, IsTracked = state.TrackingTargetId == definition.SlotId
                });
            }
            foreach (RegionChestPlan definition in state.Plan.Chests)
            {
                RegionChestState chest = state.Chests.Find(value => value.Id == definition.Id);
                int remaining = definition.Items.Count - chest.ClaimedItemIds.Count;
                projection.Markers.Add(new RegionMapMarker
                {
                    Id = definition.Id, Kind = RegionMapMarkerKind.Chest, Position = definition.Position, RemainingItems = remaining,
                    Status = !chest.Opened ? RegionMapMarkerStatus.Unopened : remaining == 0 ? RegionMapMarkerStatus.Complete : RegionMapMarkerStatus.Opened,
                    IsTracked = state.TrackingTargetId == definition.Id
                });
            }
            foreach (RegionNpcPlan definition in state.Plan.Npcs)
            {
                RegionNpcState npc = state.Npcs.Find(value => value.Id == definition.Id);
                int remaining = definition.Offers.Count - npc.SoldOfferIds.Count;
                projection.Markers.Add(new RegionMapMarker
                {
                    Id = definition.Id, Kind = RegionMapMarkerKind.Npc, Position = definition.Position, RemainingItems = remaining,
                    Status = remaining == 0 ? RegionMapMarkerStatus.SoldOut : npc.Visited ? RegionMapMarkerStatus.Visited : RegionMapMarkerStatus.Available,
                    IsTracked = state.TrackingTargetId == definition.Id
                });
            }
            projection.Markers.Add(new RegionMapMarker
            {
                Id = state.ExitTargetId, Kind = RegionMapMarkerKind.Exit, Position = state.Plan.ExitPosition,
                Status = state.ExitEnabled ? RegionMapMarkerStatus.Available : RegionMapMarkerStatus.Locked,
                IsTracked = state.TrackingTargetId == state.ExitTargetId
            });
            return projection;
        }

        /// <summary>
        /// 将世界位置投影到整张底图，保持资源约定的北向。
        /// </summary>
        /// <param name="map">地图边界及北向。</param>
        /// <param name="position">世界坐标。</param>
        /// <returns>归一化坐标及越界信息。</returns>
        public static RegionMapPoint ProjectWorld(RegionMapDefinition map, Vector3 position)
        {
            return MakePoint(ProjectWorldUv(map, position));
        }

        /// <summary>
        /// 取得未经边缘压缩的底图坐标，供路线裁切与反投影使用。
        /// </summary>
        /// <param name="map">地图边界及北向。</param>
        /// <param name="position">世界坐标。</param>
        /// <returns>可超出零到一区间的底图坐标。</returns>
        public static Vector2 ProjectWorldUv(RegionMapDefinition map, Vector3 position)
        {
            Vector2 offset = Rotate(new Vector2(position.x - map.Bounds.center.x, position.z - map.Bounds.center.y), map.NorthYawDegrees);
            return new Vector2(offset.x / map.Bounds.width, offset.y / map.Bounds.height) + Vector2.one * 0.5f;
        }

        /// <summary>
        /// 将视口内点击还原为世界位置，拒绝视口外或地图边界外的位置。
        /// </summary>
        /// <param name="map">地图边界及北向。</param>
        /// <param name="viewport">底图当前显示的归一化裁切矩形。</param>
        /// <param name="position">视口归一化点击位置。</param>
        /// <param name="height">还原坐标的世界高度。</param>
        /// <param name="world">成功时的世界坐标。</param>
        /// <returns>点击处于视口和世界地图边界内时为真。</returns>
        public static bool TryUnprojectViewport(RegionMapDefinition map, Rect viewport, Vector2 position, float height, out Vector3 world)
        {
            world = default;
            if (!RegionValueRules.Finite(position.x) || !RegionValueRules.Finite(position.y) || position.x < 0f || position.x > 1f ||
                position.y < 0f || position.y > 1f || viewport.width <= 0f || viewport.height <= 0f) return false;
            return TryUnprojectWorld(map, viewport.position + Vector2.Scale(position, viewport.size), height, out world);
        }

        /// <summary>
        /// 反转底图北向旋转并还原世界位置，不将越界坐标夹到边缘。
        /// </summary>
        /// <param name="map">地图边界及北向。</param>
        /// <param name="uv">未经边缘压缩的底图坐标。</param>
        /// <param name="height">还原坐标的世界高度。</param>
        /// <param name="world">成功时的世界坐标。</param>
        /// <returns>坐标有限且处于世界地图闭合边界内时为真。</returns>
        public static bool TryUnprojectWorld(RegionMapDefinition map, Vector2 uv, float height, out Vector3 world)
        {
            world = default;
            if (!RegionValueRules.Finite(uv.x) || !RegionValueRules.Finite(uv.y) || !RegionValueRules.Finite(height)) return false;
            Vector2 offset = Rotate(Vector2.Scale(uv - Vector2.one * 0.5f, map.Bounds.size), -map.NorthYawDegrees);
            Vector2 position = map.Bounds.center + offset;
            if (position.x < map.Bounds.xMin || position.x > map.Bounds.xMax || position.y < map.Bounds.yMin || position.y > map.Bounds.yMax)
                return false;
            world = new Vector3(position.x, height, position.y);
            return true;
        }

        /// <summary>
        /// 将世界位置投影到以玩家为中心的固定北向小地图。
        /// </summary>
        /// <param name="position">目标世界坐标。</param>
        /// <param name="center">玩家或视图中心。</param>
        /// <param name="visibleRadius">配置中的局部显示半径。</param>
        /// <param name="northYawDegrees">地图北向对应的世界偏转角。</param>
        /// <returns>方形视口内坐标或边缘箭头方向。</returns>
        public static RegionMapPoint ProjectLocal(Vector3 position, Vector3 center, float visibleRadius, float northYawDegrees = 0f)
        {
            Vector2 offset = new Vector2(position.x - center.x, position.z - center.z) / (visibleRadius * 2f);
            return MakePoint(Rotate(offset, northYawDegrees) + Vector2.one * 0.5f);
        }

        /// <summary>
        /// 计算玩家箭头相对底图北向的顺时针角度。
        /// </summary>
        /// <param name="forward">战斗朝向。</param>
        /// <param name="northYawDegrees">地图北向对应的世界偏转角。</param>
        /// <returns>相对北向的顺时针角度。</returns>
        public static float HeadingDegrees(Vector3 forward, float northYawDegrees = 0f) =>
            Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg - northYawDegrees;

        /// <summary>
        /// 将刷新阶段映射为界面状态。
        /// </summary>
        /// <param name="phase">当前刷新阶段。</param>
        /// <returns>对应地图状态。</returns>
        private static RegionMapMarkerStatus SpawnStatus(RegionSpawnPhase phase) => phase == RegionSpawnPhase.Alive ||
            phase == RegionSpawnPhase.InitialSpawn ? RegionMapMarkerStatus.Alive : phase == RegionSpawnPhase.DeadWaiting ?
            RegionMapMarkerStatus.Countdown : phase == RegionSpawnPhase.Warning ? RegionMapMarkerStatus.Warning :
            phase == RegionSpawnPhase.Defeated ? RegionMapMarkerStatus.Complete : RegionMapMarkerStatus.Waiting;

        /// <summary>
        /// 旋转平面偏移，使给定世界方向成为底图北向。
        /// </summary>
        /// <param name="offset">中心相对偏移。</param>
        /// <param name="degrees">北向世界角度。</param>
        /// <returns>地图坐标方向偏移。</returns>
        private static Vector2 Rotate(Vector2 offset, float degrees)
        {
            float angle = degrees * Mathf.Deg2Rad, sin = Mathf.Sin(angle), cos = Mathf.Cos(angle);
            return new Vector2(offset.x * cos - offset.y * sin, offset.x * sin + offset.y * cos);
        }

        /// <summary>
        /// 将越界目标沿中心射线投影到边缘，不改变目标距离事实。
        /// </summary>
        /// <param name="normalized">未裁剪归一化坐标。</param>
        /// <returns>视口位置、方向及可见状态。</returns>
        private static RegionMapPoint MakePoint(Vector2 normalized)
        {
            bool inside = normalized.x >= 0f && normalized.x <= 1f && normalized.y >= 0f && normalized.y <= 1f;
            Vector2 offset = normalized - Vector2.one * 0.5f;
            Vector2 point = inside ? normalized : Vector2.one * 0.5f + offset * (0.5f / Mathf.Max(Mathf.Abs(offset.x), Mathf.Abs(offset.y)));
            return new RegionMapPoint { NormalizedPosition = point, EdgeDirection = offset.normalized, IsInside = inside };
        }
    }
}
