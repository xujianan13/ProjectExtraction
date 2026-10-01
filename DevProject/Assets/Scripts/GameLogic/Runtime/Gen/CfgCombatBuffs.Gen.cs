using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;
using UnityEngine;

namespace GameLogic.Tables
{
    /// <summary>
    /// CfgCombatBuffs 配置行。
    /// </summary>
    [Serializable]
    public partial class CfgCombatBuffsRow
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
        /// IntervalTicks 字段。
        /// </summary>
        public int IntervalTicks;

        /// <summary>
        /// MaxStacks 字段。
        /// </summary>
        public int MaxStacks;

        /// <summary>
        /// Merge 字段。
        /// </summary>
        public int Merge;

        /// <summary>
        /// MoveSpeedAddPerStack 字段。
        /// </summary>
        public double MoveSpeedAddPerStack;

        /// <summary>
        /// AttackRangeAddPerStack 字段。
        /// </summary>
        public double AttackRangeAddPerStack;

        /// <summary>
        /// PeriodicDamagePerStack 字段。
        /// </summary>
        public double PeriodicDamagePerStack;

        /// <summary>
        /// OnApplyEffectIDs 字段。
        /// </summary>
        public int[] OnApplyEffectIDs;

        /// <summary>
        /// OnTickEffectIDs 字段。
        /// </summary>
        public int[] OnTickEffectIDs;

        /// <summary>
        /// OnExpireEffectIDs 字段。
        /// </summary>
        public int[] OnExpireEffectIDs;
    }

    /// <summary>
    /// CfgCombatBuffs 配置表。
    /// </summary>
    public partial class CfgCombatBuffsTable
    {
        private readonly Dictionary<int, CfgCombatBuffsRow> byId = new Dictionary<int, CfgCombatBuffsRow>();
        private CfgCombatBuffsRow[] rows = Array.Empty<CfgCombatBuffsRow>();

        /// <summary>
        /// 取得全部配置行。
        /// </summary>
        /// <returns>配置行的只读列表。</returns>
        public IReadOnlyList<CfgCombatBuffsRow> GetAll() => rows;

        /// <summary>
        /// 按 ID 查找配置行。
        /// </summary>
        /// <param name="id">配置行 ID。</param>
        /// <returns>找到的配置行；不存在时为 null。</returns>
        public CfgCombatBuffsRow GetById(int id) => byId.TryGetValue(id, out CfgCombatBuffsRow row) ? row : null;

        /// <summary>
        /// 加载并解析配置表。
        /// </summary>
        /// <param name="resources">资源模块。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>已经解析的配置表。</returns>
        internal static async UniTask<CfgCombatBuffsTable> LoadAsync(IResourceModule resources, CancellationToken ct)
        {
            using (ResourceLease<TextAsset> lease = await resources.AcquireAsync<TextAsset>("Table_CfgCombatBuffs", ct: ct))
            {
                TextAsset asset = lease.Asset;
                if (asset == null) throw new InvalidOperationException("配置表资源不可用：CfgCombatBuffs");
                var envelope = JsonUtility.FromJson(asset.text, typeof(Envelope)) as Envelope;
                if (envelope == null || envelope.schemaHash != "48877cc242c2c8f852e554f4682e12eb576de15ee58d5d2f0bef3da7c166dc97" || envelope.rows == null)
                    throw new InvalidOperationException("配置表结构不匹配：CfgCombatBuffs");

                var table = new CfgCombatBuffsTable();
                table.rows = envelope.rows;
                foreach (CfgCombatBuffsRow row in table.rows)
                {
                    if (row == null || !table.byId.TryAdd(row.ID, row))
                        throw new InvalidOperationException("配置表 ID 无效或重复：CfgCombatBuffs");
                }
                return table;
            }
        }

        [Serializable]
        private sealed class Envelope
        {
            public string schemaHash;
            public CfgCombatBuffsRow[] rows;
        }
    }
}
