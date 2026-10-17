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
    /// <summary>
    /// 验证不同武器进入正式准入关卡后的属性与范围。
    /// </summary>
    public sealed partial class ProjectExpeditionPlayModeTests
    {


        /// <summary>
        /// 实际换装替换对应普攻入口与预警范围，同时守卫刀的生命和防御进入 ECS。
        /// </summary>
        /// <returns>真实单局战斗范围与装备属性验证流程。</returns>
        [UnityTest]
        public IEnumerator ExpandedWeapons_UpdateBasicSkillRangeAndGuardStats() => UniTask.ToCoroutine(async () =>
        {
            await SetupAsync(new MemorySaveBackend());

            ProjectItem heavy = ProjectEquipmentRules.CreateConfiguredItem(new System.Random(24), 1,
                ProjectItemQuality.Common, "ruin_heavy_blade");
            ProjectItem pole = ProjectEquipmentRules.CreateConfiguredItem(new System.Random(24), 1,
                ProjectItemQuality.Common, "ruin_poleblade");
            ProjectItem guard = ProjectEquipmentRules.CreateConfiguredItem(new System.Random(24), 1,
                ProjectItemQuality.Common, "guard_blade");
            heavy.Equipped = true;
            ExpeditionRun run = await StartWithEquipmentFixturesAsync(heavy, new[] { pole, guard });
            Assert.That(ProjectContentConfig.Player.SkillID, Is.EqualTo(1));
            Assert.That(run.World.TryGetActor(run.Player, out CombatActorSnapshot before), Is.True);
            await AssertSkillRadius(run, 1f);
            Assert.That(run.TryOpenInventory(), Is.True);
            Assert.That(run.TryPreviewEquipment(pole, out ProjectCharacterStats current, out ProjectCharacterStats preview), Is.True);
            Assert.That(preview.AttackRange, Is.GreaterThan(current.AttackRange));
            Assert.That(run.TryEquip(pole.Id), Is.True);
            await WaitRunSavedAsync(run);
            run.ClosePanel(ExpeditionPanelKind.Inventory);
            await AssertSkillRadius(run, 1f);
            Assert.That(run.TryOpenInventory(), Is.True);
            Assert.That(run.TryEquip(guard.Id), Is.True);
            await WaitRunSavedAsync(run);
            run.ClosePanel(ExpeditionPanelKind.Inventory);
            Assert.That(run.World.TryGetActor(run.Player, out CombatActorSnapshot after), Is.True);
            Assert.That(after.MaxHealth, Is.GreaterThan(before.MaxHealth));
            Assert.That(after.Defense, Is.GreaterThan(before.Defense));
            await AssertSkillRadius(run, 1f);
            Assert.That(run.World.Faulted, Is.False);
        });

        /// <summary>
        /// 通过正式准入携带首件装备，其余定向测试物品由冻结宝箱真实领取。
        /// </summary>
        /// <param name="starter">唯一穿戴的初始武器。</param>
        /// <param name="fixtures">只用于本项测试的额外完整物品。</param>
        /// <returns>所有入场与领取保存完成的正式单局。</returns>
        private static async UniTask<ExpeditionRun> StartWithEquipmentFixturesAsync(ProjectItem starter, IReadOnlyList<ProjectItem> fixtures)
        {
            ProjectSaveData initial = ProjectSaveData.CreateDefault();
            initial.Warehouse.Clear();
            initial.Warehouse.Add(starter.Copy());
            Assert.That(await ProjectServices.Coordinator.CloseAsync(), Is.True);
            await SetupAsync(new MemorySaveBackend(), initial);
            Assert.That(await ProjectServices.Coordinator.StartRunAsync(1), Is.True);
            ExpeditionRun run = ProjectServices.Session.CurrentRun;
            RegionChestPlan chest = run.CurrentRegion.Plan.Chests.First();
            chest.Items = fixtures.Select(value => value.Copy()).ToList();
            Assert.That(run.World.TrySetPosition(run.Player, chest.Position), Is.True);
            run.Advance(CombatWorld.TickSeconds);
            await WaitRunSavedAsync(run);
            for (int attempt = 0; attempt < run.InteractionCount && run.SelectedInteractionId != chest.Id; attempt++) run.CycleInteraction();
            Assert.That(run.SelectedInteractionId, Is.EqualTo(chest.Id));
            run.RequestInteraction();
            run.Advance(CombatWorld.TickSeconds);
            await WaitRunSavedAsync(run);
            foreach (ProjectItem item in fixtures) Assert.That(run.Inventory.Count(value => value.Id == item.Id), Is.EqualTo(1));
            await WaitForWindowAsync(ProjectServices.ChestLootWindow);
            IUIModule ui = GameEntry.Get<IUIModule>();
            Assert.That(await ui.CloseWindowAsync(ProjectServices.ChestLootWindow), Is.True);
            await WaitForAsync(() => run.ActivePanel == ExpeditionPanelKind.None &&
                ui.OpenedWindows.All(window => window.Id != ProjectServices.ChestLootWindow));
            Assert.That(run.ChestLootItems, Is.Empty);
            Assert.That(run.World.TrySetPosition(run.Player, run.CurrentRegion.Plan.EntryPosition), Is.True);
            return run;
        }
    }
}
