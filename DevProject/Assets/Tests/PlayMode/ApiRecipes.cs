using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;
using GameLogic.Tables;
using NUnit.Framework;
using UnityEngine;

namespace MiniGame.Tests
{
    /// <summary>
    /// 可由测试程序集编译的框架任务示例；调用方在进入对应生命周期后使用。
    /// </summary>
    public static class ApiRecipes
    {
        /// <summary>
        /// 登记逻辑场景并切换到它。
        /// </summary>
        /// <param name="scenes">已初始化的场景模块。</param>
        /// <param name="scene">新建的项目逻辑场景。</param>
        /// <param name="ct">进入取消令牌。</param>
        /// <returns>切换完成后的异步操作。</returns>
        public static async UniTask EnterSceneAsync(ISceneModule scenes, SceneBase scene, CancellationToken ct)
        {
            scenes.RegisterScene(scene);
            await scenes.ChangeSceneAsync(scene, ct: ct);
        }

        /// <summary>
        /// 启动玩法并在独立清理令牌下等待退出。
        /// </summary>
        /// <param name="gameplays">玩法模块。</param>
        /// <param name="gameplay">本次新建的玩法实例。</param>
        /// <param name="enterToken">进入取消令牌。</param>
        /// <param name="exitToken">独立的退出令牌。</param>
        /// <returns>玩法完成退出后的异步操作。</returns>
        public static async UniTask RunGameplayAsync(IGameplayModule gameplays, IGameplay gameplay, CancellationToken enterToken, CancellationToken exitToken)
        {
            GameplayHandle handle = await gameplays.StartAsync(gameplay, ct: enterToken);
            if (gameplays.Contains(handle)) await gameplays.StopAsync(handle, exitToken);
        }

        /// <summary>
        /// 打开并关闭由 UI 管理器持有资源租约的窗口。
        /// </summary>
        /// <param name="ui">已完成窗口配置的 UI 模块。</param>
        /// <param name="id">已注册的窗口编号。</param>
        /// <param name="openToken">打开取消令牌。</param>
        /// <param name="closeToken">独立的关闭令牌。</param>
        /// <returns>窗口是否成功关闭。</returns>
        public static async UniTask<bool> ShowWindowAsync(IUIModule ui, int id, CancellationToken openToken, CancellationToken closeToken)
        {
            GameWindow window = await ui.OpenWindowAsync(id, ct: openToken);
            return window != null && await ui.CloseWindowAsync(id, ct: closeToken);
        }

        /// <summary>
        /// 为窗口配置固定高度的虚拟列表。
        /// </summary>
        /// <param name="list">窗口中的列表组件。</param>
        /// <param name="scroll">已经绑定视口和空内容根的滚动组件。</param>
        /// <param name="itemPrefab">由业务 Bind 方法展示数据的条目预制体。</param>
        /// <param name="count">当前数据行数。</param>
        public static void ShowVirtualList(GameVirtualList list, UnityEngine.UI.ScrollRect scroll, GameVirtualListItem itemPrefab, int count)
        {
            list.Configure(scroll, itemPrefab, extent: 96f, gap: 8f);
            list.SetCount(count);
        }

        /// <summary>
        /// 把一份 Sprite 资源租约交给本轮作用域管理。
        /// </summary>
        /// <param name="scope">发起调用时捕获的活跃作用域。</param>
        /// <param name="resources">资源模块。</param>
        /// <param name="address">Sprite 地址。</param>
        /// <returns>已加载资源；作用域结束时返回空，取消与加载失败按资源模块契约传播。</returns>
        public static async UniTask<Sprite> LoadIconAsync(LifetimeScope scope, IResourceModule resources, string address)
        {
            ResourceLease<Sprite> lease = await scope.AcquireAsync<Sprite>(resources, address, ct: scope.Token);
            return scope.IsActive ? lease?.Asset : null;
        }

        /// <summary>
        /// 创建池、生成一次实例、归还并等待池释放。
        /// </summary>
        /// <param name="pools">Prefab 池模块。</param>
        /// <param name="address">Prefab 地址。</param>
        /// <param name="enterToken">创建取消令牌。</param>
        /// <returns>池释放完成后的异步操作。</returns>
        public static async UniTask SpawnOnceAsync(IPrefabPoolModule pools, string address, CancellationToken enterToken)
        {
            PrefabPool pool = await pools.CreatePoolAsync(address, ct: enterToken);
            if (pool == null) return;
            PrefabInstance instance = pool.Spawn();
            if (instance != null) pool.Despawn(instance);
            await pool.DisposeAsync();
        }

