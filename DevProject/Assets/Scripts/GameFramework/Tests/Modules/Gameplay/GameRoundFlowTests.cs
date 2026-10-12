using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace GameFramework.Tests.Modules.Gameplay
{
    /// <summary>
    /// 验证单局流程的转场、取消与资源清理。
    /// </summary>
    public sealed class GameRoundFlowTests
    {
        /// <summary>
        /// 验证冷恢复沿用持久身份，重复恢复不会创建第二局，后续结算身份保持一致。
        /// </summary>
        /// <returns>测试协程。</returns>
        [UnityTest]
        public IEnumerator RestoreUsesSavedIdentityAndRejectsDuplicateEntry()
        {
            var factory = new FakeFactory();
            var flow = new GameRoundFlow(factory);
            Task<bool> restored = flow.RestoreLevelAsync(2, "persisted-round").AsTask();
            yield return Wait(restored);
            Assert.That(restored.Result, Is.True);
            Assert.That(flow.CurrentRoundId, Is.EqualTo("persisted-round"));
            Assert.That(factory.Last.RoundId, Is.EqualTo("persisted-round"));
            Assert.That(flow.RestoreLevelAsync(2, "persisted-round").GetAwaiter().GetResult(), Is.False);
            Assert.That(factory.Sessions.Count, Is.EqualTo(1));
            factory.Last.Finish(true);
            yield return WaitForState(flow, GameRoundState.Results);
            Assert.That(flow.Result.Value.RoundId, Is.EqualTo("persisted-round"));
            flow.Dispose();
        }

        /// <summary>
        /// 验证暂停、结算、下一关、重玩和返回菜单的完整流程。
        /// </summary>
        /// <returns>测试协程。</returns>
        [UnityTest]
        public IEnumerator RoundTrip_UsesFreshRoundsAndSessionPause()
        {
            var factory = new FakeFactory();
            var flow = new GameRoundFlow(factory);
            Task<bool> start = flow.StartLevelAsync(1).AsTask();
            yield return Wait(start);
            Assert.IsTrue(start.Result);
            Assert.IsTrue(flow.Pause());
            Assert.IsTrue(factory.Last.Paused);
            Assert.IsTrue(flow.Resume());
            Assert.IsFalse(factory.Last.Paused);
            string firstId = flow.CurrentRoundId;
            factory.Last.Finish(true);
            Assert.IsTrue(factory.Last.Paused);
            yield return WaitForState(flow, GameRoundState.Results);
            Assert.IsTrue(flow.HasNextLevel);
            Task<bool> next = flow.NextLevelAsync().AsTask();
            yield return Wait(next);
            Assert.IsTrue(next.Result);
            Assert.AreEqual(2, flow.CurrentLevelId);
            Assert.AreNotEqual(firstId, flow.CurrentRoundId);
            Assert.AreEqual(1, factory.Sessions[0].ExitCount);
            factory.Last.Finish(false);
            yield return WaitForState(flow, GameRoundState.Results);
            Assert.IsFalse(flow.HasNextLevel);
            Task<bool> replay = flow.RetryAsync().AsTask();
            yield return Wait(replay);
            Assert.IsTrue(replay.Result);
            Assert.AreEqual(2, flow.CurrentLevelId);
            Assert.AreNotEqual(factory.Sessions[1].RoundId, flow.CurrentRoundId);
            Task<bool> menu = flow.ReturnToMenuAsync().AsTask();
            yield return Wait(menu);
            Assert.IsTrue(menu.Result);
            Assert.AreEqual(GameRoundState.Menu, flow.State);
            Assert.AreEqual(1, factory.Sessions[2].DisposeCount);
            flow.Dispose();
        }

        /// <summary>
        /// 验证重复结算和繁忙转场不会创建新会话或重复保存。
        /// </summary>
        /// <returns>测试协程。</returns>
        [UnityTest]
        public IEnumerator DoubleFinish_AndBusyOperation_AreIgnored()
        {
            var factory = new FakeFactory();
            var saveGate = new UniTaskCompletionSource();
            int saves = 0;
            var flow = new GameRoundFlow(factory, (result, ct) =>
            {
                saves++;
                return saveGate.Task;
            });
            Task<bool> start = flow.StartLevelAsync(1).AsTask();
            yield return Wait(start);
            factory.Last.Finish(true);
            factory.Last.Finish(true);
            Assert.AreEqual(GameRoundState.Settling, flow.State);
            Assert.IsTrue(flow.IsBusy);
            Task<bool> rejected = flow.ReturnToMenuAsync().AsTask();
            yield return Wait(rejected);
            Assert.IsFalse(rejected.Result);
            saveGate.TrySetResult();
            yield return WaitForState(flow, GameRoundState.Results);
            Assert.AreEqual(1, saves);
            Assert.AreEqual(1, factory.Sessions.Count);
            flow.Dispose();
        }

        /// <summary>
        /// 验证取消加载后迟到的进入完成不会恢复运行态。
        /// </summary>
        /// <returns>测试协程。</returns>
        [UnityTest]
        public IEnumerator CancelLoading_CleansLateSession()
        {
            var factory = new FakeFactory { GateEnter = true };
            var flow = new GameRoundFlow(factory);
            Task<bool> start = flow.StartLevelAsync(1).AsTask();
            Assert.AreEqual(GameRoundState.Loading, flow.State);
            flow.CancelLoading();
            factory.Last.ReleaseEnter();
            yield return Wait(start);
            Assert.IsFalse(start.Result);
            Assert.AreEqual(GameRoundState.Menu, flow.State);
            Assert.AreEqual(1, factory.Last.ExitCount);
            Assert.AreEqual(1, factory.Last.DisposeCount);
            flow.Dispose();
        }

        /// <summary>
        /// 验证加载失败后释放会话且暴露错误。
        /// </summary>
        /// <returns>测试协程。</returns>
        [UnityTest]
        public IEnumerator EnterFailure_CleansSession()
        {
            var factory = new FakeFactory { FailEnter = true };
            var flow = new GameRoundFlow(factory);
            Task<bool> start = flow.StartLevelAsync(1).AsTask();
            yield return Wait(start);
            Assert.IsFalse(start.Result);
            Assert.AreEqual(GameRoundState.Failed, flow.State);
            Assert.IsNotEmpty(flow.Error);
            Assert.AreEqual(1, factory.Last.DisposeCount);
            flow.Dispose();
        }

        /// <summary>
        /// 验证保存失败后仅重试原有结果，不重新创建会话。
        /// </summary>
        /// <returns>测试协程。</returns>
        [UnityTest]
        public IEnumerator SaveFailure_RetriesSameResult()
        {
            var factory = new FakeFactory();
            var saved = new List<GameRoundResult>();
            var flow = new GameRoundFlow(factory, (result, ct) =>
            {
                saved.Add(result);
                if (saved.Count == 1) throw new InvalidOperationException("save failed");
                return UniTask.CompletedTask;
            });
            Task<bool> start = flow.StartLevelAsync(1).AsTask();
            yield return Wait(start);
            factory.Last.Finish(true);
            yield return WaitForState(flow, GameRoundState.Failed);
            Assert.AreEqual(1, factory.Last.ExitCount);
            Task<bool> menu = flow.ReturnToMenuAsync().AsTask();
            yield return Wait(menu);
            Assert.IsFalse(menu.Result);
            Task<bool> retry = flow.RetryAsync().AsTask();
            yield return Wait(retry);
            Assert.IsTrue(retry.Result);
            Assert.AreEqual(GameRoundState.Results, flow.State);
            Assert.AreEqual(saved[0].RoundId, saved[1].RoundId);
            Assert.AreEqual(1, factory.Sessions.Count);
            Assert.AreEqual(1, factory.Last.ExitCount);
            flow.Dispose();
        }

        /// <summary>
        /// 验证关闭等待迟到加载完成并保持终态。
        /// </summary>
        /// <returns>测试协程。</returns>
        [UnityTest]
        public IEnumerator CloseDuringLoad_DoesNotRevive()
        {
            var factory = new FakeFactory { GateEnter = true };
            var flow = new GameRoundFlow(factory);
            Task<bool> start = flow.StartLevelAsync(1).AsTask();
            Task<bool> close = flow.CloseAsync().AsTask();
            Assert.IsFalse(close.IsCompleted);
            factory.Last.ReleaseEnter();
            yield return Wait(start);
            yield return Wait(close);
            Assert.IsFalse(start.Result);
            Assert.IsTrue(close.Result);
            Assert.AreEqual(GameRoundState.Disposed, flow.State);
            Assert.AreEqual(1, factory.Last.DisposeCount);
        }

        /// <summary>
        /// 验证退出失败时保留会话，重试后只释放一次。
        /// </summary>
        /// <returns>测试协程。</returns>
        [UnityTest]
        public IEnumerator ExitFailure_CanRetryWithoutDoubleDispose()
        {
            var factory = new FakeFactory();
            var flow = new GameRoundFlow(factory);
            Task<bool> start = flow.StartLevelAsync(1).AsTask();
            yield return Wait(start);
            factory.Last.FailExitOnce = true;
            Task<bool> first = flow.ReturnToMenuAsync().AsTask();
            yield return Wait(first);
            Assert.IsFalse(first.Result);
            Assert.AreEqual(GameRoundState.Failed, flow.State);
            Task<bool> second = flow.ReturnToMenuAsync().AsTask();
            yield return Wait(second);
            Assert.IsTrue(second.Result);
            Assert.AreEqual(2, factory.Last.ExitCount);
            Assert.AreEqual(1, factory.Last.DisposeCount);
            flow.Dispose();
        }

        /// <summary>
        /// 未结算时退出失败后，重玩先清理原会话再创建新局。
        /// </summary>
        /// <returns>测试协程。</returns>
        [UnityTest]
        public IEnumerator ExitFailure_RetryStartsFreshRound()
        {
            var factory = new FakeFactory();
            var flow = new GameRoundFlow(factory);
            Task<bool> start = flow.StartLevelAsync(1).AsTask();
            yield return Wait(start);
            string originalId = flow.CurrentRoundId;
            FakeSession original = factory.Last;
            original.FailExitOnce = true;
            Task<bool> leaving = flow.ReturnToMenuAsync().AsTask();
            yield return Wait(leaving);
            Assert.IsFalse(leaving.Result);
            Assert.IsFalse(flow.Result.HasValue);
            Assert.AreEqual(GameRoundState.Failed, flow.State);
            Task<bool> retry = flow.RetryAsync().AsTask();
            yield return Wait(retry);
            Assert.IsTrue(retry.Result);
            Assert.AreEqual(GameRoundState.Playing, flow.State);
            Assert.AreEqual(1, flow.CurrentLevelId);
            Assert.AreNotEqual(originalId, flow.CurrentRoundId);
            Assert.AreEqual(2, original.ExitCount);
            Assert.AreEqual(1, original.DisposeCount);
            Assert.AreEqual(2, factory.Sessions.Count);
            flow.Dispose();
        }

        /// <summary>
        /// 结算重试被调用方取消后仍可再次重试。
        /// </summary>
        /// <returns>测试协程。</returns>
        [UnityTest]
        public IEnumerator CancelledSettlement_RetryRemainsAvailable()
        {
            var factory = new FakeFactory();
            int calls = 0;
            var cts = new CancellationTokenSource();
            var flow = new GameRoundFlow(factory, (result, ct) =>
            {
                calls++;
                if (calls == 1) throw new InvalidOperationException("first save failed");
                if (calls == 2)
                {
                    cts.Cancel();
                    throw new OperationCanceledException(ct);
                }
                return UniTask.CompletedTask;
            });
            Task<bool> start = flow.StartLevelAsync(1).AsTask();
            yield return Wait(start);
            factory.Last.Finish(true);
            yield return WaitForState(flow, GameRoundState.Failed);
            Task<bool> canceled = flow.RetryAsync(cts.Token).AsTask();
            yield return Wait(canceled);
            Assert.IsFalse(canceled.Result);
            Assert.AreEqual(GameRoundState.Failed, flow.State);
            Assert.IsNotEmpty(flow.Error);
            Task<bool> retry = flow.RetryAsync().AsTask();
            yield return Wait(retry);
            Assert.IsTrue(retry.Result);
            Assert.AreEqual(GameRoundState.Results, flow.State);
            flow.Dispose();
            cts.Dispose();
        }

        /// <summary>
        /// 退出完成时取消调用方等待仍恢复到菜单。
        /// </summary>
        /// <returns>测试协程。</returns>
        [UnityTest]
        public IEnumerator CancelDuringExit_StillReturnsToMenu()
        {
            var factory = new FakeFactory();
            var flow = new GameRoundFlow(factory);
            Task<bool> start = flow.StartLevelAsync(1).AsTask();
            yield return Wait(start);
            var cts = new CancellationTokenSource();
            factory.Last.OnExit = cts.Cancel;
            Task<bool> menu = flow.ReturnToMenuAsync(cts.Token).AsTask();
            yield return Wait(menu);
            Assert.IsTrue(menu.Result);
            Assert.AreEqual(GameRoundState.Menu, flow.State);
            cts.Dispose();
            flow.Dispose();
        }
        private static IEnumerator Wait(Task task)
        {
            while (!task.IsCompleted) yield return null;
            if (task.IsFaulted) throw task.Exception;
        }

        private static IEnumerator WaitForState(GameRoundFlow flow, GameRoundState state)
        {
            for (int frame = 0; frame < 30 && flow.State != state; frame++) yield return null;
            Assert.AreEqual(state, flow.State);
        }

        private sealed class FakeFactory : IGameRoundFactory
        {
            internal readonly List<FakeSession> Sessions = new List<FakeSession>();
            internal bool GateEnter;
            internal bool FailEnter;
            internal FakeSession Last => Sessions[Sessions.Count - 1];
            public int FirstLevelId => 1;
            public bool ContainsLevel(int levelId) => levelId == 1 || levelId == 2;
            public int GetNextLevelId(int levelId) => levelId == 1 ? 2 : 0;
            public IGameRoundSession Create(int levelId, string roundId)
            {
                var session = new FakeSession(levelId, roundId, GateEnter, FailEnter);
                Sessions.Add(session);
                return session;
            }
        }

        private sealed class FakeSession : IGameRoundSession
        {
            private readonly UniTaskCompletionSource _enterGate;
            private readonly bool _failEnter;
            internal readonly int LevelId;
            internal readonly string RoundId;
            internal bool Paused;
            internal bool FailExitOnce;
            internal Action OnExit;
            internal int ExitCount;
            internal int DisposeCount;
            public event Action<GameRoundResult> Completed;

            internal FakeSession(int levelId, string roundId, bool gateEnter, bool failEnter)
            {
                LevelId = levelId;
                RoundId = roundId;
                _enterGate = gateEnter ? new UniTaskCompletionSource() : null;
                _failEnter = failEnter;
            }

            public async UniTask EnterAsync(CancellationToken ct)
            {
                if (_enterGate != null) await _enterGate.Task;
                if (_failEnter) throw new InvalidOperationException("enter failed");
            }

            public UniTask ExitAsync(CancellationToken ct)
            {
                ExitCount++;
                OnExit?.Invoke();
                if (FailExitOnce)
                {
                    FailExitOnce = false;
                    throw new InvalidOperationException("exit failed");
                }
                return UniTask.CompletedTask;
            }

            public void SetPaused(bool paused) => Paused = paused;
            public void Dispose() => DisposeCount++;
            internal void ReleaseEnter() => _enterGate?.TrySetResult();
            internal void Finish(bool won) => Completed?.Invoke(new GameRoundResult(RoundId, LevelId, won, 10, 2f));
        }
    }
}
