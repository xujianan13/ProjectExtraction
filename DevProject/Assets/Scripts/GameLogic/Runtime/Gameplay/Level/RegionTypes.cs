using System;
using UnityEngine;

namespace GameLogic.Gameplay.Level
{
    /// <summary>
    /// 区域内稳定生成槽位的敌人类别。
    /// </summary>
    public enum RegionEnemyKind { Normal, Elite, Boss }

    /// <summary>
    /// 地图资源提供的空间锚点用途。
    /// </summary>
    public enum RegionAnchorKind { Entry, Monster, Elite, Chest, Npc, Boss, Exit }

    /// <summary>
    /// 一个槽位当前生命的生成与复活阶段。
    /// </summary>
    public enum RegionSpawnPhase { InitialSpawn, Alive, DeadWaiting, WaitingSafety, Warning, ReadyToSpawn, Defeated }

    /// <summary>
    /// 到期后未能开始或完成出生的原因。
    /// </summary>
    public enum RegionSpawnWaitReason { None, Countdown, UnsafeDistance, CameraVisible, Occupied, UnprocessedDeath }

    /// <summary>
    /// 可保存的世界平面矩形障碍。
    /// </summary>
    [Serializable]
    public sealed class RegionMapObstacle
    {
        public Vector2 Min;
        public Vector2 Max;

        /// <summary>
        /// 创建不共享可写字段的障碍副本。
        /// </summary>
        /// <returns>独立障碍副本。</returns>
        public RegionMapObstacle Copy() => new RegionMapObstacle { Min = Min, Max = Max };
    }

    /// <summary>
    /// 地图候选锚点，不包含正式业务内容。
    /// </summary>
    [Serializable]
    public sealed class RegionMapAnchor
    {
        public string Id;
        public RegionAnchorKind Kind;
        public string Tag;
        public Vector3 Position;
        public int Capacity;

        /// <summary>
        /// 创建锚点副本。
        /// </summary>
        /// <returns>独立锚点副本。</returns>
        public RegionMapAnchor Copy() => new RegionMapAnchor { Id = Id, Kind = Kind, Tag = Tag, Position = Position, Capacity = Capacity };
    }

    /// <summary>
    /// 地图中的禁止普通与精英出生范围。
    /// </summary>
    [Serializable]
    public sealed class RegionNoSpawnZone
    {
        public Vector3 Center;
        public float Radius;

        /// <summary>
        /// 创建禁止出生范围副本。
        /// </summary>
        /// <returns>独立范围副本。</returns>
        public RegionNoSpawnZone Copy() => new RegionNoSpawnZone { Center = Center, Radius = Radius };
    }

    /// <summary>
    /// 从配置转换的有效逻辑帧刷新参数。
    /// </summary>
    [Serializable]
    public sealed class RegionRespawnRule
    {
        public long DelayTicks;
        public long WarningTicks;
        public float SafeDistance;
        public float ExperienceMultiplier;

        /// <summary>
        /// 检查刷新参数是否可安全计时。
        /// </summary>
        /// <returns>全部参数有效时为真。</returns>
        public bool IsValid() => DelayTicks > 0 && WarningTicks > 0 && RegionValueRules.NonNegative(SafeDistance) &&
            RegionValueRules.NonNegative(ExperienceMultiplier);

        /// <summary>
        /// 创建刷新规则副本。
        /// </summary>
        /// <returns>独立刷新规则。</returns>
        public RegionRespawnRule Copy() => new RegionRespawnRule
        {
            DelayTicks = DelayTicks, WarningTicks = WarningTicks, SafeDistance = SafeDistance, ExperienceMultiplier = ExperienceMultiplier
        };
    }

    /// <summary>
    /// 由主协调器根据占位、距离和战斗相机提供的出生安全结果。
    /// </summary>
    public struct RegionSpawnSafety
    {
        public bool PositionAvailable;
        public bool OutsideSafeDistance;
        public bool OutsideCamera;
        public bool IsSafe => PositionAvailable && OutsideSafeDistance && OutsideCamera;
        public RegionSpawnWaitReason Reason => !PositionAvailable ? RegionSpawnWaitReason.Occupied :
            !OutsideSafeDistance ? RegionSpawnWaitReason.UnsafeDistance : !OutsideCamera ? RegionSpawnWaitReason.CameraVisible : RegionSpawnWaitReason.None;
    }

    /// <summary>
    /// 交给战斗协调器执行的出生请求，尚未创建实际生命。
    /// </summary>
    public struct RegionSpawnRequest
    {
        public string SlotId;
        public int ExpectedLifeSequence;
        public bool IsInitial;
        public int EnemyId;
        public int EnemyLevel;
        public RegionEnemyKind Kind;
        public Vector3 Position;
    }

    /// <summary>
    /// 区域数据边界共用的有限值检查。
    /// </summary>
    internal static class RegionValueRules
    {
        /// <summary>
        /// 检查有限浮点数。
        /// </summary>
        /// <param name="value">待检查数值。</param>
        /// <returns>数值有限时为真。</returns>
        internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        /// <summary>
        /// 检查有限非负值。
        /// </summary>
        /// <param name="value">待检查数值。</param>
        /// <returns>数值有限且非负时为真。</returns>
        internal static bool NonNegative(float value) => Finite(value) && value >= 0f;

        /// <summary>
        /// 检查有限世界坐标。
        /// </summary>
        /// <param name="value">待检查坐标。</param>
        /// <returns>所有坐标分量有限时为真。</returns>
        internal static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
    }
}
