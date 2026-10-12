using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;
using UnityEngine;

namespace GameLogic.Tables
{
    /// <summary>
    /// CfgRunPersistence 配置行。
    /// </summary>
    [Serializable]
    public partial class CfgRunPersistenceRow
    {
        /// <summary>
        /// ID 字段。
        /// </summary>
        public int ID;

        /// <summary>
        /// ConfigVersion 字段。
        /// </summary>
        public string ConfigVersion;

        /// <summary>
        /// CheckpointTicks 字段。
        /// </summary>
        public int CheckpointTicks;

        /// <summary>
        /// MaxPendingTicks 字段。
        /// </summary>
        public int MaxPendingTicks;

        /// <summary>
        /// MaxStateBytes 字段。
        /// </summary>
        public int MaxStateBytes;
    }

    /// <summary>
    /// CfgRunPersistence 配置表。
    /// </summary>
    public partial class CfgRunPersistenceTable
    {
        private readonly Dictionary<int, CfgRunPersistenceRow> byId = new Dictionary<int, CfgRunPersistenceRow>();
        private CfgRunPersistenceRow[] rows = Array.Empty<CfgRunPersistenceRow>();

        /// <summary>
        /// 取得全部配置行。
        /// </summary>
        /// <returns>配置行的只读列表。</returns>
        public IReadOnlyList<CfgRunPersistenceRow> GetAll() => rows;

        /// <summary>
        /// 按 ID 查找配置行。
        /// </summary>
        /// <param name="id">配置行 ID。</param>
        /// <returns>找到的配置行；不存在时为 null。</returns>
        public CfgRunPersistenceRow GetById(int id) => byId.TryGetValue(id, out CfgRunPersistenceRow row) ? row : null;

        /// <summary>
        /// 加载并解析配置表。
        /// </summary>
        /// <param name="resources">资源模块。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>已经解析的配置表。</returns>
        internal static async UniTask<CfgRunPersistenceTable> LoadAsync(IResourceModule resources, CancellationToken ct)
        {
            using (ResourceLease<TextAsset> lease = await resources.AcquireAsync<TextAsset>("Table_CfgRunPersistence", ct: ct))
            {
                TextAsset asset = lease.Asset;
                if (asset == null) throw new InvalidOperationException("配置表资源不可用：CfgRunPersistence");
                var envelope = JsonUtility.FromJson(asset.text, typeof(Envelope)) as Envelope;
                if (envelope == null || envelope.schemaHash != "a19264eb3aaa997d18ee36c7fad7407586cff7826da6a1b705c14619b4405847" || envelope.rows == null)
                    throw new InvalidOperationException("配置表结构不匹配：CfgRunPersistence");

                var table = new CfgRunPersistenceTable();
                table.rows = envelope.rows;
                foreach (CfgRunPersistenceRow row in table.rows)
                {
                    if (row == null || !table.byId.TryAdd(row.ID, row))
                        throw new InvalidOperationException("配置表 ID 无效或重复：CfgRunPersistence");
                }
                return table;
            }
        }

        [Serializable]
        private sealed class Envelope
        {
            public string schemaHash;
            public CfgRunPersistenceRow[] rows;
        }
    }
}