        /// <summary>
        /// 将输入上下文和订阅交给同一个作用域。
        /// </summary>
        /// <param name="scope">本轮玩法作用域。</param>
        /// <param name="input">输入模块。</param>
        /// <param name="map">已加载动作图的 Map 名。</param>
        /// <param name="actionId">已生成的动作标识。</param>
        /// <param name="onAction">输入处理函数。</param>
        public static void BindInput(LifetimeScope scope, IInputModule input, string map, string actionId, Action<GameActionEvent> onAction)
        {
            scope.PushContext(input, map);
            scope.SubscribeInput(input, actionId, onAction);
        }

        /// <summary>
        /// 在作用域内订阅事件并安排一次计时器。
        /// </summary>
        /// <param name="scope">本轮作用域。</param>
        /// <param name="events">事件模块。</param>
        /// <param name="timers">计时器模块。</param>
        /// <param name="eventId">项目分配的事件编号。</param>
        /// <param name="onEvent">事件处理函数。</param>
        public static void BindEventAndTimer(LifetimeScope scope, IEventModule events, ITimerModule timers, EventId eventId, Action onEvent)
        {
            scope.Subscribe(events, eventId, onEvent);
            scope.Once(timers, 0.5f, () => events.Publish(eventId));
        }

        /// <summary>
        /// 播放由作用域自动停止的浮点动画。
        /// </summary>
        /// <param name="scope">本轮作用域。</param>
        /// <param name="tweens">Tween 模块。</param>
        /// <param name="setter">属性写入函数。</param>
        public static void Animate(LifetimeScope scope, ITweenModule tweens, Action<float> setter)
        {
            tweens.Play(Tween.To(0f, 1f, 0.25f, setter), scope, unscaledTime: true);
        }

        /// <summary>
        /// 播放循环音效，并在本轮作用域结束时停止。
        /// </summary>
        /// <param name="scope">本轮作用域。</param>
        /// <param name="audio">音频模块。</param>
        /// <param name="address">音频地址。</param>
        /// <returns>播放是否取得有效句柄。</returns>
        public static async UniTask<bool> PlayLoopAsync(LifetimeScope scope, IAudioModule audio, string address)
        {
            AudioHandle handle = await audio.PlayAsync(address, AudioChannel.Sfx, loop: true, ct: scope.Token);
            if (!scope.IsActive) { audio.Stop(handle); return false; }
            scope.Defer(() => audio.Stop(handle));
            return audio.IsPlaying(handle);
        }

        /// <summary>
        /// 加载项目当前生成的全部 Excel 配置表；空表项目同样可用。
        /// </summary>
        /// <param name="resources">已初始化的资源模块。</param>
        /// <param name="ct">加载取消令牌。</param>
        /// <returns>配置表加载任务。</returns>
        public static UniTask LoadTablesAsync(IResourceModule resources, CancellationToken ct) => GameTables.LoadAsync(resources, ct);

        /// <summary>
        /// 读取运行诊断快照。
        /// </summary>
        /// <param name="diagnostics">诊断模块。</param>
        /// <returns>当前模块诊断快照。</returns>
        public static DiagnosticSnapshot Snapshot(IDiagnosticsModule diagnostics) => diagnostics.CaptureSnapshot();

        /// <summary>
        /// 将项目单局工厂交给通用流程，并显式等待关闭。
        /// </summary>
        /// <param name="factory">每局创建独立会话的项目工厂。</param>
        /// <param name="enterToken">进入取消令牌。</param>
        /// <param name="exitToken">独立的退出清理令牌。</param>
        /// <returns>是否成功进入且关闭流程。</returns>
        public static async UniTask<bool> RunRoundFlowAsync(IGameRoundFactory factory, CancellationToken enterToken, CancellationToken exitToken)
        {
            var flow = new GameRoundFlow(factory);
            bool started = await flow.StartLevelAsync(factory.FirstLevelId, enterToken);
            bool closed = await flow.CloseAsync(exitToken);
            return started && closed;
        }

        /// <summary>
        /// 为项目存档读取一个新会话，调用方随后应激活、保存并等待关闭。
        /// </summary>
        /// <typeparam name="T">项目存档数据类型。</typeparam>
        /// <param name="save">正式存档模块。</param>
        /// <param name="identity">项目和玩家的存档身份。</param>
        /// <param name="definition">版本、默认值与校验定义。</param>
        /// <param name="ct">本次打开的取消令牌。</param>
        /// <returns>含状态与未激活会话的打开结果。</returns>
        public static UniTask<SaveOpenResult<T>> OpenSaveAsync<T>(ISaveModule save, SaveIdentity identity, SaveDefinition<T> definition, CancellationToken ct)
            => save.OpenAsync(identity, definition, ct: ct);
    }

