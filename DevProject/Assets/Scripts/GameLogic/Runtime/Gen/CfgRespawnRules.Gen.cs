using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;
using UnityEngine;

namespace GameLogic.Tables
{
    /// <summary>
    /// CfgRespawnRules 配置行。
    /// </summary>
    [Serializable]
    public partial class CfgRespawnRulesRow
    {
        /// <summary>
        /// ID 字段。
        /// </summary>
        public int ID;

        /// <summary>
        /// DelaySeconds 字段。
        /// </summary>
        public double DelaySeconds;

        /// <summary>
        /// WarningSeconds 字段。
        /// </summary>
        public double WarningSeconds;

        /// <summary>
        /// SafeDistance 字段。
        /// </summary>
        public double SafeDistance;

        /// <summary>
        /// ExperienceMultiplier 字段。
        /// </summary>
        public double ExperienceMultiplier;
    }

    /// <summary>
    /// CfgRespawnRules 配置表。
    /// </summary>
    public partial class CfgRespawnRulesTable
    {
        private readonly Dictionary<int, CfgRespawnRulesRow> byId = new Dictionary<int, CfgRespawnRulesRow>();
        private CfgRespawnRulesRow[] rows = Array.Empty<CfgRespawnRulesRow>();

        /// <summary>
        /// 取得全部配置行。
        /// </summary>
        /// <returns>配置行的只读列表。</returns>
        public IReadOnlyList<CfgRespawnRulesRow> GetAll() => rows;

        /// <summary>
        /// 按 ID 查找配置行。
        /// </summary>
        /// <param name="id">配置行 ID。</param>
        /// <returns>找到的配置行；不存在时为 null。</returns>
        public CfgRespawnRulesRow GetById(int id) => byId.TryGetValue(id, out CfgRespawnRulesRow row) ? row : null;

        /// <summary>
        /// 加载并解析配置表。
        /// </summary>
        /// <param name="resources">资源模块。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>已经解析的配置表。</returns>
        internal static async UniTask<CfgRespawnRulesTable> LoadAsync(IResourceModule resources, CancellationToken ct)
        {
            using (ResourceLease<TextAsset> lease = await resources.AcquireAsync<TextAsset>("Table_CfgRespawnRules", ct: ct))
            {
                TextAsset asset = lease.Asset;
                if (asset == null) throw new InvalidOperationException("配置表资源不可用：CfgRespawnRules");
                var envelope = JsonUtility.FromJson(asset.text, typeof(Envelope)) as Envelope;
                if (envelope == null || envelope.schemaHash != "224b1c7fb7356a011b39c57f06428a4b495667eca65e3b7230d68a40fe834553" || envelope.rows == null)
                    throw new InvalidOperationException("配置表结构不匹配：CfgRespawnRules");

                var table = new CfgRespawnRulesTable();
                table.rows = envelope.rows;
                foreach (CfgRespawnRulesRow row in table.rows)
                {
                    if (row == null || !table.byId.TryAdd(row.ID, row))
                        throw new InvalidOperationException("配置表 ID 无效或重复：CfgRespawnRules");
                }
                return table;
            }
        }

        [Serializable]
        private sealed class Envelope
        {
            public string schemaHash;
            public CfgRespawnRulesRow[] rows;
        }
    }
}
