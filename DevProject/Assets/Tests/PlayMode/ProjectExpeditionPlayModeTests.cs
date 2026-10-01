using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;
using GameLogic.Gameplay.Combat;
using GameLogic.Gameplay.Expedition;
using GameLogic.Gameplay.Level;
using GameLogic.Project;
using GameLogic.Scaffold;
using GameLogic.Scenes.Expedition;
using GameLogic.UI.Battle;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using YooAsset;

namespace MiniGame.Tests
{
    /// <summary>
    /// 使用内存存档和真实战斗场景验证完整单局往返。
    /// </summary>
    public sealed partial class ProjectExpeditionPlayModeTests
    {
        /// <summary>
        /// 每项测试结束时关闭场景和框架宿主。
        /// </summary>
        /// <returns>等待 Unity 销毁延迟对象的逐帧清理流程。</returns>
        [UnityTearDown]
        public IEnumerator TearDown() => UniTask.ToCoroutine(async () =>
        {
            ISceneModule scenes = GameEntry.Get<ISceneModule>();
            if (ProjectServices.Coordinator != null) await ProjectServices.Coordinator.CloseAsync();
            if (GameEntry.IsRunning && scenes != null) await scenes.ExitCurrentSceneAsync();
            GameEntry.StopFramework();
            if (YooAssets.IsInitialized) YooAssets.Destroy();
            if (GameEntry.HasHost) UnityEngine.Object.Destroy(GameEntry.Bootstrap().gameObject);
            await UniTask.Yield();
            LogAssert.ignoreFailingMessages = false;
        });

        /// <summary>
        /// 首局撤离保留装备，第二局死亡丢失入场装备，并确认每局仅扣一次体力。
        /// </summary>
        /// <returns>等待保存、真实场景切换和窗口生命周期的逐帧测试。</returns>
        [UnityTest]
        public IEnumerator ExtractThenDie_ReentersWithoutLeakingSceneOrChargingTwice() => UniTask.ToCoroutine(async () =>
        {
            GameEntry.StopFramework();
            var saves = new SaveModule();
            Assert.That(saves.ConfigureBackend(new MemorySaveBackend()).Success, Is.True);
            GameEntry.Register<ISaveModule>(saves);
            GameEntry.Register<IInputModule>(new HeadlessInputModule());
            GameEntry.RegisterDefaults();
            GameEntry.StartFramework();
            await InitializeEditorPackageAsync();

            ISceneModule scenes = GameEntry.Get<ISceneModule>();
            IUIModule ui = GameEntry.Get<IUIModule>();
            ProjectContent.Register(scenes, ui);
            Assert.That(scenes.GetScene(ProjectServices.BattleSceneName), Is.TypeOf<ExpeditionScene>());
            scenes.UnregisterScene(ContentIds.HomeScene);
            var home = new EmptyHomeScene();
            scenes.RegisterScene(home);
            Assert.That(await ProjectServices.InitializeAsync(default), Is.True);
            Assert.That(await ProjectServices.Coordinator.InitializeAsync(), Is.True);
            ProjectSession session = ProjectServices.Session;
            int stamina = session.Stamina;
            string equippedId = session.Warehouse.Single(item => item.Equipped).Id;

            Assert.That(await ProjectServices.Coordinator.StartRunAsync(1).Timeout(TimeSpan.FromSeconds(20)), Is.True);
            ExpeditionRun first = AssertBattle(scenes, ui, session);
            Assert.That(session.Stamina, Is.EqualTo(stamina - 10));
            Assert.That(first.World.TryGetActor(first.Player, out CombatActorSnapshot before), Is.True);
            first.SetTouchMove(Vector2.right);
            first.Advance(CombatWorld.TickSeconds);
            Assert.That(first.World.TickIndex, Is.GreaterThan(0));
            Assert.That(first.World.TryGetActor(first.Player, out CombatActorSnapshot after), Is.True);
            Assert.That(after.Position.x, Is.GreaterThan(before.Position.x));
            first.SetTouchMove(Vector2.zero);
            await TriggerExtractionAsync(first);
            Assert.That(ProjectServices.Session.LastResult.Outcome.Extracted, Is.True);
            Assert.That(scenes.CurrentScene, Is.Null);
            Assert.That(ui.OpenedWindows.Any(window => window.Id == ProjectServices.ResultWindow), Is.True);
            Assert.That(await ProjectServices.Coordinator.ReturnHomeAsync(), Is.True);
            await AssertHomeClean(scenes, ui, session, home);
            Assert.That(session.Stamina, Is.EqualTo(stamina - 10));
            Assert.That(session.Warehouse.Any(item => item.Id == equippedId && item.Equipped), Is.True);

            Assert.That(await ProjectServices.Coordinator.StartRunAsync(1).Timeout(TimeSpan.FromSeconds(20)), Is.True);
            ExpeditionRun second = AssertBattle(scenes, ui, session);
            Assert.That(second.RunId, Is.Not.EqualTo(first.RunId));
            Assert.That(session.Stamina, Is.EqualTo(stamina - 20));
            Assert.That(second.Inventory.Any(item => item.Id == equippedId), Is.True);
            TriggerDeath(second);
            await WaitForPhaseAsync(ProjectRunPhase.ResultsReady);
            Assert.That(ProjectServices.Session.LastResult.Outcome.Extracted, Is.False);
            Assert.That(await ProjectServices.Coordinator.ReturnHomeAsync(), Is.True);
            await AssertHomeClean(scenes, ui, session, home);
            Assert.That(session.Stamina, Is.EqualTo(stamina - 20));
            Assert.That(session.Warehouse.Any(item => item.Id == equippedId), Is.False);
            Assert.That(session.LastRunSummary, Does.Contain("角色死亡"));
        });

