using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Envelope = GameFramework.SaveEnvelope;

namespace GameFramework
{
    /// <summary>
    /// 与游戏数据类型无关的持久化协议封装。
    /// </summary>
    [Serializable]
    internal sealed class SaveEnvelope
    {
        public int formatVersion;
        public string gameId;
        public string environment;
        public string profileId;
        public string slotId;
        public int schemaVersion;
        public long sequence;
        public string savedAtUtc;
        public string payloadJson;
        public string checksum;
    }
    /// <summary>
    /// 加载数据的来源。
    /// </summary>
    public enum SaveLoadOrigin { New, Saved, Recovered }

    /// <summary>
    /// 仓库加载后的数据与持久化状态。
    /// </summary>
    /// <typeparam name="T">游戏存档数据类型。</typeparam>
    public sealed class SaveLoadResult<T>
    {
        public SaveResult Result { get; }
        public T Data { get; }
        public SaveLoadOrigin Origin { get; }
        public bool Migrated { get; }
        public bool NeedsWrite { get; }
        public long Sequence { get; }

        /// <summary>
        /// 创建数据加载结果。
        /// </summary>
        /// <param name="result">加载结果。</param>
        /// <param name="data">已验证数据。</param>
        /// <param name="origin">数据来源。</param>
        /// <param name="migrated">是否已在内存迁移。</param>
        /// <param name="needsWrite">是否需要首次落盘。</param>
        /// <param name="sequence">当前持久序号。</param>
        internal SaveLoadResult(SaveResult result, T data, SaveLoadOrigin origin, bool migrated, bool needsWrite, long sequence)
        {
            Result = result;
            Data = data;
            Origin = origin;
            Migrated = migrated;
            NeedsWrite = needsWrite;
            Sequence = sequence;
        }
    }

    /// <summary>
    /// 完整且不可变的待提交记录。
    /// </summary>
    internal sealed class SaveCandidate
    {
        internal long Sequence { get; }
        internal string Json { get; }
        internal string Key { get; }
        internal string Checksum { get; }
        internal int ByteCount { get; }

        /// <summary>
        /// 创建待提交快照。
        /// </summary>
        /// <param name="sequence">候选序号。</param>
        /// <param name="json">完整封装 JSON。</param>
        /// <param name="key">目标键。</param>
        /// <param name="checksum">封装校验值。</param>
        internal SaveCandidate(long sequence, string json, string key, string checksum)
        {
            Sequence = sequence;
            Json = json;
            Key = key;
            Checksum = checksum;
            ByteCount = Encoding.UTF8.GetByteCount(json);
        }
    }

    /// <summary>
    /// 双槽正式存档仓库。
    /// </summary>
    /// <typeparam name="T">游戏存档数据类型。</typeparam>
    internal sealed class SaveRepository<T>
    {
        private readonly ISaveBackend backend;
        private readonly SaveIdentity identity;
        private readonly SaveDefinition<T> definition;
        private readonly int maxRecordBytes;
        private Envelope current;
        private string currentKey;
        private Envelope migrationSource;

        internal long Sequence => current?.sequence ?? 0;

        /// <summary>
        /// 创建双槽仓库。
        /// </summary>
        /// <param name="backend">物理存储后端。</param>
        /// <param name="identity">逻辑存档身份。</param>
        /// <param name="definition">数据定义。</param>
        /// <param name="maxRecordBytes">完整记录大小上限。</param>
        internal SaveRepository(ISaveBackend backend, SaveIdentity identity, SaveDefinition<T> definition, int maxRecordBytes)
        {
            this.backend = backend;
            this.identity = identity;
            this.definition = definition;
            this.maxRecordBytes = backend != null && backend.MaxRecordBytes > 0 ? Math.Min(maxRecordBytes, backend.MaxRecordBytes) : maxRecordBytes;
        }

