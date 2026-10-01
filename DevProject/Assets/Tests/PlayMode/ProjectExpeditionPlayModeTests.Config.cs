using System.Collections;
using System.Collections.Generic;
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
    /// <summary>
    /// 验证真实表参数能驱动房间表现和战斗实体。
    /// </summary>
    public sealed partial class ProjectExpeditionPlayModeTests
    {
        /// <summary>
        /// 配置房间尺寸、网格、玩家碰撞和敌人攻击后，新局读取对应参数。
        /// </summary>
        /// <returns>实际内容包资源参与的配置接入验证流程。</returns>
        [UnityTest]
        public IEnumerator ConfiguredRoomAndActorsReachRuntime() => UniTask.ToCoroutine(async () =>
        {
            await SetupAsync(new MemorySaveBackend());
            var layout = GameTables.CfgRoomLayouts.GetById(1);
            var level = GameTables.CfgLevels.GetById(1);
            var enemy = GameTables.CfgEnemies.GetById(1);
            var player = GameTables.CfgPlayerCombat.GetById(1);
            double extent = layout.HalfExtent, attack = enemy.Attack, radius = player.Radius;
            int width = level.GridWidth, height = level.GridHeight;
            try
            {
                layout.HalfExtent = 13;
                level.GridWidth = 4;
                level.GridHeight = 3;
                enemy.Attack = 17;
                player.Radius = 0.6;
                Assert.That(ProjectContentConfig.Validate(out string error), Is.True, error);
                using var scope = new LifetimeScope("ConfiguredRuntime");
                ExpeditionVisualAssets assets = await ExpeditionVisualAssets.LoadAsync(scope, GameEntry.Get<IResourceModule>());
                Assert.That(assets, Is.Not.Null);
                Assert.That(ExpeditionRun.TryCreate(ProjectServices.Session, "config-integration", 1, 42,
                    new List<ProjectItem>(), out ExpeditionRun run), Is.True);
                using var ownedRun = run;
                using var presentation = new ExpeditionPresentation(run, assets);
                run.AttachScene(assets, presentation.Root);
                Assert.That(run.Maps.Rooms.Count, Is.EqualTo(12));
                Assert.That(run.RoomView.Root.transform.Find("Floor").GetComponent<Renderer>().bounds.size.x, Is.EqualTo(26f).Within(0.001f));
                Assert.That(run.World.TryGetActor(run.Player, out CombatActorSnapshot actor), Is.True);
                Assert.That(run.World.TrySetPosition(run.Player, new Vector3(12.15f, 0, 0)), Is.False, "配置半径需要参与墙体碰撞。");
                var actors = new List<CombatActorSnapshot>();
                run.World.GetActorsSnapshot(actors);
                Assert.That(actors.Where(value => value.ActorId != 1).All(value => value.Attack == 17f), Is.True);
                Assert.That(actors.Count, Is.GreaterThan(1));
            }
            finally
            {
                layout.HalfExtent = extent;
                level.GridWidth = width;
                level.GridHeight = height;
                enemy.Attack = attack;
                player.Radius = radius;
            }
        });
    }
}