        /// <summary>
        /// 使用项目实际 Home 内容和主界面完成出战与返场，存档仍隔离在内存中。
        /// </summary>
        /// <returns>资源加载和往返的逐帧测试。</returns>
        [UnityTest]
        public IEnumerator RealHomeContent_ExtractsAndRefreshesTheActualHomeWindow() => UniTask.ToCoroutine(async () =>
        {
            GameEntry.StopFramework();
            var saves = new SaveModule();
            Assert.That(saves.ConfigureBackend(new MemorySaveBackend()).Success, Is.True);
            GameEntry.Register<ISaveModule>(saves);
            GameEntry.Register<IInputModule>(new HeadlessInputModule());
            GameEntry.RegisterDefaults();
            GameEntry.StartFramework();
            await InitializeEditorPackageAsync();
            ProjectContent.Register(GameEntry.Get<ISceneModule>(), GameEntry.Get<IUIModule>());
            Assert.That(await ProjectServices.InitializeAsync(default), Is.True);
            Assert.That(await ProjectServices.Coordinator.InitializeAsync(), Is.True);
            Assert.That(GameEntry.Get<ISceneModule>().CurrentScene, Is.TypeOf<GameLogic.Scenes.Home.HomeScene>());
            Assert.That(GameEntry.Get<IUIModule>().OpenedWindows.Any(window => window is GameLogic.UI.Home.HomeWindow), Is.True);
            Assert.That(await ProjectServices.Coordinator.StartRunAsync(1).Timeout(TimeSpan.FromSeconds(20)), Is.True);
            await TriggerExtractionAsync(ProjectServices.Session.CurrentRun);
            Assert.That(await ProjectServices.Coordinator.ReturnHomeAsync(), Is.True);
            Assert.That(ProjectServices.Coordinator.CanStart, Is.True);
            Assert.That(ProjectServices.Session.LastResult.Viewed, Is.True);
            Assert.That(GameEntry.Get<ISceneModule>().CurrentScene, Is.TypeOf<GameLogic.Scenes.Home.HomeScene>());
            Assert.That(GameEntry.Get<IUIModule>().OpenedWindows.Any(window => window is GameLogic.UI.Home.HomeWindow), Is.True);
            Assert.That(GameEntry.Get<IGameplayModule>().Count, Is.EqualTo(1));
        });

        /// <summary>
        /// 未确认开始授权时保持零 tick，保存恢复后才激活。
        /// </summary>
        /// <returns>有界等待授权和重试的测试任务。</returns>
        [UnityTest]
        public IEnumerator ActivationSaveFailure_HoldsReadyWorldUntilOriginalSaveSucceeds() => UniTask.ToCoroutine(async () =>
        {
            var backend = new FaultSaveBackend();
            await SetupAsync(backend);
            ProjectRunCoordinator coordinator = ProjectServices.Coordinator;
            coordinator.Changed += FailActivation;
            /// <summary>
            /// 在开始授权阶段注入持久化失败。
            /// </summary>
            void FailActivation()
            {
                if (coordinator.Phase == ProjectRunPhase.Activating) backend.FailWrites = true;
            }
            LogAssert.ignoreFailingMessages = true;
            UniTask<bool> entering = coordinator.StartRunAsync(1).Preserve();
            await WaitForPhaseAsync(ProjectRunPhase.RecoveryRequired);
            ExpeditionRun run = ProjectServices.Session.CurrentRun;
            Assert.That(run.IsReady, Is.True);
            Assert.That(run.IsRunning, Is.False);
            run.Advance(0.2f);
            Assert.That(run.World.TickIndex, Is.Zero);
            Assert.That(ProjectServices.Session.RunStage, Is.EqualTo(ProjectRunStage.Admitted));
            Assert.That(await coordinator.StartRunAsync(1), Is.False);
            coordinator.Changed -= FailActivation;
            backend.FailWrites = false;
            Assert.That(await coordinator.RetryAsync(), Is.True, coordinator.Message);
            Assert.That(await entering.Timeout(TimeSpan.FromSeconds(20)), Is.True);
            Assert.That(ProjectServices.Session.Stamina, Is.EqualTo(90));
            Assert.That(run.IsRunning, Is.True);
            long tick = run.World.TickIndex;
            run.SetTouchMove(Vector2.right);
            run.Advance(CombatWorld.TickSeconds * 0.5f);
            await coordinator.SetApplicationPausedAsync(true);
            run.Advance(10f);
            Assert.That(run.World.TickIndex, Is.EqualTo(tick));
            await coordinator.SetApplicationPausedAsync(false);
            run.Advance(CombatWorld.TickSeconds);
            Assert.That(run.World.TickIndex, Is.EqualTo(tick + 1), "暂停前半帧和后台时间均不得补算。");
        });

