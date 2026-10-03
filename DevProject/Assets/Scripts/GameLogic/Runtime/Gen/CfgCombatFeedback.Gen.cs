using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;
using UnityEngine;

namespace GameLogic.Tables
{
    /// <summary>
    /// CfgCombatFeedback 配置行。
    /// </summary>
    [Serializable]
    public partial class CfgCombatFeedbackRow
    {
        /// <summary>
        /// ID 字段。
        /// </summary>
        public string ID;

        /// <summary>
        /// EventKind 字段。
        /// </summary>
        public int EventKind;

        /// <summary>
        /// DefinitionID 字段。
        /// </summary>
        public int DefinitionID;

        /// <summary>
        /// Critical 字段。
        /// </summary>
        public bool Critical;

        /// <summary>
        /// Text 字段。
        /// </summary>
        public string Text;

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
        /// ShowHit 字段。
        /// </summary>
        public bool ShowHit;

        /// <summary>
        /// ShowFlash 字段。
        /// </summary>
        public bool ShowFlash;
    }

    /// <summary>
    /// CfgCombatFeedback 配置表。
    /// </summary>
    public partial class CfgCombatFeedbackTable
    {
        private readonly Dictionary<string, CfgCombatFeedbackRow> byId = new Dictionary<string, CfgCombatFeedbackRow>();
        private CfgCombatFeedbackRow[] rows = Array.Empty<CfgCombatFeedbackRow>();

        /// <summary>
        /// 取得全部配置行。
        /// </summary>
        /// <returns>配置行的只读列表。</returns>
        public IReadOnlyList<CfgCombatFeedbackRow> GetAll() => rows;

        /// <summary>
        /// 按 ID 查找配置行。
        /// </summary>
        /// <param name="id">配置行 ID。</param>
        /// <returns>找到的配置行；不存在时为 null。</returns>
        public CfgCombatFeedbackRow GetById(string id) => byId.TryGetValue(id, out CfgCombatFeedbackRow row) ? row : null;

        /// <summary>
        /// 加载并解析配置表。
        /// </summary>
        /// <param name="resources">资源模块。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>已经解析的配置表。</returns>
        internal static async UniTask<CfgCombatFeedbackTable> LoadAsync(IResourceModule resources, CancellationToken ct)
        {
            using (ResourceLease<TextAsset> lease = await resources.AcquireAsync<TextAsset>("Table_CfgCombatFeedback", ct: ct))
            {
                TextAsset asset = lease.Asset;
                if (asset == null) throw new InvalidOperationException("配置表资源不可用：CfgCombatFeedback");
                var envelope = JsonUtility.FromJson(asset.text, typeof(Envelope)) as Envelope;
                if (envelope == null || envelope.schemaHash != "2d77efb5caf59b00b1ecfe65dfe508f558d8fec7891676441b2368fe941596e8" || envelope.rows == null)
                    throw new InvalidOperationException("配置表结构不匹配：CfgCombatFeedback");

                var table = new CfgCombatFeedbackTable();
                table.rows = envelope.rows;
                foreach (CfgCombatFeedbackRow row in table.rows)
                {
                    if (row == null || !table.byId.TryAdd(row.ID, row))
                        throw new InvalidOperationException("配置表 ID 无效或重复：CfgCombatFeedback");
                }
                return table;
            }
        }

        [Serializable]
        private sealed class Envelope
        {
            public string schemaHash;
            public CfgCombatFeedbackRow[] rows;
        }
    }
}
