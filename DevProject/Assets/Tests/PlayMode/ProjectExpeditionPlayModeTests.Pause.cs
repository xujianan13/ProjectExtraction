using System;
using System.Collections;
using System.Linq;
using Cysharp.Threading.Tasks;
using GameFramework;
using GameLogic.Gameplay.Combat;
using GameLogic.Gameplay.Expedition;
using GameLogic.Project;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace MiniGame.Tests
{
    /// <summary>
    /// 验证手动暂停与主动放弃的真实窗口和存档闭环。
    /// </summary>
    public sealed partial class ProjectExpeditionPlayModeTests
    {
        /// <summary>
        /// HUD 暂停阻止战斗推进，后台往返不解除菜单暂停，恢复时不继承旧移动输入。
        /// </summary>
        /// <returns>真实战斗场景和暂停窗口的逐帧测试。</returns>
        [UnityTest]
        public IEnumerator PauseWindow_BackgroundRoundTripKeepsManualPauseAndClearsMovement() => UniTask.ToCoroutine(async () =>
        {
            await SetupAsync(new MemorySaveBackend());
            Assert.That(await ProjectServices.Coordinator.StartRunAsync(1), Is.True);
            ExpeditionRun run = ProjectServices.Session.CurrentRun;
            GameWindow hud = await WaitForWindowAsync(ProjectServices.BattleHudWindow);
            run.SetTouchMove(Vector2.right);
            ClickButton(hud, "暂停");
            GameWindow pause = await WaitForWindowAsync(ProjectServices.PauseWindow);
            Assert.That(pause, Is.TypeOf<GameLogic.UI.Battle.ProjectPauseWindow>());
            Assert.That(run.ActivePanel, Is.EqualTo(ExpeditionPanelKind.Pause));
            Assert.That(run.CanAcceptPlayerInput, Is.False);
            Assert.That(run.FeedbackPaused, Is.True);
            long tick = run.World.TickIndex;
            Assert.That(run.World.TryGetActor(run.Player, out CombatActorSnapshot before), Is.True);
            run.Advance(1f);
            Assert.That(run.World.TickIndex, Is.EqualTo(tick));

            await ProjectServices.Coordinator.SetApplicationPausedAsync(true);
            run.Advance(1f);
            await ProjectServices.Coordinator.SetApplicationPausedAsync(false);
            run.Advance(1f);
            Assert.That(run.ActivePanel, Is.EqualTo(ExpeditionPanelKind.Pause));
            Assert.That(run.World.TickIndex, Is.EqualTo(tick));
            ClickButton(pause, "设置");
            GameWindow settings = await WaitForWindowAsync(ProjectServices.SettingsWindow);
            ClickButton(settings, "关闭");
            await WaitForAsync(() => GameEntry.Get<IUIModule>().OpenedWindows.All(window => window.Id != ProjectServices.SettingsWindow));
            Assert.That(run.ActivePanel, Is.EqualTo(ExpeditionPanelKind.Pause));
            run.Advance(1f);
            Assert.That(run.World.TickIndex, Is.EqualTo(tick));
            ClickButton(pause, "继续游戏");
            await WaitForAsync(() => run.ActivePanel == ExpeditionPanelKind.None);
            Assert.That(run.CanAcceptPlayerInput, Is.True);
            Assert.That(run.FeedbackPaused, Is.False);
            Assert.That(run.World.TryGetActor(run.Player, out CombatActorSnapshot resumed), Is.True);
            run.Advance(CombatWorld.TickSeconds);
            Assert.That(run.World.TryGetActor(run.Player, out CombatActorSnapshot after), Is.True);
            Assert.That(after.Position.x, Is.EqualTo(resumed.Position.x).Within(0.0001f));
            Assert.That(after.Position.z, Is.EqualTo(resumed.Position.z).Within(0.0001f));
            Assert.That(resumed.Position.x, Is.EqualTo(before.Position.x).Within(0.0001f));
        });

        /// <summary>
        /// 取消放弃不结算；确认后保留固定装备，入场体力不返还。
        /// </summary>
        /// <returns>真实确认框、终局保存与返场验证的逐帧测试。</returns>
        [UnityTest]
        public IEnumerator PauseWindow_AbandonConfirmationSettlesOnceAndProtectsEntryEquipment() => UniTask.ToCoroutine(async () =>
        {
            ProjectSaveData initial = ProjectSaveData.CreateDefault();
            ProjectItem armor = ProjectEquipmentRules.CreateEquipment(new System.Random(73), ProjectItemType.Armor, 1, ProjectItemQuality.Common);
            armor.Equipped = true;
            initial.Warehouse.Add(armor);
            string starterId = initial.Warehouse.Single(item => item.Equipped && item.Type == ProjectItemType.Weapon).Id;
            int initialStamina = initial.Stamina;
            int initialExperience = initial.Experience;
            await SetupAsync(new MemorySaveBackend(), initial);
            ProjectSession session = ProjectServices.Session;
            Assert.That(await ProjectServices.Coordinator.StartRunAsync(1), Is.True);
            ExpeditionRun run = session.CurrentRun;
            Assert.That(session.Stamina, Is.EqualTo(initialStamina - PrototypeRules.EntryCost - PrototypeRules.RoomEntryCost));
            GameWindow hud = await WaitForWindowAsync(ProjectServices.BattleHudWindow);
            ClickButton(hud, "暂停");
            GameWindow pause = await WaitForWindowAsync(ProjectServices.PauseWindow);
            ClickButton(pause, "放弃本局");
            await ClickPauseConfirmationAsync("继续探索");
            Assert.That(ProjectServices.Coordinator.Phase, Is.EqualTo(ProjectRunPhase.Playing));
            Assert.That(run.IsRunning, Is.True);
            Assert.That(session.LastResult, Is.Null);
            Assert.That(session.Stamina, Is.EqualTo(initialStamina - PrototypeRules.EntryCost - PrototypeRules.RoomEntryCost));

            ClickButton(pause, "放弃本局");
            await ClickPauseConfirmationAsync("确认放弃");
            await WaitForPhaseAsync(ProjectRunPhase.ResultsReady);
            ProjectRunResult result = session.LastResult;
            Assert.That(result.Outcome.ExitKind, Is.EqualTo(ProjectRunExitKind.Abandoned));
            Assert.That(result.Outcome.ExtractionExperience, Is.Zero);
            Assert.That(result.LostItems, Is.Empty);
            Assert.That(result.RetainedItems.Select(item => item.Id), Is.EquivalentTo(new[] { starterId, armor.Id }));
            Assert.That(session.Stamina, Is.EqualTo(initialStamina - PrototypeRules.EntryCost - PrototypeRules.RoomEntryCost));
            Assert.That(session.Experience, Is.EqualTo(initialExperience));
            Assert.That(ProjectServices.Coordinator.TryAbandonRun(run), Is.False);
            Assert.That(await ProjectServices.Coordinator.ReturnHomeAsync(), Is.True);
            Assert.That(session.Warehouse.Select(item => item.Id), Is.EquivalentTo(new[] { starterId, armor.Id }));
        });

        /// <summary>
        /// 暂停中的设置写入失败会锁定继续与放弃，重试原候选后恢复操作。
        /// </summary>
        /// <returns>真实暂停与设置窗口、故障注入及重试的逐帧测试。</returns>
        [UnityTest]
        public IEnumerator PauseWindow_FailedPreferenceSaveBlocksActionsUntilRetry() => UniTask.ToCoroutine(async () =>
        {
            var backend = new FaultSaveBackend();
            await SetupAsync(backend);
            Assert.That(await ProjectServices.Coordinator.StartRunAsync(1), Is.True);
            ProjectSession session = ProjectServices.Session;
            ExpeditionRun run = session.CurrentRun;
            GameWindow hud = await WaitForWindowAsync(ProjectServices.BattleHudWindow);
            ClickButton(hud, "暂停");
            GameWindow pause = await WaitForWindowAsync(ProjectServices.PauseWindow);
            ClickButton(pause, "设置");
            GameWindow settings = await WaitForWindowAsync(ProjectServices.SettingsWindow);
            ProjectPreferences draft = session.Preferences;
            draft.MusicVolume = 0.5f;
            backend.FailWrites = true;
            LogAssert.ignoreFailingMessages = true;
            ProjectOperationResult failed = await session.UpdatePreferencesAsync(draft);
            Assert.That(failed.Success, Is.False);
            Assert.That(session.HasPendingSave, Is.True);
            Assert.That(run.FeedbackPaused, Is.True);
            ClickButton(settings, "关闭");
            await WaitForAsync(() => GameEntry.Get<IUIModule>().OpenedWindows.All(window => window.Id != ProjectServices.SettingsWindow));
            Assert.That(run.ActivePanel, Is.EqualTo(ExpeditionPanelKind.Pause));
            Assert.That(FindButton(pause, "继续游戏").interactable, Is.False);
            Assert.That(FindButton(pause, "放弃本局").interactable, Is.False);
            long tick = run.World.TickIndex;
            run.Advance(1f);
            Assert.That(run.World.TickIndex, Is.EqualTo(tick));

            backend.FailWrites = false;
            await WaitForAsync(() => FindButton(pause, "重试保存").interactable);
            ClickButton(pause, "重试保存");
            await WaitForAsync(() => !session.HasUncommittedSave);
            Assert.That(session.Preferences.MusicVolume, Is.EqualTo(0.5f));
            Assert.That(FindButton(pause, "继续游戏").interactable, Is.True);
            Assert.That(FindButton(pause, "放弃本局").interactable, Is.True);
            ClickButton(pause, "继续游戏");
            await WaitForAsync(() => run.ActivePanel == ExpeditionPanelKind.None);
        });

        /// <summary>
        /// 等待通用确认层出现并点击指定选择。
        /// </summary>
        /// <param name="caption">确认层按钮文字。</param>
        /// <returns>等待确认层销毁的任务。</returns>
        private static async UniTask ClickPauseConfirmationAsync(string caption)
        {
            await WaitForAsync(() => GameObject.Find("Modal Dialog") != null);
            GameObject dialog = GameObject.Find("Modal Dialog");
            Button button = dialog.GetComponentsInChildren<Button>(false).Single(item => item.GetComponentInChildren<Text>().text == caption);
            button.onClick.Invoke();
            await WaitForAsync(() => GameObject.Find("Modal Dialog") == null);
        }
    }
}
