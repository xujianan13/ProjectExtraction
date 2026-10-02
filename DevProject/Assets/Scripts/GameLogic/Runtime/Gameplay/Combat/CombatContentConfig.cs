using System;
using System.Collections.Generic;
using GameLogic.Tables;
using UnityEngine;

namespace GameLogic.Gameplay.Combat
{
    /// <summary>
    /// 将已加载的战斗内容表转换为单局战斗定义。
    /// </summary>
    public static class CombatContentConfig
    {
        /// <summary>
        /// 校验战斗表的字段、枚举、时间轴和跨表引用。
        /// </summary>
        /// <param name="error">失败时的首个错误说明。</param>
        /// <returns>全部战斗表可供运行时使用则为真。</returns>
        public static bool Validate(out string error)
        {
            error = null;
            if (GameTables.CfgCombatSkills == null || GameTables.CfgCombatTimelineNodes == null ||
                GameTables.CfgCombatEffects == null || GameTables.CfgCombatProjectiles == null ||
                GameTables.CfgCombatBuffs == null || GameTables.CfgCombatAnimationCues == null ||
                GameTables.CfgCombatAnimationSettings == null)
                return Fail("战斗配置表尚未加载。", out error);

            if (GameTables.CfgCombatSkills.GetAll().Count == 0 || GameTables.CfgCombatEffects.GetAll().Count == 0 ||
                GameTables.CfgCombatAnimationSettings.GetAll().Count != 1)
                return Fail("技能、效果或动画设置表为空或行数无效。", out error);

            foreach (CfgCombatSkillsRow row in GameTables.CfgCombatSkills.GetAll())
                if (row.ID <= 0 || row.DurationTicks <= 0 || row.CooldownTicks < 0 ||
                    !FiniteNonnegative(row.StartRange) || row.StartRangeSource < 0 || row.StartRangeSource > 1)
                    return Fail("技能参数无效：" + row.ID, out error);

            var nodeCounts = new Dictionary<int, int>();
            foreach (CfgCombatTimelineNodesRow row in GameTables.CfgCombatTimelineNodes.GetAll())
            {
                CfgCombatSkillsRow skill = GameTables.CfgCombatSkills.GetById(row.SkillID);
                if (row.ID <= 0 || skill == null || row.Tick < 0 || row.Tick >= skill.DurationTicks ||
                    row.EffectIDs == null || row.EffectIDs.Length == 0 ||
                    !ValidateEffectIds(row.EffectIDs, "时间轴 " + row.ID, out error))
                    return error == null ? Fail("时间轴参数或技能引用无效：" + row.ID, out error) : false;
                nodeCounts[row.SkillID] = nodeCounts.TryGetValue(row.SkillID, out int count) ? count + 1 : 1;
            }
            foreach (CfgCombatSkillsRow row in GameTables.CfgCombatSkills.GetAll())
                if (!nodeCounts.ContainsKey(row.ID)) return Fail("技能缺少时间轴：" + row.ID, out error);

            foreach (CfgCombatEffectsRow row in GameTables.CfgCombatEffects.GetAll())
            {
                if (row.ID <= 0 || row.DefinitionID <= 0 || row.Kind < 0 || row.Kind > (int)CombatEffectKind.Heal ||
                    row.Attribute < 0 || row.Attribute > (int)CombatAttributeKind.DamageReduction ||
                    row.Shape < 0 || row.Shape > (int)CombatHitShape.Capsule ||
                    row.RadiusSource < 0 || row.RadiusSource > 1 || row.DurationTicks < 0 ||
                    !Finite(row.Value) || !FiniteNonnegative(row.AttackCoefficient) ||
                    !FiniteNonnegative(row.Radius) || !FiniteNonnegative(row.Width) ||
                    !FiniteNonnegative(row.Height) || !Finite(row.Angle) || row.Angle < 0 || row.Angle > 360)
                    return Fail("效果参数无效：" + row.ID, out error);
                var kind = (CombatEffectKind)row.Kind;
                if ((kind == CombatEffectKind.Damage || kind == CombatEffectKind.Heal) && row.Value < 0 ||
                    kind == CombatEffectKind.Displace && row.DurationTicks <= 0 ||
                    kind == CombatEffectKind.ModifyAttribute && row.Value <= 0)
                    return Fail("效果类型参数无效：" + row.ID, out error);
                if (kind == CombatEffectKind.SpawnProjectile && GameTables.CfgCombatProjectiles.GetById(row.LinkedID) == null ||
                    kind == CombatEffectKind.ApplyBuff && GameTables.CfgCombatBuffs.GetById(row.LinkedID) == null)
                    return Fail("效果关联定义不存在：" + row.ID, out error);
            }

            foreach (CfgCombatProjectilesRow row in GameTables.CfgCombatProjectiles.GetAll())
            {
                if (row.ID <= 0 || row.Motion < 0 || row.Motion > (int)ProjectileMotionKind.Instant ||
                    row.Target < 0 || row.Target > (int)ProjectileTargetKind.EntityLock ||
                    row.SpawnAnchor < 0 || row.SpawnAnchor > (int)ProjectileSpawnAnchor.TargetEntityAtSpawn ||
                    row.LostTarget < 0 || row.LostTarget > (int)ProjectileLostTarget.Expire ||
                    !Finite(row.SpawnOffsetX) || !Finite(row.SpawnOffsetY) || !Finite(row.SpawnOffsetZ) ||
                    !Finite(row.GravityX) || !Finite(row.GravityY) || !Finite(row.GravityZ) ||
                    !Finite(row.Speed) || !Finite(row.Acceleration) || !Finite(row.MaxSpeed) ||
                    !FiniteNonnegative(row.TurnDegreesPerSecond) || !Finite(row.ArcHeight) ||
                    !FiniteNonnegative(row.MaxDistance) || !FiniteNonnegative(row.Radius) ||
                    row.FlightTicks < 0 || row.LifetimeTicks < 0 || row.MaxDistinctTargets <= 0 ||
                    row.MaxHitsPerTarget <= 0 || row.MaxBounces < 0 || row.SameTargetIntervalTicks < 0)
                    return Fail("飞弹参数无效：" + row.ID, out error);
                if (row.Motion == (int)ProjectileMotionKind.Instant)
                {
                    if (row.FlightTicks != 0 || row.Acceleration != 0 || row.TurnDegreesPerSecond != 0)
                        return Fail("瞬发飞弹参数无效：" + row.ID, out error);
                }
                else if (row.Speed <= 0 || row.LifetimeTicks <= 0 || row.MaxDistance <= 0)
                    return Fail("飞行飞弹需要速度、寿命和距离：" + row.ID, out error);
                if (row.Motion == (int)ProjectileMotionKind.FixedEndpointArc &&
                    (row.Acceleration != 0 || row.Target == (int)ProjectileTargetKind.EntityLock || row.MaxBounces > 0) ||
                    row.Motion == (int)ProjectileMotionKind.GravityArc && row.Target == (int)ProjectileTargetKind.EntityLock ||
                    row.Target == (int)ProjectileTargetKind.EntityLock && row.Motion != (int)ProjectileMotionKind.Straight)
                    return Fail("飞弹运动与目标模式冲突：" + row.ID, out error);
                if (!ValidateEffectIds(row.OnHitEffectIDs, "飞弹命中 " + row.ID, out error) ||
                    !ValidateEffectIds(row.OnArriveEffectIDs, "飞弹抵达 " + row.ID, out error) ||
                    !ValidateEffectIds(row.OnExpireEffectIDs, "飞弹到期 " + row.ID, out error)) return false;
            }

            foreach (CfgCombatBuffsRow row in GameTables.CfgCombatBuffs.GetAll())
            {
                if (row.ID <= 0 || row.DurationTicks <= 0 || row.IntervalTicks <= 0 || row.MaxStacks <= 0 ||
                    row.Merge < 0 || row.Merge > (int)CombatBuffMerge.ResetDuration ||
                    !Finite(row.MoveSpeedAddPerStack) || !Finite(row.AttackRangeAddPerStack) ||
                    !FiniteNonnegative(row.PeriodicDamagePerStack))
                    return Fail("Buff 参数无效：" + row.ID, out error);
                if (!ValidateEffectIds(row.OnApplyEffectIDs, "Buff 应用 " + row.ID, out error) ||
                    !ValidateEffectIds(row.OnTickEffectIDs, "Buff 周期 " + row.ID, out error) ||
                    !ValidateEffectIds(row.OnExpireEffectIDs, "Buff 到期 " + row.ID, out error)) return false;
            }

            CfgCombatAnimationSettingsRow settings = GameTables.CfgCombatAnimationSettings.GetById(1);
            if (settings == null || string.IsNullOrWhiteSpace(settings.IdleState) ||
                string.IsNullOrWhiteSpace(settings.MoveState) || string.IsNullOrWhiteSpace(settings.HitState) ||
                string.IsNullOrWhiteSpace(settings.DeathState) || !FinitePositive(settings.IdleSeconds) ||
                !FinitePositive(settings.MoveSeconds) || settings.HitTicks <= 0 || settings.DeathTicks <= 0 ||
                !FiniteNonnegative(settings.DeathHoldSeconds))
                return Fail("动画设置无效。", out error);
            foreach (CfgCombatAnimationCuesRow row in GameTables.CfgCombatAnimationCues.GetAll())
                if (row.ID <= 0 || string.IsNullOrWhiteSpace(row.StateName) || string.IsNullOrWhiteSpace(row.StatePath))
                    return Fail("动画 Cue 映射无效：" + row.ID, out error);
            foreach (CfgCombatEffectsRow row in GameTables.CfgCombatEffects.GetAll())
                if (row.Kind == (int)CombatEffectKind.Cue && GameTables.CfgCombatAnimationCues.GetById(row.DefinitionID) == null)
                    return Fail("Cue 缺少动画映射：" + row.DefinitionID, out error);
            return true;
        }

