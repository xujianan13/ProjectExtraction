using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;
using UnityEngine;

namespace GameLogic.Tables
{
    /// <summary>
    /// CfgRoomLayouts 配置行。
    /// </summary>
    [Serializable]
    public partial class CfgRoomLayoutsRow
    {
        /// <summary>
        /// ID 字段。
        /// </summary>
        public int ID;

        /// <summary>
        /// HalfExtent 字段。
        /// </summary>
        public double HalfExtent;

        /// <summary>
        /// WallHeight 字段。
        /// </summary>
        public double WallHeight;

        /// <summary>
        /// WallThickness 字段。
        /// </summary>
        public double WallThickness;

        /// <summary>
        /// ExitHalfWidth 字段。
        /// </summary>
        public double ExitHalfWidth;

        /// <summary>
        /// SpawnInset 字段。
        /// </summary>
        public double SpawnInset;

        /// <summary>
        /// ExitCenterInset 字段。
        /// </summary>
        public double ExitCenterInset;

        /// <summary>
        /// ExitInteractionRadius 字段。
        /// </summary>
        public double ExitInteractionRadius;

        /// <summary>
        /// ObjectInteractionRadius 字段。
        /// </summary>
        public double ObjectInteractionRadius;

        /// <summary>
        /// ChestX 字段。
        /// </summary>
        public double ChestX;

        /// <summary>
        /// ChestZ 字段。
        /// </summary>
        public double ChestZ;

        /// <summary>
        /// NpcX 字段。
        /// </summary>
        public double NpcX;

        /// <summary>
        /// NpcZ 字段。
        /// </summary>
        public double NpcZ;

        /// <summary>
        /// ExtractionX 字段。
        /// </summary>
        public double ExtractionX;

        /// <summary>
        /// ExtractionZ 字段。
        /// </summary>
        public double ExtractionZ;

        /// <summary>
        /// ObstacleHeight 字段。
        /// </summary>
        public double ObstacleHeight;
    }

    /// <summary>
    /// CfgRoomLayouts 配置表。
    /// </summary>
    public partial class CfgRoomLayoutsTable
    {
        private readonly Dictionary<int, CfgRoomLayoutsRow> byId = new Dictionary<int, CfgRoomLayoutsRow>();
        private CfgRoomLayoutsRow[] rows = Array.Empty<CfgRoomLayoutsRow>();

        /// <summary>
        /// 取得全部配置行。
        /// </summary>
        /// <returns>配置行的只读列表。</returns>
        public IReadOnlyList<CfgRoomLayoutsRow> GetAll() => rows;

        /// <summary>
        /// 按 ID 查找配置行。
        /// </summary>
        /// <param name="id">配置行 ID。</param>
        /// <returns>找到的配置行；不存在时为 null。</returns>
        public CfgRoomLayoutsRow GetById(int id) => byId.TryGetValue(id, out CfgRoomLayoutsRow row) ? row : null;

        /// <summary>
        /// 加载并解析配置表。
        /// </summary>
        /// <param name="resources">资源模块。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>已经解析的配置表。</returns>
        internal static async UniTask<CfgRoomLayoutsTable> LoadAsync(IResourceModule resources, CancellationToken ct)
        {
            using (ResourceLease<TextAsset> lease = await resources.AcquireAsync<TextAsset>("Table_CfgRoomLayouts", ct: ct))
            {
                TextAsset asset = lease.Asset;
                if (asset == null) throw new InvalidOperationException("配置表资源不可用：CfgRoomLayouts");
                var envelope = JsonUtility.FromJson(asset.text, typeof(Envelope)) as Envelope;
                if (envelope == null || envelope.schemaHash != "c6fe154bc1ae8ef8c12f623f06e119469937251667532e7fa93a3f896a8b260b" || envelope.rows == null)
                    throw new InvalidOperationException("配置表结构不匹配：CfgRoomLayouts");

                var table = new CfgRoomLayoutsTable();
                table.rows = envelope.rows;
                foreach (CfgRoomLayoutsRow row in table.rows)
                {
                    if (row == null || !table.byId.TryAdd(row.ID, row))
                        throw new InvalidOperationException("配置表 ID 无效或重复：CfgRoomLayouts");
                }
                return table;
            }
        }

        [Serializable]
        private sealed class Envelope
        {
            public string schemaHash;
            public CfgRoomLayoutsRow[] rows;
        }
    }
}
