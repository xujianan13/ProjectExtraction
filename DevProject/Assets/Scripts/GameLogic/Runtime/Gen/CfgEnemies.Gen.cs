using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;
using UnityEngine;

namespace GameLogic.Tables
{
    /// <summary>
    /// CfgEnemies 配置行。
    /// </summary>
    [Serializable]
    public partial class CfgEnemiesRow
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
        /// PrefabAddress 字段。
        /// </summary>
        public string PrefabAddress;

        /// <summary>
        /// ModelScaleX 字段。
        /// </summary>
        public double ModelScaleX;

        /// <summary>
        /// ModelScaleY 字段。
        /// </summary>
        public double ModelScaleY;

        /// <summary>
        /// ModelScaleZ 字段。
        /// </summary>
        public double ModelScaleZ;

        /// <summary>
        /// BaseHealth 字段。
        /// </summary>
        public double BaseHealth;

        /// <summary>
        /// HealthPerLevel 字段。
        /// </summary>
        public double HealthPerLevel;

        /// <summary>
        /// Attack 字段。
        /// </summary>
        public double Attack;

        /// <summary>
        /// MoveSpeed 字段。
        /// </summary>
        public double MoveSpeed;

        /// <summary>
        /// Radius 字段。
        /// </summary>
        public double Radius;

        /// <summary>
        /// AttackRange 字段。
        /// </summary>
        public double AttackRange;

        /// <summary>
        /// AggroRange 字段。
        /// </summary>
        public double AggroRange;

        /// <summary>
        /// SkillID 字段。
        /// </summary>
        public int SkillID;

        /// <summary>
        /// ExperienceBase 字段。
        /// </summary>
        public int ExperienceBase;

        /// <summary>
        /// ExperiencePerLevel 字段。
        /// </summary>
        public int ExperiencePerLevel;

        /// <summary>
        /// Role 字段。
        /// </summary>
        public int Role;

        /// <summary>
        /// Defense 字段。
        /// </summary>
        public double Defense;

        /// <summary>
        /// VulnerableEffectIDs 字段。
        /// </summary>
        public int[] VulnerableEffectIDs;

        /// <summary>
        /// VulnerabilityMultiplier 字段。
        /// </summary>
        public double VulnerabilityMultiplier;

        /// <summary>
        /// WeaknessDescription 字段。
        /// </summary>
        public string WeaknessDescription;
    }

    /// <summary>
    /// CfgEnemies 配置表。
    /// </summary>
    public partial class CfgEnemiesTable
    {
        private readonly Dictionary<int, CfgEnemiesRow> byId = new Dictionary<int, CfgEnemiesRow>();
        private CfgEnemiesRow[] rows = Array.Empty<CfgEnemiesRow>();

        /// <summary>
        /// 取得全部配置行。
        /// </summary>
        /// <returns>配置行的只读列表。</returns>
        public IReadOnlyList<CfgEnemiesRow> GetAll() => rows;

        /// <summary>
        /// 按 ID 查找配置行。
        /// </summary>
        /// <param name="id">配置行 ID。</param>
        /// <returns>找到的配置行；不存在时为 null。</returns>
        public CfgEnemiesRow GetById(int id) => byId.TryGetValue(id, out CfgEnemiesRow row) ? row : null;

        /// <summary>
        /// 加载并解析配置表。
        /// </summary>
        /// <param name="resources">资源模块。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>已经解析的配置表。</returns>
        internal static async UniTask<CfgEnemiesTable> LoadAsync(IResourceModule resources, CancellationToken ct)
        {
            using (ResourceLease<TextAsset> lease = await resources.AcquireAsync<TextAsset>("Table_CfgEnemies", ct: ct))
            {
                TextAsset asset = lease.Asset;
                if (asset == null) throw new InvalidOperationException("配置表资源不可用：CfgEnemies");
                var envelope = JsonUtility.FromJson(asset.text, typeof(Envelope)) as Envelope;
                if (envelope == null || envelope.schemaHash != "d52633067a01b2c7d9f8082c9b838b4476232dd141911e45a0877ee2e31c4e03" || envelope.rows == null)
                    throw new InvalidOperationException("配置表结构不匹配：CfgEnemies");

                var table = new CfgEnemiesTable();
                table.rows = envelope.rows;
                foreach (CfgEnemiesRow row in table.rows)
                {
                    if (row == null || !table.byId.TryAdd(row.ID, row))
                        throw new InvalidOperationException("配置表 ID 无效或重复：CfgEnemies");
                }
                return table;
            }
        }

        [Serializable]
        private sealed class Envelope
        {
            public string schemaHash;
            public CfgEnemiesRow[] rows;
        }
    }
}