        /// <summary>
        /// 结算失败后重启读取已记录终局，不将死亡变成退装备和退体力。
        /// </summary>
        /// <returns>保存失败和冷恢复的逐帧测试。</returns>
        [UnityTest]
        public IEnumerator RecordedDeath_RestartsWithoutRefundOrDuplicateSettlement() => UniTask.ToCoroutine(async () =>
        {
            var backend = new FaultSaveBackend();
            await SetupAsync(backend);
            Assert.That(await ProjectServices.Coordinator.StartRunAsync(1).Timeout(TimeSpan.FromSeconds(20)), Is.True);
            ProjectRunCoordinator coordinator = ProjectServices.Coordinator;
            coordinator.Changed += FailSettlement;
            /// <summary>
            /// 在结算阶段注入持久化失败。
            /// </summary>
            void FailSettlement()
            {
                if (coordinator.Phase == ProjectRunPhase.Settling) backend.FailWrites = true;
            }
            LogAssert.ignoreFailingMessages = true;
            ExpeditionRun run = ProjectServices.Session.CurrentRun;
            string runId = run.RunId;
            TriggerDeath(run);
            await WaitForPhaseAsync(ProjectRunPhase.RecoveryRequired);
            Assert.That(ProjectServices.Session.RunStage, Is.EqualTo(ProjectRunStage.OutcomeRecorded));
            Assert.That(GameEntry.Get<ISceneModule>().CurrentScene, Is.Null);
            Assert.That(GameEntry.Get<IUIModule>().OpenedWindows.Any(window => window.Id == ProjectServices.TransitionWindow), Is.True);
            coordinator.Changed -= FailSettlement;
            await coordinator.CloseAsync();
            GameEntry.StopFramework();
            backend.FailWrites = false;
            await SetupAsync(backend);
            Assert.That(ProjectServices.Coordinator.Phase, Is.EqualTo(ProjectRunPhase.ResultsReady));
            Assert.That(ProjectServices.Session.LastResult.Outcome.RunId, Is.EqualTo(runId));
            Assert.That(ProjectServices.Session.LastResult.Outcome.Extracted, Is.False);
            Assert.That(ProjectServices.Session.Warehouse, Is.Empty);
            Assert.That(ProjectServices.Session.Stamina, Is.EqualTo(90));
            Assert.That(ProjectServices.Session.CurrentRun, Is.Null);
            Assert.That(await ProjectServices.Coordinator.ReturnHomeAsync(), Is.True);
            int experience = ProjectServices.Session.Experience;
            Assert.That(await ProjectServices.Coordinator.ReturnHomeAsync(), Is.False);
            Assert.That(ProjectServices.Session.Experience, Is.EqualTo(experience));
        });

        /// <summary>
        /// 已保存结果返回失败后仍保留全局恢复入口，重试不再次发放收益。
        /// </summary>
        /// <returns>返场失败与恢复的逐帧测试。</returns>
        [UnityTest]
        public IEnumerator HomeFailure_KeepsGlobalRetryAfterBattleWasReleased() => UniTask.ToCoroutine(async () =>
        {
            await SetupAsync(new FaultSaveBackend());
            Assert.That(await ProjectServices.Coordinator.StartRunAsync(1).Timeout(TimeSpan.FromSeconds(20)), Is.True);
            await TriggerExtractionAsync(ProjectServices.Session.CurrentRun);
            int experience = ProjectServices.Session.Experience;
            ISceneModule scenes = GameEntry.Get<ISceneModule>();
            scenes.UnregisterScene(ContentIds.HomeScene);
            scenes.RegisterScene(new FailingHomeScene());
            LogAssert.ignoreFailingMessages = true;
            Assert.That(await ProjectServices.Coordinator.ReturnHomeAsync(), Is.False);
            Assert.That(scenes.CurrentScene, Is.Null);
            Assert.That(ProjectServices.Coordinator.CanRetry, Is.True);
            Assert.That(GameEntry.Get<IUIModule>().OpenedWindows.Any(window => window.Id == ProjectServices.TransitionWindow), Is.True);
            scenes.UnregisterScene(ContentIds.HomeScene);
            scenes.RegisterScene(new EmptyHomeScene());
            Assert.That(await ProjectServices.Coordinator.RetryAsync(), Is.True);
            Assert.That(ProjectServices.Coordinator.Phase, Is.EqualTo(ProjectRunPhase.HomeReady));
            Assert.That(ProjectServices.Session.Experience, Is.EqualTo(experience));
        });

        /// <summary>
        /// 入场保存失败后取消，确认原写入再补偿一次。
        /// </summary>
        /// <returns>取消与补偿的逐帧测试。</returns>
        [UnityTest]
        public IEnumerator CancelPendingAdmission_ConfirmsThenCompensatesOnce() => UniTask.ToCoroutine(async () =>
        {
            var backend = new FaultSaveBackend();
            await SetupAsync(backend);
            backend.FailWrites = true;
            LogAssert.ignoreFailingMessages = true;
            ProjectRunCoordinator coordinator = ProjectServices.Coordinator;
            UniTask<bool> entering = coordinator.StartRunAsync(1).Preserve();
            await WaitForPhaseAsync(ProjectRunPhase.RecoveryRequired);
            coordinator.CancelLoading();
            backend.FailWrites = false;
            Assert.That(await coordinator.RetryAsync(), Is.True, coordinator.Message);
            Assert.That(await entering.Timeout(TimeSpan.FromSeconds(20)), Is.False);
            Assert.That(coordinator.Phase, Is.EqualTo(ProjectRunPhase.HomeReady));
            Assert.That(ProjectServices.Session.Stamina, Is.EqualTo(100));
            Assert.That(ProjectServices.Session.Warehouse.Count, Is.EqualTo(1));
            Assert.That(ProjectServices.Session.ActiveRunId, Is.Empty);
            Assert.That(coordinator.CanRetry, Is.False);
        });

