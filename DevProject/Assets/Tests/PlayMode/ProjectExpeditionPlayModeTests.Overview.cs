using System.Collections;
using System.Linq;
using Cysharp.Threading.Tasks;
using GameFramework;
using GameLogic.Gameplay.Expedition;
using GameLogic.Gameplay.Level;
using GameLogic.Project;
using GameLogic.Tables;
using GameLogic.UI.Common;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace MiniGame.Tests
{
    public sealed partial class ProjectExpeditionPlayModeTests
    {
        /// <summary>
        /// 开局展示真实最终目标，总览重读冻结地图计划而不生成未来区域，收藏状态只读取已入库登记。
        /// </summary>
        /// <returns>真实 HUD、总览、收藏摘要及 Boss 候选展示的验证任务。</returns>
        [UnityTest]
        public IEnumerator Overview_FrozenPlansAndRegisteredCollectionStateRemainVisible() => UniTask.ToCoroutine(async () =>
        {
            await SetupAsync(new MemorySaveBackend());
            var definitions = ProjectV2RewardConfig.CollectionDefinitions();
            var achievements = ProjectV2RewardConfig.CollectionAchievements(out string error);
            Assert.That(error, Is.Null);
            ProjectItem registered = ProjectCollectionRules.CreateItem(new System.Random(18), definitions[0]);
            ProjectSaveData profile = ProjectSaveData.CreateDefault();
            profile.Warehouse.Add(registered.Copy());
            Assert.That(ProjectCollectionRules.RegisterRetainedItems(profile, new[] { registered }, definitions, achievements, out error), Is.True, error);
            Assert.That(await ProjectServices.Coordinator.CloseAsync(), Is.True);
            await SetupAsync(new MemorySaveBackend(), profile);
            Assert.That(await ProjectServices.Coordinator.StartRunAsync(1), Is.True);
            ExpeditionRun run = ProjectServices.Session.CurrentRun;
            await WaitRunSavedAsync(run);
            var plans = run.GetRegionOverview();
            Assert.That(plans.Count, Is.EqualTo(run.RegionCount + 1));
            Assert.That(plans.Count(value => value.IsGenerated), Is.EqualTo(1));
            Assert.That(plans.Count(value => value.IsCurrent), Is.EqualTo(1));
            Assert.That(plans[0].IsCurrent, Is.True);
            Assert.That(plans.Skip(1).All(value => !value.IsGenerated && !value.IsPassed), Is.True);
            Assert.That(run.TryCaptureState(out var before, out error), Is.True, error);
            string frozenRegions = JsonUtility.ToJson(before.Regions[0]);
            ExpeditionRegionOverview final = plans[plans.Count - 1];
            CfgBossesRow finalBoss = GameTables.CfgBosses.GetById(run.RegionRequests[run.RegionCount].BossDefinitionId);
            Assert.That(final.BossId, Is.EqualTo(finalBoss.ID));
            Assert.That(final.MapName, Is.EqualTo(GameTables.CfgRegionMaps.GetById(final.MapId).Name));
            GameWindow hud = await WaitForWindowAsync(ProjectServices.BattleHudWindow);
            Assert.That(hud.Root.GetComponentsInChildren<Text>().Any(value => value.text.Contains(final.BossName) &&
                value.text.Contains(final.Weakness)), Is.True, "开局 HUD 应直接告知最终 Boss 和弱点。");
            ClickButton(hud, "本局总览");
            GameWindow overview = await WaitForWindowAsync(ProjectServices.RogueOverviewWindow);
            string text = string.Join("\n", overview.Root.GetComponentsInChildren<Text>().Select(value => value.text));
            Assert.That(text, Does.Contain("最终 Boss：" + final.BossName));
            Assert.That(text, Does.Contain(final.Weakness));
            foreach (ExpeditionRegionOverview plan in plans)
            {
                Assert.That(text, Does.Contain("地图：" + plan.MapName));
                Assert.That(text, Does.Contain("Boss：" + plan.BossName));
            }
            foreach (string id in finalBoss.CollectionPreviewIDs)
                Assert.That(text, Does.Contain(ProjectV2RewardConfig.Collection(id).Name + " · " +
                    (ProjectServices.Session.CollectedCollectionIds.Contains(id) ? "已收藏" : "未收藏")));
            Assert.That(text, Does.Contain("当前"));
            Assert.That(text, Does.Contain("尚未进入"));
            Assert.That(text, Does.Contain("本局尚未获得强化"));
            Assert.That(run.TryCaptureState(out var after, out error), Is.True, error);
            Assert.That(after.Regions.Count, Is.EqualTo(1));
            Assert.That(JsonUtility.ToJson(after.Regions[0]), Is.EqualTo(frozenRegions));
            ClickButton(overview, "关闭");
            await WaitForAsync(() => GameEntry.Get<IUIModule>().OpenedWindows.All(value => value.Id != ProjectServices.RogueOverviewWindow));
            Assert.That(run.ActivePanel, Is.EqualTo(ExpeditionPanelKind.None), "关闭总览必须释放面板状态。");
            ClickButton(hud, "本局总览");
            Assert.That(run.ActivePanel, Is.EqualTo(ExpeditionPanelKind.RogueOverview), "再次点击总览必须接受打开请求。");
            overview = await WaitForWindowAsync(ProjectServices.RogueOverviewWindow);
            Assert.That(run.GetRegionOverview().Select(value => value.BossId), Is.EqualTo(plans.Select(value => value.BossId)));
            Assert.That(run.GetRegionOverview().Count(value => value.IsGenerated), Is.EqualTo(1));
            ClickButton(overview, "关闭");
            await WaitForAsync(() => GameEntry.Get<IUIModule>().OpenedWindows.All(value => value.Id != ProjectServices.RogueOverviewWindow));

            ProjectItem unregistered = ProjectCollectionRules.CreateItem(new System.Random(19), definitions.First(value => value.Id != registered.CollectionId));
            Assert.That(ProjectEquipmentText.Details(registered), Does.Contain("已收藏"));
            Assert.That(ProjectEquipmentText.LootSummary(registered), Does.Contain("已收藏"));
            Assert.That(ProjectEquipmentText.Details(unregistered), Does.Contain("未收藏"));
            Assert.That(ProjectEquipmentText.LootSummary(unregistered), Does.Contain("未收藏"));
            foreach (ProjectItem item in new[] { registered, unregistered })
            {
                ProjectCollectionDefinition definition = ProjectV2RewardConfig.Collection(item.CollectionId);
                foreach (string display in new[] { ProjectEquipmentText.Details(item), ProjectEquipmentText.LootSummary(item) })
                {
                    Assert.That(display, Does.Contain("来源：" + definition.Source));
                    Assert.That(display, Does.Not.Contain("系列："));
                    Assert.That(display, Does.Not.Contain(definition.Series));
                }
            }
            string slot = run.CurrentRegion.Plan.SpawnSlots.Single(value => value.Kind == RegionEnemyKind.Boss).SlotId;
            Assert.That(await DefeatSlotAsync(run, slot), Is.True);
            await ChooseAllSkillsAsync(run);
            await WaitRunSavedAsync(run);
            var choice = run.PendingBossChoice;
            Assert.That(choice, Is.Not.Null);
            ProjectItem candidate = choice.Options.First(value => value.Type == ProjectItemType.Collection);
            GameWindow bossWindow = await WaitForWindowAsync(ProjectServices.BossLootWindow);
            Button card = bossWindow.Root.GetComponentsInChildren<Button>().Single(value =>
                value.GetComponentInChildren<Text>().text.Contains(candidate.Name));
            card.onClick.Invoke();
            Assert.That(bossWindow.Root.GetComponentsInChildren<Text>().Any(value => value.text.Contains(
                ProjectEquipmentText.CollectionRegistration(candidate)) && value.text.Contains("来源：")), Is.True);
            ProjectCollectionDefinition candidateDefinition = ProjectV2RewardConfig.Collection(candidate.CollectionId);
            string candidateDetails = bossWindow.Root.GetComponentsInChildren<Text>().Single(value =>
                value.text.Contains("来源：" + candidateDefinition.Source)).text;
            Assert.That(candidateDetails, Does.Not.Contain("系列："));
            Assert.That(candidateDetails, Does.Not.Contain(candidateDefinition.Series));
            Assert.That(ProjectServices.Session.CollectedCollectionIds, Is.EquivalentTo(new[] { registered.CollectionId }));
            Assert.That(run.TryChooseBoss(choice.OfferId, choice.Revision, 0), Is.True);
            await WaitRunSavedAsync(run);
            await EnterExtractionConfirmationAsync(run);
            await ClickExtractionConfirmationAsync("继续下一区域");
            await WaitRunSavedAsync(run);
            ClickButton(hud, "本局总览");
            overview = await WaitForWindowAsync(ProjectServices.RogueOverviewWindow);
            var progressed = run.GetRegionOverview();
            Assert.That(progressed[0].IsPassed && progressed[0].IsGenerated, Is.True);
            Assert.That(progressed[1].IsCurrent && progressed[1].IsGenerated, Is.True);
            Assert.That(progressed.Skip(2).All(value => !value.IsGenerated), Is.True);
            Assert.That(progressed.Select(value => value.BossId), Is.EqualTo(plans.Select(value => value.BossId)));
            Assert.That(overview.Root.GetComponentsInChildren<Text>().Any(value => value.text.Contains("已通过")), Is.True);
            Assert.That(run.TryCaptureState(out var progressedSnapshot, out error), Is.True, error);
            Assert.That(progressedSnapshot.Regions.Count, Is.EqualTo(2));
        });
    }
}
