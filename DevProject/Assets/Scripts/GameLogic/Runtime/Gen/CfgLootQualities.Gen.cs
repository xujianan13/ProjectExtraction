using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;
using UnityEngine;

namespace GameLogic.Tables
{
    /// <summary>
    /// CfgLootQualities 配置行。
    /// </summary>
    [Serializable]
    public partial class CfgLootQualitiesRow
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
        /// Quality 字段。
        /// </summary>
        public int Quality;

        /// <summary>
        /// Weight 字段。
        /// </summary>
        public int Weight;
    }

    /// <summary>
    /// CfgLootQualities 配置表。
    /// </summary>
    public partial class CfgLootQualitiesTable
    {
        private readonly Dictionary<string, CfgLootQualitiesRow> byId = new Dictionary<string, CfgLootQualitiesRow>();
        private CfgLootQualitiesRow[] rows = Array.Empty<CfgLootQualitiesRow>();

        /// <summary>
        /// 取得全部配置行。
        /// </summary>
        /// <returns>配置行的只读列表。</returns>
        public IReadOnlyList<CfgLootQualitiesRow> GetAll() => rows;

        /// <summary>
        /// 按 ID 查找配置行。
        /// </summary>
        /// <param name="id">配置行 ID。</param>
        /// <returns>找到的配置行；不存在时为 null。</returns>
        public CfgLootQualitiesRow GetById(string id) => byId.TryGetValue(id, out CfgLootQualitiesRow row) ? row : null;

        /// <summary>
        /// 加载并解析配置表。
        /// </summary>
        /// <param name="resources">资源模块。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>已经解析的配置表。</returns>
        internal static async UniTask<CfgLootQualitiesTable> LoadAsync(IResourceModule resources, CancellationToken ct)
        {
            using (ResourceLease<TextAsset> lease = await resources.AcquireAsync<TextAsset>("Table_CfgLootQualities", ct: ct))
            {
                TextAsset asset = lease.Asset;
                if (asset == null) throw new InvalidOperationException("配置表资源不可用：CfgLootQualities");
                var envelope = JsonUtility.FromJson(asset.text, typeof(Envelope)) as Envelope;
                if (envelope == null || envelope.schemaHash != "322a1896691e4aa3411bbdf0c26057eccf541419bf9a57c73a54394bc8184105" || envelope.rows == null)
                    throw new InvalidOperationException("配置表结构不匹配：CfgLootQualities");

                var table = new CfgLootQualitiesTable();
                table.rows = envelope.rows;
                foreach (CfgLootQualitiesRow row in table.rows)
                {
                    if (row == null || !table.byId.TryAdd(row.ID, row))
                        throw new InvalidOperationException("配置表 ID 无效或重复：CfgLootQualities");
                }
                return table;
            }
        }

        [Serializable]
        private sealed class Envelope
        {
            public string schemaHash;
            public CfgLootQualitiesRow[] rows;
        }
    }
}
