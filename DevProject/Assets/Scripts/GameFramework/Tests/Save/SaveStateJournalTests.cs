using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Cysharp.Threading.Tasks;
using NUnit.Framework;

namespace GameFramework.Tests
{
    /// <summary>
    /// 验证活动状态分块、连续增量、检查点轮换与故障后的同候选重试。
    /// </summary>
    public sealed class SaveStateJournalTests
    {
        /// <summary>
        /// 提供可精确注入写入故障的测试后端。
        /// </summary>
        private sealed class Backend : ISaveBackend
        {
            public readonly Dictionary<string, string> Records = new();
            public int Writes;
            public int FailWrite;
            public bool FailReads;
            public string FailReadKey;
            public SaveError ReadError = SaveError.ReadFailed;
            public int RecordLimit = 8192;
            public bool PersistThenUncertain;
            public string StorageIdentity => "journal-test";
            public int MaxRecordBytes => RecordLimit;

            /// <summary>
            /// 初始化内存存储。
            /// </summary>
            /// <returns>成功结果。</returns>
            public UniTask<SaveResult> InitializeAsync() => UniTask.FromResult(SaveResult.Ok());

            /// <summary>
            /// 读取记录或注入明确读取失败。
            /// </summary>
            /// <param name="key">逻辑键。</param>
            /// <returns>读取结果。</returns>
            public UniTask<SaveReadResult> ReadAsync(string key) => UniTask.FromResult(FailReads || key == FailReadKey ?
                SaveReadResult.Failed(SaveResult.Fail(ReadError, "read")) :
                Records.TryGetValue(key, out string value) ? SaveReadResult.Found(value) : SaveReadResult.Missing());

            /// <summary>
            /// 在指定写入点拒绝提交，其他操作保持完整记录语义。
            /// </summary>
            /// <param name="key">逻辑键。</param>
            /// <param name="record">完整记录。</param>
            /// <returns>写入结果。</returns>
            public UniTask<SaveResult> WriteAsync(string key, string record)
            {
                Writes++;
                if (Writes == FailWrite) return UniTask.FromResult(SaveResult.Fail(SaveError.WriteFailed, "write", true));
                if (Encoding.UTF8.GetByteCount(record) > MaxRecordBytes)
                    return UniTask.FromResult(SaveResult.Fail(SaveError.TooLarge, "record"));
                Records[key] = record;
                return UniTask.FromResult(PersistThenUncertain ? SaveResult.Fail(SaveError.Uncertain, "write", true) : SaveResult.Ok());
            }
        }

        /// <summary>
        /// 建立容量足以覆盖多个分块的测试实例。
        /// </summary>
        /// <param name="backend">测试存储。</param>
        /// <param name="interval">检查点增量间隔。</param>
        /// <returns>独立日志实例。</returns>
        private static SaveStateJournal Journal(Backend backend, int interval = 3) => new(backend, "test/run", interval, 65536);

        /// <summary>
        /// 创建具有大块不变内容与少量动态内容的组件。
        /// </summary>
        /// <param name="tick">动态值。</param>
        /// <param name="extra">是否包含可删除组件。</param>
        /// <returns>完整测试状态。</returns>
        private static List<SaveStateComponent> State(int tick, bool extra = true)
        {
            var state = new List<SaveStateComponent> { new() { Key = "plan", Value = new string('地', 3000) },
                new() { Key = "actor/1", Value = "{\"tick\":" + tick + "}" } };
            if (extra) state.Add(new SaveStateComponent { Key = "buff/7", Value = "{\"ticks\":20}" });
            return state;
        }

