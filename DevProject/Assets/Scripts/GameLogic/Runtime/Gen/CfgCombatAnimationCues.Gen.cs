using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;
using UnityEngine;

namespace GameLogic.Tables
{
    /// <summary>
    /// CfgCombatAnimationCues 配置行。
    /// </summary>
    [Serializable]
    public partial class CfgCombatAnimationCuesRow
    {
        /// <summary>
        /// ID 字段。
        /// </summary>
        public int ID;

        /// <summary>
        /// IsAttack 字段。
        /// </summary>
        public bool IsAttack;

        /// <summary>
        /// StateName 字段。
        /// </summary>
        public string StateName;

        /// <summary>
        /// StatePath 字段。
        /// </summary>
        public string StatePath;
    }

    /// <summary>
    /// CfgCombatAnimationCues 配置表。
    /// </summary>
    public partial class CfgCombatAnimationCuesTable
    {
        private readonly Dictionary<int, CfgCombatAnimationCuesRow> byId = new Dictionary<int, CfgCombatAnimationCuesRow>();
        private CfgCombatAnimationCuesRow[] rows = Array.Empty<CfgCombatAnimationCuesRow>();

        /// <summary>
        /// 取得全部配置行。
        /// </summary>
        /// <returns>配置行的只读列表。</returns>
        public IReadOnlyList<CfgCombatAnimationCuesRow> GetAll() => rows;

        /// <summary>
        /// 按 ID 查找配置行。
        /// </summary>
        /// <param name="id">配置行 ID。</param>
        /// <returns>找到的配置行；不存在时为 null。</returns>
        public CfgCombatAnimationCuesRow GetById(int id) => byId.TryGetValue(id, out CfgCombatAnimationCuesRow row) ? row : null;

        /// <summary>
        /// 加载并解析配置表。
        /// </summary>
        /// <param name="resources">资源模块。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>已经解析的配置表。</returns>
        internal static async UniTask<CfgCombatAnimationCuesTable> LoadAsync(IResourceModule resources, CancellationToken ct)
        {
            using (ResourceLease<TextAsset> lease = await resources.AcquireAsync<TextAsset>("Table_CfgCombatAnimationCues", ct: ct))
            {
                TextAsset asset = lease.Asset;
                if (asset == null) throw new InvalidOperationException("配置表资源不可用：CfgCombatAnimationCues");
                var envelope = JsonUtility.FromJson(asset.text, typeof(Envelope)) as Envelope;
                if (envelope == null || envelope.schemaHash != "a9d14bddd318a06f8bd454156ceb7048b59579bd62186fd145e556644ada3818" || envelope.rows == null)
                    throw new InvalidOperationException("配置表结构不匹配：CfgCombatAnimationCues");

                var table = new CfgCombatAnimationCuesTable();
                table.rows = envelope.rows;
                foreach (CfgCombatAnimationCuesRow row in table.rows)
                {
                    if (row == null || !table.byId.TryAdd(row.ID, row))
                        throw new InvalidOperationException("配置表 ID 无效或重复：CfgCombatAnimationCues");
                }
                return table;
            }
        }

        [Serializable]
        private sealed class Envelope
        {
            public string schemaHash;
            public CfgCombatAnimationCuesRow[] rows;
        }
    }
}
