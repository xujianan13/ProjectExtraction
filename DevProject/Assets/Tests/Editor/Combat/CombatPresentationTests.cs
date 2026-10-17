using System.Collections.Generic;
using GameLogic.Gameplay.Combat;
using NUnit.Framework;
using UnityEngine;

namespace ProjectExtraction.Tests.Editor.Combat
{
    /// <summary>
    /// 验证战斗预警只读快照与施法时间轴及飞弹出生点一致。
    /// </summary>
    public sealed class CombatPresentationTests
    {
        private readonly List<CombatWorld> worlds = new List<CombatWorld>();
        private readonly List<CombatTelegraphSnapshot> snapshots = new List<CombatTelegraphSnapshot>();

        /// <summary>
        /// 释放本测试创建的战斗世界。
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            foreach (CombatWorld world in worlds) world.Dispose();
            worlds.Clear();
        }

        /// <summary>
        /// 按时间轴依次预警下一次伤害节点，并保留形状、范围、方向与进度。
        /// </summary>
        [Test]
        public void DamageNode_UsesNextFutureHitAndConfiguredShape()
        {
            var catalog = new CombatCatalog();
            var skill = new CombatSkillDefinition { CombatSkillId = 1, DurationTicks = 12, StartRange = 6f };
            var cue = new CombatTimelineNode { Tick = 2 };
            cue.Effects.Add(new CombatEffectDefinition { Kind = CombatEffectKind.Cue, DefinitionId = 1 });
            var firstHit = new CombatTimelineNode { Tick = 5 };
            firstHit.Effects.Add(new CombatEffectDefinition
            {
                Kind = CombatEffectKind.Damage, Value = 0f, Shape = CombatHitShape.Box, Radius = 2.5f, Width = 0.8f, Angle = 75f
            });
            var secondHit = new CombatTimelineNode { Tick = 9 };
            secondHit.Effects.Add(new CombatEffectDefinition
            {
                Kind = CombatEffectKind.Damage, Value = 0f, Shape = CombatHitShape.Sector, Radius = 3f, Angle = 110f
            });
            skill.Nodes.Add(cue);
            skill.Nodes.Add(firstHit);
            skill.Nodes.Add(secondHit);
            catalog.Skills.Add(1, skill);
            CombatWorld world = CreateWorld(catalog);
            CombatEntityRef player = CreateActor(world, 1, 1, true, Vector3.zero);
            Assert.That(world.SetActiveRoom(1, player), Is.True);
            Assert.That(world.TryStartSkill(player, 1, default), Is.True);

            CombatTelegraphSnapshot initial = SingleSnapshot(world);
            Assert.That(initial.ActorId, Is.EqualTo(1));
            Assert.That(initial.RoomId, Is.EqualTo(1));
            Assert.That(initial.IsPlayer, Is.True);
            Assert.That(initial.IsProjectile, Is.False);
            Assert.That(initial.Origin, Is.EqualTo(Vector3.zero));
            Assert.That(initial.Direction, Is.EqualTo(Vector3.forward));
            Assert.That(initial.Shape, Is.EqualTo(CombatHitShape.Box));
            Assert.That(initial.Radius, Is.EqualTo(2.5f));
            Assert.That(initial.Width, Is.EqualTo(0.8f));
            Assert.That(initial.Angle, Is.EqualTo(75f));
            Assert.That(initial.Progress, Is.Zero);

            for (int tick = 0; tick < 2; tick++) world.Step();
            Assert.That(SingleSnapshot(world).Progress, Is.EqualTo(0.4f).Within(0.0001f));
            for (int tick = 0; tick < 3; tick++) world.Step();
            CombatTelegraphSnapshot next = SingleSnapshot(world);
            Assert.That(next.Cast, Is.EqualTo(initial.Cast));
            Assert.That(next.Shape, Is.EqualTo(CombatHitShape.Sector));
            Assert.That(next.Radius, Is.EqualTo(3f));
            Assert.That(next.Angle, Is.EqualTo(110f));
            Assert.That(next.Progress, Is.EqualTo(5f / 9f).Within(0.0001f));
            for (int tick = 0; tick < 4; tick++) world.Step();
            world.GetTelegraphsSnapshot(snapshots);
            Assert.That(snapshots, Is.Empty);
        }