        /// <summary>
        /// 读取并校验常规双槽。
        /// </summary>
        /// <returns>可恢复的数据或明确错误。</returns>
        internal async UniTask<SaveLoadResult<T>> LoadAsync()
        {
            if (!ValidArguments()) return Failed(SaveError.InvalidArgument, "存档参数无效");
            var a = await ReadSlotAsync(Key("a"));
            var b = await ReadSlotAsync(Key("b"));
            if (a.Error.Error == SaveError.ReadFailed) return Failed(SaveError.ReadFailed, a.Error.Message, true);
            if (b.Error.Error == SaveError.ReadFailed) return Failed(SaveError.ReadFailed, b.Error.Message, true);
            var fatal = Fatal(a, b);
            if (!fatal.Success) return new SaveLoadResult<T>(fatal, default, SaveLoadOrigin.Saved, false, false, 0);
            if (a.Envelope != null && b.Envelope != null && a.Envelope.sequence == b.Envelope.sequence && a.Envelope.checksum != b.Envelope.checksum)
                return Failed(SaveError.Conflict, "双槽同序号内容冲突");
            var selected = a.Envelope == null ? b : b.Envelope == null || a.Envelope.sequence >= b.Envelope.sequence ? a : b;
            if (selected.Envelope == null)
            {
                if (a.Found || b.Found) return Failed(SaveError.Corrupt, "双槽没有可用记录");
                var backupA = await ReadSlotAsync(Key("pre-migration/a"));
                var backupB = await ReadSlotAsync(Key("pre-migration/b"));
                if (backupA.Error.Error == SaveError.TooLarge || backupB.Error.Error == SaveError.TooLarge)
                    return Failed(SaveError.TooLarge, "迁移备份超过当前上限");
                if (backupA.Error.Error == SaveError.ReadFailed || backupB.Error.Error == SaveError.ReadFailed)
                    return Failed(SaveError.ReadFailed, "迁移备份读取失败", true);
                if (backupA.Found || backupB.Found) return Failed(SaveError.RecoveryRequired, "存在迁移备份，需要显式恢复");
                var created = CreateDefault(out var data);
                return new SaveLoadResult<T>(created, data, SaveLoadOrigin.New, false, created.Success, 0);
            }
            var converted = Convert(selected.Envelope, out var loaded, out var migrated);
            var recovered = (a.Found && a.Envelope == null) || (b.Found && b.Envelope == null);
            if (!converted.Success && selected.Envelope.schemaVersion == definition.SchemaVersion &&
                (converted.Error == SaveError.ValidationFailed || converted.Error == SaveError.Corrupt))
            {
                var fallback = selected.Key == a.Key ? b : a;
                if (fallback.Envelope == null) return Failed(SaveError.Corrupt, "没有通过数据校验的记录");
                var fallbackResult = Convert(fallback.Envelope, out loaded, out migrated);
                if (!fallbackResult.Success)
                    return fallbackResult.Error == SaveError.MigrationFailed ?
                        new SaveLoadResult<T>(fallbackResult, default, SaveLoadOrigin.Saved, false, false, 0) :
                        Failed(SaveError.Corrupt, "双槽数据校验失败");
                selected = fallback;
                converted = fallbackResult;
                recovered = true;
            }
            if (!converted.Success) return new SaveLoadResult<T>(converted, default, SaveLoadOrigin.Saved, false, false, 0);
            current = selected.Envelope;
            currentKey = selected.Key;
            migrationSource = selected.Envelope.schemaVersion < definition.SchemaVersion ? selected.Envelope : null;
            return new SaveLoadResult<T>(SaveResult.Ok(), loaded, recovered ? SaveLoadOrigin.Recovered : SaveLoadOrigin.Saved,
                migrated, migrated, Sequence);
        }
        /// <summary>
        /// 校验并封装独立快照。
        /// </summary>
        /// <param name="data">游戏数据快照。</param>
        /// <param name="candidate">创建出的不可变候选。</param>
        /// <returns>准备结果。</returns>
        internal SaveResult Prepare(T data, out SaveCandidate candidate)
        {
            candidate = null;
            if (!ValidArguments()) return SaveResult.Fail(SaveError.InvalidArgument, "存档参数无效");
            if (Sequence == long.MaxValue) return SaveResult.Fail(SaveError.Conflict, "序号已达到上限");
            string payload;
            try
            {
                var problem = definition.Validate(data);
                if (!string.IsNullOrEmpty(problem)) return SaveResult.Fail(SaveError.ValidationFailed, problem);
                payload = definition.Codec.Encode(data);
                if (string.IsNullOrWhiteSpace(payload)) return SaveResult.Fail(SaveError.ValidationFailed, "编码结果为空");
                var independent = definition.Codec.Decode(payload);
                problem = definition.Validate(independent);
                if (!string.IsNullOrEmpty(problem)) return SaveResult.Fail(SaveError.ValidationFailed, problem);
            }
            catch (Exception ex) { return SaveResult.Fail(SaveError.ValidationFailed, ex.Message); }
            var envelope = CreateEnvelope(definition.SchemaVersion, Sequence + 1, payload);
            var json = JsonUtility.ToJson(envelope);
            var key = currentKey == Key("a") ? Key("b") : Key("a");
            candidate = new SaveCandidate(envelope.sequence, json, key, envelope.checksum);
            return candidate.ByteCount > maxRecordBytes ? SaveResult.Fail(SaveError.TooLarge, "完整存档记录超过上限") : SaveResult.Ok();
        }