        /// <summary>
        /// 按表构建独立战斗目录，装备攻击范围用于动态半径与起手距离。
        /// </summary>
        /// <param name="attackRange">装备计算后的攻击范围。</param>
        /// <returns>本局独立战斗目录。</returns>
        public static CombatCatalog CreateCatalog(float attackRange)
        {
            if (float.IsNaN(attackRange) || float.IsInfinity(attackRange) || attackRange <= 0)
                throw new ArgumentOutOfRangeException(nameof(attackRange));
            if (!Validate(out string error)) throw new InvalidOperationException(error);
            var catalog = new CombatCatalog { InitialAttackRange = attackRange };
            foreach (CfgCombatSkillsRow row in GameTables.CfgCombatSkills.GetAll())
                catalog.Skills.Add(row.ID, new CombatSkillDefinition
                {
                    CombatSkillId = row.ID, DurationTicks = row.DurationTicks, CooldownTicks = row.CooldownTicks,
                    StartRange = (float)row.StartRange + (row.StartRangeSource == 1 ? attackRange : 0),
                    AttackRangeSource = row.StartRangeSource == 1
                });
            var nodes = new List<CfgCombatTimelineNodesRow>(GameTables.CfgCombatTimelineNodes.GetAll());
            nodes.Sort((left, right) =>
            {
                int comparison = left.SkillID.CompareTo(right.SkillID);
                if (comparison == 0) comparison = left.Tick.CompareTo(right.Tick);
                return comparison == 0 ? left.ID.CompareTo(right.ID) : comparison;
            });
            foreach (CfgCombatTimelineNodesRow row in nodes)
            {
                var node = new CombatTimelineNode { Tick = row.Tick };
                AddEffects(node.Effects, row.EffectIDs, attackRange);
                catalog.Skills[row.SkillID].Nodes.Add(node);
            }
            foreach (CfgCombatProjectilesRow row in GameTables.CfgCombatProjectiles.GetAll())
            {
                var projectile = new CombatProjectileDefinition
                {
                    ProjectileId = row.ID, Motion = (ProjectileMotionKind)row.Motion, Target = (ProjectileTargetKind)row.Target,
                    SpawnAnchor = (ProjectileSpawnAnchor)row.SpawnAnchor, LostTarget = (ProjectileLostTarget)row.LostTarget,
                    SpawnOffset = new Vector3((float)row.SpawnOffsetX, (float)row.SpawnOffsetY, (float)row.SpawnOffsetZ),
                    Speed = (float)row.Speed, Acceleration = (float)row.Acceleration, MaxSpeed = (float)row.MaxSpeed,
                    TurnDegreesPerSecond = (float)row.TurnDegreesPerSecond,
                    Gravity = new Vector3((float)row.GravityX, (float)row.GravityY, (float)row.GravityZ),
                    ArcHeight = (float)row.ArcHeight, FlightTicks = row.FlightTicks, LifetimeTicks = row.LifetimeTicks,
                    MaxDistance = (float)row.MaxDistance, Radius = (float)row.Radius,
                    MaxDistinctTargets = row.MaxDistinctTargets, MaxBounces = row.MaxBounces,
                    SameTargetIntervalTicks = row.SameTargetIntervalTicks, MaxHitsPerTarget = row.MaxHitsPerTarget,
                    OnlyLockedTarget = row.OnlyLockedTarget, RemoveOnSourceDeath = row.RemoveOnSourceDeath
                };
                AddEffects(projectile.OnHit, row.OnHitEffectIDs, attackRange);
                AddEffects(projectile.OnArrive, row.OnArriveEffectIDs, attackRange);
                AddEffects(projectile.OnExpire, row.OnExpireEffectIDs, attackRange);
                catalog.Projectiles.Add(row.ID, projectile);
            }
            foreach (CfgCombatBuffsRow row in GameTables.CfgCombatBuffs.GetAll())
            {
                var buff = new CombatBuffDefinition
                {
                    BuffId = row.ID, DurationTicks = row.DurationTicks, IntervalTicks = row.IntervalTicks,
                    MaxStacks = row.MaxStacks, Merge = (CombatBuffMerge)row.Merge,
                    MoveSpeedAddPerStack = (float)row.MoveSpeedAddPerStack,
                    AttackRangeAddPerStack = (float)row.AttackRangeAddPerStack,
                    PeriodicDamagePerStack = (float)row.PeriodicDamagePerStack
                };
                AddEffects(buff.OnApply, row.OnApplyEffectIDs, attackRange);
                AddEffects(buff.OnTick, row.OnTickEffectIDs, attackRange);
                AddEffects(buff.OnExpire, row.OnExpireEffectIDs, attackRange);
                catalog.Buffs.Add(row.ID, buff);
            }
            return catalog;
        }

