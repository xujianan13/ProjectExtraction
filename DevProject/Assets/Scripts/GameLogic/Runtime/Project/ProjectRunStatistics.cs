using System;
using System.Collections.Generic;
using System.Text;
using GameLogic.Gameplay.Combat;
using GameLogic.Gameplay.Level;

namespace GameLogic.Project
{
    /// <summary>
    /// 单类敌人的一次生命收益统计，只累计已经确认的真实击杀与收益。
    /// </summary>
    [Serializable]
    public sealed class ProjectEnemyLifeStatistics
    {
        public int Kills;
        public long Progress;
        public long Experience;

        /// <summary>
        /// 创建独立的生命收益统计副本。
        /// </summary>
        /// <returns>不共享可变对象的统计副本。</returns>
        public ProjectEnemyLifeStatistics Copy() => (ProjectEnemyLifeStatistics)MemberwiseClone();
    }

    /// <summary>
    /// 已生成区域的有效停留时间与真实领奖完成状态。
    /// </summary>
    [Serializable]
    public sealed class ProjectRegionStatistics
    {
        public int RegionId;
        public int RegionIndex;
        public bool IsFinalRegion;
        public long EffectiveTicks;
        public bool Completed;

        /// <summary>
        /// 创建独立区域统计，后续恢复或切区不改变冻结记录。
        /// </summary>
        /// <returns>独立区域统计副本。</returns>
        public ProjectRegionStatistics Copy() => (ProjectRegionStatistics)MemberwiseClone();
    }

    /// <summary>
    /// 线性房间的有效时间、完成事实与已经发放的唯一槽位收益。
    /// </summary>
    [Serializable]
    public sealed class ProjectRoomStatistics
    {
        public int RoomId;
        public int RoomIndex;
        public ExpeditionRoomKind Kind;
        public bool Completed;
        public long EffectiveTicks;
        public int NormalKills;
        public int EliteKills;
        public int BossKills;
        public long NormalExperience;
        public long EliteExperience;
        public long BossExperience;
        public int OpenedChests;
        public int ClaimedChestItems;
        public int VisitedNpcs;
        public int BoughtNpcOffers;
        public List<string> RewardedSlotIds = new();
        public int RewardedKills => NormalKills + EliteKills + BossKills;
        public long EarnedExperience => NormalExperience + EliteExperience + BossExperience;

        /// <summary>
        /// 复制房间统计及唯一收益身份，后续重建不改变既有快照。
        /// </summary>
        /// <returns>独立房间统计。</returns>
        public ProjectRoomStatistics Copy()
        {
            var copy = (ProjectRoomStatistics)MemberwiseClone();
            copy.RewardedSlotIds = RewardedSlotIds == null ? null : new List<string>(RewardedSlotIds);
            return copy;
        }
    }

    /// <summary>
    /// 单局冻结统计；版本三记录线性房间与唯一发奖，版本一、二仅用于读取历史，版本零表示未记录。
    /// </summary>
    [Serializable]
    public sealed class ProjectRunStatistics
    {
        public const int CurrentVersion = 3;

        public int Version;
        public int TotalRooms;
        public int VisitedRooms;
        public int ClearedCombatRooms;
        public int EnemyKills;
        public int BossRoomKills;
        public int RogueChoices;
        public int EntryItemCount;
        public long EntryItemSellValue;
        public double DamageDealt;
        public double DamageTaken;
        public double HealingReceived;
        public int TotalRegions;
        public int RegionBossKills;
        public int FinalBossKills;
        public int BossLootRounds;
        public long RegionBossExperience;
        public long FinalBossExperience;
        public ProjectEnemyLifeStatistics NormalFirst = new();
        public ProjectEnemyLifeStatistics NormalRespawn = new();
        public ProjectEnemyLifeStatistics EliteFirst = new();
        public ProjectEnemyLifeStatistics EliteRespawn = new();
        public List<ProjectRegionStatistics> Regions = new();
        public List<ProjectRoomStatistics> Rooms = new();

