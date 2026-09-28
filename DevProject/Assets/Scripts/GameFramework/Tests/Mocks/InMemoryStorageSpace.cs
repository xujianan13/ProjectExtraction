using System.Collections.Generic;

namespace GameFramework.Tests.Mocks
{
    /// <summary>
    /// 内存存储空间，供 StorageModule 测试使用。
    /// </summary>
    internal sealed class InMemoryStorageSpace : StorageSpace
    {
        private readonly Dictionary<string, string> _data = new(16);
        public int SaveCount { get; private set; }
        public int DeleteAllCount { get; private set; }

        public override bool HasKey(string key) => _data.ContainsKey(key);

        public override void Delete(string key) => _data.Remove(key);

        public override void DeleteAll()
        {
            _data.Clear();
            DeleteAllCount++;
        }

        public override string GetString(string key, string defaultValue = null)
            => _data.TryGetValue(key, out var value) ? value : defaultValue;

        public override void SetString(string key, string value) => _data[key] = value;

        public override int GetInt(string key, int defaultValue = 0)
            => _data.TryGetValue(key, out var value) && int.TryParse(value, out var result) ? result : defaultValue;

        public override void SetInt(string key, int value) => _data[key] = value.ToString();

        public override float GetFloat(string key, float defaultValue = 0f)
            => _data.TryGetValue(key, out var value) && float.TryParse(value, out var result) ? result : defaultValue;

        public override void SetFloat(string key, float value) => _data[key] = value.ToString();

        public override bool GetBool(string key, bool defaultValue = false)
            => _data.TryGetValue(key, out var value) && bool.TryParse(value, out var result) ? result : defaultValue;

        public override void SetBool(string key, bool value) => _data[key] = value.ToString();

        public override byte[] GetBytes(string key)
            => _data.TryGetValue(key, out var value) ? System.Convert.FromBase64String(value) : null;

        public override void SetBytes(string key, byte[] value)
        {
            if (value == null) Delete(key);
            else _data[key] = System.Convert.ToBase64String(value);
        }

        public override T GetObject<T>(string key, T defaultValue = default) => defaultValue;

        public override void SetObject<T>(string key, T value)
        {
            if (value == null) Delete(key);
        }

        public override void Save() => SaveCount++;
    }
}
