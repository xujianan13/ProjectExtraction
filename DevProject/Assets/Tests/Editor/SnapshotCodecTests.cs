using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GameFramework;
using GameLogic.Gameplay.Combat;
using GameLogic.Gameplay.Expedition;
using GameLogic.Gameplay.Level;
using GameLogic.Gameplay.Roguelike;
using GameLogic.Project;
using NUnit.Framework;
using UnityEngine;

namespace ProjectExtraction.Tests
{
    /// <summary>
    /// 检查快照组件完整性、恢复顺序和外部损坏输入的拒绝边界。
    /// </summary>
    public sealed class SnapshotCodecTests
    {
        /// <summary>
        /// 组件按键排序后仍按冻结计划重组槽位，并保持下一次编码完全一致。
        /// </summary>
        [Test]
        public void RoundTripPreservesPlanSlotOrderAndIndependentComponents()
        {
            ExpeditionRunSnapshot original = CodecSnapshot();
            Assert.That(ExpeditionSnapshotCodec.TryEncode(original, out var components, out string error), Is.True, error);
            Assert.That(ExpeditionSnapshotCodec.TryDecode(components, out var restored, out error), Is.True, error);
            Assert.That(restored.Regions[0].Slots.Select(value => value.SlotId), Is.EqualTo(new[] { "z-last-key", "a-first-key" }));
            Assert.That(restored.PendingOutcome, Is.Null);
            Assert.That(ExpeditionSnapshotCodec.TryEncode(restored, out var second, out error), Is.True, error);
            Assert.That(second.Select(value => value.Key + "=" + value.Value), Is.EqualTo(components.Select(value => value.Key + "=" + value.Value)));
            restored.Regions[0].Slots[0].LifeSequence = 9;
            Assert.That(original.Regions[0].Slots[0].LifeSequence, Is.EqualTo(1));
        }

        /// <summary>
        /// 缺少任何头、区域或瞬态战斗组件都不能恢复为完整世界。
        /// </summary>
        /// <param name="key">需要从完整修订删除的组件。</param>
        [TestCase("run")]
        [TestCase("world.actor.1")]
        [TestCase("world.cast.2")]
        [TestCase("region.1.slot.z-last-key")]
        [TestCase("region.1.state")]
        [TestCase("manifest")]
        public void MissingComponentReturnsFalse(string key)
        {
            Assert.That(ExpeditionSnapshotCodec.TryEncode(CodecSnapshot(), out var components, out _), Is.True);
            Assert.That(components.RemoveAll(value => value.Key == key), Is.EqualTo(1));
            Assert.That(ExpeditionSnapshotCodec.TryDecode(components, out var rejected, out string error), Is.False);
            Assert.That(rejected, Is.Null);
            Assert.That(error, Is.Not.Empty);
        }

        /// <summary>
        /// 保留组件键却替换内部角色身份时必须明确拒绝。
        /// </summary>
        [Test]
        public void ComponentKeyMustMatchSerializedIdentity()
        {
            Assert.That(ExpeditionSnapshotCodec.TryEncode(CodecSnapshot(), out var components, out _), Is.True);
            SaveStateComponent actor = components.Single(value => value.Key == "world.actor.1");
            CombatActorState changed = JsonUtility.FromJson<CombatActorState>(actor.Value);
            changed.Key = 7;
            actor.Value = JsonUtility.ToJson(changed);
            Assert.That(ExpeditionSnapshotCodec.TryDecode(components, out _, out _), Is.False);
            actor.Value = "{}";
            Assert.That(ExpeditionSnapshotCodec.TryDecode(components, out _, out _), Is.False);
        }

        /// <summary>
        /// 空元素、重复全局实体键和活动地图玩家不一致均在编码入口拒绝。
        /// </summary>
        [Test]
        public void MalformedWorldAndRegionCollectionsReturnFalse()
        {
            ExpeditionRunSnapshot snapshot = CodecSnapshot();
            snapshot.World.Actors.Add(null);
            Assert.That(ExpeditionSnapshotCodec.TryEncode(snapshot, out _, out _), Is.False);
            snapshot.World.Actors.RemoveAt(1);
            snapshot.World.Casts[0].Key = 1;
            Assert.That(ExpeditionSnapshotCodec.TryEncode(snapshot, out _, out _), Is.False);
            snapshot.World.Casts[0].Key = 2;
            snapshot.World.ActiveRoomId = 2;
            Assert.That(ExpeditionSnapshotCodec.TryEncode(snapshot, out _, out _), Is.False);
            snapshot.World.ActiveRoomId = 1;
            snapshot.Regions[0].Slots[0] = null;
            Assert.That(ExpeditionSnapshotCodec.TryEncode(snapshot, out _, out _), Is.False);
            snapshot.Regions[0].Slots[0] = new RegionSpawnSlotState { SlotId = "unknown" };
            Assert.That(ExpeditionSnapshotCodec.TryEncode(snapshot, out _, out _), Is.False);
        }

