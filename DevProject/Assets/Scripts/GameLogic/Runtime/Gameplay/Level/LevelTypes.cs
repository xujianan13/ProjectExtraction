using UnityEngine;

namespace GameLogic.Gameplay.Level
{
    /// <summary>
    /// 地图的四个通行方向。
    /// </summary>
    public enum RoomDirection
    {
        North,
        East,
        South,
        West
    }

    /// <summary>
    /// 首版地图内容类型。
    /// </summary>
    public enum LevelRoomType
    {
        Mob,
        Boss,
        Npc,
        Loot
    }

    /// <summary>
    /// 场景占位交互物类型。
    /// </summary>
    public enum LevelInteractionKind
    {
        None,
        Chest,
        Npc,
        Extraction
    }

    /// <summary>
    /// 关卡内的轴对齐障碍矩形。
    /// </summary>
    public readonly struct LevelObstacle
    {
        public readonly Vector2 Min;
        public readonly Vector2 Max;

        /// <summary>
        /// 创建局部 XZ 平面障碍。
        /// </summary>
        /// <param name="min">矩形最小 XZ 坐标。</param>
        /// <param name="max">矩形最大 XZ 坐标。</param>
        public LevelObstacle(Vector2 min, Vector2 max)
        {
            Min = min;
            Max = max;
        }

        /// <summary>
        /// 判断圆形占位是否与障碍重叠。
        /// </summary>
        /// <param name="position">局部平面的目标中心。</param>
        /// <param name="radius">目标半径。</param>
        /// <returns>重叠时为真。</returns>
        public bool Overlaps(Vector2 position, float radius)
        {
            float x = Mathf.Clamp(position.x, Min.x, Max.x);
            float y = Mathf.Clamp(position.y, Min.y, Max.y);
            return (position - new Vector2(x, y)).sqrMagnitude < radius * radius;
        }
    }
}
