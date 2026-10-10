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
using UnityEngine.EventSystems;
using UnityEngine.TestTools;

namespace MiniGame.Tests
{
    public sealed partial class ProjectExpeditionPlayModeTests
    {
        /// <summary>
        /// 进入撤离圈只暂停待确认，取消后留在圈内不重复提示，交互和重新进入均可再次确认。
        /// </summary>
        /// <returns>真实确认框、交互及范围变化的逐帧测试。</returns>
        [UnityTest]
        public IEnumerator ExtractionConfirmation_CancelKeepsRunAndReopensOnlyOnExplicitInteractionOrReentry() => UniTask.ToCoroutine(async () =>
        {
            await SetupAsync(new MemorySaveBackend());
            Assert.That(await ProjectServices.Coordinator.StartRunAsync(1), Is.True);
            ProjectSession session = ProjectServices.Session;
            ExpeditionRun run = session.CurrentRun;
            await EnterExtractionConfirmationAsync(run);
            Assert.That(run.IsRunning, Is.True);
            Assert.That(ProjectServices.Coordinator.Phase, Is.EqualTo(ProjectRunPhase.Playing));
            Assert.That(session.RunStage, Is.EqualTo(ProjectRunStage.Running));
            Assert.That(session.RecordedOutcome, Is.Null);
            Assert.That(session.LastResult, Is.Null);
            Assert.That(run.CanAcceptPlayerInput, Is.False);
            Assert.That(run.FeedbackPaused, Is.True);
            long tick = run.World.TickIndex;
            run.SetTouchMove(Vector2.right);
            run.Advance(1f);
            Assert.That(run.World.TickIndex, Is.EqualTo(tick));

            await ClickExtractionConfirmationAsync("继续探索");
            Assert.That(run.ActivePanel, Is.EqualTo(ExpeditionPanelKind.None));
            Assert.That(run.CanAcceptPlayerInput, Is.True);
            Assert.That(run.FeedbackPaused, Is.False);
            Assert.That(run.World.TryGetActor(run.Player, out CombatActorSnapshot before), Is.True);
            run.Advance(CombatWorld.TickSeconds * 3);
            await UniTask.Yield();
            Assert.That(run.ActivePanel, Is.EqualTo(ExpeditionPanelKind.None));
            Assert.That(GameObject.Find("Modal Dialog"), Is.Null, "取消后仍停在撤离圈内不能逐帧重新弹出。");
            Assert.That(run.World.TryGetActor(run.Player, out CombatActorSnapshot after), Is.True);
            Assert.That(after.Position, Is.EqualTo(before.Position), "进入确认时应清除尚未执行的移动输入。");
            string extractionId = "extraction:" + run.CurrentRoom.RoomId;
            for (int index = 0; index < run.InteractionCount && run.SelectedInteractionId != extractionId; index++) run.CycleInteraction();
            Assert.That(run.SelectedInteractionId, Is.EqualTo(extractionId));
            run.RequestInteraction();
            run.Advance(CombatWorld.TickSeconds);
            await WaitForExtractionConfirmationAsync();
            Assert.That(run.ActivePanel, Is.EqualTo(ExpeditionPanelKind.ExtractionConfirmation));
            await ClickExtractionConfirmationAsync("继续探索");

            MoveOutsideExtraction(run);
            run.Advance(CombatWorld.TickSeconds);
            Assert.That(run.ActivePanel, Is.EqualTo(ExpeditionPanelKind.None));
            MoveToExtractionCenter(run);
            run.Advance(CombatWorld.TickSeconds);
            await WaitForExtractionConfirmationAsync();
            Assert.That(run.ActivePanel, Is.EqualTo(ExpeditionPanelKind.ExtractionConfirmation));
            Assert.That(run.IsRunning, Is.True);
            Assert.That(session.LastResult, Is.Null);
            await ClickExtractionConfirmationAsync("继续探索");
        });

