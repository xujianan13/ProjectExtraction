using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;
using UnityEngine;

namespace GameLogic.Tables
{
    /// <summary>
    /// CfgRoomTemplates 配置行。
    /// </summary>
    [Serializable]
    public partial class CfgRoomTemplatesRow
    {
        /// <summary>
        /// ID 字段。
        /// </summary>
        public int ID;

        /// <summary>
        /// LevelID 字段。
        /// </summary>
        public int LevelID;

        /// <summary>
        /// RoomType 字段。
        /// </summary>
        public string RoomType;

        /// <summary>
        /// IsStart 字段。
        /// </summary>
        public bool IsStart;

        /// <summary>
        /// LayoutID 字段。
        /// </summary>
        public int LayoutID;

        /// <summary>
        /// Weight 字段。
        /// </summary>
        public int Weight;

        /// <summary>
        /// AllowedExits 字段。
        /// </summary>
        public int AllowedExits;

        /// <summary>
        /// EncounterPoolID 字段。
        /// </summary>
        public string EncounterPoolID;

        /// <summary>
        /// LootPoolID 字段。
        /// </summary>
        public string LootPoolID;

        /// <summary>
        /// NpcPrice 字段。
        /// </summary>
        public int NpcPrice;

        /// <summary>
        /// NpcStock 字段。
        /// </summary>
        public int NpcStock;
    }

    /// <summary>
    /// CfgRoomTemplates 配置表。
    /// </summary>
    public partial class CfgRoomTemplatesTable
    {
        private readonly Dictionary<int, CfgRoomTemplatesRow> byId = new Dictionary<int, CfgRoomTemplatesRow>();
        private CfgRoomTemplatesRow[] rows = Array.Empty<CfgRoomTemplatesRow>();

        /// <summary>
        /// 取得全部配置行。
        /// </summary>
        /// <returns>配置行的只读列表。</returns>
        public IReadOnlyList<CfgRoomTemplatesRow> GetAll() => rows;

        /// <summary>
        /// 按 ID 查找配置行。
        /// </summary>
        /// <param name="id">配置行 ID。</param>
        /// <returns>找到的配置行；不存在时为 null。</returns>
        public CfgRoomTemplatesRow GetById(int id) => byId.TryGetValue(id, out CfgRoomTemplatesRow row) ? row : null;

        /// <summary>
        /// 加载并解析配置表。
        /// </summary>
        /// <param name="resources">资源模块。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>已经解析的配置表。</returns>
        internal static async UniTask<CfgRoomTemplatesTable> LoadAsync(IResourceModule resources, CancellationToken ct)
        {
            using (ResourceLease<TextAsset> lease = await resources.AcquireAsync<TextAsset>("Table_CfgRoomTemplates", ct: ct))
            {
                TextAsset asset = lease.Asset;
                if (asset == null) throw new InvalidOperationException("配置表资源不可用：CfgRoomTemplates");
                var envelope = JsonUtility.FromJson(asset.text, typeof(Envelope)) as Envelope;
                if (envelope == null || envelope.schemaHash != "6a53e5d1d8da5982d5ffb09d42c004e833b7664790e3577d094825fc37dd8c31" || envelope.rows == null)
                    throw new InvalidOperationException("配置表结构不匹配：CfgRoomTemplates");

                var table = new CfgRoomTemplatesTable();
                table.rows = envelope.rows;
                foreach (CfgRoomTemplatesRow row in table.rows)
                {
                    if (row == null || !table.byId.TryAdd(row.ID, row))
                        throw new InvalidOperationException("配置表 ID 无效或重复：CfgRoomTemplates");
                }
                return table;
            }
        }

        [Serializable]
        private sealed class Envelope
        {
            public string schemaHash;
            public CfgRoomTemplatesRow[] rows;
        }
    }
}
