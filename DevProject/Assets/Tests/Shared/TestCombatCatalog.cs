using GameLogic.Gameplay.Combat;

namespace ProjectExtraction.Tests.Shared
{
    /// <summary>
    /// 仅供机制测试构造隔离数据，正式玩法必须通过内容配置创建战斗目录。
    /// </summary>
    public static class TestCombatCatalog
    {
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