        /// <summary>
        /// 真实撤离按钮只兑现一次终局，并保留安全箱与箱外装备以及完整撤离经验。
        /// </summary>
        /// <returns>确认、持久结算和返场收益的逐帧测试。</returns>
        [UnityTest]
        public IEnumerator ExtractionConfirmation_ConfirmRetainsAllItemsAndAwardsExperienceOnce() => UniTask.ToCoroutine(async () =>
        {
            ProjectSaveData initial = ProjectSaveData.CreateDefault();
            ProjectItem armor = ProjectEquipmentRules.CreateEquipment(new System.Random(173), ProjectItemType.Armor, 1, ProjectItemQuality.Common);
            armor.Equipped = true;
            initial.Warehouse.Add(armor);
            string starterId = initial.Warehouse.Single(item => item.Equipped && item.Type == ProjectItemType.Weapon).Id;
            await SetupAsync(new MemorySaveBackend(), initial);
            ProjectSession session = ProjectServices.Session;
            Assert.That(await ProjectServices.Coordinator.StartRunAsync(1), Is.True);
            ExpeditionRun run = session.CurrentRun;
            Assert.That(run.TryOpenInventory(), Is.True);
            await WaitForWindowAsync(ProjectServices.ExpeditionInventoryWindow);
            Assert.That(run.TryEquip(starterId), Is.True);
            Assert.That(run.ToggleSafe(starterId), Is.True);
            await GameEntry.Get<IUIModule>().CloseWindowAsync(ProjectServices.ExpeditionInventoryWindow);
            await EnterExtractionConfirmationAsync(run);
            string runId = run.RunId;
            string[] retainedIds = run.Inventory.Select(item => item.Id).ToArray();
            var expected = initial.Copy();
            ProjectProfileRules.AddExperience(expected, run.KillExperience + PrototypeRules.ExtractionExperience);
            await ClickExtractionConfirmationAsync("确认撤离");
            Assert.That(run.TryConfirmExtraction(), Is.False, "按钮提交后的同一单局不能再次接受撤离确认。");
            await WaitForPhaseAsync(ProjectRunPhase.ResultsReady);
            ProjectRunResult result = session.LastResult;
            Assert.That(result.Outcome.RunId, Is.EqualTo(runId));
            Assert.That(result.Outcome.Extracted, Is.True);
            Assert.That(result.Outcome.Abandoned, Is.False);
            Assert.That(result.Outcome.ExtractionExperience, Is.EqualTo(PrototypeRules.ExtractionExperience));
            Assert.That(result.Outcome.SafeItemIds, Does.Contain(starterId));
            Assert.That(result.RetainedItems.Select(item => item.Id), Is.EquivalentTo(retainedIds));
            Assert.That(result.LostItems, Is.Empty);
            Assert.That(session.Level, Is.EqualTo(expected.Level));
            Assert.That(session.Experience, Is.EqualTo(expected.Experience));
            Assert.That(session.Warehouse.Select(item => item.Id), Is.EquivalentTo(retainedIds));
            Assert.That(session.RunHistory.Count(item => item.RunId == runId), Is.EqualTo(1));
            Assert.That(run.TryConfirmExtraction(), Is.False);
            Assert.That(await ProjectServices.Coordinator.ReturnHomeAsync(), Is.True);
            Assert.That(run.TryConfirmExtraction(), Is.False);
            Assert.That(session.Level, Is.EqualTo(expected.Level));
            Assert.That(session.Experience, Is.EqualTo(expected.Experience));
            Assert.That(session.Stamina, Is.EqualTo(initial.Stamina - PrototypeRules.EntryCost));
            Assert.That(session.Warehouse.Select(item => item.Id), Is.EquivalentTo(retainedIds));
            Assert.That(session.RunHistory.Count(item => item.RunId == runId), Is.EqualTo(1));
        });

