using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;
using UnityEngine;

namespace GameLogic.Tables
{
    /// <summary>
    /// CfgRegionMaps 配置行。
    /// </summary>
    [Serializable]
    public partial class CfgRegionMapsRow
    {
        /// <summary>
        /// ID 字段。
        /// </summary>
        public int ID;

        /// <summary>
        /// Version 字段。
        /// </summary>
        public string Version;

        /// <summary>
        /// PrefabAddress 字段。
        /// </summary>
        public string PrefabAddress;

        /// <summary>
        /// GeometryAddress 字段。
        /// </summary>
        public string GeometryAddress;

        /// <summary>
        /// MinimapAddress 字段。
        /// </summary>
        public string MinimapAddress;

        /// <summary>
        /// Name 字段。
        /// </summary>
        public string Name;
    }

    /// <summary>
    /// CfgRegionMaps 配置表。
    /// </summary>
    public partial class CfgRegionMapsTable
    {
        private readonly Dictionary<int, CfgRegionMapsRow> byId = new Dictionary<int, CfgRegionMapsRow>();
        private CfgRegionMapsRow[] rows = Array.Empty<CfgRegionMapsRow>();

        /// <summary>
        /// 取得全部配置行。
        /// </summary>
        /// <returns>配置行的只读列表。</returns>
        public IReadOnlyList<CfgRegionMapsRow> GetAll() => rows;

        /// <summary>
        /// 按 ID 查找配置行。
        /// </summary>
        /// <param name="id">配置行 ID。</param>
        /// <returns>找到的配置行；不存在时为 null。</returns>
        public CfgRegionMapsRow GetById(int id) => byId.TryGetValue(id, out CfgRegionMapsRow row) ? row : null;

        /// <summary>
        /// 加载并解析配置表。
        /// </summary>
        /// <param name="resources">资源模块。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>已经解析的配置表。</returns>
        internal static async UniTask<CfgRegionMapsTable> LoadAsync(IResourceModule resources, CancellationToken ct)
        {
            using (ResourceLease<TextAsset> lease = await resources.AcquireAsync<TextAsset>("Table_CfgRegionMaps", ct: ct))
            {
                TextAsset asset = lease.Asset;
                if (asset == null) throw new InvalidOperationException("配置表资源不可用：CfgRegionMaps");
                var envelope = JsonUtility.FromJson(asset.text, typeof(Envelope)) as Envelope;
                if (envelope == null || envelope.schemaHash != "86cc8a5c97aefe79c8db133c8778a7042c26c8250b1b14d6808b21bda34907d6" || envelope.rows == null)
                    throw new InvalidOperationException("配置表结构不匹配：CfgRegionMaps");

                var table = new CfgRegionMapsTable();
                table.rows = envelope.rows;
                foreach (CfgRegionMapsRow row in table.rows)
                {
                    if (row == null || !table.byId.TryAdd(row.ID, row))
                        throw new InvalidOperationException("配置表 ID 无效或重复：CfgRegionMaps");
                }
                return table;
            }
        }

        [Serializable]
        private sealed class Envelope
        {
            public string schemaHash;
            public CfgRegionMapsRow[] rows;
        }
    }
}