        /// <summary>
        /// Home 退出抛出异常后可重建协调器，不遗留永久关闭门禁。
        /// </summary>
        /// <returns>关闭失败和重建恢复的逐帧测试。</returns>
        [UnityTest]
        public IEnumerator CloseFailure_CanRetryCleanupAndRestoreServices() => UniTask.ToCoroutine(async () =>
        {
            await SetupAsync(new FaultSaveBackend());
            ISceneModule scenes = GameEntry.Get<ISceneModule>();
            await scenes.ExitCurrentSceneAsync();
            scenes.UnregisterScene(ContentIds.HomeScene);
            scenes.RegisterScene(new ExitFailingHomeScene());
            await scenes.ChangeSceneAsync(ContentIds.HomeScene);
            ProjectRunCoordinator original = ProjectServices.Coordinator;
            LogAssert.ignoreFailingMessages = true;
            Assert.That(await original.CloseAsync(), Is.False);
            Assert.That(original.CanRetry, Is.True);
            Assert.That(await original.RetryAsync(), Is.True, original.Message);
            Assert.That(ProjectServices.Coordinator, Is.Not.SameAs(original));
            Assert.That(ProjectServices.Coordinator.CanStart, Is.True);
            Assert.That(ProjectServices.Session.Stamina, Is.EqualTo(100));
        });

        /// <summary>
        /// 武器与护甲独占各自槽位，保存后的属性在入场和返场保持一致。
        /// </summary>
        /// <returns>穿戴、实际战斗快照及读档的逐帧测试。</returns>
        [UnityTest]
        public IEnumerator EquipmentStats_EquipEnterExtractAndReloadKeepFrozenRolls() => UniTask.ToCoroutine(async () =>
        {
            var backend = new FaultSaveBackend();
            ProjectSaveData initial = ProjectSaveData.CreateDefault();
            ProjectItem first = ProjectEquipmentRules.CreateEquipment(new System.Random(20), ProjectItemType.Armor, 1, ProjectItemQuality.Epic);
            ProjectItem second = ProjectEquipmentRules.CreateEquipment(new System.Random(21), ProjectItemType.Armor, 1, ProjectItemQuality.Artifact);
            first.Equipped = true;
            initial.Warehouse.Add(first);
            initial.Warehouse.Add(second);
            await SetupAsync(backend, initial);
            ProjectSession session = ProjectServices.Session;
            Assert.That((await session.EquipAsync(second.Id)).Success, Is.True);
            Assert.That(session.Warehouse.Count(item => item.Equipped && ProjectEquipmentRules.IsArmor(item.Type)), Is.EqualTo(1));
            Assert.That(session.Warehouse.Single(item => item.Id == first.Id).Equipped, Is.False);
            ProjectCharacterStats stats = session.CharacterStats;
            string frozen = JsonUtility.ToJson(session.Warehouse.Single(item => item.Id == second.Id));
            Assert.That(await ProjectServices.Coordinator.StartRunAsync(1).Timeout(TimeSpan.FromSeconds(20)), Is.True);
            ExpeditionRun run = session.CurrentRun;
            Assert.That(run.World.TryGetActor(run.Player, out CombatActorSnapshot actor), Is.True);
            Assert.That(actor.MaxHealth, Is.EqualTo(stats.MaxHealth));
            Assert.That(actor.Attack, Is.EqualTo(stats.Attack));
            Assert.That(actor.Defense, Is.EqualTo(stats.Defense));
            Assert.That(actor.CriticalRating, Is.EqualTo(stats.CriticalRating));
            Assert.That(actor.CriticalDamage, Is.EqualTo(stats.CriticalDamage));
            Assert.That(actor.DodgeChance, Is.EqualTo(stats.DodgeChance));
            Assert.That(actor.DamageIncrease, Is.EqualTo(stats.DamageIncrease));
            Assert.That(actor.DamageReduction, Is.EqualTo(stats.DamageReduction));
            await TriggerExtractionAsync(run);
            Assert.That(await ProjectServices.Coordinator.ReturnHomeAsync(), Is.True);
            Assert.That(JsonUtility.ToJson(session.Warehouse.Single(item => item.Id == second.Id)), Is.EqualTo(frozen));
            Assert.That(await ProjectServices.InitializeAsync(default), Is.True);
            Assert.That(await ProjectServices.Coordinator.InitializeAsync(), Is.True);
            session = ProjectServices.Session;
            Assert.That(JsonUtility.ToJson(session.Warehouse.Single(item => item.Id == second.Id)), Is.EqualTo(frozen));
            Assert.That(session.CharacterStats.MaxHealth, Is.EqualTo(stats.MaxHealth));
            Assert.That((await session.EquipAsync(second.Id)).Success, Is.True);
            Assert.That(session.CharacterStats.MaxHealth, Is.EqualTo(100));
            Assert.That(session.Warehouse.Single(item => item.Type == ProjectItemType.Weapon).Equipped, Is.True);
        });