        public bool HasData => Version == 1 || Version == 2 || Version == CurrentVersion;
        public bool HasRegionData => Version == 2;
        public bool HasRoomData => Version == CurrentVersion;
        public int VisitedRegions => Regions.Count;
        public int CompletedRegions => Regions.FindAll(region => region.Completed).Count;
        public int CompletedRooms => Rooms.FindAll(room => room.Completed).Count;
        public bool IsFinalCompletion => HasRegionData && Regions.Exists(region => region.IsFinalRegion && region.Completed) ||
            HasRoomData && Rooms.Exists(room => room.Kind == ExpeditionRoomKind.Boss && room.Completed);
        public long EarnedKillExperience => NormalFirst.Experience + NormalRespawn.Experience + EliteFirst.Experience +
            EliteRespawn.Experience + RegionBossExperience + FinalBossExperience;

        /// <summary>
        /// 创建不共享引用的统计副本。
        /// </summary>
        /// <returns>独立统计副本。</returns>
        public ProjectRunStatistics Copy()
        {
            var copy = (ProjectRunStatistics)MemberwiseClone();
            copy.NormalFirst = NormalFirst?.Copy();
            copy.NormalRespawn = NormalRespawn?.Copy();
            copy.EliteFirst = EliteFirst?.Copy();
            copy.EliteRespawn = EliteRespawn?.Copy();
            copy.Regions = Regions?.ConvertAll(region => region?.Copy());
            copy.Rooms = Rooms?.ConvertAll(room => room?.Copy());
            return copy;
        }

        /// <summary>
        /// 校验统计版本、计数和有限数值；空记录与全零旧记录表示无统计。
        /// </summary>
        /// <param name="statistics">待校验的统计。</param>
        /// <returns>统计是否有效。</returns>
        public static bool IsValid(ProjectRunStatistics statistics)
        {
            if (statistics == null) return true;
            if (statistics.Version == 0)
                return statistics.TotalRooms == 0 && statistics.VisitedRooms == 0 && statistics.ClearedCombatRooms == 0 &&
                    statistics.EnemyKills == 0 && statistics.BossRoomKills == 0 && statistics.RogueChoices == 0 &&
                    statistics.EntryItemCount == 0 && statistics.EntryItemSellValue == 0 && statistics.DamageDealt == 0 &&
                    statistics.DamageTaken == 0 && statistics.HealingReceived == 0 && NoRegionData(statistics) &&
                    (statistics.Rooms == null || statistics.Rooms.Count == 0);
            if (!statistics.HasData || statistics.EnemyKills < 0 || statistics.RogueChoices < 0 || statistics.EntryItemCount < 0 ||
                statistics.EntryItemSellValue < 0 || statistics.EntryItemSellValue > (long)statistics.EntryItemCount * int.MaxValue ||
                !IsFiniteNonnegative(statistics.DamageDealt) || !IsFiniteNonnegative(statistics.DamageTaken) ||
                !IsFiniteNonnegative(statistics.HealingReceived)) return false;
            if (statistics.HasRoomData) return ValidRoomStatistics(statistics);
            if (statistics.Rooms != null && statistics.Rooms.Count != 0) return false;
            if (statistics.Version == 1)
                return statistics.TotalRooms >= 1 && statistics.VisitedRooms >= 1 && statistics.VisitedRooms <= statistics.TotalRooms &&
                    statistics.ClearedCombatRooms >= 0 && statistics.ClearedCombatRooms <= statistics.VisitedRooms &&
                    statistics.BossRoomKills >= 0 && statistics.BossRoomKills <= statistics.EnemyKills && NoRegionData(statistics);
            if (statistics.TotalRooms != 0 || statistics.VisitedRooms != 0 || statistics.ClearedCombatRooms != 0 ||
                statistics.BossRoomKills != 0 || statistics.TotalRegions < 1 || statistics.RegionBossKills < 0 ||
                statistics.RegionBossKills >= statistics.TotalRegions || statistics.FinalBossKills < 0 || statistics.FinalBossKills > 1 ||
                statistics.BossLootRounds < 0 || !ValidLife(statistics.NormalFirst) || !ValidLife(statistics.NormalRespawn) ||
                !ValidLife(statistics.EliteFirst) || !ValidLife(statistics.EliteRespawn) || statistics.RegionBossExperience < 0 ||
                statistics.RegionBossExperience > int.MaxValue || statistics.FinalBossExperience < 0 ||
                statistics.FinalBossExperience > int.MaxValue || statistics.Regions == null || statistics.Regions.Count > statistics.TotalRegions) return false;
            long kills = (long)statistics.NormalFirst.Kills + statistics.NormalRespawn.Kills + statistics.EliteFirst.Kills +
                statistics.EliteRespawn.Kills + statistics.RegionBossKills + statistics.FinalBossKills;
            if (kills != statistics.EnemyKills || statistics.EarnedKillExperience > int.MaxValue ||
                statistics.RegionBossKills == 0 && statistics.RegionBossExperience != 0 ||
                statistics.FinalBossKills == 0 && statistics.FinalBossExperience != 0) return false;
            var ids = new HashSet<int>();
            for (int index = 0; index < statistics.Regions.Count; index++)
            {
                ProjectRegionStatistics region = statistics.Regions[index];
                if (region == null || region.RegionId <= 0 || !ids.Add(region.RegionId) || region.RegionIndex != index ||
                    region.EffectiveTicks < 0 || region.IsFinalRegion != (index == statistics.TotalRegions - 1) ||
                    region.IsFinalRegion && region.Completed && statistics.FinalBossKills != 1) return false;
            }
            return statistics.CompletedRegions <= statistics.RegionBossKills + statistics.FinalBossKills;
        }

