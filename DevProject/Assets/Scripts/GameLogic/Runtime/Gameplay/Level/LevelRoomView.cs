using System;
using GameLogic.Gameplay.Expedition;
using UnityEngine;

namespace GameLogic.Gameplay.Level
{
    /// <summary>
    /// 由热更预制件构成的单张地图表现，生命周期由持有者显式释放。
    /// </summary>
    public sealed class LevelRoomView : IDisposable
    {
        private readonly LevelRoomState room;
        private readonly GameObject root;
        private readonly GameObject extractionMarker;
        private readonly GameObject chestMarker;
        private readonly LevelRoomLayout layout;
        private readonly LevelDefinition level;
        private readonly ExpeditionVisualAssets assets;
        private bool disposed;

        public GameObject Root => root;
        public LevelRoomLayout Layout => layout;

        /// <summary>
        /// 创建地图视图和全部固定环境物件。
        /// </summary>
        /// <param name="state">待显示的单局地图状态。</param>
        /// <param name="visualAssets">已加载的关卡预制件。</param>
        /// <param name="level">关卡定义。</param>
        /// <param name="parent">可选的场景父节点。</param>
        private LevelRoomView(LevelRoomState state, ExpeditionVisualAssets visualAssets, LevelDefinition level, Transform parent)
        {
            room = state;
            this.level = level;
            layout = new LevelRoomLayout(level);
            assets = visualAssets;
            root = UnityEngine.Object.Instantiate(assets.Room, parent, false);
            root.name = "LevelRoom_" + state.RoomId;
            Transform floor = root.transform.Find("Floor");
            Vector3 meshSize = floor.GetComponent<MeshFilter>().sharedMesh.bounds.size;
            floor.localScale = new Vector3(layout.HalfExtent * 2f / meshSize.x, floor.localScale.y, layout.HalfExtent * 2f / meshSize.z);
            BuildWalls();
            BuildObstacles();
            BuildExits();
            if (state.Type == LevelRoomType.Loot)
                chestMarker = CreatePart("Chest", assets.Chest, ChestPosition + Vector3.up * 0.6f, new Vector3(1.3f, 1.2f, 1f));
            if (state.Type == LevelRoomType.Npc)
                CreatePart("Npc", assets.Npc, NpcPosition + Vector3.up, Vector3.one);
            if (state.HasExtractionCandidate)
                extractionMarker = CreatePart("Extraction", assets.Extraction, ExtractionPosition + Vector3.up * 0.08f,
                    new Vector3(1.5f, 0.08f, 1.5f));
            Refresh();
        }

        private Vector3 ChestPosition => new Vector3((float)level.Layout.ChestX, 0f, (float)level.Layout.ChestZ);
        private Vector3 NpcPosition => new Vector3((float)level.Layout.NpcX, 0f, (float)level.Layout.NpcZ);
        private Vector3 ExtractionPosition => new Vector3((float)level.Layout.ExtractionX, 0f, (float)level.Layout.ExtractionZ);

        /// <summary>
        /// 创建一张地图的预制件视图。
        /// </summary>
        /// <param name="room">单局地图状态。</param>
        /// <param name="assets">已加载的关卡预制件。</param>
        /// <param name="parent">可选的场景父节点。</param>
        /// <returns>需要由持有者释放的地图视图。</returns>
        public static LevelRoomView Create(LevelRoomState room, ExpeditionVisualAssets assets, Transform parent = null)
        {
            return Create(room, assets, room?.Plan?.Definition ?? PrototypeLevelConfig.DefaultLevel, parent);
        }

        /// <summary>
        /// 使用指定关卡配置创建房间视图。
        /// </summary>
        /// <param name="room">单局地图状态。</param>
        /// <param name="assets">已加载的关卡预制件。</param>
        /// <param name="level">关卡定义。</param>
        /// <param name="parent">可选的场景父节点。</param>
        /// <returns>需要由持有者释放的地图视图。</returns>
        public static LevelRoomView Create(LevelRoomState room, ExpeditionVisualAssets assets, LevelDefinition level, Transform parent = null)
        {
            LevelDefinition definition = room?.Plan?.Definition ?? level;
            return room == null || assets == null || definition == null ? null : new LevelRoomView(room, assets, definition, parent);
        }