        /// <summary>
        /// 换图取消玩家施法后不再输出预警；施法者死亡同样清除预警。
        /// </summary>
        [Test]
        public void CancellationAndDeath_ClearTelegraphs()
        {
            var catalog = new CombatCatalog();
            AddDamageSkill(catalog, 1, 8, 0f);
            AddDamageSkill(catalog, 2, 1, 100f);
            CombatWorld world = CreateWorld(catalog);
            CombatEntityRef player = CreateActor(world, 1, 1, true, Vector3.zero);
            CombatEntityRef enemy = CreateActor(world, 2, 1, false, new Vector3(0f, 0f, 1f), 20f);
            Assert.That(world.SetActiveRoom(1, player), Is.True);
            Assert.That(world.TryStartSkill(player, 1, default), Is.True);
            Assert.That(SingleSnapshot(world).ActorId, Is.EqualTo(1));
            Assert.That(world.SetActiveRoom(2, player), Is.True);
            world.GetTelegraphsSnapshot(snapshots);
            Assert.That(snapshots, Is.Empty);
            Assert.That(world.SetActiveRoom(1, player), Is.True);
            world.GetTelegraphsSnapshot(snapshots);
            Assert.That(snapshots, Is.Empty);

            Assert.That(world.TryStartSkill(enemy, 1, default), Is.True);
            Assert.That(SingleSnapshot(world).ActorId, Is.EqualTo(2));
            Assert.That(world.TryStartSkill(player, 2, enemy), Is.False, "同一帧取消动作不会重新获得动作接纳机会。");
            world.Step();
            Assert.That(world.TryStartSkill(player, 2, enemy), Is.True);
            world.Step();
            Assert.That(world.TryGetActor(enemy, out CombatActorSnapshot dead), Is.True);
            Assert.That(dead.Dead, Is.True);
            world.GetTelegraphsSnapshot(snapshots);
            Assert.That(snapshots, Is.Empty);
        }

        /// <summary>
        /// 非活动房间的敌人预警不输出，回访后进度沿冻结的房间时钟继续。
        /// </summary>
        [Test]
        public void EnemyTelegraph_FreezesOutsideActiveRoomAndReturnsOnVisit()
        {
            var catalog = new CombatCatalog();
            AddDamageSkill(catalog, 1, 8, 0f);
            CombatWorld world = CreateWorld(catalog);
            CombatEntityRef player = CreateActor(world, 1, 1, true, Vector3.zero);
            CombatEntityRef enemy = CreateActor(world, 2, 1, false, new Vector3(0f, 0f, 2f));
            Assert.That(world.SetActiveRoom(1, player), Is.True);
            Assert.That(world.TryStartSkill(enemy, 1, default), Is.True);
            world.Step();
            world.Step();
            CombatTelegraphSnapshot before = SingleSnapshot(world);
            Assert.That(before.Progress, Is.EqualTo(0.25f).Within(0.0001f));
            Assert.That(world.SetActiveRoom(2, player), Is.True);
            for (int tick = 0; tick < 20; tick++) world.Step();
            world.GetTelegraphsSnapshot(snapshots);
            Assert.That(snapshots, Is.Empty);
            Assert.That(world.SetActiveRoom(1, player), Is.True);
            CombatTelegraphSnapshot returned = SingleSnapshot(world);
            Assert.That(returned.Cast, Is.EqualTo(before.Cast));
            Assert.That(returned.Progress, Is.EqualTo(before.Progress));
            world.Step();
            Assert.That(SingleSnapshot(world).Progress, Is.EqualTo(0.375f).Within(0.0001f));
        }

