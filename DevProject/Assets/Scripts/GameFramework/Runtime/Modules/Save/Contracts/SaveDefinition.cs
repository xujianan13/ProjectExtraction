using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace GameFramework
{
    /// <summary>
    /// 游戏数据的 JSON 编解码器。
    /// </summary>
    /// <typeparam name="T">存档数据类型。</typeparam>
    public interface ISaveCodec<T>
    {
        /// <summary>
        /// 编码独立数据快照。
        /// </summary>
        /// <param name="data">待编码数据。</param>
        /// <returns>JSON 文本。</returns>
        string Encode(T data);

        /// <summary>
        /// 解码 JSON 文本。
        /// </summary>
        /// <param name="json">JSON 文本。</param>
        /// <returns>解码后的数据。</returns>
        T Decode(string json);
    }

    /// <summary>
    /// 使用 Unity JsonUtility 编解码普通数据对象。
    /// </summary>
    /// <typeparam name="T">存档数据类型。</typeparam>
    public sealed class JsonUtilitySaveCodec<T> : ISaveCodec<T>
    {
        /// <summary>
        /// 编码数据。
        /// </summary>
        /// <param name="data">存档数据。</param>
        /// <returns>JSON 文本。</returns>
        public string Encode(T data) => JsonUtility.ToJson(data);

        /// <summary>
        /// 解码数据。
        /// </summary>
        /// <param name="json">JSON 文本。</param>
        /// <returns>存档数据。</returns>
        public T Decode(string json) => (T)JsonUtility.FromJson(json, typeof(T));
    }

    /// <summary>
    /// 一个纯 JSON 数据迁移步骤。
    /// </summary>
    public sealed class SaveMigration
    {
        public int FromVersion { get; }
        public Func<string, string> ValidateSource { get; }
        public Func<string, string> Migrate { get; }

        /// <summary>
        /// 声明一个版本迁移。
        /// </summary>
        /// <param name="fromVersion">来源 schema 版本。</param>
        /// <param name="validateSource">来源 JSON 校验，空结果代表通过。</param>
        /// <param name="migrate">将来源 JSON 转为下一版本 JSON 的纯数据函数。</param>
        public SaveMigration(int fromVersion, Func<string, string> validateSource, Func<string, string> migrate)
        {
            FromVersion = fromVersion;
            ValidateSource = validateSource;
            Migrate = migrate;
        }
    }

    /// <summary>
    /// 游戏存档默认值、当前校验与迁移链定义。
    /// </summary>
    /// <typeparam name="T">存档数据类型。</typeparam>
    public sealed class SaveDefinition<T>
    {
        public int SchemaVersion { get; }
        public Func<T> CreateDefault { get; }
        public Func<T, string> Validate { get; }
        public ISaveCodec<T> Codec { get; }
        public IReadOnlyDictionary<int, SaveMigration> Migrations { get; }
        public bool IsValid { get; }

        /// <summary>
        /// 创建游戏数据定义。
        /// </summary>
        /// <param name="schemaVersion">当前 schema 版本。</param>
        /// <param name="createDefault">新档默认数据工厂。</param>
        /// <param name="validate">当前数据校验，空结果代表通过。</param>
        /// <param name="codec">可选 JSON 编解码器。</param>
        /// <param name="migrations">逐版迁移步骤。</param>
        public SaveDefinition(int schemaVersion, Func<T> createDefault, Func<T, string> validate, ISaveCodec<T> codec = null,
            IEnumerable<SaveMigration> migrations = null)
        {
            SchemaVersion = schemaVersion;
            CreateDefault = createDefault;
            Validate = validate;
            Codec = codec ?? new JsonUtilitySaveCodec<T>();
            var map = new Dictionary<int, SaveMigration>();
            var valid = schemaVersion > 0 && createDefault != null && validate != null;
            if (migrations != null)
                foreach (var step in migrations)
                {
                    if (step == null || step.FromVersion <= 0 || step.FromVersion >= schemaVersion ||
                        step.ValidateSource == null || step.Migrate == null || map.ContainsKey(step.FromVersion)) valid = false;
                    else map.Add(step.FromVersion, step);
                }
            Migrations = new ReadOnlyDictionary<int, SaveMigration>(map);
            IsValid = valid;
        }
    }
}
