using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using GameFramework;
using GameLogic.Gameplay.Combat;
using GameLogic.Gameplay.Expedition;
using GameLogic.Gameplay.Level;
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
        /// 用真实尾王领奖和出口转换遍历三张地图，绕障到达只移动，不自动打开交互或发放收益。
        /// </summary>
        /// <returns>真实关卡和保存门禁的逐帧验收任务。</returns>
        [UnityTest]
        public IEnumerator Navigation_AllThreeMapsAvoidObstaclesAndRequireExplicitInteraction() => UniTask.ToCoroutine(async () =>
        {
            await SetupAsync(new MemorySaveBackend());
            using var scope = new LifetimeScope("NavigationThreeMaps");
            ConfigureNavigationFixture(scope);
            CfgBossesRow first = GameTables.CfgBosses.GetById(1);
            CfgBossesRow second = GameTables.CfgBosses.GetById(2);
            int firstMax = first.MaxLevel, secondMin = second.MinLevel;
            scope.Defer(() => { first.MaxLevel = firstMax; second.MinLevel = secondMin; });
            first.MaxLevel = 1;
            second.MinLevel = 2;
            ExpeditionRun run = await StartNavigationFixtureAsync();
            var maps = new HashSet<int>();
            for (int region = 0; region <= run.RegionCount; region++)
            {
                await WaitRunSavedAsync(run);
                FreezeStatisticsFixtureEnemyMovement(run);
                maps.Add(run.CurrentMap.MapId);
                CombatActorSpec combat = NavigationPlayerBase(run);
                SetNavigationAttackRange(run, 0f);
                List<Vector3> route = FindNavigationObstacleRoute(run);
                Assert.That(route.Count, Is.GreaterThan(2), "该夹具必须真实绕过静态障碍。");
                AssertNavigationRouteGeometry(run, route);
                int inventory = run.Inventory.Count, experience = run.KillExperience;
                Vector3 destination = route[route.Count - 1];
                ExpeditionDrop drop = AddInteractionDrop(run, "navigation-uncollected-" + region, destination);
                Assert.That(run.TryStartNavigation(destination, false, out string error), Is.True, error);
                await FinishNavigationAsync(run, 12);
                Assert.That(Vector3.Distance(InputPersistencePlayer(run).Position, destination),
                    Is.LessThanOrEqualTo((float)ProjectContentConfig.Navigation.ArrivalDistance + 0.0001f));
                Assert.That(run.ActivePanel, Is.EqualTo(ExpeditionPanelKind.None));
                Assert.That(run.Inventory.Count, Is.EqualTo(inventory));
                Assert.That(drop.Collected, Is.False, "到达可拾取物旁仍必须明确提交交互。");
                Assert.That(run.KillExperience, Is.EqualTo(experience));
                Assert.That(run.PendingRogueChoice, Is.Null);
                Assert.That(run.PendingBossChoice, Is.Null);
                Assert.That(run.NavigationRoute, Is.Empty);
                if (run.IsFinalRegion) break;
                SetNavigationAttackRange(run, combat.AttackRange);
                await EnterExtractionConfirmationAsync(run);
                await ClickExtractionConfirmationAsync("留在本区");
                await WaitRunSavedAsync(run);
                Assert.That(run.TryStartNavigation(run.CurrentRegion.Plan.EntryPosition, false, out error), Is.True, error);
                Assert.That(run.IsNavigating, Is.True);
                await EnterExtractionConfirmationAsync(run);
                Assert.That(run.IsNavigating, Is.True, "出口确认暂停应保留导航，转换成功后才清理。");
                await ClickExtractionConfirmationAsync("继续下一区域");
                await WaitRunSavedAsync(run);
                Assert.That(run.CurrentRegionIndex, Is.EqualTo(region + 1));
                Assert.That(run.IsNavigating, Is.False);
                Assert.That(run.NavigationRoute, Is.Empty);
            }
            Assert.That(maps, Is.EquivalentTo(new[] { 1, 2, 3 }));
        });

        /// <summary>
        /// 同一条绕障路线按单帧和十二帧推进得到相同停点，剩余补帧不会越过终点或继续漂移。
        /// </summary>
        /// <returns>实际 ECS 移动和存档门禁的逐帧验收任务。</returns>
        [UnityTest]
        public IEnumerator Navigation_SingleAndMultipleTickAdvanceReachTheSameStop() => UniTask.ToCoroutine(async () =>
        {
            await SetupAsync(new MemorySaveBackend());
            using var scope = new LifetimeScope("NavigationTickBatches");
            ConfigureNavigationFixture(scope);
            ExpeditionRun run = await StartNavigationFixtureAsync();
            SetNavigationAttackRange(run, 0f);
            List<Vector3> route = FindNavigationObstacleRoute(run);
            Vector3 start = route[0], destination = route[route.Count - 1];
            Assert.That(run.TryStartNavigation(destination, false, out string error), Is.True, error);
            await FinishNavigationAsync(run, 1);
            Vector3 single = InputPersistencePlayer(run).Position;
            Assert.That(run.World.TrySetPosition(run.Player, start), Is.True);
            Assert.That(run.TryStartNavigation(destination, false, out error), Is.True, error);
            await FinishNavigationAsync(run, 12);
            Vector3 batched = InputPersistencePlayer(run).Position;
            Assert.That(Vector3.Distance(single, batched), Is.LessThan(0.0001f));
            Assert.That(Vector3.Distance(batched, destination),
                Is.LessThanOrEqualTo((float)ProjectContentConfig.Navigation.ArrivalDistance + 0.0001f));
            await AdvanceNavigationTicksAsync(run, 12, 12);
            Assert.That(InputPersistencePlayer(run).Position, Is.EqualTo(batched));
            Assert.That(run.NavigationRoute, Is.Empty);
        });

        /// <summary>
        /// 地图和应用暂停保留指令，关闭后恢复；重复目的地可替换路线，触屏输入即时接管移动。
        /// </summary>
        /// <returns>真实地图窗口、应用暂停和输入接管的逐帧验收任务。</returns>
        [UnityTest]
        public IEnumerator Navigation_MapAndBackgroundPauseKeepRouteUntilManualTakeover() => UniTask.ToCoroutine(async () =>
        {
            await SetupAsync(new MemorySaveBackend());
            using var scope = new LifetimeScope("NavigationPause");
            ConfigureNavigationFixture(scope);
            ExpeditionRun run = await StartNavigationFixtureAsync();
            SetNavigationAttackRange(run, 0f);
            List<Vector3> route = FindNavigationObstacleRoute(run);
            Vector3 destination = route[route.Count - 1];
            Assert.That(run.TryOpenMap(), Is.True);
            GameWindow map = await WaitForWindowAsync(ProjectServices.ExpeditionMapWindow);
            Assert.That(run.TryStartNavigation(destination, false, out string error), Is.True, error);
            Vector3 before = InputPersistencePlayer(run).Position;
            long tick = run.World.TickIndex;
            run.Advance(10f);
            Assert.That(run.World.TickIndex, Is.EqualTo(tick));
            Assert.That(InputPersistencePlayer(run).Position, Is.EqualTo(before));
            Assert.That(run.IsNavigating, Is.True);
            await ProjectServices.Coordinator.SetApplicationPausedAsync(true);
            run.Advance(10f);
            await ProjectServices.Coordinator.SetApplicationPausedAsync(false);
            Assert.That(run.IsNavigating, Is.True);
            Assert.That(run.NavigationDestination, Is.EqualTo(destination));
            Assert.That(run.TryStartNavigation(destination, false, out error), Is.True, error);
            Assert.That(run.NavigationRoute, Is.EqualTo(route));
            ClickButton(map, "返回探索");
            await WaitForAsync(() => run.ActivePanel == ExpeditionPanelKind.None);
            run.Advance(CombatWorld.TickSeconds);
            Assert.That(Vector3.Distance(InputPersistencePlayer(run).Position, before), Is.GreaterThan(0f));
            await WaitRunSavedAsync(run);
            Assert.That(run.IsNavigating, Is.True);
            run.SetTouchMove(Vector2.right);
            Assert.That(run.IsNavigating, Is.False, "手动输入应在提交时立即接管。");
            Assert.That(run.NavigationRoute, Is.Empty);
            Vector3 handoff = InputPersistencePlayer(run).Position;
            run.Advance(CombatWorld.TickSeconds);
            Assert.That(InputPersistencePlayer(run).Position.x, Is.GreaterThan(handoff.x));
            Assert.That(InputPersistencePlayer(run).Position.z, Is.EqualTo(handoff.z).Within(0.0001f));
            run.SetTouchMove(Vector2.zero);
        });

        /// <summary>
        /// 非法静态坐标拒绝规划；合法坐标上的真实敌人持续堵路时停止，暂停时间不计入堵路计时。
        /// </summary>
        /// <returns>权威静态和动态碰撞的逐帧验收任务。</returns>
        [UnityTest]
        public IEnumerator Navigation_InvalidPointsFailAndDynamicBlockStopsAfterEffectiveTime() => UniTask.ToCoroutine(async () =>
        {
            await SetupAsync(new MemorySaveBackend());
            using var scope = new LifetimeScope("NavigationBlocked");
            ConfigureNavigationFixture(scope);
            ExpeditionRun run = await StartNavigationFixtureAsync();
            SetNavigationAttackRange(run, 0f);
            var route = new List<Vector3> { Vector3.zero };
            Vector3 outside = new(run.CurrentMap.Bounds.xMax + 1f, 0f, run.CurrentMap.Bounds.center.y);
            Assert.That(run.TryPreviewNavigation(outside, false, route, out string error), Is.False);
            Assert.That(error, Is.Not.Empty);
            Assert.That(route, Is.Empty);
            RegionMapObstacle obstacle = run.CurrentMap.Obstacles.First();
            Vector2 center = (obstacle.Min + obstacle.Max) * 0.5f;
            Assert.That(run.TryStartNavigation(new Vector3(center.x, 0f, center.y), false, out error), Is.False);
            Assert.That(run.TryStartNavigation(new Vector3(float.NaN, 0f, 0f), false, out error), Is.False);
            Assert.That(run.IsNavigating, Is.False);
            Vector3 target = FindNavigationBlockedTarget(run);
            Assert.That(run.TryStartNavigation(target, false, out error), Is.True, error);
            await AdvanceNavigationTicksAsync(run, 45, 12);
            Assert.That(run.IsNavigating, Is.True);
            Vector3 blocked = InputPersistencePlayer(run).Position;
            run.SetPaused(true);
            run.Advance(20f);
            Assert.That(run.IsNavigating, Is.True);
            Assert.That(InputPersistencePlayer(run).Position, Is.EqualTo(blocked));
            run.SetPaused(false);
            await AdvanceNavigationTicksAsync(run, 30, 12);
            Assert.That(run.IsNavigating, Is.True, "暂停前后累计有效时间仍未达到堵路阈值。");
            await AdvanceNavigationTicksAsync(run, 45, 12);
            Assert.That(run.IsNavigating, Is.False);
            StringAssert.Contains("被阻挡", run.Message);
            Assert.That(run.NavigationRoute, Is.Empty);
            Assert.That(Vector3.Distance(InputPersistencePlayer(run).Position, target),
                Is.GreaterThan((float)ProjectContentConfig.Navigation.ArrivalDistance));
        });

        /// <summary>
        /// 反复冲刺也无法穿过真实敌人的占位，位移期间暂停计时，位移结束不得清空已累计的堵路时间。
        /// </summary>
        /// <returns>无伤害测试技能、真实位移组件和堵路计时的逐帧验收任务。</returns>
        [UnityTest]
        public IEnumerator Navigation_RepeatedBlockedDisplacementsStillAccumulateStuckTime() => UniTask.ToCoroutine(async () =>
        {
            await SetupAsync(new MemorySaveBackend());
            using var scope = new LifetimeScope("NavigationRepeatedDisplacement");
            ConfigureNavigationFixture(scope);
            foreach (int id in ProjectContentConfig.Player.SkillIDs)
            {
                CfgCombatSkillsRow normal = GameTables.CfgCombatSkills.GetById(id);
                int mode = normal.CastMode;
                scope.Defer(() => normal.CastMode = mode);
                normal.CastMode = (int)CombatSkillCastMode.Manual;
            }
            int skillId = ProjectContentConfig.Player.SkillID;
            int displacementId = GameTables.CfgCombatEffects.GetAll().First(value => value.Kind == (int)CombatEffectKind.Displace).ID;
            foreach (CfgCombatTimelineNodesRow node in GameTables.CfgCombatTimelineNodes.GetAll().Where(value => value.SkillID == skillId))
            {
                int[] effects = node.EffectIDs;
                scope.Defer(() => node.EffectIDs = effects);
                node.EffectIDs = new[] { displacementId };
            }
            ExpeditionRun run = await StartNavigationFixtureAsync();
            SetNavigationAttackRange(run, 0f);
            Vector3 target = FindNavigationBlockedTarget(run);
            Assert.That(run.TryStartNavigation(target, false, out string error), Is.True, error);
            await AdvanceNavigationTicksAsync(run, 24, 12);
            Assert.That(run.IsNavigating, Is.True);
            Vector3 blocked = InputPersistencePlayer(run).Position;
            int kills = run.CaptureStatistics().EnemyKills;
            int casts = 0, displacementPeriods = 0, displacedTicks = 0, ordinaryTicks = 0;
            bool previouslyDisplaced = false;
            int interval = 30;
            Assert.That(interval * CombatWorld.TickSeconds, Is.LessThan((float)ProjectContentConfig.Navigation.StuckSeconds));
            int limit = Mathf.CeilToInt((float)ProjectContentConfig.Navigation.StuckSeconds / CombatWorld.TickSeconds) * 4 + 60;
            for (int tick = 0; tick < limit && run.IsNavigating; tick++)
            {
                await WaitRunSavedAsync(run);
                if (tick % interval == 0)
                {
                    Assert.That(run.World.TryStartSkill(run.Player, skillId, default), Is.True, "周期性无伤害冲刺必须真正开始。");
                    casts++;
                }
                run.Advance(CombatWorld.TickSeconds);
                Assert.That(run.World.TryGetMovementStatus(run.Player, out _, out bool displaced), Is.True);
                if (displaced)
                {
                    displacedTicks++;
                    if (!previouslyDisplaced) displacementPeriods++;
                }
                else ordinaryTicks++;
                previouslyDisplaced = displaced;
                Assert.That(Vector3.Distance(InputPersistencePlayer(run).Position, blocked), Is.LessThan(0.0001f),
                    "普通移动及战斗冲刺都必须被同一敌人占位阻挡，不能用真实前进重置堵路时间。");
            }
            await WaitRunSavedAsync(run);
            Assert.That(casts, Is.GreaterThanOrEqualTo(3));
            Assert.That(displacementPeriods, Is.GreaterThanOrEqualTo(3), "必须实际经历多次位移开始和结束。");
            Assert.That(displacedTicks, Is.GreaterThan(0));
            Assert.That(ordinaryTicks, Is.GreaterThan(0));
            Assert.That(run.CaptureStatistics().EnemyKills, Is.EqualTo(kills), "测试技能只产生位移，不造成死亡或领奖暂停。");
            Assert.That(run.IsNavigating, Is.False, "周期性位移结束重算路线后，非位移有效帧仍须累计堵路并停止。");
            StringAssert.Contains("被阻挡", run.Message);
            Assert.That(run.NavigationRoute, Is.Empty);
        });

        /// <summary>
        /// 导航只属于本次运行输入，快照恢复不续行，场景解绑和停止均清空路线。
        /// </summary>
        /// <returns>完整快照恢复和真实场景生命周期的逐帧验收任务。</returns>
        [UnityTest]
        public IEnumerator Navigation_RestoreDetachAndStopClearTransientMovement() => UniTask.ToCoroutine(async () =>
        {
            await SetupAsync(new MemorySaveBackend());
            using var scope = new LifetimeScope("NavigationLifecycle");
            ConfigureNavigationFixture(scope);
            ExpeditionRun run = await StartNavigationFixtureAsync();
            SetNavigationAttackRange(run, 0f);
            List<Vector3> route = FindNavigationObstacleRoute(run);
            Assert.That(run.TryStartNavigation(route[route.Count - 1], false, out string error), Is.True, error);
            run.Advance(CombatWorld.TickSeconds);
            await WaitRunSavedAsync(run);
            Assert.That(run.TryCaptureState(out ExpeditionRunSnapshot snapshot, out error), Is.True, error);
            Assert.That(ExpeditionSnapshotCodec.TryEncode(snapshot, out var parts, out error), Is.True, error);
            Assert.That(ExpeditionSnapshotCodec.TryDecode(parts, out var decoded, out error), Is.True, error);
            Assert.That(ExpeditionRun.TryRestore(ProjectServices.Session, decoded, out ExpeditionRun restored, out error), Is.True, error);
            using (restored)
            {
                using var restoredScope = new LifetimeScope("NavigationRestoredScene");
                ExpeditionVisualAssets assets = await ExpeditionVisualAssets.LoadAsync(restoredScope, GameEntry.Get<IResourceModule>());
                restored.AttachScene(assets);
                Assert.That(restored.MarkReady(), Is.True);
                Assert.That(restored.Activate(), Is.True);
                Assert.That(restored.IsNavigating, Is.False);
                Assert.That(restored.NavigationRoute, Is.Empty);
                Vector3 before = InputPersistencePlayer(restored).Position;
                restored.Advance(CombatWorld.TickSeconds);
                Assert.That(InputPersistencePlayer(restored).Position, Is.EqualTo(before), "恢复不得继承导航的移动轴。");
                Assert.That(restored.TryStartNavigation(route[route.Count - 1], false, out error), Is.True, error);
                restored.Stop();
                Assert.That(restored.IsNavigating, Is.False);
                Assert.That(restored.NavigationRoute, Is.Empty);
            }
            Assert.That(run.IsNavigating, Is.True);
            run.DetachScene();
            Assert.That(run.IsNavigating, Is.False);
            Assert.That(run.NavigationRoute, Is.Empty);
            Assert.That(run.IsRunning, Is.False);
        });

        /// <summary>
        /// 将技能可用距离缩到角色占位无法重叠的范围，保留敌人源配置要求的合法正移速。
        /// </summary>
        /// <param name="scope">负责恢复全部临时配置的生命作用域。</param>
        private static void ConfigureNavigationFixture(LifetimeScope scope)
        {
            foreach (CfgEnemySkillsRow skill in GameTables.CfgEnemySkills.GetAll())
            {
                double min = skill.MinDistance, max = skill.MaxDistance;
                scope.Defer(() => { skill.MinDistance = min; skill.MaxDistance = max; });
                skill.MinDistance = 0d;
                skill.MaxDistance = 0.001d;
            }
        }

        /// <summary>
        /// 先校验合法源配置并完成正式入场，再通过公开基础属性接口固定已经生成的敌人移动。
        /// </summary>
        /// <returns>已入场且敌人位置固定的真实单局。</returns>
        private static async UniTask<ExpeditionRun> StartNavigationFixtureAsync()
        {
            Assert.That(ProjectContentConfig.Validate(out string error), Is.True, error);
            Assert.That(await ProjectServices.Coordinator.StartRunAsync(1), Is.True,
                "coordinator=" + ProjectServices.Coordinator.Message + ", runSave=" + ProjectServices.Session.RunSaveMessage);
            ExpeditionRun run = ProjectServices.Session.CurrentRun;
            FreezeStatisticsFixtureEnemyMovement(run);
            return run;
        }

        /// <summary>
        /// 读取玩家原始属性以临时控制自动攻击，不改保存结构或业务阶段。
        /// </summary>
        /// <param name="run">当前单局。</param>
        /// <returns>玩家基础属性。</returns>
        private static CombatActorSpec NavigationPlayerBase(ExpeditionRun run)
        {
            Assert.That(run.World.TryExportState(out CombatWorldSnapshot world, out string error), Is.True, error);
            return world.Actors.Single(value => value.Spec.IsPlayer).BaseSpec;
        }

        /// <summary>
        /// 使用公开战斗属性入口控制测试期间的自动攻击范围。
        /// </summary>
        /// <param name="run">当前单局。</param>
        /// <param name="range">需要设置的基础攻击范围。</param>
        private static void SetNavigationAttackRange(ExpeditionRun run, float range)
        {
            CombatActorSpec spec = NavigationPlayerBase(run);
            spec.AttackRange = range;
            Assert.That(run.World.TryReplaceBaseAttributes(run.Player, spec), Is.True);
        }

        /// <summary>
        /// 在实际地图的障碍两侧寻找合法起终点，排除固定敌人的占位后保留真实绕障路线。
        /// </summary>
        /// <param name="run">当前完整区域。</param>
        /// <returns>包含玩家起点、至少一个拐角和目的地的路线。</returns>
        private static List<Vector3> FindNavigationObstacleRoute(ExpeditionRun run)
        {
            Assert.That(run.World.TryExportState(out CombatWorldSnapshot world, out string error), Is.True, error);
            CombatActorState player = world.Actors.Single(value => value.Spec.IsPlayer);
            float radius = player.Spec.Radius, attackRange = player.Spec.AttackRange + player.RangeBonus;
            var route = new List<Vector3>();
            foreach (RegionMapObstacle wall in run.CurrentMap.Obstacles)
            {
                Vector2 center = (wall.Min + wall.Max) * 0.5f;
                foreach (float padding in new[] { 1.5f, 3f, 5f })
                foreach (bool horizontal in new[] { true, false })
                foreach (float offset in new[] { 0f, -0.75f, 0.75f })
                {
                    Vector3 start = horizontal ? new Vector3(wall.Min.x - radius - padding, 0f, center.y + offset) :
                        new Vector3(center.x + offset, 0f, wall.Min.y - radius - padding);
                    Vector3 end = horizontal ? new Vector3(wall.Max.x + radius + padding, 0f, center.y + offset) :
                        new Vector3(center.x + offset, 0f, wall.Max.y + radius + padding);
                    if (!run.World.TrySetPosition(run.Player, start) || !run.TryPreviewNavigation(end, false, route, out error) || route.Count < 3)
                        continue;
                    bool clear = true;
                    for (int segment = 1; segment < route.Count && clear; segment++)
                    {
                        int samples = Mathf.CeilToInt(Vector3.Distance(route[segment - 1], route[segment]) / 0.1f);
                        for (int sample = 0; sample <= samples && clear; sample++)
                        {
                            Vector3 point = Vector3.Lerp(route[segment - 1], route[segment], sample / (float)samples);
                            clear = world.Actors.Where(value => !value.Spec.IsPlayer && value.Spec.RoomId == run.World.ActiveRoomId && value.Health > 0f)
                                .All(value => Vector3.Distance(value.Position, point) > Mathf.Max(radius + value.Spec.Radius, attackRange) + 0.2f);
                        }
                    }
                    if (clear) return route;
                }
            }
            Assert.Fail("真实地图中未找到不受动态占位干扰的绕障路线，map=" + run.CurrentMap.MapId + ", seed=" + run.Seed);
            return route;
        }

        /// <summary>
        /// 独立采样验证整条规划路线没有越界或穿过配置障碍，使用角色真实半径。
        /// </summary>
        /// <param name="run">当前单局。</param>
        /// <param name="route">公开预览返回的完整路线。</param>
        private static void AssertNavigationRouteGeometry(ExpeditionRun run, IReadOnlyList<Vector3> route)
        {
            Assert.That(run.World.TryExportState(out CombatWorldSnapshot world, out string error), Is.True, error);
            float radius = world.Actors.Single(value => value.Spec.IsPlayer).Spec.Radius;
            var geometry = new RegionMapGeometry(run.CurrentMap);
            for (int segment = 1; segment < route.Count; segment++)
            {
                int samples = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(route[segment - 1], route[segment]) / 0.05f));
                for (int sample = 0; sample <= samples; sample++)
                    Assert.That(geometry.IsBlocked(Vector3.Lerp(route[segment - 1], route[segment], sample / (float)samples), radius),
                        Is.False, "路线应保持整个角色圆形占位合法，map=" + run.CurrentMap.MapId + ", segment=" + segment);
            }
        }

        /// <summary>
        /// 在真实敌人周围寻找直接可达且不重叠的起点，将敌人中心作为静态合法的堵路目标。
        /// </summary>
        /// <param name="run">当前单局。</param>
        /// <returns>动态敌人占位的目标坐标。</returns>
        private static Vector3 FindNavigationBlockedTarget(ExpeditionRun run)
        {
            Assert.That(run.World.TryExportState(out CombatWorldSnapshot world, out string error), Is.True, error);
            float radius = world.Actors.Single(value => value.Spec.IsPlayer).Spec.Radius;
            var route = new List<Vector3>();
            foreach (CombatActorState enemy in world.Actors.Where(value => !value.Spec.IsPlayer && value.Health > 0f))
            foreach (Vector3 direction in new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back })
            {
                Vector3 start = enemy.Position + direction * (radius + enemy.Spec.Radius + 0.4f);
                if (run.World.TrySetPosition(run.Player, start) && run.TryPreviewNavigation(enemy.Position, false, route, out error) && route.Count == 2)
                    return enemy.Position;
            }
            Assert.Fail("真实敌人附近未找到直接可达的动态堵路夹具。");
            return default;
        }

        /// <summary>
        /// 等待每批保存完成后推进固定数量的有效逻辑帧，避免内存后端的预算门禁影响测试时间。
        /// </summary>
        /// <param name="run">当前单局。</param>
        /// <param name="ticks">总共需要推进的有效帧数。</param>
        /// <param name="batch">每次调用推进的最大帧数。</param>
        /// <returns>完整推进与保存确认的任务。</returns>
        private static async UniTask AdvanceNavigationTicksAsync(ExpeditionRun run, int ticks, int batch)
        {
            for (int completed = 0; completed < ticks;)
            {
                await WaitRunSavedAsync(run);
                int count = Math.Min(batch, ticks - completed);
                long before = run.World.TickIndex;
                run.Advance(CombatWorld.TickSeconds * count);
                Assert.That(run.World.TickIndex, Is.EqualTo(before + count), run.Message);
                completed += count;
            }
            await WaitRunSavedAsync(run);
        }

        /// <summary>
        /// 有界推进真实导航直到到达，输出地图、种子和路线状态以定位失败。
        /// </summary>
        /// <param name="run">持有导航指令的单局。</param>
        /// <param name="batch">每次调用推进的逻辑帧数。</param>
        /// <returns>导航结束及保存确认任务。</returns>
        private static async UniTask FinishNavigationAsync(ExpeditionRun run, int batch)
        {
            for (int ticks = 0; ticks < 2400 && run.IsNavigating; ticks += batch)
                await AdvanceNavigationTicksAsync(run, batch, batch);
            Assert.That(run.IsNavigating, Is.False, "map=" + run.CurrentMap.MapId + ", seed=" + run.Seed + ", message=" + run.Message);
            StringAssert.Contains("目的地", run.Message);
        }
    }
}
