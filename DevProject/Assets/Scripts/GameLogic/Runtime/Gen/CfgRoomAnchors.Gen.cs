using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;
using UnityEngine;

namespace GameLogic.Tables
{
    /// <summary>
    /// CfgRoomAnchors 配置行。
    /// </summary>
    [Serializable]
    public partial class CfgRoomAnchorsRow
    {
        /// <summary>
        /// ID 字段。
        /// </summary>
        public string ID;

        /// <summary>
        /// MapID 字段。
        /// </summary>
        public int MapID;

        /// <summary>
        /// AnchorID 字段。
        /// </summary>
        public string AnchorID;

        /// <summary>
        /// Kind 字段。
        /// </summary>
        public int Kind;

        /// <summary>
        /// Tag 字段。
        /// </summary>
        public string Tag;

        /// <summary>
        /// X 字段。
        /// </summary>
        public double X;

        /// <summary>
        /// Z 字段。
        /// </summary>
        public double Z;

        /// <summary>
        /// Capacity 字段。
        /// </summary>
        public int Capacity;

        /// <summary>
        /// NoSpawnRadius 字段。
        /// </summary>
        public double NoSpawnRadius;
    }

    /// <summary>
    /// CfgRoomAnchors 配置表。
    /// </summary>
    public partial class CfgRoomAnchorsTable
    {
        private readonly Dictionary<string, CfgRoomAnchorsRow> byId = new Dictionary<string, CfgRoomAnchorsRow>();
        private CfgRoomAnchorsRow[] rows = Array.Empty<CfgRoomAnchorsRow>();

        /// <summary>
        /// 取得全部配置行。
        /// </summary>
        /// <returns>配置行的只读列表。</returns>
        public IReadOnlyList<CfgRoomAnchorsRow> GetAll() => rows;

        /// <summary>
        /// 按 ID 查找配置行。
        /// </summary>
        /// <param name="id">配置行 ID。</param>
        /// <returns>找到的配置行；不存在时为 null。</returns>
        public CfgRoomAnchorsRow GetById(string id) => byId.TryGetValue(id, out CfgRoomAnchorsRow row) ? row : null;

        /// <summary>
        /// 加载并解析配置表。
        /// </summary>
        /// <param name="resources">资源模块。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>已经解析的配置表。</returns>
        internal static async UniTask<CfgRoomAnchorsTable> LoadAsync(IResourceModule resources, CancellationToken ct)
        {
            using (ResourceLease<TextAsset> lease = await resources.AcquireAsync<TextAsset>("Table_CfgRoomAnchors", ct: ct))
            {
                TextAsset asset = lease.Asset;
                if (asset == null) throw new InvalidOperationException("配置表资源不可用：CfgRoomAnchors");
                var envelope = JsonUtility.FromJson(asset.text, typeof(Envelope)) as Envelope;
                if (envelope == null || envelope.schemaHash != "d79aaa074934d1a5bc737889ce81083ee1bc35ca227c5a68df241932f35d0a17" || envelope.rows == null)
                    throw new InvalidOperationException("配置表结构不匹配：CfgRoomAnchors");

                var table = new CfgRoomAnchorsTable();
                table.rows = envelope.rows;
                foreach (CfgRoomAnchorsRow row in table.rows)
                {
                    if (row == null || !table.byId.TryAdd(row.ID, row))
                        throw new InvalidOperationException("配置表 ID 无效或重复：CfgRoomAnchors");
                }
                return table;
            }
        }

        [Serializable]
        private sealed class Envelope
        {
            public string schemaHash;
            public CfgRoomAnchorsRow[] rows;
        }
    }
}
