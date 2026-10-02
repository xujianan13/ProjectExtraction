using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;
using UnityEngine;

namespace GameLogic.Tables
{
    /// <summary>
    /// CfgEncounters 配置行。
    /// </summary>
    [Serializable]
    public partial class CfgEncountersRow
    {
        /// <summary>
        /// ID 字段。
        /// </summary>
        public int ID;

        /// <summary>
        /// PoolID 字段。
        /// </summary>
        public string PoolID;

        /// <summary>
        /// LayoutID 字段。
        /// </summary>
        public int LayoutID;

        /// <summary>
        /// MinLevel 字段。
        /// </summary>
        public int MinLevel;

        /// <summary>
        /// MaxLevel 字段。
        /// </summary>
        public int MaxLevel;

        /// <summary>
        /// Weight 字段。
        /// </summary>
        public int Weight;
    }

    /// <summary>
    /// CfgEncounters 配置表。
    /// </summary>
    public partial class CfgEncountersTable
    {
        private readonly Dictionary<int, CfgEncountersRow> byId = new Dictionary<int, CfgEncountersRow>();
        private CfgEncountersRow[] rows = Array.Empty<CfgEncountersRow>();

        /// <summary>
        /// 取得全部配置行。
        /// </summary>
        /// <returns>配置行的只读列表。</returns>
        public IReadOnlyList<CfgEncountersRow> GetAll() => rows;

        /// <summary>
        /// 按 ID 查找配置行。
        /// </summary>
        /// <param name="id">配置行 ID。</param>
        /// <returns>找到的配置行；不存在时为 null。</returns>
        public CfgEncountersRow GetById(int id) => byId.TryGetValue(id, out CfgEncountersRow row) ? row : null;

        /// <summary>
        /// 加载并解析配置表。
        /// </summary>
        /// <param name="resources">资源模块。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>已经解析的配置表。</returns>
        internal static async UniTask<CfgEncountersTable> LoadAsync(IResourceModule resources, CancellationToken ct)
        {
            using (ResourceLease<TextAsset> lease = await resources.AcquireAsync<TextAsset>("Table_CfgEncounters", ct: ct))
            {
                TextAsset asset = lease.Asset;
                if (asset == null) throw new InvalidOperationException("配置表资源不可用：CfgEncounters");
                var envelope = JsonUtility.FromJson(asset.text, typeof(Envelope)) as Envelope;
                if (envelope == null || envelope.schemaHash != "0020e4965f2d667947fea34ef2dd3ca0c4f30930dda762579bdcb56a12faa785" || envelope.rows == null)
                    throw new InvalidOperationException("配置表结构不匹配：CfgEncounters");

                var table = new CfgEncountersTable();
                table.rows = envelope.rows;
                foreach (CfgEncountersRow row in table.rows)
                {
                    if (row == null || !table.byId.TryAdd(row.ID, row))
                        throw new InvalidOperationException("配置表 ID 无效或重复：CfgEncounters");
                }
                return table;
            }
        }

        [Serializable]
        private sealed class Envelope
        {
            public string schemaHash;
            public CfgEncountersRow[] rows;
        }
    }
}
