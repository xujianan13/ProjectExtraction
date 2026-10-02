using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;
using UnityEngine;

namespace GameLogic.Tables
{
    /// <summary>
    /// CfgSpawns 配置行。
    /// </summary>
    [Serializable]
    public partial class CfgSpawnsRow
    {
        /// <summary>
        /// ID 字段。
        /// </summary>
        public int ID;

        /// <summary>
        /// EncounterID 字段。
        /// </summary>
        public int EncounterID;

        /// <summary>
        /// EnemyID 字段。
        /// </summary>
        public int EnemyID;

        /// <summary>
        /// Count 字段。
        /// </summary>
        public int Count;

        /// <summary>
        /// StartX 字段。
        /// </summary>
        public double StartX;

        /// <summary>
        /// StartZ 字段。
        /// </summary>
        public double StartZ;

        /// <summary>
        /// StepX 字段。
        /// </summary>
        public double StepX;

        /// <summary>
        /// StepZ 字段。
        /// </summary>
        public double StepZ;

        /// <summary>
        /// AlternateZ 字段。
        /// </summary>
        public double AlternateZ;
    }

    /// <summary>
    /// CfgSpawns 配置表。
    /// </summary>
    public partial class CfgSpawnsTable
    {
        private readonly Dictionary<int, CfgSpawnsRow> byId = new Dictionary<int, CfgSpawnsRow>();
        private CfgSpawnsRow[] rows = Array.Empty<CfgSpawnsRow>();

        /// <summary>
        /// 取得全部配置行。
        /// </summary>
        /// <returns>配置行的只读列表。</returns>
        public IReadOnlyList<CfgSpawnsRow> GetAll() => rows;

        /// <summary>
        /// 按 ID 查找配置行。
        /// </summary>
        /// <param name="id">配置行 ID。</param>
        /// <returns>找到的配置行；不存在时为 null。</returns>
        public CfgSpawnsRow GetById(int id) => byId.TryGetValue(id, out CfgSpawnsRow row) ? row : null;

        /// <summary>
        /// 加载并解析配置表。
        /// </summary>
        /// <param name="resources">资源模块。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>已经解析的配置表。</returns>
        internal static async UniTask<CfgSpawnsTable> LoadAsync(IResourceModule resources, CancellationToken ct)
        {
            using (ResourceLease<TextAsset> lease = await resources.AcquireAsync<TextAsset>("Table_CfgSpawns", ct: ct))
            {
                TextAsset asset = lease.Asset;
                if (asset == null) throw new InvalidOperationException("配置表资源不可用：CfgSpawns");
                var envelope = JsonUtility.FromJson(asset.text, typeof(Envelope)) as Envelope;
                if (envelope == null || envelope.schemaHash != "d857049dd2f98132dcb682eac298605b69369589513a45d4d80f3cda55715d5b" || envelope.rows == null)
                    throw new InvalidOperationException("配置表结构不匹配：CfgSpawns");

                var table = new CfgSpawnsTable();
                table.rows = envelope.rows;
                foreach (CfgSpawnsRow row in table.rows)
                {
                    if (row == null || !table.byId.TryAdd(row.ID, row))
                        throw new InvalidOperationException("配置表 ID 无效或重复：CfgSpawns");
                }
                return table;
            }
        }

        [Serializable]
        private sealed class Envelope
        {
            public string schemaHash;
            public CfgSpawnsRow[] rows;
        }
    }
}
