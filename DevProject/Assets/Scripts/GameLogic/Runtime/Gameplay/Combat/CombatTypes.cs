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
    /// 技能的主要效果分类。
    /// </summary>
    public enum CombatSkillEffectClass { Active, Auxiliary, Buff }

    /// <summary>
    /// 技能的用途分类。
    /// </summary>
    public enum CombatSkillType { BasicAttack, Normal, Special }

    /// <summary>
    /// 技能的施放入口。
    /// </summary>
    public enum CombatSkillCastMode { Automatic, Conditional, Persistent, Manual }

    /// <summary>
    /// 技能准入所需的目标。
    /// </summary>
    public enum CombatSkillTargetKind { Enemy, Self }

    /// <summary>
    /// 状态的显式生命周期。
    /// </summary>
    public enum CombatBuffLifetime { Timed, Persistent }

    /// <summary>
    /// 战斗效果类型。
    /// </summary>
    public enum CombatEffectKind { Cue, Damage, Displace, SpawnProjectile, ApplyBuff, ModifyAttribute, Heal }

    /// <summary>
    /// 局内可强化的战斗属性。
    /// </summary>
    public enum CombatAttributeKind
    {
        MaxHealth, Attack, Defense, CriticalRating, CriticalDamage, DodgeChance, DamageIncrease, DamageReduction
    }

    /// <summary>
    /// 飞弹运动类型。
    /// </summary>
    public enum ProjectileMotionKind { Straight, GravityArc, FixedEndpointArc, Instant, Orbit }

    /// <summary>
    /// 飞弹目标绑定类型。
    /// </summary>
    public enum ProjectileTargetKind { Direction, PointSnapshot, EntitySnapshot, EntityLock }

    /// <summary>
    /// 锁定目标丢失后的处理方式。
    /// </summary>
    public enum ProjectileLostTarget { ContinueDirection, LastPoint, Expire, Reacquire }

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
    public enum CombatEventKind { Cue, Damage, Death, RoomCleared, ProjectileHit, ProjectileEnded, BuffApplied, BuffExpired, CastCancelled, Dodged, Heal }

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
        public readonly bool IsCritical;

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
        /// <param name="isCritical">伤害是否为暴击。</param>
        public CombatEvent(CombatEventKind kind, long tick, int roomId, int actorId, int targetActorId,
            int definitionId, float value, Vector3 position, bool isCritical = false)
        {
            Kind = kind;
            Tick = tick;
            RoomId = roomId;
            ActorId = actorId;
            TargetActorId = targetActorId;
            DefinitionId = definitionId;
            Value = value;
            Position = position;
            IsCritical = isCritical;
        }
    }

    /// <summary>
    /// 敌人自动选择技能时使用的纯数据条件。
    /// </summary>
    [Serializable]
    public struct CombatEnemySkillDefinition
    {
        public int SkillId;
        public float MinHealthRatio;
        public float MaxHealthRatio;
        public float MinDistance;
        public float MaxDistance;
    }

    /// <summary>
    /// 一项配置伤害效果对角色的软弱点倍率。
    /// </summary>
    [Serializable]
    public struct CombatDamageVulnerability
    {
        public int EffectId;
        public float Multiplier;
    }

    /// <summary>
    /// 创建角色所需的初始数据。
    /// </summary>
    [Serializable]
    public struct CombatActorSpec
    {
        public int ActorId;
        public int RoomId;
        public int Team;
        public bool IsPlayer;
        public Vector3 Position;
        public float MaxHealth;
        public float Attack;
        public float Defense;
        public float CriticalRating;
        public float CriticalDamage;
        public float DodgeChance;
        public float DamageIncrease;
        public float DamageReduction;
        public float MoveSpeed;
        public float Radius;
        public float AttackRange;
        public int BasicSkillId;
        public int[] SkillIds;
        public CombatEnemySkillDefinition[] EnemySkills;
        public CombatDamageVulnerability[] DamageVulnerabilities;
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
        public readonly float Attack;
        public readonly float Defense;
        public readonly float CriticalRating;
        public readonly float CriticalDamage;
        public readonly float DodgeChance;
        public readonly float DamageIncrease;
        public readonly float DamageReduction;
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
        /// <param name="attack">最终攻击值。</param>
        /// <param name="defense">最终防御值。</param>
        /// <param name="criticalRating">最终暴击值。</param>
        /// <param name="criticalDamage">最终暴击伤害倍率。</param>
        /// <param name="dodgeChance">最终闪避概率。</param>
        /// <param name="damageIncrease">最终增伤比例。</param>
        /// <param name="damageReduction">最终减伤比例。</param>
        public CombatActorSnapshot(CombatEntityRef entity, int actorId, int roomId, Vector3 position, float health,
            float maxHealth, bool dead, float attack = 0f, float defense = 0f, float criticalRating = 0f,
            float criticalDamage = 1.5f, float dodgeChance = 0f, float damageIncrease = 0f, float damageReduction = 0f)
        {
            Entity = entity;
            ActorId = actorId;
            RoomId = roomId;
            Position = position;
            Health = health;
            MaxHealth = maxHealth;
            Attack = attack;
            Defense = defense;
            CriticalRating = criticalRating;
            CriticalDamage = criticalDamage;
            DodgeChance = dodgeChance;
            DamageIncrease = damageIncrease;
            DamageReduction = damageReduction;
            Dead = dead;
        }
    }
}
