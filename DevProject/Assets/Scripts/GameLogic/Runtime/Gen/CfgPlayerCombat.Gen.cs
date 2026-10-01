using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;
using UnityEngine;

namespace GameLogic.Tables
{
    /// <summary>
    /// CfgPlayerCombat 配置行。
    /// </summary>
    [Serializable]
    public partial class CfgPlayerCombatRow
    {
        /// <summary>
        /// ID 字段。
        /// </summary>
        public int ID;

        /// <summary>
        /// Radius 字段。
        /// </summary>
        public double Radius;

        /// <summary>
        /// SkillID 字段。
        /// </summary>
        public int SkillID;

        /// <summary>
        /// SpawnX 字段。
        /// </summary>
        public double SpawnX;

        /// <summary>
        /// SpawnZ 字段。
        /// </summary>
        public double SpawnZ;

        /// <summary>
        /// ScaleX 字段。
        /// </summary>
        public double ScaleX;

        /// <summary>
        /// ScaleY 字段。
        /// </summary>
        public double ScaleY;

        /// <summary>
        /// ScaleZ 字段。
        /// </summary>
        public double ScaleZ;
    }

    /// <summary>
    /// CfgPlayerCombat 配置表。
    /// </summary>
    public partial class CfgPlayerCombatTable
    {
        private readonly Dictionary<int, CfgPlayerCombatRow> byId = new Dictionary<int, CfgPlayerCombatRow>();
        private CfgPlayerCombatRow[] rows = Array.Empty<CfgPlayerCombatRow>();

        /// <summary>
        /// 取得全部配置行。
        /// </summary>
        /// <returns>配置行的只读列表。</returns>
        public IReadOnlyList<CfgPlayerCombatRow> GetAll() => rows;

        /// <summary>
        /// 按 ID 查找配置行。
        /// </summary>
        /// <param name="id">配置行 ID。</param>
        /// <returns>找到的配置行；不存在时为 null。</returns>
        public CfgPlayerCombatRow GetById(int id) => byId.TryGetValue(id, out CfgPlayerCombatRow row) ? row : null;

        /// <summary>
        /// 加载并解析配置表。
        /// </summary>
        /// <param name="resources">资源模块。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>已经解析的配置表。</returns>
        internal static async UniTask<CfgPlayerCombatTable> LoadAsync(IResourceModule resources, CancellationToken ct)
        {
            using (ResourceLease<TextAsset> lease = await resources.AcquireAsync<TextAsset>("Table_CfgPlayerCombat", ct: ct))
            {
                TextAsset asset = lease.Asset;
                if (asset == null) throw new InvalidOperationException("配置表资源不可用：CfgPlayerCombat");
                var envelope = JsonUtility.FromJson(asset.text, typeof(Envelope)) as Envelope;
                if (envelope == null || envelope.schemaHash != "a8d066416861faf8ac7c5fb6451011f60e958ada65196b243ee1ec166b498215" || envelope.rows == null)
                    throw new InvalidOperationException("配置表结构不匹配：CfgPlayerCombat");

                var table = new CfgPlayerCombatTable();
                table.rows = envelope.rows;
                foreach (CfgPlayerCombatRow row in table.rows)
                {
                    if (row == null || !table.byId.TryAdd(row.ID, row))
                        throw new InvalidOperationException("配置表 ID 无效或重复：CfgPlayerCombat");
                }
                return table;
            }
        }

        [Serializable]
        private sealed class Envelope
        {
            public string schemaHash;
            public CfgPlayerCombatRow[] rows;
        }
    }
}
