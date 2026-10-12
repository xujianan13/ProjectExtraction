using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;
using UnityEngine;

namespace GameLogic.Tables
{
    /// <summary>
    /// CfgElitePool 配置行。
    /// </summary>
    [Serializable]
    public partial class CfgElitePoolRow
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
        /// EnemyID 字段。
        /// </summary>
        public int EnemyID;

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
    /// CfgElitePool 配置表。
    /// </summary>
    public partial class CfgElitePoolTable
    {
        private readonly Dictionary<int, CfgElitePoolRow> byId = new Dictionary<int, CfgElitePoolRow>();
        private CfgElitePoolRow[] rows = Array.Empty<CfgElitePoolRow>();

        /// <summary>
        /// 取得全部配置行。
        /// </summary>
        /// <returns>配置行的只读列表。</returns>
        public IReadOnlyList<CfgElitePoolRow> GetAll() => rows;

        /// <summary>
        /// 按 ID 查找配置行。
        /// </summary>
        /// <param name="id">配置行 ID。</param>
        /// <returns>找到的配置行；不存在时为 null。</returns>
        public CfgElitePoolRow GetById(int id) => byId.TryGetValue(id, out CfgElitePoolRow row) ? row : null;

        /// <summary>
        /// 加载并解析配置表。
        /// </summary>
        /// <param name="resources">资源模块。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>已经解析的配置表。</returns>
        internal static async UniTask<CfgElitePoolTable> LoadAsync(IResourceModule resources, CancellationToken ct)
        {
            using (ResourceLease<TextAsset> lease = await resources.AcquireAsync<TextAsset>("Table_CfgElitePool", ct: ct))
            {
                TextAsset asset = lease.Asset;
                if (asset == null) throw new InvalidOperationException("配置表资源不可用：CfgElitePool");
                var envelope = JsonUtility.FromJson(asset.text, typeof(Envelope)) as Envelope;
                if (envelope == null || envelope.schemaHash != "3dd3e799ce2c2bebd08e37d6358abe9917a4987facf5ede0eda17f6548e150ac" || envelope.rows == null)
                    throw new InvalidOperationException("配置表结构不匹配：CfgElitePool");

                var table = new CfgElitePoolTable();
                table.rows = envelope.rows;
                foreach (CfgElitePoolRow row in table.rows)
                {
                    if (row == null || !table.byId.TryAdd(row.ID, row))
                        throw new InvalidOperationException("配置表 ID 无效或重复：CfgElitePool");
                }
                return table;
            }
        }

        [Serializable]
        private sealed class Envelope
        {
            public string schemaHash;
            public CfgElitePoolRow[] rows;
        }
    }
}
