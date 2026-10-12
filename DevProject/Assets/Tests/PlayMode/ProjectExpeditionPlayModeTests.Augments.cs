using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using GameFramework;
using GameLogic.Gameplay.Combat;
using GameLogic.Gameplay.Expedition;
using GameLogic.Gameplay.Roguelike;
using GameLogic.Project;
using GameLogic.UI.Battle;
using GameLogic.UI.Roguelike;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MiniGame.Tests
{
    public sealed partial class ProjectExpeditionPlayModeTests
    {
        /// <summary>
        /// 真实配置高级装备与升级后的 Rogue 强化共同进入 ECS，卸装只移除装备来源。
        /// </summary>
        /// <returns>使用真实热更资源的逐帧集成任务。</returns>
        [UnityTest]
        public IEnumerator Augments_EquipmentAndRankedRogueAffectRealSkillRange() => UniTask.ToCoroutine(async () =>
        {
            await SetupAsync(new MemorySaveBackend());
            using var scope = new LifetimeScope("AugmentsIntegration");
            ProjectItem weapon = ProjectEquipmentRules.CreateEquipment(new System.Random(44), ProjectItemType.Weapon, 1, ProjectItemQuality.Epic);
            weapon.AdvancedAffixes = new List<ProjectAdvancedAffix>
            {
                new() { DefinitionId = "weapon_wide", AugmentId = "wide_blade", Version = 1, Value = 0.2f }
            };
            weapon.Equipped = true;
            ProjectSaveData initial = ProjectSaveData.CreateDefault();
            initial.Warehouse.Clear();
            initial.Warehouse.Add(weapon);
            Assert.That(await ProjectServices.Coordinator.CloseAsync(), Is.True);
            await SetupAsync(new MemorySaveBackend(), initial);
            SetProgressThreshold(scope, 1);
            RestrictSkillPool(scope, "wide_blade");
            var wide = GameLogic.Tables.GameTables.CfgRogueRewards.GetById("wide_blade");
            int maximum = wide.MaxRank;
            scope.Defer(() => wide.MaxRank = maximum);
            wide.MaxRank = 2;
            Assert.That(await ProjectServices.Coordinator.StartRunAsync(1), Is.True);
            ExpeditionRun run = ProjectServices.Session.CurrentRun;
            await AssertSkillRadiusAsync(run, 1.2f);
            await GrantRogueUpgradeAsync(run, "wide_blade", 1);
            await GrantRogueUpgradeAsync(run, "wide_blade", 2);
            await ChooseAllSkillsAsync(run);
            Assert.That(run.RogueUpgrades.Count(item => item.Id == "wide_blade"), Is.EqualTo(1));
            await AssertSkillRadiusAsync(run, 1.2f * 1.3f);
            Assert.That(run.TryOpenInventory(), Is.True);
            Assert.That(run.TryEquip(weapon.Id), Is.True);
            await WaitRunSavedAsync(run);
            run.ClosePanel(ExpeditionPanelKind.Inventory);
            await AssertSkillRadiusAsync(run, 1.3f);
            Assert.That(run.TryOpenInventory(), Is.True);
            Assert.That(run.ToggleSafe(weapon.Id), Is.True);
            await WaitRunSavedAsync(run);
            string frozen = JsonUtility.ToJson(run.Inventory.Single(item => item.Id == weapon.Id).AdvancedAffixes[0]);
            Assert.That(JsonUtility.ToJson(run.GetRetainedItems(false).Single().AdvancedAffixes[0]), Is.EqualTo(frozen));
            Assert.That(run.World.Faulted, Is.False);
        });

        /// <summary>
        /// 总览真实窗口持有自己的暂停，关闭不会解除应用暂停且返场销毁总览。
        /// </summary>
        /// <returns>从主界面出战到本局总览和返场的异步测试。</returns>
        [UnityTest]
        public IEnumerator RogueOverviewWindow_PausesIndependentlyAndClosesAtRunEnd() => UniTask.ToCoroutine(async () =>
        {
            await SetupAsync(new MemorySaveBackend());
            using var scope = new LifetimeScope("RogueOverviewProgress");
            SetProgressThreshold(scope, 1);
            Assert.That(await ProjectServices.Coordinator.StartRunAsync(1), Is.True);
            ExpeditionRun run = ProjectServices.Session.CurrentRun;
            await EarnRogueChoiceAsync(run);
            RogueChoiceSnapshot offer = run.PendingRogueChoice;
            int index = offer.Options[0].Index;
            Assert.That(run.TryChooseRogue(offer.OfferId, offer.Revision, index), Is.True);
            await WaitRunSavedAsync(run);
            await ChooseAllSkillsAsync(run);
            IUIModule ui = GameEntry.Get<IUIModule>();
            await ui.CloseWindowAsync(ProjectServices.RogueChoiceWindow);
            BattleHudWindow hud = ui.OpenedWindows.OfType<BattleHudWindow>().Single();
            ClickButton(hud, "本局总览");
            for (int frame = 0; frame < 60 && !ui.OpenedWindows.Any(item => item.Id == ProjectServices.RogueOverviewWindow); frame++)
                await UniTask.Yield();
            RogueOverviewWindow overview = ui.OpenedWindows.OfType<RogueOverviewWindow>().Single();
            Assert.That(overview.Config.UseMask, Is.True);
            Assert.That(overview.Root.GetComponentsInChildren<UnityEngine.UI.Text>().Any(label =>
                label.text.Contains(offer.Options[index].Reward.Title)), Is.True);
            Canvas.ForceUpdateCanvases();
            Assert.That(overview.Root.GetComponentsInChildren<UnityEngine.UI.ScrollRect>().All(scroll => scroll.viewport.rect.height > 0), Is.True);
            long tick = run.World.TickIndex;
            run.Advance(1);
            Assert.That(run.World.TickIndex, Is.EqualTo(tick));
            Assert.That(run.TryOpenInventory(), Is.False);
            Assert.That(run.TryBeginRogueChoice("blocked"), Is.False);
            run.SetPaused(true);
            await ui.CloseWindowAsync(ProjectServices.RogueOverviewWindow);
            run.Advance(1);
            Assert.That(run.World.TickIndex, Is.EqualTo(tick));
            run.SetPaused(false);
            Assert.That(run.CanAcceptPlayerInput, Is.True);
            await TriggerExtractionAsync(run);
            Assert.That(await ProjectServices.Coordinator.ReturnHomeAsync(), Is.True);
            Assert.That(ui.OpenedWindows.Any(item => item.Id == ProjectServices.RogueOverviewWindow), Is.False);
            Assert.That(await ProjectServices.Coordinator.StartRunAsync(1), Is.True);
            Assert.That(ProjectServices.Session.CurrentRun.RogueUpgrades, Is.Empty);
        });

        /// <summary>
        /// 通过实际击杀资格领取指定技能的下一等级。
        /// </summary>
        /// <param name="run">测试单局。</param>
        /// <param name="id">目标技能身份。</param>
        /// <param name="rank">期待达到的等级。</param>
        /// <returns>选择与保存全部完成的任务。</returns>
        private static async UniTask GrantRogueUpgradeAsync(ExpeditionRun run, string id, int rank)
        {
            if (run.PendingRogueChoice == null) await EarnRogueChoiceAsync(run);
            RogueChoiceSnapshot offer = run.PendingRogueChoice;
            RogueChoiceOption option = offer.Options.Single(item => item.Reward.Id == id);
            Assert.That(option.Reward.Rank, Is.EqualTo(rank));
            Assert.That(run.TryChooseRogue(offer.OfferId, offer.Revision, option.Index), Is.True);
            await WaitRunSavedAsync(run);
            Assert.That(run.RogueUpgrades.Single(item => item.Id == id).Rank, Is.EqualTo(rank));
        }

        /// <summary>
        /// 通过真实基础技能预警观察装备与本局强化合成后的作用范围。
        /// </summary>
        /// <param name="run">测试单局。</param>
        /// <param name="multiplier">预期最终范围倍率。</param>
        /// <returns>真实预警和技能冷却完成任务。</returns>
        private static async UniTask AssertSkillRadiusAsync(ExpeditionRun run, float multiplier)
        {
            ProjectSession session = ProjectServices.Session;
            float baseRange = ProjectEquipmentRules.Calculate(run.Inventory, session.PowerRank, session.VitalityRank, session.AgilityRank).AttackRange;
            await WaitRunSavedAsync(run);
            run.SetTouchMove(Vector2.zero);
            Assert.That(run.World.TrySetPosition(run.Player, run.CurrentRegion.Plan.EntryPosition), Is.True);
            bool started = false;
            for (int tick = 0; tick < 120 && !started; tick++)
            {
                await WaitRunSavedAsync(run);
                Assert.That(run.IsChoosingReward, Is.False, "范围断言必须在技能选择完成后执行。");
                started = run.World.TryStartSkill(run.Player, ProjectContentConfig.Player.SkillID, default);
                if (!started) run.Advance(CombatWorld.TickSeconds);
            }
            Assert.That(started, Is.True, "已有施法必须在有界战斗时间内结束，才能观察新一轮预警。");
            var telegraphs = new List<CombatTelegraphSnapshot>();
            run.World.GetTelegraphsSnapshot(telegraphs);
            Assert.That(telegraphs.Single(item => item.ActorId == 1).Radius, Is.EqualTo(baseRange * multiplier).Within(0.001f));
            for (int tick = 0; tick < 100; tick++)
            {
                await WaitRunSavedAsync(run);
                run.Advance(CombatWorld.TickSeconds);
            }
            await WaitRunSavedAsync(run);
        }

        /// <summary>
        /// 保留共享范围断言入口，调用方等待保存和真实技能冷却完成。
        /// </summary>
        /// <param name="run">需要观察范围的正式单局。</param>
        /// <param name="multiplier">预期最终范围倍率。</param>
        /// <returns>预警检查及保存完成任务。</returns>
        private static UniTask AssertSkillRadius(ExpeditionRun run, float multiplier) => AssertSkillRadiusAsync(run, multiplier);
    }
}
