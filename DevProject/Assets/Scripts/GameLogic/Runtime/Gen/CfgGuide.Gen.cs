using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;
using UnityEngine;

namespace GameLogic.Tables
{
    /// <summary>
    /// CfgGuide 配置行。
    /// </summary>
    [Serializable]
    public partial class CfgGuideRow
    {
        /// <summary>
        /// ID 字段。
        /// </summary>
        public string ID;

        /// <summary>
        /// SortOrder 字段。
        /// </summary>
        public int SortOrder;

        /// <summary>
        /// Title 字段。
        /// </summary>
        public string Title;

        /// <summary>
        /// Body 字段。
        /// </summary>
        public string Body;
    }

    /// <summary>
    /// CfgGuide 配置表。
    /// </summary>
    public partial class CfgGuideTable
    {
        private readonly Dictionary<string, CfgGuideRow> byId = new Dictionary<string, CfgGuideRow>();
        private CfgGuideRow[] rows = Array.Empty<CfgGuideRow>();

        /// <summary>
        /// 取得全部配置行。
        /// </summary>
        /// <returns>配置行的只读列表。</returns>
        public IReadOnlyList<CfgGuideRow> GetAll() => rows;

        /// <summary>
        /// 按 ID 查找配置行。
        /// </summary>
        /// <param name="id">配置行 ID。</param>
        /// <returns>找到的配置行；不存在时为 null。</returns>
        public CfgGuideRow GetById(string id) => byId.TryGetValue(id, out CfgGuideRow row) ? row : null;

        /// <summary>
        /// 加载并解析配置表。
        /// </summary>
        /// <param name="resources">资源模块。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>已经解析的配置表。</returns>
        internal static async UniTask<CfgGuideTable> LoadAsync(IResourceModule resources, CancellationToken ct)
        {
            using (ResourceLease<TextAsset> lease = await resources.AcquireAsync<TextAsset>("Table_CfgGuide", ct: ct))
            {
                TextAsset asset = lease.Asset;
                if (asset == null) throw new InvalidOperationException("配置表资源不可用：CfgGuide");
                var envelope = JsonUtility.FromJson(asset.text, typeof(Envelope)) as Envelope;
                if (envelope == null || envelope.schemaHash != "0dd6a929b559d674e3a128bfd4e2ca76980976208a86ddf0da0048107a7a80e0" || envelope.rows == null)
                    throw new InvalidOperationException("配置表结构不匹配：CfgGuide");

                var table = new CfgGuideTable();
                table.rows = envelope.rows;
                foreach (CfgGuideRow row in table.rows)
                {
                    if (row == null || !table.byId.TryAdd(row.ID, row))
                        throw new InvalidOperationException("配置表 ID 无效或重复：CfgGuide");
                }
                return table;
            }
        }

        [Serializable]
        private sealed class Envelope
        {
            public string schemaHash;
            public CfgGuideRow[] rows;
        }
    }
}
