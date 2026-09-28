namespace GameFramework
{
    /// <summary>
    /// 本地持久化模块接口。负责配置底层存储驱动，并统一管理所有存储空间。
    /// </summary>
    public interface IStorageModule : IModule
    {
        /// <summary>
        /// 获取当前使用的存储空间工厂。
        /// </summary>
        /// <returns></returns>
        IStorageSpaceFactory GetFactory();

        /// <summary>
        /// 设置当前使用的存储空间工厂。
        /// </summary>
        /// <param name="factory"></param>
        /// <returns></returns>
        IStorageSpaceFactory SetFactory(IStorageSpaceFactory factory);

        /// <summary>
        /// 获取全局存储空间。
        /// </summary>
        /// <returns></returns>
        StorageSpace GetGlobal();

        /// <summary>
        /// 获取指定 name 对应的存储空间。不存在时返回 null。
        /// </summary>
        StorageSpace Get(string name);

        /// <summary>
        /// 添加指定 name 对应的存储空间。已存在时返回现有空间。
        /// </summary>
        StorageSpace Add(string name);

        /// <summary>
        /// 删除指定 name 对应的存储空间及其底层数据。
        /// </summary>
        bool Delete(string name);

        /// <summary>
        /// 保存所有存储空间的底层数据。
        /// </summary>
        void SaveAll();

        /// <summary>
        /// 删除底层存储中的所有数据。
        /// </summary>
        void DeleteAll();
    }
}
