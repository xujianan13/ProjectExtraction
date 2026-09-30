using UnityEngine;

namespace GameLogic.Gameplay.Combat
{
    /// <summary>
    /// 供表现层读取的飞弹状态。
    /// </summary>
    public readonly struct CombatProjectileSnapshot
    {
        public readonly CombatEntityRef Entity;
        public readonly int ProjectileId;
        public readonly int RoomId;
        public readonly Vector3 Position;
        public readonly Vector3 Direction;

        /// <summary>
        /// 创建飞弹状态快照。
        /// </summary>
        /// <param name="entity">飞弹实体引用。</param>
        /// <param name="projectileId">飞弹定义编号。</param>
        /// <param name="roomId">飞弹所在地图编号。</param>
        /// <param name="position">飞弹当前位置。</param>
        /// <param name="direction">当前方向。</param>
        public CombatProjectileSnapshot(CombatEntityRef entity, int projectileId, int roomId, Vector3 position,
            Vector3 direction)
        {
            Entity = entity;
            ProjectileId = projectileId;
            RoomId = roomId;
            Position = position;
            Direction = direction;
        }
    }
}
