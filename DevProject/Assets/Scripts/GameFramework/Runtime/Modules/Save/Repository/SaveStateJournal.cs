using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace GameFramework
{
    /// <summary>
    /// 一份具有稳定键的状态组件；内容由业务序列化和校验。
    /// </summary>
    [Serializable]
    public sealed class SaveStateComponent
    {
        public string Key;
        public string Value;

        /// <summary>
        /// 创建不共享可写字段的组件副本。
        /// </summary>
        /// <returns>独立组件。</returns>
        public SaveStateComponent Copy() => new() { Key = Key, Value = Value };
    }

    /// <summary>
    /// 完整检查点与连续增量的恢复结果。
    /// </summary>
    public sealed class SaveJournalLoadResult
    {
        public SaveResult Result;
        public bool Missing;
        public bool UsedPreviousRevision;
        public long Revision;
        public List<SaveStateComponent> Components;
    }

    /// <summary>
    /// 在完整记录后端上保存有序组件增量，用双检查点和双提交头保证中断后的一致恢复。
    /// </summary>
    public sealed class SaveStateJournal
    {
        [Serializable]
        private sealed class Pointer
        {
            public int Bank;
            public int Sequence;
            public long Revision;
            public int Chunks;
            public string Digest;
        }

        [Serializable]
        private sealed class Head
        {
            public int Format = 1;
            public string Identity;
            public long Revision;
            public Pointer Checkpoint;
            public List<Pointer> Deltas = new();
        }

        [Serializable]
        private sealed class Payload
        {
            public string Identity;
            public long BaseRevision;
            public long Revision;
            public bool Checkpoint;
            public List<SaveStateComponent> Changed = new();
            public List<string> Removed = new();
        }

        [Serializable]
        private sealed class Chunk
        {
            public string Identity;
            public long Revision;
            public int Index;
            public int Count;
            public string Text;
        }

        private readonly ISaveBackend backend;
        private readonly string prefix;
        private readonly int checkpointInterval;
        private readonly int maxStateBytes;
        private readonly int chunkCharacters;
        private readonly int maxChunks;
        private readonly long maxPayloadBytes;
        private Head committed;
        private Dictionary<string, string> state = new(StringComparer.Ordinal);
        private Head pendingHead;
        private Pointer pendingPointer;
        private Dictionary<string, string> pendingState;
        private string pendingPayload;
        private bool busy;

        public long Revision => committed?.Revision ?? 0;
        public bool HasPending => pendingHead != null;
        public bool IsBusy => busy;
        public long LastPayloadBytes { get; private set; }
        public long TotalWrittenBytes { get; private set; }
        public double LastCommitMilliseconds { get; private set; }

        /// <summary>
        /// 创建由唯一业务会话持有的增量存储；两个数据区轮换，不无限累积历史文件。
        /// </summary>
        /// <param name="backend">已初始化且具有完整记录提交语义的后端。</param>
        /// <param name="prefix">由存档身份派生的独占逻辑键前缀。</param>
        /// <param name="checkpointInterval">每次完整检查点之间允许的增量数。</param>
        /// <param name="maxStateBytes">完整组件列表含 JSON 编码的最大 UTF-8 字节数。</param>
        public SaveStateJournal(ISaveBackend backend, string prefix, int checkpointInterval, int maxStateBytes)
        {
            this.backend = backend;
            this.prefix = prefix;
            this.checkpointInterval = checkpointInterval;
            this.maxStateBytes = maxStateBytes;
            chunkCharacters = Math.Max(1, (backend.MaxRecordBytes - 2048) / 8);
            maxPayloadBytes = maxStateBytes + 2048L;
            maxChunks = (int)Math.Min(int.MaxValue, Math.Max(1, (maxPayloadBytes + chunkCharacters - 1) / chunkCharacters));
        }

        /// <summary>
        /// 恢复指定活动身份最新的完整提交，不跨越缺失或损坏的增量。
        /// </summary>
        /// <param name="identity">业务活动身份。</param>
        /// <returns>完整一致状态、明确缺失或读取错误。</returns>
        public async UniTask<SaveJournalLoadResult> LoadAsync(string identity)
        {
            if (busy || HasPending) return Failed(SaveError.Conflict, "增量存档仍有未完成操作。");
            if (!ValidConfiguration(identity)) return Failed(SaveError.InvalidArgument, "增量存档配置无效。");
            busy = true;
            SaveReadResult first = await backend.ReadAsync(prefix + "/head-a");
            SaveReadResult second = await backend.ReadAsync(prefix + "/head-b");
            if (first.Status == SaveReadStatus.Failed || second.Status == SaveReadStatus.Failed)
            {
                busy = false;
                return new SaveJournalLoadResult { Result = first.Status == SaveReadStatus.Failed ? first.Result : second.Result };
            }
            var candidates = new List<Head>();
            bool foundMatching = false;
            bool damagedHead = false;
            foreach (SaveReadResult read in new[] { first, second })
            {
                if (read.Status != SaveReadStatus.Found) continue;
                if (!TryReadJson(read.Record, out Head head)) { foundMatching = true; damagedHead = true; continue; }
                if (head.Identity != identity) continue;
                foundMatching = true;
                if (Encoding.UTF8.GetByteCount(read.Record) <= backend.MaxRecordBytes && ValidHead(head, identity)) candidates.Add(head);
                else damagedHead = true;
            }
            candidates.Sort((left, right) => right.Revision.CompareTo(left.Revision));
            for (int index = 0; index < candidates.Count; index++)
            {
                Head candidate = candidates[index];
                SaveJournalLoadResult loaded = await ReadHeadAsync(candidate);
                if (!loaded.Result.Success)
                {
                    if (loaded.Result.Error != SaveError.Corrupt)
                    { busy = false; return loaded; }
                    continue;
                }
                committed = candidate;
                state = ToDictionary(loaded.Components);
                loaded.UsedPreviousRevision = index > 0 || damagedHead;
                busy = false;
                return loaded;
            }
            busy = false;
            if (!foundMatching) { committed = null; state.Clear(); }
            return foundMatching ? Failed(SaveError.Corrupt, "未找到完整一致的活动状态，原始记录已保留。") :
                new SaveJournalLoadResult { Result = SaveResult.Ok(), Missing = true };
        }

        /// <summary>
        /// 冻结下一份完整逻辑状态，仅写入与上一份不同的组件及删除键。
        /// </summary>
        /// <param name="identity">业务活动身份。</param>
        /// <param name="components">完整 tick 或完整关键事务收束后的组件。</param>
        /// <returns>持久提交结果；失败时保留同一候选供重试。</returns>
        public UniTask<SaveResult> CommitAsync(string identity, IReadOnlyList<SaveStateComponent> components)
        {
            if (busy || HasPending) return UniTask.FromResult(SaveResult.Fail(SaveError.Conflict, "请先完成原增量存档操作。"));
            if (!ValidConfiguration(identity) || !TryValidateComponents(components, out Dictionary<string, string> next))
                return UniTask.FromResult(SaveResult.Fail(SaveError.ValidationFailed, "活动状态组件或容量无效。"));
            bool newIdentity = committed == null || committed.Identity != identity;
            long revision = newIdentity ? 1 : committed.Revision + 1;
            if (revision <= 0) return UniTask.FromResult(SaveResult.Fail(SaveError.ValidationFailed, "活动状态修订号溢出。"));
            bool checkpoint = newIdentity || committed.Deltas.Count >= checkpointInterval;
            int bank = checkpoint ? (committed == null ? 0 : 1 - committed.Checkpoint.Bank) : committed.Checkpoint.Bank;
            var payload = new Payload { Identity = identity, Revision = revision, BaseRevision = checkpoint ? 0 : committed.Revision,
                Checkpoint = checkpoint };
            foreach (var pair in next)
                if (checkpoint || !state.TryGetValue(pair.Key, out string previous) || previous != pair.Value)
                    payload.Changed.Add(new SaveStateComponent { Key = pair.Key, Value = pair.Value });
            if (!checkpoint)
                foreach (string key in state.Keys) if (!next.ContainsKey(key)) payload.Removed.Add(key);
            payload.Changed.Sort((left, right) => string.CompareOrdinal(left.Key, right.Key));
            payload.Removed.Sort(StringComparer.Ordinal);
            string serialized = JsonUtility.ToJson(payload);
            Pointer pointer = CreatePointer(bank, checkpoint ? 0 : committed.Deltas.Count + 1, revision, serialized);
            Head head = CreateHead(identity, revision, pointer, checkpoint);
            if (!checkpoint && (Encoding.UTF8.GetByteCount(serialized) > maxPayloadBytes ||
                Encoding.UTF8.GetByteCount(JsonUtility.ToJson(head)) > backend.MaxRecordBytes))
            {
                if (committed.Deltas.Count == 0)
                    return UniTask.FromResult(SaveResult.Fail(SaveError.TooLarge, "后端无法容纳最低连续增量提交头。"));
                payload.BaseRevision = 0;
                payload.Checkpoint = true;
                payload.Changed.Clear();
                payload.Removed.Clear();
                foreach (var pair in next) payload.Changed.Add(new SaveStateComponent { Key = pair.Key, Value = pair.Value });
                payload.Changed.Sort((left, right) => string.CompareOrdinal(left.Key, right.Key));
                serialized = JsonUtility.ToJson(payload);
                pointer = CreatePointer(1 - committed.Checkpoint.Bank, 0, revision, serialized);
                head = CreateHead(identity, revision, pointer, true);
            }
            if (Encoding.UTF8.GetByteCount(serialized) > maxPayloadBytes || pointer.Chunks > maxChunks ||
                Encoding.UTF8.GetByteCount(JsonUtility.ToJson(head)) > backend.MaxRecordBytes)
                return UniTask.FromResult(SaveResult.Fail(SaveError.TooLarge, "活动状态超出载荷或提交头容量。"));
            pendingPayload = serialized;
            pendingPointer = pointer;
            pendingHead = head;
            pendingState = next;
            return RetryAsync();
        }

        /// <summary>
        /// 重试原候选的相同数据区与修订号，不创建新的逻辑提交。
        /// </summary>
        /// <returns>原候选持久提交结果。</returns>
        public async UniTask<SaveResult> RetryAsync()
        {
            if (busy || !HasPending) return SaveResult.Fail(SaveError.Conflict, "没有可重试的活动状态，或写入尚未结束。");
            busy = true;
            var timer = System.Diagnostics.Stopwatch.StartNew();
            LastPayloadBytes = Encoding.UTF8.GetByteCount(pendingPayload);
            for (int index = 0; index < pendingPointer.Chunks; index++)
            {
                int start = index * chunkCharacters;
                string text = pendingPayload.Substring(start, Math.Min(chunkCharacters, pendingPayload.Length - start));
                string record = JsonUtility.ToJson(new Chunk { Identity = pendingHead.Identity, Revision = pendingHead.Revision,
                    Index = index, Count = pendingPointer.Chunks, Text = text });
                SaveResult result = await WriteVerifiedAsync(BlockKey(pendingPointer, index), record);
                if (!result.Success) { busy = false; return result; }
            }
            string headRecord = JsonUtility.ToJson(pendingHead);
            SaveResult committedResult = await WriteVerifiedAsync(prefix + (pendingHead.Revision % 2 == 0 ? "/head-a" : "/head-b"), headRecord);
            if (!committedResult.Success) { busy = false; return committedResult; }
            committed = pendingHead;
            state = pendingState;
            pendingHead = null;
            pendingPointer = null;
            pendingState = null;
            pendingPayload = null;
            LastCommitMilliseconds = timer.Elapsed.TotalMilliseconds;
            busy = false;
            return SaveResult.Ok();
        }

        /// <summary>
        /// 顺序读取检查点和全部连续增量，并验证最终修订。
        /// </summary>
        /// <param name="head">已经校验的提交头。</param>
        /// <returns>重建的组件状态。</returns>
        private async UniTask<SaveJournalLoadResult> ReadHeadAsync(Head head)
        {
            var pointers = new List<Pointer> { head.Checkpoint };
            pointers.AddRange(head.Deltas);
            var restored = new Dictionary<string, string>(StringComparer.Ordinal);
            long revision = 0;
            foreach (Pointer pointer in pointers)
            {
                var builder = new StringBuilder();
                for (int index = 0; index < pointer.Chunks; index++)
                {
                    SaveReadResult read = await backend.ReadAsync(BlockKey(pointer, index));
                    if (read.Status == SaveReadStatus.Failed) return new SaveJournalLoadResult { Result = read.Result };
                    if (read.Status != SaveReadStatus.Found || !TryReadJson(read.Record, out Chunk chunk) ||
                        Encoding.UTF8.GetByteCount(read.Record) > backend.MaxRecordBytes ||
                        chunk.Identity != head.Identity || chunk.Revision != pointer.Revision || chunk.Index != index ||
                        chunk.Count != pointer.Chunks || chunk.Text == null || chunk.Text.Length > chunkCharacters)
                        return Failed(SaveError.Corrupt, "活动状态分块缺失或身份不匹配。");
                    builder.Append(chunk.Text);
                    if (builder.Length > maxPayloadBytes) return Failed(SaveError.Corrupt, "活动状态载荷超出容量。");
                }
                string text = builder.ToString();
                if (Encoding.UTF8.GetByteCount(text) > maxPayloadBytes || Digest(text) != pointer.Digest ||
                    !TryReadJson(text, out Payload payload) || payload.Identity != head.Identity ||
                    payload.Revision != pointer.Revision || payload.BaseRevision != revision || payload.Checkpoint != (revision == 0) ||
                    payload.Changed == null || payload.Removed == null)
                    return Failed(SaveError.Corrupt, "活动状态校验和或连续修订无效。");
                var changedKeys = new HashSet<string>(StringComparer.Ordinal);
                foreach (SaveStateComponent component in payload.Changed)
                {
                    if (component == null || string.IsNullOrEmpty(component.Key) || component.Value == null || !changedKeys.Add(component.Key))
                        return Failed(SaveError.Corrupt, "活动状态组件重复或缺失。");
                    restored[component.Key] = component.Value;
                }
                foreach (string key in payload.Removed)
                    if (key == null || !changedKeys.Add(key) || !restored.Remove(key))
                        return Failed(SaveError.Corrupt, "活动状态删除键无效。");
                revision = payload.Revision;
            }
            var components = new List<SaveStateComponent>();
            foreach (var pair in restored) components.Add(new SaveStateComponent { Key = pair.Key, Value = pair.Value });
            if (revision != head.Revision || !TryValidateComponents(components, out _))
                return Failed(SaveError.Corrupt, "活动状态最终修订或容量无效。");
            return new SaveJournalLoadResult { Result = SaveResult.Ok(), Revision = revision, Components = components };
        }

        /// <summary>
        /// 等待后端提交并读回同一记录，只有完全一致才发布成功。
        /// </summary>
        /// <param name="key">目标逻辑键。</param>
        /// <param name="record">冻结记录。</param>
        /// <returns>提交确认结果。</returns>
        private async UniTask<SaveResult> WriteVerifiedAsync(string key, string record)
        {
            if (Encoding.UTF8.GetByteCount(record) > backend.MaxRecordBytes)
                return SaveResult.Fail(SaveError.TooLarge, "活动状态单条记录超出后端容量。");
            SaveResult written = await backend.WriteAsync(key, record);
            if (!written.Success && written.Error != SaveError.Uncertain) return written;
            TotalWrittenBytes += Encoding.UTF8.GetByteCount(record);
            SaveReadResult read = await backend.ReadAsync(key);
            if (read.Status != SaveReadStatus.Found || read.Record != record)
                return SaveResult.Fail(SaveError.Uncertain, "活动状态提交尚未获得完整读回确认。", true);
            return SaveResult.Ok();
        }

        /// <summary>
        /// 校验提交头和增量列表的结构上限。
        /// </summary>
        /// <param name="head">反序列化的提交头。</param>
        /// <param name="identity">期望业务身份。</param>
        /// <returns>提交头结构可读时为真。</returns>
        private bool ValidHead(Head head, string identity)
        {
            if (head.Format != 1 || head.Identity != identity || head.Revision < 1 || !ValidPointer(head.Checkpoint) ||
                head.Checkpoint.Sequence != 0 || head.Deltas == null || head.Deltas.Count > checkpointInterval) return false;
            long revision = head.Checkpoint.Revision;
            for (int index = 0; index < head.Deltas.Count; index++)
            {
                Pointer pointer = head.Deltas[index];
                if (!ValidPointer(pointer) || pointer.Bank != head.Checkpoint.Bank || pointer.Sequence != index + 1 ||
                    pointer.Revision != revision + 1) return false;
                revision = pointer.Revision;
            }
            return revision == head.Revision;
        }

        /// <summary>
        /// 检查一段数据引用的范围。
        /// </summary>
        /// <param name="pointer">数据引用。</param>
        /// <returns>引用字段合法时为真。</returns>
        private bool ValidPointer(Pointer pointer) => pointer != null && pointer.Bank >= 0 && pointer.Bank <= 1 &&
            pointer.Sequence >= 0 && pointer.Sequence <= checkpointInterval && pointer.Revision > 0 &&
            pointer.Chunks > 0 && pointer.Chunks <= maxChunks && pointer.Digest?.Length == 64;

        /// <summary>
        /// 检查外部传入的组件和状态总量。
        /// </summary>
        /// <param name="components">待提交组件。</param>
        /// <param name="result">已经隔离的键值状态。</param>
        /// <returns>组件唯一且容量有效时为真。</returns>
        private bool TryValidateComponents(IReadOnlyList<SaveStateComponent> components, out Dictionary<string, string> result)
        {
            result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (components == null || components.Count == 0) return false;
            long bytes = 2;
            foreach (SaveStateComponent component in components)
            {
                if (component == null || string.IsNullOrWhiteSpace(component.Key) || component.Key.Length > 256 || component.Value == null ||
                    result.ContainsKey(component.Key)) return false;
                result.Add(component.Key, component.Value);
                bytes += Encoding.UTF8.GetByteCount(JsonUtility.ToJson(component)) + (result.Count > 1 ? 1 : 0);
                if (bytes > maxStateBytes) return false;
            }
            return true;
        }

        /// <summary>
        /// 创建冻结载荷的分块指针。
        /// </summary>
        /// <param name="bank">目标数据区。</param>
        /// <param name="sequence">段序号。</param>
        /// <param name="revision">候选修订。</param>
        /// <param name="payload">完整编码载荷。</param>
        /// <returns>内容摘要与分块数量固定的指针。</returns>
        private Pointer CreatePointer(int bank, int sequence, long revision, string payload) => new()
        {
            Bank = bank, Sequence = sequence, Revision = revision,
            Chunks = (payload.Length + chunkCharacters - 1) / chunkCharacters, Digest = Digest(payload)
        };

        /// <summary>
        /// 创建候选提交头，不改变已提交的增量列表。
        /// </summary>
        /// <param name="identity">业务活动身份。</param>
        /// <param name="revision">候选修订。</param>
        /// <param name="pointer">本次新段指针。</param>
        /// <param name="checkpoint">是否建立完整检查点。</param>
        /// <returns>完整候选提交头。</returns>
        private Head CreateHead(string identity, long revision, Pointer pointer, bool checkpoint)
        {
            var head = new Head { Identity = identity, Revision = revision, Checkpoint = checkpoint ? pointer : committed.Checkpoint,
                Deltas = checkpoint ? new List<Pointer>() : new List<Pointer>(committed.Deltas) };
            if (!checkpoint) head.Deltas.Add(pointer);
            return head;
        }

        /// <summary>
        /// 检查存储配置与身份的基本范围。
        /// </summary>
        /// <param name="identity">业务活动身份。</param>
        /// <returns>配置可以用于分块存储时为真。</returns>
        private bool ValidConfiguration(string identity) => !string.IsNullOrWhiteSpace(identity) && identity.Length <= 128 &&
            !string.IsNullOrWhiteSpace(prefix) && checkpointInterval >= 1 && checkpointInterval <= 256 &&
            maxStateBytes > 0 && maxStateBytes <= 64 * 1024 * 1024 && backend.MaxRecordBytes >= 4096;

        /// <summary>
        /// 取得固定数据区内的分块键。
        /// </summary>
        /// <param name="pointer">段引用。</param>
        /// <param name="index">分块序号。</param>
        /// <returns>受控的逻辑键。</returns>
        private string BlockKey(Pointer pointer, int index) => prefix + "/bank-" + pointer.Bank + "/segment-" + pointer.Sequence + "/chunk-" + index;

        /// <summary>
        /// 将已验证组件转换为内部独立键值表。
        /// </summary>
        /// <param name="components">完整组件列表。</param>
        /// <returns>独立键值表。</returns>
        private static Dictionary<string, string> ToDictionary(List<SaveStateComponent> components)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (SaveStateComponent component in components) result.Add(component.Key, component.Value);
            return result;
        }

        /// <summary>
        /// 计算完整载荷的内容摘要。
        /// </summary>
        /// <param name="text">冻结的 UTF-8 内容。</param>
        /// <returns>十六进制 SHA-256 摘要。</returns>
        private static string Digest(string text)
        {
            using SHA256 hash = SHA256.Create();
            return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "").ToLowerInvariant();
        }

        /// <summary>
        /// 在外部存储边界解析 JSON，损坏记录返回失败并保留原数据。
        /// </summary>
        /// <typeparam name="T">字段式记录类型。</typeparam>
        /// <param name="text">待解析文本。</param>
        /// <param name="value">解析后的记录。</param>
        /// <returns>文本可以解析为记录时为真。</returns>
        private static bool TryReadJson<T>(string text, out T value) where T : class
        {
            value = null;
            if (string.IsNullOrWhiteSpace(text)) return false;
            try { value = JsonUtility.FromJson<T>(text); return value != null; }
            catch (ArgumentException) { return false; }
        }

        /// <summary>
        /// 创建明确的恢复失败结果。
        /// </summary>
        /// <param name="error">错误分类。</param>
        /// <param name="message">可诊断的原因。</param>
        /// <returns>不含默认状态的失败结果。</returns>
        private static SaveJournalLoadResult Failed(SaveError error, string message) => new() { Result = SaveResult.Fail(error, message) };
    }
}
