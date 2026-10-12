using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;
using GameLogic.Gameplay.Combat;
using GameLogic.Gameplay.Expedition;
using GameLogic.Gameplay.Level;
using GameLogic.Project;
using GameLogic.Tables;
using NUnit.Framework;
using ProjectExtraction.Tests.Shared;
using UnityEngine;

namespace ProjectExtraction.Tests
{
    /// <summary>
    /// 验证唯一会话中的完整 tick FIFO、不可变候选和物理写入独占门禁。
    /// </summary>
    public sealed class ProjectRunPersistenceTests
    {
        private IUIModule previousUi;
        private TestUi ui;

        /// <summary>
        /// 暂时注册可控制关闭回执的窗口模块，恢复测试不加载场景或真实资源。
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            Assert.That(GameEntry.IsRunning, Is.False);
            TestGameTables.LoadAll();
            previousUi = GameEntry.Get<IUIModule>();
            ui = (TestUi)GameEntry.Register<IUIModule>(new TestUi());
        }

        /// <summary>
        /// 恢复全局模块注册，避免测试窗口影响其他用例。
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            if (previousUi != null) GameEntry.Register(previousUi);
            else
            {
                object manager = typeof(GameEntry).GetField("_manager", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                ((IDictionary)manager.GetType().GetField("_modules", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(manager))
                    .Remove(typeof(IUIModule));
                ((IList)manager.GetType().GetField("_ordered", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(manager)).Remove(ui);
            }
        }

        /// <summary>
        /// 只提供本组恢复测试需要的窗口关闭回执，不创建 Unity 对象。
        /// </summary>
        private sealed class TestUi : IUIModule
        {
            public bool HoldClose;
            public bool Closed;
            public int CloseCalls;
            private UniTaskCompletionSource<bool> closeReceipt;
            public int Priority => 0;

            /// <summary>
            /// 初始化无资源测试模块。
            /// </summary>
            public void OnInit() {}

            /// <summary>
            /// 更新无资源测试模块。
            /// </summary>
            /// <param name="deltaTime">当前帧时长。</param>
            public void OnUpdate(float deltaTime) {}

            /// <summary>
            /// 关闭无资源测试模块。
            /// </summary>
            public void OnShutdown() {}

            /// <summary>
            /// 确认恢复窗口关闭以继续业务续体。
            /// </summary>
            public void ReleaseClose() => closeReceipt.TrySetResult(true);

            /// <summary>
            /// UI 根节点是否已建立。
            /// </summary>
            public bool IsReady => true;

            /// <summary>
            /// 当前 UI 是否需要阻断玩法输入。
            /// </summary>
            public bool BlocksGameplayInput => !Closed;

            /// <summary>
            /// 当前已打开的业务窗口。
            /// </summary>
            public IReadOnlyList<GameWindow> OpenedWindows => Array.Empty<GameWindow>();

            /// <summary>
            /// 配置反馈层及所有业务窗口共用的屏幕缩放参考尺寸。
            /// </summary>
            /// <param name="referenceResolution">参考分辨率，宽高必须为有限正数。</param>
            /// <param name="matchWidthOrHeight">宽高匹配权重，范围为零到一。</param>
            /// <returns>两个 Canvas 均已更新时为真；未初始化或参数无效时为假。</returns>
            public bool TryConfigureCanvas(Vector2 referenceResolution, float matchWidthOrHeight = 0.5f) => true;

            /// <summary>
            /// 配置业务窗口；由热更程序在内容包激活后调用一次。
            /// </summary>
            /// <param name="configs">窗口配置集合。</param>
            /// <param name="register">窗口类型显式注册回调。</param>
            public void ConfigureWindows(IEnumerable<GameWindowConfig> configs, Action<GameWindowRegistry> register) {}

            /// <summary>
            /// 按配置 ID 打开业务窗口。
            /// </summary>
            /// <param name="id">窗口配置 ID。</param>
            /// <param name="args">窗口打开参数。</param>
            /// <param name="ct">取消令牌。</param>
            /// <param name="options">加载反馈选项。</param>
            /// <returns>已打开的窗口。</returns>
            public UniTask<GameWindow> OpenWindowAsync(int id, GameWindowOpenArgs args = null, CancellationToken ct = default,
                UILoadOptions options = null) => UniTask.FromResult<GameWindow>(null);

            /// <summary>
            /// 使用原有方法签名按配置 ID 打开业务窗口。
            /// </summary>
            /// <param name="id">窗口配置 ID。</param>
            /// <param name="args">窗口打开参数。</param>
            /// <param name="ct">取消令牌。</param>
            /// <returns>已打开的窗口。</returns>
            public UniTask<GameWindow> OpenWindowAsync(int id, GameWindowOpenArgs args, CancellationToken ct) => UniTask.FromResult<GameWindow>(null);

            /// <summary>
            /// 按注册类型打开业务窗口。
            /// </summary>
            /// <typeparam name="TWindow">窗口逻辑类型。</typeparam>
            /// <param name="args">窗口打开参数。</param>
            /// <param name="ct">取消令牌。</param>
            /// <param name="options">加载反馈选项。</param>
            /// <returns>已打开的窗口。</returns>
            public UniTask<TWindow> OpenWindowAsync<TWindow>(GameWindowOpenArgs args = null, CancellationToken ct = default,
                UILoadOptions options = null) where TWindow : GameWindow => UniTask.FromResult<TWindow>(null);

            /// <summary>
            /// 使用原有方法签名按注册类型打开业务窗口。
            /// </summary>
            /// <typeparam name="TWindow">窗口逻辑类型。</typeparam>
            /// <param name="args">窗口打开参数。</param>
            /// <param name="ct">取消令牌。</param>
            /// <returns>已打开的窗口。</returns>
            public UniTask<TWindow> OpenWindowAsync<TWindow>(GameWindowOpenArgs args, CancellationToken ct) where TWindow : GameWindow =>
                UniTask.FromResult<TWindow>(null);

            /// <summary>
            /// 运行带有等待提示和可恢复资源失败重试的异步操作。
            /// </summary>
            /// <typeparam name="T">操作结果类型。</typeparam>
            /// <param name="operation">接收取消令牌、响应取消并自行清理晚到结果的异步操作。</param>
            /// <param name="options">加载反馈选项。</param>
            /// <param name="ct">调用方取消令牌。</param>
            /// <returns>异步操作结果；取消失败对话框时抛出取消异常。</returns>
            public UniTask<T> RunWithLoadingAsync<T>(Func<CancellationToken, UniTask<T>> operation, UILoadOptions options = null,
                CancellationToken ct = default) => UniTask.FromResult(default(T));

            /// <summary>
            /// 关闭指定业务窗口。
            /// </summary>
            /// <param name="id">窗口配置 ID。</param>
            /// <param name="reason">关闭原因。</param>
            /// <param name="ct">取消令牌。</param>
            /// <returns>是否关闭了一个已打开的窗口。</returns>
            public async UniTask<bool> CloseWindowAsync(int id, GameWindowCloseReason reason = GameWindowCloseReason.User, CancellationToken ct = default)
            {
                Assert.That(id, Is.EqualTo(ProjectServices.TransitionWindow));
                CloseCalls++;
                if (HoldClose)
                {
                    closeReceipt = new UniTaskCompletionSource<bool>();
                    await closeReceipt.Task;
                }
                Closed = true;
                return true;
            }

            /// <summary>
            /// 关闭当前最上层的业务窗口。
            /// </summary>
            /// <param name="ct">取消令牌。</param>
            /// <returns>是否关闭了一个已打开的窗口。</returns>
            public UniTask<bool> CloseTopWindowAsync(CancellationToken ct = default) => UniTask.FromResult(false);

            /// <summary>
            /// 关闭全部已打开的业务窗口。
            /// </summary>
            /// <param name="ct">取消令牌。</param>
            /// <returns>关闭操作。</returns>
            public UniTask CloseAllWindowsAsync(CancellationToken ct = default) => UniTask.CompletedTask;

            /// <summary>
            /// 销毁指定逻辑作用域的业务窗口及其资源租约。
            /// </summary>
            /// <param name="scope">需要退出的逻辑作用域。</param>
            /// <param name="ct">取消令牌。</param>
            /// <returns>作用域销毁操作。</returns>
            public UniTask DestroyWindowScopeAsync(GameWindowScope scope, CancellationToken ct = default) => UniTask.CompletedTask;

            /// <summary>
            /// 排队显示一条非模态提示。
            /// </summary>
            /// <param name="message">提示内容。</param>
            /// <param name="durationSeconds">显示时长，单位为秒。</param>
            public void ShowToast(string message, float durationSeconds = 2.5f) {}

            /// <summary>
            /// 显示模态确认框；多个请求按调用顺序显示。
            /// </summary>
            /// <param name="title">标题。</param>
            /// <param name="message">正文。</param>
            /// <param name="confirmText">确认按钮文字。</param>
            /// <param name="cancelText">取消按钮文字。</param>
            /// <param name="ct">取消令牌。</param>
            /// <returns>玩家是否选择确认。</returns>
            public UniTask<bool> ConfirmAsync(string title, string message, string confirmText = "确定", string cancelText = "取消",
                CancellationToken ct = default) => UniTask.FromResult(false);

            /// <summary>
            /// 显示只有确认按钮的模态提示框。
            /// </summary>
            /// <param name="title">标题。</param>
            /// <param name="message">正文。</param>
            /// <param name="confirmText">确认按钮文字。</param>
            /// <param name="ct">取消令牌。</param>
            /// <returns>玩家关闭提示框后的操作。</returns>
            public UniTask AlertAsync(string title, string message, string confirmText = "确定", CancellationToken ct = default) =>
                UniTask.CompletedTask;

            /// <summary>
            /// 显示全屏输入阻断等待层，释放返回的句柄后隐藏。
            /// </summary>
            /// <param name="message">等待层文字。</param>
            /// <returns>可更新文字的等待层句柄。</returns>
            public IUIBusyHandle BeginBusy(string message = "请稍候…") => null;
        }

        private sealed class Backend : ISaveBackend
        {
            public readonly Dictionary<string, string> Records = new();
            public readonly List<string> Attempts = new();
            public bool HoldNextWrite;
            public bool FailReads;
            public int ActiveWrites;
            public int MaximumActiveWrites;
            private UniTaskCompletionSource<SaveResult> heldWrite;
            public string StorageIdentity => "project-run-pump-test";
            public int MaxRecordBytes => 8192;

            /// <summary>
            /// 初始化独立内存后端。
            /// </summary>
            /// <returns>成功结果。</returns>
            public UniTask<SaveResult> InitializeAsync() => UniTask.FromResult(SaveResult.Ok());

            /// <summary>
            /// 读取冻结记录或明确注入读取故障。
            /// </summary>
            /// <param name="key">逻辑键。</param>
            /// <returns>明确读取结果。</returns>
            public UniTask<SaveReadResult> ReadAsync(string key) => UniTask.FromResult(FailReads ?
                SaveReadResult.Failed(SaveResult.Fail(SaveError.ReadFailed, "read")) :
                Records.TryGetValue(key, out string record) ? SaveReadResult.Found(record) : SaveReadResult.Missing());

            /// <summary>
            /// 暂停指定一次物理写入以观测业务队列，所有记录保持原子语义。
            /// </summary>
            /// <param name="key">逻辑键。</param>
            /// <param name="record">完整不可变记录。</param>
            /// <returns>完整写入结果。</returns>
            public async UniTask<SaveResult> WriteAsync(string key, string record)
            {
                Attempts.Add(record);
                ActiveWrites++;
                MaximumActiveWrites = Math.Max(MaximumActiveWrites, ActiveWrites);
                SaveResult result = SaveResult.Ok();
                if (HoldNextWrite)
                {
                    HoldNextWrite = false;
                    heldWrite = new UniTaskCompletionSource<SaveResult>();
                    result = await heldWrite.Task;
                }
                if (result.Success) Records[key] = record;
                ActiveWrites--;
                return result;
            }

            /// <summary>
            /// 释放被冻结的一次写入，不影响后来排队的候选。
            /// </summary>
            /// <param name="success">是否成功提交本次记录。</param>
            public void ReleaseWrite(bool success)
            {
                UniTaskCompletionSource<SaveResult> receipt = heldWrite;
                heldWrite = null;
                receipt.TrySetResult(success ? SaveResult.Ok() : SaveResult.Fail(SaveError.WriteFailed, "write", true));
            }
        }

        /// <summary>
        /// 异步积压期间候选与调用方隔离，预算阻塞下一帧而不丢弃 FIFO。
        /// </summary>
        [Test]
        public void PendingTicksRemainImmutableAndRespectQueueBudget()
        {
            var backend = new Backend { HoldNextWrite = true };
            ProjectSession session = CreateSession(backend);
            SetField(session, "maxPendingRunTicks", 2);
            ExpeditionRunSnapshot first = Snapshot(1);
            Assert.That(session.EnqueueRunState(first), Is.True);
            first.World.Tick = 99;
            UniTask<SaveResult> second = session.CommitRunStateAsync(Snapshot(2));
            Assert.That(session.RunSaveBlocked, Is.True);
            Assert.That(session.EnqueueRunState(Snapshot(3)), Is.False);
            Assert.That(session.HasUncommittedSave, Is.False);
            backend.ReleaseWrite(true);
            Assert.That(second.GetAwaiter().GetResult().Success, Is.True);
            Assert.That(session.RunJournalRevision, Is.EqualTo(2));
            Assert.That(session.RunSaveBlocked || session.HasPendingRunSave, Is.False);
            ProjectRunSnapshotReadResult loaded = session.ReadRunSnapshotAsync("run").GetAwaiter().GetResult();
            Assert.That(loaded.Success, Is.True, loaded.Error);
            Assert.That(loaded.Snapshot.World.Tick, Is.EqualTo(2));
            Assert.That(backend.MaximumActiveWrites, Is.EqualTo(1));
        }

        /// <summary>
        /// 失败阻塞模拟并保留所有候选，显式重试使用原内容和原修订。
        /// </summary>
        [Test]
        public void FailedCandidateRetriesSameRevisionBeforeLaterTicks()
        {
            var backend = new Backend { HoldNextWrite = true };
            ProjectSession session = CreateSession(backend);
            int failures = 0;
            session.RunSaveFailed += () => failures++;
            UniTask<SaveResult> first = session.CommitRunStateAsync(Snapshot(1));
            UniTask<SaveResult> second = session.CommitRunStateAsync(Snapshot(2));
            backend.ReleaseWrite(false);
            Assert.That(first.GetAwaiter().GetResult().Success || second.GetAwaiter().GetResult().Success, Is.False);
            Assert.That(session.RunSaveBlocked, Is.True);
            Assert.That(failures, Is.EqualTo(1));
            Assert.That(session.RunJournalRevision, Is.Zero);
            Assert.That(session.EnqueueRunState(Snapshot(3)), Is.False);
            Assert.That(session.RetryRunSaveAsync().GetAwaiter().GetResult().Success, Is.True);
            Assert.That(backend.Attempts[1], Is.EqualTo(backend.Attempts[0]));
            Assert.That(session.RunJournalRevision, Is.EqualTo(2));
            Assert.That(session.ReadRunSnapshotAsync("run").GetAwaiter().GetResult().Snapshot.World.Tick, Is.EqualTo(2));
        }

        /// <summary>
        /// 关键局外事务先阻止新帧入队并排空已有帧，物理保存期间泵不启动。
        /// </summary>
        [Test]
        public void ProfileGateDrainsQueuedTicksBeforeExclusiveWrite()
        {
            var backend = new Backend { HoldNextWrite = true };
            ProjectSession session = CreateSession(backend);
            Assert.That(session.EnqueueRunState(Snapshot(1)), Is.True);
            Assert.That(session.EnqueueRunState(Snapshot(2)), Is.True);
            SetField(session, "profileSaveWaiting", true);
            UniTask<SaveResult> drained = session.WaitForRunSavesAsync();
            Assert.That(drained.Status, Is.EqualTo(UniTaskStatus.Pending));
            Assert.That(session.EnqueueRunState(Snapshot(3)), Is.False);
            backend.ReleaseWrite(true);
            Assert.That(drained.GetAwaiter().GetResult().Success, Is.True);
            SetField(session, "saving", true);
            SetField(session, "profileSaveWaiting", false);
            Assert.That(session.EnqueueRunState(Snapshot(3)), Is.False);
            SetField(session, "saving", false);
            Assert.That(session.CommitRunStateAsync(Snapshot(3)).GetAwaiter().GetResult().Success, Is.True);
            Assert.That(session.RunJournalRevision, Is.EqualTo(3));
            Assert.That(backend.MaximumActiveWrites, Is.EqualTo(1));
        }

        /// <summary>
        /// 关键业务与后台保存共享失败等待，重试原候选后全部续体完成且不追加重复修订。
        /// </summary>
        [Test]
        public void CriticalCandidateAndBackgroundShareFailedSaveAndOriginalRevision()
        {
            var backend = new Backend { HoldNextWrite = true };
            ProjectSession session = CreateSession(backend);
            Assert.That(ExpeditionRun.TryCreate(session, "run", 1, 291,
                new List<ProjectItem>(session.Warehouse), out ExpeditionRun run), Is.True);
            using (run)
            using (var coordinator = new ProjectRunCoordinator(session))
            {
                SetField(session, "<CurrentRun>k__BackingField", run);
                SetField(coordinator, "<Phase>k__BackingField", ProjectRunPhase.Playing);
                SetField(coordinator, "recoveryUiPending", true);
                SetField(run, "committing", true);
                ui.HoldClose = true;
                ExpeditionRunSnapshot candidate = Snapshot(1);
                UniTask<bool> business = ConfirmBusinessAsync(coordinator, run, candidate);
                UniTask background = coordinator.SetApplicationPausedAsync(true);
                Assert.That(background.Status, Is.EqualTo(UniTaskStatus.Pending));
                backend.ReleaseWrite(false);
                Assert.That(business.Status, Is.EqualTo(UniTaskStatus.Pending));
                Assert.That(background.Status, Is.EqualTo(UniTaskStatus.Pending));
                Assert.That(coordinator.CanRetry, Is.True);
                Assert.That(run.IsSavingRun, Is.True);
                coordinator.SetApplicationPausedAsync(false).GetAwaiter().GetResult();
                Assert.That(coordinator.RetryAsync().GetAwaiter().GetResult(), Is.True);
                Assert.That(business.Status, Is.EqualTo(UniTaskStatus.Pending));
                Assert.That(background.Status, Is.EqualTo(UniTaskStatus.Pending));
                Assert.That(run.IsSavingRun, Is.True);
                Assert.That(coordinator.Phase, Is.EqualTo(ProjectRunPhase.RecoveryRequired));
                Assert.That(ui.BlocksGameplayInput, Is.True);
                ui.ReleaseClose();
                Assert.That(business.GetAwaiter().GetResult(), Is.True);
                background.GetAwaiter().GetResult();
                Assert.That(run.IsSavingRun, Is.False);
                Assert.That(typeof(ExpeditionRun).GetField("paused", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(run), Is.False);
                Assert.That(ui.CloseCalls, Is.EqualTo(1));
                Assert.That(ui.BlocksGameplayInput, Is.False);
                Assert.That(session.RunJournalRevision, Is.EqualTo(1));
                Assert.That(backend.Attempts[1], Is.EqualTo(backend.Attempts[0]));
                Assert.That(session.ReadRunSnapshotAsync("run").GetAwaiter().GetResult().Snapshot.World.Tick, Is.EqualTo(1));
            }
        }

        /// <summary>
        /// 满队列拒绝关键候选后，屏障先确认原帧，再单独提交该候选而非误报已确认。
        /// </summary>
        [Test]
        public void FullQueueRejectionDrainsOriginalFailureBeforeCommittingCandidate()
        {
            var backend = new Backend { HoldNextWrite = true };
            ProjectSession session = CreateSession(backend);
            SetField(session, "maxPendingRunTicks", 1);
            Assert.That(session.EnqueueRunState(Snapshot(1)), Is.True);
            ExpeditionRunSnapshot candidate = Snapshot(2);
            Assert.That(TryCommit(session, candidate, out UniTask<SaveResult> rejected), Is.False);
            Assert.That(rejected.GetAwaiter().GetResult().Success, Is.False);
            using var coordinator = new ProjectRunCoordinator(session);
            SetField(coordinator, "<Phase>k__BackingField", ProjectRunPhase.Playing);
            SetField(coordinator, "recoveryUiPending", true);
            UniTask<bool> critical = BeginSnapshotBarrier(coordinator, candidate);
            Assert.That(critical.Status, Is.EqualTo(UniTaskStatus.Pending));
            backend.ReleaseWrite(false);
            Assert.That(critical.Status, Is.EqualTo(UniTaskStatus.Pending));
            Assert.That(coordinator.RetryAsync().GetAwaiter().GetResult(), Is.True);
            Assert.That(critical.GetAwaiter().GetResult(), Is.True);
            Assert.That(session.RunJournalRevision, Is.EqualTo(2));
            Assert.That(backend.Attempts[1], Is.EqualTo(backend.Attempts[0]));
            Assert.That(session.ReadRunSnapshotAsync("run").GetAwaiter().GetResult().Snapshot.World.Tick, Is.EqualTo(2));
        }

        /// <summary>
        /// 排空之后仍被门禁拒绝的候选必须在重试时真正入队，不能用空队列成功冒充其回执。
        /// </summary>
        [Test]
        public void RejectedCandidateRetriesAdmissionRatherThanEmptyJournal()
        {
            ProjectSession session = CreateSession(new Backend());
            SetField(session, "profileSaveWaiting", true);
            using var coordinator = new ProjectRunCoordinator(session);
            SetField(coordinator, "<Phase>k__BackingField", ProjectRunPhase.Playing);
            SetField(coordinator, "recoveryUiPending", true);
            UniTask<bool> critical = BeginSnapshotBarrier(coordinator, Snapshot(7));
            Assert.That(critical.Status, Is.EqualTo(UniTaskStatus.Pending));
            Assert.That(session.RunJournalRevision, Is.Zero);
            SetField(session, "profileSaveWaiting", false);
            Assert.That(coordinator.RetryAsync().GetAwaiter().GetResult(), Is.True);
            Assert.That(critical.GetAwaiter().GetResult(), Is.True);
            Assert.That(session.RunJournalRevision, Is.EqualTo(1));
            Assert.That(session.ReadRunSnapshotAsync("run").GetAwaiter().GetResult().Snapshot.World.Tick, Is.EqualTo(7));
        }

        /// <summary>
        /// 第二个关键事务等待当前重试完成，不覆盖恢复入口，也不提前执行自己的保存。
        /// </summary>
        [Test]
        public void ConcurrentCriticalBarriersExecuteInOrderAfterRetry()
        {
            ProjectSession session = CreateSession(new Backend());
            using var coordinator = new ProjectRunCoordinator(session);
            SetField(coordinator, "<Phase>k__BackingField", ProjectRunPhase.Playing);
            SetField(coordinator, "recoveryUiPending", true);
            int firstAttempts = 0, secondAttempts = 0;
            UniTask<bool> first = BeginBarrier(coordinator, () => UniTask.FromResult(
                new ProjectOperationResult(++firstAttempts > 1, "first")));
            UniTask<bool> second = BeginBarrier(coordinator, () =>
            {
                secondAttempts++;
                return UniTask.FromResult(new ProjectOperationResult(true, "second"));
            });
            Assert.That(first.Status, Is.EqualTo(UniTaskStatus.Pending));
            Assert.That(second.Status, Is.EqualTo(UniTaskStatus.Pending));
            Assert.That(secondAttempts, Is.Zero);
            Assert.That(coordinator.RetryAsync().GetAwaiter().GetResult(), Is.True);
            Assert.That(first.GetAwaiter().GetResult() && second.GetAwaiter().GetResult(), Is.True);
            Assert.That(firstAttempts, Is.EqualTo(2));
            Assert.That(secondAttempts, Is.EqualTo(1));
        }

        /// <summary>
        /// 固定日志换局后只恢复新身份；读取错误保留明确失败，不发布默认局。
        /// </summary>
        [Test]
        public void ReadRejectsOtherIdentityAndPreservesBackendFailure()
        {
            var backend = new Backend();
            ProjectSession session = CreateSession(backend);
            Assert.That(session.CommitRunStateAsync(Snapshot(1)).GetAwaiter().GetResult().Success, Is.True);
            Assert.That(session.ReadRunSnapshotAsync("other").GetAwaiter().GetResult().Missing, Is.True);
            ExpeditionRunSnapshot next = Snapshot(9);
            next.RunId = "other";
            foreach (RegionGenerationRequest request in next.Requests) request.RunId = next.RunId;
            Assert.That(session.CommitRunStateAsync(next).GetAwaiter().GetResult().Success, Is.True);
            Assert.That(session.ReadRunSnapshotAsync("other").GetAwaiter().GetResult().Snapshot.World.Tick, Is.EqualTo(9));
            backend.FailReads = true;
            ProjectRunSnapshotReadResult failed = session.ReadRunSnapshotAsync("other").GetAwaiter().GetResult();
            Assert.That(failed.Success || failed.Missing, Is.False);
            Assert.That(failed.Snapshot, Is.Null);
            Assert.That(failed.Error, Is.EqualTo("read"));
        }

        /// <summary>
        /// 创建不连接局外保存模块的独立会话，并注入正式日志后端。
        /// </summary>
        /// <param name="backend">测试保存后端。</param>
        /// <returns>已经创建单局日志的会话。</returns>
        private static ProjectSession CreateSession(Backend backend)
        {
            var session = (ProjectSession)Activator.CreateInstance(typeof(ProjectSession), BindingFlags.Instance | BindingFlags.NonPublic,
                null, new object[] { null, ProjectSaveData.CreateDefault() }, null);
            typeof(ProjectSession).GetMethod("CreateRunJournal", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(session, new object[] { backend });
            return session;
        }

        /// <summary>
        /// 由真实配置生成可编码的准入基线，并使用指定时钟区分队列提交。
        /// </summary>
        /// <param name="tick">完整战斗时钟。</param>
        /// <returns>独立候选快照。</returns>
        private static ExpeditionRunSnapshot Snapshot(long tick)
        {
            var profile = ProjectSaveData.CreateDefault();
            var session = (ProjectSession)Activator.CreateInstance(typeof(ProjectSession), BindingFlags.Instance | BindingFlags.NonPublic,
                null, new object[] { null, profile }, null);
            Assert.That(ExpeditionRun.TryCreate(session, "run", 1, 291, new List<ProjectItem>(profile.Warehouse), out ExpeditionRun run), Is.True);
            using (run)
            {
                Assert.That(run.TryCaptureState(out ExpeditionRunSnapshot snapshot, out string error), Is.True, error);
                snapshot.World.Tick = tick;
                foreach (CombatRoomState room in snapshot.World.Rooms) room.Tick = tick;
                return snapshot;
            }
        }

        /// <summary>
        /// 注入私有调度门禁以验证外部可见队列行为，不触发真实局外存档。
        /// </summary>
        /// <param name="target">独立测试会话、运行对象或协调器。</param>
        /// <param name="name">门禁字段。</param>
        /// <param name="value">门禁值。</param>
        private static void SetField(object target, string name, object value) =>
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

        /// <summary>
        /// 在真实协调器屏障确认后模拟业务续体释放交互门禁。
        /// </summary>
        /// <param name="coordinator">本次测试的真实流程协调器。</param>
        /// <param name="run">等待业务提交确认的运行对象。</param>
        /// <param name="snapshot">已经冻结的实际业务候选。</param>
        /// <returns>原业务已获得持久确认时为真。</returns>
        private async UniTask<bool> ConfirmBusinessAsync(ProjectRunCoordinator coordinator, ExpeditionRun run, ExpeditionRunSnapshot snapshot)
        {
            bool saved = await BeginSnapshotBarrier(coordinator, snapshot);
            if (saved)
            {
                Assert.That(ui.Closed, Is.True, "业务续体必须在恢复窗口关闭之后执行。");
                SetField(run, "committing", false);
            }
            return saved;
        }

        /// <summary>
        /// 检查真实内部入口是否接纳候选，并获取其独立回执。
        /// </summary>
        /// <param name="session">独立会话。</param>
        /// <param name="snapshot">业务候选。</param>
        /// <param name="task">候选回执或拒绝结果。</param>
        /// <returns>唯一队列已经接纳时为真。</returns>
        private static bool TryCommit(ProjectSession session, ExpeditionRunSnapshot snapshot, out UniTask<SaveResult> task)
        {
            object[] arguments = { snapshot, null };
            bool accepted = (bool)typeof(ProjectSession).GetMethod("TryCommitRunState", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(session, arguments);
            task = (UniTask<SaveResult>)arguments[1];
            return accepted;
        }

        /// <summary>
        /// 调用真实统一快照屏障，覆盖先排空和后接纳的完整协议。
        /// </summary>
        /// <param name="coordinator">独立协调器。</param>
        /// <param name="snapshot">已经冻结的完整候选。</param>
        /// <returns>原候选屏障的等待任务。</returns>
        private static UniTask<bool> BeginSnapshotBarrier(ProjectRunCoordinator coordinator, ExpeditionRunSnapshot snapshot) =>
            (UniTask<bool>)typeof(ProjectRunCoordinator).GetMethod("SaveSnapshotBarrierAsync", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(coordinator, new object[] { snapshot, ProjectRunPhase.Playing });

        /// <summary>
        /// 调用项目内部真实保存屏障，测试不打开全局 UI 或场景。
        /// </summary>
        /// <param name="coordinator">独立协调器。</param>
        /// <param name="save">本次保存操作。</param>
        /// <returns>原保存屏障的等待任务。</returns>
        private static UniTask<bool> BeginBarrier(ProjectRunCoordinator coordinator, Func<UniTask<ProjectOperationResult>> save) =>
            (UniTask<bool>)typeof(ProjectRunCoordinator).GetMethod("SaveBarrierAsync", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(coordinator, new object[] { save, ProjectRunPhase.Playing });
    }
}
