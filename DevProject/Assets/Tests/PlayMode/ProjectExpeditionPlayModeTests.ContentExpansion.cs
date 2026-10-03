using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using GameFramework;
using GameLogic.Gameplay.Combat;
using GameLogic.Gameplay.Expedition;
using GameLogic.Gameplay.Level;
using GameLogic.Project;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MiniGame.Tests
{
    /// <summary>
    /// 验证新增房间和武器进入真实单局后的表现。
    /// </summary>
    public sealed partial class ProjectExpeditionPlayModeTests
    {
        /// <summary>
        /// 新布局的实际障碍及敌人遵循预抽计划，未清房回访保留原实体。
        /// </summary>
        /// <returns>加载正式资源并在实际房间间移动的测试流程。</returns>
        [UnityTest]
        public IEnumerator ExpandedRooms_SpawnPlannedEnemiesAndKeepThemOnRevisit() => UniTask.ToCoroutine(async () =>
        {
            await SetupAsync(new MemorySaveBackend());
            Assert.That(LevelContentConfig.TryGetLevel(3, out LevelDefinition level), Is.True);
            int selectedSeed = -1;
            for (int seed = 0; seed < 300; seed++)
            {
                if (!LevelGenerator.TryGenerate(seed, level, out LevelRunState plan)) continue;
                LevelRoomState[] expanded = plan.Rooms.Where(room => room.Type == LevelRoomType.Mob &&
                    (room.Plan.Definition.Layout.ID == 8 || room.Plan.Definition.Layout.ID == 9)).ToArray();
                if (expanded.Select(room => room.Plan.Definition.Layout.ID).Distinct().Count() == 2 &&
                    expanded.SelectMany(room => room.Plan.Spawns).Any(group => group.EnemyID == 5) &&
                    expanded.SelectMany(room => room.Plan.Spawns).Any(group => group.EnemyID == 6))
                { selectedSeed = seed; break; }
            }
            Assert.That(selectedSeed, Is.GreaterThanOrEqualTo(0), "固定种子窗口没有覆盖两种布局及新敌人。");
            using var scope = new LifetimeScope("ExpandedRoomsIntegration");
            ExpeditionVisualAssets assets = await ExpeditionVisualAssets.LoadAsync(scope, GameEntry.Get<IResourceModule>());
            Assert.That(ExpeditionRun.TryCreate(ProjectServices.Session, "expanded-rooms", 3, selectedSeed,
                new List<ProjectItem>(), out ExpeditionRun run), Is.True);
            using var ownedRun = run;
            using var presentation = new ExpeditionPresentation(run, assets);
            run.AttachScene(assets, presentation.Root);
            Assert.That(run.MarkReady() && run.Activate(), Is.True);
            var actors = new List<CombatActorSnapshot>();
            foreach (int layoutId in new[] { 8, 9 })
            {
                LevelRoomState room = run.Maps.Rooms.First(value => value.Type == LevelRoomType.Mob &&
                    value.Plan.Definition.Layout.ID == layoutId);
                MoveToRoom(run, room.RoomId);
                Assert.That(run.Layout.Obstacles.Count, Is.EqualTo(room.Plan.Definition.Obstacles.Count));
                Assert.That(run.Layout.Obstacles.Count, Is.GreaterThan(0));
                Assert.That(run.RoomView.Root.transform.Cast<Transform>().Count(child => child.name == "Obstacle"),
                    Is.EqualTo(run.Layout.Obstacles.Count));
                run.World.GetActorsSnapshot(actors);
                CombatActorSnapshot[] first = actors.Where(actor => actor.RoomId == room.RoomId && actor.ActorId != 1).ToArray();
                Assert.That(first.Length, Is.EqualTo(room.Plan.Spawns.Sum(group => group.Count)));
                foreach (CombatActorSnapshot actor in first)
                    Assert.That(run.TryGetEnemyDefinition(actor.ActorId, out EnemyDefinition enemy) &&
                        room.Plan.Spawns.Any(group => group.EnemyID == enemy.ID), Is.True);
                Assert.That(room.IsCleared, Is.False);
                MoveToRoom(run, room.Exits.Values.First());
                MoveToRoom(run, room.RoomId);
                run.World.GetActorsSnapshot(actors);
                CollectionAssert.AreEqual(first.Select(actor => actor.Entity).ToArray(),
                    actors.Where(actor => actor.RoomId == room.RoomId && actor.ActorId != 1).Select(actor => actor.Entity).ToArray());
                Assert.That(room.EnemiesSpawned, Is.True);
                Assert.That(room.IsCleared, Is.False);
            }
            Assert.That(run.World.Faulted, Is.False);
        });

        /// <summary>
        /// 实际换装改变基础技能一的预警范围，同时守卫刀的生命和防御进入 ECS。
        /// </summary>
        /// <returns>真实单局战斗范围与装备属性验证流程。</returns>
        [UnityTest]
        public IEnumerator ExpandedWeapons_UpdateBasicSkillRangeAndGuardStats() => UniTask.ToCoroutine(async () =>
        {
            await SetupAsync(new MemorySaveBackend());
            using var scope = new LifetimeScope("ExpandedWeaponsIntegration");
            ExpeditionVisualAssets assets = await ExpeditionVisualAssets.LoadAsync(scope, GameEntry.Get<IResourceModule>());
            ProjectItem heavy = ProjectEquipmentRules.CreateConfiguredItem(new System.Random(24), 1,
                ProjectItemQuality.Common, "ruin_heavy_blade");
            ProjectItem pole = ProjectEquipmentRules.CreateConfiguredItem(new System.Random(24), 1,
                ProjectItemQuality.Common, "ruin_poleblade");
            ProjectItem guard = ProjectEquipmentRules.CreateConfiguredItem(new System.Random(24), 1,
                ProjectItemQuality.Common, "guard_blade");
            heavy.Equipped = true;
            Assert.That(ExpeditionRun.TryCreate(ProjectServices.Session, "expanded-weapons", 1, 42,
                new List<ProjectItem> { heavy, pole, guard }, out ExpeditionRun run), Is.True);
            using var ownedRun = run;
            run.AttachScene(assets);
            Assert.That(run.MarkReady() && run.Activate(), Is.True);
            Assert.That(ProjectContentConfig.Player.SkillID, Is.EqualTo(1));
            Assert.That(run.World.TryGetActor(run.Player, out CombatActorSnapshot before), Is.True);
            AssertSkillRadius(run, 1f);
            Assert.That(run.TryOpenInventory(), Is.True);
            Assert.That(run.TryPreviewEquipment(pole, out ProjectCharacterStats current, out ProjectCharacterStats preview), Is.True);
            Assert.That(preview.AttackRange, Is.GreaterThan(current.AttackRange));
            Assert.That(run.TryEquip(pole.Id), Is.True);
            run.ClosePanel(ExpeditionPanelKind.Inventory);
            AssertSkillRadius(run, 1f);
            Assert.That(run.TryOpenInventory(), Is.True);
            Assert.That(run.TryEquip(guard.Id), Is.True);
            run.ClosePanel(ExpeditionPanelKind.Inventory);
            Assert.That(run.World.TryGetActor(run.Player, out CombatActorSnapshot after), Is.True);
            Assert.That(after.MaxHealth, Is.GreaterThan(before.MaxHealth));
            Assert.That(after.Defense, Is.GreaterThan(before.Defense));
            AssertSkillRadius(run, 1f);
            Assert.That(run.World.Faulted, Is.False);
        });
    }
}
