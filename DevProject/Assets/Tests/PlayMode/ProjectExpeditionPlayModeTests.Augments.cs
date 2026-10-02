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
            ExpeditionVisualAssets assets = await ExpeditionVisualAssets.LoadAsync(scope, GameEntry.Get<IResourceModule>());
            ProjectItem weapon = ProjectEquipmentRules.CreateEquipment(new System.Random(44), ProjectItemType.Weapon, 1, ProjectItemQuality.Epic);
            weapon.AdvancedAffixes = new List<ProjectAdvancedAffix>
            {
                new() { DefinitionId = "weapon_wide", AugmentId = "wide_blade", Version = 1, Value = 0.2f }
            };
            weapon.Equipped = true;
            Assert.That(ExpeditionRun.TryCreate(ProjectServices.Session, "augment-run", 1, 42,
                new List<ProjectItem> { weapon }, out ExpeditionRun run), Is.True);
            using var ownedRun = run;
            run.AttachScene(assets);
            Assert.That(run.MarkReady() && run.Activate(), Is.True);
            AssertSkillRadius(run, 1.2f);
            GrantRogueUpgrade(run, "wide_blade", 1);
            GrantRogueUpgrade(run, "wide_blade", 2);
            Assert.That(run.RogueUpgrades.Count(item => item.Id == "wide_blade"), Is.EqualTo(1));
            AssertSkillRadius(run, 1.2f * 1.3f);
            Assert.That(run.TryOpenInventory(), Is.True);
            Assert.That(run.TryEquip(weapon.Id), Is.True);
            run.ClosePanel(ExpeditionPanelKind.Inventory);
            AssertSkillRadius(run, 1.3f);
            Assert.That(run.TryOpenInventory(), Is.True);
            Assert.That(run.ToggleSafe(weapon.Id), Is.True);
            string frozen = JsonUtility.ToJson(run.Inventory.Single(item => item.Id == weapon.Id).AdvancedAffixes[0]);
            Assert.That(JsonUtility.ToJson(run.GetRetainedItems(false).Single().AdvancedAffixes[0]), Is.EqualTo(frozen));
            Assert.That(run.World.Faulted, Is.False);
        });

        /// <summary>
        /// 总览真实窗口持有自己的暂停，关闭不会解除应用暂停且返场销毁总览。
        /// </summary>
        /// <returns>从主界面出战到强化总览和返场的异步测试。</returns>
        [UnityTest]
        public IEnumerator RogueOverviewWindow_PausesIndependentlyAndClosesAtRunEnd() => UniTask.ToCoroutine(async () =>
        {
            await SetupAsync(new MemorySaveBackend());
            Assert.That(await ProjectServices.Coordinator.StartRunAsync(1), Is.True);
            ExpeditionRun run = ProjectServices.Session.CurrentRun;
            Assert.That(run.TryBeginRogueChoice("overview-test"), Is.True);
            RogueChoiceSnapshot offer = run.PendingRogueChoice;
            for (int attempt = 0; attempt < 32 && offer.Options.All(item => item.Reward.Kind == RogueRewardKind.RandomLoot); attempt++)
            {
                Assert.That(run.TryChooseRogue(offer.OfferId, offer.Revision, 0), Is.True);
                Assert.That(run.TryBeginRogueChoice("overview-retry:" + attempt), Is.True);
                offer = run.PendingRogueChoice;
            }
            int index = offer.Options.First(item => item.Reward.Kind != RogueRewardKind.RandomLoot).Index;
            Assert.That(run.TryChooseRogue(offer.OfferId, offer.Revision, index), Is.True);
            IUIModule ui = GameEntry.Get<IUIModule>();
            await ui.CloseWindowAsync(ProjectServices.RogueChoiceWindow);
            BattleHudWindow hud = ui.OpenedWindows.OfType<BattleHudWindow>().Single();
            ClickButton(hud, "强化总览");
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
        /// 从正常抽选接口累计指定奖励等级，非目标选项优先领取独立物品。
        /// </summary>
        /// <param name="run">测试单局。</param>
        /// <param name="id">目标强化身份。</param>
        /// <param name="rank">期待达到的等级。</param>
        private static void GrantRogueUpgrade(ExpeditionRun run, string id, int rank)
        {
            for (int attempt = 0; attempt < 200; attempt++)
            {
                Assert.That(run.TryBeginRogueChoice("rank:" + id + ":" + rank + ":" + attempt), Is.True);
                RogueChoiceSnapshot offer = run.PendingRogueChoice;
                RogueChoiceOption option = offer.Options.FirstOrDefault(item => item.Reward.Id == id) ??
                    offer.Options.FirstOrDefault(item => item.Reward.Kind == RogueRewardKind.RandomLoot) ?? offer.Options[0];
                Assert.That(run.TryChooseRogue(offer.OfferId, offer.Revision, option.Index), Is.True);
                if (run.RogueUpgrades.Any(item => item.Id == id && item.Rank == rank)) return;
            }
            Assert.Fail("目标奖励没有在固定种子候选序列内出现。");
        }

        /// <summary>
        /// 通过真实基础技能预警观察装备与本局强化合成后的作用范围。
        /// </summary>
        /// <param name="run">测试单局。</param>
        /// <param name="multiplier">预期最终范围倍率。</param>
        private static void AssertSkillRadius(ExpeditionRun run, float multiplier)
        {
            ProjectSession session = ProjectServices.Session;
            float baseRange = ProjectEquipmentRules.Calculate(run.Inventory, session.PowerRank, session.VitalityRank, session.AgilityRank).AttackRange;
            Assert.That(run.World.TryStartSkill(run.Player, ProjectContentConfig.Player.SkillID, default), Is.True);
            var telegraphs = new List<CombatTelegraphSnapshot>();
            run.World.GetTelegraphsSnapshot(telegraphs);
            Assert.That(telegraphs.Single(item => item.ActorId == 1).Radius, Is.EqualTo(baseRange * multiplier).Within(0.001f));
            for (int tick = 0; tick < 100; tick++) run.Advance(CombatWorld.TickSeconds);
        }
    }
}
