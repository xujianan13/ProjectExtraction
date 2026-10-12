using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;

namespace GameLogic.Tables
{
    /// <summary>
    /// 全部配置表的加载入口。
    /// </summary>
    public static partial class GameTables
    {
        /// <summary>
        /// CfgAdvancedAffixes 配置表。
        /// </summary>
        public static CfgAdvancedAffixesTable CfgAdvancedAffixes { get; private set; }

        /// <summary>
        /// CfgAffixes 配置表。
        /// </summary>
        public static CfgAffixesTable CfgAffixes { get; private set; }

        /// <summary>
        /// CfgAssets 配置表。
        /// </summary>
        public static CfgAssetsTable CfgAssets { get; private set; }

        /// <summary>
        /// CfgBosses 配置表。
        /// </summary>
        public static CfgBossesTable CfgBosses { get; private set; }

        /// <summary>
        /// CfgBossRewards 配置表。
        /// </summary>
        public static CfgBossRewardsTable CfgBossRewards { get; private set; }

        /// <summary>
        /// CfgChestPool 配置表。
        /// </summary>
        public static CfgChestPoolTable CfgChestPool { get; private set; }

        /// <summary>
        /// CfgCollectionAchievements 配置表。
        /// </summary>
        public static CfgCollectionAchievementsTable CfgCollectionAchievements { get; private set; }

        /// <summary>
        /// CfgCollections 配置表。
        /// </summary>
        public static CfgCollectionsTable CfgCollections { get; private set; }

        /// <summary>
        /// CfgCombatAnimationCues 配置表。
        /// </summary>
        public static CfgCombatAnimationCuesTable CfgCombatAnimationCues { get; private set; }

        /// <summary>
        /// CfgCombatAnimationSettings 配置表。
        /// </summary>
        public static CfgCombatAnimationSettingsTable CfgCombatAnimationSettings { get; private set; }

        /// <summary>
        /// CfgCombatAugments 配置表。
        /// </summary>
        public static CfgCombatAugmentsTable CfgCombatAugments { get; private set; }

        /// <summary>
        /// CfgCombatBuffs 配置表。
        /// </summary>
        public static CfgCombatBuffsTable CfgCombatBuffs { get; private set; }

        /// <summary>
        /// CfgCombatEffects 配置表。
        /// </summary>
        public static CfgCombatEffectsTable CfgCombatEffects { get; private set; }

        /// <summary>
        /// CfgCombatFeedback 配置表。
        /// </summary>
        public static CfgCombatFeedbackTable CfgCombatFeedback { get; private set; }

        /// <summary>
        /// CfgCombatProjectiles 配置表。
        /// </summary>
        public static CfgCombatProjectilesTable CfgCombatProjectiles { get; private set; }

        /// <summary>
        /// CfgCombatSkills 配置表。
        /// </summary>
        public static CfgCombatSkillsTable CfgCombatSkills { get; private set; }

        /// <summary>
        /// CfgCombatTimelineNodes 配置表。
        /// </summary>
        public static CfgCombatTimelineNodesTable CfgCombatTimelineNodes { get; private set; }

        /// <summary>
        /// CfgDisplay 配置表。
        /// </summary>
        public static CfgDisplayTable CfgDisplay { get; private set; }

        /// <summary>
        /// CfgElitePool 配置表。
        /// </summary>
        public static CfgElitePoolTable CfgElitePool { get; private set; }

        /// <summary>
        /// CfgEncounters 配置表。
        /// </summary>
        public static CfgEncountersTable CfgEncounters { get; private set; }

        /// <summary>
        /// CfgEnemies 配置表。
        /// </summary>
        public static CfgEnemiesTable CfgEnemies { get; private set; }

        /// <summary>
        /// CfgEnemySkills 配置表。
        /// </summary>
        public static CfgEnemySkillsTable CfgEnemySkills { get; private set; }

