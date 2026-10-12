using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;
using UnityEngine;

namespace GameLogic.Tables
{
    /// <summary>
    /// CfgRogueProgress 配置行。
    /// </summary>
    [Serializable]
    public partial class CfgRogueProgressRow
    {
        /// <summary>
        /// ID 字段。
        /// </summary>
        public int ID;

        /// <summary>
        /// NormalContribution 字段。
        /// </summary>
        public int NormalContribution;

        /// <summary>
        /// EliteContribution 字段。
        /// </summary>
        public int EliteContribution;

        /// <summary>
        /// Threshold 字段。
        /// </summary>
        public int Threshold;
    }

    /// <summary>
    /// CfgRogueProgress 配置表。
    /// </summary>
    public partial class CfgRogueProgressTable
    {
        private readonly Dictionary<int, CfgRogueProgressRow> byId = new Dictionary<int, CfgRogueProgressRow>();
        private CfgRogueProgressRow[] rows = Array.Empty<CfgRogueProgressRow>();

        /// <summary>
        /// 取得全部配置行。
        /// </summary>
        /// <returns>配置行的只读列表。</returns>
        public IReadOnlyList<CfgRogueProgressRow> GetAll() => rows;

        /// <summary>
        /// 按 ID 查找配置行。
        /// </summary>
        /// <param name="id">配置行 ID。</param>
        /// <returns>找到的配置行；不存在时为 null。</returns>
        public CfgRogueProgressRow GetById(int id) => byId.TryGetValue(id, out CfgRogueProgressRow row) ? row : null;

        /// <summary>
        /// 加载并解析配置表。
        /// </summary>
        /// <param name="resources">资源模块。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>已经解析的配置表。</returns>
        internal static async UniTask<CfgRogueProgressTable> LoadAsync(IResourceModule resources, CancellationToken ct)
        {
            using (ResourceLease<TextAsset> lease = await resources.AcquireAsync<TextAsset>("Table_CfgRogueProgress", ct: ct))
            {
                TextAsset asset = lease.Asset;
                if (asset == null) throw new InvalidOperationException("配置表资源不可用：CfgRogueProgress");
                var envelope = JsonUtility.FromJson(asset.text, typeof(Envelope)) as Envelope;
                if (envelope == null || envelope.schemaHash != "e0158fc7609c5ec17b21e1ab544e7a53a745f8a3ce6155ee3e5a102a89fd495c" || envelope.rows == null)
                    throw new InvalidOperationException("配置表结构不匹配：CfgRogueProgress");

                var table = new CfgRogueProgressTable();
                table.rows = envelope.rows;
                foreach (CfgRogueProgressRow row in table.rows)
                {
                    if (row == null || !table.byId.TryAdd(row.ID, row))
                        throw new InvalidOperationException("配置表 ID 无效或重复：CfgRogueProgress");
                }
                return table;
            }
        }

        [Serializable]
        private sealed class Envelope
        {
            public string schemaHash;
            public CfgRogueProgressRow[] rows;
        }
    }
}
