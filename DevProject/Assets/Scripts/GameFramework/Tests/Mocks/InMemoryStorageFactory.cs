namespace GameFramework.Tests.Mocks
{
    /// <summary>
    /// 创建 InMemoryStorageSpace 的工厂。
    /// </summary>
    internal sealed class InMemoryStorageFactory : IStorageSpaceFactory
    {
        public StorageSpace Create(string name) => new InMemoryStorageSpace();
    }
}
