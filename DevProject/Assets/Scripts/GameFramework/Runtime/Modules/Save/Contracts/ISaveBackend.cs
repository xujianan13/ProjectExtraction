using Cysharp.Threading.Tasks;

namespace GameFramework
{
    /// <summary>
    /// 完整存档记录的持久化端口；物理操作真正结束前不得提前完成返回任务。
    /// </summary>
    public interface ISaveBackend
    {
        string StorageIdentity { get; }
        int MaxRecordBytes { get; }

        /// <summary>
        /// 初始化持久存储。
        /// </summary>
        /// <returns>初始化结果。</returns>
        UniTask<SaveResult> InitializeAsync();

        /// <summary>
        /// 读取记录并区分不存在和访问失败。
        /// </summary>
        /// <param name="key">逻辑键。</param>
        /// <returns>读取结果。</returns>
        UniTask<SaveReadResult> ReadAsync(string key);

        /// <summary>
        /// 写入一个完整记录并等待后端提交边界。
        /// </summary>
        /// <param name="key">逻辑键。</param>
        /// <param name="record">完整记录字符串。</param>
        /// <returns>写入结果。</returns>
        UniTask<SaveResult> WriteAsync(string key, string record);
    }
}