        /// <summary>
        /// 验证连续增量、组件删除、完整检查点轮换及冷恢复。
        /// </summary>
        [Test]
        public void RoundTripAcrossCheckpointsKeepsCompleteStateAndWritesOnlyChangedComponents()
        {
            var backend = new Backend();
            var journal = Journal(backend);
            Assert.That(journal.CommitAsync("run", State(1)).GetAwaiter().GetResult().Success, Is.True);
            long fullBytes = journal.LastPayloadBytes;
            Assert.That(journal.CommitAsync("run", State(2, false)).GetAwaiter().GetResult().Success, Is.True);
            Assert.That(journal.LastPayloadBytes, Is.LessThan(fullBytes / 10));
            for (int tick = 3; tick <= 15; tick++)
                Assert.That(journal.CommitAsync("run", State(tick, false)).GetAwaiter().GetResult().Success, Is.True);
            SaveJournalLoadResult loaded = Journal(backend).LoadAsync("run").GetAwaiter().GetResult();
            Assert.That(loaded.Result.Success, Is.True, loaded.Result.Message);
            Assert.That(loaded.Revision, Is.EqualTo(15));
            Assert.That(loaded.Components.Single(value => value.Key == "actor/1").Value, Is.EqualTo("{\"tick\":15}"));
            Assert.That(loaded.Components.Any(value => value.Key == "buff/7"), Is.False);
            Assert.That(backend.Records.Count, Is.LessThan(50));
        }

        /// <summary>
        /// 验证下一检查点写到任意分块中断时保留原完整状态，同候选可继续提交。
        /// </summary>
        /// <param name="failureOffset">下一次事务内失败的写入序号。</param>
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(4)]
        [TestCase(6)]
        public void InterruptedCheckpointNeverPublishesHalfStateAndRetriesSameRevision(int failureOffset)
        {
            var backend = new Backend();
            var journal = Journal(backend, 1);
            Assert.That(journal.CommitAsync("run", State(1)).GetAwaiter().GetResult().Success, Is.True);
            Assert.That(journal.CommitAsync("run", State(2)).GetAwaiter().GetResult().Success, Is.True);
            backend.FailWrite = backend.Writes + failureOffset;
            SaveResult failed = journal.CommitAsync("run", State(3, false)).GetAwaiter().GetResult();
            Assert.That(failed.Success, Is.False);
            Assert.That(journal.HasPending, Is.True);
            SaveJournalLoadResult old = Journal(backend, 1).LoadAsync("run").GetAwaiter().GetResult();
            Assert.That(old.Result.Success, Is.True, old.Result.Message);
            Assert.That(old.Revision, Is.EqualTo(2));
            backend.FailWrite = 0;
            Assert.That(journal.RetryAsync().GetAwaiter().GetResult().Success, Is.True);
            SaveJournalLoadResult loaded = Journal(backend, 1).LoadAsync("run").GetAwaiter().GetResult();
            Assert.That(loaded.Revision, Is.EqualTo(3));
            Assert.That(loaded.Components.Any(value => value.Key == "buff/7"), Is.False);
        }

        /// <summary>
        /// 验证损坏增量不会被跨过，原始记录不会在读取中被覆盖。
        /// </summary>
        [Test]
        public void CorruptLatestDeltaFallsBackToPreviousCompleteRevisionWithoutRewritingRecords()
        {
            var backend = new Backend();
            var journal = Journal(backend);
            Assert.That(journal.CommitAsync("run", State(1)).GetAwaiter().GetResult().Success, Is.True);
            Assert.That(journal.CommitAsync("run", State(2)).GetAwaiter().GetResult().Success, Is.True);
            string delta = backend.Records.Keys.Single(key => key.Contains("segment-1/chunk-0"));
            backend.Records[delta] = "broken";
            int writes = backend.Writes;
            SaveJournalLoadResult loaded = Journal(backend).LoadAsync("run").GetAwaiter().GetResult();
            Assert.That(loaded.Result.Success && loaded.UsedPreviousRevision, Is.True);
            Assert.That(loaded.Revision, Is.EqualTo(1));
            Assert.That(backend.Writes, Is.EqualTo(writes));
            Assert.That(backend.Records[delta], Is.EqualTo("broken"));
        }

        /// <summary>
        /// 验证读取故障不能伪装为不存在或默认新局。
        /// </summary>
        [Test]
        public void FailedReadDoesNotCreateMissingState()
        {
            var backend = new Backend { FailReads = true };
            SaveJournalLoadResult loaded = Journal(backend).LoadAsync("run").GetAwaiter().GetResult();
            Assert.That(loaded.Result.Error, Is.EqualTo(SaveError.ReadFailed));
            Assert.That(loaded.Missing, Is.False);
            Assert.That(loaded.Components, Is.Null);
        }

