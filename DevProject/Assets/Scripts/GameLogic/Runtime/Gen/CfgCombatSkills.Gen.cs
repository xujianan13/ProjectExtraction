using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;
using UnityEngine;

namespace GameLogic.Tables
{
    /// <summary>
    /// CfgCombatSkills 配置行。
    /// </summary>
    [Serializable]
    public partial class CfgCombatSkillsRow
    {
        /// <summary>
        /// ID 字段。
        /// </summary>
        public int ID;

        /// <summary>
        /// DurationTicks 字段。
        /// </summary>
        public int DurationTicks;

        /// <summary>
        /// CooldownTicks 字段。
        /// </summary>
        public int CooldownTicks;

        /// <summary>
        /// StartRange 字段。
        /// </summary>
        public double StartRange;

        /// <summary>
        /// StartRangeSource 字段。
        /// </summary>
        public int StartRangeSource;
    }

    /// <summary>
    /// CfgCombatSkills 配置表。
    /// </summary>
    public partial class CfgCombatSkillsTable
    {
        private readonly Dictionary<int, CfgCombatSkillsRow> byId = new Dictionary<int, CfgCombatSkillsRow>();
        private CfgCombatSkillsRow[] rows = Array.Empty<CfgCombatSkillsRow>();

        /// <summary>
        /// 取得全部配置行。
        /// </summary>
        /// <returns>配置行的只读列表。</returns>
        public IReadOnlyList<CfgCombatSkillsRow> GetAll() => rows;

        /// <summary>
        /// 按 ID 查找配置行。
        /// </summary>
        /// <param name="id">配置行 ID。</param>
        /// <returns>找到的配置行；不存在时为 null。</returns>
        public CfgCombatSkillsRow GetById(int id) => byId.TryGetValue(id, out CfgCombatSkillsRow row) ? row : null;

        /// <summary>
        /// 加载并解析配置表。
        /// </summary>
        /// <param name="resources">资源模块。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>已经解析的配置表。</returns>
        internal static async UniTask<CfgCombatSkillsTable> LoadAsync(IResourceModule resources, CancellationToken ct)
        {
            using (ResourceLease<TextAsset> lease = await resources.AcquireAsync<TextAsset>("Table_CfgCombatSkills", ct: ct))
            {
                TextAsset asset = lease.Asset;
                if (asset == null) throw new InvalidOperationException("配置表资源不可用：CfgCombatSkills");
                var envelope = JsonUtility.FromJson(asset.text, typeof(Envelope)) as Envelope;
                if (envelope == null || envelope.schemaHash != "6ce1e0c000af0279944f2952b23b04b7876bf0fdb117be6cb77ecd010b442260" || envelope.rows == null)
                    throw new InvalidOperationException("配置表结构不匹配：CfgCombatSkills");

                var table = new CfgCombatSkillsTable();
                table.rows = envelope.rows;
                foreach (CfgCombatSkillsRow row in table.rows)
                {
                    if (row == null || !table.byId.TryAdd(row.ID, row))
                        throw new InvalidOperationException("配置表 ID 无效或重复：CfgCombatSkills");
                }
                return table;
            }
        }

        [Serializable]
        private sealed class Envelope
        {
            public string schemaHash;
            public CfgCombatSkillsRow[] rows;
        }
    }
}
