using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;
using UnityEngine;

namespace GameLogic.Tables
{
    /// <summary>
    /// CfgCombatAnimationSettings 配置行。
    /// </summary>
    [Serializable]
    public partial class CfgCombatAnimationSettingsRow
    {
        /// <summary>
        /// ID 字段。
        /// </summary>
        public int ID;

        /// <summary>
        /// IdleState 字段。
        /// </summary>
        public string IdleState;

        /// <summary>
        /// MoveState 字段。
        /// </summary>
        public string MoveState;

        /// <summary>
        /// HitState 字段。
        /// </summary>
        public string HitState;

        /// <summary>
        /// DeathState 字段。
        /// </summary>
        public string DeathState;

        /// <summary>
        /// IdleSeconds 字段。
        /// </summary>
        public double IdleSeconds;

        /// <summary>
        /// MoveSeconds 字段。
        /// </summary>
        public double MoveSeconds;

        /// <summary>
        /// HitTicks 字段。
        /// </summary>
        public int HitTicks;

        /// <summary>
        /// DeathTicks 字段。
        /// </summary>
        public int DeathTicks;

        /// <summary>
        /// DeathHoldSeconds 字段。
        /// </summary>
        public double DeathHoldSeconds;
    }

    /// <summary>
    /// CfgCombatAnimationSettings 配置表。
    /// </summary>
    public partial class CfgCombatAnimationSettingsTable
    {
        private readonly Dictionary<int, CfgCombatAnimationSettingsRow> byId = new Dictionary<int, CfgCombatAnimationSettingsRow>();
        private CfgCombatAnimationSettingsRow[] rows = Array.Empty<CfgCombatAnimationSettingsRow>();

        /// <summary>
        /// 取得全部配置行。
        /// </summary>
        /// <returns>配置行的只读列表。</returns>
        public IReadOnlyList<CfgCombatAnimationSettingsRow> GetAll() => rows;

        /// <summary>
        /// 按 ID 查找配置行。
        /// </summary>
        /// <param name="id">配置行 ID。</param>
        /// <returns>找到的配置行；不存在时为 null。</returns>
        public CfgCombatAnimationSettingsRow GetById(int id) => byId.TryGetValue(id, out CfgCombatAnimationSettingsRow row) ? row : null;

        /// <summary>
        /// 加载并解析配置表。
        /// </summary>
        /// <param name="resources">资源模块。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>已经解析的配置表。</returns>
        internal static async UniTask<CfgCombatAnimationSettingsTable> LoadAsync(IResourceModule resources, CancellationToken ct)
        {
            using (ResourceLease<TextAsset> lease = await resources.AcquireAsync<TextAsset>("Table_CfgCombatAnimationSettings", ct: ct))
            {
                TextAsset asset = lease.Asset;
                if (asset == null) throw new InvalidOperationException("配置表资源不可用：CfgCombatAnimationSettings");
                var envelope = JsonUtility.FromJson(asset.text, typeof(Envelope)) as Envelope;
                if (envelope == null || envelope.schemaHash != "b8cc2729ded81896847d46fbd241f48e369c13aa728a07df44930e5ae1936b54" || envelope.rows == null)
                    throw new InvalidOperationException("配置表结构不匹配：CfgCombatAnimationSettings");

                var table = new CfgCombatAnimationSettingsTable();
                table.rows = envelope.rows;
                foreach (CfgCombatAnimationSettingsRow row in table.rows)
                {
                    if (row == null || !table.byId.TryAdd(row.ID, row))
                        throw new InvalidOperationException("配置表 ID 无效或重复：CfgCombatAnimationSettings");
                }
                return table;
            }
        }

        [Serializable]
        private sealed class Envelope
        {
            public string schemaHash;
            public CfgCombatAnimationSettingsRow[] rows;
        }
    }
}
