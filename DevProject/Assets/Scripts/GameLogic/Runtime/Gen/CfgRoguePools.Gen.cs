using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;
using UnityEngine;

namespace GameLogic.Tables
{
    /// <summary>
    /// CfgRoguePools 配置行。
    /// </summary>
    [Serializable]
    public partial class CfgRoguePoolsRow
    {
        /// <summary>
        /// ID 字段。
        /// </summary>
        public string ID;

        /// <summary>
        /// PoolID 字段。
        /// </summary>
        public string PoolID;

        /// <summary>
        /// RewardID 字段。
        /// </summary>
        public string RewardID;
    }

    /// <summary>
    /// CfgRoguePools 配置表。
    /// </summary>
    public partial class CfgRoguePoolsTable
    {
        private readonly Dictionary<string, CfgRoguePoolsRow> byId = new Dictionary<string, CfgRoguePoolsRow>();
        private CfgRoguePoolsRow[] rows = Array.Empty<CfgRoguePoolsRow>();

        /// <summary>
        /// 取得全部配置行。
        /// </summary>
        /// <returns>配置行的只读列表。</returns>
        public IReadOnlyList<CfgRoguePoolsRow> GetAll() => rows;

        /// <summary>
        /// 按 ID 查找配置行。
        /// </summary>
        /// <param name="id">配置行 ID。</param>
        /// <returns>找到的配置行；不存在时为 null。</returns>
        public CfgRoguePoolsRow GetById(string id) => byId.TryGetValue(id, out CfgRoguePoolsRow row) ? row : null;

        /// <summary>
        /// 加载并解析配置表。
        /// </summary>
        /// <param name="resources">资源模块。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>已经解析的配置表。</returns>
        internal static async UniTask<CfgRoguePoolsTable> LoadAsync(IResourceModule resources, CancellationToken ct)
        {
            using (ResourceLease<TextAsset> lease = await resources.AcquireAsync<TextAsset>("Table_CfgRoguePools", ct: ct))
            {
                TextAsset asset = lease.Asset;
                if (asset == null) throw new InvalidOperationException("配置表资源不可用：CfgRoguePools");
                var envelope = JsonUtility.FromJson(asset.text, typeof(Envelope)) as Envelope;
                if (envelope == null || envelope.schemaHash != "ba384a3dd3e6d3661abaf56cdab159ad704a1e17b649d4cf0d210b5fa17a5ddd" || envelope.rows == null)
                    throw new InvalidOperationException("配置表结构不匹配：CfgRoguePools");

                var table = new CfgRoguePoolsTable();
                table.rows = envelope.rows;
                foreach (CfgRoguePoolsRow row in table.rows)
                {
                    if (row == null || !table.byId.TryAdd(row.ID, row))
                        throw new InvalidOperationException("配置表 ID 无效或重复：CfgRoguePools");
                }
                return table;
            }
        }

        [Serializable]
        private sealed class Envelope
        {
            public string schemaHash;
            public CfgRoguePoolsRow[] rows;
        }
    }
}