        /// <summary>
        /// 直接 JSON 往返产生的可选对象空壳不会变成终局，也不改变真实准入快照。
        /// </summary>
        [Test]
        public void AdmissionSnapshotRestoresWithOptionalInlineShells()
        {
            ProjectSession session = Session();
            Assert.That(ExpeditionRun.TryCreate(session, "codec-baseline", 1, 814,
                new List<ProjectItem>(session.Warehouse), out ExpeditionRun original), Is.True);
            using (original)
            {
                Assert.That(original.TryCaptureState(out var snapshot, out string error), Is.True, error);
                ExpeditionRunSnapshot cold = JsonUtility.FromJson<ExpeditionRunSnapshot>(JsonUtility.ToJson(snapshot));
                Assert.That(ExpeditionRun.TryRestore(session, cold, out var restored, out error), Is.True, error);
                using (restored)
                {
                    Assert.That(restored.TryCaptureState(out var recaptured, out error), Is.True, error);
                    Assert.That(JsonUtility.ToJson(recaptured), Is.EqualTo(JsonUtility.ToJson(snapshot)));
                }
                cold.World.Actors.Add(null);
                Assert.That(ExpeditionRun.TryRestore(session, cold, out _, out error), Is.False);
                cold.World.Actors.RemoveAt(cold.World.Actors.Count - 1);
                cold.World.ActiveRoomId = 999;
                Assert.That(ExpeditionRun.TryRestore(session, cold, out _, out error), Is.False);
            }
        }

        /// <summary>
        /// 未领取物品不可同时在背包，领取记录必须来自冻结候选且不可重复。
        /// </summary>
        [Test]
        public void SnapshotOwnershipRejectsDuplicateOriginsAndForgedClaims()
        {
            ExpeditionRunSnapshot snapshot = OwnershipSnapshot();
            Assert.That(ValidItems(snapshot), Is.True);
            ProjectItem item = snapshot.Regions[0].Plan.Chests[0].Items[0];
            snapshot.Inventory.Add(item.Copy());
            Assert.That(ValidItems(snapshot), Is.False);
            snapshot.Regions[0].Chests[0].Opened = true;
            snapshot.Regions[0].Chests[0].ClaimedItemIds.Add(item.Id);
            Assert.That(ValidItems(snapshot), Is.True);
            snapshot.Regions[0].Chests[0].ClaimedItemIds.Add(item.Id);
            Assert.That(ValidItems(snapshot), Is.False);
            snapshot.Regions[0].Chests[0].ClaimedItemIds.RemoveAt(1);
            snapshot.Regions[0].Chests[0].ClaimedItemIds[0] = "foreign-item";
            Assert.That(ValidItems(snapshot), Is.False);
            snapshot.Regions[0].Chests[0].ClaimedItemIds[0] = item.Id;
            snapshot.Drops.Add(new ExpeditionDrop { RoomId = 1, Item = item.Copy(), Collected = true });
            Assert.That(ValidItems(snapshot), Is.False);
            snapshot.Drops.Clear();
            snapshot.Regions[0].Chests[0] = null;
            Assert.That(ValidItems(snapshot), Is.False);
        }