        /// <summary>
        /// 提交迁移检查点与常规候选并读回确认。
        /// </summary>
        /// <param name="candidate">已准备的不可变候选。</param>
        /// <returns>提交结果。</returns>
        internal async UniTask<SaveResult> CommitAsync(SaveCandidate candidate)
        {
            var check = CheckCandidate(candidate);
            if (!check.Success) return check;
            if (migrationSource != null)
            {
                var checkpoint = await EnsureCheckpointAsync();
                if (!checkpoint.Success) return checkpoint;
            }
            var written = await backend.WriteAsync(candidate.Key, candidate.Json);
            var reconciled = await ReconcileAsync(candidate);
            return !written.Success && reconciled.Error == SaveError.WriteFailed ? written : reconciled;
        }

        /// <summary>
        /// 核对在途候选是否已持久化。
        /// </summary>
        /// <param name="candidate">待核对候选。</param>
        /// <returns>确定成功、未提交或不确定的结果。</returns>
        internal async UniTask<SaveResult> ReconcileAsync(SaveCandidate candidate)
        {
            var check = CheckCandidate(candidate);
            if (!check.Success) return check;
            var target = await ReadSlotAsync(candidate.Key);
            var other = await ReadSlotAsync(candidate.Key == Key("a") ? Key("b") : Key("a"));
            if (target.Error.Error == SaveError.ReadFailed || other.Error.Error == SaveError.ReadFailed ||
                target.Error.Error == SaveError.TooLarge || other.Error.Error == SaveError.TooLarge)
                return SaveResult.Fail(SaveError.Uncertain, "提交后读取失败", true);
            if (other.Error.Error == SaveError.FormatUnsupported || other.Error.Error == SaveError.VersionTooNew)
                return SaveResult.Fail(other.Error.Error, other.Error.Message);
            if (other.Envelope != null && other.Envelope.sequence >= candidate.Sequence)
                return SaveResult.Fail(SaveError.Conflict, "另一槽存在同序号或更新记录");
            if (target.Error.Error == SaveError.FormatUnsupported || target.Error.Error == SaveError.VersionTooNew)
                return SaveResult.Fail(target.Error.Error, target.Error.Message);
            if (!target.Found || target.Envelope == null || target.Envelope.sequence < candidate.Sequence)
                return SaveResult.Fail(SaveError.WriteFailed, "候选未提交", true);
            if (target.Raw != candidate.Json || target.Envelope.checksum != candidate.Checksum)
                return SaveResult.Fail(SaveError.Conflict, "目标槽出现不同记录");
            if (other.Found && other.Envelope == null) return SaveResult.Fail(SaveError.Conflict, "另一槽已经改变");
            current = target.Envelope;
            currentKey = candidate.Key;
            migrationSource = null;
            return SaveResult.Ok();
        }
        /// <summary>
        /// 检查常规槽和迁移备份的恢复候选。
        /// </summary>
        /// <returns>候选列表和读取结果。</returns>
        internal async UniTask<SaveRecoveryInspection> InspectRecoveryAsync()
        {
            var candidates = new List<SaveRecoveryCandidate>();
            if (!ValidArguments()) return new SaveRecoveryInspection(SaveResult.Fail(SaveError.InvalidArgument, "存档参数无效"), candidates);
            foreach (SaveRecoverySource source in Enum.GetValues(typeof(SaveRecoverySource)))
            {
                var slot = await ReadSlotAsync(SourceKey(source));
                if (slot.Error.Error == SaveError.ReadFailed || slot.Error.Error == SaveError.TooLarge)
                    return new SaveRecoveryInspection(slot.Error, candidates);
                if (!slot.Found) continue;
                var envelope = slot.Envelope;
                if (envelope == null)
                {
                    candidates.Add(new SaveRecoveryCandidate(null, identity, source, 0, 0, null, false, slot.Error));
                    continue;
                }
                var usable = Convert(envelope, out _, out _);
                candidates.Add(new SaveRecoveryCandidate(CandidateId(source, envelope), identity, source, envelope.schemaVersion,
                    envelope.sequence, envelope.checksum, usable.Success, usable));
            }
            return new SaveRecoveryInspection(SaveResult.Ok(), candidates);
        }

