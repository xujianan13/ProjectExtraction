using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;
using UnityEngine;

namespace GameLogic.Tables
{
    /// <summary>
    /// CfgBosses 配置行。
    /// </summary>
    [Serializable]
    public partial class CfgBossesRow
    {
        /// <summary>
        /// ID 字段。
        /// </summary>
        public int ID;

        /// <summary>
        /// Name 字段。
        /// </summary>
        public string Name;

        /// <summary>
        /// EnemyID 字段。
        /// </summary>
        public int EnemyID;

        /// <summary>
        /// RewardID 字段。
        /// </summary>
        public int RewardID;

        /// <summary>
        /// WeaknessDescription 字段。
        /// </summary>
        public string WeaknessDescription;

        /// <summary>
        /// CollectionPreviewIDs 字段。
        /// </summary>
        public string[] CollectionPreviewIDs;
    }

    /// <summary>
    /// CfgBosses 配置表。
    /// </summary>
    public partial class CfgBossesTable
    {
        private readonly Dictionary<int, CfgBossesRow> byId = new Dictionary<int, CfgBossesRow>();
        private CfgBossesRow[] rows = Array.Empty<CfgBossesRow>();

        /// <summary>
        /// 取得全部配置行。
        /// </summary>
        /// <returns>配置行的只读列表。</returns>
        public IReadOnlyList<CfgBossesRow> GetAll() => rows;

        /// <summary>
        /// 按 ID 查找配置行。
        /// </summary>
        /// <param name="id">配置行 ID。</param>
        /// <returns>找到的配置行；不存在时为 null。</returns>
        public CfgBossesRow GetById(int id) => byId.TryGetValue(id, out CfgBossesRow row) ? row : null;

        /// <summary>
        /// 加载并解析配置表。
        /// </summary>
        /// <param name="resources">资源模块。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>已经解析的配置表。</returns>
        internal static async UniTask<CfgBossesTable> LoadAsync(IResourceModule resources, CancellationToken ct)
        {
            using (ResourceLease<TextAsset> lease = await resources.AcquireAsync<TextAsset>("Table_CfgBosses", ct: ct))
            {
                TextAsset asset = lease.Asset;
                if (asset == null) throw new InvalidOperationException("配置表资源不可用：CfgBosses");
                var envelope = JsonUtility.FromJson(asset.text, typeof(Envelope)) as Envelope;
                if (envelope == null || envelope.schemaHash != "4973bf55b5e06b4ba99a694f2dcc7b4ce60d8aed83244129a43e9766fbcc0586" || envelope.rows == null)
                    throw new InvalidOperationException("配置表结构不匹配：CfgBosses");

                var table = new CfgBossesTable();
                table.rows = envelope.rows;
                foreach (CfgBossesRow row in table.rows)
                {
                    if (row == null || !table.byId.TryAdd(row.ID, row))
                        throw new InvalidOperationException("配置表 ID 无效或重复：CfgBosses");
                }
                return table;
            }
        }

        [Serializable]
        private sealed class Envelope
        {
            public string schemaHash;
            public CfgBossesRow[] rows;
        }
    }
}
