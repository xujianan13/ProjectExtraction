using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;
using UnityEngine;

namespace GameLogic.Tables
{
    /// <summary>
    /// CfgObstacles 配置行。
    /// </summary>
    [Serializable]
    public partial class CfgObstaclesRow
    {
        /// <summary>
        /// ID 字段。
        /// </summary>
        public int ID;

        /// <summary>
        /// LayoutID 字段。
        /// </summary>
        public int LayoutID;

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
    /// CfgObstacles 配置表。
    /// </summary>
    public partial class CfgObstaclesTable
    {
        private readonly Dictionary<int, CfgObstaclesRow> byId = new Dictionary<int, CfgObstaclesRow>();
        private CfgObstaclesRow[] rows = Array.Empty<CfgObstaclesRow>();

        /// <summary>
        /// 取得全部配置行。
        /// </summary>
        /// <returns>配置行的只读列表。</returns>
        public IReadOnlyList<CfgObstaclesRow> GetAll() => rows;

        /// <summary>
        /// 按 ID 查找配置行。
        /// </summary>
        /// <param name="id">配置行 ID。</param>
        /// <returns>找到的配置行；不存在时为 null。</returns>
        public CfgObstaclesRow GetById(int id) => byId.TryGetValue(id, out CfgObstaclesRow row) ? row : null;

        /// <summary>
        /// 加载并解析配置表。
        /// </summary>
        /// <param name="resources">资源模块。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>已经解析的配置表。</returns>
        internal static async UniTask<CfgObstaclesTable> LoadAsync(IResourceModule resources, CancellationToken ct)
        {
            using (ResourceLease<TextAsset> lease = await resources.AcquireAsync<TextAsset>("Table_CfgObstacles", ct: ct))
            {
                TextAsset asset = lease.Asset;
                if (asset == null) throw new InvalidOperationException("配置表资源不可用：CfgObstacles");
                var envelope = JsonUtility.FromJson(asset.text, typeof(Envelope)) as Envelope;
                if (envelope == null || envelope.schemaHash != "4d81e2d5e9c4fa60241b2a2d1f5f002ec871999d99ace1c0a032be56485dd719" || envelope.rows == null)
                    throw new InvalidOperationException("配置表结构不匹配：CfgObstacles");

                var table = new CfgObstaclesTable();
                table.rows = envelope.rows;
                foreach (CfgObstaclesRow row in table.rows)
                {
                    if (row == null || !table.byId.TryAdd(row.ID, row))
                        throw new InvalidOperationException("配置表 ID 无效或重复：CfgObstacles");
                }
                return table;
            }
        }

        [Serializable]
        private sealed class Envelope
        {
            public string schemaHash;
            public CfgObstaclesRow[] rows;
        }
    }
}
