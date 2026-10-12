using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;
using UnityEngine;

namespace GameLogic.Tables
{
    /// <summary>
    /// CfgChestPool 配置行。
    /// </summary>
    [Serializable]
    public partial class CfgChestPoolRow
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
        /// ItemCount 字段。
        /// </summary>
        public int ItemCount;

        /// <summary>
        /// Weight 字段。
        /// </summary>
        public int Weight;
    }

    /// <summary>
    /// CfgChestPool 配置表。
    /// </summary>
    public partial class CfgChestPoolTable
    {
        private readonly Dictionary<int, CfgChestPoolRow> byId = new Dictionary<int, CfgChestPoolRow>();
        private CfgChestPoolRow[] rows = Array.Empty<CfgChestPoolRow>();

        /// <summary>
        /// 取得全部配置行。
        /// </summary>
        /// <returns>配置行的只读列表。</returns>
        public IReadOnlyList<CfgChestPoolRow> GetAll() => rows;

        /// <summary>
        /// 按 ID 查找配置行。
        /// </summary>
        /// <param name="id">配置行 ID。</param>
        /// <returns>找到的配置行；不存在时为 null。</returns>
        public CfgChestPoolRow GetById(int id) => byId.TryGetValue(id, out CfgChestPoolRow row) ? row : null;

        /// <summary>
        /// 加载并解析配置表。
        /// </summary>
        /// <param name="resources">资源模块。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>已经解析的配置表。</returns>
        internal static async UniTask<CfgChestPoolTable> LoadAsync(IResourceModule resources, CancellationToken ct)
        {
            using (ResourceLease<TextAsset> lease = await resources.AcquireAsync<TextAsset>("Table_CfgChestPool", ct: ct))
            {
                TextAsset asset = lease.Asset;
                if (asset == null) throw new InvalidOperationException("配置表资源不可用：CfgChestPool");
                var envelope = JsonUtility.FromJson(asset.text, typeof(Envelope)) as Envelope;
                if (envelope == null || envelope.schemaHash != "8ff74a4ebac4daa3bfecff2f4dc27267cfba280ebb90bba77d25164b1d215b39" || envelope.rows == null)
                    throw new InvalidOperationException("配置表结构不匹配：CfgChestPool");

                var table = new CfgChestPoolTable();
                table.rows = envelope.rows;
                foreach (CfgChestPoolRow row in table.rows)
                {
                    if (row == null || !table.byId.TryAdd(row.ID, row))
                        throw new InvalidOperationException("配置表 ID 无效或重复：CfgChestPool");
                }
                return table;
            }
        }

        [Serializable]
        private sealed class Envelope
        {
            public string schemaHash;
            public CfgChestPoolRow[] rows;
        }
    }
}
