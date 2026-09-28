using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace GameFramework
{
    /// <summary>
    /// 基于 PlayerPrefs 的存储空间实现。
    /// Standalone / Editor 使用系统默认位置，标准 WebGL 使用 IndexedDB；小游戏行为由实际接入的 SDK 决定。
    /// </summary>
    public sealed class PlayerPrefsStorageSpace : StorageSpace
    {
        private readonly string _name;
        private readonly string _registryKey;
        private readonly string _legacyRegistryKey;
        private readonly string _prefix;

        /// <summary>
        /// 创建 PlayerPrefs 存储空间。
        /// </summary>
        /// <param name="name">空间名称，作为底层 key 前缀以隔离不同空间。</param>
        public PlayerPrefsStorageSpace(string name)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("存储空间 name 不能为空。", nameof(name));
            _name = name;
            _prefix = string.Concat("GF2|", Encode(_name), "|");
            _registryKey = string.Concat(_prefix, "R");
            _legacyRegistryKey = string.Concat(_name, "__registry__");
        }

        /// <summary>
        /// 存储空间名称。
        /// </summary>
        public string Name => _name;

        /// <inheritdoc />
        public override bool HasKey(string key) => TryResolveKey(key, out _, out _);

        /// <inheritdoc />
        public override void Delete(string key)
        {
            var storageKey = ToStorageKey(key);
            PlayerPrefs.DeleteKey(storageKey);
            UntrackKey(storageKey);
            BlockLegacyKey(key);
        }

        /// <inheritdoc />
        public override void DeleteAll()
        {
            var keys = ReadRegistry();
            for (int i = 0; i < keys.Count; i++)
            {
                PlayerPrefs.DeleteKey(keys[i]);
                PlayerPrefs.SetInt(string.Concat(_prefix, "B|", keys[i].Substring(_prefix.Length + 2)), 1);
            }

            var legacyKeys = ReadLegacyRegistry();
            for (int i = 0; i < legacyKeys.Count; i++)
            {
                if (legacyKeys[i].StartsWith(_name, StringComparison.Ordinal))
                    BlockLegacyKey(legacyKeys[i].Substring(_name.Length));
            }
            PlayerPrefs.DeleteKey(_registryKey);
        }

        /// <inheritdoc />
        public override string GetString(string key, string defaultValue = null)
        {
            if (!TryResolveKey(key, out string storageKey, out bool legacy)) return defaultValue;
            string value = PlayerPrefs.GetString(storageKey);
            if (legacy) SetString(key, value);
            return value;
        }

        /// <inheritdoc />
        public override void SetString(string key, string value)
        {
            var storageKey = ToStorageKey(key);
            PlayerPrefs.SetString(storageKey, value ?? string.Empty);
            TrackKey(storageKey);
        }

        /// <inheritdoc />
        public override int GetInt(string key, int defaultValue = 0)
        {
            if (!TryResolveKey(key, out string storageKey, out bool legacy)) return defaultValue;
            int value = PlayerPrefs.GetInt(storageKey, defaultValue);
            if (legacy) SetInt(key, value);
            return value;
        }

        /// <inheritdoc />
        public override void SetInt(string key, int value)
        {
            var storageKey = ToStorageKey(key);
            PlayerPrefs.SetInt(storageKey, value);
            TrackKey(storageKey);
        }

        /// <inheritdoc />
        public override float GetFloat(string key, float defaultValue = 0f)
        {
            if (!TryResolveKey(key, out string storageKey, out bool legacy)) return defaultValue;
            float value = PlayerPrefs.GetFloat(storageKey, defaultValue);
            if (legacy) SetFloat(key, value);
            return value;
        }

        /// <inheritdoc />
        public override void SetFloat(string key, float value)
        {
            var storageKey = ToStorageKey(key);
            PlayerPrefs.SetFloat(storageKey, value);
            TrackKey(storageKey);
        }

        /// <inheritdoc />
        public override bool GetBool(string key, bool defaultValue = false)
            => GetInt(key, defaultValue ? 1 : 0) != 0;

        /// <inheritdoc />
        public override void SetBool(string key, bool value)
        {
            var storageKey = ToStorageKey(key);
            PlayerPrefs.SetInt(storageKey, value ? 1 : 0);
            TrackKey(storageKey);
        }

        /// <inheritdoc />
        public override byte[] GetBytes(string key)
        {
            string encoded = GetString(key);
            if (encoded == null) return null;
            if (string.IsNullOrEmpty(encoded)) return Array.Empty<byte>();

            try
            {
                return Convert.FromBase64String(encoded);
            }
            catch (FormatException)
            {
                return null;
            }
        }

        /// <inheritdoc />
        public override void SetBytes(string key, byte[] value)
        {
            if (value == null)
            {
                Delete(key);
                return;
            }

            var storageKey = ToStorageKey(key);
            PlayerPrefs.SetString(storageKey, Convert.ToBase64String(value));
            TrackKey(storageKey);
        }

        /// <inheritdoc />
        public override T GetObject<T>(string key, T defaultValue = default)
        {
            string json = GetString(key);
            if (json == null) return defaultValue;
            if (string.IsNullOrEmpty(json)) return defaultValue;

            try
            {
                return JsonUtility.FromJson<T>(json);
            }
            catch (Exception)
            {
                return defaultValue;
            }
        }

        /// <inheritdoc />
        public override void SetObject<T>(string key, T value)
        {
            if (value == null)
            {
                Delete(key);
                return;
            }

            var storageKey = ToStorageKey(key);
            PlayerPrefs.SetString(storageKey, JsonUtility.ToJson(value));
            TrackKey(storageKey);
        }

        /// <inheritdoc />
        public override void Save() => PlayerPrefs.Save();

        /// <summary>
        /// 构建不会与其他空间或注册表冲突的新版底层键。
        /// </summary>
        /// <param name="key">逻辑键。</param>
        /// <returns>新版底层键。</returns>
        private string ToStorageKey(string key)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("存储 key 不能为空。", nameof(key));
            return string.Concat(_prefix, "D|", Encode(key));
        }


        /// <summary>
        /// 将名称编码为不含换行的 PlayerPrefs 键片段。
        /// </summary>
        /// <param name="value">原始名称。</param>
        /// <returns>Base64 编码的键片段。</returns>
        private static string Encode(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

        /// <summary>
        /// 先查新版键，再按旧版注册表查找尚未迁移的数据。
        /// </summary>
        /// <param name="key">逻辑键。</param>
        /// <param name="storageKey">找到的底层键。</param>
        /// <param name="legacy">是否来自旧版存储。</param>
        /// <returns>是否找到可读数据。</returns>
        private bool TryResolveKey(string key, out string storageKey, out bool legacy)
        {
            storageKey = ToStorageKey(key);
            legacy = false;
            if (PlayerPrefs.HasKey(storageKey)) return true;
            if (PlayerPrefs.HasKey(string.Concat(_prefix, "B|", Encode(key)))) return false;

            string oldKey = string.Concat(_name, key);
            var oldRegistry = ReadLegacyRegistry();
            if (!oldRegistry.Contains(oldKey) || !PlayerPrefs.HasKey(oldKey)) return false;
            storageKey = oldKey;
            legacy = true;
            return true;
        }

        /// <summary>
        /// 标记旧键不再允许回退读取。
        /// </summary>
        /// <param name="key">逻辑键。</param>
        private void BlockLegacyKey(string key)
        {
            PlayerPrefs.SetInt(string.Concat(_prefix, "B|", Encode(key)), 1);
        }

        /// <summary>
        /// 读取旧版存储键注册表。
        /// </summary>
        /// <returns>旧版底层键列表。</returns>
        private List<string> ReadLegacyRegistry()
        {
            if (!PlayerPrefs.HasKey(_legacyRegistryKey)) return new List<string>(0);
            string raw = PlayerPrefs.GetString(_legacyRegistryKey);
            return string.IsNullOrEmpty(raw) ? new List<string>(0) : new List<string>(raw.Split('\n'));
        }
        private List<string> ReadRegistry()
        {
            if (!PlayerPrefs.HasKey(_registryKey)) return new List<string>(4);

            var raw = PlayerPrefs.GetString(_registryKey);
            if (string.IsNullOrEmpty(raw)) return new List<string>(4);

            var parts = raw.Split('\n');
            return new List<string>(parts);
        }

        private void WriteRegistry(List<string> keys)
        {
            if (keys.Count == 0)
            {
                PlayerPrefs.DeleteKey(_registryKey);
                return;
            }

            PlayerPrefs.SetString(_registryKey, string.Join("\n", keys));
        }

        private void TrackKey(string storageKey)
        {
            var keys = ReadRegistry();
            for (int i = 0; i < keys.Count; i++)
            {
                if (keys[i] == storageKey) return;
            }

            keys.Add(storageKey);
            WriteRegistry(keys);
        }

        private void UntrackKey(string storageKey)
        {
            var keys = ReadRegistry();
            for (int i = keys.Count - 1; i >= 0; i--)
            {
                if (keys[i] != storageKey) continue;

                keys.RemoveAt(i);
                WriteRegistry(keys);
                return;
            }
        }
    }
}