        /// <summary>
        /// CfgGuide 配置表。
        /// </summary>
        public static CfgGuideTable CfgGuide { get; private set; }

        /// <summary>
        /// CfgHomeShop 配置表。
        /// </summary>
        public static CfgHomeShopTable CfgHomeShop { get; private set; }

        /// <summary>
        /// CfgItemDefinitions 配置表。
        /// </summary>
        public static CfgItemDefinitionsTable CfgItemDefinitions { get; private set; }

        /// <summary>
        /// CfgLevels 配置表。
        /// </summary>
        public static CfgLevelsTable CfgLevels { get; private set; }

        /// <summary>
        /// CfgLootPool 配置表。
        /// </summary>
        public static CfgLootPoolTable CfgLootPool { get; private set; }

        /// <summary>
        /// CfgLootQualities 配置表。
        /// </summary>
        public static CfgLootQualitiesTable CfgLootQualities { get; private set; }

        /// <summary>
        /// CfgNavigation 配置表。
        /// </summary>
        public static CfgNavigationTable CfgNavigation { get; private set; }

        /// <summary>
        /// CfgNpcPool 配置表。
        /// </summary>
        public static CfgNpcPoolTable CfgNpcPool { get; private set; }

        /// <summary>
        /// CfgPlayerCombat 配置表。
        /// </summary>
        public static CfgPlayerCombatTable CfgPlayerCombat { get; private set; }

        /// <summary>
        /// CfgProfile 配置表。
        /// </summary>
        public static CfgProfileTable CfgProfile { get; private set; }

        /// <summary>
        /// CfgQualities 配置表。
        /// </summary>
        public static CfgQualitiesTable CfgQualities { get; private set; }

        /// <summary>
        /// CfgRegionContent 配置表。
        /// </summary>
        public static CfgRegionContentTable CfgRegionContent { get; private set; }

        /// <summary>
        /// CfgRegionMaps 配置表。
        /// </summary>
        public static CfgRegionMapsTable CfgRegionMaps { get; private set; }

        /// <summary>
        /// CfgRespawnRules 配置表。
        /// </summary>
        public static CfgRespawnRulesTable CfgRespawnRules { get; private set; }

        /// <summary>
        /// CfgRoguePolicy 配置表。
        /// </summary>
        public static CfgRoguePolicyTable CfgRoguePolicy { get; private set; }

        /// <summary>
        /// CfgRoguePools 配置表。
        /// </summary>
        public static CfgRoguePoolsTable CfgRoguePools { get; private set; }

        /// <summary>
        /// CfgRogueProgress 配置表。
        /// </summary>
        public static CfgRogueProgressTable CfgRogueProgress { get; private set; }

        /// <summary>
        /// CfgRogueRewards 配置表。
        /// </summary>
        public static CfgRogueRewardsTable CfgRogueRewards { get; private set; }

        /// <summary>
        /// CfgRunPersistence 配置表。
        /// </summary>
        public static CfgRunPersistenceTable CfgRunPersistence { get; private set; }

        /// <summary>
        /// CfgSkills 配置表。
        /// </summary>
        public static CfgSkillsTable CfgSkills { get; private set; }

        /// <summary>
        /// CfgSpawns 配置表。
        /// </summary>
        public static CfgSpawnsTable CfgSpawns { get; private set; }

