using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameLogic.Gameplay.Combat
{
    /// <summary>
    /// 完整战斗提交边界的独立数据，不保存运行世界编号或 ECS 槽位。
    /// </summary>
    [Serializable]
    public sealed class CombatWorldSnapshot
    {
        public int Version = 2;
        public int RandomAlgorithmVersion = 1;
        public long RandomState;
        public long Tick;
        public long NextEntityKey;
        public long NextRootId;
        public long NextTriggerId;
        public int NextDeathId;
        public int HighestActorId;
        public int ActiveRoomId;
        public long PlayerKey;
        public List<CombatRoomState> Rooms = new();
        public List<CombatActorState> Actors = new();
        public List<CombatCastState> Casts = new();
        public List<CombatDisplacementState> Displacements = new();
        public List<CombatProjectileState> Projectiles = new();
        public List<CombatBuffState> Buffs = new();
    }

    /// <summary>
    /// 地图几何与局部有效时钟。
    /// </summary>
    [Serializable]
    public sealed class CombatRoomState
    {
        public int RoomId;
        public long Tick;
        public bool HasBounds;
        public Rect Bounds;
        public bool Cleared;
        public List<Rect> Obstacles = new();
    }

    /// <summary>
    /// 可排序的整数冷却记录。
    /// </summary>
    [Serializable]
    public sealed class CombatSkillCooldownState
    {
        public int SkillId;
        public int RemainingTicks;
    }

    /// <summary>
    /// 词条组的独立配置及各词条冷却时钟。
    /// </summary>
    [Serializable]
    public sealed class CombatAugmentGroupState
    {
        public string GroupId;
        public List<CombatAugmentDefinition> Definitions = new();
        public List<CombatAugmentCooldownState> Cooldowns = new();
    }

    /// <summary>
    /// 一项词条的下次可用时钟。
    /// </summary>
    [Serializable]
    public sealed class CombatAugmentCooldownState
    {
        public string AugmentId;
        public long Tick;
    }

    /// <summary>
    /// 角色完整运行状态，移动输入不参与持久化。
    /// </summary>
    [Serializable]
    public sealed class CombatActorState
    {
        public long Key;
        public CombatActorSpec Spec;
        public CombatActorSpec BaseSpec;
        public float[] PermanentBonuses;
        public List<CombatAugmentGroupState> AugmentGroups = new();
        public List<CombatSkillCooldownState> SkillCooldowns = new();
        public Vector3 Position;
        public Vector3 Forward;
        public int SkillCursor;
        public long ActionAcceptedTick = -1;
        public List<int> EnabledPersistentSkills = new();
        public Vector2 AiMove;
        public float Health;
        public float MoveBonus;
        public float RangeBonus;
        public bool DeadPending;
        public bool DeathPublished;
        public int LastInstigatorActorId;
        public Vector3 Velocity;
        public Vector3 CueDirection;
        public int CueId;
        public int CueDurationTicks;
        public long CueClock;
        public long HitClock;
        public long DeathClock;
    }

    /// <summary>
    /// 效果出生时冻结的进攻属性。
    /// </summary>
    [Serializable]
    public sealed class CombatOffenseState
    {
        public float Attack;
        public float CriticalRating;
        public float CriticalDamage;
        public float DamageIncrease;
        public float DamageMultiplier;
    }

    /// <summary>
    /// 施法执行游标，目标键零为空，负数表示已经失效的非空引用。
    /// </summary>
    [Serializable]
    public sealed class CombatCastState
    {
        public long Key;
        public long OwnerKey;
        public int SkillId;
        public long StartTick;
        public long TargetKey;
        public Vector3 TargetPoint;
        public Vector3 Forward;
        public int NextNode;
        public float RangeDelta;
        public float RangeMultiplier;
        public float TimeMultiplier = 1f;
        public bool UsesWorldClock;
        public CombatOffenseState Offense;
    }

    /// <summary>
    /// 尚未完成的战斗位移。
    /// </summary>
    [Serializable]
    public sealed class CombatDisplacementState
    {
        public long Key;
        public long OwnerKey;
        public Vector3 Direction;
        public float DistancePerTick;
        public int RemainingTicks;
        public long CreatedTick;
    }

    /// <summary>
    /// 飞弹对某次生命的命中去重记录。
    /// </summary>
    [Serializable]
    public sealed class CombatProjectileHitState
    {
        public long TargetKey;
        public long Tick;
        public int Count;
    }

    /// <summary>
    /// 飞弹完整轨迹、来源快照与命中历史。
    /// </summary>
    [Serializable]
    public sealed class CombatProjectileState
    {
        public long Key;
        public int ProjectileId;
        public float RangeDelta;
        public float RangeMultiplier;
        public bool PassiveDerived;
        public int RoomId;
        public int InstigatorActorId;
        public int SourceTeam;
        public CombatOffenseState Offense;
        public long SourceKey;
        public long TargetKey;
        public Vector3 LastTargetPoint;
        public Vector3 Position;
        public Vector3 Previous;
        public Vector3 Origin;
        public Vector3 EndPoint;
        public Vector3 Velocity;
        public Vector3 Direction;
        public int FlightTicks;
        public int ElapsedTicks;
        public long CreatedTick;
        public int Bounces;
        public float Distance;
        public int DistinctHits;
        public float OrbitAngle;
        public int ControllerSkillId;
        public bool Terminal;
        public List<CombatProjectileHitState> Hits = new();
    }

    /// <summary>
    /// Buff 已应用状态，恢复不会再次触发初次效果。
    /// </summary>
    [Serializable]
    public sealed class CombatBuffState
    {
        public long Key;
        public long OwnerKey;
        public float RangeDelta;
        public float RangeMultiplier;
        public bool PassiveDerived;
        public int BuffId;
        public int SourceActorId;
        public CombatOffenseState Offense;
        public int Stacks;
        public long ExpireTick;
        public long NextTick;
        public bool Removed;
    }
}
