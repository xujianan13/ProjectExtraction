using System;
using System.Collections.Generic;
using GameLogic.Gameplay.Combat;
using GameLogic.Gameplay.Level;
using GameLogic.Gameplay.Roguelike;
using GameLogic.Project;

namespace GameLogic.Gameplay.Expedition
{
    /// <summary>
    /// 一次完整逻辑提交的关卡快照，所有可写集合均与正在运行的世界隔离。
    /// </summary>
    [Serializable]
    public sealed class ExpeditionRunSnapshot
    {
        public int Version = 1;
        public string ConfigVersion;
        public string RunId;
        public int LevelId;
        public int Seed;
        public int LootLevel;
        public int CurrentRegionIndex;
        public int NextActorId = 2;
        public int KillExperience;
        public int GoldSpent;
        public float AugmentBaseAttackRange;
        public long LootRandomState;
        public long BossRandomState;
        public long ActiveRogueQualification;
        public bool RegionInitialized;
        public List<RegionGenerationRequest> Requests = new();
        public List<RegionRuntimeState> Regions = new();
        public List<ProjectItem> Inventory = new();
        public List<string> ProtectedItemIds = new();
        public List<ExpeditionDrop> Drops = new();
        public List<int> PendingBossRegions = new();
        public RogueChoiceState Rogue;
        public RogueProgressState Progress;
        public BossLootChoiceState BossLoot;
        public ProjectRunStatistics Statistics;
        public bool HasPendingOutcome;
        public ProjectRunOutcome PendingOutcome;
        public CombatWorldSnapshot World;
    }
}