        /// <summary>
        /// 真实版本二存档升级后保留护甲身份和生命随机值，不重抽属性。
        /// </summary>
        /// <returns>旧版保存和新版打开的逐帧测试。</returns>
        [UnityTest]
        public IEnumerator LegacyEquipment_OpensVersionTwoSaveWithoutReroll()
            => UniTask.ToCoroutine(async () =>
        {
            ProjectSaveData initial = ProjectSaveData.CreateDefault();
            initial.Warehouse.Add(new ProjectItem
            {
                Id = "legacy-armor", Name = "旧制护甲", Type = ProjectItemType.Equipment, RequiredLevel = 1,
                BonusHealth = 25, SellGold = 9, Equipped = true
            });
            await SetupAsync(new FaultSaveBackend(), initial, 2);
            ProjectItem armor = ProjectServices.Session.Warehouse.Single(item => item.Id == "legacy-armor");
            Assert.That(armor.Type, Is.EqualTo(ProjectItemType.Armor));
            Assert.That(armor.Quality, Is.EqualTo(ProjectItemQuality.Common));
            Assert.That(armor.EquipmentVersion, Is.EqualTo(1));
            Assert.That(armor.Affixes, Is.Empty);
            Assert.That(ProjectServices.Session.CharacterStats.MaxHealth, Is.EqualTo(125));
            Assert.That(armor.SellGold, Is.EqualTo(9));
        });

        /// <summary>
        /// 初始化包含真实关卡和界面预制体的 Editor 模拟资源包。
        /// </summary>
        /// <returns>资源清单激活任务。</returns>
        private static async UniTask InitializeEditorPackageAsync()
        {
            var build = EditorSimulateBuildInvoker.Build("GamePackage", (int)EBundleType.VirtualAssetBundle);
            bool loaded = await GameEntry.Get<IResourceModule>().InitializePackageAsync("GamePackage", async package =>
            {
                if (package.InitializeStatus != EOperationStatus.Succeeded)
                {
                    var initialize = package.InitializePackageAsync(new EditorSimulateModeOptions
                    {
                        EditorFileSystemParameters = FileSystemParameters.CreateDefaultEditorFileSystemParameters(build.PackageRootDirectory)
                    });
                    await UniTask.WaitUntil(() => initialize.IsDone).Timeout(TimeSpan.FromSeconds(30));
                    Assert.That(initialize.Status, Is.EqualTo(EOperationStatus.Succeeded), initialize.Error);
                }
                var version = package.RequestPackageVersionAsync(new RequestPackageVersionOptions(true, 30));
                await UniTask.WaitUntil(() => version.IsDone).Timeout(TimeSpan.FromSeconds(30));
                Assert.That(version.Status, Is.EqualTo(EOperationStatus.Succeeded), version.Error);
                var manifest = package.LoadPackageManifestAsync(new LoadPackageManifestOptions(version.PackageVersion, 30));
                await UniTask.WaitUntil(() => manifest.IsDone).Timeout(TimeSpan.FromSeconds(30));
                Assert.That(manifest.Status, Is.EqualTo(EOperationStatus.Succeeded), manifest.Error);
            });
            Assert.That(loaded, Is.True, "Editor 模拟资源包初始化失败。");
        }

        /// <summary>
        /// 建立隔离存档、测试输入和逻辑主界面的项目运行环境。
        /// </summary>
        /// <param name="backend">可重复打开的测试存储。</param>
        /// <param name="initial">可选的预置玩家数据。</param>
        /// <param name="schemaVersion">预置记录的存档版本。</param>
        /// <returns>初始化任务。</returns>
        private static async UniTask SetupAsync(ISaveBackend backend, ProjectSaveData initial = null, int schemaVersion = 3)
        {
            GameEntry.StopFramework();
            var saves = new SaveModule();
            Assert.That(saves.ConfigureBackend(backend).Success, Is.True);
            GameEntry.Register<ISaveModule>(saves);
            GameEntry.Register<IInputModule>(new HeadlessInputModule());
            GameEntry.RegisterDefaults();
            GameEntry.StartFramework();
            await InitializeEditorPackageAsync();
            if (initial != null)
            {
                var definition = new SaveDefinition<ProjectSaveData>(schemaVersion, initial.Copy, ProjectSaveData.Validate, new ProjectSaveCodec());
                var seeded = await saves.OpenAsync(new SaveIdentity("ProjectExtraction", "prototype", "local", "profile"), definition,
                    new SavePolicy { AutoSave = false, FlushOnBackground = false });
                Assert.That(seeded.Result.Success, Is.True, seeded.Result.Message);
                seeded.Session.BindSnapshot(initial.Copy);
                seeded.Session.AcceptRecovery();
                Assert.That(seeded.Session.Activate().Success, Is.True);
                Assert.That(seeded.Session.MarkDirty().Success, Is.True);
                SaveFlushResult receipt = await seeded.Session.FlushAsync();
                Assert.That(receipt.Result.Success, Is.True, receipt.Result.Message);
                seeded.Session.Dispose();
            }
            ISceneModule scenes = GameEntry.Get<ISceneModule>();
            ProjectContent.Register(scenes, GameEntry.Get<IUIModule>());
            scenes.UnregisterScene(ContentIds.HomeScene);
            scenes.RegisterScene(new EmptyHomeScene());
            Assert.That(await ProjectServices.InitializeAsync(default), Is.True);
            Assert.That(await ProjectServices.Coordinator.InitializeAsync(), Is.True);
        }

