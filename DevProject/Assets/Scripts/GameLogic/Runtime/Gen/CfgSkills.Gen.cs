using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;
using UnityEngine;

namespace GameLogic.Tables
{
    /// <summary>
    /// CfgSkills 配置行。
    /// </summary>
    [Serializable]
    public partial class CfgSkillsRow
    {
        /// <summary>
        /// ID 字段。
        /// </summary>
        public string ID;

        /// <summary>
        /// Name 字段。
        /// </summary>
        public string Name;

        /// <summary>
        /// Description 字段。
        /// </summary>
        public string Description;

        /// <summary>
        /// MaxRank 字段。
        /// </summary>
        public int MaxRank;

        /// <summary>
        /// CostBase 字段。
        /// </summary>
        public int CostBase;

        /// <summary>
        /// CostPerRank 字段。
        /// </summary>
        public int CostPerRank;

        /// <summary>
        /// EffectPerRank 字段。
        /// </summary>
        public double EffectPerRank;

        /// <summary>
        /// DisplayMultiplier 字段。
        /// </summary>
        public double DisplayMultiplier;

        /// <summary>
        /// DisplayPrefix 字段。
        /// </summary>
        public string DisplayPrefix;

        /// <summary>
        /// DisplaySuffix 字段。
        /// </summary>
        public string DisplaySuffix;
    }

    /// <summary>
    /// CfgSkills 配置表。
    /// </summary>
    public partial class CfgSkillsTable
    {
        private readonly Dictionary<string, CfgSkillsRow> byId = new Dictionary<string, CfgSkillsRow>();
        private CfgSkillsRow[] rows = Array.Empty<CfgSkillsRow>();

        /// <summary>
        /// 取得全部配置行。
        /// </summary>
        /// <returns>配置行的只读列表。</returns>
        public IReadOnlyList<CfgSkillsRow> GetAll() => rows;

        /// <summary>
        /// 按 ID 查找配置行。
        /// </summary>
        /// <param name="id">配置行 ID。</param>
        /// <returns>找到的配置行；不存在时为 null。</returns>
        public CfgSkillsRow GetById(string id) => byId.TryGetValue(id, out CfgSkillsRow row) ? row : null;

        /// <summary>
        /// 加载并解析配置表。
        /// </summary>
        /// <param name="resources">资源模块。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>已经解析的配置表。</returns>
        internal static async UniTask<CfgSkillsTable> LoadAsync(IResourceModule resources, CancellationToken ct)
        {
            using (ResourceLease<TextAsset> lease = await resources.AcquireAsync<TextAsset>("Table_CfgSkills", ct: ct))
            {
                TextAsset asset = lease.Asset;
                if (asset == null) throw new InvalidOperationException("配置表资源不可用：CfgSkills");
                var envelope = JsonUtility.FromJson(asset.text, typeof(Envelope)) as Envelope;
                if (envelope == null || envelope.schemaHash != "09aa56f902ed566af9da4661b03e691f435fde10a10e78a6d9af16c1c18cfe91" || envelope.rows == null)
                    throw new InvalidOperationException("配置表结构不匹配：CfgSkills");

                var table = new CfgSkillsTable();
                table.rows = envelope.rows;
                foreach (CfgSkillsRow row in table.rows)
                {
                    if (row == null || !table.byId.TryAdd(row.ID, row))
                        throw new InvalidOperationException("配置表 ID 无效或重复：CfgSkills");
                }
                return table;
            }
        }

        [Serializable]
        private sealed class Envelope
        {
            public string schemaHash;
            public CfgSkillsRow[] rows;
        }
    }
}
