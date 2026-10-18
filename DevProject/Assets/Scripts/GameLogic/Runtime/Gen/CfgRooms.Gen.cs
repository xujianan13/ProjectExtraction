using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;
using UnityEngine;

namespace GameLogic.Tables
{
    /// <summary>
    /// CfgRooms 配置行。
    /// </summary>
    [Serializable]
    public partial class CfgRoomsRow
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
        /// Kind 字段。
        /// </summary>
        public int Kind;

        /// <summary>
        /// MapID 字段。
        /// </summary>
        public int MapID;

        /// <summary>
        /// EncounterID 字段。
        /// </summary>
        public int EncounterID;

        /// <summary>
        /// BossID 字段。
        /// </summary>
        public int BossID;

        /// <summary>
        /// ChestPoolID 字段。
        /// </summary>
        public string ChestPoolID;

        /// <summary>
        /// NpcPoolID 字段。
        /// </summary>
        public string NpcPoolID;

        /// <summary>
        /// EnemyLevelOffset 字段。
        /// </summary>
        public int EnemyLevelOffset;

        /// <summary>
        /// InteractionRadius 字段。
        /// </summary>
        public double InteractionRadius;

        /// <summary>
        /// ReachabilityGridSpacing 字段。
        /// </summary>
        public double ReachabilityGridSpacing;

        /// <summary>
        /// ReachabilityMaxCells 字段。
        /// </summary>
        public int ReachabilityMaxCells;

        /// <summary>
        /// MaxSpawnSlots 字段。
        /// </summary>
        public int MaxSpawnSlots;

        /// <summary>
        /// ChestCount 字段。
        /// </summary>
        public int ChestCount;
    }

    /// <summary>
    /// CfgRooms 配置表。
    /// </summary>
    public partial class CfgRoomsTable
    {
        private readonly Dictionary<int, CfgRoomsRow> byId = new Dictionary<int, CfgRoomsRow>();
        private CfgRoomsRow[] rows = Array.Empty<CfgRoomsRow>();

        /// <summary>
        /// 取得全部配置行。
        /// </summary>
        /// <returns>配置行的只读列表。</returns>
        public IReadOnlyList<CfgRoomsRow> GetAll() => rows;

        /// <summary>
        /// 按 ID 查找配置行。
        /// </summary>
        /// <param name="id">配置行 ID。</param>
        /// <returns>找到的配置行；不存在时为 null。</returns>
        public CfgRoomsRow GetById(int id) => byId.TryGetValue(id, out CfgRoomsRow row) ? row : null;

        /// <summary>
        /// 加载并解析配置表。
        /// </summary>
        /// <param name="resources">资源模块。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>已经解析的配置表。</returns>
        internal static async UniTask<CfgRoomsTable> LoadAsync(IResourceModule resources, CancellationToken ct)
        {
            using (ResourceLease<TextAsset> lease = await resources.AcquireAsync<TextAsset>("Table_CfgRooms", ct: ct))
            {
                TextAsset asset = lease.Asset;
                if (asset == null) throw new InvalidOperationException("配置表资源不可用：CfgRooms");
                var envelope = JsonUtility.FromJson(asset.text, typeof(Envelope)) as Envelope;
                if (envelope == null || envelope.schemaHash != "8bbf7b47295c24de627e48e07d16b4932c64c1a1fdefd1cfa98ce9d55ac09f0a" || envelope.rows == null)
                    throw new InvalidOperationException("配置表结构不匹配：CfgRooms");

                var table = new CfgRoomsTable();
                table.rows = envelope.rows;
                foreach (CfgRoomsRow row in table.rows)
                {
                    if (row == null || !table.byId.TryAdd(row.ID, row))
                        throw new InvalidOperationException("配置表 ID 无效或重复：CfgRooms");
                }
                return table;
            }
        }

        [Serializable]
        private sealed class Envelope
        {
            public string schemaHash;
            public CfgRoomsRow[] rows;
        }
    }
}