        /// <summary>
        /// 将显式选择的来源恢复为新空槽的待写数据。
        /// </summary>
        /// <param name="candidate">已检查的来源候选。</param>
        /// <param name="target">新身份的仓库。</param>
        /// <returns>需要激活后首次保存的恢复数据。</returns>
        internal async UniTask<SaveLoadResult<T>> RestoreToNewSlotAsync(SaveRecoveryCandidate candidate, SaveRepository<T> target)
        {
            if (candidate == null || target == null || !candidate.Identity.Equals(identity) || !candidate.IsUsable ||
                !target.ValidArguments() || target.identity.Equals(identity) || target.backend.StorageIdentity != backend.StorageIdentity)
                return Failed(SaveError.InvalidArgument, "恢复参数无效");
            var source = await ReadSlotAsync(SourceKey(candidate.Source));
            if (source.Error.Error == SaveError.TooLarge) return Failed(SaveError.TooLarge, source.Error.Message);
            if (source.Error.Error == SaveError.ReadFailed) return Failed(SaveError.ReadFailed, source.Error.Message, true);
            if (source.Envelope == null || CandidateId(candidate.Source, source.Envelope) != candidate.CandidateId)
                return Failed(SaveError.Conflict, "恢复来源已经改变");
            foreach (var suffix in new[] { "a", "b", "pre-migration/a", "pre-migration/b" })
            {
                var slot = await target.ReadSlotAsync(target.Key(suffix));
                if (slot.Error.Error == SaveError.TooLarge) return Failed(SaveError.TooLarge, slot.Error.Message);
                if (slot.Error.Error == SaveError.ReadFailed) return Failed(SaveError.ReadFailed, slot.Error.Message, true);
                if (slot.Found) return Failed(SaveError.Conflict, "恢复目标不是空槽");
            }
            var converted = Convert(source.Envelope, out var data, out var migrated);
            return new SaveLoadResult<T>(converted, data, SaveLoadOrigin.Recovered, migrated, converted.Success, 0);
        }

        /// <summary>
        /// 创建新档默认数据并验证。
        /// </summary>
        /// <param name="data">已验证默认数据。</param>
        /// <returns>创建结果。</returns>
        private SaveResult CreateDefault(out T data)
        {
            data = default;
            try
            {
                data = definition.CreateDefault();
                var problem = definition.Validate(data);
                return string.IsNullOrEmpty(problem) ? SaveResult.Ok() : SaveResult.Fail(SaveError.ValidationFailed, problem);
            }
            catch (Exception ex) { return SaveResult.Fail(SaveError.ValidationFailed, ex.Message); }
        }

