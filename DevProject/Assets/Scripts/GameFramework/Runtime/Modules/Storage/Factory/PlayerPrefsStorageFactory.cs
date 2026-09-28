namespace GameFramework
{
    /// <summary>
    /// 基于 PlayerPrefs 的存储空间工厂。
    /// </summary>
    public sealed class PlayerPrefsStorageFactory : IStorageSpaceFactory
    {
        /// <inheritdoc />
        public StorageSpace Create(string name) => new PlayerPrefsStorageSpace(name);
    }
}