        /// <summary>
        /// 沿相邻出口进入实际撤离地图，由范围检测触发终局。
        /// </summary>
        /// <param name="run">本局运行对象。</param>
        /// <returns>等待结算结果的任务。</returns>
        private static async UniTask TriggerExtractionAsync(ExpeditionRun run)
        {
            LevelRoomState target = run.Maps.Rooms.First(room => room.HasExtractionCandidate);
            var previous = new Dictionary<int, int> { [run.CurrentRoom.RoomId] = 0 };
            var queue = new Queue<int>();
            queue.Enqueue(run.CurrentRoom.RoomId);
            while (queue.Count > 0 && !previous.ContainsKey(target.RoomId))
            {
                int id = queue.Dequeue();
                run.Maps.TryGetRoom(id, out LevelRoomState room);
                foreach (int next in room.Exits.Values)
                    if (!previous.ContainsKey(next)) { previous[next] = id; queue.Enqueue(next); }
            }
            var path = new Stack<int>();
            for (int id = target.RoomId; id != run.CurrentRoom.RoomId; id = previous[id]) path.Push(id);
            while (path.Count > 0)
            {
                int destination = path.Pop();
                RoomDirection direction = run.CurrentRoom.Exits.First(pair => pair.Value == destination).Key;
                float edge = PrototypeLevelConfig.RoomHalfExtent - 1;
                Vector3 position = direction == RoomDirection.North ? new Vector3(0, 0, edge) :
                    direction == RoomDirection.South ? new Vector3(0, 0, -edge) :
                    direction == RoomDirection.East ? new Vector3(edge, 0, 0) : new Vector3(-edge, 0, 0);
                Assert.That(run.World.TrySetPosition(run.Player, position), Is.True);
                run.RequestInteraction();
                run.Advance(CombatWorld.TickSeconds);
                Assert.That(run.CurrentRoom.RoomId, Is.EqualTo(destination));
            }
            target.MarkCleared();
            Assert.That(run.World.TrySetPosition(run.Player, new Vector3(4, 0, -1)), Is.True);
            run.Advance(CombatWorld.TickSeconds);
            Assert.That(run.IsRunning, Is.False);
            await WaitForPhaseAsync(ProjectRunPhase.ResultsReady);
        }

        /// <summary>
        /// 用真实敌方伤害杀死角色，避免直接调用结算入口。
        /// </summary>
        /// <param name="run">本局运行对象。</param>
        private static void TriggerDeath(ExpeditionRun run)
        {
            Assert.That(run.World.TrySetPosition(run.Player, new Vector3(0, 0, -8)), Is.True);
            CombatEntityRef killer = run.World.CreateActor(new CombatActorSpec
            {
                ActorId = 90000, RoomId = run.CurrentRoom.RoomId, Team = 2,
                Position = new Vector3(0, 0, -9), MaxHealth = 100000, Attack = 10000, Radius = 0.3f,
                AttackRange = 2, AggroRange = 10, MoveSpeed = 0, BasicSkillId = 2
            });
            Assert.That(run.World.TryGetActor(killer, out _), Is.True, "致死敌人必须出生在未重叠位置。");
            for (int tick = 0; tick < 200 && run.IsRunning; tick++) run.Advance(CombatWorld.TickSeconds);
            Assert.That(run.IsRunning, Is.False, "真实敌方伤害应触发死亡。");
        }

        /// <summary>
        /// 有界等待项目阶段，失败时输出当前状态与原因。
        /// </summary>
        /// <param name="phase">期望阶段。</param>
        /// <returns>逐帧等待任务。</returns>
        private static async UniTask WaitForPhaseAsync(ProjectRunPhase phase)
        {
            float until = Time.realtimeSinceStartup + 15;
            while (ProjectServices.Coordinator.Phase != phase && Time.realtimeSinceStartup < until)
            {
                if (!GameEntry.HasHost && ProjectServices.Session.CurrentRun?.IsPresentingDeath == true)
                {
                    var gameplay = (GameplayModule)GameEntry.Get<IGameplayModule>();
                    gameplay.OnUpdate(CombatWorld.TickSeconds);
                    gameplay.OnLateUpdate(CombatWorld.TickSeconds);
                }
                await UniTask.Yield();
            }
            Assert.That(ProjectServices.Coordinator.Phase, Is.EqualTo(phase), ProjectServices.Coordinator.Message);
            await UniTask.Yield();
        }

        /// <summary>
        /// 可注入写入故障且可跨项目会话重用的测试后端。
        /// </summary>
        private sealed class FaultSaveBackend : ISaveBackend
        {
            private readonly MemorySaveBackend inner = new();
            public bool FailWrites;
            public string StorageIdentity => inner.StorageIdentity;
            public int MaxRecordBytes => inner.MaxRecordBytes;

            /// <summary>
            /// 初始化内存存储。
            /// </summary>
            /// <returns>初始化结果。</returns>
            public UniTask<SaveResult> InitializeAsync() => inner.InitializeAsync();

