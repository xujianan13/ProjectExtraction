using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;
using UnityEngine;

namespace GameLogic.Tables
{
    /// <summary>
    /// CfgHomeShop 配置行。
    /// </summary>
    [Serializable]
    public partial class CfgHomeShopRow
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
        /// DefinitionId 字段。
        /// </summary>
        public string DefinitionId;

        /// <summary>
        /// Quality 字段。
        /// </summary>
        public int Quality;

        /// <summary>
        /// ItemLevel 字段。
        /// </summary>
        public int ItemLevel;

        /// <summary>
        /// RequiredLevel 字段。
        /// </summary>
        public int RequiredLevel;

        /// <summary>
        /// PriceGold 字段。
        /// </summary>
        public int PriceGold;

        /// <summary>
        /// StockLimit 字段。
        /// </summary>
        public int StockLimit;

        /// <summary>
        /// ItemSeed 字段。
        /// </summary>
        public int ItemSeed;

        /// <summary>
        /// SortOrder 字段。
        /// </summary>
        public int SortOrder;
    }

    /// <summary>
    /// CfgHomeShop 配置表。
    /// </summary>
    public partial class CfgHomeShopTable
    {
        private readonly Dictionary<string, CfgHomeShopRow> byId = new Dictionary<string, CfgHomeShopRow>();
        private CfgHomeShopRow[] rows = Array.Empty<CfgHomeShopRow>();

        /// <summary>
        /// 取得全部配置行。
        /// </summary>
        /// <returns>配置行的只读列表。</returns>
        public IReadOnlyList<CfgHomeShopRow> GetAll() => rows;

        /// <summary>
        /// 按 ID 查找配置行。
        /// </summary>
        /// <param name="id">配置行 ID。</param>
        /// <returns>找到的配置行；不存在时为 null。</returns>
        public CfgHomeShopRow GetById(string id) => byId.TryGetValue(id, out CfgHomeShopRow row) ? row : null;

        /// <summary>
        /// 加载并解析配置表。
        /// </summary>
        /// <param name="resources">资源模块。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>已经解析的配置表。</returns>
        internal static async UniTask<CfgHomeShopTable> LoadAsync(IResourceModule resources, CancellationToken ct)
        {
            using (ResourceLease<TextAsset> lease = await resources.AcquireAsync<TextAsset>("Table_CfgHomeShop", ct: ct))
            {
                TextAsset asset = lease.Asset;
                if (asset == null) throw new InvalidOperationException("配置表资源不可用：CfgHomeShop");
                var envelope = JsonUtility.FromJson(asset.text, typeof(Envelope)) as Envelope;
                if (envelope == null || envelope.schemaHash != "a10597b6cc16dcebc83a16c3d0c4b8fe5256b8e53cded009037043b01cfa741c" || envelope.rows == null)
                    throw new InvalidOperationException("配置表结构不匹配：CfgHomeShop");

                var table = new CfgHomeShopTable();
                table.rows = envelope.rows;
                foreach (CfgHomeShopRow row in table.rows)
                {
                    if (row == null || !table.byId.TryAdd(row.ID, row))
                        throw new InvalidOperationException("配置表 ID 无效或重复：CfgHomeShop");
                }
                return table;
            }
        }

        [Serializable]
        private sealed class Envelope
        {
            public string schemaHash;
            public CfgHomeShopRow[] rows;
        }
    }
}