        /// <summary>
        /// 按地图状态更新撤离点显现，不重新生成物品或敌人。
        /// </summary>
        public void Refresh()
        {
            if (extractionMarker != null) extractionMarker.SetActive(room.IsExtractionVisible);
            if (chestMarker != null) chestMarker.SetActive(!room.ChestOpened);
        }

        /// <summary>
        /// 获取从目标地图某一侧进入后的安全出生位置。
        /// </summary>
        /// <param name="entrySide">目标地图的入口所在方向。</param>
        /// <returns>地图根节点下的局部出生位置。</returns>
        public Vector3 GetSpawnPosition(RoomDirection entrySide)
        {
            float offset = (float)level.Layout.HalfExtent - (float)level.Layout.SpawnInset;
            switch (entrySide)
            {
                case RoomDirection.North: return new Vector3(0f, 0f, offset);
                case RoomDirection.East: return new Vector3(offset, 0f, 0f);
                case RoomDirection.South: return new Vector3(0f, 0f, -offset);
                default: return new Vector3(-offset, 0f, 0f);
            }
        }

        /// <summary>
        /// 查询角色附近已连接的出口。
        /// </summary>
        /// <param name="localPosition">地图根节点下的局部位置。</param>
        /// <param name="direction">成功时的出口方向。</param>
        /// <returns>附近存在可通行出口时为真。</returns>
        public bool TryGetExitNear(Vector3 localPosition, out RoomDirection direction)
        {
            float center = (float)level.Layout.HalfExtent - (float)level.Layout.ExitCenterInset;
            float radiusSquared = (float)level.Layout.ExitInteractionRadius * (float)level.Layout.ExitInteractionRadius;
            foreach (RoomDirection candidate in room.Exits.Keys)
            {
                Vector3 position = ExitCenter(candidate, center);
                float dx = localPosition.x - position.x;
                float dz = localPosition.z - position.z;
                if (dx * dx + dz * dz > radiusSquared) continue;
                direction = candidate;
                return true;
            }
            direction = default;
            return false;
        }

        /// <summary>
        /// 查询角色附近可用的箱子、NPC 或撤离标志。
        /// </summary>
        /// <param name="localPosition">地图根节点下的局部位置。</param>
        /// <param name="kind">成功时的交互类型。</param>
        /// <returns>附近存在可用交互时为真。</returns>
        public bool TryGetInteractionNear(Vector3 localPosition, out LevelInteractionKind kind)
        {
            float radiusSquared = (float)level.Layout.ObjectInteractionRadius * (float)level.Layout.ObjectInteractionRadius;
            if (room.IsExtractionVisible && IsNear(localPosition, ExtractionPosition, radiusSquared))
            {
                kind = LevelInteractionKind.Extraction;
                return true;
            }
            if (room.Type == LevelRoomType.Loot && !room.ChestOpened && IsNear(localPosition, ChestPosition, radiusSquared))
            {
                kind = LevelInteractionKind.Chest;
                return true;
            }
            if (room.Type == LevelRoomType.Npc && IsNear(localPosition, NpcPosition, radiusSquared))
            {
                kind = LevelInteractionKind.Npc;
                return true;
            }
            kind = LevelInteractionKind.None;
            return false;
        }

        /// <summary>
        /// 销毁地图实例，不改变单局状态。
        /// </summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            root.SetActive(false);
            if (Application.isPlaying) UnityEngine.Object.Destroy(root);
            else UnityEngine.Object.DestroyImmediate(root);
        }

        /// <summary>
        /// 按地图局部几何放置环境预制件。
        /// </summary>
        /// <param name="name">层级名称。</param>
        /// <param name="prefab">已加载的环境预制件。</param>
        /// <param name="position">局部位置。</param>
        /// <param name="scale">局部尺寸。</param>
        /// <returns>创建的预制件实例。</returns>
        private GameObject CreatePart(string name, GameObject prefab, Vector3 position, Vector3 scale)
        {
            GameObject part = UnityEngine.Object.Instantiate(prefab, root.transform, false);
            part.name = name;
            part.transform.localPosition = position;
            part.transform.localScale = scale;
            return part;
        }

