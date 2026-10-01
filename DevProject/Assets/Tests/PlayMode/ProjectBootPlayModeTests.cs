#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;
using GameLogic;
using GameLogic.Gameplay.Combat;
using GameLogic.Project;
using GameLogic.UI.Home;
using NUnit.Framework;
using UnityEngine;
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
        /// 加载实际启动场景并通过主界面进入可移动、可自动造成伤害的战斗。
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
            foreach (var entry in new[] { (label: "仓库", id: 2101), (label: "技能", id: 2102), (label: "角色属性", id: 2400) })
            {
                Click(home, entry.label);
                await WaitAsync(() => ui.OpenedWindows.Any(window => window.Id == entry.id && window.State == GameWindowState.Opened),
                    entry.label + "按钮未打开窗口。");
                Assert.That(await ui.CloseWindowAsync(entry.id), Is.True);
            }
            int stamina = ProjectServices.Session.Stamina;
            Click(home, "出战");
            await WaitAsync(() => ProjectServices.Session.CurrentRun?.IsRunning == true &&
                ProjectServices.Coordinator.Phase == ProjectRunPhase.Playing, "出战按钮未进入运行中的关卡。");
            var run = ProjectServices.Session.CurrentRun;
            Assert.That(run.IsReady, Is.True);
            Assert.That(run.RoomView, Is.Not.Null);
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
            CombatEntityRef enemy = run.World.CreateActor(new CombatActorSpec
            {
                ActorId = 900001, RoomId = run.CurrentRoom.RoomId, Team = 2, Position = player.Position + Vector3.forward,
                MaxHealth = 1000, Radius = 0.3f, MoveSpeed = 0, AttackRange = 0, AggroRange = 0
            });
            Assert.That(enemy.Id, Is.Not.Zero);
            await WaitAsync(() => run.World.TryGetActor(enemy, out CombatActorSnapshot target) && target.Health < 1000,
                "正常玩法更新未触发范围内自动攻击伤害。", 5f);
            Assert.That(run.World.TickIndex, Is.GreaterThan(0));
            Assert.That(run.IsRunning, Is.True);
        });

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