        /// <summary>
        /// 来源锚点按施法方向旋转局部出生偏移，并导出飞弹预警范围。
        /// </summary>
        [Test]
        public void SourceProjectile_RotatesSpawnOffsetWithCastForward()
        {
            var catalog = ProjectileCatalog(ProjectileSpawnAnchor.Source);
            CombatWorld world = CreateWorld(catalog);
            CombatEntityRef player = CreateActor(world, 1, 1, true, Vector3.zero, 100f, 6f);
            Assert.That(world.SetActiveRoom(1, player), Is.True);
            Assert.That(world.SetMove(player, Vector2.right), Is.True);
            world.Step();
            Assert.That(world.SetMove(player, Vector2.zero), Is.True);
            Assert.That(world.TryGetActor(player, out CombatActorSnapshot actor), Is.True);
            Assert.That(world.TryStartSkill(player, 1, default), Is.True);
            CombatTelegraphSnapshot snapshot = SingleSnapshot(world);
            Assert.That(snapshot.IsProjectile, Is.True);
            Assert.That(snapshot.Origin.x, Is.EqualTo(actor.Position.x + 1f).Within(0.0001f));
            Assert.That(snapshot.Origin.z, Is.EqualTo(actor.Position.z).Within(0.0001f));
            Assert.That(snapshot.Direction, Is.EqualTo(Vector3.right));
            Assert.That(snapshot.Shape, Is.EqualTo(CombatHitShape.Capsule));
            Assert.That(snapshot.Radius, Is.EqualTo(6f));
            Assert.That(snapshot.Width, Is.EqualTo(0.5f));
        }

        /// <summary>
        /// 目标点锚保持施法时位置，目标实体锚跟随当前位置，均旋转出生偏移。
        /// </summary>
        /// <param name="anchor">待验证的目标锚点。</param>
        /// <param name="expectedX">预期出生点的 X 坐标。</param>
        /// <param name="expectedZ">预期出生点的 Z 坐标。</param>
        [TestCase(ProjectileSpawnAnchor.TargetPointSnapshot, 4f, 0f)]
        [TestCase(ProjectileSpawnAnchor.TargetEntityAtSpawn, 5f, 1f)]
        public void TargetProjectile_UsesChosenAnchorAndRotatedOffset(ProjectileSpawnAnchor anchor, float expectedX, float expectedZ)
        {
            CombatWorld world = CreateWorld(ProjectileCatalog(anchor));
            CombatEntityRef player = CreateActor(world, 1, 1, true, Vector3.zero, 100f, 6f);
            CombatEntityRef target = CreateActor(world, 2, 1, false, new Vector3(3f, 0f, 0f));
            Assert.That(world.SetActiveRoom(1, player), Is.True);
            Assert.That(world.SetMove(player, Vector2.right), Is.True);
            world.Step();
            Assert.That(world.SetMove(player, Vector2.zero), Is.True);
            Assert.That(world.TryStartSkill(player, 1, target), Is.True);
            Assert.That(world.TrySetPosition(target, new Vector3(4f, 0f, 1f)), Is.True);
            CombatTelegraphSnapshot snapshot = SingleSnapshot(world);
            Assert.That(snapshot.IsProjectile, Is.True);
            Assert.That(snapshot.Shape, Is.EqualTo(CombatHitShape.Circle));
            Assert.That(snapshot.Radius, Is.EqualTo(0.25f));
            Assert.That(snapshot.Origin.x, Is.EqualTo(expectedX).Within(0.0001f));
            Assert.That(snapshot.Origin.z, Is.EqualTo(expectedZ).Within(0.0001f));
        }