        /// <summary>
        /// 后台暂停和撤离位置失效时拒绝确认，失败的真实按钮不会产生终局。
        /// </summary>
        /// <returns>应用生命周期、权威位置校验和确认框关闭的逐帧测试。</returns>
        [UnityTest]
        public IEnumerator ExtractionConfirmation_BackgroundAndInvalidPositionRejectSettlement() => UniTask.ToCoroutine(async () =>
        {
            await SetupAsync(new MemorySaveBackend());
            Assert.That(await ProjectServices.Coordinator.StartRunAsync(1), Is.True);
            ProjectSession session = ProjectServices.Session;
            ExpeditionRun run = session.CurrentRun;
            Assert.That(run.TryConfirmExtraction(), Is.False, "未打开确认面板时不能直接提交撤离。");
            await EnterExtractionConfirmationAsync(run);
            long tick = run.World.TickIndex;
            await ProjectServices.Coordinator.SetApplicationPausedAsync(true);
            Assert.That(run.TryConfirmExtraction(), Is.False);
            run.Advance(1f);
            Assert.That(run.World.TickIndex, Is.EqualTo(tick));
            Assert.That(run.ActivePanel, Is.EqualTo(ExpeditionPanelKind.ExtractionConfirmation));
            Assert.That(session.RecordedOutcome, Is.Null);
            await ProjectServices.Coordinator.SetApplicationPausedAsync(false);
            Assert.That(run.ActivePanel, Is.EqualTo(ExpeditionPanelKind.ExtractionConfirmation));
            MoveOutsideExtraction(run);
            Assert.That(run.TryConfirmExtraction(), Is.False, "面板打开期间也必须复核最新玩家位置。");
            await ClickExtractionConfirmationAsync("确认撤离");
            Assert.That(run.IsRunning, Is.True);
            Assert.That(run.ActivePanel, Is.EqualTo(ExpeditionPanelKind.None));
            Assert.That(ProjectServices.Coordinator.Phase, Is.EqualTo(ProjectRunPhase.Playing));
            Assert.That(session.RecordedOutcome, Is.Null);
            Assert.That(session.LastResult, Is.Null);
            run.Advance(CombatWorld.TickSeconds);
            MoveToExtractionCenter(run);
            run.Advance(CombatWorld.TickSeconds);
            await WaitForExtractionConfirmationAsync();
            await ClickExtractionConfirmationAsync("继续探索");
        });

        /// <summary>
        /// 沿真实出口到达撤离地图，途中确认均取消，最终进入可见撤离圈并等待真实界面。
        /// </summary>
        /// <param name="run">本局运行对象。</param>
        /// <returns>已经暂停并显示撤离确认的任务。</returns>
        private static async UniTask EnterExtractionConfirmationAsync(ExpeditionRun run)
        {
            LevelRoomState target = run.Maps.Rooms.First(room => room.HasExtractionCandidate);
            var previous = new Dictionary<int, int> { [run.CurrentRoom.RoomId] = 0 };
            var queue = new Queue<int>();
            queue.Enqueue(run.CurrentRoom.RoomId);
            while (queue.Count > 0 && !previous.ContainsKey(target.RoomId))
            {
                int id = queue.Dequeue();
                run.Maps.TryGetRoom(id, out LevelRoomState room);
                foreach (int next in room.Exits.Values)
                    if (!previous.ContainsKey(next)) { previous[next] = id; queue.Enqueue(next); }
            }
            Assert.That(previous.ContainsKey(target.RoomId), Is.True);
            var path = new Stack<int>();
            for (int id = target.RoomId; id != run.CurrentRoom.RoomId; id = previous[id]) path.Push(id);
            while (path.Count > 0)
            {
                if (run.ActivePanel == ExpeditionPanelKind.ExtractionConfirmation) await ClickExtractionConfirmationAsync("继续探索");
                int destination = path.Pop();
                RoomDirection direction = run.CurrentRoom.Exits.First(pair => pair.Value == destination).Key;
                var layout = run.CurrentRoom.Plan.Definition.Layout;
                float edge = (float)(layout.HalfExtent - layout.ExitCenterInset);
                Vector3 position = direction == RoomDirection.North ? new Vector3(0, 0, edge) :
                    direction == RoomDirection.South ? new Vector3(0, 0, -edge) :
                    direction == RoomDirection.East ? new Vector3(edge, 0, 0) : new Vector3(-edge, 0, 0);
                Assert.That(run.World.TrySetPosition(run.Player, position), Is.True);
                run.RequestInteraction();
                run.Advance(CombatWorld.TickSeconds);
                Assert.That(run.CurrentRoom.RoomId, Is.EqualTo(destination));
            }
            target.MarkCleared();
            if (run.ActivePanel != ExpeditionPanelKind.ExtractionConfirmation)
            {
                MoveToExtractionCenter(run);
                run.Advance(CombatWorld.TickSeconds);
            }
            Assert.That(run.ActivePanel, Is.EqualTo(ExpeditionPanelKind.ExtractionConfirmation));
            await WaitForExtractionConfirmationAsync();
        }