        /// <summary>
        /// 迁移并校验一份原始记录。
        /// </summary>
        /// <param name="envelope">来源封装。</param>
        /// <param name="data">当前版本数据。</param>
        /// <param name="migrated">是否执行迁移。</param>
        /// <returns>转换结果。</returns>
        private SaveResult Convert(Envelope envelope, out T data, out bool migrated)
        {
            data = default;
            migrated = envelope.schemaVersion < definition.SchemaVersion;
            if (envelope.schemaVersion > definition.SchemaVersion) return SaveResult.Fail(SaveError.VersionTooNew, "存档 schema 更新");
            var json = envelope.payloadJson;
            try
            {
                for (var version = envelope.schemaVersion; version < definition.SchemaVersion; version++)
                {
                    if (!definition.Migrations.TryGetValue(version, out var step)) return SaveResult.Fail(SaveError.MigrationFailed, "迁移链缺少版本 " + version);
                    var problem = step.ValidateSource(json);
                    if (!string.IsNullOrEmpty(problem)) return SaveResult.Fail(SaveError.MigrationFailed, problem);
                    json = step.Migrate(json);
                    if (string.IsNullOrWhiteSpace(json)) return SaveResult.Fail(SaveError.MigrationFailed, "迁移输出为空");
                }
                data = definition.Codec.Decode(json);
                var currentProblem = definition.Validate(data);
                return string.IsNullOrEmpty(currentProblem) ? SaveResult.Ok() : SaveResult.Fail(
                    migrated ? SaveError.MigrationFailed : SaveError.ValidationFailed, currentProblem);
            }
            catch (Exception ex) { return SaveResult.Fail(migrated ? SaveError.MigrationFailed : SaveError.Corrupt, ex.Message); }
        }

        /// <summary>
        /// 持久化旧 schema 的独立迁移检查点。
        /// </summary>
        /// <returns>检查点结果。</returns>
        private async UniTask<SaveResult> EnsureCheckpointAsync()
        {
            var a = await ReadSlotAsync(Key("pre-migration/a"));
            var b = await ReadSlotAsync(Key("pre-migration/b"));
            if (a.Error.Error == SaveError.TooLarge || b.Error.Error == SaveError.TooLarge)
                return SaveResult.Fail(SaveError.TooLarge, "迁移备份超过当前上限");
            if (a.Error.Error == SaveError.ReadFailed || b.Error.Error == SaveError.ReadFailed)
                return SaveResult.Fail(SaveError.ReadFailed, "迁移备份读取失败", true);
            if (a.Error.Error == SaveError.FormatUnsupported || a.Error.Error == SaveError.VersionTooNew)
                return a.Error;
            if (b.Error.Error == SaveError.FormatUnsupported || b.Error.Error == SaveError.VersionTooNew)
                return b.Error;
            if (a.Envelope?.checksum == migrationSource.checksum || b.Envelope?.checksum == migrationSource.checksum) return SaveResult.Ok();
            var key = a.Envelope == null ? Key("pre-migration/a") : b.Envelope == null ? Key("pre-migration/b") :
                a.Envelope.sequence <= b.Envelope.sequence ? Key("pre-migration/a") : Key("pre-migration/b");
            var json = JsonUtility.ToJson(migrationSource);
            if (Encoding.UTF8.GetByteCount(json) > maxRecordBytes) return SaveResult.Fail(SaveError.TooLarge, "迁移备份超过上限");
            var write = await backend.WriteAsync(key, json);
            var read = await ReadSlotAsync(key);
            if (read.Error.Error == SaveError.ReadFailed) return SaveResult.Fail(SaveError.Uncertain, "迁移备份读回失败", true);
            if (read.Raw == json && read.Envelope?.checksum == migrationSource.checksum) return SaveResult.Ok();
            return !read.Found && !write.Success ? write : SaveResult.Fail(SaveError.Uncertain, "迁移备份未能确认", true);
        }

