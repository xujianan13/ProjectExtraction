using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;
using UnityEngine;

namespace GameLogic.Tables
{
    /// <summary>
    /// CfgRoomMaps 配置行。
    /// </summary>
    [Serializable]
    public partial class CfgRoomMapsRow
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
        /// Name 字段。
        /// </summary>
        public string Name;

        /// <summary>
        /// BoundsX 字段。
        /// </summary>
        public double BoundsX;

        /// <summary>
        /// BoundsZ 字段。
        /// </summary>
        public double BoundsZ;

        /// <summary>
        /// Width 字段。
        /// </summary>
        public double Width;

        /// <summary>
        /// Length 字段。
        /// </summary>
        public double Length;

        /// <summary>
        /// NorthYawDegrees 字段。
        /// </summary>
        public double NorthYawDegrees;
    }

    /// <summary>
    /// CfgRoomMaps 配置表。
    /// </summary>
    public partial class CfgRoomMapsTable
    {
        private readonly Dictionary<int, CfgRoomMapsRow> byId = new Dictionary<int, CfgRoomMapsRow>();
        private CfgRoomMapsRow[] rows = Array.Empty<CfgRoomMapsRow>();

        /// <summary>
        /// 取得全部配置行。
        /// </summary>
        /// <returns>配置行的只读列表。</returns>
        public IReadOnlyList<CfgRoomMapsRow> GetAll() => rows;

        /// <summary>
        /// 按 ID 查找配置行。
        /// </summary>
        /// <param name="id">配置行 ID。</param>
        /// <returns>找到的配置行；不存在时为 null。</returns>
        public CfgRoomMapsRow GetById(int id) => byId.TryGetValue(id, out CfgRoomMapsRow row) ? row : null;

        /// <summary>
        /// 加载并解析配置表。
        /// </summary>
        /// <param name="resources">资源模块。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>已经解析的配置表。</returns>
        internal static async UniTask<CfgRoomMapsTable> LoadAsync(IResourceModule resources, CancellationToken ct)
        {
            using (ResourceLease<TextAsset> lease = await resources.AcquireAsync<TextAsset>("Table_CfgRoomMaps", ct: ct))
            {
                TextAsset asset = lease.Asset;
                if (asset == null) throw new InvalidOperationException("配置表资源不可用：CfgRoomMaps");
                var envelope = JsonUtility.FromJson(asset.text, typeof(Envelope)) as Envelope;
                if (envelope == null || envelope.schemaHash != "f71b009dc825b18c9e6ed1d7375c9c8f9713b462d52ae7721a567c070b97f170" || envelope.rows == null)
                    throw new InvalidOperationException("配置表结构不匹配：CfgRoomMaps");

                var table = new CfgRoomMapsTable();
                table.rows = envelope.rows;
                foreach (CfgRoomMapsRow row in table.rows)
                {
                    if (row == null || !table.byId.TryAdd(row.ID, row))
                        throw new InvalidOperationException("配置表 ID 无效或重复：CfgRoomMaps");
                }
                return table;
            }
        }

        [Serializable]
        private sealed class Envelope
        {
            public string schemaHash;
            public CfgRoomMapsRow[] rows;
        }
    }
}
