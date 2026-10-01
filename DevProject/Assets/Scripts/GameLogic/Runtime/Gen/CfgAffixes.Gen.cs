using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;
using UnityEngine;

namespace GameLogic.Tables
{
    /// <summary>
    /// CfgAffixes 配置行。
    /// </summary>
    [Serializable]
    public partial class CfgAffixesRow
    {
        /// <summary>
        /// ID 字段。
        /// </summary>
        public string ID;

        /// <summary>
        /// Attribute 字段。
        /// </summary>
        public int Attribute;

        /// <summary>
        /// Scale 字段。
        /// </summary>
        public double Scale;
    }

    /// <summary>
    /// CfgAffixes 配置表。
    /// </summary>
    public partial class CfgAffixesTable
    {
        private readonly Dictionary<string, CfgAffixesRow> byId = new Dictionary<string, CfgAffixesRow>();
        private CfgAffixesRow[] rows = Array.Empty<CfgAffixesRow>();

        /// <summary>
        /// 取得全部配置行。
        /// </summary>
        /// <returns>配置行的只读列表。</returns>
        public IReadOnlyList<CfgAffixesRow> GetAll() => rows;

        /// <summary>
        /// 按 ID 查找配置行。
        /// </summary>
        /// <param name="id">配置行 ID。</param>
        /// <returns>找到的配置行；不存在时为 null。</returns>
        public CfgAffixesRow GetById(string id) => byId.TryGetValue(id, out CfgAffixesRow row) ? row : null;

        /// <summary>
        /// 加载并解析配置表。
        /// </summary>
        /// <param name="resources">资源模块。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>已经解析的配置表。</returns>
        internal static async UniTask<CfgAffixesTable> LoadAsync(IResourceModule resources, CancellationToken ct)
        {
            using (ResourceLease<TextAsset> lease = await resources.AcquireAsync<TextAsset>("Table_CfgAffixes", ct: ct))
            {
                TextAsset asset = lease.Asset;
                if (asset == null) throw new InvalidOperationException("配置表资源不可用：CfgAffixes");
                var envelope = JsonUtility.FromJson(asset.text, typeof(Envelope)) as Envelope;
                if (envelope == null || envelope.schemaHash != "1ed0be92cbb3c1a09d035bd12fe0e4c7b2f31e76f4e3abb0789a5bfb19ae96e1" || envelope.rows == null)
                    throw new InvalidOperationException("配置表结构不匹配：CfgAffixes");

                var table = new CfgAffixesTable();
                table.rows = envelope.rows;
                foreach (CfgAffixesRow row in table.rows)
                {
                    if (row == null || !table.byId.TryAdd(row.ID, row))
                        throw new InvalidOperationException("配置表 ID 无效或重复：CfgAffixes");
                }
                return table;
            }
        }

        [Serializable]
        private sealed class Envelope
        {
            public string schemaHash;
            public CfgAffixesRow[] rows;
        }
    }
}
