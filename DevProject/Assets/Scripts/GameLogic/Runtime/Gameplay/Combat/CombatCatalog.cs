using System.Collections.Generic;
using System;
using UnityEngine;

namespace GameLogic.Gameplay.Combat
{
    /// <summary>
    /// 技能命中形状。
    /// </summary>
    public enum CombatHitShape { Circle, Sector, Box, Capsule, ForwardBox }

    /// <summary>
    /// 战斗效果的不可变配置候选。
    /// </summary>
    [Serializable]
    public sealed class CombatEffectDefinition
    {
        public CombatEffectKind Kind;
        public CombatAttributeKind Attribute;
        public int DefinitionId;
        public int Count = 1;
        public float SpreadDegrees;
        public float Value;
        public float AttackCoefficient;
        public bool CanCritical = true;
        public bool CanDodge = true;
        public int DurationTicks;
        public int LinkedId;
        public CombatHitShape Shape = CombatHitShape.Circle;
        public float Radius;
        public bool AttackRangeSource;
        public float AttackRangeMultiplier = 1f;
        public float Angle = 360f;
        public float Width;
        public float Height = 2f;
        public bool RequireLineOfSight;
    }

    /// <summary>
    /// 战斗技能的时间轴节点。
    /// </summary>
    public sealed class CombatTimelineNode
    {
        public int Tick;
        public readonly List<CombatEffectDefinition> Effects = new List<CombatEffectDefinition>();
    }

    /// <summary>
    /// 战斗技能定义，与局外成长技能定义分离。
    /// </summary>
    public sealed class CombatSkillDefinition
    {
        public CombatSkillEffectClass EffectClass;
        public CombatSkillType SkillType;
        public CombatSkillCastMode CastMode;
        public CombatSkillTargetKind TargetKind;
        public bool OccupiesAction = true;
        public float MaxHealthRatio = 1f;
        public int RequiredMissingBuffId;
        public int MaintainBuffId;
        public int CombatSkillId;
        public int DurationTicks;
        public int CooldownTicks;
        public float StartRange;
        public bool AttackRangeSource;
        public float AttackRangeMultiplier = 1f;
        public readonly List<CombatTimelineNode> Nodes = new List<CombatTimelineNode>();
    }

    /// <summary>
    /// 飞弹运行参数定义。
    /// </summary>
    public sealed class CombatProjectileDefinition
    {
        public int ProjectileId;
        public ProjectileMotionKind Motion;
        public ProjectileTargetKind Target = ProjectileTargetKind.Direction;
        public ProjectileSpawnAnchor SpawnAnchor = ProjectileSpawnAnchor.Source;
        public ProjectileLostTarget LostTarget = ProjectileLostTarget.ContinueDirection;
        public Vector3 SpawnOffset;
        public float ReacquireRange;
        public float OrbitRadius;
        public float OrbitDegreesPerSecond;
        public float OrbitInitialAngle;
        public float Speed;
        public float Acceleration;
        public float MaxSpeed;
        public float TurnDegreesPerSecond;
        public Vector3 Gravity = new Vector3(0f, -9.81f, 0f);
        public float ArcHeight;
        public int FlightTicks;
        public int LifetimeTicks = 120;
        public float MaxDistance = 20f;
        public float Radius = 0.15f;
        public int MaxDistinctTargets = 1;
        public int MaxBounces;
        public int SameTargetIntervalTicks;
        public int MaxHitsPerTarget = 1;
        public bool OnlyLockedTarget;
        public bool RemoveOnSourceDeath;
        public readonly List<CombatEffectDefinition> OnHit = new List<CombatEffectDefinition>();
        public readonly List<CombatEffectDefinition> OnArrive = new List<CombatEffectDefinition>();
        public readonly List<CombatEffectDefinition> OnExpire = new List<CombatEffectDefinition>();
    }

    /// <summary>
    /// Buff 运行参数定义。
    /// </summary>
    public sealed class CombatBuffDefinition
    {
        public int BuffId;
        public int DurationTicks;
        public int IntervalTicks;
        public int MaxStacks = 1;
        public CombatBuffMerge Merge = CombatBuffMerge.AddStack;
        public float MoveSpeedAddPerStack;
        public float AttackRangeAddPerStack;
        public CombatBuffLifetime Lifetime;
        public readonly List<CombatEffectDefinition> OnApply = new List<CombatEffectDefinition>();
        public readonly List<CombatEffectDefinition> OnTick = new List<CombatEffectDefinition>();
        public readonly List<CombatEffectDefinition> OnExpire = new List<CombatEffectDefinition>();
    }

    /// <summary>
    /// 高级词条在已确认战斗结果上的触发条件。
    /// </summary>
    public enum CombatTriggerKind { None, Hit, CriticalHit, Kill, Damaged }

    /// <summary>
    /// 本局角色词条定义；世界接纳时复制定义及效果，调用方可复用原始对象。
    /// </summary>
    [Serializable]
    public sealed class CombatAugmentDefinition
    {
        public string Id;
        public CombatTriggerKind Trigger;
        public float Chance = 1f;
        public int CooldownTicks;
        public bool TargetSelf;
        public List<CombatEffectDefinition> Effects = new List<CombatEffectDefinition>();
        public bool AffectsBasicAttack;
        public int SkillId;
        public float CooldownMultiplier = 1f;
        public float RangeMultiplier = 1f;
        public float DamageMultiplier = 1f;
    }

    /// <summary>
    /// 战斗配置集合，供原型与后续导表共用。
    /// </summary>
    public sealed class CombatCatalog
    {
        public float InitialAttackRange;
        public readonly Dictionary<int, CombatSkillDefinition> Skills = new Dictionary<int, CombatSkillDefinition>();
        public readonly Dictionary<int, CombatProjectileDefinition> Projectiles = new Dictionary<int, CombatProjectileDefinition>();
        public readonly Dictionary<int, CombatBuffDefinition> Buffs = new Dictionary<int, CombatBuffDefinition>();

        /// <summary>
        /// 由配置装配层登记的合法伤害效果编号，包含动态词条使用的伤害定义。
        /// </summary>
        public readonly HashSet<int> DamageEffectIds = new HashSet<int>();

    }
}
