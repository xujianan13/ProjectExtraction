using System;
using System.Collections.Generic;
using System.Text;
using Cysharp.Threading.Tasks;

namespace GameFramework
{
    /// <summary>
    /// 仅供测试使用的进程内存存档后端。
    /// </summary>
    public sealed class MemorySaveBackend : ISaveBackend
    {
        private readonly Dictionary<string, string> records = new(StringComparer.Ordinal);
        private readonly int maxRecordBytes;

        public string StorageIdentity { get; }
        public int MaxRecordBytes => maxRecordBytes;

        /// <summary>
        /// 创建隔离的内存后端。
        /// </summary>
        /// <param name="storageIdentity">共享存储身份；不传入时使用唯一身份。</param>
        /// <param name="maxRecordBytes">单条记录最大 UTF-8 字节数。</param>
        public MemorySaveBackend(string storageIdentity = null, int maxRecordBytes = 262144)
        {
            StorageIdentity = string.IsNullOrWhiteSpace(storageIdentity) ? $"memory://{Guid.NewGuid():N}" : storageIdentity;
            this.maxRecordBytes = maxRecordBytes;
        }

        /// <summary>
        /// 检查内存后端配置。
        /// </summary>
        /// <returns>初始化结果。</returns>
        public UniTask<SaveResult> InitializeAsync() => UniTask.FromResult(maxRecordBytes > 0
            ? SaveResult.Ok() : SaveResult.Fail(SaveError.InvalidArgument, "Invalid memory record limit."));

        /// <summary>
        /// 读取指定记录。
        /// </summary>
        /// <param name="key">逻辑键。</param>
        /// <returns>读取结果。</returns>
        public UniTask<SaveReadResult> ReadAsync(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return UniTask.FromResult(SaveReadResult.Failed(SaveResult.Fail(SaveError.InvalidArgument, "Invalid save key.")));
            return UniTask.FromResult(records.TryGetValue(key, out var record) ? SaveReadResult.Found(record) : SaveReadResult.Missing());
        }

        /// <summary>
        /// 写入指定记录。
        /// </summary>
        /// <param name="key">逻辑键。</param>
        /// <param name="record">完整记录。</param>
        /// <returns>写入结果。</returns>
        public UniTask<SaveResult> WriteAsync(string key, string record)
        {
            if (string.IsNullOrWhiteSpace(key) || record == null) return UniTask.FromResult(SaveResult.Fail(SaveError.InvalidArgument, "Invalid save key or record."));
            if (maxRecordBytes <= 0) return UniTask.FromResult(SaveResult.Fail(SaveError.InvalidArgument, "Invalid memory record limit."));
            if (Encoding.UTF8.GetByteCount(record) > maxRecordBytes) return UniTask.FromResult(SaveResult.Fail(SaveError.TooLarge, "Save record is too large."));
            records[key] = record;
            return UniTask.FromResult(SaveResult.Ok());
        }
    }
}
