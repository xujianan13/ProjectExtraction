using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;
using UnityEngine;

namespace GameLogic.Tables
{
    /// <summary>
    /// CfgRoguePolicy 配置行。
    /// </summary>
    [Serializable]
    public partial class CfgRoguePolicyRow
    {
        /// <summary>
        /// ID 字段。
        /// </summary>
        public int ID;

        /// <summary>
        /// RefreshEnabled 字段。
        /// </summary>
        public bool RefreshEnabled;

        /// <summary>
        /// ExtraChoiceEnabled 字段。
        /// </summary>
        public bool ExtraChoiceEnabled;

        /// <summary>
        /// MaxRefreshes 字段。
        /// </summary>
        public int MaxRefreshes;

        /// <summary>
        /// MobClearChance 字段。
        /// </summary>
        public double MobClearChance;

        /// <summary>
        /// BossClearReward 字段。
        /// </summary>
        public bool BossClearReward;

        /// <summary>
        /// FirstMobClearReward 字段。
        /// </summary>
        public bool FirstMobClearReward;

        /// <summary>
        /// MaxConsecutiveMobMisses 字段。
        /// </summary>
        public int MaxConsecutiveMobMisses;

        /// <summary>
        /// SelectedUpgradeWeightMultiplier 字段。
        /// </summary>
        public int SelectedUpgradeWeightMultiplier;

        /// <summary>
        /// UnlockedComboWeightMultiplier 字段。
        /// </summary>
        public int UnlockedComboWeightMultiplier;
    }

    /// <summary>
    /// CfgRoguePolicy 配置表。
    /// </summary>
    public partial class CfgRoguePolicyTable
    {
        private readonly Dictionary<int, CfgRoguePolicyRow> byId = new Dictionary<int, CfgRoguePolicyRow>();
        private CfgRoguePolicyRow[] rows = Array.Empty<CfgRoguePolicyRow>();

        /// <summary>
        /// 取得全部配置行。
        /// </summary>
        /// <returns>配置行的只读列表。</returns>
        public IReadOnlyList<CfgRoguePolicyRow> GetAll() => rows;

        /// <summary>
        /// 按 ID 查找配置行。
        /// </summary>
        /// <param name="id">配置行 ID。</param>
        /// <returns>找到的配置行；不存在时为 null。</returns>
        public CfgRoguePolicyRow GetById(int id) => byId.TryGetValue(id, out CfgRoguePolicyRow row) ? row : null;

        /// <summary>
        /// 加载并解析配置表。
        /// </summary>
        /// <param name="resources">资源模块。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>已经解析的配置表。</returns>
        internal static async UniTask<CfgRoguePolicyTable> LoadAsync(IResourceModule resources, CancellationToken ct)
        {
            using (ResourceLease<TextAsset> lease = await resources.AcquireAsync<TextAsset>("Table_CfgRoguePolicy", ct: ct))
            {
                TextAsset asset = lease.Asset;
                if (asset == null) throw new InvalidOperationException("配置表资源不可用：CfgRoguePolicy");
                var envelope = JsonUtility.FromJson(asset.text, typeof(Envelope)) as Envelope;
                if (envelope == null || envelope.schemaHash != "aedd5f8fbfdfadf7e3bd3a19f792fac2ba0958466c453f6efc9e95a2a795fce3" || envelope.rows == null)
                    throw new InvalidOperationException("配置表结构不匹配：CfgRoguePolicy");

                var table = new CfgRoguePolicyTable();
                table.rows = envelope.rows;
                foreach (CfgRoguePolicyRow row in table.rows)
                {
                    if (row == null || !table.byId.TryAdd(row.ID, row))
                        throw new InvalidOperationException("配置表 ID 无效或重复：CfgRoguePolicy");
                }
                return table;
            }
        }

        [Serializable]
        private sealed class Envelope
        {
            public string schemaHash;
            public CfgRoguePolicyRow[] rows;
        }
    }
}
