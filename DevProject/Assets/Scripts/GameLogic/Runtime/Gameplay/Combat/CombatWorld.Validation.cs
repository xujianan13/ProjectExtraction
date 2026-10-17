using UnityEngine;

namespace GameLogic.Gameplay.Combat
{
    /// <summary>
    /// 战斗配置的运行入口校验。
    /// </summary>
    public sealed partial class CombatWorld
    {
        /// <summary>
        /// 校验飞弹定义的数值和模式边界。
        /// </summary>
        /// <param name="definition">飞弹定义。</param>
        /// <returns>参数可用于确定性模拟则为真。</returns>
        private static bool ValidProjectile(CombatProjectileDefinition definition)
        {
            if (definition == null || definition.ProjectileId <= 0 || definition.Motion < ProjectileMotionKind.Straight || definition.Motion > ProjectileMotionKind.Orbit ||
                definition.LostTarget < ProjectileLostTarget.ContinueDirection || definition.LostTarget > ProjectileLostTarget.Reacquire || !IsFinite(definition.SpawnOffset) ||
                !IsFinite(definition.Gravity) || !IsFinite(definition.Speed) || !IsFinite(definition.Acceleration) ||
                !IsFinite(definition.MaxSpeed) || !IsFinite(definition.TurnDegreesPerSecond) ||
                !IsFinite(definition.ArcHeight) || !IsFinite(definition.MaxDistance) ||
                !IsFinite(definition.Radius) || definition.Radius < 0f ||
                definition.MaxDistinctTargets <= 0 || definition.MaxHitsPerTarget <= 0 ||
                definition.SameTargetIntervalTicks < 0 || definition.MaxBounces < 0) return false;
            if (definition.Motion == ProjectileMotionKind.Instant)
                return definition.FlightTicks == 0 && definition.Acceleration == 0f &&
                    definition.TurnDegreesPerSecond == 0f;
            if (!IsFinite(definition.ReacquireRange) || definition.ReacquireRange < 0f ||
                !IsFinite(definition.OrbitRadius) || !IsFinite(definition.OrbitDegreesPerSecond) || !IsFinite(definition.OrbitInitialAngle)) return false;
            if (definition.Motion == ProjectileMotionKind.Orbit)
                return definition.OrbitRadius > 0f && definition.SameTargetIntervalTicks > 0 && definition.Target == ProjectileTargetKind.Direction;
            if (definition.Speed <= 0f || definition.LifetimeTicks <= 0 || definition.MaxDistance <= 0f) return false;
            if (definition.Motion == ProjectileMotionKind.FixedEndpointArc &&
                (definition.Acceleration != 0f || definition.Target == ProjectileTargetKind.EntityLock ||
                 definition.MaxBounces > 0)) return false;
            if (definition.Motion == ProjectileMotionKind.GravityArc &&
                definition.Target == ProjectileTargetKind.EntityLock) return false;
            if (definition.Target == ProjectileTargetKind.EntityLock &&
                definition.Motion != ProjectileMotionKind.Straight) return false;
            return true;
        }

        /// <summary>
        /// 校验 Buff 的周期、层数与属性数值。
        /// </summary>
        /// <param name="definition">Buff 定义。</param>
        /// <returns>配置合法则为真。</returns>
        private static bool ValidBuff(CombatBuffDefinition definition) => definition != null &&
            definition.BuffId > 0 && definition.Lifetime >= CombatBuffLifetime.Timed && definition.Lifetime <= CombatBuffLifetime.Persistent && (definition.Lifetime == CombatBuffLifetime.Persistent || definition.DurationTicks > 0) && definition.IntervalTicks > 0 &&
            definition.MaxStacks > 0 && IsFinite(definition.MoveSpeedAddPerStack) &&
            IsFinite(definition.AttackRangeAddPerStack);

        /// <summary>
        /// 校验统一效果的必要数值和引用。
        /// </summary>
        /// <param name="effect">效果定义。</param>
        /// <returns>效果参数合法则为真。</returns>
        private static bool ValidEffect(CombatEffectDefinition effect)
        {
            if (effect == null || effect.Kind < CombatEffectKind.Cue || effect.Kind > CombatEffectKind.Heal ||
                !IsFinite(effect.Value) || !IsFinite(effect.AttackCoefficient) || !IsFinite(effect.Radius) ||
                !IsFinite(effect.AttackRangeMultiplier) || effect.AttackRangeMultiplier < 0f ||
                effect.Shape < CombatHitShape.Circle || effect.Shape > CombatHitShape.ForwardBox || !IsFinite(effect.Angle) || !IsFinite(effect.Width) || !IsFinite(effect.Height) ||
                effect.Count <= 0 || effect.Count > 64 || !IsFinite(effect.SpreadDegrees) ||
                effect.Radius < 0f || effect.Width < 0f || effect.Height < 0f) return false;
            if ((effect.Kind == CombatEffectKind.Damage || effect.Kind == CombatEffectKind.Heal) &&
                (effect.Value < 0f || effect.AttackCoefficient < 0f)) return false;
            if (effect.Kind == CombatEffectKind.Displace && effect.DurationTicks <= 0) return false;
            if (effect.Kind == CombatEffectKind.ModifyAttribute &&
                (effect.Value <= 0f || !ValidAttribute(effect.Attribute))) return false;
            if ((effect.Kind == CombatEffectKind.SpawnProjectile || effect.Kind == CombatEffectKind.ApplyBuff) &&
                effect.LinkedId <= 0) return false;
            return true;
        }
    }
}
