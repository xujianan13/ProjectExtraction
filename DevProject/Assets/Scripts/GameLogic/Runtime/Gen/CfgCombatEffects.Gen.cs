using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;
using UnityEngine;

namespace GameLogic.Tables
{
    /// <summary>
    /// CfgCombatEffects 配置行。
    /// </summary>
    [Serializable]
    public partial class CfgCombatEffectsRow
    {
        /// <summary>
        /// ID 字段。
        /// </summary>
        public int ID;

        /// <summary>
        /// Kind 字段。
        /// </summary>
        public int Kind;

        /// <summary>
        /// Attribute 字段。
        /// </summary>
        public int Attribute;

        /// <summary>
        /// DefinitionID 字段。
        /// </summary>
        public int DefinitionID;

        /// <summary>
        /// Value 字段。
        /// </summary>
        public double Value;

        /// <summary>
        /// AttackCoefficient 字段。
        /// </summary>
        public double AttackCoefficient;

        /// <summary>
        /// CanCritical 字段。
        /// </summary>
        public bool CanCritical;

        /// <summary>
        /// CanDodge 字段。
        /// </summary>
        public bool CanDodge;

        /// <summary>
        /// DurationTicks 字段。
        /// </summary>
        public int DurationTicks;

        /// <summary>
        /// LinkedID 字段。
        /// </summary>
        public int LinkedID;

        /// <summary>
        /// Shape 字段。
        /// </summary>
        public int Shape;

        /// <summary>
        /// Radius 字段。
        /// </summary>
        public double Radius;

        /// <summary>
        /// RadiusSource 字段。
        /// </summary>
        public int RadiusSource;

        /// <summary>
        /// Angle 字段。
        /// </summary>
        public double Angle;

        /// <summary>
        /// Width 字段。
        /// </summary>
        public double Width;

        /// <summary>
        /// Height 字段。
        /// </summary>
        public double Height;

        /// <summary>
        /// RequireLineOfSight 字段。
        /// </summary>
        public bool RequireLineOfSight;
    }

    /// <summary>
    /// CfgCombatEffects 配置表。
    /// </summary>
    public partial class CfgCombatEffectsTable
    {
        private readonly Dictionary<int, CfgCombatEffectsRow> byId = new Dictionary<int, CfgCombatEffectsRow>();
        private CfgCombatEffectsRow[] rows = Array.Empty<CfgCombatEffectsRow>();

        /// <summary>
        /// 取得全部配置行。
        /// </summary>
        /// <returns>配置行的只读列表。</returns>
        public IReadOnlyList<CfgCombatEffectsRow> GetAll() => rows;

        /// <summary>
        /// 按 ID 查找配置行。
        /// </summary>
        /// <param name="id">配置行 ID。</param>
        /// <returns>找到的配置行；不存在时为 null。</returns>
        public CfgCombatEffectsRow GetById(int id) => byId.TryGetValue(id, out CfgCombatEffectsRow row) ? row : null;

        /// <summary>
        /// 加载并解析配置表。
        /// </summary>
        /// <param name="resources">资源模块。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>已经解析的配置表。</returns>
        internal static async UniTask<CfgCombatEffectsTable> LoadAsync(IResourceModule resources, CancellationToken ct)
        {
            using (ResourceLease<TextAsset> lease = await resources.AcquireAsync<TextAsset>("Table_CfgCombatEffects", ct: ct))
            {
                TextAsset asset = lease.Asset;
                if (asset == null) throw new InvalidOperationException("配置表资源不可用：CfgCombatEffects");
                var envelope = JsonUtility.FromJson(asset.text, typeof(Envelope)) as Envelope;
                if (envelope == null || envelope.schemaHash != "52e4219312f9aed42161c35fa1a8d45a2bb85cf37220da66da5e09960a18eebe" || envelope.rows == null)
                    throw new InvalidOperationException("配置表结构不匹配：CfgCombatEffects");

                var table = new CfgCombatEffectsTable();
                table.rows = envelope.rows;
                foreach (CfgCombatEffectsRow row in table.rows)
                {
                    if (row == null || !table.byId.TryAdd(row.ID, row))
                        throw new InvalidOperationException("配置表 ID 无效或重复：CfgCombatEffects");
                }
                return table;
            }
        }

        [Serializable]
        private sealed class Envelope
        {
            public string schemaHash;
            public CfgCombatEffectsRow[] rows;
        }
    }
}
