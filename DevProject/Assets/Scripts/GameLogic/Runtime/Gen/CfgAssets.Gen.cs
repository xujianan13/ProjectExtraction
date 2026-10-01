using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;
using UnityEngine;

namespace GameLogic.Tables
{
    /// <summary>
    /// CfgAssets 配置行。
    /// </summary>
    [Serializable]
    public partial class CfgAssetsRow
    {
        /// <summary>
        /// ID 字段。
        /// </summary>
        public string ID;

        /// <summary>
        /// Address 字段。
        /// </summary>
        public string Address;
    }

    /// <summary>
    /// CfgAssets 配置表。
    /// </summary>
    public partial class CfgAssetsTable
    {
        private readonly Dictionary<string, CfgAssetsRow> byId = new Dictionary<string, CfgAssetsRow>();
        private CfgAssetsRow[] rows = Array.Empty<CfgAssetsRow>();

        /// <summary>
        /// 取得全部配置行。
        /// </summary>
        /// <returns>配置行的只读列表。</returns>
        public IReadOnlyList<CfgAssetsRow> GetAll() => rows;

        /// <summary>
        /// 按 ID 查找配置行。
        /// </summary>
        /// <param name="id">配置行 ID。</param>
        /// <returns>找到的配置行；不存在时为 null。</returns>
        public CfgAssetsRow GetById(string id) => byId.TryGetValue(id, out CfgAssetsRow row) ? row : null;

        /// <summary>
        /// 加载并解析配置表。
        /// </summary>
        /// <param name="resources">资源模块。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>已经解析的配置表。</returns>
        internal static async UniTask<CfgAssetsTable> LoadAsync(IResourceModule resources, CancellationToken ct)
        {
            using (ResourceLease<TextAsset> lease = await resources.AcquireAsync<TextAsset>("Table_CfgAssets", ct: ct))
            {
                TextAsset asset = lease.Asset;
                if (asset == null) throw new InvalidOperationException("配置表资源不可用：CfgAssets");
                var envelope = JsonUtility.FromJson(asset.text, typeof(Envelope)) as Envelope;
                if (envelope == null || envelope.schemaHash != "1028b31c68ae3a866c8a1920fa514c4e8943aec3c5e94864dbda5b1baad9b63d" || envelope.rows == null)
                    throw new InvalidOperationException("配置表结构不匹配：CfgAssets");

                var table = new CfgAssetsTable();
                table.rows = envelope.rows;
                foreach (CfgAssetsRow row in table.rows)
                {
                    if (row == null || !table.byId.TryAdd(row.ID, row))
                        throw new InvalidOperationException("配置表 ID 无效或重复：CfgAssets");
                }
                return table;
            }
        }

        [Serializable]
        private sealed class Envelope
        {
            public string schemaHash;
            public CfgAssetsRow[] rows;
        }
    }
}