        /// <summary>
        /// 第三次检查点回收旧数据区时，每个写入中断点仍恢复完整前版。
        /// </summary>
        [Test]
        public void RecycledBankSurvivesEveryWriteInterruption()
        {
            var probeBackend = new Backend();
            var probe = Journal(probeBackend, 1);
            for (int tick = 1; tick <= 4; tick++) Assert.That(probe.CommitAsync("run", State(tick)).GetAwaiter().GetResult().Success, Is.True);
            int before = probeBackend.Writes;
            Assert.That(probe.CommitAsync("run", State(5, false)).GetAwaiter().GetResult().Success, Is.True);
            int count = probeBackend.Writes - before;
            for (int offset = 1; offset <= count; offset++)
            {
                var backend = new Backend();
                var journal = Journal(backend, 1);
                for (int tick = 1; tick <= 4; tick++) Assert.That(journal.CommitAsync("run", State(tick)).GetAwaiter().GetResult().Success, Is.True);
                backend.FailWrite = backend.Writes + offset;
                Assert.That(journal.CommitAsync("run", State(5, false)).GetAwaiter().GetResult().Success, Is.False);
                Assert.That(journal.Revision, Is.EqualTo(4));
                Assert.That(Journal(backend, 1).LoadAsync("run").GetAwaiter().GetResult().Revision, Is.EqualTo(4));
                backend.FailWrite = 0;
                Assert.That(journal.RetryAsync().GetAwaiter().GetResult().Success, Is.True);
                Assert.That(Journal(backend, 1).LoadAsync("run").GetAwaiter().GetResult().Revision, Is.EqualTo(5));
            }
        }

        /// <summary>
        /// 小记录后端根据提交头预算提前轮换，避免产生无法重试的超大头。
        /// </summary>
        [Test]
        public void HeadCapacityTriggersCheckpointBeforeWritingOversizedRecord()
        {
            var backend = new Backend { RecordLimit = 4096 };
            var journal = Journal(backend, 256);
            for (int tick = 1; tick <= 80; tick++)
                Assert.That(journal.CommitAsync("run", State(tick)).GetAwaiter().GetResult().Success, Is.True);
            Assert.That(backend.Records.Values.All(record => Encoding.UTF8.GetByteCount(record) <= backend.MaxRecordBytes), Is.True);
            Assert.That(Journal(backend, 256).LoadAsync("run").GetAwaiter().GetResult().Revision, Is.EqualTo(80));
        }

        /// <summary>
        /// 组件编码开销计入容量，拒绝大量短键或转义内容构成的超限输入。
        /// </summary>
        [Test]
        public void EncodedComponentBudgetRejectsBeforeAnyWrite()
        {
            var backend = new Backend();
            var journal = new SaveStateJournal(backend, "budget", 3, 64);
            var components = new List<SaveStateComponent> { new() { Key = "a", Value = new string('\n', 30) } };
            Assert.That(journal.CommitAsync("run", components).GetAwaiter().GetResult().Success, Is.False);
            Assert.That(journal.HasPending, Is.False);
            Assert.That(backend.Writes, Is.Zero);
        }

        /// <summary>
        /// 最新头结构损坏也明确标记使用前版，而非伪装成最新完整提交。
        /// </summary>
        [Test]
        public void CorruptHeadReportsPreviousRevision()
        {
            var backend = new Backend();
            var journal = Journal(backend);
            Assert.That(journal.CommitAsync("run", State(1)).GetAwaiter().GetResult().Success, Is.True);
            Assert.That(journal.CommitAsync("run", State(2)).GetAwaiter().GetResult().Success, Is.True);
            backend.Records["test/run/head-a"] = "broken";
            SaveJournalLoadResult loaded = Journal(backend).LoadAsync("run").GetAwaiter().GetResult();
            Assert.That(loaded.Result.Success && loaded.UsedPreviousRevision, Is.True);
            Assert.That(loaded.Revision, Is.EqualTo(1));
        }

