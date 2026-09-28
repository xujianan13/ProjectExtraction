namespace GameFramework
{
    /// <summary>
    /// 存储空间工厂，按平台或配置创建具体 <see cref="StorageSpace"/> 实现。
    /// </summary>
    public interface IStorageSpaceFactory
    {
        /// <summary>
        /// 创建指定名称的存储空间。
        /// </summary>
        /// <param name="name">空间名称，由具体实现决定如何使用（如 key 前缀）。</param>
        StorageSpace Create(string name);
    }
}
