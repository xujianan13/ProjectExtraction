using UnityEngine;

namespace GameLogic.Gameplay.Combat
{
    /// <summary>
    /// 供表现层读取的角色动画状态。
    /// </summary>
    public readonly struct CombatAnimationSnapshot
    {
        public readonly CombatEntityRef Entity;
        public readonly int ActorId;
        public readonly int RoomId;
        public readonly long ClockTick;
        public readonly Vector3 Forward;
        public readonly Vector3 Velocity;
        public readonly Vector3 CueDirection;
        public readonly int CueId;
        public readonly int CueElapsedTicks;
        public readonly int CueDurationTicks;
        public readonly int HitElapsedTicks;
        public readonly int DeathElapsedTicks;
        public readonly bool Dead;

        /// <summary>
        /// 创建角色动画状态快照。
        /// </summary>
        /// <param name="entity">角色实体引用。</param>
        /// <param name="actorId">稳定角色编号。</param>
        /// <param name="roomId">角色当前地图编号。</param>
        /// <param name="clockTick">角色使用的逻辑时钟。</param>
        /// <param name="forward">角色当前朝向。</param>
        /// <param name="velocity">本帧实际移动速度。</param>
        /// <param name="cueDirection">动作提示方向。</param>
        /// <param name="cueId">动作提示编号。</param>
        /// <param name="cueElapsedTicks">动作提示已经经过的逻辑帧。</param>
        /// <param name="cueDurationTicks">动作提示持续的逻辑帧。</param>
        /// <param name="hitElapsedTicks">最近一次有效受击后经过的逻辑帧，未受击时为负一。</param>
        /// <param name="deathElapsedTicks">死亡发布后经过的逻辑帧，未死亡时为负一。</param>
        /// <param name="dead">角色是否已进入死亡状态。</param>
        public CombatAnimationSnapshot(CombatEntityRef entity, int actorId, int roomId, long clockTick,
            Vector3 forward, Vector3 velocity, Vector3 cueDirection, int cueId, int cueElapsedTicks,
            int cueDurationTicks, int hitElapsedTicks, int deathElapsedTicks, bool dead)
        {
            Entity = entity;
            ActorId = actorId;
            RoomId = roomId;
            ClockTick = clockTick;
            Forward = forward;
            Velocity = velocity;
            CueDirection = cueDirection;
            CueId = cueId;
            CueElapsedTicks = cueElapsedTicks;
            CueDurationTicks = cueDurationTicks;
            HitElapsedTicks = hitElapsedTicks;
            DeathElapsedTicks = deathElapsedTicks;
            Dead = dead;
        }
    }

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