            /// <summary>
            /// 读取已确认的持久记录。
            /// </summary>
            /// <param name="key">记录键。</param>
            /// <returns>读取结果。</returns>
            public UniTask<SaveReadResult> ReadAsync(string key) => inner.ReadAsync(key);

            /// <summary>
            /// 按测试开关注入明确的写入失败。
            /// </summary>
            /// <param name="key">记录键。</param>
            /// <param name="record">完整持久记录。</param>
            /// <returns>写入结果。</returns>
            public UniTask<SaveResult> WriteAsync(string key, string record) => FailWrites
                ? UniTask.FromResult(SaveResult.Fail(SaveError.WriteFailed, "注入写入失败。")) : inner.WriteAsync(key, record);
        }

        /// <summary>
        /// 模拟 Home 在场景进入阶段失败。
        /// </summary>
        private sealed class FailingHomeScene : SceneBase, IProjectHomeReady
        {
            public bool IsReady => false;

            /// <summary>
            /// 注册和正式 Home 相同的路由身份。
            /// </summary>
            public FailingHomeScene() : base(ContentIds.HomeScene) { }

            /// <summary>
            /// 返回有意构造的加载故障。
            /// </summary>
            /// <param name="enterData">场景入参。</param>
            /// <param name="ct">加载取消令牌。</param>
            /// <returns>失败的场景进入任务。</returns>
            protected override UniTask OnEnterSceneAsync(SceneEnterData enterData, CancellationToken ct) =>
                UniTask.FromException(new InvalidOperationException("注入 Home 加载失败。"));
        }

        /// <summary>
        /// 首次退出失败，供重建流程验证释放重试。
        /// </summary>
        private sealed class ExitFailingHomeScene : SceneBase, IProjectHomeReady
        {
            private bool failed;
            public bool IsReady => IsActive;

            /// <summary>
            /// 使用正式 Home 的路由身份。
            /// </summary>
            public ExitFailingHomeScene() : base(ContentIds.HomeScene) { }

            /// <summary>
            /// 首次退出返回故障，之后允许清理。
            /// </summary>
            /// <param name="ct">退出取消令牌。</param>
            /// <returns>首次失败或随后完成的退出任务。</returns>
            protected override UniTask OnExitSceneAsync(CancellationToken ct)
            {
                if (failed) return UniTask.CompletedTask;
                failed = true;
                return UniTask.FromException(new InvalidOperationException("注入 Home 退出失败。"));
            }
        }

        /// <summary>
        /// 验证战斗场景、运行时 HUD 和世界均由本次入场创建。
        /// </summary>
        /// <param name="scenes">场景模块。</param>
        /// <param name="ui">窗口模块。</param>
        /// <param name="session">已初始化的项目会话。</param>
        /// <returns>正在运行的单局。</returns>
        private static ExpeditionRun AssertBattle(ISceneModule scenes, IUIModule ui, ProjectSession session)
        {
            Assert.That(scenes.CurrentScene, Is.TypeOf<ExpeditionScene>());
            Assert.That(ui.OpenedWindows.Any(window => window is BattleHudWindow), Is.True);
            Assert.That(GameEntry.Get<IGameplayModule>().Count, Is.EqualTo(1));
            GameObject visualRoot = GameObject.Find("ExpeditionPresentation");
            Assert.That(visualRoot, Is.Not.Null);
            Assert.That(GameObject.Find("Actor_1")?.transform.IsChildOf(visualRoot.transform), Is.True);
            Assert.That(GameEntry.Get<IResourceModule>().TryEvict<GameObject>("SC_Expedition"), Is.False, "场景持有资源租约时不得淘汰。");
            Assert.That(GameObject.Find("LevelRoom_" + session.CurrentRun.CurrentRoom.RoomId)?.transform.IsChildOf(visualRoot.transform), Is.True);
            Assert.That(session.CurrentRun, Is.Not.Null);
            Assert.That(session.CurrentRun.RoomView, Is.Not.Null);
            return session.CurrentRun;
        }

        /// <summary>
        /// 等待销毁排队完成后检查局外场景和运行时资源。
        /// </summary>
        /// <param name="scenes">场景模块。</param>
        /// <param name="ui">窗口模块。</param>
        /// <param name="session">已初始化的项目会话。</param>
        /// <param name="home">测试用局外场景。</param>
        /// <returns>单帧异步清理检查。</returns>
        private static async UniTask AssertHomeClean(ISceneModule scenes, IUIModule ui, ProjectSession session, EmptyHomeScene home)
        {
            await UniTask.Yield();
            Assert.That(scenes.CurrentScene, Is.SameAs(home));
            Assert.That(ui.OpenedWindows.Any(window => window is BattleHudWindow), Is.False);
            Assert.That(GameEntry.Get<IGameplayModule>().Count, Is.Zero);
            Assert.That(GameObject.Find("ExpeditionPresentation"), Is.Null);
            Assert.That(GameObject.Find("Actor_1"), Is.Null);
            await GameEntry.Get<IResourceModule>().TrimAsync(true);
            Assert.That(GameEntry.Get<IResourceModule>().TryAcquire<GameObject>("SC_Expedition", out _), Is.False, "退出后预制体租约应可清理。");
            Assert.That(session.CurrentRun, Is.Null);
            Assert.That(session.HasPendingSave, Is.False);
        }

