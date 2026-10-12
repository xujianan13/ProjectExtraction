using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;
using UnityEngine;

namespace GameLogic.Tables
{
    /// <summary>
    /// CfgCollections 配置行。
    /// </summary>
    [Serializable]
    public partial class CfgCollectionsRow
    {
        /// <summary>
        /// ID 字段。
        /// </summary>
        public string ID;

        /// <summary>
        /// Name 字段。
        /// </summary>
        public string Name;

        /// <summary>
        /// Series 字段。
        /// </summary>
        public string Series;

        /// <summary>
        /// Source 字段。
        /// </summary>
        public string Source;

        /// <summary>
        /// SellGold 字段。
        /// </summary>
        public int SellGold;

        /// <summary>
        /// RequiredLevel 字段。
        /// </summary>
        public int RequiredLevel;

        /// <summary>
        /// Quality 字段。
        /// </summary>
        public int Quality;
    }

    /// <summary>
    /// CfgCollections 配置表。
    /// </summary>
    public partial class CfgCollectionsTable
    {
        private readonly Dictionary<string, CfgCollectionsRow> byId = new Dictionary<string, CfgCollectionsRow>();
        private CfgCollectionsRow[] rows = Array.Empty<CfgCollectionsRow>();

        /// <summary>
        /// 取得全部配置行。
        /// </summary>
        /// <returns>配置行的只读列表。</returns>
        public IReadOnlyList<CfgCollectionsRow> GetAll() => rows;

        /// <summary>
        /// 按 ID 查找配置行。
        /// </summary>
        /// <param name="id">配置行 ID。</param>
        /// <returns>找到的配置行；不存在时为 null。</returns>
        public CfgCollectionsRow GetById(string id) => byId.TryGetValue(id, out CfgCollectionsRow row) ? row : null;

        /// <summary>
        /// 加载并解析配置表。
        /// </summary>
        /// <param name="resources">资源模块。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>已经解析的配置表。</returns>
        internal static async UniTask<CfgCollectionsTable> LoadAsync(IResourceModule resources, CancellationToken ct)
        {
            using (ResourceLease<TextAsset> lease = await resources.AcquireAsync<TextAsset>("Table_CfgCollections", ct: ct))
            {
                TextAsset asset = lease.Asset;
                if (asset == null) throw new InvalidOperationException("配置表资源不可用：CfgCollections");
                var envelope = JsonUtility.FromJson(asset.text, typeof(Envelope)) as Envelope;
                if (envelope == null || envelope.schemaHash != "5e3f437d4e41b5a292e2648ffe3f736462caf4384f0283b72c9adb83db7413ed" || envelope.rows == null)
                    throw new InvalidOperationException("配置表结构不匹配：CfgCollections");

                var table = new CfgCollectionsTable();
                table.rows = envelope.rows;
                foreach (CfgCollectionsRow row in table.rows)
                {
                    if (row == null || !table.byId.TryAdd(row.ID, row))
                        throw new InvalidOperationException("配置表 ID 无效或重复：CfgCollections");
                }
                return table;
            }
        }

        [Serializable]
        private sealed class Envelope
        {
            public string schemaHash;
            public CfgCollectionsRow[] rows;
        }
    }
}