        /// <summary>
        /// 从配置编号创建独立效果定义，任一编号缺失时整体失败。
        /// </summary>
        /// <param name="ids">按执行顺序排列的效果配置编号。</param>
        /// <param name="attackRange">用于动态半径初值的装备攻击范围。</param>
        /// <param name="effects">成功时返回独立效果列表。</param>
        /// <returns>范围和全部编号有效且映射成功时为真。</returns>
        public static bool TryCreateEffects(int[] ids, float attackRange, out List<CombatEffectDefinition> effects)
        {
            effects = null;
            if (ids == null || !FinitePositive(attackRange)) return false;
            foreach (int id in ids)
                if (id <= 0 || GameTables.CfgCombatEffects.GetById(id) == null) return false;
            var created = new List<CombatEffectDefinition>(ids.Length);
            AddEffects(created, ids, attackRange);
            effects = created;
            return true;
        }

        /// <summary>
        /// 为目标列表创建独立效果定义。
        /// </summary>
        /// <param name="destination">目标效果列表。</param>
        /// <param name="ids">效果配置编号。</param>
        /// <param name="attackRange">装备计算后的攻击范围。</param>
        private static void AddEffects(List<CombatEffectDefinition> destination, int[] ids, float attackRange)
        {
            if (ids == null) return;
            foreach (int id in ids)
            {
                CfgCombatEffectsRow row = GameTables.CfgCombatEffects.GetById(id);
                destination.Add(new CombatEffectDefinition
                {
                    Kind = (CombatEffectKind)row.Kind, Attribute = (CombatAttributeKind)row.Attribute,
                    DefinitionId = row.DefinitionID, Value = (float)row.Value,
                    AttackCoefficient = (float)row.AttackCoefficient, CanCritical = row.CanCritical,
                    CanDodge = row.CanDodge, DurationTicks = row.DurationTicks, LinkedId = row.LinkedID,
                    Shape = (CombatHitShape)row.Shape,
                    Radius = (float)row.Radius + (row.RadiusSource == 1 ? attackRange : 0),
                    AttackRangeSource = row.RadiusSource == 1,
                    Angle = (float)row.Angle, Width = (float)row.Width, Height = (float)row.Height,
                    RequireLineOfSight = row.RequireLineOfSight
                });
            }
        }

