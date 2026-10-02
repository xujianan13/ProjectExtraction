using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;
using UnityEngine;

namespace GameLogic.Tables
{
    /// <summary>
    /// CfgQualities 配置行。
    /// </summary>
    [Serializable]
    public partial class CfgQualitiesRow
    {
        /// <summary>
        /// ID 字段。
        /// </summary>
        public int ID;

        /// <summary>
        /// Name 字段。
        /// </summary>
        public string Name;

        /// <summary>
        /// ColorR 字段。
        /// </summary>
        public double ColorR;

        /// <summary>
        /// ColorG 字段。
        /// </summary>
        public double ColorG;

        /// <summary>
        /// ColorB 字段。
        /// </summary>
        public double ColorB;

        /// <summary>
        /// Weight 字段。
        /// </summary>
        public int Weight;

        /// <summary>
        /// AffixCount 字段。
        /// </summary>
        public int AffixCount;

        /// <summary>
        /// ScaleMin 字段。
        /// </summary>
        public double ScaleMin;

        /// <summary>
        /// ScaleMax 字段。
        /// </summary>
        public double ScaleMax;

        /// <summary>
        /// AdvancedAffixCount 字段。
        /// </summary>
        public int AdvancedAffixCount;
    }

    /// <summary>
    /// CfgQualities 配置表。
    /// </summary>
    public partial class CfgQualitiesTable
    {
        private readonly Dictionary<int, CfgQualitiesRow> byId = new Dictionary<int, CfgQualitiesRow>();
        private CfgQualitiesRow[] rows = Array.Empty<CfgQualitiesRow>();

        /// <summary>
        /// 取得全部配置行。
        /// </summary>
        /// <returns>配置行的只读列表。</returns>
        public IReadOnlyList<CfgQualitiesRow> GetAll() => rows;

        /// <summary>
        /// 按 ID 查找配置行。
        /// </summary>
        /// <param name="id">配置行 ID。</param>
        /// <returns>找到的配置行；不存在时为 null。</returns>
        public CfgQualitiesRow GetById(int id) => byId.TryGetValue(id, out CfgQualitiesRow row) ? row : null;

        /// <summary>
        /// 加载并解析配置表。
        /// </summary>
        /// <param name="resources">资源模块。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>已经解析的配置表。</returns>
        internal static async UniTask<CfgQualitiesTable> LoadAsync(IResourceModule resources, CancellationToken ct)
        {
            using (ResourceLease<TextAsset> lease = await resources.AcquireAsync<TextAsset>("Table_CfgQualities", ct: ct))
            {
                TextAsset asset = lease.Asset;
                if (asset == null) throw new InvalidOperationException("配置表资源不可用：CfgQualities");
                var envelope = JsonUtility.FromJson(asset.text, typeof(Envelope)) as Envelope;
                if (envelope == null || envelope.schemaHash != "d559963efed495fd550c39a78c59fadcbc317e139e1509b3ccec0cc44184e192" || envelope.rows == null)
                    throw new InvalidOperationException("配置表结构不匹配：CfgQualities");

                var table = new CfgQualitiesTable();
                table.rows = envelope.rows;
                foreach (CfgQualitiesRow row in table.rows)
                {
                    if (row == null || !table.byId.TryAdd(row.ID, row))
                        throw new InvalidOperationException("配置表 ID 无效或重复：CfgQualities");
                }
                return table;
            }
        }

        [Serializable]
        private sealed class Envelope
        {
            public string schemaHash;
            public CfgQualitiesRow[] rows;
        }
    }
}