        /// <summary>
        /// 任意后端读取失败不得被解释为损坏并静默退回前版。
        /// </summary>
        [Test]
        public void FailedChunkReadRetainsBackendError()
        {
            var backend = new Backend();
            var journal = Journal(backend);
            Assert.That(journal.CommitAsync("run", State(1)).GetAwaiter().GetResult().Success, Is.True);
            Assert.That(journal.CommitAsync("run", State(2)).GetAwaiter().GetResult().Success, Is.True);
            backend.FailReadKey = "test/run/bank-0/segment-1/chunk-0";
            backend.ReadError = SaveError.Unsupported;
            SaveJournalLoadResult loaded = Journal(backend).LoadAsync("run").GetAwaiter().GetResult();
            Assert.That(loaded.Result.Error, Is.EqualTo(SaveError.Unsupported));
            Assert.That(loaded.Missing || loaded.UsedPreviousRevision, Is.False);
        }

        /// <summary>
        /// 不确定写入经完整读回可以确认；提交头读回失败保留原候选修订。
        /// </summary>
        [Test]
        public void AtomicHeadRequiresReadbackAndRetriesOriginalRevision()
        {
            var backend = new Backend { PersistThenUncertain = true };
            var journal = Journal(backend);
            Assert.That(journal.CommitAsync("run", State(1)).GetAwaiter().GetResult().Success, Is.True);
            backend.FailReadKey = "test/run/head-a";
            Assert.That(journal.CommitAsync("run", State(2)).GetAwaiter().GetResult().Error, Is.EqualTo(SaveError.Uncertain));
            Assert.That(journal.Revision, Is.EqualTo(1));
            Assert.That(journal.HasPending, Is.True);
            backend.FailReadKey = null;
            Assert.That(Journal(backend).LoadAsync("run").GetAwaiter().GetResult().Revision, Is.EqualTo(2));
            Assert.That(journal.RetryAsync().GetAwaiter().GetResult().Success, Is.True);
            Assert.That(journal.Revision, Is.EqualTo(2));
        }

        /// <summary>
        /// 换局复用固定逻辑键，不把旧活动身份解释为新局数据。
        /// </summary>
        [Test]
        public void NewIdentityReplacesStateWithinSameBanks()
        {
            var backend = new Backend();
            var journal = Journal(backend);
            Assert.That(journal.CommitAsync("first", State(1)).GetAwaiter().GetResult().Success, Is.True);
            Assert.That(journal.CommitAsync("first", State(2)).GetAwaiter().GetResult().Success, Is.True);
            Assert.That(journal.CommitAsync("second", State(9, false)).GetAwaiter().GetResult().Success, Is.True);
            SaveJournalLoadResult loaded = Journal(backend).LoadAsync("second").GetAwaiter().GetResult();
            Assert.That(loaded.Result.Success, Is.True);
            Assert.That(loaded.Revision, Is.EqualTo(1));
            Assert.That(loaded.Components.Any(component => component.Key == "buff/7"), Is.False);
            Assert.That(Journal(backend).LoadAsync("third").GetAwaiter().GetResult().Missing, Is.True);
        }

        /// <summary>
        /// 验证跨局读取不会混入旧身份，重复组件直接失败且不写存储。
        /// </summary>
        [Test]
        public void IdentityAndDuplicateKeysAreValidatedBeforeWriting()
        {
            var backend = new Backend();
            var journal = Journal(backend);
            Assert.That(journal.CommitAsync("first", State(1)).GetAwaiter().GetResult().Success, Is.True);
            Assert.That(Journal(backend).LoadAsync("second").GetAwaiter().GetResult().Missing, Is.True);
            List<SaveStateComponent> duplicate = State(2);
            duplicate.Add(duplicate[0].Copy());
            int writes = backend.Writes;
            Assert.That(journal.CommitAsync("first", duplicate).GetAwaiter().GetResult().Success, Is.False);
            Assert.That(backend.Writes, Is.EqualTo(writes));
        }
    }
}
