using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;
using UnityEngine;

namespace GameLogic.Tables
{
    /// <summary>
    /// CfgEnemySkills 配置行。
    /// </summary>
    [Serializable]
    public partial class CfgEnemySkillsRow
    {
        /// <summary>
        /// ID 字段。
        /// </summary>
        public int ID;

        /// <summary>
        /// EnemyID 字段。
        /// </summary>
        public int EnemyID;

        /// <summary>
        /// SkillID 字段。
        /// </summary>
        public int SkillID;

        /// <summary>
        /// Priority 字段。
        /// </summary>
        public int Priority;

        /// <summary>
        /// Weight 字段。
        /// </summary>
        public int Weight;

        /// <summary>
        /// MinHealthRatio 字段。
        /// </summary>
        public double MinHealthRatio;

        /// <summary>
        /// MaxHealthRatio 字段。
        /// </summary>
        public double MaxHealthRatio;

        /// <summary>
        /// MinDistance 字段。
        /// </summary>
        public double MinDistance;

        /// <summary>
        /// MaxDistance 字段。
        /// </summary>
        public double MaxDistance;
    }

    /// <summary>
    /// CfgEnemySkills 配置表。
    /// </summary>
    public partial class CfgEnemySkillsTable
    {
        private readonly Dictionary<int, CfgEnemySkillsRow> byId = new Dictionary<int, CfgEnemySkillsRow>();
        private CfgEnemySkillsRow[] rows = Array.Empty<CfgEnemySkillsRow>();

        /// <summary>
        /// 取得全部配置行。
        /// </summary>
        /// <returns>配置行的只读列表。</returns>
        public IReadOnlyList<CfgEnemySkillsRow> GetAll() => rows;

        /// <summary>
        /// 按 ID 查找配置行。
        /// </summary>
        /// <param name="id">配置行 ID。</param>
        /// <returns>找到的配置行；不存在时为 null。</returns>
        public CfgEnemySkillsRow GetById(int id) => byId.TryGetValue(id, out CfgEnemySkillsRow row) ? row : null;

        /// <summary>
        /// 加载并解析配置表。
        /// </summary>
        /// <param name="resources">资源模块。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>已经解析的配置表。</returns>
        internal static async UniTask<CfgEnemySkillsTable> LoadAsync(IResourceModule resources, CancellationToken ct)
        {
            using (ResourceLease<TextAsset> lease = await resources.AcquireAsync<TextAsset>("Table_CfgEnemySkills", ct: ct))
            {
                TextAsset asset = lease.Asset;
                if (asset == null) throw new InvalidOperationException("配置表资源不可用：CfgEnemySkills");
                var envelope = JsonUtility.FromJson(asset.text, typeof(Envelope)) as Envelope;
                if (envelope == null || envelope.schemaHash != "cfef212105b4f0730c084c0905d2a9641c636190bebc5dbf38a356092913adab" || envelope.rows == null)
                    throw new InvalidOperationException("配置表结构不匹配：CfgEnemySkills");

                var table = new CfgEnemySkillsTable();
                table.rows = envelope.rows;
                foreach (CfgEnemySkillsRow row in table.rows)
                {
                    if (row == null || !table.byId.TryAdd(row.ID, row))
                        throw new InvalidOperationException("配置表 ID 无效或重复：CfgEnemySkills");
                }
                return table;
            }
        }

        [Serializable]
        private sealed class Envelope
        {
            public string schemaHash;
            public CfgEnemySkillsRow[] rows;
        }
    }
}