        /// <summary>
        /// 测试用局外场景，不加载 Home Prefab。
        /// </summary>
        private sealed class EmptyHomeScene : SceneBase, IProjectHomeReady
        {
            /// <summary>
            /// 创建不含资源加载的局外场景。
            /// </summary>
            public EmptyHomeScene() : base(ContentIds.HomeScene) { }
            public bool IsReady => IsActive;
        }

        /// <summary>
        /// 测试用输入模块，只让显式触屏移动驱动单局。
        /// </summary>
        private sealed class HeadlessInputModule : ModuleBase, IInputModule
        {
            private sealed class EmptyHandle : IDisposable
            {
                /// <summary>
                /// 释放空输入焦点。
                /// </summary>
                public void Dispose() { }
            }

            public InputActionAsset FrameworkActions => null;
            public InputDeviceKind ActiveDevice => default;
            public IReadOnlyList<GamePointer> Pointers => Array.Empty<GamePointer>();
            public event Action<InputDeviceKind> DeviceChanged { add { } remove { } }
            public event Action<GamePointerEvent> PointerEvent { add { } remove { } }
            public event Action<GameGestureEvent> GestureEvent { add { } remove { } }

            /// <summary>
            /// 测试不加载热更输入资产。
            /// </summary>
            /// <param name="location">输入资产地址。</param>
            /// <param name="ct">取消令牌。</param>
            /// <returns>始终为否。</returns>
            public UniTask<bool> LoadGameplayActionsAsync(string location, CancellationToken ct = default) => UniTask.FromResult(false);

            /// <summary>
            /// 清除测试输入状态。
            /// </summary>
            public void UnloadGameplayActions() { }

            /// <summary>
            /// 创建不读取外部资源的测试输入焦点。
            /// </summary>
            /// <param name="map">动作映射名称。</param>
            /// <param name="priority">焦点优先级。</param>
            /// <returns>空焦点句柄。</returns>
            public IDisposable PushContext(string map, int priority = 0) => new EmptyHandle();

            /// <summary>
            /// 创建测试用输入阻断句柄。
            /// </summary>
            /// <param name="reason">阻断原因。</param>
            /// <returns>空阻断句柄。</returns>
            public IDisposable BlockGameplay(string reason) => new EmptyHandle();

            /// <summary>
            /// 返回未按下的动作状态。
            /// </summary>
            /// <param name="id">动作身份。</param>
            /// <returns>默认按钮状态。</returns>
            public InputButtonState GetButton(string id) => default;

            /// <summary>
            /// 返回零值轴。
            /// </summary>
            /// <param name="id">动作身份。</param>
            /// <returns>零值。</returns>
            public float GetAxis(string id) => 0f;

            /// <summary>
            /// 返回零值移动轴。
            /// </summary>
            /// <param name="id">动作身份。</param>
            /// <returns>零向量。</returns>
            public Vector2 GetVector2(string id) => Vector2.zero;

            /// <summary>
            /// 注册无动作回调。
            /// </summary>
            /// <param name="id">动作身份。</param>
            /// <param name="handler">动作处理器。</param>
            /// <returns>空订阅句柄。</returns>
            public IDisposable Subscribe(string id, Action<GameActionEvent> handler) => new EmptyHandle();

            /// <summary>
            /// 注册无返回键回调。
            /// </summary>
            /// <param name="handler">返回动作处理器。</param>
            /// <param name="priority">优先级。</param>
            /// <returns>空订阅句柄。</returns>
            public IDisposable RegisterBackHandler(Func<bool> handler, int priority = 0) => new EmptyHandle();

            /// <summary>
            /// 测试中没有可消费的动作。
            /// </summary>
            /// <param name="id">动作身份。</param>
            /// <returns>始终为否。</returns>
            public bool TryConsumeAction(string id) => false;

            /// <summary>
            /// 测试中没有鼠标或触屏指针。
            /// </summary>
            /// <param name="pointerId">指针身份。</param>
            /// <param name="pointer">默认指针状态。</param>
            /// <returns>始终为否。</returns>
            public bool TryGetPointer(int pointerId, out GamePointer pointer) { pointer = default; return false; }

            /// <summary>
            /// 测试中没有指针世界射线。
            /// </summary>
            /// <param name="camera">目标相机。</param>
            /// <param name="pointerId">指针身份。</param>
            /// <param name="ray">默认射线。</param>
            /// <returns>始终为否。</returns>
            public bool TryGetWorldRay(Camera camera, int pointerId, out Ray ray) { ray = default; return false; }

            /// <summary>
            /// 测试不执行交互重绑定。
            /// </summary>
            /// <param name="id">动作身份。</param>
            /// <param name="bindingId">绑定身份。</param>
            /// <param name="ct">取消令牌。</param>
            /// <returns>始终为否。</returns>
            public UniTask<bool> BeginRebindAsync(string id, Guid bindingId, CancellationToken ct = default) => UniTask.FromResult(false);

            /// <summary>
            /// 测试中没有绑定覆盖。
            /// </summary>
            /// <param name="id">动作身份。</param>
            public void ResetBinding(string id) { }

            /// <summary>
            /// 测试中没有有效绑定文字。
            /// </summary>
            /// <param name="id">动作身份。</param>
            /// <returns>空文字。</returns>
            public string GetBindingDisplayText(string id) => string.Empty;
        }
    }
}
