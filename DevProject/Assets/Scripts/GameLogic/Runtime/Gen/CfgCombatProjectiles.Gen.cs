using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;
using UnityEngine;

namespace GameLogic.Tables
{
    /// <summary>
    /// CfgCombatProjectiles 配置行。
    /// </summary>
    [Serializable]
    public partial class CfgCombatProjectilesRow
    {
        /// <summary>
        /// ID 字段。
        /// </summary>
        public int ID;

        /// <summary>
        /// Motion 字段。
        /// </summary>
        public int Motion;

        /// <summary>
        /// Target 字段。
        /// </summary>
        public int Target;

        /// <summary>
        /// SpawnAnchor 字段。
        /// </summary>
        public int SpawnAnchor;

        /// <summary>
        /// LostTarget 字段。
        /// </summary>
        public int LostTarget;

        /// <summary>
        /// SpawnOffsetX 字段。
        /// </summary>
        public double SpawnOffsetX;

        /// <summary>
        /// SpawnOffsetY 字段。
        /// </summary>
        public double SpawnOffsetY;

        /// <summary>
        /// SpawnOffsetZ 字段。
        /// </summary>
        public double SpawnOffsetZ;

        /// <summary>
        /// Speed 字段。
        /// </summary>
        public double Speed;

        /// <summary>
        /// Acceleration 字段。
        /// </summary>
        public double Acceleration;

        /// <summary>
        /// MaxSpeed 字段。
        /// </summary>
        public double MaxSpeed;

        /// <summary>
        /// TurnDegreesPerSecond 字段。
        /// </summary>
        public double TurnDegreesPerSecond;

        /// <summary>
        /// GravityX 字段。
        /// </summary>
        public double GravityX;

        /// <summary>
        /// GravityY 字段。
        /// </summary>
        public double GravityY;

        /// <summary>
        /// GravityZ 字段。
        /// </summary>
        public double GravityZ;

        /// <summary>
        /// ArcHeight 字段。
        /// </summary>
        public double ArcHeight;

        /// <summary>
        /// FlightTicks 字段。
        /// </summary>
        public int FlightTicks;

        /// <summary>
        /// LifetimeTicks 字段。
        /// </summary>
        public int LifetimeTicks;

        /// <summary>
        /// MaxDistance 字段。
        /// </summary>
        public double MaxDistance;

        /// <summary>
        /// Radius 字段。
        /// </summary>
        public double Radius;

        /// <summary>
        /// MaxDistinctTargets 字段。
        /// </summary>
        public int MaxDistinctTargets;

        /// <summary>
        /// MaxBounces 字段。
        /// </summary>
        public int MaxBounces;

        /// <summary>
        /// SameTargetIntervalTicks 字段。
        /// </summary>
        public int SameTargetIntervalTicks;

        /// <summary>
        /// MaxHitsPerTarget 字段。
        /// </summary>
        public int MaxHitsPerTarget;

        /// <summary>
        /// OnlyLockedTarget 字段。
        /// </summary>
        public bool OnlyLockedTarget;

        /// <summary>
        /// RemoveOnSourceDeath 字段。
        /// </summary>
        public bool RemoveOnSourceDeath;

        /// <summary>
        /// OnHitEffectIDs 字段。
        /// </summary>
        public int[] OnHitEffectIDs;

        /// <summary>
        /// OnArriveEffectIDs 字段。
        /// </summary>
        public int[] OnArriveEffectIDs;

        /// <summary>
        /// OnExpireEffectIDs 字段。
        /// </summary>
        public int[] OnExpireEffectIDs;

        /// <summary>
        /// ReacquireRange 字段。
        /// </summary>
        public double ReacquireRange;

        /// <summary>
        /// OrbitRadius 字段。
        /// </summary>
        public double OrbitRadius;

        /// <summary>
        /// OrbitDegreesPerSecond 字段。
        /// </summary>
        public double OrbitDegreesPerSecond;

        /// <summary>
        /// OrbitInitialAngle 字段。
        /// </summary>
        public double OrbitInitialAngle;

        /// <summary>
        /// VisualScaleX 字段。
        /// </summary>
        public double VisualScaleX;

        /// <summary>
        /// VisualScaleY 字段。
        /// </summary>
        public double VisualScaleY;

        /// <summary>
        /// VisualScaleZ 字段。
        /// </summary>
        public double VisualScaleZ;

        /// <summary>
        /// VisualRed 字段。
        /// </summary>
        public double VisualRed;

        /// <summary>
        /// VisualGreen 字段。
        /// </summary>
        public double VisualGreen;

        /// <summary>
        /// VisualBlue 字段。
        /// </summary>
        public double VisualBlue;

        /// <summary>
        /// VisualHeight 字段。
        /// </summary>
        public double VisualHeight;
    }

    /// <summary>
    /// CfgCombatProjectiles 配置表。
    /// </summary>
    public partial class CfgCombatProjectilesTable
    {
        private readonly Dictionary<int, CfgCombatProjectilesRow> byId = new Dictionary<int, CfgCombatProjectilesRow>();
        private CfgCombatProjectilesRow[] rows = Array.Empty<CfgCombatProjectilesRow>();

        /// <summary>
        /// 取得全部配置行。
        /// </summary>
        /// <returns>配置行的只读列表。</returns>
        public IReadOnlyList<CfgCombatProjectilesRow> GetAll() => rows;

        /// <summary>
        /// 按 ID 查找配置行。
        /// </summary>
        /// <param name="id">配置行 ID。</param>
        /// <returns>找到的配置行；不存在时为 null。</returns>
        public CfgCombatProjectilesRow GetById(int id) => byId.TryGetValue(id, out CfgCombatProjectilesRow row) ? row : null;

        /// <summary>
        /// 加载并解析配置表。
        /// </summary>
        /// <param name="resources">资源模块。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>已经解析的配置表。</returns>
        internal static async UniTask<CfgCombatProjectilesTable> LoadAsync(IResourceModule resources, CancellationToken ct)
        {
            using (ResourceLease<TextAsset> lease = await resources.AcquireAsync<TextAsset>("Table_CfgCombatProjectiles", ct: ct))
            {
                TextAsset asset = lease.Asset;
                if (asset == null) throw new InvalidOperationException("配置表资源不可用：CfgCombatProjectiles");
                var envelope = JsonUtility.FromJson(asset.text, typeof(Envelope)) as Envelope;
                if (envelope == null || envelope.schemaHash != "dedfbf914af96bc57d6f8af7375e75c3a3812cf60018f0f3e2309b8b42e07ec9" || envelope.rows == null)
                    throw new InvalidOperationException("配置表结构不匹配：CfgCombatProjectiles");

                var table = new CfgCombatProjectilesTable();
                table.rows = envelope.rows;
                foreach (CfgCombatProjectilesRow row in table.rows)
                {
                    if (row == null || !table.byId.TryAdd(row.ID, row))
                        throw new InvalidOperationException("配置表 ID 无效或重复：CfgCombatProjectiles");
                }
                return table;
            }
        }

        [Serializable]
        private sealed class Envelope
        {
            public string schemaHash;
            public CfgCombatProjectilesRow[] rows;
        }
    }
}