        /// <summary>
        /// 读取一个物理槽并校验封装。
        /// </summary>
        /// <param name="key">槽键。</param>
        /// <returns>槽读取状态。</returns>
        private async UniTask<Slot> ReadSlotAsync(string key)
        {
            var read = await backend.ReadAsync(key);
            if (read.Status == SaveReadStatus.Missing) return new Slot(key, false, null, null, SaveResult.Ok());
            if (read.Status == SaveReadStatus.Failed)
            {
                if (read.Result.Error == SaveError.Corrupt)
                    return new Slot(key, true, null, null, SaveResult.Fail(SaveError.Corrupt, read.Result.Message));
                if (read.Result.Error == SaveError.TooLarge)
                    return new Slot(key, true, null, null, SaveResult.Fail(SaveError.TooLarge, read.Result.Message));
                return new Slot(key, false, null, null, SaveResult.Fail(SaveError.ReadFailed, read.Result.Message, read.Result.CanRetry));
            }
            if (read.Record != null && Encoding.UTF8.GetByteCount(read.Record) > maxRecordBytes)
                return new Slot(key, true, read.Record, null, SaveResult.Fail(SaveError.TooLarge, "记录超过当前上限"));
            if (string.IsNullOrEmpty(read.Record))
                return new Slot(key, true, read.Record, null, SaveResult.Fail(SaveError.Corrupt, "记录为空"));
            try
            {
                var envelope = (Envelope)JsonUtility.FromJson(read.Record, typeof(Envelope));
                if (envelope == null) return new Slot(key, true, read.Record, null, SaveResult.Fail(SaveError.Corrupt, "封装为空"));
                if (envelope.formatVersion != 1) return new Slot(key, true, read.Record, null,
                    SaveResult.Fail(SaveError.FormatUnsupported, "封装版本不受支持"));
                if (envelope.sequence <= 0 || envelope.schemaVersion <= 0 || envelope.payloadJson == null ||
                    string.IsNullOrEmpty(envelope.savedAtUtc) || string.IsNullOrEmpty(envelope.checksum) ||
                    envelope.gameId != identity.GameId || envelope.environment != identity.Environment ||
                    envelope.profileId != identity.ProfileId || envelope.slotId != identity.SlotId ||
                    !string.Equals(envelope.checksum, Checksum(envelope), StringComparison.OrdinalIgnoreCase))
                    return new Slot(key, true, read.Record, null, SaveResult.Fail(SaveError.Corrupt, "封装校验失败"));
                if (envelope.schemaVersion > definition.SchemaVersion) return new Slot(key, true, read.Record, envelope,
                    SaveResult.Fail(SaveError.VersionTooNew, "存档 schema 更新"));
                return new Slot(key, true, read.Record, envelope, SaveResult.Ok());
            }
            catch (Exception ex) { return new Slot(key, true, read.Record, null, SaveResult.Fail(SaveError.Corrupt, ex.Message)); }
        }

        /// <summary>
        /// 计算阻断性读取错误。
        /// </summary>
        /// <param name="a">常规 A 槽。</param>
        /// <param name="b">常规 B 槽。</param>
        /// <returns>优先级最高的失败或成功。</returns>
        private static SaveResult Fatal(Slot a, Slot b)
        {
            foreach (var error in new[] { SaveError.TooLarge, SaveError.FormatUnsupported, SaveError.VersionTooNew })
                if (a.Error.Error == error || b.Error.Error == error) return a.Error.Error == error ? a.Error : b.Error;
            return SaveResult.Ok();
        }

        /// <summary>
        /// 验证候选仍属于当前序号和目标槽。
        /// </summary>
        /// <param name="candidate">待验证候选。</param>
        /// <returns>验证结果。</returns>
        private SaveResult CheckCandidate(SaveCandidate candidate) => candidate != null && candidate.Sequence == Sequence + 1 &&
            candidate.Key == (currentKey == Key("a") ? Key("b") : Key("a")) && candidate.ByteCount <= maxRecordBytes
                ? SaveResult.Ok() : SaveResult.Fail(SaveError.InvalidArgument, "候选已过期或无效");

        /// <summary>
        /// 创建封装并计算校验值。
        /// </summary>
        /// <param name="schema">游戏数据版本。</param>
        /// <param name="sequence">记录序号。</param>
        /// <param name="payload">原始数据 JSON。</param>
        /// <returns>完整封装。</returns>
        private Envelope CreateEnvelope(int schema, long sequence, string payload)
        {
            var envelope = new Envelope { formatVersion = 1, gameId = identity.GameId, environment = identity.Environment,
                profileId = identity.ProfileId, slotId = identity.SlotId, schemaVersion = schema, sequence = sequence,
                savedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture), payloadJson = payload };
            envelope.checksum = Checksum(envelope);
            return envelope;
        }

