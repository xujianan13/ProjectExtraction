using System;
using UnityEngine;

namespace GameLogic.Gameplay.Combat
{
    /// <summary>
    /// 战斗实体引用，代次防止对象池复用后误命中。
    /// </summary>
    public readonly struct CombatEntityRef : IEquatable<CombatEntityRef>
    {
        public readonly int World;
        public readonly int Id;
        public readonly int Generation;

        /// <summary>
        /// 创建战斗实体引用。
        /// </summary>
        /// <param name="world">战斗世界编号。</param>
        /// <param name="id">实体槽位编号。</param>
        /// <param name="generation">槽位代次。</param>
        public CombatEntityRef(int world, int id, int generation)
        {
            World = world;
            Id = id;
            Generation = generation;
        }

        /// <summary>
        /// 判断两个引用是否指向同一代实体。
        /// </summary>
        /// <param name="other">待比较的实体引用。</param>
        /// <returns>世界、槽位和代次均相同则为真。</returns>
        public bool Equals(CombatEntityRef other) => World == other.World && Id == other.Id && Generation == other.Generation;

        /// <summary>
        /// 判断对象是否为相同实体引用。
        /// </summary>
        /// <param name="obj">待比较对象。</param>
        /// <returns>引用相同则为真。</returns>
        public override bool Equals(object obj) => obj is CombatEntityRef other && Equals(other);

        /// <summary>
        /// 获取引用的哈希值。
        /// </summary>
        /// <returns>引用哈希值。</returns>
        public override int GetHashCode() => (World * 397) ^ (Id * 31) ^ Generation;
    }

    /// <summary>
    /// 战斗效果类型。
    /// </summary>
    public enum CombatEffectKind { Cue, Damage, Displace, SpawnProjectile, ApplyBuff }

    /// <summary>
    /// 飞弹运动类型。
    /// </summary>
    public enum ProjectileMotionKind { Straight, GravityArc, FixedEndpointArc, Instant }

    /// <summary>
    /// 飞弹目标绑定类型。
    /// </summary>
    public enum ProjectileTargetKind { Direction, PointSnapshot, EntitySnapshot, EntityLock }

    /// <summary>
    /// 锁定目标丢失后的处理方式。
    /// </summary>
    public enum ProjectileLostTarget { ContinueDirection, LastPoint, Expire }

    /// <summary>
    /// 飞弹出生锚点。
    /// </summary>
    public enum ProjectileSpawnAnchor { Source, TargetPointSnapshot, TargetEntityAtSpawn }

    /// <summary>
    /// Buff 合并策略。
    /// </summary>
    public enum CombatBuffMerge { AddStack, Replace, ResetDuration }

    /// <summary>
    /// 战斗事件类型。
    /// </summary>
    public enum CombatEventKind { Cue, Damage, Death, RoomCleared, ProjectileHit, ProjectileEnded, BuffApplied, BuffExpired, CastCancelled }

    /// <summary>
    /// 一条已确认的战斗事件。
    /// </summary>
    public readonly struct CombatEvent
    {
        public readonly CombatEventKind Kind;
        public readonly long Tick;
        public readonly int RoomId;
        public readonly int ActorId;
        public readonly int TargetActorId;
        public readonly int DefinitionId;
        public readonly float Value;
        public readonly Vector3 Position;

        /// <summary>
        /// 创建战斗事件。
        /// </summary>
        /// <param name="kind">事件类型。</param>
        /// <param name="tick">战斗逻辑帧。</param>
        /// <param name="roomId">所属地图编号。</param>
        /// <param name="actorId">来源角色编号。</param>
        /// <param name="targetActorId">目标角色编号。</param>
        /// <param name="definitionId">配置编号。</param>
        /// <param name="value">事件数值。</param>
        /// <param name="position">事件位置。</param>
        public CombatEvent(CombatEventKind kind, long tick, int roomId, int actorId, int targetActorId, int definitionId, float value, Vector3 position)
        {
            Kind = kind;
            Tick = tick;
            RoomId = roomId;
            ActorId = actorId;
            TargetActorId = targetActorId;
            DefinitionId = definitionId;
            Value = value;
            Position = position;
        }
    }

    /// <summary>
    /// 创建角色所需的初始数据。
    /// </summary>
    public struct CombatActorSpec
    {
        public int ActorId;
        public int RoomId;
        public int Team;
        public bool IsPlayer;
        public Vector3 Position;
        public float MaxHealth;
        public float MoveSpeed;
        public float Radius;
        public float AttackRange;
        public int BasicSkillId;
        public float AggroRange;
    }

    /// <summary>
    /// 供玩法层读取的角色快照。
    /// </summary>
    public readonly struct CombatActorSnapshot
    {
        public readonly CombatEntityRef Entity;
        public readonly int ActorId;
        public readonly int RoomId;
        public readonly Vector3 Position;
        public readonly float Health;
        public readonly float MaxHealth;
        public readonly bool Dead;

        /// <summary>
        /// 创建角色状态快照。
        /// </summary>
        /// <param name="entity">实体引用。</param>
        /// <param name="actorId">业务角色编号。</param>
        /// <param name="roomId">当前地图编号。</param>
        /// <param name="position">当前位置。</param>
        /// <param name="health">当前生命值。</param>
        /// <param name="maxHealth">最大生命值。</param>
        /// <param name="dead">角色是否死亡。</param>
        public CombatActorSnapshot(CombatEntityRef entity, int actorId, int roomId, Vector3 position, float health, float maxHealth, bool dead)
        {
            Entity = entity;
            ActorId = actorId;
            RoomId = roomId;
            Position = position;
            Health = health;
            MaxHealth = maxHealth;
            Dead = dead;
        }
    }
}
