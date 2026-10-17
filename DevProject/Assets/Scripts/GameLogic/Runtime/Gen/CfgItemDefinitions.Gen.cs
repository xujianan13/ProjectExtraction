using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;
using UnityEngine;

namespace GameLogic.Tables
{
    /// <summary>
    /// CfgItemDefinitions 配置行。
    /// </summary>
    [Serializable]
    public partial class CfgItemDefinitionsRow
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
        /// Type 字段。
        /// </summary>
        public int Type;

        /// <summary>
        /// StarterEquipped 字段。
        /// </summary>
        public bool StarterEquipped;

        /// <summary>
        /// SellBase 字段。
        /// </summary>
        public int SellBase;

        /// <summary>
        /// SellPerLevel 字段。
        /// </summary>
        public int SellPerLevel;

        /// <summary>
        /// SellRandomExclusive 字段。
        /// </summary>
        public int SellRandomExclusive;

        /// <summary>
        /// SellPerQuality 字段。
        /// </summary>
        public int SellPerQuality;

        /// <summary>
        /// AttackBase 字段。
        /// </summary>
        public double AttackBase;

        /// <summary>
        /// AttackPerLevel 字段。
        /// </summary>
        public double AttackPerLevel;

        /// <summary>
        /// AttackRandomExclusive 字段。
        /// </summary>
        public int AttackRandomExclusive;

        /// <summary>
        /// HealthBase 字段。
        /// </summary>
        public double HealthBase;

        /// <summary>
        /// HealthPerLevel 字段。
        /// </summary>
        public double HealthPerLevel;

        /// <summary>
        /// HealthRandomExclusive 字段。
        /// </summary>
        public int HealthRandomExclusive;

        /// <summary>
        /// DefenseBase 字段。
        /// </summary>
        public double DefenseBase;

        /// <summary>
        /// DefensePerLevel 字段。
        /// </summary>
        public double DefensePerLevel;

        /// <summary>
        /// DefenseRandomMin 字段。
        /// </summary>
        public int DefenseRandomMin;

        /// <summary>
        /// DefenseRandomExclusive 字段。
        /// </summary>
        public int DefenseRandomExclusive;

        /// <summary>
        /// RangeBase 字段。
        /// </summary>
        public double RangeBase;

        /// <summary>
        /// RangeRandom 字段。
        /// </summary>
        public double RangeRandom;

        /// <summary>
        /// CollectionID 字段。
        /// </summary>
        public string CollectionID;

        /// <summary>
        /// WeaponClass 字段。
        /// </summary>
        public int WeaponClass;

        /// <summary>
        /// BasicSkillID 字段。
        /// </summary>
        public int BasicSkillID;
    }

    /// <summary>
    /// CfgItemDefinitions 配置表。
    /// </summary>
    public partial class CfgItemDefinitionsTable
    {
        private readonly Dictionary<string, CfgItemDefinitionsRow> byId = new Dictionary<string, CfgItemDefinitionsRow>();
        private CfgItemDefinitionsRow[] rows = Array.Empty<CfgItemDefinitionsRow>();

        /// <summary>
        /// 取得全部配置行。
        /// </summary>
        /// <returns>配置行的只读列表。</returns>
        public IReadOnlyList<CfgItemDefinitionsRow> GetAll() => rows;

        /// <summary>
        /// 按 ID 查找配置行。
        /// </summary>
        /// <param name="id">配置行 ID。</param>
        /// <returns>找到的配置行；不存在时为 null。</returns>
        public CfgItemDefinitionsRow GetById(string id) => byId.TryGetValue(id, out CfgItemDefinitionsRow row) ? row : null;

        /// <summary>
        /// 加载并解析配置表。
        /// </summary>
        /// <param name="resources">资源模块。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>已经解析的配置表。</returns>
        internal static async UniTask<CfgItemDefinitionsTable> LoadAsync(IResourceModule resources, CancellationToken ct)
        {
            using (ResourceLease<TextAsset> lease = await resources.AcquireAsync<TextAsset>("Table_CfgItemDefinitions", ct: ct))
            {
                TextAsset asset = lease.Asset;
                if (asset == null) throw new InvalidOperationException("配置表资源不可用：CfgItemDefinitions");
                var envelope = JsonUtility.FromJson(asset.text, typeof(Envelope)) as Envelope;
                if (envelope == null || envelope.schemaHash != "1969eb866a17ec14a41885aa07db057ae6606043038117f8dacc07d4ff06c697" || envelope.rows == null)
                    throw new InvalidOperationException("配置表结构不匹配：CfgItemDefinitions");

                var table = new CfgItemDefinitionsTable();
                table.rows = envelope.rows;
                foreach (CfgItemDefinitionsRow row in table.rows)
                {
                    if (row == null || !table.byId.TryAdd(row.ID, row))
                        throw new InvalidOperationException("配置表 ID 无效或重复：CfgItemDefinitions");
                }
                return table;
            }
        }

        [Serializable]
        private sealed class Envelope
        {
            public string schemaHash;
            public CfgItemDefinitionsRow[] rows;
        }
    }
}
