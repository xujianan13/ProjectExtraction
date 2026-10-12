#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;
using GameLogic;
using GameLogic.Gameplay.Combat;
using GameLogic.Gameplay.Level;
using GameLogic.Project;
using GameLogic.UI.Home;
using NUnit.Framework;
using ProjectExtraction.Tests.Shared;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace MiniGame.Tests
{
    /// <summary>
    /// 从真实宿主场景验证启动、主界面按钮、输入和自动战斗，玩家存档隔离在内存中。
    /// </summary>
    public sealed class ProjectBootPlayModeTests
    {
        private Keyboard keyboard;
        private bool started;
        private InputSettings previousInputSettings;
        private InputSettings testInputSettings;

        /// <summary>
        /// 真实宿主按项目配置使用内存后端，重启后不保留上次已确认的玩家偏好。
        /// </summary>
        /// <returns>两次真实启动与内存会话隔离的逐帧验证。</returns>
        [UnityTest]
        public IEnumerator MainScene_MemorySaveBackendResetsPreferencesOnRestart() => UniTask.ToCoroutine(async () =>
        {
            ScriptableObject bootSettings = Resources.Load<ScriptableObject>("HotUpdateBootSettings");
            Assert.That(bootSettings, Is.Not.Null);
            using var serializedSettings = new SerializedObject(bootSettings);
            SerializedProperty backendMode = serializedSettings.FindProperty("saveBackendMode");
            Assert.That(backendMode, Is.Not.Null);
            Assert.That(backendMode.intValue, Is.EqualTo(4), "真实启动测试只允许项目内存后端配置，禁止接触磁盘存档。");
            Assert.That(backendMode.enumNames[backendMode.enumValueIndex], Is.EqualTo("Memory"));
            string firstStorageIdentity = null;
            for (int launch = 0; launch < 2; launch++)
            {
                GameEntry.StopFramework();
                Assert.That(GameEntry.HasHost, Is.False, "测试开始时不得存在其他框架宿主。");
                GameEntry.Register<ISaveModule>(new SaveModule());
                previousInputSettings = InputSystem.settings;
                testInputSettings = UnityEngine.Object.Instantiate(previousInputSettings);
                testInputSettings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                testInputSettings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                InputSystem.settings = testInputSettings;
                started = true;
                await SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
                await WaitAsync(() => GameMain.IsReady && ProjectServices.Coordinator?.CanStart == true &&
                    !SceneManager.GetSceneByName("Loading").isLoaded, "内存会话启动未进入可出战的主界面。");
                ISaveBackend backend = GameEntry.Get<ISaveModule>().Backend;
                Assert.That(backend, Is.TypeOf<MemorySaveBackend>());
                ProjectSession session = ProjectServices.Session;
                Assert.That(session.Preferences.MusicVolume, Is.EqualTo(1f), "新内存会话不得读取上一次启动的偏好。");
                if (launch == 0)
                {
                    firstStorageIdentity = backend.StorageIdentity;
                    ProjectPreferences draft = session.Preferences;
                    draft.MusicVolume = 0.3f;
                    ProjectOperationResult saved = await session.UpdatePreferencesAsync(draft);
                    Assert.That(saved.Success, Is.True, saved.Message);
                    Assert.That(session.Preferences.MusicVolume, Is.EqualTo(0.3f));
                    await TearDown().ToUniTask();
                }
                else Assert.That(backend.StorageIdentity, Is.Not.EqualTo(firstStorageIdentity));
            }
        });

        /// <summary>
        /// 加载实际启动场景，通过主界面出战，并验证真实输入、自动攻击和死亡返场。
        /// </summary>
        /// <returns>完整启动链的逐帧验证。</returns>
        [UnityTest]
        public IEnumerator MainScene_HomeButtonsEnterBattleAndRealInputDrivesCombat() => UniTask.ToCoroutine(async () =>
        {
            GameEntry.StopFramework();
            Assert.That(GameEntry.HasHost, Is.False, "测试开始时不得存在其他框架宿主。");
            GameEntry.Register<ISaveModule>(new IsolatedSaveModule());
            previousInputSettings = InputSystem.settings;
            testInputSettings = UnityEngine.Object.Instantiate(previousInputSettings);
            testInputSettings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            testInputSettings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings = testInputSettings;
            started = true;
            await SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            await WaitAsync(() => GameMain.IsReady && ProjectServices.Coordinator?.CanStart == true &&
                !SceneManager.GetSceneByName("Loading").isLoaded, "真实启动未进入可出战的主界面。");
            Assert.That(GameEntry.Get<ISaveModule>().Backend, Is.TypeOf<MemorySaveBackend>());
            Assert.That(GameEntry.Get<IInputModule>(), Is.TypeOf<InputModule>());
            IUIModule ui = GameEntry.Get<IUIModule>();
            HomeWindow home = ui.OpenedWindows.OfType<HomeWindow>().Single();
            await UniTask.Yield();
            AssertWindowLayout(home);
            foreach (var entry in new[] { (label: "仓库", id: 2101), (label: "技能", id: 2102), (label: "角色属性", id: 2400) })
            {
                Click(home, entry.label);
                await WaitAsync(() => ui.OpenedWindows.Any(window => window.Id == entry.id && window.State == GameWindowState.Opened),
                    entry.label + "按钮未打开窗口。");
                await UniTask.Yield();
                GameWindow modal = ui.OpenedWindows.Single(window => window.Id == entry.id);
                AssertWindowLayout(modal);
                await AssertModalStableAndCloseAsync(modal);
                await WaitAsync(() => !ui.OpenedWindows.Any(window => window.Id == entry.id), entry.label + "关闭按钮未关闭窗口。");
            }
            GameWindow resultWindow = await ui.OpenWindowAsync(ProjectServices.ResultWindow);
            Assert.That(resultWindow, Is.Not.Null);
            await UniTask.Yield();
            AssertWindowLayout(resultWindow);
            Assert.That(await ui.CloseWindowAsync(ProjectServices.ResultWindow), Is.True);
            int stamina = ProjectServices.Session.Stamina;
            Click(home, "出战");
            await WaitAsync(() => ProjectServices.Session.CurrentRun?.IsRunning == true &&
                ProjectServices.Coordinator.Phase == ProjectRunPhase.Playing, "出战按钮未进入运行中的关卡。");
            var run = ProjectServices.Session.CurrentRun;
            Assert.That(run.IsReady, Is.True);
            Assert.That(run.RegionView, Is.Not.Null);
            Assert.That(ProjectServices.Session.Stamina, Is.EqualTo(stamina - PrototypeRules.EntryCost));
            Assert.That(ui.OpenedWindows.Any(window => window.Id == ProjectServices.BattleHudWindow), Is.True);
            Assert.That(run.World.TryGetActor(run.Player, out CombatActorSnapshot before), Is.True);
            keyboard = InputSystem.AddDevice<Keyboard>();
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.D));
            InputSystem.Update();
            Assert.That(keyboard.dKey.isPressed, Is.True, "测试键盘状态未被 Input System 接收。");
            await WaitAsync(() => run.World.TryGetActor(run.Player, out CombatActorSnapshot moved) &&
                moved.Position.x > before.Position.x + 0.1f, "真实输入模块未把 D 键传入角色移动。", 5f);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            InputSystem.Update();
            await UniTask.Yield();
            Assert.That(run.World.TryGetActor(run.Player, out CombatActorSnapshot player), Is.True);
            RegionSpawnSlotState slot = run.CurrentRegion.Slots.First(value => value.Phase == RegionSpawnPhase.Alive);
            Assert.That(run.World.TryFindActor(slot.CurrentActorId, out CombatEntityRef enemy), Is.True);
            Assert.That(run.World.TryAddAttributeBonus(enemy, CombatAttributeKind.MaxHealth, 1000), Is.True);
            Assert.That(run.World.TryGetActor(enemy, out CombatActorSnapshot enemyBefore), Is.True);
            Assert.That(CombatTestPositioning.TryPlaceAdjacent(run.World, run.Player, enemy), Is.True);
            await WaitAsync(() => run.World.TryGetActor(enemy, out CombatActorSnapshot target) && target.Health < enemyBefore.Health,
                "正常玩法更新未触发范围内自动攻击伤害。", 5f);
            Assert.That(run.World.TickIndex, Is.GreaterThan(0));
            Assert.That(run.IsRunning, Is.True);
            Assert.That(run.World.TryGetActor(run.Player, out player), Is.True);
            Assert.That(run.World.TryAddAttributeBonus(enemy, CombatAttributeKind.Attack, 100000), Is.True);
            Assert.That(run.World.TryAddAttributeBonus(enemy, CombatAttributeKind.Defense, 100000), Is.True);
            Assert.That(CombatTestPositioning.TryPlaceAdjacent(run.World, run.Player, enemy), Is.True);
            await WaitAsync(() => run.IsPresentingDeath, "真实宿主未启动死亡收尾。", 5f);
            long finalTick = run.World.TickIndex;
            Assert.That(run.IsRunning, Is.False);
            Assert.That(GameEntry.Get<ISceneModule>().CurrentScene, Is.Not.Null);
            await WaitAsync(() => ProjectServices.Coordinator.CanReturn,
                "真实宿主未在倒下动画结束后显示可返回的结果页。", 5f);
            Assert.That(run.World.TickIndex, Is.EqualTo(finalTick));
            Assert.That(run.DeathPresentationElapsed, Is.EqualTo(GameLogic.Gameplay.Expedition.ExpeditionRun.DeathPresentationDuration));
            Assert.That(await ProjectServices.Coordinator.ReturnHomeAsync(), Is.True);
            Assert.That(ProjectServices.Coordinator.CanStart, Is.True);
        });

        /// <summary>
        /// 连续多帧验证模态遮罩顺序与关闭按钮射线命中，并通过实际命中对象关闭窗口。
        /// </summary>
        /// <param name="window">待验证的模态窗口。</param>
        /// <returns>层级稳定性及关闭交互的逐帧验证。</returns>
        private static async UniTask AssertModalStableAndCloseAsync(GameWindow window)
        {
            await UniTask.NextFrame(PlayerLoopTiming.LastPostLateUpdate);
            RectTransform root = window.RootTransform;
            Transform mask = root.parent.Find("[WindowMask]");
            Assert.That(mask, Is.Not.Null, window.Config.Name + "缺少模态遮罩。");
            Assert.That(EventSystem.current, Is.Not.Null);
            UnityEngine.UI.Button close = root.GetComponentsInChildren<UnityEngine.UI.Button>(false)
                .Single(button => button.GetComponentInChildren<UnityEngine.UI.Text>().text == "关闭");
            var pointer = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
            var hits = new List<RaycastResult>();
            int maskIndex = mask.GetSiblingIndex();
            int windowIndex = root.GetSiblingIndex();
            GameObject clickTarget = null;
            for (int frame = 0; frame < 12; frame++)
            {
                await UniTask.NextFrame(PlayerLoopTiming.LastPostLateUpdate);
                Canvas.ForceUpdateCanvases();
                Assert.That(mask.GetSiblingIndex(), Is.LessThan(root.GetSiblingIndex()), window.Config.Name + "遮罩盖住了窗口。");
                Assert.That(mask.GetSiblingIndex(), Is.EqualTo(maskIndex), window.Config.Name + "遮罩顺序逐帧变化。");
                Assert.That(root.GetSiblingIndex(), Is.EqualTo(windowIndex), window.Config.Name + "窗口顺序逐帧变化。");
                var buttonRect = (RectTransform)close.transform;
                pointer.position = RectTransformUtility.WorldToScreenPoint(null, buttonRect.TransformPoint(buttonRect.rect.center));
                hits.Clear();
                EventSystem.current.RaycastAll(pointer, hits);
                Assert.That(hits, Is.Not.Empty);
                clickTarget = ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject);
                Assert.That(clickTarget, Is.SameAs(close.gameObject), window.Config.Name + "关闭按钮被其他图层拦截。");
            }
            Assert.That(close.IsInteractable(), Is.True);
            ExecuteEvents.Execute(clickTarget, pointer, ExecuteEvents.pointerClickHandler);
        }

        /// <summary>
        /// 验证真实窗口主操作在屏幕范围内，滚动视口有面积且 Home 三段互不重叠。
        /// </summary>
        /// <param name="window">已经打开并完成布局的窗口。</param>
        private static void AssertWindowLayout(GameWindow window)
        {
            Canvas.ForceUpdateCanvases();
            RectTransform root = window.RootTransform;
            foreach (ScrollRect scroll in root.GetComponentsInChildren<ScrollRect>(false))
            {
                Assert.That(scroll.viewport, Is.Not.Null, scroll.name + " 缺少视口。");
                Assert.That(scroll.viewport.rect.width, Is.GreaterThan(1f), scroll.name + " 视口宽度为零。");
                Assert.That(scroll.viewport.rect.height, Is.GreaterThan(1f), scroll.name + " 视口高度为零。");
            }
            foreach (Button button in root.GetComponentsInChildren<Button>(false))
                AssertInside(root, (RectTransform)button.transform, button.name);
            if (window is not HomeWindow) return;
            RectTransform surface = root.Find("HomeContent") as RectTransform;
            Assert.That(surface, Is.Not.Null);
            RectTransform layout = surface.Find("Layout") as RectTransform;
            Assert.That(layout, Is.Not.Null);
            RectTransform header = layout.Find("Header") as RectTransform;
            RectTransform body = layout.Find("Main") as RectTransform;
            RectTransform footer = layout.Find("Footer") as RectTransform;
            Assert.That(header, Is.Not.Null);
            Assert.That(body, Is.Not.Null);
            Assert.That(footer, Is.Not.Null);
            AssertInside(surface, header, "Home 顶栏");
            AssertInside(surface, body, "Home 正文");
            AssertInside(surface, footer, "Home 底栏");
            Assert.That(Bounds(header).bottom, Is.GreaterThanOrEqualTo(Bounds(body).top - 1f), "Home 顶栏与正文重叠。");
            Assert.That(Bounds(body).bottom, Is.GreaterThanOrEqualTo(Bounds(footer).top - 1f), "Home 正文与底栏重叠。");
        }

        /// <summary>
        /// 验证子矩形位于窗口范围内。
        /// </summary>
        /// <param name="parent">允许范围。</param>
        /// <param name="child">待检查节点。</param>
        /// <param name="name">断言名称。</param>
        private static void AssertInside(RectTransform parent, RectTransform child, string name)
        {
            var outer = Bounds(parent);
            var inner = Bounds(child);
            Assert.That(inner.left, Is.GreaterThanOrEqualTo(outer.left - 1f), name + " 左侧越界。");
            Assert.That(inner.right, Is.LessThanOrEqualTo(outer.right + 1f), name + " 右侧越界。");
            Assert.That(inner.bottom, Is.GreaterThanOrEqualTo(outer.bottom - 1f), name + " 底部越界。");
            Assert.That(inner.top, Is.LessThanOrEqualTo(outer.top + 1f), name + " 顶部越界。");
        }

        /// <summary>
        /// 读取屏幕空间的矩形边界。
        /// </summary>
        /// <param name="rect">待检查矩形。</param>
        /// <returns>左、右、下、上边界。</returns>
        private static (float left, float right, float bottom, float top) Bounds(RectTransform rect)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            return (corners[0].x, corners[2].x, corners[0].y, corners[2].y);
        }
        /// <summary>
        /// 调用实际窗口按钮绑定的业务回调，不替换导航方法。
        /// </summary>
        /// <param name="window">当前主界面。</param>
        /// <param name="label">按钮显示文本。</param>
        private static void Click(GameWindow window, string label)
        {
            Button button = window.RootTransform.GetComponentsInChildren<Button>(false).Single(item =>
                item.GetComponentInChildren<Text>().text == label);
            Assert.That(button.interactable, Is.True, label + "按钮不可用。");
            button.onClick.Invoke();
        }

        /// <summary>
        /// 等待真实 Unity 更新驱动状态变化，超时明确失败。
        /// </summary>
        /// <param name="ready">完成条件。</param>
        /// <param name="message">失败说明。</param>
        /// <param name="seconds">允许等待秒数。</param>
        /// <returns>逐帧等待任务。</returns>
        private static async UniTask WaitAsync(Func<bool> ready, string message, float seconds = 40f)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (!ready() && Time.realtimeSinceStartup < deadline) await UniTask.Yield();
            bool completed = ready();
            if (!completed)
            {
                var run = ProjectServices.Session?.CurrentRun;
                string diagnostics = string.Join("; ", GameEntry.Get<IDiagnosticsModule>().CaptureSnapshot().Sections
                    .Where(section => section.Name == "Input").SelectMany(section => section.Values).Select(value => value.Name + "=" + value.Value));
                message += $" Tick={run?.World.TickIndex} TimeScale={Time.timeScale} Background={GameEntry.IsInBackground} " +
                    $"Move={GameEntry.Get<IInputModule>()?.GetVector2("Gameplay/Move")} Key={Keyboard.current?.dKey.isPressed} {diagnostics}";
            }
            Assert.That(completed, Is.True, message);
        }

        /// <summary>
        /// 释放本测试的真实宿主、场景和虚拟键盘。
        /// </summary>
        /// <returns>清理协程。</returns>
        [UnityTearDown]
        public IEnumerator TearDown() => UniTask.ToCoroutine(async () =>
        {
            if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
            keyboard = null;
            if (!started) return;
            if (ProjectServices.Coordinator != null) await ProjectServices.Coordinator.CloseAsync();
            foreach (string name in new[] { "[HotUpdateBootstrap]", "Framework" })
            {
                GameObject instance = GameObject.Find(name);
                if (instance != null) UnityEngine.Object.Destroy(instance);
            }
            await UniTask.Yield();
            GameEntry.StopFramework();
            InputSystem.settings = previousInputSettings;
            UnityEngine.Object.Destroy(testInputSettings);
            testInputSettings = null;
            previousInputSettings = null;
            GameEntry host = UnityEngine.Object.FindObjectOfType<GameEntry>();
            if (host != null) UnityEngine.Object.Destroy(host.gameObject);
            await UniTask.Yield();
            Scene cleanup = SceneManager.CreateScene("ProjectBootTestCleanup");
            SceneManager.SetActiveScene(cleanup);
            foreach (string name in new[] { "Loading", "Main" })
            {
                Scene scene = SceneManager.GetSceneByName(name);
                if (scene.IsValid() && scene.isLoaded) await SceneManager.UnloadSceneAsync(scene);
            }
            started = false;
        });

        /// <summary>
        /// 仅替换存储介质，仍委托正式存档模块执行迁移、事务和生命周期。
        /// </summary>
        private sealed class IsolatedSaveModule : ModuleBase, ISaveModule
        {
            private readonly SaveModule inner = new();
            private readonly MemorySaveBackend memory = new();
            public ISaveBackend Backend => inner.Backend;
            public override int Priority => inner.Priority;

            /// <summary>
            /// 接入正式存档模块生命周期。
            /// </summary>
            public override void OnInit() => inner.OnInit();

            /// <summary>
            /// 驱动正式存档会话。
            /// </summary>
            /// <param name="deltaTime">本帧时间。</param>
            public override void OnUpdate(float deltaTime) => inner.OnUpdate(deltaTime);

            /// <summary>
            /// 释放正式存档会话。
            /// </summary>
            public override void OnShutdown() => inner.OnShutdown();

            /// <summary>
            /// 将宿主后端请求固定到本测试内存，避免读写玩家存档。
            /// </summary>
            /// <param name="backend">宿主选定的后端，仅校验其存在。</param>
            /// <returns>正式存档模块的配置结果。</returns>
            public SaveResult ConfigureBackend(ISaveBackend backend)
            {
                Assert.That(backend, Is.Not.Null);
                return inner.ConfigureBackend(memory);
            }

            /// <summary>
            /// 委托正式打开和迁移流程。
            /// </summary>
            /// <typeparam name="T">存档数据类型。</typeparam>
            /// <param name="identity">存档身份。</param>
            /// <param name="definition">数据定义。</param>
            /// <param name="policy">保存策略。</param>
            /// <param name="ct">取消令牌。</param>
            /// <returns>打开结果。</returns>
            public UniTask<SaveOpenResult<T>> OpenAsync<T>(SaveIdentity identity, SaveDefinition<T> definition,
                SavePolicy policy = null, CancellationToken ct = default) => inner.OpenAsync(identity, definition, policy, ct);

            /// <summary>
            /// 委托正式恢复检查。
            /// </summary>
            /// <typeparam name="T">存档数据类型。</typeparam>
            /// <param name="identity">存档身份。</param>
            /// <param name="definition">数据定义。</param>
            /// <param name="ct">取消令牌。</param>
            /// <returns>恢复候选。</returns>
            public UniTask<SaveRecoveryInspection> InspectRecoveryAsync<T>(SaveIdentity identity, SaveDefinition<T> definition,
                CancellationToken ct = default) => inner.InspectRecoveryAsync(identity, definition, ct);

            /// <summary>
            /// 委托正式恢复事务。
            /// </summary>
            /// <typeparam name="T">存档数据类型。</typeparam>
            /// <param name="candidate">恢复候选。</param>
            /// <param name="targetIdentity">目标身份。</param>
            /// <param name="definition">数据定义。</param>
            /// <param name="policy">保存策略。</param>
            /// <param name="ct">取消令牌。</param>
            /// <returns>恢复结果。</returns>
            public UniTask<SaveOpenResult<T>> RestoreToNewSlotAsync<T>(SaveRecoveryCandidate candidate, SaveIdentity targetIdentity,
                SaveDefinition<T> definition, SavePolicy policy = null, CancellationToken ct = default) =>
                inner.RestoreToNewSlotAsync(candidate, targetIdentity, definition, policy, ct);
        }
    }
}
#endif
