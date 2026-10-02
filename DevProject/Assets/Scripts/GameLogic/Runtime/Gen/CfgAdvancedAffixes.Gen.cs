using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;
using UnityEngine;

namespace GameLogic.Tables
{
    /// <summary>
    /// CfgAdvancedAffixes 配置行。
    /// </summary>
    [Serializable]
    public partial class CfgAdvancedAffixesRow
    {
        /// <summary>
        /// ID 字段。
        /// </summary>
        public string ID;

        /// <summary>
        /// AugmentID 字段。
        /// </summary>
        public string AugmentID;

        /// <summary>
        /// ItemType 字段。
        /// </summary>
        public int ItemType;

        /// <summary>
        /// MinQuality 字段。
        /// </summary>
        public int MinQuality;

        /// <summary>
        /// Weight 字段。
        /// </summary>
        public int Weight;

        /// <summary>
        /// ValueMin 字段。
        /// </summary>
        public double ValueMin;

        /// <summary>
        /// ValueMax 字段。
        /// </summary>
        public double ValueMax;
    }

    /// <summary>
    /// CfgAdvancedAffixes 配置表。
    /// </summary>
    public partial class CfgAdvancedAffixesTable
    {
        private readonly Dictionary<string, CfgAdvancedAffixesRow> byId = new Dictionary<string, CfgAdvancedAffixesRow>();
        private CfgAdvancedAffixesRow[] rows = Array.Empty<CfgAdvancedAffixesRow>();

        /// <summary>
        /// 取得全部配置行。
        /// </summary>
        /// <returns>配置行的只读列表。</returns>
        public IReadOnlyList<CfgAdvancedAffixesRow> GetAll() => rows;

        /// <summary>
        /// 按 ID 查找配置行。
        /// </summary>
        /// <param name="id">配置行 ID。</param>
        /// <returns>找到的配置行；不存在时为 null。</returns>
        public CfgAdvancedAffixesRow GetById(string id) => byId.TryGetValue(id, out CfgAdvancedAffixesRow row) ? row : null;

        /// <summary>
        /// 加载并解析配置表。
        /// </summary>
        /// <param name="resources">资源模块。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>已经解析的配置表。</returns>
        internal static async UniTask<CfgAdvancedAffixesTable> LoadAsync(IResourceModule resources, CancellationToken ct)
        {
            using (ResourceLease<TextAsset> lease = await resources.AcquireAsync<TextAsset>("Table_CfgAdvancedAffixes", ct: ct))
            {
                TextAsset asset = lease.Asset;
                if (asset == null) throw new InvalidOperationException("配置表资源不可用：CfgAdvancedAffixes");
                var envelope = JsonUtility.FromJson(asset.text, typeof(Envelope)) as Envelope;
                if (envelope == null || envelope.schemaHash != "ace373a4aa1133f77c2e7200289e5aa980dbd5390db0b16535ff5b968e70fe67" || envelope.rows == null)
                    throw new InvalidOperationException("配置表结构不匹配：CfgAdvancedAffixes");

                var table = new CfgAdvancedAffixesTable();
                table.rows = envelope.rows;
                foreach (CfgAdvancedAffixesRow row in table.rows)
                {
                    if (row == null || !table.byId.TryAdd(row.ID, row))
                        throw new InvalidOperationException("配置表 ID 无效或重复：CfgAdvancedAffixes");
                }
                return table;
            }
        }

        [Serializable]
        private sealed class Envelope
        {
            public string schemaHash;
            public CfgAdvancedAffixesRow[] rows;
        }
    }
}
