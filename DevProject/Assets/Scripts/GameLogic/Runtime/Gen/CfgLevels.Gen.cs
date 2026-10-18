using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;
using UnityEngine;

namespace GameLogic.Tables
{
    /// <summary>
    /// CfgLevels 配置行。
    /// </summary>
    [Serializable]
    public partial class CfgLevelsRow
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
        /// Description 字段。
        /// </summary>
        public string Description;

        /// <summary>
        /// RequiredLevel 字段。
        /// </summary>
        public int RequiredLevel;

        /// <summary>
        /// IsDefault 字段。
        /// </summary>
        public bool IsDefault;

        /// <summary>
        /// LootPoolID 字段。
        /// </summary>
        public string LootPoolID;

        /// <summary>
        /// LootLevel 字段。
        /// </summary>
        public int LootLevel;

        /// <summary>
        /// RoomIDs 字段。
        /// </summary>
        public int[] RoomIDs;
    }

    /// <summary>
    /// CfgLevels 配置表。
    /// </summary>
    public partial class CfgLevelsTable
    {
        private readonly Dictionary<int, CfgLevelsRow> byId = new Dictionary<int, CfgLevelsRow>();
        private CfgLevelsRow[] rows = Array.Empty<CfgLevelsRow>();

        /// <summary>
        /// 取得全部配置行。
        /// </summary>
        /// <returns>配置行的只读列表。</returns>
        public IReadOnlyList<CfgLevelsRow> GetAll() => rows;

        /// <summary>
        /// 按 ID 查找配置行。
        /// </summary>
        /// <param name="id">配置行 ID。</param>
        /// <returns>找到的配置行；不存在时为 null。</returns>
        public CfgLevelsRow GetById(int id) => byId.TryGetValue(id, out CfgLevelsRow row) ? row : null;

        /// <summary>
        /// 加载并解析配置表。
        /// </summary>
        /// <param name="resources">资源模块。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>已经解析的配置表。</returns>
        internal static async UniTask<CfgLevelsTable> LoadAsync(IResourceModule resources, CancellationToken ct)
        {
            using (ResourceLease<TextAsset> lease = await resources.AcquireAsync<TextAsset>("Table_CfgLevels", ct: ct))
            {
                TextAsset asset = lease.Asset;
                if (asset == null) throw new InvalidOperationException("配置表资源不可用：CfgLevels");
                var envelope = JsonUtility.FromJson(asset.text, typeof(Envelope)) as Envelope;
                if (envelope == null || envelope.schemaHash != "452ed5e5c4d071ced7939b683c154a95257c3ca5c538084518867edec10d71e8" || envelope.rows == null)
                    throw new InvalidOperationException("配置表结构不匹配：CfgLevels");

                var table = new CfgLevelsTable();
                table.rows = envelope.rows;
                foreach (CfgLevelsRow row in table.rows)
                {
                    if (row == null || !table.byId.TryAdd(row.ID, row))
                        throw new InvalidOperationException("配置表 ID 无效或重复：CfgLevels");
                }
                return table;
            }
        }

        [Serializable]
        private sealed class Envelope
        {
            public string schemaHash;
            public CfgLevelsRow[] rows;
        }
    }
}
