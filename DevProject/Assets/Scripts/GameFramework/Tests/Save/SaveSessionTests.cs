using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace GameFramework.Tests.Save
{
    /// <summary>
    /// 验证存档会话在取消、超时、关闭和数据持续变化时不会丢失写入顺序。
    /// </summary>
    public sealed class SaveSessionTests
    {
        private SaveModule _module;
        private ControlledBackend _backend;
        private SaveIdentity _identity;
        private double _now;
        private int _value;

        /// <summary>
        /// 创建独立的存储身份和手动时钟。
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            _now = 0;
            _value = 1;
            _backend = new ControlledBackend(Guid.NewGuid().ToString("N"));
            _identity = new SaveIdentity("save-tests", "editor", "guest", "main");
            _module = new SaveModule(() => _now);
            Assert.IsTrue(_module.ConfigureBackend(_backend).Success);
            _module.OnInit();
        }

        /// <summary>
        /// 结束底层等待并释放占用，避免跨测试遗留。
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            _backend.FinishWrite(SaveResult.Fail(SaveError.WriteFailed, "测试收尾"));
            _backend.FinishInitialize();
            _module.OnShutdown();
        }

        /// <summary>
        /// 打开和绑定不会提前写入，显式激活后首存才落盘。
        /// </summary>
        /// <returns>测试协程。</returns>
        [UnityTest]
        public IEnumerator OpenRequiresExplicitActivation() => UniTask.ToCoroutine(async () =>
        {
            var opened = await _module.OpenAsync(_identity, Definition());
            Assert.IsTrue(opened.Result.Success, opened.Result.Message);
            var session = opened.Session;
            Assert.IsTrue(session.BindSnapshot(Snapshot).Success);
            _now = 20;
            _module.OnUpdate(0);
            Assert.AreEqual(0, _backend.Writes);
            Assert.AreEqual(SaveError.NotActive, (await session.FlushAsync()).Result.Error);
            Assert.IsTrue(session.Activate().Success);
            Assert.IsTrue((await session.FlushAsync()).Result.Success);
            Assert.AreEqual(1, _backend.Writes);
        });

        /// <summary>
        /// debounce 合并变更，持续变更到最大等待时间也会保存。
        /// </summary>
        /// <returns>测试协程。</returns>
        [UnityTest]
        public IEnumerator AutoSaveUsesUnscaledClockAndMaximumWait() => UniTask.ToCoroutine(async () =>
        {
            var session = await OpenActive();
            _now = 0.8;
            session.MarkDirty();
            _module.OnUpdate(0);
            Assert.AreEqual(0, _backend.Writes);
            _now = 1.5;
            session.MarkDirty();
            _module.OnUpdate(0);
            Assert.AreEqual(0, _backend.Writes);
            _now = 10.1;
            session.MarkDirty();
            _module.OnUpdate(0);
            Assert.AreEqual(1, _backend.Writes);
            Assert.IsFalse(session.GetStatus().Dirty);
        });

        /// <summary>
        /// 写入中的新修订号不会被旧快照完成回调清除。
        /// </summary>
        /// <returns>测试协程。</returns>
        [UnityTest]
        public IEnumerator MutationDuringWriteRemainsDirty() => UniTask.ToCoroutine(async () =>
        {
            var session = await OpenActive();
            _backend.DelayWrites = true;
            var first = session.FlushAsync().AsTask();
            _value = 9;
            session.MarkDirty();
            _backend.FinishWrite(SaveResult.Ok());
            var receipt = await first.AsUniTask();
            Assert.IsTrue(receipt.Result.Success);
            Assert.Less(receipt.SavedRevision, session.GetStatus().Revision);
            Assert.IsTrue(session.GetStatus().Dirty);
            _backend.DelayWrites = false;
            Assert.IsTrue((await session.FlushAsync()).Result.Success);
            Assert.AreEqual(2, _backend.Writes);
            session.Dispose();
            var loaded = await _module.OpenAsync(_identity, Definition());
            Assert.AreEqual(9, loaded.LoadResult.Data.Count);
        });

        /// <summary>
        /// 超时不能启动第二次写入，关闭会话后仍须等待原物理写入释放身份。
        /// </summary>
        /// <returns>测试协程。</returns>
        [UnityTest]
        public IEnumerator TimeoutRetainsWriterUntilLateCompletion() => UniTask.ToCoroutine(async () =>
        {
            var session = await OpenActive();
            _backend.DelayWrites = true;
            var pending = session.FlushAsync().AsTask();
            _now = 11;
            _module.OnUpdate(0);
            Assert.AreEqual(SaveError.Uncertain, (await pending.AsUniTask()).Result.Error);
            Assert.AreEqual(SaveError.Uncertain, (await session.RetryAsync()).Result.Error);
            Assert.AreEqual(1, _backend.Writes);
            session.Dispose();
            Assert.AreEqual(SaveError.Conflict, (await _module.OpenAsync(_identity, Definition())).Result.Error);
            Assert.AreEqual(SaveError.Conflict, _module.ConfigureBackend(new ControlledBackend("other")).Error);
            _backend.FinishWrite(SaveResult.Ok());
            await UniTask.Yield();
            var reopened = await _module.OpenAsync(_identity, Definition());
            Assert.IsTrue(reopened.Result.Success, reopened.Result.Message);
            Assert.AreEqual(1, reopened.LoadResult.Data.Count);
        });

        /// <summary>
        /// 取消调用方等待不会中止物理写入或错误地显示保存失败后无数据。
        /// </summary>
        /// <returns>测试协程。</returns>
        [UnityTest]
        public IEnumerator CancellationOnlyCancelsWait() => UniTask.ToCoroutine(async () =>
        {
            var session = await OpenActive();
            _backend.DelayWrites = true;
            using var cts = new CancellationTokenSource();
            var pending = session.FlushAsync(cts.Token).AsTask();
            cts.Cancel();
            Assert.AreEqual(SaveError.Cancelled, (await pending.AsUniTask()).Result.Error);
            Assert.AreEqual(SavePhase.Saving, session.GetStatus().Phase);
            _backend.FinishWrite(SaveResult.Ok());
            await UniTask.Yield();
            Assert.IsFalse(session.GetStatus().Dirty);
            Assert.AreEqual(1, session.GetStatus().Sequence);
        });

        /// <summary>
        /// 不同后端对象使用同一存储标识时仍只能打开一个写入会话。
        /// </summary>
        /// <returns>测试协程。</returns>
        [UnityTest]
        public IEnumerator StableBackendIdentityPreventsDuplicateWriter() => UniTask.ToCoroutine(async () =>
        {
            var session = await OpenActive();
            var other = new SaveModule();
            other.ConfigureBackend(new ControlledBackend(_backend.StorageIdentity));
            var duplicate = await other.OpenAsync(_identity, Definition());
            Assert.AreEqual(SaveError.Conflict, duplicate.Result.Error);
            session.Dispose();
            var available = await other.OpenAsync(_identity, Definition());
            Assert.IsTrue(available.Result.Success);
            other.OnShutdown();
        });

        /// <summary>
        /// 临时写失败按退避重试，配额失败等待用户处理。
        /// </summary>
        /// <returns>测试协程。</returns>
        [UnityTest]
        public IEnumerator AutoRetryIsBoundedAndQuotaDoesNotLoop() => UniTask.ToCoroutine(async () =>
        {
            var session = await OpenActive(new SavePolicy { MaxAttempts = 2 });
            _backend.NextFailure = SaveResult.Fail(SaveError.WriteFailed, "临时失败", true);
            Assert.AreEqual(SaveError.WriteFailed, (await session.FlushAsync()).Result.Error);
            _backend.NextFailure = SaveResult.Fail(SaveError.QuotaExceeded, "空间不足");
            _now = 2;
            _module.OnUpdate(0);
            Assert.AreEqual(2, _backend.Writes);
            _now = 100;
            _module.OnUpdate(0);
            Assert.AreEqual(2, _backend.Writes);
            Assert.AreEqual(SaveError.QuotaExceeded, session.GetStatus().LastResult.Error);
            Assert.IsTrue((await session.RetryAsync()).Result.Success);
        });

        /// <summary>
        /// 新快照出现校验错误时不写文件，修复数据后可以手动重试。
        /// </summary>
        /// <returns>测试协程。</returns>
        [UnityTest]
        public IEnumerator InvalidSnapshotDoesNotOverwriteSave() => UniTask.ToCoroutine(async () =>
        {
            var session = await OpenActive();
            Assert.IsTrue((await session.FlushAsync()).Result.Success);
            _value = -1;
            session.MarkDirty();
            Assert.AreEqual(SaveError.ValidationFailed, (await session.FlushAsync()).Result.Error);
            Assert.AreEqual(1, _backend.Writes);
            _value = 4;
            Assert.IsTrue((await session.RetryAsync()).Result.Success);
            Assert.AreEqual(2, _backend.Writes);
        });

        /// <summary>
        /// 在途打开被取消后，初始化真正结束前不能重开或换后端。
        /// </summary>
        /// <returns>测试协程。</returns>
        [UnityTest]
        public IEnumerator CancelledOpenRetainsOwnershipUntilReadSettles() => UniTask.ToCoroutine(async () =>
        {
            _backend.DelayInitialize = true;
            using var cts = new CancellationTokenSource();
            var opening = _module.OpenAsync(_identity, Definition(), ct: cts.Token).AsTask();
            cts.Cancel();
            Assert.AreEqual(SaveError.Cancelled, (await opening.AsUniTask()).Result.Error);
            Assert.AreEqual(SaveError.Conflict, (await _module.OpenAsync(_identity, Definition())).Result.Error);
            _backend.FinishInitialize();
            await UniTask.Yield();
            _backend.DelayInitialize = false;
            var reopened = await _module.OpenAsync(_identity, Definition());
            Assert.IsTrue(reopened.Result.Success, reopened.Result.Message);
        });

        /// <summary>
        /// 关闭等待最终提交，关闭期间拒绝新变化。
        /// </summary>
        /// <returns>测试协程。</returns>
        [UnityTest]
        public IEnumerator CloseWaitsForSaveAndFreezesMutations() => UniTask.ToCoroutine(async () =>
        {
            var session = await OpenActive();
            _backend.DelayWrites = true;
            var closing = session.CloseAsync().AsTask();
            Assert.AreEqual(SaveError.Closed, session.MarkDirty().Error);
            Assert.IsFalse(closing.IsCompleted);
            _backend.FinishWrite(SaveResult.Ok());
            Assert.IsTrue((await closing.AsUniTask()).Success);
            Assert.AreEqual(SavePhase.Closed, session.GetStatus().Phase);
            Assert.IsTrue((await _module.OpenAsync(_identity, Definition())).Result.Success);
        });

        /// <summary>
        /// 手动恢复到新槽保留来源存档且目标必须为空。
        /// </summary>
        /// <returns>测试协程。</returns>
        [UnityTest]
        public IEnumerator RecoveryCreatesNewSlotWithoutChangingSource() => UniTask.ToCoroutine(async () =>
        {
            var session = await OpenActive();
            Assert.IsTrue((await session.FlushAsync()).Result.Success);
            session.Dispose();
            var inspected = await _module.InspectRecoveryAsync(_identity, Definition());
            Assert.IsTrue(inspected.Result.Success);
            Assert.Greater(inspected.Candidates.Count, 0);
            var target = new SaveIdentity("save-tests", "editor", "guest", "recovered");
            var restored = await _module.RestoreToNewSlotAsync(inspected.Candidates[0], target, Definition());
            Assert.IsTrue(restored.Result.Success, restored.Result.Message);
            Assert.AreEqual(1, _backend.Writes);
            restored.Session.BindSnapshot(() => restored.LoadResult.Data);
            Assert.IsTrue(restored.Session.Activate().Success);
            Assert.IsTrue((await restored.Session.FlushAsync()).Result.Success);
            restored.Session.Dispose();
            Assert.AreEqual(SaveError.Conflict, (await _module.RestoreToNewSlotAsync(inspected.Candidates[0], target, Definition())).Result.Error);
            Assert.AreEqual(1, (await _module.OpenAsync(_identity, Definition())).LoadResult.Data.Count);
        });

        /// <summary>
        /// 后台信号立即保存并合并重复事件。
        /// </summary>
        /// <returns>测试协程。</returns>
        [UnityTest]
        public IEnumerator BackgroundFlushIgnoresDebounceAndDuplicateSignals() => UniTask.ToCoroutine(async () =>
        {
            var session = await OpenActive();
            _backend.DelayWrites = true;
            session.OnBackground(true);
            session.OnBackground(true);
            Assert.AreEqual(1, _backend.Writes);
            _backend.FinishWrite(SaveResult.Ok());
            await UniTask.Yield();
            session.OnBackground(true);
            Assert.AreEqual(1, _backend.Writes);
        });

        /// <summary>
        /// 同步释放未保存数据后，异步关闭不能伪报保存成功。
        /// </summary>
        /// <returns>测试协程。</returns>
        [UnityTest]
        public IEnumerator DisposeIsNotSuccessfulClose() => UniTask.ToCoroutine(async () =>
        {
            var session = await OpenActive();
            session.Dispose();
            Assert.AreEqual(SaveError.Closed, (await session.CloseAsync()).Error);
            Assert.AreEqual(0, _backend.Writes);
            var next = await OpenActive();
            Assert.IsTrue((await next.CloseAsync()).Success);
            Assert.IsTrue((await next.CloseAsync()).Success);
        });

        /// <summary>
        /// 打开并激活测试存档。
        /// </summary>
        /// <param name="policy">可选策略。</param>
        /// <returns>活动会话。</returns>
        private async UniTask<SaveSession<Data>> OpenActive(SavePolicy policy = null)
        {
            var result = await _module.OpenAsync(_identity, Definition(), policy);
            Assert.IsTrue(result.Result.Success, result.Result.Message);
            Assert.IsTrue(result.Session.BindSnapshot(Snapshot).Success);
            Assert.IsTrue(result.Session.Activate().Success);
            return result.Session;
        }

        /// <summary>
        /// 捕获独立数据快照。
        /// </summary>
        /// <returns>测试快照。</returns>
        private Data Snapshot() => new() { Count = _value };

        /// <summary>
        /// 创建当前版本数据定义。
        /// </summary>
        /// <returns>数据定义。</returns>
        private static SaveDefinition<Data> Definition() => new(1, () => new Data(), data => data != null && data.Count >= 0 ? null : "计数无效");

        [Serializable]
        private sealed class Data { public int Count; }

        /// <summary>
        /// 可控制真实结束时刻的后端替身。
        /// </summary>
        private sealed class ControlledBackend : ISaveBackend
        {
            private readonly Dictionary<string, string> _records = new();
            private UniTaskCompletionSource<SaveResult> _writeGate;
            private UniTaskCompletionSource<SaveResult> _initGate;
            public string StorageIdentity { get; }
            public int MaxRecordBytes => 262144;
            public int Writes;
            public bool DelayWrites, DelayInitialize;
            public SaveResult NextFailure;

            /// <summary>
            /// 创建隔离后端。
            /// </summary>
            /// <param name="id">持久化身份。</param>
            public ControlledBackend(string id) => StorageIdentity = id;

            /// <summary>
            /// 初始化或等待测试释放初始化操作。
            /// </summary>
            /// <returns>初始化结果。</returns>
            public UniTask<SaveResult> InitializeAsync()
            {
                if (!DelayInitialize) return UniTask.FromResult(SaveResult.Ok());
                _initGate = new UniTaskCompletionSource<SaveResult>();
                return _initGate.Task;
            }

            /// <summary>
            /// 读取已有记录。
            /// </summary>
            /// <param name="key">记录键。</param>
            /// <returns>读取结果。</returns>
            public UniTask<SaveReadResult> ReadAsync(string key) => UniTask.FromResult(
                _records.TryGetValue(key, out string value) ? SaveReadResult.Found(value) : SaveReadResult.Missing());

            /// <summary>
            /// 保存完整记录，延迟时由测试控制完成。
            /// </summary>
            /// <param name="key">目标键。</param>
            /// <param name="record">完整记录。</param>
            /// <returns>物理操作结果。</returns>
            public async UniTask<SaveResult> WriteAsync(string key, string record)
            {
                Writes++;
                SaveResult result = NextFailure;
                NextFailure = SaveResult.Ok();
                if (DelayWrites)
                {
                    _writeGate = new UniTaskCompletionSource<SaveResult>();
                    result = await _writeGate.Task;
                }
                if (result.Success) _records[key] = record;
                return result;
            }

            /// <summary>
            /// 完成在途物理写入。
            /// </summary>
            /// <param name="result">物理结果。</param>
            public void FinishWrite(SaveResult result)
            {
                var gate = _writeGate;
                _writeGate = null;
                gate?.TrySetResult(result);
            }

            /// <summary>
            /// 完成在途初始化。
            /// </summary>
            public void FinishInitialize()
            {
                var gate = _initGate;
                _initGate = null;
                gate?.TrySetResult(SaveResult.Ok());
            }
        }
    }
}