        /// <summary>
        /// 对确定字段顺序和 UTF-8 字节长度计算 SHA-256。
        /// </summary>
        /// <param name="envelope">待校验封装。</param>
        /// <returns>小写十六进制校验值。</returns>
        private static string Checksum(Envelope envelope)
        {
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                writer.Write(envelope.formatVersion);
                WriteString(writer, envelope.gameId);
                WriteString(writer, envelope.environment);
                WriteString(writer, envelope.profileId);
                WriteString(writer, envelope.slotId);
                writer.Write(envelope.schemaVersion);
                writer.Write(envelope.sequence);
                WriteString(writer, envelope.savedAtUtc);
                WriteString(writer, envelope.payloadJson);
            }
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(stream.ToArray());
            var text = new StringBuilder(bytes.Length * 2);
            foreach (var value in bytes) text.Append(value.ToString("x2", CultureInfo.InvariantCulture));
            return text.ToString();
        }

        /// <summary>
        /// 写入带长度前缀的 UTF-8 字符串。
        /// </summary>
        /// <param name="writer">二进制写入器。</param>
        /// <param name="value">字段内容。</param>
        private static void WriteString(BinaryWriter writer, string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
            writer.Write(bytes.Length);
            writer.Write(bytes);
        }

        /// <summary>
        /// 判断构造参数是否满足仓库要求。
        /// </summary>
        /// <returns>参数是否有效。</returns>
        private bool ValidArguments() => backend != null && identity.IsValid && definition?.IsValid == true && maxRecordBytes > 0;

        /// <summary>
        /// 构造身份内物理键。
        /// </summary>
        /// <param name="suffix">槽路径。</param>
        /// <returns>物理键。</returns>
        private string Key(string suffix) => identity.KeyPrefix + "/" + suffix;

        /// <summary>
        /// 取得恢复来源键。
        /// </summary>
        /// <param name="source">恢复来源。</param>
        /// <returns>来源键。</returns>
        private string SourceKey(SaveRecoverySource source) => Key(source switch
        {
            SaveRecoverySource.A => "a", SaveRecoverySource.B => "b",
            SaveRecoverySource.PreMigrationA => "pre-migration/a", _ => "pre-migration/b"
        });

        /// <summary>
        /// 构造候选内容标识。
        /// </summary>
        /// <param name="source">来源槽。</param>
        /// <param name="envelope">候选记录。</param>
        /// <returns>可复查的候选标识。</returns>
        private static string CandidateId(SaveRecoverySource source, Envelope envelope)
            => source + ":" + envelope.sequence.ToString(CultureInfo.InvariantCulture) + ":" + envelope.checksum;

        /// <summary>
        /// 创建失败的加载结果。
        /// </summary>
        /// <param name="error">错误类别。</param>
        /// <param name="message">错误说明。</param>
        /// <param name="retry">是否允许重试。</param>
        /// <returns>失败加载结果。</returns>
        private static SaveLoadResult<T> Failed(SaveError error, string message, bool retry = false)
            => new(SaveResult.Fail(error, message, retry), default, SaveLoadOrigin.Saved, false, false, 0);

        /// <summary>
        /// 单槽读取快照。
        /// </summary>
        private sealed class Slot
        {
            internal string Key { get; }
            internal bool Found { get; }
            internal string Raw { get; }
            internal Envelope Envelope { get; }
            internal SaveResult Error { get; }

            /// <summary>
            /// 创建单槽状态。
            /// </summary>
            /// <param name="key">物理键。</param>
            /// <param name="found">记录是否存在。</param>
            /// <param name="raw">原始记录。</param>
            /// <param name="envelope">已验证封装。</param>
            /// <param name="error">读取或校验结果。</param>
            internal Slot(string key, bool found, string raw, Envelope envelope, SaveResult error)
            {
                Key = key;
                Found = found;
                Raw = raw;
                Envelope = envelope;
                Error = error;
            }
        }
    }
}
