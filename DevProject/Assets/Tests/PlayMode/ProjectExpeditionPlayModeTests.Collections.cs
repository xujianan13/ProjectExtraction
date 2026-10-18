using System.Collections;
using System.Linq;
using Cysharp.Threading.Tasks;
using GameFramework;
using GameLogic.Project;
using GameLogic.Scaffold;
using GameLogic.UI.Character;
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
        /// 从真实主界面打开收藏窗口，展示全部配置图鉴、成就条件及永久奖励并正常关闭。
        /// </summary>
        /// <returns>真实入口、图鉴内容与关闭行为的逐帧验证任务。</returns>
        [UnityTest]
        public IEnumerator Collections_RealHomeShowsConfiguredEntriesAndAchievementRewards() => UniTask.ToCoroutine(async () =>
        {
            await SetupRealHomeAsync(new MemorySaveBackend());
            GameWindow home = await WaitForWindowAsync(ContentIds.HomeWindow);
            ClickButton(home, "收藏");
            GameWindow window = await WaitForWindowAsync(ProjectServices.CollectionWindow);
            Assert.That(window, Is.TypeOf<CollectionWindow>());
            Canvas.ForceUpdateCanvases();
            ScrollRect scroll = window.Root.GetComponentInChildren<ScrollRect>();
            Assert.That(scroll.content, Is.Not.Null);
            var definitions = ProjectRewardConfig.CollectionDefinitions();
            Assert.That(definitions.Count, Is.EqualTo(4));
            Assert.That(scroll.content.Cast<Transform>().Count(value => value.name.StartsWith("Collection_")), Is.EqualTo(definitions.Count));
            foreach (ProjectCollectionDefinition definition in definitions)
            {
                Transform card = scroll.content.Find("Collection_" + definition.Id);
                Assert.That(card, Is.Not.Null, definition.Id);
                Assert.That(card.GetComponents<Image>().Length, Is.EqualTo(1));
                Assert.That(card.GetComponent<Image>().color.a, Is.EqualTo(1f));
                string text = string.Join("\n", card.GetComponentsInChildren<Text>().Select(value => value.text));
                Assert.That(text, Does.Contain("未收藏 · " + definition.Name));
                Assert.That(text, Does.Contain("来源：" + definition.Source));
                Assert.That(text, Does.Contain("重复售价：" + definition.SellGold + " 金币"));
            }

            var achievements = ProjectRewardConfig.CollectionAchievements(out string error);
            Assert.That(error, Is.Null);
            Assert.That(scroll.content.Cast<Transform>().Count(value => value.name.StartsWith("Achievement_")), Is.EqualTo(achievements.Count));
            foreach (ProjectCollectionAchievementDefinition achievement in achievements)
            {
                Transform card = scroll.content.Find("Achievement_" + achievement.Id);
                Assert.That(card, Is.Not.Null, achievement.Id);
                string text = string.Join("\n", card.GetComponentsInChildren<Text>().Select(value => value.text));
                Assert.That(text, Does.Contain(achievement.Name + " · 0/" + achievement.RequiredCollectionIds.Count + " · 未完成"));
                foreach (string id in achievement.RequiredCollectionIds)
                    Assert.That(text, Does.Contain("○ " + ProjectRewardConfig.Collection(id).Name));
                foreach (ProjectAttributeValue bonus in achievement.AttributeRewards)
                    Assert.That(text, Does.Contain("永久奖励：" + ProjectEquipmentText.FinalAttribute(bonus.Attribute, bonus.Value)));
            }
            Assert.That(ProjectServices.Session.CollectedCollectionIds, Is.Empty);
            Assert.That(ProjectServices.Session.AchievedCollectionIds, Is.Empty);
            Assert.That(ProjectServices.Session.CollectionBonusSources, Is.Empty);
            ClickButton(window, "关闭");
            await WaitForAsync(() => GameEntry.Get<IUIModule>().OpenedWindows.All(value => value.Id != ProjectServices.CollectionWindow));
            Assert.That(ProjectServices.Coordinator.Phase, Is.EqualTo(ProjectRunPhase.HomeReady));
            Assert.That(GameEntry.Get<IUIModule>().OpenedWindows.Any(value => value.Id == ContentIds.HomeWindow), Is.True);
        });
    }
}
