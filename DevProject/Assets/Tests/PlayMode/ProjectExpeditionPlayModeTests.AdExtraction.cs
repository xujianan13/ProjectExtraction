using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using GameFramework;
using GameLogic.Advertising;
using GameLogic.Gameplay.Combat;
using GameLogic.Gameplay.Expedition;
using GameLogic.Gameplay.Level;
using GameLogic.Project;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace MiniGame.Tests
{
    public sealed partial class ProjectExpeditionPlayModeTests
    {
        /// <summary>
        /// 广告确认后通过真实勾选按钮带出三件背包物品，固定装备自动保护且终局只结算一次。
        /// </summary>
        /// <returns>广告窗口、物品身份、保留损失与历史验证任务。</returns>
        [UnityTest]
        public IEnumerator AdExtraction_RealSelectionRetainsThreeBagItemsAndProtectedEquipment() => UniTask.ToCoroutine(async () =>
        {
            await SetupAsync(new MemorySaveBackend());
            ProjectItem starter = ProjectEquipmentRules.CreateEquipment(new System.Random(61), ProjectItemType.Weapon, 1, ProjectItemQuality.Common);
            starter.Equipped = true;
            ProjectItem[] fixtures = CreateAdExtractionItems(5);
            ExpeditionRun run = await StartWithEquipmentFixturesAsync(starter, fixtures);
            Assert.That(run.TryOpenPause(), Is.True);
            Assert.That(await run.RequestAdExtractionAsync(), Is.True);
            await WaitRunSavedAsync(run);
            GameWindow window = await WaitForWindowAsync(ProjectServices.AdExtractionWindow);
            Assert.That(run.AdExtractionCandidates.Select(item => item.Id), Is.EquivalentTo(fixtures.Select(item => item.Id)));
            foreach (ProjectItem item in fixtures.Take(3))
                window.Root.GetComponentsInChildren<Button>(false).Single(button => button.GetComponentInChildren<Text>().text.Contains(item.Name)).onClick.Invoke();
            ClickButton(window, "确认撤离");
            await WaitForPhaseAsync(ProjectRunPhase.ResultsReady);
            ProjectRunResult result = ProjectServices.Session.LastResult;
            Assert.That(result.Outcome.ExitKind, Is.EqualTo(ProjectRunExitKind.AdExtraction));
            Assert.That(result.RetainedItems.Select(item => item.Id), Is.EquivalentTo(fixtures.Take(3).Select(item => item.Id).Append(starter.Id)));
            Assert.That(result.LostItems.Select(item => item.Id), Is.EquivalentTo(fixtures.Skip(3).Select(item => item.Id)));
            Assert.That(run.TryConfirmAdExtraction(fixtures.Take(3).Select(item => item.Id).ToArray()), Is.False);
            Assert.That(ProjectServices.Session.RunHistory.Count(entry => entry.RunId == run.RunId), Is.EqualTo(1));
        });

        /// <summary>
        /// 不足三件默认全选仍可取消勾选，也允许只保留固定装备，候选不接受外来或出战装备身份。
        /// </summary>
        /// <returns>默认全选、取消勾选和零物品终局验证任务。</returns>
        [UnityTest]
        public IEnumerator AdExtraction_FewerItemsDefaultAllButMayBeDeselectedToZero() => UniTask.ToCoroutine(async () =>
        {
            await SetupAsync(new MemorySaveBackend());
            ProjectItem starter = ProjectEquipmentRules.CreateEquipment(new System.Random(62), ProjectItemType.Weapon, 1, ProjectItemQuality.Common);
            starter.Equipped = true;
            ProjectItem[] fixtures = CreateAdExtractionItems(2);
            ExpeditionRun run = await StartWithEquipmentFixturesAsync(starter, fixtures);
            Assert.That(run.TryOpenPause(), Is.True);
            Assert.That(await run.RequestAdExtractionAsync(), Is.True);
            await WaitRunSavedAsync(run);
            GameWindow window = await WaitForWindowAsync(ProjectServices.AdExtractionWindow);
            foreach (ProjectItem item in fixtures)
            {
                Button button = window.Root.GetComponentsInChildren<Button>(false).Single(value => value.GetComponentInChildren<Text>().text.Contains(item.Name));
                StringAssert.StartsWith("[✓]", button.GetComponentInChildren<Text>().text);
                Assert.That(button.interactable, Is.True);
                button.onClick.Invoke();
            }
            Assert.That(run.TryConfirmAdExtraction(new[] { starter.Id }), Is.False);
            Assert.That(run.TryConfirmAdExtraction(new[] { "not-owned-by-run" }), Is.False);
            ClickButton(window, "确认撤离");
            await WaitForPhaseAsync(ProjectRunPhase.ResultsReady);
            Assert.That(ProjectServices.Session.LastResult.RetainedItems.Single().Id, Is.EqualTo(starter.Id));
            Assert.That(ProjectServices.Session.LastResult.LostItems.Select(item => item.Id), Is.EquivalentTo(fixtures.Select(item => item.Id)));
        });

        /// <summary>
        /// 广告取消或失败不改变背包，已完成授权可在返回暂停和暂离续关后复用。
        /// </summary>
        /// <returns>可控广告回调、重复请求、授权保存与续关验证任务。</returns>
        [UnityTest]
        public IEnumerator AdExtraction_CancellationPreservesItemsAndAuthorizationSurvivesSuspension() => UniTask.ToCoroutine(async () =>
        {
            await SetupAsync(new MemorySaveBackend());
            Assert.That(await ProjectServices.Coordinator.StartRunAsync(1), Is.True);
            ExpeditionRun run = ProjectServices.Session.CurrentRun;
            Assert.That(run.TryOpenPause(), Is.True);
            string[] entry = run.EntryEquipment.Select(item => item.Id).ToArray();
            RewardedAdService original = ProjectServices.RewardedAds;
            var provider = new HeldRewardedAdProvider("expedition_extract");
            using var replacement = new RewardedAdService(provider);
            SetRevivalAdService(replacement);
            try
            {
                UniTask<bool> cancelled = run.RequestAdExtractionAsync().Preserve();
                Assert.That(await run.RequestAdExtractionAsync(), Is.False);
                Assert.That(provider.Calls, Is.EqualTo(1));
                provider.Complete(RewardedAdStatus.Cancelled);
                Assert.That(await cancelled, Is.False);
                Assert.That(run.HasAdExtractionAuthorization, Is.False);
                Assert.That(run.Inventory, Is.Empty);
                Assert.That(ProjectServices.Session.RecordedOutcome, Is.Null);
                LogAssert.ignoreFailingMessages = true;
                UniTask<bool> failed = run.RequestAdExtractionAsync().Preserve();
                provider.Complete(RewardedAdStatus.Failed);
                Assert.That(await failed, Is.False);
                UniTask<bool> completed = run.RequestAdExtractionAsync().Preserve();
                provider.Complete(RewardedAdStatus.Completed);
                Assert.That(await completed, Is.True);
                await WaitRunSavedAsync(run);
                Assert.That(run.HasAdExtractionAuthorization, Is.True);
                await WaitForWindowAsync(ProjectServices.AdExtractionWindow);
                run.ClosePanel(ExpeditionPanelKind.AdExtraction);
                await WaitForAsync(() => run.ActivePanel == ExpeditionPanelKind.Pause);
                Assert.That(await ProjectServices.Coordinator.SuspendRunAsync(run), Is.True);
                Assert.That(await ProjectServices.Coordinator.ResumeRunAsync(), Is.True);
                run = ProjectServices.Session.CurrentRun;
                Assert.That(run.HasAdExtractionAuthorization, Is.True);
                Assert.That(run.TryOpenPause(), Is.True);
                Assert.That(run.TryOpenAdExtraction(), Is.True);
                Assert.That(provider.Calls, Is.EqualTo(3));
                Assert.That(run.EntryEquipment.Select(item => item.Id), Is.EquivalentTo(entry));
            }
            finally { SetRevivalAdService(original); }
        });

        /// <summary>
        /// 创建仅用于测试广告选择的稳定、可区分物品。
        /// </summary>
        /// <param name="count">测试物品数量。</param>
        /// <returns>具有唯一名称和身份的物品数组。</returns>
        private static ProjectItem[] CreateAdExtractionItems(int count)
        {
            var items = new ProjectItem[count];
            for (int index = 0; index < count; index++)
            {
                ProjectItem item = ProjectEquipmentRules.CreateEquipment(new System.Random(300 + index), ProjectItemType.Armor, 1, ProjectItemQuality.Common);
                item.Id = "ad-selection-item-" + index;
                item.Name = "广告选择物品" + index;
                items[index] = item;
            }
            return items;
        }

    }
}
