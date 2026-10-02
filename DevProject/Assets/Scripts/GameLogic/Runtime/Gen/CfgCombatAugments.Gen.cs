using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;
using UnityEngine;

namespace GameLogic.Tables
{
    /// <summary>
    /// CfgCombatAugments 配置行。
    /// </summary>
    [Serializable]
    public partial class CfgCombatAugmentsRow
    {
        /// <summary>
        /// ID 字段。
        /// </summary>
        public string ID;

        /// <summary>
        /// Version 字段。
        /// </summary>
        public int Version;

        /// <summary>
        /// Title 字段。
        /// </summary>
        public string Title;

        /// <summary>
        /// Description 字段。
        /// </summary>
        public string Description;

        /// <summary>
        /// Trigger 字段。
        /// </summary>
        public int Trigger;

        /// <summary>
        /// Chance 字段。
        /// </summary>
        public double Chance;

        /// <summary>
        /// CooldownTicks 字段。
        /// </summary>
        public int CooldownTicks;

        /// <summary>
        /// TargetSelf 字段。
        /// </summary>
        public bool TargetSelf;

        /// <summary>
        /// SkillID 字段。
        /// </summary>
        public int SkillID;

        /// <summary>
        /// EffectIDs 字段。
        /// </summary>
        public int[] EffectIDs;

        /// <summary>
        /// ModifierKind 字段。
        /// </summary>
        public int ModifierKind;

        /// <summary>
        /// ValueMode 字段。
        /// </summary>
        public int ValueMode;
    }

    /// <summary>
    /// CfgCombatAugments 配置表。
    /// </summary>
    public partial class CfgCombatAugmentsTable
    {
        private readonly Dictionary<string, CfgCombatAugmentsRow> byId = new Dictionary<string, CfgCombatAugmentsRow>();
        private CfgCombatAugmentsRow[] rows = Array.Empty<CfgCombatAugmentsRow>();

        /// <summary>
        /// 取得全部配置行。
        /// </summary>
        /// <returns>配置行的只读列表。</returns>
        public IReadOnlyList<CfgCombatAugmentsRow> GetAll() => rows;

        /// <summary>
        /// 按 ID 查找配置行。
        /// </summary>
        /// <param name="id">配置行 ID。</param>
        /// <returns>找到的配置行；不存在时为 null。</returns>
        public CfgCombatAugmentsRow GetById(string id) => byId.TryGetValue(id, out CfgCombatAugmentsRow row) ? row : null;

        /// <summary>
        /// 加载并解析配置表。
        /// </summary>
        /// <param name="resources">资源模块。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>已经解析的配置表。</returns>
        internal static async UniTask<CfgCombatAugmentsTable> LoadAsync(IResourceModule resources, CancellationToken ct)
        {
            using (ResourceLease<TextAsset> lease = await resources.AcquireAsync<TextAsset>("Table_CfgCombatAugments", ct: ct))
            {
                TextAsset asset = lease.Asset;
                if (asset == null) throw new InvalidOperationException("配置表资源不可用：CfgCombatAugments");
                var envelope = JsonUtility.FromJson(asset.text, typeof(Envelope)) as Envelope;
                if (envelope == null || envelope.schemaHash != "c22b1feff90c46cedd141cc3fb3e81cb746ab380a80202d3787d294d71cdc6b3" || envelope.rows == null)
                    throw new InvalidOperationException("配置表结构不匹配：CfgCombatAugments");

                var table = new CfgCombatAugmentsTable();
                table.rows = envelope.rows;
                foreach (CfgCombatAugmentsRow row in table.rows)
                {
                    if (row == null || !table.byId.TryAdd(row.ID, row))
                        throw new InvalidOperationException("配置表 ID 无效或重复：CfgCombatAugments");
                }
                return table;
            }
        }

        [Serializable]
        private sealed class Envelope
        {
            public string schemaHash;
            public CfgCombatAugmentsRow[] rows;
        }
    }
}