        /// <summary>
        /// 生成四侧墙体，并为已连接的出口留出通道。
        /// </summary>
        private void BuildWalls()
        {
            foreach (RoomDirection direction in Enum.GetValues(typeof(RoomDirection)))
            {
                bool hasExit = room.Exits.ContainsKey(direction);
                if (!hasExit) { CreateWallSegment(direction, 0f, (float)level.Layout.HalfExtent * 2f); continue; }
                float length = (float)level.Layout.HalfExtent - (float)level.Layout.ExitHalfWidth;
                float offset = ((float)level.Layout.HalfExtent + (float)level.Layout.ExitHalfWidth) * 0.5f;
                CreateWallSegment(direction, -offset, length);
                CreateWallSegment(direction, offset, length);
            }
        }

        /// <summary>
        /// 创建一段边界墙。
        /// </summary>
        /// <param name="direction">墙体所在边。</param>
        /// <param name="offset">沿墙体方向的中心偏移。</param>
        /// <param name="length">墙段长度。</param>
        private void CreateWallSegment(RoomDirection direction, float offset, float length)
        {
            float extent = (float)level.Layout.HalfExtent;
            float thickness = (float)level.Layout.WallThickness;
            float height = (float)level.Layout.WallHeight;
            bool horizontal = direction == RoomDirection.North || direction == RoomDirection.South;
            Vector3 position = horizontal
                ? new Vector3(offset, height * 0.5f, direction == RoomDirection.North ? extent : -extent)
                : new Vector3(direction == RoomDirection.East ? extent : -extent, height * 0.5f, offset);
            Vector3 scale = horizontal ? new Vector3(length, height, thickness) : new Vector3(thickness, height, length);
            CreatePart("Wall_" + direction, assets.Wall, position, scale);
        }

        /// <summary>
        /// 生成与空间查询一致的少量内部障碍。
        /// </summary>
        private void BuildObstacles()
        {
            foreach (LevelObstacle obstacle in layout.Obstacles)
            {
                Vector2 size = obstacle.Max - obstacle.Min;
                Vector2 center = (obstacle.Min + obstacle.Max) * 0.5f;
                CreatePart("Obstacle", assets.Obstacle, new Vector3(center.x, (float)level.Layout.ObstacleHeight * 0.5f, center.y),
                    new Vector3(size.x, (float)level.Layout.ObstacleHeight, size.y));
            }
        }

        /// <summary>
        /// 在已连接出口内侧生成不阻挡的方向占位标志。
        /// </summary>
        private void BuildExits()
        {
            foreach (RoomDirection direction in room.Exits.Keys)
            {
                Vector3 position = ExitCenter(direction, (float)level.Layout.HalfExtent - (float)level.Layout.ExitCenterInset);
                CreatePart("Exit_" + direction, assets.Exit, position + Vector3.up * 0.1f, new Vector3(1.7f, 0.2f, 1.7f));
            }
        }

        /// <summary>
        /// 获取指定方向的局部出口中心。
        /// </summary>
        /// <param name="direction">出口方向。</param>
        /// <param name="distance">距房间中心的平面距离。</param>
        /// <returns>地图局部坐标。</returns>
        private static Vector3 ExitCenter(RoomDirection direction, float distance)
        {
            switch (direction)
            {
                case RoomDirection.North: return new Vector3(0f, 0f, distance);
                case RoomDirection.East: return new Vector3(distance, 0f, 0f);
                case RoomDirection.South: return new Vector3(0f, 0f, -distance);
                default: return new Vector3(-distance, 0f, 0f);
            }
        }

        /// <summary>
        /// 判断局部位置与交互中心的平面距离。
        /// </summary>
        /// <param name="position">角色局部位置。</param>
        /// <param name="target">交互中心位置。</param>
        /// <param name="radiusSquared">允许距离的平方。</param>
        /// <returns>位于交互范围时为真。</returns>
        private static bool IsNear(Vector3 position, Vector3 target, float radiusSquared)
        {
            float dx = position.x - target.x;
            float dz = position.z - target.z;
            return dx * dx + dz * dz <= radiusSquared;
        }
    }
}
