using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameLogic.Gameplay.Level
{
    /// <summary>
    /// 地图局部平面的边界和障碍，供移动及战斗空间查询。
    /// </summary>
    public sealed class LevelRoomLayout
    {
        private readonly LevelDefinition level;
        public float HalfExtent => (float)level.Layout.HalfExtent;
        public float WallThickness => (float)level.Layout.WallThickness;
        public float ObjectInteractionRadius => (float)level.Layout.ObjectInteractionRadius;
        public IReadOnlyList<LevelObstacle> Obstacles => level.Obstacles;

        /// <summary>
        /// 使用表中显式默认关卡创建布局。
        /// </summary>
        public LevelRoomLayout() : this(PrototypeLevelConfig.DefaultLevel) { }

        /// <summary>
        /// 使用指定关卡的布局。
        /// </summary>
        /// <param name="level">已解析的关卡定义。</param>
        public LevelRoomLayout(LevelDefinition level) => this.level = level ?? throw new ArgumentNullException(nameof(level));

        /// <summary>
        /// 判断圆形角色是否触及地图边界或内部障碍。
        /// </summary>
        /// <param name="localPosition">地图根节点下的局部位置。</param>
        /// <param name="radius">角色平面半径。</param>
        /// <returns>目标位置被阻挡时为真。</returns>
        public bool IsBlocked(Vector3 localPosition, float radius)
        {
            float limit = HalfExtent - WallThickness * 0.5f - radius;
            if (radius < 0f || localPosition.x < -limit || localPosition.x > limit ||
                localPosition.z < -limit || localPosition.z > limit) return true;
            var center = new Vector2(localPosition.x, localPosition.z);
            foreach (LevelObstacle obstacle in Obstacles) if (obstacle.Overlaps(center, radius)) return true;
            return false;
        }

        /// <summary>
        /// 将角色位置限制到地图内可行走区域。
        /// </summary>
        /// <param name="localPosition">地图根节点下的目标位置。</param>
        /// <param name="radius">角色平面半径。</param>
        /// <returns>调整后的局部位置。</returns>
        public Vector3 ClampPosition(Vector3 localPosition, float radius)
        {
            radius = Mathf.Max(0f, radius);
            float limit = HalfExtent - WallThickness * 0.5f - radius;
            localPosition.x = Mathf.Clamp(localPosition.x, -limit, limit);
            localPosition.z = Mathf.Clamp(localPosition.z, -limit, limit);
            foreach (LevelObstacle obstacle in Obstacles)
            {
                var center = new Vector2(localPosition.x, localPosition.z);
                if (!obstacle.Overlaps(center, radius)) continue;
                float left = Mathf.Abs(localPosition.x - (obstacle.Min.x - radius));
                float right = Mathf.Abs(localPosition.x - (obstacle.Max.x + radius));
                float down = Mathf.Abs(localPosition.z - (obstacle.Min.y - radius));
                float up = Mathf.Abs(localPosition.z - (obstacle.Max.y + radius));
                float nearest = Mathf.Min(left, right, down, up);
                if (nearest == left) localPosition.x = obstacle.Min.x - radius;
                else if (nearest == right) localPosition.x = obstacle.Max.x + radius;
                else if (nearest == down) localPosition.z = obstacle.Min.y - radius;
                else localPosition.z = obstacle.Max.y + radius;
            }
            return localPosition;
        }
    }
}
