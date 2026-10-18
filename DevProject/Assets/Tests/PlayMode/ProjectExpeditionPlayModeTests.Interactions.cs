using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using GameFramework;
using GameLogic.Gameplay.Combat;
using GameLogic.Gameplay.Expedition;
using GameLogic.Project;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MiniGame.Tests
{
    public sealed partial class ProjectExpeditionPlayModeTests
    {
        /// <summary>
        /// 多个物品和商人重叠时，一次请求只处理明确选中的实例。
        /// </summary>
        /// <returns>目标切换、领取和保存验证任务。</returns>
        [UnityTest]
        public IEnumerator InteractionTargets_CycleAndCollectOnlySelectedLoot() => UniTask.ToCoroutine(async () =>
        {
            await SetupAsync(new MemorySaveBackend());
            Assert.That(await ProjectServices.Coordinator.StartRunAsync(1), Is.True);
            ExpeditionRun run = ProjectServices.Session.CurrentRun;
            await AdvanceToRoomAsync(run, GameLogic.Gameplay.Level.ExpeditionRoomKind.Npc);
            Vector3 position = run.CurrentRoom.Plan.Npcs.First().Position;
            Assert.That(run.World.TrySetPosition(run.Player, position), Is.True);
            ExpeditionDrop first = AddInteractionDrop(run, "interaction-a", position);
            ExpeditionDrop second = AddInteractionDrop(run, "interaction-b", position + Vector3.right * 0.4f);
            run.Advance(CombatWorld.TickSeconds);
            Assert.That(run.InteractionCount, Is.EqualTo(3));
            SelectInteractionLoot(run, first.Item.Id);
            Assert.That(run.TryGetInteractionItem(out ProjectItem preview), Is.True);
            preview.Name = "mutated-preview";
            Assert.That(first.Item.Name, Is.Not.EqualTo(preview.Name));
            SelectInteractionLoot(run, second.Item.Id);
            run.RequestInteraction();
            run.Advance(CombatWorld.TickSeconds);
            await WaitRunSavedAsync(run);
            Assert.That(run.Inventory.Count(value => value.Id == second.Item.Id), Is.EqualTo(1));
            Assert.That(first.Collected, Is.False);
            Assert.That(second.Collected, Is.True);
            run.RequestInteraction();
            run.SetPaused(true);
            run.SetPaused(false);
            run.Advance(CombatWorld.TickSeconds);
            Assert.That(first.Collected, Is.False);
        });

        /// <summary>
        /// 已锁定物品离开范围后，不把失效请求改成打开相邻商店。
        /// </summary>
        /// <returns>具体交互身份失效测试任务。</returns>
        [UnityTest]
        public IEnumerator InteractionTargets_InvalidatedRequestDoesNotOpenAdjacentMerchant() => UniTask.ToCoroutine(async () =>
        {
            await SetupAsync(new MemorySaveBackend());
            Assert.That(await ProjectServices.Coordinator.StartRunAsync(1), Is.True);
            ExpeditionRun run = ProjectServices.Session.CurrentRun;
            await AdvanceToRoomAsync(run, GameLogic.Gameplay.Level.ExpeditionRoomKind.Npc);
            Vector3 position = run.CurrentRoom.Plan.Npcs.First().Position;
            Assert.That(run.World.TrySetPosition(run.Player, position), Is.True);
            ExpeditionDrop drop = AddInteractionDrop(run, "invalidated-loot", position);
            run.Advance(CombatWorld.TickSeconds);
            SelectInteractionLoot(run, drop.Item.Id);
            run.RequestInteraction();
            drop.Position = run.CurrentRoom.Plan.EntryPosition;
            run.Advance(CombatWorld.TickSeconds);
            Assert.That(drop.Collected, Is.False);
            Assert.That(run.ActivePanel, Is.EqualTo(ExpeditionPanelKind.None));
            Assert.That(run.Message, Does.Contain("重新选择"));
            run.RequestInteraction();
            run.Advance(CombatWorld.TickSeconds);
            await WaitRunSavedAsync(run);
            Assert.That(run.ActivePanel, Is.EqualTo(ExpeditionPanelKind.Merchant));
        });

        /// <summary>
        /// 用测试固定物品创建两个重叠目标，物品仍经过正式领取入口。
        /// </summary>
        /// <param name="run">正在运行的关卡。</param>
        /// <param name="id">测试物品身份。</param>
        /// <param name="position">物品位置。</param>
        /// <returns>可验证领取状态的掉落。</returns>
        private static ExpeditionDrop AddInteractionDrop(ExpeditionRun run, string id, Vector3 position)
        {
            ProjectItem item = ProjectEquipmentRules.CreateLoot(new System.Random(42), 1);
            item.Id = id;
            var drop = new ExpeditionDrop { RoomId = run.CurrentRoom.Plan.Request.RegionId, Position = position, Item = item };
            ((List<ExpeditionDrop>)run.Drops).Add(drop);
            return drop;
        }

        /// <summary>
        /// 使用公开切换动作选中指定物品。
        /// </summary>
        /// <param name="run">正在运行的关卡。</param>
        /// <param name="itemId">目标物品身份。</param>
        private static void SelectInteractionLoot(ExpeditionRun run, string itemId)
        {
            for (int index = 0; index < run.InteractionCount && run.SelectedLootId != itemId; index++) run.CycleInteraction();
            Assert.That(run.SelectedLootId, Is.EqualTo(itemId));
        }
    }
}
