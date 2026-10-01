using System.Collections.Generic;
using UnityEngine;

namespace GameLogic.Gameplay.Combat
{
    /// <summary>
    /// 技能命中形状。
    /// </summary>
    public enum CombatHitShape { Circle, Sector, Box, Capsule }

    /// <summary>
    /// 战斗效果的不可变配置候选。
    /// </summary>
    public sealed class CombatEffectDefinition
    {
        public CombatEffectKind Kind;
        public CombatAttributeKind Attribute;
        public int DefinitionId;
        public float Value;
        public float AttackCoefficient;
        public bool CanCritical = true;
        public bool CanDodge = true;
        public int DurationTicks;
        public int LinkedId;
        public CombatHitShape Shape = CombatHitShape.Circle;
        public float Radius;
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
        public int CombatSkillId;
        public int DurationTicks;
        public int CooldownTicks;
        public float StartRange;
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
        public float PeriodicDamagePerStack;
        public readonly List<CombatEffectDefinition> OnApply = new List<CombatEffectDefinition>();
        public readonly List<CombatEffectDefinition> OnTick = new List<CombatEffectDefinition>();
        public readonly List<CombatEffectDefinition> OnExpire = new List<CombatEffectDefinition>();
    }

    /// <summary>
    /// 战斗配置集合，供原型与后续导表共用。
    /// </summary>
    public sealed class CombatCatalog
    {
        public readonly Dictionary<int, CombatSkillDefinition> Skills = new Dictionary<int, CombatSkillDefinition>();
        public readonly Dictionary<int, CombatProjectileDefinition> Projectiles = new Dictionary<int, CombatProjectileDefinition>();
        public readonly Dictionary<int, CombatBuffDefinition> Buffs = new Dictionary<int, CombatBuffDefinition>();

        /// <summary>
        /// 创建用于最小战斗循环的测试配置。
        /// </summary>
        /// <returns>包含普攻、飞弹和周期 Buff 的配置集合。</returns>
        public static CombatCatalog CreatePrototype()
        {
            var catalog = new CombatCatalog();
            var attack = new CombatSkillDefinition { CombatSkillId = 1, DurationTicks = 16, CooldownTicks = 24, StartRange = 2.2f };
            var cue = new CombatTimelineNode { Tick = 0 };
            cue.Effects.Add(new CombatEffectDefinition { Kind = CombatEffectKind.Cue, DefinitionId = 1, DurationTicks = 16 });
            var dash = new CombatTimelineNode { Tick = 5 };
            dash.Effects.Add(new CombatEffectDefinition { Kind = CombatEffectKind.Displace, DefinitionId = 2, Value = 2f, DurationTicks = 5 });
            var hit = new CombatTimelineNode { Tick = 10 };
            hit.Effects.Add(new CombatEffectDefinition
            {
                Kind = CombatEffectKind.Damage, DefinitionId = 3, Value = 15f, Shape = CombatHitShape.Sector, Radius = 2.2f,
                Angle = 100f, Height = 2f, RequireLineOfSight = true
            });
            attack.Nodes.Add(cue);
            attack.Nodes.Add(dash);
            attack.Nodes.Add(hit);
            catalog.Skills.Add(attack.CombatSkillId, attack);
            var periodic = new CombatBuffDefinition
            {
                BuffId = 1, DurationTicks = 180, IntervalTicks = 60, MaxStacks = 3,
                Merge = CombatBuffMerge.AddStack
            };
            periodic.OnTick.Add(new CombatEffectDefinition
            {
                Kind = CombatEffectKind.Damage, DefinitionId = 5, Value = 2f, CanCritical = false, CanDodge = false
            });
            catalog.Buffs.Add(periodic.BuffId, periodic);
            var projectile = new CombatProjectileDefinition
            {
                ProjectileId = 1, Motion = ProjectileMotionKind.Straight, Speed = 10f, MaxSpeed = 10f,
                LifetimeTicks = 90, MaxDistance = 15f, Radius = 0.18f, MaxDistinctTargets = 1
            };
            projectile.OnHit.Add(new CombatEffectDefinition { Kind = CombatEffectKind.Damage, DefinitionId = 4, Value = 12f });
            catalog.Projectiles.Add(projectile.ProjectileId, projectile);
            return catalog;
        }
    }
}
