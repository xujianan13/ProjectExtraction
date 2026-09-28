using System;
using System.Linq;
using Cysharp.Threading.Tasks;
using NUnit.Framework;

namespace GameFramework.Tests
{
    /// <summary>
    /// 覆盖正式存档仓库的双槽、迁移与阻断性失败。
    /// </summary>
    public sealed class SaveRepositoryTests
    {
        /// <summary>
        /// 测试用的可序列化存档数据。
        /// </summary>
        [Serializable]
        private sealed class Data { public int Value; }

        private static readonly SaveIdentity Identity = new("game", "editor", "profile", "main");

        /// <summary>
        /// 创建测试存档定义。
        /// </summary>
        /// <param name="version">目标 schema 版本。</param>
        /// <param name="migration">可选迁移步骤。</param>
        /// <returns>校验非负数值的存档定义。</returns>
        private static SaveDefinition<Data> Definition(int version = 1, SaveMigration migration = null) =>
            new(version, () => new Data(), value => value != null && value.Value >= 0 ? null : "invalid",
                migrations: migration == null ? null : new[] { migration });

        /// <summary>
        /// 验证空槽创建未落盘默认数据，提交后在 A/B 槽间轮换。
        /// </summary>
        [Test]
        public void MissingSlotsCreateUnsavedDefaultAndAlternateAfterCommit()
        {
            var backend = new MemorySaveBackend();
            var repository = new SaveRepository<Data>(backend, Identity, Definition(), 262144);
            var loaded = repository.LoadAsync().GetAwaiter().GetResult();
            Assert.That(loaded.Result.Success && loaded.NeedsWrite && loaded.Origin == SaveLoadOrigin.New, Is.True);
            Assert.That(repository.Prepare(new Data { Value = 1 }, out var first).Success, Is.True);
            Assert.That(first.Key, Does.EndWith("/a"));
            Assert.That(repository.CommitAsync(first).GetAwaiter().GetResult().Success, Is.True);
            Assert.That(repository.Prepare(new Data { Value = 2 }, out var second).Success, Is.True);
            Assert.That(second.Key, Does.EndWith("/b"));
            Assert.That(repository.CommitAsync(second).GetAwaiter().GetResult().Success, Is.True);
            Assert.That(new SaveRepository<Data>(backend, Identity, Definition(), 262144).LoadAsync().GetAwaiter().GetResult().Data.Value, Is.EqualTo(2));
        }

        /// <summary>
        /// 验证最新槽损坏时恢复旧槽且不改写损坏记录。
        /// </summary>
        [Test]
        public void CorruptNewestSlotRecoversOlderWithoutChangingStorage()
        {
            var backend = new MemorySaveBackend();
            var repository = new SaveRepository<Data>(backend, Identity, Definition(), 262144);
            repository.Prepare(new Data { Value = 1 }, out var first);
            Assert.That(repository.CommitAsync(first).GetAwaiter().GetResult().Success, Is.True);
            repository.Prepare(new Data { Value = 2 }, out var second);
            Assert.That(repository.CommitAsync(second).GetAwaiter().GetResult().Success, Is.True);
            backend.WriteAsync(second.Key, second.Json.Substring(0, second.Json.Length / 2)).GetAwaiter().GetResult();
            var loaded = new SaveRepository<Data>(backend, Identity, Definition(), 262144).LoadAsync().GetAwaiter().GetResult();
            Assert.That(loaded.Result.Success && loaded.Origin == SaveLoadOrigin.Recovered, Is.True);
            Assert.That(loaded.Data.Value, Is.EqualTo(1));
            Assert.That(backend.ReadAsync(second.Key).GetAwaiter().GetResult().Record, Is.Not.EqualTo(second.Json));
        }

        /// <summary>
        /// 验证未来 schema 阻止旧版本加载并保留迁移备份。
        /// </summary>
        [Test]
        public void FutureSchemaBlocksOldRepositoryAndRecoveryCanUseMigrationBackup()
        {
            var backend = new MemorySaveBackend();
            var oldRepository = new SaveRepository<Data>(backend, Identity, Definition(), 262144);
            oldRepository.Prepare(new Data { Value = 3 }, out var oldCandidate);
            Assert.That(oldRepository.CommitAsync(oldCandidate).GetAwaiter().GetResult().Success, Is.True);
            var migration = new SaveMigration(1, json => null, json => json);
            var newer = new SaveRepository<Data>(backend, Identity, Definition(2, migration), 262144);
            var migrated = newer.LoadAsync().GetAwaiter().GetResult();
            Assert.That(migrated.Migrated && migrated.NeedsWrite, Is.True);
            newer.Prepare(migrated.Data, out var newCandidate);
            Assert.That(newer.CommitAsync(newCandidate).GetAwaiter().GetResult().Success, Is.True);
            var blocked = new SaveRepository<Data>(backend, Identity, Definition(), 262144).LoadAsync().GetAwaiter().GetResult();
            Assert.That(blocked.Result.Error, Is.EqualTo(SaveError.VersionTooNew));
            var inspection = newer.InspectRecoveryAsync().GetAwaiter().GetResult();
            Assert.That(inspection.Candidates.Any(item => item.Source == SaveRecoverySource.PreMigrationA && item.SchemaVersion == 1), Is.True);
        }

