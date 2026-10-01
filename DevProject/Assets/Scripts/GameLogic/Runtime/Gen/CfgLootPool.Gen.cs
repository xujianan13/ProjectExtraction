using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;
using UnityEngine;

namespace GameLogic.Tables
{
    /// <summary>
    /// CfgLootPool 配置行。
    /// </summary>
    [Serializable]
    public partial class CfgLootPoolRow
    {
        /// <summary>
        /// ID 字段。
        /// </summary>
        public string ID;

        /// <summary>
        /// PoolID 字段。
        /// </summary>
        public string PoolID;

        /// <summary>
        /// DefinitionId 字段。
        /// </summary>
        public string DefinitionId;

        /// <summary>
        /// Weight 字段。
        /// </summary>
        public int Weight;

        /// <summary>
        /// MinLevel 字段。
        /// </summary>
        public int MinLevel;

        /// <summary>
        /// MaxLevel 字段。
        /// </summary>
        public int MaxLevel;
    }

    /// <summary>
    /// CfgLootPool 配置表。
    /// </summary>
    public partial class CfgLootPoolTable
    {
        private readonly Dictionary<string, CfgLootPoolRow> byId = new Dictionary<string, CfgLootPoolRow>();
        private CfgLootPoolRow[] rows = Array.Empty<CfgLootPoolRow>();

        /// <summary>
        /// 取得全部配置行。
        /// </summary>
        /// <returns>配置行的只读列表。</returns>
        public IReadOnlyList<CfgLootPoolRow> GetAll() => rows;

        /// <summary>
        /// 按 ID 查找配置行。
        /// </summary>
        /// <param name="id">配置行 ID。</param>
        /// <returns>找到的配置行；不存在时为 null。</returns>
        public CfgLootPoolRow GetById(string id) => byId.TryGetValue(id, out CfgLootPoolRow row) ? row : null;

        /// <summary>
        /// 加载并解析配置表。
        /// </summary>
        /// <param name="resources">资源模块。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>已经解析的配置表。</returns>
        internal static async UniTask<CfgLootPoolTable> LoadAsync(IResourceModule resources, CancellationToken ct)
        {
            using (ResourceLease<TextAsset> lease = await resources.AcquireAsync<TextAsset>("Table_CfgLootPool", ct: ct))
            {
                TextAsset asset = lease.Asset;
                if (asset == null) throw new InvalidOperationException("配置表资源不可用：CfgLootPool");
                var envelope = JsonUtility.FromJson(asset.text, typeof(Envelope)) as Envelope;
                if (envelope == null || envelope.schemaHash != "2d57e733abc81d4a4d67b9d82e991dbd7de53a1e5b5ef2d9d59db700f4bdcfb5" || envelope.rows == null)
                    throw new InvalidOperationException("配置表结构不匹配：CfgLootPool");

                var table = new CfgLootPoolTable();
                table.rows = envelope.rows;
                foreach (CfgLootPoolRow row in table.rows)
                {
                    if (row == null || !table.byId.TryAdd(row.ID, row))
                        throw new InvalidOperationException("配置表 ID 无效或重复：CfgLootPool");
                }
                return table;
            }
        }

        [Serializable]
        private sealed class Envelope
        {
            public string schemaHash;
            public CfgLootPoolRow[] rows;
        }
    }
}