    /// <summary>
    /// 单局工厂和会话的可编译接入示例，项目在 EnterAsync 填入资源和规则。
    /// </summary>
    public sealed class ApiRoundFactory : IGameRoundFactory
    {
        /// <summary>
        /// 第一关标识。
        /// </summary>
        public int FirstLevelId => 1;

        /// <summary>
        /// 检查项目关卡编号。
        /// </summary>
        /// <param name="levelId">待检查编号。</param>
        /// <returns>示例关卡是否存在。</returns>
        public bool ContainsLevel(int levelId) => levelId == FirstLevelId;

        /// <summary>
        /// 查询下一关。
        /// </summary>
        /// <param name="levelId">当前关卡编号。</param>
        /// <returns>零表示本示例没有下一关。</returns>
        public int GetNextLevelId(int levelId) => 0;

        /// <summary>
        /// 每局创建独立会话。
        /// </summary>
        /// <param name="levelId">关卡编号。</param>
        /// <param name="roundId">本局唯一编号。</param>
        /// <returns>新会话。</returns>
        public IGameRoundSession Create(int levelId, string roundId) => new ApiRoundSession(levelId, roundId);
    }

    /// <summary>
    /// 一个最小单局会话，展示生命周期和结算连接点。
    /// </summary>
    public sealed class ApiRoundSession : IGameRoundSession
    {
        private readonly int _levelId;
        private readonly string _roundId;
        private LifetimeScope _scope;

        /// <summary>
        /// 保存本局身份。
        /// </summary>
        /// <param name="levelId">关卡编号。</param>
        /// <param name="roundId">本局唯一编号。</param>
        public ApiRoundSession(int levelId, string roundId) { _levelId = levelId; _roundId = roundId; }

        /// <summary>
        /// 本局完成时的结果事件。
        /// </summary>
        public event Action<GameRoundResult> Completed;

        /// <summary>
        /// 建立本轮所有权，项目内容应在此加载并登记到作用域。
        /// </summary>
        /// <param name="ct">进入取消令牌。</param>
        /// <returns>进入完成后的异步操作。</returns>
        public UniTask EnterAsync(CancellationToken ct)
        {
            _scope = new LifetimeScope("ApiRound", ct);
            return UniTask.CompletedTask;
        }

        /// <summary>
        /// 发出项目规则计算出的结算结果。
        /// </summary>
        /// <param name="won">是否胜利。</param>
        /// <param name="score">本局分数。</param>
        /// <param name="elapsedSeconds">本局耗时。</param>
        public void Complete(bool won, int score, float elapsedSeconds)
        {
            if (_scope?.IsActive == true) Completed?.Invoke(new GameRoundResult(_roundId, _levelId, won, score, elapsedSeconds));
        }

        /// <summary>
        /// 本局暂停状态应由项目规则和内容实现。
        /// </summary>
        /// <param name="paused">是否暂停。</param>
        public void SetPaused(bool paused) { }

        /// <summary>
        /// 停止本轮工作并完成项目异步清理。
        /// </summary>
        /// <param name="ct">独立清理令牌。</param>
        /// <returns>清理完成后的异步操作。</returns>
        public UniTask ExitAsync(CancellationToken ct) { Dispose(); return UniTask.CompletedTask; }

        /// <summary>
        /// 同步释放本轮作用域，供强制退出兜底。
        /// </summary>
        public void Dispose() { _scope?.Dispose(); _scope = null; Completed = null; }
    }

    /// <summary>
    /// API 示例中具有行为意义的生命周期验证。
    /// </summary>
    public sealed class ApiRecipesTests
    {
        /// <summary>
        /// 验证作用域释放后事件订阅不再派发。
        /// </summary>
        [Test]
        public void ScopeDisposalStopsEventDelivery()
        {
            var events = new EventModule();
            var scope = new LifetimeScope("ApiRecipeTest");
            int count = 0;
            scope.Subscribe(events, new EventId(260926), () => count++);
            events.Publish(new EventId(260926));
            scope.Dispose();
            events.Publish(new EventId(260926));
            Assert.AreEqual(1, count);
        }
    }
}
