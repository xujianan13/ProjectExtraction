using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;
using UnityEngine;

namespace GameLogic.Tables
{
    /// <summary>
    /// CfgCollectionAchievements 配置行。
    /// </summary>
    [Serializable]
    public partial class CfgCollectionAchievementsRow
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
        /// CollectionIDs 字段。
        /// </summary>
        public string[] CollectionIDs;

        /// <summary>
        /// Attributes 字段。
        /// </summary>
        public int[] Attributes;

        /// <summary>
        /// Values 字段。
        /// </summary>
        public double[] Values;
    }

    /// <summary>
    /// CfgCollectionAchievements 配置表。
    /// </summary>
    public partial class CfgCollectionAchievementsTable
    {
        private readonly Dictionary<string, CfgCollectionAchievementsRow> byId = new Dictionary<string, CfgCollectionAchievementsRow>();
        private CfgCollectionAchievementsRow[] rows = Array.Empty<CfgCollectionAchievementsRow>();

        /// <summary>
        /// 取得全部配置行。
        /// </summary>
        /// <returns>配置行的只读列表。</returns>
        public IReadOnlyList<CfgCollectionAchievementsRow> GetAll() => rows;

        /// <summary>
        /// 按 ID 查找配置行。
        /// </summary>
        /// <param name="id">配置行 ID。</param>
        /// <returns>找到的配置行；不存在时为 null。</returns>
        public CfgCollectionAchievementsRow GetById(string id) => byId.TryGetValue(id, out CfgCollectionAchievementsRow row) ? row : null;

        /// <summary>
        /// 加载并解析配置表。
        /// </summary>
        /// <param name="resources">资源模块。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>已经解析的配置表。</returns>
        internal static async UniTask<CfgCollectionAchievementsTable> LoadAsync(IResourceModule resources, CancellationToken ct)
        {
            using (ResourceLease<TextAsset> lease = await resources.AcquireAsync<TextAsset>("Table_CfgCollectionAchievements", ct: ct))
            {
                TextAsset asset = lease.Asset;
                if (asset == null) throw new InvalidOperationException("配置表资源不可用：CfgCollectionAchievements");
                var envelope = JsonUtility.FromJson(asset.text, typeof(Envelope)) as Envelope;
                if (envelope == null || envelope.schemaHash != "23779207917135b9c84f0bd80ae580833b9d9cc30365750af12aba547912bc31" || envelope.rows == null)
                    throw new InvalidOperationException("配置表结构不匹配：CfgCollectionAchievements");

                var table = new CfgCollectionAchievementsTable();
                table.rows = envelope.rows;
                foreach (CfgCollectionAchievementsRow row in table.rows)
                {
                    if (row == null || !table.byId.TryAdd(row.ID, row))
                        throw new InvalidOperationException("配置表 ID 无效或重复：CfgCollectionAchievements");
                }
                return table;
            }
        }

        [Serializable]
        private sealed class Envelope
        {
            public string schemaHash;
            public CfgCollectionAchievementsRow[] rows;
        }
    }
}
