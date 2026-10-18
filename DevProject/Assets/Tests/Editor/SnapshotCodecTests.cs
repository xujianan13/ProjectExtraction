using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GameFramework;
using GameLogic.Gameplay.Expedition;
using GameLogic.Gameplay.Level;
using GameLogic.Project;
using NUnit.Framework;
using ProjectExtraction.Tests.Shared;
using UnityEngine;

namespace ProjectExtraction.Tests
{
    /// <summary>
    /// 验证 V3 单一业务检查点协议和固定装备、关卡背包的归属边界。
    /// </summary>
    public sealed class SnapshotCodecTests
    {
        /// <summary>
        /// 加载与运行时一致的配置表。
        /// </summary>
        [SetUp]
        public void LoadConfiguration() => TestGameTables.LoadAll();

        /// <summary>
        /// 编码只产生业务检查点组件，往返保持值相同且不共享物品引用。
        /// </summary>
        [Test]
        public void RoundTripPreservesIndependentBusinessCheckpoint()
        {
            ExpeditionRunSnapshot original = Snapshot();
            original.ElapsedTicks = 35;
            Assert.That(ExpeditionSnapshotCodec.TryEncode(original, out var components, out string error), Is.True, error);
            Assert.That(components.Select(value => value.Key), Is.EqualTo(new[] { "run.checkpoint.v3" }));
            Assert.That(ExpeditionSnapshotCodec.TryDecode(components, out var restored, out error), Is.True, error);
            Assert.That(JsonUtility.ToJson(restored), Is.EqualTo(JsonUtility.ToJson(original)));
            restored.EntryEquipment[0].Name = "改变副本";
            Assert.That(original.EntryEquipment[0].Name, Is.Not.EqualTo(restored.EntryEquipment[0].Name));
            StringAssert.DoesNotContain("\"World\"", components[0].Value);
            StringAssert.DoesNotContain("\"Drops\"", components[0].Value);
        }

        /// <summary>
        /// 缺失、重复或旧世界组件不冒充 V3 业务检查点。
        /// </summary>
        [Test]
        public void MissingDuplicateAndOldWorldComponentsAreRejected()
        {
            Assert.That(ExpeditionSnapshotCodec.TryEncode(Snapshot(), out var components, out _), Is.True);
            Assert.That(ExpeditionSnapshotCodec.TryDecode(new List<SaveStateComponent>(), out _, out _), Is.False);
            Assert.That(ExpeditionSnapshotCodec.TryDecode(new[] { components[0], components[0] }, out _, out _), Is.False);
            Assert.That(ExpeditionSnapshotCodec.TryDecode(new[] { new SaveStateComponent { Key = "run", Value = components[0].Value } }, out _, out _), Is.False);
            components[0].Value = "{}";
            Assert.That(ExpeditionSnapshotCodec.TryDecode(components, out var rejected, out string error), Is.False);
            Assert.That(rejected, Is.Null);
            Assert.That(error, Is.Not.Empty);
        }

        /// <summary>
        /// 旧版本、缺少玩家或缺失必要集合在编码边界明确失败。
        /// </summary>
        [Test]
        public void InvalidVersionAndMissingRequiredStateAreRejected()
        {
            ExpeditionRunSnapshot snapshot = Snapshot();
            snapshot.Version = 2;
            Assert.That(ExpeditionSnapshotCodec.TryEncode(snapshot, out _, out _), Is.False);
            snapshot.Version = 3;
            snapshot.PlayerState = null;
            Assert.That(ExpeditionSnapshotCodec.TryEncode(snapshot, out _, out _), Is.False);
            snapshot = Snapshot();
            snapshot.Inventory = null;
            Assert.That(ExpeditionSnapshotCodec.TryEncode(snapshot, out _, out _), Is.False);
            snapshot = Snapshot();
            snapshot.HasPendingOutcome = true;
            Assert.That(ExpeditionSnapshotCodec.TryEncode(snapshot, out _, out _), Is.False);
        }

        /// <summary>
        /// 固定出战装备不能重复出现在关卡背包，背包物品不能带穿戴状态。
        /// </summary>
        [Test]
        public void OwnershipRejectsDuplicatedEntryItemAndEquippedBagItem()
        {
            ExpeditionRunSnapshot snapshot = Snapshot();
            Assert.That(ValidItems(snapshot), Is.True);
            ProjectItem duplicate = snapshot.EntryEquipment[0].Copy();
            duplicate.Equipped = false;
            snapshot.Inventory.Add(duplicate);
            Assert.That(ValidItems(snapshot), Is.False);
            duplicate.Id = "codec-bag-item";
            Assert.That(ValidItems(snapshot), Is.True);
            duplicate.Equipped = true;
            Assert.That(ValidItems(snapshot), Is.False);
        }

        /// <summary>
        /// 冻结宝箱物品入包必须同时具有领取事实，不能让未领取候选与背包重复拥有。
        /// </summary>
        [Test]
        public void ChestOwnershipRequiresMatchingClaimFact()
        {
            ExpeditionRunSnapshot snapshot = Snapshot();
            RegionGenerationRequest request = snapshot.Requests.First(value => value.RoomKind == ExpeditionRoomKind.Chest);
            Assert.That(LevelContentConfig.TryCreateRoomMap(request.MapId, out var map, out string error), Is.True, error);
            Assert.That(RegionContentGenerator.TryGenerate(request, map, LevelContentConfig.CreateRegionContent(request), out var plan, out error), Is.True, error);
            Assert.That(RegionRuntimeState.TryCreate(plan, out var room, out error), Is.True, error);
            snapshot.Rooms.Add(ExpeditionRoomCheckpoint.Capture(room));
            ProjectItem item = snapshot.Rooms[0].Plan.Chests[0].Items[0].Copy();
            snapshot.Inventory.Add(item);
            Assert.That(ValidItems(snapshot), Is.False);
            snapshot.Rooms[0].Chests[0].Opened = true;
            snapshot.Rooms[0].Chests[0].ClaimedItemIds.Add(item.Id);
            Assert.That(ValidItems(snapshot), Is.True);
        }

        /// <summary>
        /// 由真实玩家和关卡配置创建尚未绑定场景的业务检查点。
        /// </summary>
        /// <returns>完整且独立的检查点候选。</returns>
        private static ExpeditionRunSnapshot Snapshot()
        {
            var session = (ProjectSession)Activator.CreateInstance(typeof(ProjectSession), BindingFlags.Instance | BindingFlags.NonPublic,
                null, new object[] { null, ProjectSaveData.CreateDefault() }, null);
            Assert.That(ExpeditionRun.TryCreate(session, "codec", 1, 42, new List<ProjectItem>(session.Warehouse), out var run), Is.True);
            using (run)
            {
                Assert.That(run.TryCaptureState(out var snapshot, out string error), Is.True, error);
                return snapshot;
            }
        }

        /// <summary>
        /// 单独验证物品归属，不为损坏候选建立场景和表现资源。
        /// </summary>
        /// <param name="snapshot">待验证的业务检查点。</param>
        /// <returns>物品归属有效时为真。</returns>
        private static bool ValidItems(ExpeditionRunSnapshot snapshot) => (bool)typeof(ExpeditionRun)
            .GetMethod("ValidateSnapshotItems", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { snapshot, null });
    }
}