        /// <summary>
        /// 从固定房间计划和业务账本生成收益统计，不读取敌人临时实体或生命次数。
        /// </summary>
        /// <param name="plan">已冻结的房间计划。</param>
        /// <param name="rewardedSlotIds">已经确认发放过收益的唯一槽位身份。</param>
        /// <param name="completed">房间出口是否已按业务规则开放。</param>
        /// <param name="effectiveTicks">本房累计有效帧数。</param>
        /// <param name="chests">宝箱领取事实。</param>
        /// <param name="npcs">商人访问与购买事实。</param>
        /// <param name="statistics">成功时返回独立房间统计。</param>
        /// <returns>账本身份唯一、属于计划且收益配置有效时为真。</returns>
        public static bool TryCreateRoomStatistics(RegionPlan plan, IReadOnlyList<string> rewardedSlotIds, bool completed,
            long effectiveTicks, IReadOnlyList<RegionChestState> chests, IReadOnlyList<RegionNpcState> npcs, out ProjectRoomStatistics statistics)
        {
            statistics = null;
            if (plan == null || plan.Request == null || plan.SpawnSlots == null || plan.Chests == null || plan.Npcs == null ||
                rewardedSlotIds == null || effectiveTicks < 0 || chests == null || npcs == null) return false;
            var row = new ProjectRoomStatistics { RoomId = plan.Request.RegionId, RoomIndex = plan.Request.RegionIndex,
                Kind = plan.Request.RoomKind, Completed = completed, EffectiveTicks = effectiveTicks };
            var slots = new Dictionary<string, RegionSpawnSlotPlan>(StringComparer.Ordinal);
            foreach (var slot in plan.SpawnSlots)
                if (slot == null || string.IsNullOrWhiteSpace(slot.SlotId) || !slots.TryAdd(slot.SlotId, slot)) return false;
            var rewarded = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in rewardedSlotIds)
            {
                if (string.IsNullOrWhiteSpace(id) || !rewarded.Add(id) || !slots.TryGetValue(id, out var slot) ||
                    !LevelContentConfig.TryGetEnemy(slot.EnemyId, out var enemy)) return false;
                int experience = enemy.ExperienceAt(slot.EnemyLevel);
                if (experience < 0) return false;
                row.RewardedSlotIds.Add(id);
                if (slot.Kind == RegionEnemyKind.Normal)
                {
                    row.NormalKills++;
                    row.NormalExperience += experience;
                }
                else if (slot.Kind == RegionEnemyKind.Elite)
                {
                    row.EliteKills++;
                    row.EliteExperience += experience;
                }
                else if (slot.Kind == RegionEnemyKind.Boss)
                {
                    row.BossKills++;
                    row.BossExperience += experience;
                }
                else return false;
            }
            row.RewardedSlotIds.Sort(StringComparer.Ordinal);
            if (completed && (row.Kind == ExpeditionRoomKind.Combat || row.Kind == ExpeditionRoomKind.Boss) &&
                rewarded.Count != slots.Count) return false;
            var chestIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var state in chests)
            {
                if (state == null || state.ClaimedItemIds == null || !chestIds.Add(state.Id)) return false;
                var chest = plan.Chests.Find(value => value.Id == state.Id);
                if (chest == null || chest.Items == null || !state.Opened && state.ClaimedItemIds.Count > 0) return false;
                var itemIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (var id in state.ClaimedItemIds)
                    if (!itemIds.Add(id) || !chest.Items.Exists(item => item.Id == id)) return false;
                if (state.Opened) row.OpenedChests++;
                row.ClaimedChestItems += state.ClaimedItemIds.Count;
                if (completed && row.Kind == ExpeditionRoomKind.Chest && (!state.Opened || state.ClaimedItemIds.Count != chest.Items.Count)) return false;
            }
            var npcIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var state in npcs)
            {
                if (state == null || state.SoldOfferIds == null || !npcIds.Add(state.Id)) return false;
                var npc = plan.Npcs.Find(value => value.Id == state.Id);
                if (npc == null || npc.Offers == null || !state.Visited && state.SoldOfferIds.Count > 0) return false;
                var offerIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (var id in state.SoldOfferIds)
                    if (!offerIds.Add(id) || !npc.Offers.Exists(offer => offer.Id == id)) return false;
                if (state.Visited) row.VisitedNpcs++;
                row.BoughtNpcOffers += state.SoldOfferIds.Count;
            }
            if (chests.Count != plan.Chests.Count || npcs.Count != plan.Npcs.Count || !ValidRoomRow(row)) return false;
            statistics = row;
            return true;
        }

        /// <summary>
        /// 根据房间唯一发奖事实重算汇总，不累计重开后相同槽位的重复击杀。
        /// </summary>
        public void RecalculateRoomTotals()
        {
            VisitedRooms = Rooms.Count;
            ClearedCombatRooms = EnemyKills = BossRoomKills = FinalBossKills = 0;
            NormalFirst = new ProjectEnemyLifeStatistics();
            EliteFirst = new ProjectEnemyLifeStatistics();
            FinalBossExperience = 0;
            foreach (var room in Rooms)
            {
                if (room.Kind == ExpeditionRoomKind.Combat && room.Completed) ClearedCombatRooms++;
                EnemyKills += room.RewardedKills;
                NormalFirst.Kills += room.NormalKills;
                NormalFirst.Experience += room.NormalExperience;
                EliteFirst.Kills += room.EliteKills;
                EliteFirst.Experience += room.EliteExperience;
                FinalBossKills += room.BossKills;
                FinalBossExperience += room.BossExperience;
            }
            BossRoomKills = FinalBossKills;
        }

        /// <summary>
        /// 校验线性房间汇总与各房唯一收益完全一致，拒绝混入旧版复活或区域成长统计。
        /// </summary>
        /// <param name="statistics">新版本冻结统计。</param>
        /// <returns>顺序、完成、收益与汇总全部一致时为真。</returns>
        private static bool ValidRoomStatistics(ProjectRunStatistics statistics)
        {
            if (statistics.TotalRooms < 1 || statistics.VisitedRooms < 0 || statistics.VisitedRooms > statistics.TotalRooms ||
                statistics.Rooms == null || statistics.Rooms.Count != statistics.VisitedRooms || statistics.TotalRegions != 0 ||
                statistics.RegionBossKills != 0 || statistics.RegionBossExperience != 0 || statistics.RogueChoices != 0 ||
                statistics.BossLootRounds < 0 || !ValidLife(statistics.NormalRespawn) || !ValidLife(statistics.EliteRespawn) ||
                !EmptyLife(statistics.NormalRespawn) || !EmptyLife(statistics.EliteRespawn) ||
                statistics.Regions == null || statistics.Regions.Count != 0 || !ValidLife(statistics.NormalFirst) ||
                !ValidLife(statistics.EliteFirst) || statistics.NormalFirst.Progress != 0 || statistics.EliteFirst.Progress != 0 ||
                statistics.FinalBossExperience < 0 || statistics.FinalBossExperience > int.MaxValue ||
                statistics.FinalBossKills < 0 || statistics.FinalBossKills > 1) return false;
            var ids = new HashSet<int>();
            var rewardedIds = new HashSet<string>(StringComparer.Ordinal);
            long normalKills = 0, eliteKills = 0, bossKills = 0, normalExperience = 0, eliteExperience = 0, bossExperience = 0;
            int cleared = 0;
            for (int index = 0; index < statistics.Rooms.Count; index++)
            {
                var room = statistics.Rooms[index];
                if (!ValidRoomRow(room) || room.RoomIndex != index || !ids.Add(room.RoomId) ||
                    index < statistics.Rooms.Count - 1 && !room.Completed ||
                    (room.Kind == ExpeditionRoomKind.Boss) != (index == statistics.TotalRooms - 1)) return false;
                foreach (var id in room.RewardedSlotIds) if (!rewardedIds.Add(id)) return false;
                if (room.Kind == ExpeditionRoomKind.Combat && room.Completed) cleared++;
                normalKills += room.NormalKills;
                eliteKills += room.EliteKills;
                bossKills += room.BossKills;
                normalExperience += room.NormalExperience;
                eliteExperience += room.EliteExperience;
                bossExperience += room.BossExperience;
            }
            return normalKills == statistics.NormalFirst.Kills && eliteKills == statistics.EliteFirst.Kills &&
                bossKills == statistics.FinalBossKills && bossKills == statistics.BossRoomKills &&
                normalKills + eliteKills + bossKills == statistics.EnemyKills && normalExperience == statistics.NormalFirst.Experience &&
                eliteExperience == statistics.EliteFirst.Experience && bossExperience == statistics.FinalBossExperience &&
                normalExperience + eliteExperience + bossExperience <= int.MaxValue && cleared == statistics.ClearedCombatRooms;
        }

        /// <summary>
        /// 校验一行房间统计的身份、类型、唯一收益和交互计数。
        /// </summary>
        /// <param name="room">待检查的房间行。</param>
        /// <returns>房间统计结构完整时为真。</returns>
        private static bool ValidRoomRow(ProjectRoomStatistics room)
        {
            if (room == null || room.RoomId <= 0 || room.RoomIndex < 0 || !Enum.IsDefined(typeof(ExpeditionRoomKind), room.Kind) ||
                room.EffectiveTicks < 0 || room.NormalKills < 0 || room.EliteKills < 0 || room.BossKills < 0 || room.BossKills > 1 ||
                room.NormalExperience < 0 || room.NormalExperience > int.MaxValue || room.EliteExperience < 0 ||
                room.EliteExperience > int.MaxValue || room.BossExperience < 0 || room.BossExperience > int.MaxValue ||
                room.OpenedChests < 0 || room.ClaimedChestItems < 0 || room.VisitedNpcs < 0 || room.BoughtNpcOffers < 0 ||
                room.RewardedSlotIds == null || (long)room.NormalKills + room.EliteKills + room.BossKills != room.RewardedSlotIds.Count ||
                room.NormalKills == 0 && room.NormalExperience != 0 || room.EliteKills == 0 && room.EliteExperience != 0 ||
                room.BossKills == 0 && room.BossExperience != 0) return false;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in room.RewardedSlotIds) if (string.IsNullOrWhiteSpace(id) || !ids.Add(id)) return false;
            if (room.Kind == ExpeditionRoomKind.Combat && room.BossKills != 0 ||
                room.Kind == ExpeditionRoomKind.Boss && (room.NormalKills != 0 || room.EliteKills != 0 || room.Completed && room.BossKills != 1) ||
                (room.Kind == ExpeditionRoomKind.Npc || room.Kind == ExpeditionRoomKind.Chest) && room.RewardedKills != 0 ||
                room.Kind != ExpeditionRoomKind.Chest && (room.OpenedChests != 0 || room.ClaimedChestItems != 0) ||
                room.Kind != ExpeditionRoomKind.Npc && (room.VisitedNpcs != 0 || room.BoughtNpcOffers != 0)) return false;
            return true;
        }

        /// <summary>
        /// 只接受真实击杀支撑的非负收益，不从历史缺项推算。
        /// </summary>
        /// <param name="life">生命分类收益。</param>
        /// <returns>分类计数与收益合法时为真。</returns>
        private static bool ValidLife(ProjectEnemyLifeStatistics life) => life != null && life.Kills >= 0 && life.Progress >= 0 &&
            life.Experience >= 0 && life.Experience <= int.MaxValue && (life.Kills > 0 || life.Progress == 0 && life.Experience == 0);

        /// <summary>
        /// 校验旧统计没有伪造新增区域分类。
        /// </summary>
        /// <param name="statistics">旧版本统计。</param>
        /// <returns>所有新增字段均缺席或为空时为真。</returns>
        private static bool NoRegionData(ProjectRunStatistics statistics) => statistics.TotalRegions == 0 &&
            statistics.RegionBossKills == 0 && statistics.FinalBossKills == 0 && statistics.BossLootRounds == 0 &&
            statistics.RegionBossExperience == 0 && statistics.FinalBossExperience == 0 &&
            EmptyLife(statistics.NormalFirst) && EmptyLife(statistics.NormalRespawn) && EmptyLife(statistics.EliteFirst) &&
            EmptyLife(statistics.EliteRespawn) && (statistics.Regions == null || statistics.Regions.Count == 0);

        /// <summary>
        /// 判断历史缺席的分类统计是否没有任何新增计数。
        /// </summary>
        /// <param name="life">可缺席的历史分类。</param>
        /// <returns>未记录任何分类收益时为真。</returns>
        private static bool EmptyLife(ProjectEnemyLifeStatistics life) => life == null || life.Kills == 0 && life.Progress == 0 && life.Experience == 0;

        /// <summary>
        /// 使用真实版本生成只读表现说明，旧记录明确标记区域分类未记录。
        /// </summary>
        /// <returns>可以在结果和历史滚动区显示的统计说明。</returns>
        public string Describe()
        {
            if (!HasData) return "本局未记录完整表现统计。";
            if (HasRoomData)
            {
                var rooms = new StringBuilder($"房间 {VisitedRooms}/{TotalRooms} · 已完成 {CompletedRooms}\n" +
                    $"有效发奖击杀 {EnemyKills} · 永久经验 {EarnedKillExperience} · 最终 Boss {FinalBossKills}\n");
                foreach (var room in Rooms)
                    rooms.Append("房间 ").Append(room.RoomIndex + 1).Append(" · ").Append(RoomKindName(room.Kind))
                        .Append(" · ").Append(room.Completed ? "已完成" : "未完成").Append(" · 击杀 ").Append(room.RewardedKills)
                        .Append(" · 经验 ").Append(room.EarnedExperience).Append(" · 有效停留 ")
                        .Append((room.EffectiveTicks * (double)CombatWorld.TickSeconds).ToString("0.#")).Append(" 秒\n");
                long opened = 0, claimed = 0, visits = 0, purchases = 0;
                foreach (var room in Rooms)
                {
                    opened += room.OpenedChests;
                    claimed += room.ClaimedChestItems;
                    visits += room.VisitedNpcs;
                    purchases += room.BoughtNpcOffers;
                }
                return rooms.Append($"宝箱 {opened} · 领取物品 {claimed} · NPC 访问 {visits} · 购买 {purchases}\n" +
                    $"造成伤害 {DamageDealt:0.#} · 承受伤害 {DamageTaken:0.#} · 实际治疗 {HealingReceived:0.#}").ToString();
            }
            if (!HasRegionData) return $"历史房间 {VisitedRooms}/{TotalRooms} · 击杀 {EnemyKills} · 技能选择 {RogueChoices}\n" +
                "v2 区域、生命分类、Boss 领奖轮次及提前带回/通关未记录。\n" +
                $"造成伤害 {DamageDealt:0.#} · 承受伤害 {DamageTaken:0.#} · 实际治疗 {HealingReceived:0.#}";
            var text = new StringBuilder($"探索区域 {VisitedRegions}/{TotalRegions} · 已完成区域 {CompletedRegions}\n" +
                $"击杀 {EnemyKills} · 区域尾王 {RegionBossKills} · 最终 Boss {FinalBossKills}\n技能选择 {RogueChoices} · Boss 领奖 {BossLootRounds} 轮\n");
            AppendLife(text, "普通首次", NormalFirst);
            AppendLife(text, "普通复活", NormalRespawn);
            AppendLife(text, "精英首次", EliteFirst);
            AppendLife(text, "精英复活", EliteRespawn);
            foreach (ProjectRegionStatistics region in Regions)
                text.Append(region.IsFinalRegion ? "最终场地" : "区域 " + (region.RegionIndex + 1)).Append(" 有效停留 ")
                    .Append((region.EffectiveTicks * (double)CombatWorld.TickSeconds).ToString("0.#"))
                    .Append(" 秒 · ").Append(region.Completed ? "已完成" : "未完成").Append('\n');
            return text.Append($"造成伤害 {DamageDealt:0.#} · 承受伤害 {DamageTaken:0.#} · 实际治疗 {HealingReceived:0.#}").ToString();
        }

        /// <summary>
        /// 输出一次生命分类的真实击杀、获得进度和经验。
        /// </summary>
        /// <param name="text">统计说明缓冲区。</param>
        /// <param name="name">分类中文名称。</param>
        /// <param name="life">已经记录的真实收益。</param>
        private static void AppendLife(StringBuilder text, string name, ProjectEnemyLifeStatistics life) =>
            text.Append(name).Append(" 击杀 ").Append(life.Kills).Append(" · 进度 ").Append(life.Progress)
                .Append(" · 经验 ").Append(life.Experience).Append('\n');

        /// <summary>
        /// 返回房间类型的显示名称。
        /// </summary>
        /// <param name="kind">房间类型。</param>
        /// <returns>对应的中文名称。</returns>
        private static string RoomKindName(ExpeditionRoomKind kind) => kind == ExpeditionRoomKind.Combat ? "怪物房" :
            kind == ExpeditionRoomKind.Npc ? "NPC 房" : kind == ExpeditionRoomKind.Chest ? "宝箱房" : "最终 Boss";

        /// <summary>
        /// 校验测量数值为有限非负数。
        /// </summary>
        /// <param name="value">待校验数值。</param>
        /// <returns>数值是否有效。</returns>
        private static bool IsFiniteNonnegative(double value) => !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0;
    }

    /// <summary>
    /// 已结算物品的回收估值及入场快照对比。
    /// </summary>
    public readonly struct ProjectRunEconomySummary
    {
        public long RetainedSellValue { get; }
        public long LostSellValue { get; }
        public long EntrySellValue { get; }
        public long NetAssetValue { get; }
        public bool HasEntrySnapshot { get; }

        /// <summary>
        /// 创建只读收益明细。
        /// </summary>
        /// <param name="retainedSellValue">保留物品回收估值。</param>
        /// <param name="lostSellValue">丢失物品回收估值。</param>
        /// <param name="entrySellValue">入场物品回收估值。</param>
        /// <param name="netAssetValue">扣除入场估值及金币支出的净资产变化。</param>
        /// <param name="hasEntrySnapshot">是否记录入场快照。</param>
        public ProjectRunEconomySummary(long retainedSellValue, long lostSellValue, long entrySellValue, long netAssetValue, bool hasEntrySnapshot)
        {
            RetainedSellValue = retainedSellValue;
            LostSellValue = lostSellValue;
            EntrySellValue = entrySellValue;
            NetAssetValue = netAssetValue;
            HasEntrySnapshot = hasEntrySnapshot;
        }
    }

    /// <summary>
    /// 依据冻结结果计算只读回收估值。
    /// </summary>
    public static class ProjectRunEconomy
    {
        /// <summary>
        /// 计算保留、丢失和净资产估值，不修改钱包或结算。
        /// </summary>
        /// <param name="result">已结算结果。</param>
        /// <returns>收益明细；旧记录没有可用的净资产对比。</returns>
        public static ProjectRunEconomySummary Calculate(ProjectRunResult result)
        {
            long retained = SumSellValue(result?.RetainedItems);
            long lost = SumSellValue(result?.LostItems);
            bool hasEntry = result?.Outcome?.Statistics?.HasData == true;
            long entry = hasEntry ? result.Outcome.Statistics.EntryItemSellValue : 0;
            long net = hasEntry ? retained - entry - result.Outcome.GoldSpent : 0;
            return new ProjectRunEconomySummary(retained, lost, entry, net, hasEntry);
        }

        /// <summary>
        /// 求冻结物品的回收估值总和。
        /// </summary>
        /// <param name="items">冻结物品集合。</param>
        /// <returns>回收估值总和。</returns>
        private static long SumSellValue(IEnumerable<ProjectItem> items)
        {
            long total = 0;
            if (items == null) return total;
            foreach (ProjectItem item in items) if (item != null) total += item.SellGold;
            return total;
        }
    }
}
