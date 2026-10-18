using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;
using UnityEngine;

namespace GameLogic.Tables
{
    /// <summary>
    /// CfgRoomObstacles 配置行。
    /// </summary>
    [Serializable]
    public partial class CfgRoomObstaclesRow
    {
        /// <summary>
        /// ID 字段。
        /// </summary>
        public int ID;

        /// <summary>
        /// MapID 字段。
        /// </summary>
        public int MapID;

        /// <summary>
        /// MinX 字段。
        /// </summary>
        public double MinX;

        /// <summary>
        /// MinZ 字段。
        /// </summary>
        public double MinZ;

        /// <summary>
        /// MaxX 字段。
        /// </summary>
        public double MaxX;

        /// <summary>
        /// MaxZ 字段。
        /// </summary>
        public double MaxZ;
    }

    /// <summary>
    /// CfgRoomObstacles 配置表。
    /// </summary>
    public partial class CfgRoomObstaclesTable
    {
        private readonly Dictionary<int, CfgRoomObstaclesRow> byId = new Dictionary<int, CfgRoomObstaclesRow>();
        private CfgRoomObstaclesRow[] rows = Array.Empty<CfgRoomObstaclesRow>();

        /// <summary>
        /// 取得全部配置行。
        /// </summary>
        /// <returns>配置行的只读列表。</returns>
        public IReadOnlyList<CfgRoomObstaclesRow> GetAll() => rows;

        /// <summary>
        /// 按 ID 查找配置行。
        /// </summary>
        /// <param name="id">配置行 ID。</param>
        /// <returns>找到的配置行；不存在时为 null。</returns>
        public CfgRoomObstaclesRow GetById(int id) => byId.TryGetValue(id, out CfgRoomObstaclesRow row) ? row : null;

        /// <summary>
        /// 加载并解析配置表。
        /// </summary>
        /// <param name="resources">资源模块。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>已经解析的配置表。</returns>
        internal static async UniTask<CfgRoomObstaclesTable> LoadAsync(IResourceModule resources, CancellationToken ct)
        {
            using (ResourceLease<TextAsset> lease = await resources.AcquireAsync<TextAsset>("Table_CfgRoomObstacles", ct: ct))
            {
                TextAsset asset = lease.Asset;
                if (asset == null) throw new InvalidOperationException("配置表资源不可用：CfgRoomObstacles");
                var envelope = JsonUtility.FromJson(asset.text, typeof(Envelope)) as Envelope;
                if (envelope == null || envelope.schemaHash != "c4d1dff2299683afccebd729081775f5fc2a77c307d79aa3f97955de9185f4e1" || envelope.rows == null)
                    throw new InvalidOperationException("配置表结构不匹配：CfgRoomObstacles");

                var table = new CfgRoomObstaclesTable();
                table.rows = envelope.rows;
                foreach (CfgRoomObstaclesRow row in table.rows)
                {
                    if (row == null || !table.byId.TryAdd(row.ID, row))
                        throw new InvalidOperationException("配置表 ID 无效或重复：CfgRoomObstacles");
                }
                return table;
            }
        }

        [Serializable]
        private sealed class Envelope
        {
            public string schemaHash;
            public CfgRoomObstaclesRow[] rows;
        }
    }
}