        /// <summary>
        /// 将角色放在当前房间配置的撤离点中心。
        /// </summary>
        /// <param name="run">本局运行对象。</param>
        private static void MoveToExtractionCenter(ExpeditionRun run)
        {
            var layout = run.CurrentRoom.Plan.Definition.Layout;
            Assert.That(run.World.TrySetPosition(run.Player, new Vector3((float)layout.ExtractionX, 0, (float)layout.ExtractionZ)), Is.True);
        }

        /// <summary>
        /// 选择一个合法出口作为圈外位置，避免依赖随机障碍布局中的固定空地。
        /// </summary>
        /// <param name="run">本局运行对象。</param>
        private static void MoveOutsideExtraction(ExpeditionRun run)
        {
            var layout = run.CurrentRoom.Plan.Definition.Layout;
            float edge = (float)(layout.HalfExtent - layout.ExitCenterInset);
            Vector3 center = new Vector3((float)layout.ExtractionX, 0, (float)layout.ExtractionZ);
            foreach (RoomDirection direction in run.CurrentRoom.Exits.Keys)
            {
                Vector3 position = direction == RoomDirection.North ? new Vector3(0, 0, edge) :
                    direction == RoomDirection.South ? new Vector3(0, 0, -edge) :
                    direction == RoomDirection.East ? new Vector3(edge, 0, 0) : new Vector3(-edge, 0, 0);
                if (Vector3.Distance(position, center) > (float)layout.ObjectInteractionRadius && run.World.TrySetPosition(run.Player, position)) return;
            }
            Assert.Fail("撤离地图应有至少一个可站立且位于撤离圈外的连通出口。");
        }

        /// <summary>
        /// 等待撤离确认框并验证完整提示及两个选择按钮。
        /// </summary>
        /// <returns>已显示的通用确认层对象。</returns>
        private static async UniTask<GameObject> WaitForExtractionConfirmationAsync()
        {
            await WaitForAsync(() => GameObject.Find("Modal Dialog") != null);
            GameObject dialog = GameObject.Find("Modal Dialog");
            string[] captions = dialog.GetComponentsInChildren<UnityEngine.UI.Text>(false).Select(text => text.text).ToArray();
            Assert.That(captions, Does.Contain("确认撤离？"));
            Assert.That(captions, Does.Contain("撤离后将结束本局，带回背包和安全箱中的全部物品，并获得撤离经验。是否现在撤离？"));
            Assert.That(captions, Does.Contain("确认撤离"));
            Assert.That(captions, Does.Contain("继续探索"));
            return dialog;
        }

        /// <summary>
        /// 点击真实撤离确认框的指定按钮并等待确认层销毁。
        /// </summary>
        /// <param name="caption">确认框按钮文字。</param>
        /// <returns>等待本次确认界面完成的任务。</returns>
        private static async UniTask ClickExtractionConfirmationAsync(string caption)
        {
            GameObject dialog = await WaitForExtractionConfirmationAsync();
            UnityEngine.UI.Button button = dialog.GetComponentsInChildren<UnityEngine.UI.Button>(false)
                .Single(item => item.GetComponentInChildren<UnityEngine.UI.Text>().text == caption);
            await UniTask.NextFrame(PlayerLoopTiming.LastPostLateUpdate);
            Canvas.ForceUpdateCanvases();
            Assert.That(EventSystem.current, Is.Not.Null);
            var buttonRect = (RectTransform)button.transform;
            var pointer = new PointerEventData(EventSystem.current)
            {
                button = PointerEventData.InputButton.Left,
                position = RectTransformUtility.WorldToScreenPoint(null, buttonRect.TransformPoint(buttonRect.rect.center))
            };
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(pointer, hits);
            Assert.That(hits, Is.Not.Empty);
            GameObject clickTarget = ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject);
            Assert.That(clickTarget, Is.SameAs(button.gameObject), "撤离确认按钮不能被其他图层拦截。");
            Assert.That(button.IsInteractable(), Is.True);
            ExecuteEvents.Execute(clickTarget, pointer, ExecuteEvents.pointerClickHandler);
            await WaitForAsync(() => GameObject.Find("Modal Dialog") == null);
        }
    }
}
