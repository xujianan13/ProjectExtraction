using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;
using UnityEngine;

namespace GameLogic.Tables
{
    /// <summary>
    /// CfgBossRewards 配置行。
    /// </summary>
    [Serializable]
    public partial class CfgBossRewardsRow
    {
        /// <summary>
        /// ID 字段。
        /// </summary>
        public int ID;

        /// <summary>
        /// Rounds 字段。
        /// </summary>
        public int Rounds;

        /// <summary>
        /// EquipmentPoolID 字段。
        /// </summary>
        public string EquipmentPoolID;

        /// <summary>
        /// CollectionIDs 字段。
        /// </summary>
        public string[] CollectionIDs;

        /// <summary>
        /// EquipmentWeight 字段。
        /// </summary>
        public int EquipmentWeight;

        /// <summary>
        /// CollectionWeight 字段。
        /// </summary>
        public int CollectionWeight;

        /// <summary>
        /// MinQuality 字段。
        /// </summary>
        public int MinQuality;

        /// <summary>
        /// MaxQuality 字段。
        /// </summary>
        public int MaxQuality;

        /// <summary>
        /// MinEquipmentCandidates 字段。
        /// </summary>
        public int MinEquipmentCandidates;

        /// <summary>
        /// MinCollectionCandidates 字段。
        /// </summary>
        public int MinCollectionCandidates;

        /// <summary>
        /// AllowRepeatCollections 字段。
        /// </summary>
        public bool AllowRepeatCollections;
    }

    /// <summary>
    /// CfgBossRewards 配置表。
    /// </summary>
    public partial class CfgBossRewardsTable
    {
        private readonly Dictionary<int, CfgBossRewardsRow> byId = new Dictionary<int, CfgBossRewardsRow>();
        private CfgBossRewardsRow[] rows = Array.Empty<CfgBossRewardsRow>();

        /// <summary>
        /// 取得全部配置行。
        /// </summary>
        /// <returns>配置行的只读列表。</returns>
        public IReadOnlyList<CfgBossRewardsRow> GetAll() => rows;

        /// <summary>
        /// 按 ID 查找配置行。
        /// </summary>
        /// <param name="id">配置行 ID。</param>
        /// <returns>找到的配置行；不存在时为 null。</returns>
        public CfgBossRewardsRow GetById(int id) => byId.TryGetValue(id, out CfgBossRewardsRow row) ? row : null;

        /// <summary>
        /// 加载并解析配置表。
        /// </summary>
        /// <param name="resources">资源模块。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>已经解析的配置表。</returns>
        internal static async UniTask<CfgBossRewardsTable> LoadAsync(IResourceModule resources, CancellationToken ct)
        {
            using (ResourceLease<TextAsset> lease = await resources.AcquireAsync<TextAsset>("Table_CfgBossRewards", ct: ct))
            {
                TextAsset asset = lease.Asset;
                if (asset == null) throw new InvalidOperationException("配置表资源不可用：CfgBossRewards");
                var envelope = JsonUtility.FromJson(asset.text, typeof(Envelope)) as Envelope;
                if (envelope == null || envelope.schemaHash != "2ebc368d9bd3fae7934dbdb9bf19bed8c92ed8cff2ed8d839eeaef9b777d3d25" || envelope.rows == null)
                    throw new InvalidOperationException("配置表结构不匹配：CfgBossRewards");

                var table = new CfgBossRewardsTable();
                table.rows = envelope.rows;
                foreach (CfgBossRewardsRow row in table.rows)
                {
                    if (row == null || !table.byId.TryAdd(row.ID, row))
                        throw new InvalidOperationException("配置表 ID 无效或重复：CfgBossRewards");
                }
                return table;
            }
        }

        [Serializable]
        private sealed class Envelope
        {
            public string schemaHash;
            public CfgBossRewardsRow[] rows;
        }
    }
}
