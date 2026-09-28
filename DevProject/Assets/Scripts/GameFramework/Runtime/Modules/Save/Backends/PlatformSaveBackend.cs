using System;
using System.Text;
using Cysharp.Threading.Tasks;

namespace GameFramework
{
    /// <summary>
    /// 小游戏平台真实 SDK 存储调用的注入端口。
    /// </summary>
    public interface IPlatformSaveStore
    {
        string StorageIdentity { get; }
        int MaxRecordBytes { get; }

        /// <summary>
        /// 检查 SDK 可用性。
        /// </summary>
        /// <returns>初始化结果。</returns>
        UniTask<SaveResult> InitializeAsync();

        /// <summary>
        /// 读取 SDK 本地键值；缺失必须与失败区分。
        /// </summary>
        /// <param name="key">完整逻辑键。</param>
        /// <returns>读取结果。</returns>
        UniTask<SaveReadResult> GetAsync(string key);

        /// <summary>
        /// 写入 SDK 本地键值；完成任务时 SDK 操作必须已经结束。
        /// </summary>
        /// <param name="key">完整逻辑键。</param>
        /// <param name="record">完整记录。</param>
        /// <returns>SDK 完成结果。</returns>
        UniTask<SaveResult> SetAsync(string key, string record);
    }

    /// <summary>
    /// 小游戏平台键值存储后端；真实 SDK 由游戏包注入。
    /// </summary>
    public sealed class PlatformSaveBackend : ISaveBackend
    {
        private readonly IPlatformSaveStore store;
        public string StorageIdentity => store?.StorageIdentity ?? "platform://unavailable";
        public int MaxRecordBytes => store?.MaxRecordBytes ?? 0;

        /// <summary>
        /// 创建注入式平台后端。
        /// </summary>
        /// <param name="store">经所选 SDK 实现的真实存储端口。</param>
        public PlatformSaveBackend(IPlatformSaveStore store) => this.store = store;

        /// <summary>
        /// 检查实际 SDK 存储能力。
        /// </summary>
        /// <returns>初始化结果。</returns>
        public UniTask<SaveResult> InitializeAsync()
        {
            if (store == null) return UniTask.FromResult(SaveResult.Fail(SaveError.Unsupported, "Platform save SDK is not configured."));
            if (string.IsNullOrWhiteSpace(store.StorageIdentity) || store.MaxRecordBytes <= 0)
                return UniTask.FromResult(SaveResult.Fail(SaveError.InvalidArgument, "Platform save store identity or capacity is invalid."));
            return store.InitializeAsync();
        }

        /// <summary>
        /// 经 SDK 读取记录。
        /// </summary>
        /// <param name="key">逻辑键。</param>
        /// <returns>读取结果。</returns>
        public UniTask<SaveReadResult> ReadAsync(string key)
        {
            if (store == null) return UniTask.FromResult(SaveReadResult.Failed(SaveResult.Fail(SaveError.Unsupported, "Platform save SDK is not configured.")));
            if (string.IsNullOrWhiteSpace(key)) return UniTask.FromResult(SaveReadResult.Failed(SaveResult.Fail(SaveError.InvalidArgument, "Invalid save key.")));
            return store.GetAsync(key);
        }

        /// <summary>
        /// 经 SDK 写入完整记录。
        /// </summary>
        /// <param name="key">逻辑键。</param>
        /// <param name="record">完整记录。</param>
        /// <returns>SDK 提供的完成结果。</returns>
        public UniTask<SaveResult> WriteAsync(string key, string record)
        {
            if (store == null) return UniTask.FromResult(SaveResult.Fail(SaveError.Unsupported, "Platform save SDK is not configured."));
            if (string.IsNullOrWhiteSpace(key) || record == null) return UniTask.FromResult(SaveResult.Fail(SaveError.InvalidArgument, "Invalid save key or record."));
            if (Encoding.UTF8.GetByteCount(record) > store.MaxRecordBytes) return UniTask.FromResult(SaveResult.Fail(SaveError.TooLarge, "Save record exceeds platform limit."));
            return store.SetAsync(key, record);
        }
    }
}