        /// <summary>
        /// 加载当前内容包中的全部配置表。
        /// </summary>
        /// <param name="resources">资源模块。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>加载完成后的异步操作。</returns>
        public static async UniTask LoadAsync(IResourceModule resources, CancellationToken ct)
        {
            if (resources == null) throw new ArgumentNullException(nameof(resources));
            ct.ThrowIfCancellationRequested();
            CfgAdvancedAffixesTable nextCfgAdvancedAffixes = await CfgAdvancedAffixesTable.LoadAsync(resources, ct);
            CfgAffixesTable nextCfgAffixes = await CfgAffixesTable.LoadAsync(resources, ct);
            CfgAssetsTable nextCfgAssets = await CfgAssetsTable.LoadAsync(resources, ct);
            CfgBossesTable nextCfgBosses = await CfgBossesTable.LoadAsync(resources, ct);
            CfgBossRewardsTable nextCfgBossRewards = await CfgBossRewardsTable.LoadAsync(resources, ct);
            CfgChestPoolTable nextCfgChestPool = await CfgChestPoolTable.LoadAsync(resources, ct);
            CfgCollectionAchievementsTable nextCfgCollectionAchievements = await CfgCollectionAchievementsTable.LoadAsync(resources, ct);
            CfgCollectionsTable nextCfgCollections = await CfgCollectionsTable.LoadAsync(resources, ct);
            CfgCombatAnimationCuesTable nextCfgCombatAnimationCues = await CfgCombatAnimationCuesTable.LoadAsync(resources, ct);
            CfgCombatAnimationSettingsTable nextCfgCombatAnimationSettings = await CfgCombatAnimationSettingsTable.LoadAsync(resources, ct);
            CfgCombatAugmentsTable nextCfgCombatAugments = await CfgCombatAugmentsTable.LoadAsync(resources, ct);
            CfgCombatBuffsTable nextCfgCombatBuffs = await CfgCombatBuffsTable.LoadAsync(resources, ct);
            CfgCombatEffectsTable nextCfgCombatEffects = await CfgCombatEffectsTable.LoadAsync(resources, ct);
            CfgCombatFeedbackTable nextCfgCombatFeedback = await CfgCombatFeedbackTable.LoadAsync(resources, ct);
            CfgCombatProjectilesTable nextCfgCombatProjectiles = await CfgCombatProjectilesTable.LoadAsync(resources, ct);
            CfgCombatSkillsTable nextCfgCombatSkills = await CfgCombatSkillsTable.LoadAsync(resources, ct);
            CfgCombatTimelineNodesTable nextCfgCombatTimelineNodes = await CfgCombatTimelineNodesTable.LoadAsync(resources, ct);
            CfgDisplayTable nextCfgDisplay = await CfgDisplayTable.LoadAsync(resources, ct);
            CfgElitePoolTable nextCfgElitePool = await CfgElitePoolTable.LoadAsync(resources, ct);
            CfgEncountersTable nextCfgEncounters = await CfgEncountersTable.LoadAsync(resources, ct);
            CfgEnemiesTable nextCfgEnemies = await CfgEnemiesTable.LoadAsync(resources, ct);
            CfgEnemySkillsTable nextCfgEnemySkills = await CfgEnemySkillsTable.LoadAsync(resources, ct);
            CfgGuideTable nextCfgGuide = await CfgGuideTable.LoadAsync(resources, ct);
            CfgHomeShopTable nextCfgHomeShop = await CfgHomeShopTable.LoadAsync(resources, ct);
            CfgItemDefinitionsTable nextCfgItemDefinitions = await CfgItemDefinitionsTable.LoadAsync(resources, ct);
            CfgLevelsTable nextCfgLevels = await CfgLevelsTable.LoadAsync(resources, ct);
            CfgLootPoolTable nextCfgLootPool = await CfgLootPoolTable.LoadAsync(resources, ct);
            CfgLootQualitiesTable nextCfgLootQualities = await CfgLootQualitiesTable.LoadAsync(resources, ct);
            CfgNavigationTable nextCfgNavigation = await CfgNavigationTable.LoadAsync(resources, ct);
            CfgNpcPoolTable nextCfgNpcPool = await CfgNpcPoolTable.LoadAsync(resources, ct);
            CfgPlayerCombatTable nextCfgPlayerCombat = await CfgPlayerCombatTable.LoadAsync(resources, ct);
            CfgProfileTable nextCfgProfile = await CfgProfileTable.LoadAsync(resources, ct);
            CfgQualitiesTable nextCfgQualities = await CfgQualitiesTable.LoadAsync(resources, ct);
            CfgRegionContentTable nextCfgRegionContent = await CfgRegionContentTable.LoadAsync(resources, ct);
            CfgRegionMapsTable nextCfgRegionMaps = await CfgRegionMapsTable.LoadAsync(resources, ct);
            CfgRespawnRulesTable nextCfgRespawnRules = await CfgRespawnRulesTable.LoadAsync(resources, ct);
            CfgRoguePolicyTable nextCfgRoguePolicy = await CfgRoguePolicyTable.LoadAsync(resources, ct);
            CfgRoguePoolsTable nextCfgRoguePools = await CfgRoguePoolsTable.LoadAsync(resources, ct);
            CfgRogueProgressTable nextCfgRogueProgress = await CfgRogueProgressTable.LoadAsync(resources, ct);
            CfgRogueRewardsTable nextCfgRogueRewards = await CfgRogueRewardsTable.LoadAsync(resources, ct);
            CfgRunPersistenceTable nextCfgRunPersistence = await CfgRunPersistenceTable.LoadAsync(resources, ct);
            CfgSkillsTable nextCfgSkills = await CfgSkillsTable.LoadAsync(resources, ct);
            CfgSpawnsTable nextCfgSpawns = await CfgSpawnsTable.LoadAsync(resources, ct);
            CfgAdvancedAffixes = nextCfgAdvancedAffixes;
            CfgAffixes = nextCfgAffixes;
            CfgAssets = nextCfgAssets;
            CfgBosses = nextCfgBosses;
            CfgBossRewards = nextCfgBossRewards;
            CfgChestPool = nextCfgChestPool;
            CfgCollectionAchievements = nextCfgCollectionAchievements;
            CfgCollections = nextCfgCollections;
            CfgCombatAnimationCues = nextCfgCombatAnimationCues;
            CfgCombatAnimationSettings = nextCfgCombatAnimationSettings;
            CfgCombatAugments = nextCfgCombatAugments;
            CfgCombatBuffs = nextCfgCombatBuffs;
            CfgCombatEffects = nextCfgCombatEffects;
            CfgCombatFeedback = nextCfgCombatFeedback;
            CfgCombatProjectiles = nextCfgCombatProjectiles;
            CfgCombatSkills = nextCfgCombatSkills;
            CfgCombatTimelineNodes = nextCfgCombatTimelineNodes;
            CfgDisplay = nextCfgDisplay;
            CfgElitePool = nextCfgElitePool;
            CfgEncounters = nextCfgEncounters;
            CfgEnemies = nextCfgEnemies;
            CfgEnemySkills = nextCfgEnemySkills;
            CfgGuide = nextCfgGuide;
            CfgHomeShop = nextCfgHomeShop;
            CfgItemDefinitions = nextCfgItemDefinitions;
            CfgLevels = nextCfgLevels;
            CfgLootPool = nextCfgLootPool;
            CfgLootQualities = nextCfgLootQualities;
            CfgNavigation = nextCfgNavigation;
            CfgNpcPool = nextCfgNpcPool;
            CfgPlayerCombat = nextCfgPlayerCombat;
            CfgProfile = nextCfgProfile;
            CfgQualities = nextCfgQualities;
            CfgRegionContent = nextCfgRegionContent;
            CfgRegionMaps = nextCfgRegionMaps;
            CfgRespawnRules = nextCfgRespawnRules;
            CfgRoguePolicy = nextCfgRoguePolicy;
            CfgRoguePools = nextCfgRoguePools;
            CfgRogueProgress = nextCfgRogueProgress;
            CfgRogueRewards = nextCfgRogueRewards;
            CfgRunPersistence = nextCfgRunPersistence;
            CfgSkills = nextCfgSkills;
            CfgSpawns = nextCfgSpawns;
        }
    }
}
