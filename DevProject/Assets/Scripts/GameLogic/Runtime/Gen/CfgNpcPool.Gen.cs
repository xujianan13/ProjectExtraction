using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;
using UnityEngine;

namespace GameLogic.Tables
{
    /// <summary>
    /// CfgNpcPool 配置行。
    /// </summary>
    [Serializable]
    public partial class CfgNpcPoolRow
    {
        /// <summary>
        /// ID 字段。
        /// </summary>
        public int ID;

        /// <summary>
        /// PoolID 字段。
        /// </summary>
        public string PoolID;

        /// <summary>
        /// LootPoolID 字段。
        /// </summary>
        public string LootPoolID;

        /// <summary>
        /// Price 字段。
        /// </summary>
        public int Price;

        /// <summary>
        /// Stock 字段。
        /// </summary>
        public int Stock;

        /// <summary>
        /// Weight 字段。
        /// </summary>
        public int Weight;
    }

    /// <summary>
    /// CfgNpcPool 配置表。
    /// </summary>
    public partial class CfgNpcPoolTable
    {
        private readonly Dictionary<int, CfgNpcPoolRow> byId = new Dictionary<int, CfgNpcPoolRow>();
        private CfgNpcPoolRow[] rows = Array.Empty<CfgNpcPoolRow>();

        /// <summary>
        /// 取得全部配置行。
        /// </summary>
        /// <returns>配置行的只读列表。</returns>
        public IReadOnlyList<CfgNpcPoolRow> GetAll() => rows;

        /// <summary>
        /// 按 ID 查找配置行。
        /// </summary>
        /// <param name="id">配置行 ID。</param>
        /// <returns>找到的配置行；不存在时为 null。</returns>
        public CfgNpcPoolRow GetById(int id) => byId.TryGetValue(id, out CfgNpcPoolRow row) ? row : null;

        /// <summary>
        /// 加载并解析配置表。
        /// </summary>
        /// <param name="resources">资源模块。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>已经解析的配置表。</returns>
        internal static async UniTask<CfgNpcPoolTable> LoadAsync(IResourceModule resources, CancellationToken ct)
        {
            using (ResourceLease<TextAsset> lease = await resources.AcquireAsync<TextAsset>("Table_CfgNpcPool", ct: ct))
            {
                TextAsset asset = lease.Asset;
                if (asset == null) throw new InvalidOperationException("配置表资源不可用：CfgNpcPool");
                var envelope = JsonUtility.FromJson(asset.text, typeof(Envelope)) as Envelope;
                if (envelope == null || envelope.schemaHash != "37f3acebe773db1ff6af1ce7bc91011e9fbe65d60140dee639f9437ddba520c3" || envelope.rows == null)
                    throw new InvalidOperationException("配置表结构不匹配：CfgNpcPool");

                var table = new CfgNpcPoolTable();
                table.rows = envelope.rows;
                foreach (CfgNpcPoolRow row in table.rows)
                {
                    if (row == null || !table.byId.TryAdd(row.ID, row))
                        throw new InvalidOperationException("配置表 ID 无效或重复：CfgNpcPool");
                }
                return table;
            }
        }

        [Serializable]
        private sealed class Envelope
        {
            public string schemaHash;
            public CfgNpcPoolRow[] rows;
        }
    }
}