        /// <summary>
        /// 校验效果编号列表中的全部外键。
        /// </summary>
        /// <param name="ids">效果配置编号。</param>
        /// <param name="owner">引用来源。</param>
        /// <param name="error">失败原因。</param>
        /// <returns>全部引用存在则为真。</returns>
        private static bool ValidateEffectIds(int[] ids, string owner, out string error)
        {
            error = null;
            if (ids == null) return Fail(owner + " 的效果编号数组缺失。", out error);
            foreach (int id in ids)
                if (GameTables.CfgCombatEffects.GetById(id) == null)
                    return Fail(owner + " 引用了不存在的效果：" + id, out error);
            return true;
        }

        /// <summary>
        /// 返回校验失败并写入原因。
        /// </summary>
        /// <param name="message">失败原因。</param>
        /// <param name="error">输出失败原因。</param>
        /// <returns>恒为假。</returns>
        private static bool Fail(string message, out string error) { error = message; return false; }

        /// <summary>
        /// 判断数值可安全转换为有限 float。
        /// </summary>
        /// <param name="value">待验证数值。</param>
        /// <returns>数值有效则为真。</returns>
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value) && Math.Abs(value) <= float.MaxValue;

        /// <summary>
        /// 判断数值为非负有限 float。
        /// </summary>
        /// <param name="value">待验证数值。</param>
        /// <returns>数值有效则为真。</returns>
        private static bool FiniteNonnegative(double value) => Finite(value) && value >= 0;

        /// <summary>
        /// 判断数值为正有限 float。
        /// </summary>
        /// <param name="value">待验证数值。</param>
        /// <returns>数值有效则为真。</returns>
        private static bool FinitePositive(double value) => Finite(value) && value > 0;
    }
}
