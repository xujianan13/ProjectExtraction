using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;
using UnityEngine;

namespace GameLogic.Tables
{
    /// <summary>
    /// CfgCombatTimelineNodes 配置行。
    /// </summary>
    [Serializable]
    public partial class CfgCombatTimelineNodesRow
    {
        /// <summary>
        /// ID 字段。
        /// </summary>
        public int ID;

        /// <summary>
        /// SkillID 字段。
        /// </summary>
        public int SkillID;

        /// <summary>
        /// Tick 字段。
        /// </summary>
        public int Tick;

        /// <summary>
        /// EffectIDs 字段。
        /// </summary>
        public int[] EffectIDs;
    }

    /// <summary>
    /// CfgCombatTimelineNodes 配置表。
    /// </summary>
    public partial class CfgCombatTimelineNodesTable
    {
        private readonly Dictionary<int, CfgCombatTimelineNodesRow> byId = new Dictionary<int, CfgCombatTimelineNodesRow>();
        private CfgCombatTimelineNodesRow[] rows = Array.Empty<CfgCombatTimelineNodesRow>();

        /// <summary>
        /// 取得全部配置行。
        /// </summary>
        /// <returns>配置行的只读列表。</returns>
        public IReadOnlyList<CfgCombatTimelineNodesRow> GetAll() => rows;

        /// <summary>
        /// 按 ID 查找配置行。
        /// </summary>
        /// <param name="id">配置行 ID。</param>
        /// <returns>找到的配置行；不存在时为 null。</returns>
        public CfgCombatTimelineNodesRow GetById(int id) => byId.TryGetValue(id, out CfgCombatTimelineNodesRow row) ? row : null;

        /// <summary>
        /// 加载并解析配置表。
        /// </summary>
        /// <param name="resources">资源模块。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>已经解析的配置表。</returns>
        internal static async UniTask<CfgCombatTimelineNodesTable> LoadAsync(IResourceModule resources, CancellationToken ct)
        {
            using (ResourceLease<TextAsset> lease = await resources.AcquireAsync<TextAsset>("Table_CfgCombatTimelineNodes", ct: ct))
            {
                TextAsset asset = lease.Asset;
                if (asset == null) throw new InvalidOperationException("配置表资源不可用：CfgCombatTimelineNodes");
                var envelope = JsonUtility.FromJson(asset.text, typeof(Envelope)) as Envelope;
                if (envelope == null || envelope.schemaHash != "5c5f446a6d388583e7a4ff1bc7a68badfbf000538ae7cb359af8ac83c4465d42" || envelope.rows == null)
                    throw new InvalidOperationException("配置表结构不匹配：CfgCombatTimelineNodes");

                var table = new CfgCombatTimelineNodesTable();
                table.rows = envelope.rows;
                foreach (CfgCombatTimelineNodesRow row in table.rows)
                {
                    if (row == null || !table.byId.TryAdd(row.ID, row))
                        throw new InvalidOperationException("配置表 ID 无效或重复：CfgCombatTimelineNodes");
                }
                return table;
            }
        }

        [Serializable]
        private sealed class Envelope
        {
            public string schemaHash;
            public CfgCombatTimelineNodesRow[] rows;
        }
    }
}