        /// <summary>
        /// 创建只用于组件格式验证的带无序槽位快照。
        /// </summary>
        /// <returns>完全独立的组件格式候选。</returns>
        private static ExpeditionRunSnapshot CodecSnapshot()
        {
            var request = new RegionGenerationRequest { RunId = "codec", RegionId = 1, RegionIndex = 0 };
            var region = new RegionRuntimeState { Plan = new RegionPlan { Request = request.Copy() } };
            foreach (string id in new[] { "z-last-key", "a-first-key" })
            {
                region.Plan.SpawnSlots.Add(new RegionSpawnSlotPlan { SlotId = id });
                region.Slots.Add(new RegionSpawnSlotState { SlotId = id });
            }
            var world = new CombatWorldSnapshot { ActiveRoomId = 1, PlayerKey = 1 };
            world.Rooms.Add(new CombatRoomState { RoomId = 1 });
            world.Actors.Add(new CombatActorState { Key = 1, Spec = new CombatActorSpec { ActorId = 1, RoomId = 1, IsPlayer = true } });
            world.Casts.Add(new CombatCastState { Key = 2 });
            return new ExpeditionRunSnapshot { RunId = "codec", Requests = new List<RegionGenerationRequest> { request },
                Regions = new List<RegionRuntimeState> { region }, World = world,
                Rogue = new RogueChoiceState { RunId = "codec", State = new PersistentRandom(7).State, Policy = new RogueChoicePolicy() },
                Progress = new RogueProgressState { Threshold = 20 }, BossLoot = new BossLootChoiceState { RunId = "codec" },
                Statistics = new ProjectRunStatistics() };
        }

        /// <summary>
        /// 创建真实字段完整且带一个冻结宝箱的物品校验快照。
        /// </summary>
        /// <returns>可供纯归属校验的完整物品候选。</returns>
        private static ExpeditionRunSnapshot OwnershipSnapshot()
        {
            ExpeditionRunSnapshot snapshot = CodecSnapshot();
            RegionPlan plan = snapshot.Regions[0].Plan;
            plan.Request.LevelId = 1;
            plan.Request.LootLevel = 1;
            plan.Request.EnemyLevel = 1;
            plan.Request.BossDefinitionId = 1;
            plan.Request.BossEnemyId = 1;
            plan.Request.BossEnemyLevel = 1;
            plan.Request.BossAnchorId = "boss";
            plan.Request.MapId = 1;
            plan.Request.MapVersion = "test";
            plan.Request.ConfigVersion = "test";
            plan.EntryAnchorId = "entry";
            plan.ExitAnchorId = "exit";
            plan.PlayerRadius = 0.4f;
            plan.InteractionRadius = 1;
            plan.NormalRespawn = new RegionRespawnRule { DelayTicks = 20, SafeDistance = 2, WarningTicks = 2 };
            plan.EliteRespawn = new RegionRespawnRule { DelayTicks = 40, SafeDistance = 2, WarningTicks = 2 };
            plan.MonsterRandomState = plan.EliteRandomState = plan.ChestRandomState = plan.NpcRandomState = new PersistentRandom(7).State;
            plan.SpawnSlots.Clear();
            plan.SpawnSlots.Add(new RegionSpawnSlotPlan { SlotId = "boss", GroupId = "boss", EncounterId = 1, EnemyId = 1,
                EnemyLevel = 1, Kind = RegionEnemyKind.Boss, AnchorId = "boss", Radius = 0.4f });
            ProjectItem item = ProjectV2RewardConfig.CreateBossCandidates(new PersistentRandom(42), 1, 1, "batch", 1, new HashSet<string>())[0];
            plan.Chests.Add(new RegionChestPlan { Id = "chest", DefinitionId = 1, AnchorId = "chest", Items = new List<ProjectItem> { item } });
            snapshot.Regions[0].Chests.Add(new RegionChestState { Id = "chest" });
            snapshot.BossLoot = new GameLogic.Gameplay.Roguelike.BossLootChoiceState();
            Assert.That(plan.TryValidateStructure(out string error), Is.True, error);
            return snapshot;
        }

        /// <summary>
        /// 只调用局内物品边界，避免为损坏归属测试构造表现和战斗资源。
        /// </summary>
        /// <param name="snapshot">需要验证的冻结候选。</param>
        /// <returns>物品归属校验结果。</returns>
        private static bool ValidItems(ExpeditionRunSnapshot snapshot) => (bool)typeof(ExpeditionRun)
            .GetMethod("ValidateSnapshotItems", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { snapshot, null });

        /// <summary>
        /// 创建不连接物理后端的真实账号会话。
        /// </summary>
        /// <returns>只供恢复入口读取角色属性的会话。</returns>
        private static ProjectSession Session() => (ProjectSession)Activator.CreateInstance(typeof(ProjectSession),
            BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { null, ProjectSaveData.CreateDefault() }, null);
    }
}
