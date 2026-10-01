using System.Collections.Generic;
using GameLogic.Gameplay.Combat;
using NUnit.Framework;
using UnityEngine;

namespace ProjectExtraction.Tests.Editor.Combat
{
    /// <summary>
    /// 验证逐 tick 回调和暂停时间边界。
    /// </summary>
    public sealed class CombatWorldAdvanceTests
    {
        /// <summary>
        /// 回调能观察完整 tick，拒绝后同帧剩余 tick 不再执行。
        /// </summary>
        [Test]
        public void AfterTickCanStopRemainingTicks()
        {
            var world = CreateWorld();
            var observed = new List<long>();
            int stepped = world.Advance(CombatWorld.TickSeconds * 4f, () =>
            {
                observed.Add(world.TickIndex);
                return false;
            });
            Assert.That(stepped, Is.EqualTo(1));
            Assert.That(world.TickIndex, Is.EqualTo(1));
            Assert.That(observed, Is.EqualTo(new long[] { 1 }));
            world.ClearPendingTime();
            Assert.That(world.Advance(CombatWorld.TickSeconds), Is.EqualTo(1));
            Assert.That(world.TickIndex, Is.EqualTo(2));
            world.Dispose();
        }

        /// <summary>
        /// 清空积累时间后仅执行新的完整 tick。
        /// </summary>
        [Test]
        public void ClearPendingTimeDiscardsPausedFraction()
        {
            var world = CreateWorld();
            Assert.That(world.Advance(CombatWorld.TickSeconds * 0.75f), Is.EqualTo(0));
            world.ClearPendingTime();
            Assert.That(world.Advance(CombatWorld.TickSeconds * 0.5f), Is.EqualTo(0));
            Assert.That(world.Advance(CombatWorld.TickSeconds * 0.5f), Is.EqualTo(1));
            Assert.That(world.TickIndex, Is.EqualTo(1));
            world.Dispose();
        }

        /// <summary>
        /// 建立仅包含玩家的可推进战斗世界。
        /// </summary>
        /// <returns>已设置活动地图的战斗世界。</returns>
        private static CombatWorld CreateWorld()
        {
            var world = new CombatWorld(ProjectExtraction.Tests.Shared.TestCombatCatalog.CreatePrototype());
            world.SetRoomBounds(1, new Rect(-10f, -10f, 20f, 20f));
            var player = world.CreateActor(new CombatActorSpec
            {
                ActorId = 1, RoomId = 1, Team = 1, IsPlayer = true, Position = Vector3.zero,
                MaxHealth = 100f, MoveSpeed = 0f, Radius = 0.3f
            });
            Assert.That(world.SetActiveRoom(1, player), Is.True);
            return world;
        }
    }
}
