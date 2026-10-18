using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;
using UnityEngine;

namespace GameLogic.Tables
{
    /// <summary>
    /// CfgProfile 配置行。
    /// </summary>
    [Serializable]
    public partial class CfgProfileRow
    {
        /// <summary>
        /// ID 字段。
        /// </summary>
        public int ID;

        /// <summary>
        /// StaminaCap 字段。
        /// </summary>
        public int StaminaCap;

        /// <summary>
        /// EntryCost 字段。
        /// </summary>
        public int EntryCost;

        /// <summary>
        /// RecoverySeconds 字段。
        /// </summary>
        public int RecoverySeconds;

        /// <summary>
        /// SupplyAmount 字段。
        /// </summary>
        public int SupplyAmount;

        /// <summary>
        /// GameUtcOffsetHours 字段。
        /// </summary>
        public int GameUtcOffsetHours;

        /// <summary>
        /// DailyResetHour 字段。
        /// </summary>
        public int DailyResetHour;

        /// <summary>
        /// SkillPointsPerLevel 字段。
        /// </summary>
        public int SkillPointsPerLevel;

        /// <summary>
        /// ExtractionExperience 字段。
        /// </summary>
        public int ExtractionExperience;

        /// <summary>
        /// KeepKillExperienceOnDeath 字段。
        /// </summary>
        public bool KeepKillExperienceOnDeath;

        /// <summary>
        /// NpcItemPrice 字段。
        /// </summary>
        public int NpcItemPrice;

        /// <summary>
        /// ExperienceBase 字段。
        /// </summary>
        public int ExperienceBase;

        /// <summary>
        /// ExperiencePerLevel 字段。
        /// </summary>
        public int ExperiencePerLevel;

        /// <summary>
        /// StartingLevel 字段。
        /// </summary>
        public int StartingLevel;

        /// <summary>
        /// StartingExperience 字段。
        /// </summary>
        public int StartingExperience;

        /// <summary>
        /// StartingGold 字段。
        /// </summary>
        public int StartingGold;

        /// <summary>
        /// StartingDiamonds 字段。
        /// </summary>
        public int StartingDiamonds;

        /// <summary>
        /// StartingSkillPoints 字段。
        /// </summary>
        public int StartingSkillPoints;

        /// <summary>
        /// BaseMaxHealth 字段。
        /// </summary>
        public double BaseMaxHealth;

        /// <summary>
        /// BaseCriticalDamage 字段。
        /// </summary>
        public double BaseCriticalDamage;

        /// <summary>
        /// BaseAttackRange 字段。
        /// </summary>
        public double BaseAttackRange;

        /// <summary>
        /// BaseMoveSpeed 字段。
        /// </summary>
        public double BaseMoveSpeed;

        /// <summary>
        /// UnarmedAttack 字段。
        /// </summary>
        public double UnarmedAttack;

        /// <summary>
        /// MaxAttributeValue 字段。
        /// </summary>
        public double MaxAttributeValue;

        /// <summary>
        /// MaxAttackRange 字段。
        /// </summary>
        public double MaxAttackRange;

        /// <summary>
        /// MaxDodgeChance 字段。
        /// </summary>
        public double MaxDodgeChance;

        /// <summary>
        /// MaxDamageReduction 字段。
        /// </summary>
        public double MaxDamageReduction;

        /// <summary>
        /// MaxLevel 字段。
        /// </summary>
        public int MaxLevel;

        /// <summary>
        /// StarterDefinitionId 字段。
        /// </summary>
        public string StarterDefinitionId;

        /// <summary>
        /// DefaultLootPoolID 字段。
        /// </summary>
        public string DefaultLootPoolID;

        /// <summary>
        /// AffixLevelScale 字段。
        /// </summary>
        public double AffixLevelScale;

        /// <summary>
        /// SupplyHourFirst 字段。
        /// </summary>
        public int SupplyHourFirst;

        /// <summary>
        /// SupplyHourSecond 字段。
        /// </summary>
        public int SupplyHourSecond;

        /// <summary>
        /// SupplyHourThird 字段。
        /// </summary>
        public int SupplyHourThird;

        /// <summary>
        /// StarterQuality 字段。
        /// </summary>
        public int StarterQuality;

        /// <summary>
        /// RunHistoryLimit 字段。
        /// </summary>
        public int RunHistoryLimit;

        /// <summary>
        /// AdExtractionItemLimit 字段。
        /// </summary>
        public int AdExtractionItemLimit;

        /// <summary>
        /// RoomEntryCost 字段。
        /// </summary>
        public int RoomEntryCost;
    }

    /// <summary>
    /// CfgProfile 配置表。
    /// </summary>
    public partial class CfgProfileTable
    {
        private readonly Dictionary<int, CfgProfileRow> byId = new Dictionary<int, CfgProfileRow>();
        private CfgProfileRow[] rows = Array.Empty<CfgProfileRow>();

        /// <summary>
        /// 取得全部配置行。
        /// </summary>
        /// <returns>配置行的只读列表。</returns>
        public IReadOnlyList<CfgProfileRow> GetAll() => rows;

        /// <summary>
        /// 按 ID 查找配置行。
        /// </summary>
        /// <param name="id">配置行 ID。</param>
        /// <returns>找到的配置行；不存在时为 null。</returns>
        public CfgProfileRow GetById(int id) => byId.TryGetValue(id, out CfgProfileRow row) ? row : null;

        /// <summary>
        /// 加载并解析配置表。
        /// </summary>
        /// <param name="resources">资源模块。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>已经解析的配置表。</returns>
        internal static async UniTask<CfgProfileTable> LoadAsync(IResourceModule resources, CancellationToken ct)
        {
            using (ResourceLease<TextAsset> lease = await resources.AcquireAsync<TextAsset>("Table_CfgProfile", ct: ct))
            {
                TextAsset asset = lease.Asset;
                if (asset == null) throw new InvalidOperationException("配置表资源不可用：CfgProfile");
                var envelope = JsonUtility.FromJson(asset.text, typeof(Envelope)) as Envelope;
                if (envelope == null || envelope.schemaHash != "1467aac4bc77a5f9eef08e165f34f3b9f56ecbbec9c6dd487fa4ffeba0830cd9" || envelope.rows == null)
                    throw new InvalidOperationException("配置表结构不匹配：CfgProfile");

                var table = new CfgProfileTable();
                table.rows = envelope.rows;
                foreach (CfgProfileRow row in table.rows)
                {
                    if (row == null || !table.byId.TryAdd(row.ID, row))
                        throw new InvalidOperationException("配置表 ID 无效或重复：CfgProfile");
                }
                return table;
            }
        }

        [Serializable]
        private sealed class Envelope
        {
            public string schemaHash;
            public CfgProfileRow[] rows;
        }
    }
}
