using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;
using UnityEngine;

namespace GameLogic.Tables
{
    /// <summary>
    /// CfgRogueRewards 配置行。
    /// </summary>
    [Serializable]
    public partial class CfgRogueRewardsRow
    {
        /// <summary>
        /// ID 字段。
        /// </summary>
        public string ID;

        /// <summary>
        /// Title 字段。
        /// </summary>
        public string Title;

        /// <summary>
        /// Description 字段。
        /// </summary>
        public string Description;

        /// <summary>
        /// Kind 字段。
        /// </summary>
        public int Kind;

        /// <summary>
        /// Attribute 字段。
        /// </summary>
        public int Attribute;

        /// <summary>
        /// Amount 字段。
        /// </summary>
        public double Amount;

        /// <summary>
        /// LootType 字段。
        /// </summary>
        public int LootType;

        /// <summary>
        /// Weight 字段。
        /// </summary>
        public int Weight;
    }

    /// <summary>
    /// CfgRogueRewards 配置表。
    /// </summary>
    public partial class CfgRogueRewardsTable
    {
        private readonly Dictionary<string, CfgRogueRewardsRow> byId = new Dictionary<string, CfgRogueRewardsRow>();
        private CfgRogueRewardsRow[] rows = Array.Empty<CfgRogueRewardsRow>();

        /// <summary>
        /// 取得全部配置行。
        /// </summary>
        /// <returns>配置行的只读列表。</returns>
        public IReadOnlyList<CfgRogueRewardsRow> GetAll() => rows;

        /// <summary>
        /// 按 ID 查找配置行。
        /// </summary>
        /// <param name="id">配置行 ID。</param>
        /// <returns>找到的配置行；不存在时为 null。</returns>
        public CfgRogueRewardsRow GetById(string id) => byId.TryGetValue(id, out CfgRogueRewardsRow row) ? row : null;

        /// <summary>
        /// 加载并解析配置表。
        /// </summary>
        /// <param name="resources">资源模块。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>已经解析的配置表。</returns>
        internal static async UniTask<CfgRogueRewardsTable> LoadAsync(IResourceModule resources, CancellationToken ct)
        {
            using (ResourceLease<TextAsset> lease = await resources.AcquireAsync<TextAsset>("Table_CfgRogueRewards", ct: ct))
            {
                TextAsset asset = lease.Asset;
                if (asset == null) throw new InvalidOperationException("配置表资源不可用：CfgRogueRewards");
                var envelope = JsonUtility.FromJson(asset.text, typeof(Envelope)) as Envelope;
                if (envelope == null || envelope.schemaHash != "546e80ede78ec3064035ebe298b618082ff8ffa2c2a357aacf762f2ff3d8532e" || envelope.rows == null)
                    throw new InvalidOperationException("配置表结构不匹配：CfgRogueRewards");

                var table = new CfgRogueRewardsTable();
                table.rows = envelope.rows;
                foreach (CfgRogueRewardsRow row in table.rows)
                {
                    if (row == null || !table.byId.TryAdd(row.ID, row))
                        throw new InvalidOperationException("配置表 ID 无效或重复：CfgRogueRewards");
                }
                return table;
            }
        }

        [Serializable]
        private sealed class Envelope
        {
            public string schemaHash;
            public CfgRogueRewardsRow[] rows;
        }
    }
}
