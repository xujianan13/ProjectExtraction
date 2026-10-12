using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;
using UnityEngine;

namespace GameLogic.Tables
{
    /// <summary>
    /// CfgNavigation 配置行。
    /// </summary>
    [Serializable]
    public partial class CfgNavigationRow
    {
        /// <summary>
        /// ID 字段。
        /// </summary>
        public int ID;

        /// <summary>
        /// CornerClearance 字段。
        /// </summary>
        public double CornerClearance;

        /// <summary>
        /// ArrivalDistance 字段。
        /// </summary>
        public double ArrivalDistance;

        /// <summary>
        /// TargetApproachDistance 字段。
        /// </summary>
        public double TargetApproachDistance;

        /// <summary>
        /// StuckSeconds 字段。
        /// </summary>
        public double StuckSeconds;

        /// <summary>
        /// ProgressDistance 字段。
        /// </summary>
        public double ProgressDistance;
    }

    /// <summary>
    /// CfgNavigation 配置表。
    /// </summary>
    public partial class CfgNavigationTable
    {
        private readonly Dictionary<int, CfgNavigationRow> byId = new Dictionary<int, CfgNavigationRow>();
        private CfgNavigationRow[] rows = Array.Empty<CfgNavigationRow>();

        /// <summary>
        /// 取得全部配置行。
        /// </summary>
        /// <returns>配置行的只读列表。</returns>
        public IReadOnlyList<CfgNavigationRow> GetAll() => rows;

        /// <summary>
        /// 按 ID 查找配置行。
        /// </summary>
        /// <param name="id">配置行 ID。</param>
        /// <returns>找到的配置行；不存在时为 null。</returns>
        public CfgNavigationRow GetById(int id) => byId.TryGetValue(id, out CfgNavigationRow row) ? row : null;

        /// <summary>
        /// 加载并解析配置表。
        /// </summary>
        /// <param name="resources">资源模块。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>已经解析的配置表。</returns>
        internal static async UniTask<CfgNavigationTable> LoadAsync(IResourceModule resources, CancellationToken ct)
        {
            using (ResourceLease<TextAsset> lease = await resources.AcquireAsync<TextAsset>("Table_CfgNavigation", ct: ct))
            {
                TextAsset asset = lease.Asset;
                if (asset == null) throw new InvalidOperationException("配置表资源不可用：CfgNavigation");
                var envelope = JsonUtility.FromJson(asset.text, typeof(Envelope)) as Envelope;
                if (envelope == null || envelope.schemaHash != "eb4827fb35c84968064891089bde752cd916d9b40bbb7f28a28763c8f73ed5cc" || envelope.rows == null)
                    throw new InvalidOperationException("配置表结构不匹配：CfgNavigation");

                var table = new CfgNavigationTable();
                table.rows = envelope.rows;
                foreach (CfgNavigationRow row in table.rows)
                {
                    if (row == null || !table.byId.TryAdd(row.ID, row))
                        throw new InvalidOperationException("配置表 ID 无效或重复：CfgNavigation");
                }
                return table;
            }
        }

        [Serializable]
        private sealed class Envelope
        {
            public string schemaHash;
            public CfgNavigationRow[] rows;
        }
    }
}
