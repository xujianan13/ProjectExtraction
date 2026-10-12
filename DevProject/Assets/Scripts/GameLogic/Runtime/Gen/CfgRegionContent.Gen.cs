using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;
using UnityEngine;

namespace GameLogic.Tables
{
    /// <summary>
    /// CfgRegionContent 配置行。
    /// </summary>
    [Serializable]
    public partial class CfgRegionContentRow
    {
        /// <summary>
        /// ID 字段。
        /// </summary>
        public int ID;

        /// <summary>
        /// MonsterGroupCount 字段。
        /// </summary>
        public int MonsterGroupCount;

        /// <summary>
        /// EliteCount 字段。
        /// </summary>
        public int EliteCount;

        /// <summary>
        /// ChestCount 字段。
        /// </summary>
        public int ChestCount;

        /// <summary>
        /// NpcCount 字段。
        /// </summary>
        public int NpcCount;

        /// <summary>
        /// MonsterPoolID 字段。
        /// </summary>
        public string MonsterPoolID;

        /// <summary>
        /// ElitePoolID 字段。
        /// </summary>
        public string ElitePoolID;

        /// <summary>
        /// ChestPoolID 字段。
        /// </summary>
        public string ChestPoolID;

        /// <summary>
        /// NpcPoolID 字段。
        /// </summary>
        public string NpcPoolID;

        /// <summary>
        /// NormalRespawnID 字段。
        /// </summary>
        public int NormalRespawnID;

        /// <summary>
        /// EliteRespawnID 字段。
        /// </summary>
        public int EliteRespawnID;

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
    }

    /// <summary>
    /// CfgRegionContent 配置表。
    /// </summary>
    public partial class CfgRegionContentTable
    {
        private readonly Dictionary<int, CfgRegionContentRow> byId = new Dictionary<int, CfgRegionContentRow>();
        private CfgRegionContentRow[] rows = Array.Empty<CfgRegionContentRow>();

        /// <summary>
        /// 取得全部配置行。
        /// </summary>
        /// <returns>配置行的只读列表。</returns>
        public IReadOnlyList<CfgRegionContentRow> GetAll() => rows;

        /// <summary>
        /// 按 ID 查找配置行。
        /// </summary>
        /// <param name="id">配置行 ID。</param>
        /// <returns>找到的配置行；不存在时为 null。</returns>
        public CfgRegionContentRow GetById(int id) => byId.TryGetValue(id, out CfgRegionContentRow row) ? row : null;

        /// <summary>
        /// 加载并解析配置表。
        /// </summary>
        /// <param name="resources">资源模块。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>已经解析的配置表。</returns>
        internal static async UniTask<CfgRegionContentTable> LoadAsync(IResourceModule resources, CancellationToken ct)
        {
            using (ResourceLease<TextAsset> lease = await resources.AcquireAsync<TextAsset>("Table_CfgRegionContent", ct: ct))
            {
                TextAsset asset = lease.Asset;
                if (asset == null) throw new InvalidOperationException("配置表资源不可用：CfgRegionContent");
                var envelope = JsonUtility.FromJson(asset.text, typeof(Envelope)) as Envelope;
                if (envelope == null || envelope.schemaHash != "c4cf3201ab5be994344f62b4923372337fa14f4ae3ac0356b50367a632d75fd4" || envelope.rows == null)
                    throw new InvalidOperationException("配置表结构不匹配：CfgRegionContent");

                var table = new CfgRegionContentTable();
                table.rows = envelope.rows;
                foreach (CfgRegionContentRow row in table.rows)
                {
                    if (row == null || !table.byId.TryAdd(row.ID, row))
                        throw new InvalidOperationException("配置表 ID 无效或重复：CfgRegionContent");
                }
                return table;
            }
        }

        [Serializable]
        private sealed class Envelope
        {
            public string schemaHash;
            public CfgRegionContentRow[] rows;
        }
    }
}
