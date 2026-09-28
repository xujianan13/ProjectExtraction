namespace GameFramework
{
    /// <summary>
    /// 本地持久化存储空间抽象基类。具体子类负责实现底层读写逻辑。
    /// </summary>
    public abstract class StorageSpace
    {
        /// <summary>
        /// 判断 key 是否存在。
        /// </summary>
        public abstract bool HasKey(string key);

        /// <summary>
        /// 删除指定 key。
        /// </summary>
        public abstract void Delete(string key);

        /// <summary>
        /// 删除底层存储中的所有数据。
        /// </summary>
        public abstract void DeleteAll();

        /// <summary>
        /// 读取字符串。
        /// </summary>
        public abstract string GetString(string key, string defaultValue = null);

        /// <summary>
        /// 写入字符串。
        /// </summary>
        public abstract void SetString(string key, string value);

        /// <summary>
        /// 读取整数。
        /// </summary>
        public abstract int GetInt(string key, int defaultValue = 0);

        /// <summary>
        /// 写入整数。
        /// </summary>
        public abstract void SetInt(string key, int value);

        /// <summary>
        /// 读取浮点数。
        /// </summary>
        public abstract float GetFloat(string key, float defaultValue = 0f);

        /// <summary>
        /// 写入浮点数。
        /// </summary>
        public abstract void SetFloat(string key, float value);

        /// <summary>
        /// 读取布尔值。
        /// </summary>
        public abstract bool GetBool(string key, bool defaultValue = false);

        /// <summary>
        /// 写入布尔值。
        /// </summary>
        public abstract void SetBool(string key, bool value);

        /// <summary>
        /// 读取二进制数据。key 不存在时返回 null。
        /// </summary>
        public abstract byte[] GetBytes(string key);

        /// <summary>
        /// 写入二进制数据。value 为 null 时删除 key。
        /// </summary>
        public abstract void SetBytes(string key, byte[] value);

        /// <summary>
        /// 读取 JSON 对象。key 不存在或反序列化失败时返回 defaultValue。
        /// </summary>
        public abstract T GetObject<T>(string key, T defaultValue = default) where T : class;

        /// <summary>
        /// 写入 JSON 对象。value 为 null 时删除 key。
        /// </summary>
        public abstract void SetObject<T>(string key, T value) where T : class;

        /// <summary>
        /// 将内存中的修改刷入持久化介质。
        /// </summary>
        public abstract void Save();
    }
}
