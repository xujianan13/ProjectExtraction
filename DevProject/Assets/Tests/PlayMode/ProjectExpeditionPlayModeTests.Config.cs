using System.Collections;
using System.Linq;
using Cysharp.Threading.Tasks;
using GameFramework;
using GameLogic.Gameplay.Combat;
using GameLogic.Gameplay.Expedition;
using GameLogic.Project;
using GameLogic.Tables;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MiniGame.Tests
{
    public sealed partial class ProjectExpeditionPlayModeTests
    {
        /// <summary>
        /// 实际运行从关卡配置读取区域数量，并由整图资源提供几何和出生点。
        /// </summary>
        /// <returns>配置、地图资源与角色实体接入验证任务。</returns>
        [UnityTest]
        public IEnumerator ConfiguredRegionsAndActorsReachRuntime() => UniTask.ToCoroutine(async () =>
        {
            await SetupAsync(new MemorySaveBackend());
            Assert.That(ProjectContentConfig.Validate(out string error), Is.True, error);
            Assert.That(await ProjectServices.Coordinator.StartRunAsync(1), Is.True);
            ExpeditionRun run = ProjectServices.Session.CurrentRun;
            Assert.That(run.RegionCount, Is.EqualTo(GameTables.CfgLevels.GetById(1).RegionCount));
            Assert.That(run.RegionView, Is.Not.Null);
            Assert.That(run.CurrentMap.MapId, Is.EqualTo(run.CurrentRegion.Plan.Request.MapId));
            Assert.That(run.World.TryGetActor(run.Player, out CombatActorSnapshot player), Is.True);
            Assert.That(player.Position, Is.EqualTo(run.CurrentRegion.Plan.EntryPosition));
            Assert.That(run.World.TrySetPosition(run.Player, new Vector3(run.CurrentMap.Bounds.xMax + 1, 0, 0)), Is.False);
            foreach (var slot in run.CurrentRegion.Slots)
            {
                Assert.That(run.World.TryFindActor(slot.CurrentActorId, out CombatEntityRef actor), Is.True);
                Assert.That(run.World.TryGetActor(actor, out CombatActorSnapshot enemy), Is.True);
                var plan = run.CurrentRegion.Plan.SpawnSlots.Single(value => value.SlotId == slot.SlotId);
                Assert.That(enemy.RoomId, Is.EqualTo(run.CurrentRegion.Plan.Request.RegionId));
                Assert.That(enemy.Attack, Is.EqualTo(GameTables.CfgEnemies.GetById(plan.EnemyId).Attack).Within(0.001));
            }
        });
    }
}
