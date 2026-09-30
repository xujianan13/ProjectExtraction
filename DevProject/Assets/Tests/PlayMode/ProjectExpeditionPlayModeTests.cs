using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;
using GameLogic.Gameplay.Combat;
using GameLogic.Gameplay.Expedition;
using GameLogic.Project;
using GameLogic.Scaffold;
using GameLogic.Scenes.Expedition;
using GameLogic.UI.Battle;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace MiniGame.Tests
{
    /// <summary>
    /// 使用内存存档和真实战斗场景验证完整单局往返。
    /// </summary>
    public sealed class ProjectExpeditionPlayModeTests
    {
        /// <summary>
        /// 每项测试结束时关闭场景和框架宿主。
        /// </summary>
        /// <returns>等待 Unity 销毁延迟对象的逐帧清理流程。</returns>
        [UnityTearDown]
        public IEnumerator TearDown() => UniTask.ToCoroutine(async () =>
        {
            ISceneModule scenes = GameEntry.Get<ISceneModule>();
            if (GameEntry.IsRunning && scenes != null) await scenes.ExitCurrentSceneAsync();
            GameEntry.StopFramework();
            if (GameEntry.HasHost) UnityEngine.Object.Destroy(GameEntry.Bootstrap().gameObject);
            await UniTask.Yield();
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

            ISceneModule scenes = GameEntry.Get<ISceneModule>();
            IUIModule ui = GameEntry.Get<IUIModule>();
            ProjectContent.Register(scenes, ui);
            Assert.That(scenes.GetScene(ProjectServices.BattleSceneName), Is.TypeOf<ExpeditionScene>());
            scenes.UnregisterScene(ContentIds.HomeScene);
            var home = new EmptyHomeScene();
            scenes.RegisterScene(home);
            Assert.That(await ProjectServices.InitializeAsync(default), Is.True);
            await scenes.ChangeSceneAsync(ContentIds.HomeScene);
            ProjectSession session = ProjectServices.Session;
            int stamina = session.Stamina;
            string equippedId = session.Warehouse.Single(item => item.Equipped).Id;

            Assert.That(await session.StartRunAsync(1), Is.True);
            ExpeditionRun first = AssertBattle(scenes, ui, session);
            Assert.That(session.Stamina, Is.EqualTo(stamina - 10));
            Assert.That(first.World.TryGetActor(first.Player, out CombatActorSnapshot before), Is.True);
            first.SetTouchMove(Vector2.right);
            first.Advance(CombatWorld.TickSeconds);
            Assert.That(first.World.TickIndex, Is.GreaterThan(0));
            Assert.That(first.World.TryGetActor(first.Player, out CombatActorSnapshot after), Is.True);
            Assert.That(after.Position.x, Is.GreaterThan(before.Position.x));
            first.SetTouchMove(Vector2.zero);
            Assert.That(await session.FinishRunAsync(true), Is.True);
            await AssertHomeClean(scenes, ui, session, home);
            Assert.That(session.Stamina, Is.EqualTo(stamina - 10));
            Assert.That(session.Warehouse.Any(item => item.Id == equippedId && item.Equipped), Is.True);

            Assert.That(await session.StartRunAsync(1), Is.True);
            ExpeditionRun second = AssertBattle(scenes, ui, session);
            Assert.That(second.RunId, Is.Not.EqualTo(first.RunId));
            Assert.That(session.Stamina, Is.EqualTo(stamina - 20));
            Assert.That(second.Inventory.Any(item => item.Id == equippedId), Is.True);
            Assert.That(await session.FinishRunAsync(false), Is.True);
            await AssertHomeClean(scenes, ui, session, home);
            Assert.That(session.Stamina, Is.EqualTo(stamina - 20));
            Assert.That(session.Warehouse.Any(item => item.Id == equippedId), Is.False);
            Assert.That(session.LastRunSummary, Does.Contain("角色死亡"));
        });

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
            Assert.That(GameObject.Find("ExpeditionPresentation"), Is.Not.Null);
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
            Assert.That(session.CurrentRun, Is.Null);
            Assert.That(session.HasPendingSave, Is.False);
        }

        /// <summary>
        /// 测试用局外场景，不加载 Home Prefab。
        /// </summary>
        private sealed class EmptyHomeScene : SceneBase
        {
            /// <summary>
            /// 创建不含资源加载的局外场景。
            /// </summary>
            public EmptyHomeScene() : base(ContentIds.HomeScene) { }
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
