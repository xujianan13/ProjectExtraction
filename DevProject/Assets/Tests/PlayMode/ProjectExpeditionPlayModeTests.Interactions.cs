using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using GameFramework;
using GameLogic.Gameplay.Combat;
using GameLogic.Gameplay.Expedition;
using GameLogic.Gameplay.Level;
using GameLogic.Project;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MiniGame.Tests
{
    public sealed partial class ProjectExpeditionPlayModeTests
    {
        /// <summary>
        /// 多个掉落与商店重叠时保持选定物品，预览隔离且一次请求只领取一件。
        /// </summary>
        /// <returns>加载真实资源并推进交互的逐帧测试。</returns>
        [UnityTest]
        public IEnumerator InteractionTargets_CycleAndPreviewThenCollectOnlySelectedLoot() => UniTask.ToCoroutine(async () =>
        {
            await SetupAsync(new MemorySaveBackend());
            using var scope = new LifetimeScope("InteractionSelection");
            ExpeditionVisualAssets assets = await ExpeditionVisualAssets.LoadAsync(scope, GameEntry.Get<IResourceModule>());
            using ExpeditionRun run = CreateInteractionRun(assets, 1);
            LevelRoomState room = run.Maps.Rooms.Single(value => value.Type == LevelRoomType.Npc);
            MoveToRoom(run, room.RoomId);
            var layout = room.Plan.Definition.Layout;
            Vector3 position = new Vector3((float)layout.NpcX, 0, (float)layout.NpcZ);
            Assert.That(run.World.TrySetPosition(run.Player, position), Is.True);
            ExpeditionDrop first = AddInteractionDrop(run, "interaction-a", position);
            ExpeditionDrop second = AddInteractionDrop(run, "interaction-b", position + Vector3.right * 0.4f);
            run.Advance(CombatWorld.TickSeconds);
            Assert.That(run.InteractionCount, Is.EqualTo(3));
            Assert.That(run.SelectedLootId, Is.EqualTo(first.Item.Id));
            Assert.That(run.TryGetInteractionItem(out ProjectItem preview), Is.True);
            preview.Name = "modified-preview";
            Assert.That(first.Item.Name, Is.Not.EqualTo(preview.Name));
            SelectInteractionLoot(run, second.Item.Id);
            run.Advance(CombatWorld.TickSeconds);
            Assert.That(run.SelectedLootId, Is.EqualTo(second.Item.Id), "仍在范围内时不因另一件更近而跳选。");
            run.RequestInteraction();
            run.Advance(CombatWorld.TickSeconds * 3);
            Assert.That(run.Inventory.Select(value => value.Id), Is.EquivalentTo(new[] { second.Item.Id }));
            Assert.That(first.Collected, Is.False);
            Assert.That(second.Collected, Is.True);
            Assert.That(run.SelectedLootId, Is.EqualTo(first.Item.Id));
            run.RequestInteraction();
            run.SetPaused(true);
            run.SetPaused(false);
            run.Advance(CombatWorld.TickSeconds);
            Assert.That(first.Collected, Is.False, "暂停须清除先前尚未执行的拾取请求。");
        });

        /// <summary>
        /// 请求后的目标失效不能转为进入同一位置的出口，重新明确请求才允许换图。
        /// </summary>
        /// <returns>加载真实出口并推进交互的逐帧测试。</returns>
        [UnityTest]
        public IEnumerator InteractionTargets_InvalidatedLootRequestDoesNotEnterNearbyExit() => UniTask.ToCoroutine(async () =>
        {
            await SetupAsync(new MemorySaveBackend());
            using var scope = new LifetimeScope("InteractionInvalidation");
            ExpeditionVisualAssets assets = await ExpeditionVisualAssets.LoadAsync(scope, GameEntry.Get<IResourceModule>());
            using ExpeditionRun run = CreateInteractionRun(assets, 1);
            RoomDirection direction = run.CurrentRoom.Exits.Keys.First();
            var layout = run.CurrentRoom.Plan.Definition.Layout;
            float edge = (float)(layout.HalfExtent - layout.ExitCenterInset);
            Vector3 position = direction == RoomDirection.North ? new Vector3(0, 0, edge) :
                direction == RoomDirection.South ? new Vector3(0, 0, -edge) :
                direction == RoomDirection.East ? new Vector3(edge, 0, 0) : new Vector3(-edge, 0, 0);
            Assert.That(run.World.TrySetPosition(run.Player, position), Is.True);
            ExpeditionDrop drop = AddInteractionDrop(run, "exit-overlap", position);
            run.Advance(CombatWorld.TickSeconds);
            SelectInteractionLoot(run, drop.Item.Id);
            run.RequestInteraction();
            int current = run.CurrentRoom.RoomId;
            drop.Position = Vector3.zero;
            run.Advance(CombatWorld.TickSeconds);
            Assert.That(run.CurrentRoom.RoomId, Is.EqualTo(current));
            Assert.That(drop.Collected, Is.False);
            Assert.That(run.Message, Does.Contain("重新选择"));
            Assert.That(run.InteractionDescription, Does.Contain("未探索"));
            Assert.That(run.Maps.TryGetDestination(direction, out int destination), Is.True);
            run.RequestInteraction();
            run.Advance(CombatWorld.TickSeconds);
            Assert.That(run.CurrentRoom.RoomId, Is.EqualTo(destination));
            Assert.That(run.HasInteraction, Is.False, "换房不能保留旧出口或物品目标。");
        });

        /// <summary>
        /// 商店和宝箱均使用关卡独立的掉落等级，高关物品不再取关卡编号作为等级。
        /// </summary>
        /// <returns>加载真实商品及宝箱的逐帧测试。</returns>
        [UnityTest]
        public IEnumerator RewardLootLevel_ReachesMerchantAndChest() => UniTask.ToCoroutine(async () =>
        {
            await SetupAsync(new MemorySaveBackend());
            using var scope = new LifetimeScope("RewardLootLevel");
            ExpeditionVisualAssets assets = await ExpeditionVisualAssets.LoadAsync(scope, GameEntry.Get<IResourceModule>());
            using ExpeditionRun run = CreateInteractionRun(assets, 3);
            Assert.That(run.LootLevel, Is.EqualTo(6));
            LevelRoomState merchant = run.Maps.Rooms.Single(value => value.Type == LevelRoomType.Npc);
            MoveToRoom(run, merchant.RoomId);
            var layout = merchant.Plan.Definition.Layout;
            Assert.That(run.World.TrySetPosition(run.Player, new Vector3((float)layout.NpcX, 0, (float)layout.NpcZ)), Is.True);
            run.RequestInteraction();
            run.Advance(CombatWorld.TickSeconds);
            Assert.That(run.ActivePanel, Is.EqualTo(ExpeditionPanelKind.Merchant));
            Assert.That(run.MerchantOffers.All(value => value.Item.RequiredLevel == 6), Is.True);
            run.ClosePanel(ExpeditionPanelKind.Merchant);
            LevelRoomState chest = run.Maps.Rooms.First(value => value.Type == LevelRoomType.Loot);
            MoveToRoom(run, chest.RoomId);
            layout = chest.Plan.Definition.Layout;
            Assert.That(run.World.TrySetPosition(run.Player, new Vector3((float)layout.ChestX, 0, (float)layout.ChestZ)), Is.True);
            run.RequestInteraction();
            run.Advance(CombatWorld.TickSeconds);
            Assert.That(run.Inventory.Count, Is.EqualTo(1));
            Assert.That(run.Inventory[0].RequiredLevel, Is.EqualTo(6));
        });

        /// <summary>
        /// 创建使用真实地图和资源、由测试独立推进的单局。
        /// </summary>
        /// <param name="assets">本次测试已加载的资源。</param>
        /// <param name="levelId">关卡编号。</param>
        /// <returns>已激活且由调用方释放的单局。</returns>
        private static ExpeditionRun CreateInteractionRun(ExpeditionVisualAssets assets, int levelId)
        {
            Assert.That(ExpeditionRun.TryCreate(ProjectServices.Session, "interaction-" + levelId, levelId, 42,
                new List<ProjectItem>(), out ExpeditionRun run), Is.True);
            run.AttachScene(assets);
            Assert.That(run.MarkReady() && run.Activate(), Is.True);
            return run;
        }

        /// <summary>
        /// 为交互边界测试放入一个固定身份的掉落，正式游戏仍只由战斗事件生成。
        /// </summary>
        /// <param name="run">被测单局。</param>
        /// <param name="id">物品稳定身份。</param>
        /// <param name="position">测试掉落位置。</param>
        /// <returns>可检查领取状态的测试记录。</returns>
        private static ExpeditionDrop AddInteractionDrop(ExpeditionRun run, string id, Vector3 position)
        {
            ProjectItem item = ProjectEquipmentRules.CreateLoot(new System.Random(42), 1);
            item.Id = id;
            var drop = new ExpeditionDrop { RoomId = run.CurrentRoom.RoomId, Position = position, Item = item };
            ((List<ExpeditionDrop>)run.Drops).Add(drop);
            return drop;
        }

        /// <summary>
        /// 使用公开切换入口选中指定测试物品。
        /// </summary>
        /// <param name="run">被测单局。</param>
        /// <param name="itemId">物品身份。</param>
        private static void SelectInteractionLoot(ExpeditionRun run, string itemId)
        {
            for (int index = 0; index < run.InteractionCount && run.SelectedLootId != itemId; index++) run.CycleInteraction();
            Assert.That(run.SelectedLootId, Is.EqualTo(itemId));
        }
    }
}