        /// <summary>
        /// 创建包含一次飞弹发射及旋转偏移的测试配置。
        /// </summary>
        /// <param name="anchor">飞弹出生锚点。</param>
        /// <returns>技能与飞弹定义完整的目录。</returns>
        private static CombatCatalog ProjectileCatalog(ProjectileSpawnAnchor anchor)
        {
            var catalog = new CombatCatalog();
            var skill = new CombatSkillDefinition { CombatSkillId = 1, DurationTicks = 12, StartRange = 6f };
            var node = new CombatTimelineNode { Tick = 8 };
            node.Effects.Add(new CombatEffectDefinition { Kind = CombatEffectKind.SpawnProjectile, LinkedId = 1 });
            skill.Nodes.Add(node);
            catalog.Skills.Add(1, skill);
            catalog.Projectiles.Add(1, new CombatProjectileDefinition
            {
                ProjectileId = 1, Motion = ProjectileMotionKind.Straight, Target = ProjectileTargetKind.Direction,
                SpawnAnchor = anchor, SpawnOffset = Vector3.forward, Speed = 10f, MaxSpeed = 10f,
                MaxDistance = 12f, Radius = 0.25f
            });
            return catalog;
        }

        /// <summary>
        /// 为指定 tick 添加一个直接伤害技能。
        /// </summary>
        /// <param name="catalog">目标目录。</param>
        /// <param name="id">技能编号。</param>
        /// <param name="tick">伤害节点 tick。</param>
        /// <param name="value">伤害值。</param>
        private static void AddDamageSkill(CombatCatalog catalog, int id, int tick, float value)
        {
            var skill = new CombatSkillDefinition { CombatSkillId = id, DurationTicks = tick + 2, StartRange = 6f };
            var node = new CombatTimelineNode { Tick = tick };
            node.Effects.Add(new CombatEffectDefinition
            {
                Kind = CombatEffectKind.Damage, Value = value, Shape = CombatHitShape.Circle, Radius = 3f,
                CanCritical = false, CanDodge = false
            });
            skill.Nodes.Add(node);
            catalog.Skills.Add(id, skill);
        }

        /// <summary>
        /// 建立并登记战斗世界。
        /// </summary>
        /// <param name="catalog">战斗配置。</param>
        /// <returns>已设置房间边界的战斗世界。</returns>
        private CombatWorld CreateWorld(CombatCatalog catalog)
        {
            var world = new CombatWorld(catalog);
            world.SetRoomBounds(1, new Rect(-10f, -10f, 20f, 20f));
            world.SetRoomBounds(2, new Rect(-10f, -10f, 20f, 20f));
            worlds.Add(world);
            return world;
        }

        /// <summary>
        /// 创建不自动施法的测试角色。
        /// </summary>
        /// <param name="world">目标世界。</param>
        /// <param name="actorId">业务角色编号。</param>
        /// <param name="roomId">地图编号。</param>
        /// <param name="isPlayer">是否为玩家。</param>
        /// <param name="position">初始位置。</param>
        /// <param name="health">生命上限。</param>
        /// <param name="moveSpeed">移动速度。</param>
        /// <returns>新建角色实体引用。</returns>
        private static CombatEntityRef CreateActor(CombatWorld world, int actorId, int roomId, bool isPlayer, Vector3 position,
            float health = 100f, float moveSpeed = 0f) => world.CreateActor(new CombatActorSpec
            {
                ActorId = actorId, RoomId = roomId, Team = isPlayer ? 1 : 2, IsPlayer = isPlayer,
                Position = position, MaxHealth = health, MoveSpeed = moveSpeed, Radius = 0.3f
            });

        /// <summary>
        /// 要求当前世界仅有一条预警并返回它。
        /// </summary>
        /// <param name="world">待查询的战斗世界。</param>
        /// <returns>唯一的预警快照。</returns>
        private CombatTelegraphSnapshot SingleSnapshot(CombatWorld world)
        {
            world.GetTelegraphsSnapshot(snapshots);
            Assert.That(snapshots, Has.Count.EqualTo(1));
            return snapshots[0];
        }
    }
}
