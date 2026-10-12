using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;
using UnityEngine;

namespace GameLogic.Tables
{
    /// <summary>
    /// CfgDisplay 配置行。
    /// </summary>
    [Serializable]
    public partial class CfgDisplayRow
    {
        /// <summary>
        /// ID 字段。
        /// </summary>
        public int ID;

        /// <summary>
        /// TargetFrameRate 字段。
        /// </summary>
        public int TargetFrameRate;
    }

    /// <summary>
    /// CfgDisplay 配置表。
    /// </summary>
    public partial class CfgDisplayTable
    {
        private readonly Dictionary<int, CfgDisplayRow> byId = new Dictionary<int, CfgDisplayRow>();
        private CfgDisplayRow[] rows = Array.Empty<CfgDisplayRow>();

        /// <summary>
        /// 取得全部配置行。
        /// </summary>
        /// <returns>配置行的只读列表。</returns>
        public IReadOnlyList<CfgDisplayRow> GetAll() => rows;

        /// <summary>
        /// 按 ID 查找配置行。
        /// </summary>
        /// <param name="id">配置行 ID。</param>
        /// <returns>找到的配置行；不存在时为 null。</returns>
        public CfgDisplayRow GetById(int id) => byId.TryGetValue(id, out CfgDisplayRow row) ? row : null;

        /// <summary>
        /// 加载并解析配置表。
        /// </summary>
        /// <param name="resources">资源模块。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>已经解析的配置表。</returns>
        internal static async UniTask<CfgDisplayTable> LoadAsync(IResourceModule resources, CancellationToken ct)
        {
            using (ResourceLease<TextAsset> lease = await resources.AcquireAsync<TextAsset>("Table_CfgDisplay", ct: ct))
            {
                TextAsset asset = lease.Asset;
                if (asset == null) throw new InvalidOperationException("配置表资源不可用：CfgDisplay");
                var envelope = JsonUtility.FromJson(asset.text, typeof(Envelope)) as Envelope;
                if (envelope == null || envelope.schemaHash != "615dea0f2fc834b3e2a6f14a7dfb836f91783c532df5f719346bddddb2ca83ee" || envelope.rows == null)
                    throw new InvalidOperationException("配置表结构不匹配：CfgDisplay");

                var table = new CfgDisplayTable();
                table.rows = envelope.rows;
                foreach (CfgDisplayRow row in table.rows)
                {
                    if (row == null || !table.byId.TryAdd(row.ID, row))
                        throw new InvalidOperationException("配置表 ID 无效或重复：CfgDisplay");
                }
                return table;
            }
        }

        [Serializable]
        private sealed class Envelope
        {
            public string schemaHash;
            public CfgDisplayRow[] rows;
        }
    }
}