        /// <summary>
        /// 验证确定写失败保留后端的配额错误。
        /// </summary>
        [Test]
        public void DeterminateWriteFailureRetainsBackendError()
        {
            var backend = new FaultBackend();
            var repository = new SaveRepository<Data>(backend, Identity, Definition(), 262144);
            repository.Prepare(new Data { Value = 1 }, out var candidate);
            backend.WriteFailure = SaveResult.Fail(SaveError.QuotaExceeded, "quota");
            var result = repository.CommitAsync(candidate).GetAwaiter().GetResult();
            Assert.That(result.Error, Is.EqualTo(SaveError.QuotaExceeded));
            Assert.That(repository.Sequence, Is.Zero);
        }

        /// <summary>
        /// 验证读取失败不会被当作新档。
        /// </summary>
        [Test]
        public void ReadFailureNeverCreatesDefaultData()
        {
            var backend = new FaultBackend { ReadFailure = true };
            var loaded = new SaveRepository<Data>(backend, Identity, Definition(), 262144).LoadAsync().GetAwaiter().GetResult();
            Assert.That(loaded.Result.Error, Is.EqualTo(SaveError.ReadFailed));
            Assert.That(loaded.NeedsWrite, Is.False);
        }

        /// <summary>
        /// 验证未知封装格式即使缺少旧字段也阻断回退。
        /// </summary>
        [Test]
        public void UnknownEnvelopeFormatBlocksFallbackEvenWithoutLegacyFields()
        {
            var backend = new MemorySaveBackend();
            var repository = new SaveRepository<Data>(backend, Identity, Definition(), 262144);
            repository.Prepare(new Data { Value = 1 }, out var oldCandidate);
            Assert.That(repository.CommitAsync(oldCandidate).GetAwaiter().GetResult().Success, Is.True);
            backend.WriteAsync(Identity.KeyPrefix + "/b", "{\"formatVersion\":2}").GetAwaiter().GetResult();
            var loaded = new SaveRepository<Data>(backend, Identity, Definition(), 262144).LoadAsync().GetAwaiter().GetResult();
            Assert.That(loaded.Result.Error, Is.EqualTo(SaveError.FormatUnsupported));
            Assert.That(loaded.NeedsWrite, Is.False);
        }

        /// <summary>
        /// 验证降低记录上限不会把已有大档当作新档。
        /// </summary>
        [Test]
        public void ReducedRecordLimitBlocksExistingSave()
        {
            var backend = new MemorySaveBackend();
            var repository = new SaveRepository<Data>(backend, Identity, Definition(), 262144);
            repository.Prepare(new Data { Value = 1 }, out var candidate);
            Assert.That(repository.CommitAsync(candidate).GetAwaiter().GetResult().Success, Is.True);
            var loaded = new SaveRepository<Data>(backend, Identity, Definition(), 32).LoadAsync().GetAwaiter().GetResult();
            Assert.That(loaded.Result.Error, Is.EqualTo(SaveError.TooLarge));
            Assert.That(loaded.NeedsWrite, Is.False);
            Assert.That(backend.ReadAsync(candidate.Key).GetAwaiter().GetResult().Record, Is.EqualTo(candidate.Json));
        }

        /// <summary>
        /// 可注入读写错误的测试存储后端。
        /// </summary>
        private sealed class FaultBackend : ISaveBackend
        {
            private readonly MemorySaveBackend backend = new();
            public bool ReadFailure;
            public SaveResult WriteFailure;
            public string StorageIdentity => backend.StorageIdentity;
            public int MaxRecordBytes => backend.MaxRecordBytes;
            /// <summary>
            /// 初始化内存后端。
            /// </summary>
            /// <returns>初始化结果。</returns>
            public UniTask<SaveResult> InitializeAsync() => backend.InitializeAsync();
            /// <summary>
            /// 读取记录或注入读取失败。
            /// </summary>
            /// <param name="key">待读取的记录键。</param>
            /// <returns>读取结果。</returns>
            public UniTask<SaveReadResult> ReadAsync(string key) => ReadFailure ? UniTask.FromResult(
                SaveReadResult.Failed(SaveResult.Fail(SaveError.ReadFailed, "io", true))) : backend.ReadAsync(key);
            /// <summary>
            /// 写入记录或注入写入失败。
            /// </summary>
            /// <param name="key">待写入的记录键。</param>
            /// <param name="record">完整记录内容。</param>
            /// <returns>写入结果。</returns>
            public UniTask<SaveResult> WriteAsync(string key, string record) => WriteFailure.Success ? backend.WriteAsync(key, record) : UniTask.FromResult(WriteFailure);
        }
    }
}