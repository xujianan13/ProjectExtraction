using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using System.Text;
using GameFramework;
using GameLogic.Gameplay.Combat;
using GameLogic.Gameplay.Expedition;
using GameLogic.Gameplay.Level;
using GameLogic.Project;
using GameLogic.UI.Common;
using UnityEngine;

namespace GameLogic.UI.Battle
{
    /// <summary>
    /// 局内生命、房间进度、交互与物品面板入口。
    /// </summary>
    public sealed class BattleHudWindow : GameWindow
    {
        private ExpeditionRun run;
        private ProjectRunCoordinator coordinator;
        private UnityEngine.UI.Text interactionLabel;
        private UnityEngine.UI.Text targetSummary;
        private UnityEngine.UI.Button cycleButton;
        private UnityEngine.UI.Text status;
        private UnityEngine.UI.Text hint;
        private UnityEngine.UI.Text skills;
        private readonly List<CombatSkillSnapshot> skillSnapshots = new();
        private readonly StringBuilder skillSummary = new();
        private UnityEngine.UI.Button resumeChoice;
        private UnityEngine.UI.Button interactButton;
        private ExpeditionJoystick joystick;
        private BattleHudLayout layout;
        private UnityEngine.UI.Button bagButton;
        private UnityEngine.UI.Button overviewButton;
        private UnityEngine.UI.Button pauseButton;
        private string selectedInteractionId;
        private bool openingPanel;
        private ExpeditionPanelKind openedPanel;
        private ExpeditionPanelKind requestedPanel;
        private int panelVersion;

        /// <summary>
        /// 建立代码驱动的 HUD 层级，沿用框架的 Canvas 与 EventSystem。
        /// </summary>
        protected override void BindComponents()
        {
            RectTransform content = ProjectUiFactory.Fill(RootTransform, "BattleHud", Color.clear);
            var header = ProjectUiFactory.Vertical(content, "Status", 5);
            status = ProjectUiFactory.Label(header, "", 34, TextAnchor.UpperLeft, Color.white);
            ProjectUiFactory.Height(status.rectTransform, 104f);
            hint = ProjectUiFactory.Label(header, "", 24, TextAnchor.UpperLeft, Color.white);
            ProjectUiFactory.Height(hint.rectTransform, 104f);
            hint.resizeTextForBestFit = true;
            hint.resizeTextMinSize = 16;
            hint.resizeTextMaxSize = 24;
            skills = ProjectUiFactory.Label(header, string.Empty, 34, TextAnchor.UpperLeft, new Color(0.65f, 0.94f, 1f));
            skills.name = "CombatSkills";
            ProjectUiFactory.Height(skills.rectTransform, 206f);
            skills.resizeTextForBestFit = true;
            skills.resizeTextMinSize = 30;
            skills.resizeTextMaxSize = 34;
            RectTransform stick = CreateDisc(content, "MoveStick", new Color(0.1f, 0.25f, 0.35f, 0.72f));
            joystick = stick.gameObject.AddComponent<ExpeditionJoystick>();
            RectTransform handle = CreateDisc(stick, "Handle", new Color(0.3f, 0.72f, 0.82f, 0.9f));
            handle.anchorMin = Vector2.one * 0.5f;
            handle.anchorMax = Vector2.one * 0.5f;
            handle.sizeDelta = Vector2.one * 100f;
            handle.GetComponent<JoystickDiscGraphic>().raycastTarget = false;
            joystick.SetHandle(handle);
            RectTransform actions = ProjectUiFactory.Vertical(content, "ControlActions", 8f);
            targetSummary = ProjectUiFactory.Label(actions, "附近暂无可交互目标", 22, TextAnchor.UpperLeft, Color.white);
            ProjectUiFactory.Height(targetSummary.rectTransform, 170f);
            cycleButton = ProjectUiFactory.ActionButton(actions, "切换目标 / Tab", CycleInteraction);
            cycleButton.GetComponentInChildren<UnityEngine.UI.Text>().fontSize = 26;
            ProjectUiFactory.Height((RectTransform)cycleButton.transform, 52f);
            actions.GetComponent<UnityEngine.UI.VerticalLayoutGroup>().childAlignment = TextAnchor.LowerCenter;
            interactButton = ProjectUiFactory.ActionButton(actions, "交互 / E", RequestInteraction);
            interactionLabel = interactButton.GetComponentInChildren<UnityEngine.UI.Text>();
            interactionLabel.fontSize = 32;
            ProjectUiFactory.Height((RectTransform)interactButton.transform, 90f);
            bagButton = ProjectUiFactory.ActionButton(actions, "背包", OpenInventory);
            bagButton.GetComponentInChildren<UnityEngine.UI.Text>().fontSize = 32;
            ProjectUiFactory.Height((RectTransform)bagButton.transform, 90f);
            RectTransform menuRow = ProjectUiFactory.Horizontal(actions, "MenuActions", 8f);
            ProjectUiFactory.Height(menuRow, 90f);
            overviewButton = ProjectUiFactory.ActionButton(menuRow, "成长与进度", OpenProgression);
            overviewButton.GetComponentInChildren<UnityEngine.UI.Text>().fontSize = 24;
            pauseButton = ProjectUiFactory.ActionButton(menuRow, "暂停", OpenPause);
            resumeChoice = ProjectUiFactory.ActionButton(actions, "继续选择奖励", OpenPendingChoice);
            resumeChoice.GetComponentInChildren<UnityEngine.UI.Text>().fontSize = 30;
            ProjectUiFactory.Height((RectTransform)resumeChoice.transform, 90f);
            resumeChoice.gameObject.SetActive(false);
            layout = content.gameObject.AddComponent<BattleHudLayout>();
            layout.Configure(header, stick, actions);
        }

        /// <summary>
        /// 绑定当前单局和保存状态。
        /// </summary>
        /// <param name="args">包含单局实例的参数。</param>
        protected override void Open(GameWindowOpenArgs args)
        {
            run = args.UserData as ExpeditionRun;
            if (run == null) return;
            joystick.Bind(run.SetTouchMove, () => run.CanAcceptPlayerInput);
            run.Changed += Refresh;
            coordinator = ProjectServices.Coordinator;
            if (coordinator != null) coordinator.Changed += Refresh;
            ExpeditionRun current = run;
            OpenScope.Defer(() =>
            {
                current.Changed -= Refresh;
                if (coordinator != null) coordinator.Changed -= Refresh;
                joystick.ResetInput();
                joystick.Bind(null);
                current.SetTouchMove(Vector2.zero);
                current.ClosePanel(current.ActivePanel);
                if (ReferenceEquals(run, current)) run = null;
            });
            Refresh();
        }

        /// <summary>
        /// 刷新房间进度、战斗状态与当前可用操作。
        /// </summary>
        private void Refresh()
        {
            if (run == null) return;
            if (requestedPanel != run.ActivePanel)
            {
                requestedPanel = run.ActivePanel;
                panelVersion++;
            }
            if (!run.IsSavingRun)
            {
                run.World.TryGetActor(run.Player, out CombatActorSnapshot player);
                status.text = "房间 " + (run.CurrentRoomIndex + 1) + " / " + run.RoomCount + " · " + RoomKindLabel(run.CurrentRoomKind) +
                    " · 生命 " + Mathf.CeilToInt(player.Health) + " / " + Mathf.CeilToInt(player.MaxHealth) +
                    "\n等级 " + run.PlayerLevel + " · 经验 " + run.PlayerExperience + " / " + run.ExperienceToNextLevel + " · 技能点 " + run.SkillPoints;
            }
            int remaining = 0;
            foreach (RegionSpawnSlotState slot in run.CurrentRoom.Slots)
                if (slot.Phase == RegionSpawnPhase.InitialSpawn || slot.Phase == RegionSpawnPhase.Alive) remaining++;
            string objective = run.CurrentRoomKind == ExpeditionRoomKind.Combat || run.CurrentRoomKind == ExpeditionRoomKind.Boss ?
                "剩余目标 " + remaining : run.CurrentRoomKind == ExpeditionRoomKind.Chest ? "领取本房宝箱后前往上方出口" : "可与商人交易，前往上方出口离开";
            hint.text = objective + "\n" + (run.CurrentRoom.ExitEnabled ? "↑ 上方出口已开放" : "↑ 完成本房目标后开放出口") + "\n" + run.Message;
            run.World.GetSkillsSnapshot(run.Player, skillSnapshots);
            skillSummary.Clear();
            for (int index = 0; index < skillSnapshots.Count; index++)
            {
                if (index > 0) skillSummary.Append('\n');
                skillSummary.Append(CombatSkillText.Summary(skillSnapshots[index]));
            }
            skills.text = skillSummary.ToString();
            RefreshTarget();
            bool choosingDeath = run.IsAwaitingDeathChoice;
            bool choosing = run.IsChoosingReward || choosingDeath;
            resumeChoice.interactable = choosingDeath ? run.CanChooseDeath :
                run.IsChoosingReward && !run.IsSavingRun && run.PendingBossChoice != null;
            resumeChoice.GetComponentInChildren<UnityEngine.UI.Text>().text = choosingDeath ? "继续死亡选择" :
                "继续选择 Boss 战利品";
            if (resumeChoice.gameObject.activeSelf != choosing)
            {
                resumeChoice.gameObject.SetActive(choosing);
                layout.Refresh();
            }
            bool acceptsInput = run.CanAcceptPlayerInput;
            joystick.enabled = acceptsInput;
            if (!acceptsInput) joystick.ResetInput();
            interactButton.interactable = acceptsInput && run.HasInteraction;
            cycleButton.interactable = acceptsInput && run.InteractionCount > 1;
            if (choosing)
            {
                run.SetTouchMove(Vector2.zero);
            }
            interactionLabel.text = run.InteractionLabel;
            bool controlsAvailable = acceptsInput && !run.IsSavingRun;
            bagButton.interactable = overviewButton.interactable = pauseButton.interactable = controlsAvailable;
            if (run.ActivePanel == ExpeditionPanelKind.None) openedPanel = ExpeditionPanelKind.None;
            else if (!openingPanel && openedPanel != run.ActivePanel) OpenActivePanelAsync(run.ActivePanel).Forget();
        }

        /// <summary>
        /// 只在附近目标身份变化时读取掉落副本，其余目标显示单局说明。
        /// </summary>
        private void RefreshTarget()
        {
            string id = run.SelectedInteractionId;
            if (selectedInteractionId != id)
            {
                selectedInteractionId = id;
                if (run.TryGetInteractionItem(out ProjectItem item))
                {
                    targetSummary.text = ProjectEquipmentText.LootSummary(item);
                    return;
                }
                targetSummary.text = string.Empty;
            }
            if (string.IsNullOrEmpty(run.SelectedLootId))
                targetSummary.text = string.IsNullOrEmpty(run.InteractionDescription) ? "附近暂无可交互目标" : run.InteractionDescription;
            else if (string.IsNullOrEmpty(targetSummary.text)) targetSummary.text = "目标信息暂不可用";
        }

        /// <summary>
        /// 请求打开本局背包弹窗。
        /// </summary>
        private void OpenInventory()
        {
            if (coordinator?.Phase == ProjectRunPhase.Playing && run?.TryOpenInventory() == true)
                run.SetTouchMove(Vector2.zero);
        }

        /// <summary>
        /// 返回当前房间类型的玩家可读名称。
        /// </summary>
        /// <param name="kind">当前房间类型。</param>
        /// <returns>房间类型名称。</returns>
        private static string RoomKindLabel(ExpeditionRoomKind kind) => kind == ExpeditionRoomKind.Combat ? "怪物房" :
            kind == ExpeditionRoomKind.Npc ? "商人房" : kind == ExpeditionRoomKind.Chest ? "宝箱房" : "Boss 房";

        /// <summary>
        /// 请求打开永久成长与房间进度弹窗。
        /// </summary>
        private void OpenProgression()
        {
            if (coordinator?.Phase == ProjectRunPhase.Playing && run?.TryOpenProgression() == true)
                run.SetTouchMove(Vector2.zero);
        }

        /// <summary>
        /// 打开暂停菜单并清空摇杆输入。
        /// </summary>
        private void OpenPause()
        {
            if (coordinator?.Phase == ProjectRunPhase.Playing && run?.TryOpenPause() == true)
                run.SetTouchMove(Vector2.zero);
        }

        /// <summary>
        /// 按单局面板状态打开对应的模态窗口，失败时归还面板状态。
        /// </summary>
        /// <param name="kind">准备打开的面板类型。</param>
        /// <returns>窗口打开任务。</returns>
        private async UniTaskVoid OpenActivePanelAsync(ExpeditionPanelKind kind)
        {
            if (run == null || openingPanel) return;
            openingPanel = true;
            ExpeditionRun current = run;
            LifetimeScope scope = OpenScope;
            int version = panelVersion;
            IUIModule ui = GameEntry.Get<IUIModule>();
            int windowId = kind == ExpeditionPanelKind.Inventory ? ProjectServices.ExpeditionInventoryWindow :
                kind == ExpeditionPanelKind.Merchant ? ProjectServices.ExpeditionMerchantWindow :
                kind == ExpeditionPanelKind.Pause ? ProjectServices.PauseWindow :
                kind == ExpeditionPanelKind.DeathChoice ? ProjectServices.DeathChoiceWindow :
                kind == ExpeditionPanelKind.AdExtraction ? ProjectServices.AdExtractionWindow :
                kind == ExpeditionPanelKind.ChestLoot ? ProjectServices.ChestLootWindow :
                kind == ExpeditionPanelKind.ExtractionConfirmation ? ProjectServices.RegionExitWindow : ProjectServices.ProgressionWindow;
            try
            {
                GameWindow opened = ui == null ? null : await ui.OpenWindowAsync(windowId, new GameWindowOpenArgs(current), scope.Token);
                if (opened == null)
                {
                    if (OwnsPanelRequest(current, scope, kind, version)) current.ClosePanel(kind);
                    return;
                }
                if (!OwnsPanelRequest(current, scope, kind, version))
                {
                    if (opened.State == GameWindowState.Opened && current.ActivePanel != kind) await ui.CloseWindowAsync(windowId);
                    return;
                }
                if (opened.State != GameWindowState.Opened || opened.Root == null)
                {
                    current.ClosePanel(kind);
                    await ui.CloseWindowAsync(windowId);
                    return;
                }
                openedPanel = kind;
            }
            catch (System.OperationCanceledException)
            {
                if (OwnsPanelRequest(current, scope, kind, version)) current.ClosePanel(kind);
            }
            catch (System.Exception error)
            {
                GameEntry.Get<ILogModule>()?.Error("BattleHud", "打开局内面板失败：" + error.Message);
                if (OwnsPanelRequest(current, scope, kind, version)) current.ClosePanel(kind);
            }
            finally
            {
                openingPanel = false;
                if (scope.IsActive && ReferenceEquals(run, current) && current.ActivePanel != ExpeditionPanelKind.None &&
                    openedPanel != current.ActivePanel) Refresh();
            }
        }

        /// <summary>
        /// 校验异步打开任务仍拥有当前面板请求，防止同类窗口重开后被旧任务关闭。
        /// </summary>
        /// <param name="current">打开任务绑定的单局。</param>
        /// <param name="scope">发起任务的 HUD 打开作用域。</param>
        /// <param name="kind">任务请求的面板类型。</param>
        /// <param name="version">发起时的面板请求版本。</param>
        /// <returns>同一作用域和单局的当前请求仍由此任务拥有时为真。</returns>
        private bool OwnsPanelRequest(ExpeditionRun current, LifetimeScope scope, ExpeditionPanelKind kind, int version) =>
            scope.IsActive && ReferenceEquals(run, current) && panelVersion == version && current.ActivePanel == kind;

        /// <summary>
        /// 在可操作时切换附近目标。
        /// </summary>
        private void CycleInteraction()
        {
            if (coordinator?.Phase == ProjectRunPhase.Playing && run?.CanAcceptPlayerInput == true && run.InteractionCount > 1)
                run.CycleInteraction();
        }
        /// <summary>
        /// 仅在本局游玩阶段提交交互请求。
        /// </summary>
        private void RequestInteraction()
        {
            if (coordinator?.Phase == ProjectRunPhase.Playing && run?.IsChoosingReward == false) run.RequestInteraction();
        }

        /// <summary>
        /// 在选择窗口被系统关闭后重新打开死亡选择或当前待选奖励。
        /// </summary>
        private void OpenPendingChoice()
        {
            if (run == null) return;
            if (run.IsAwaitingDeathChoice)
            {
                if (run.CanChooseDeath) run.TryOpenDeathChoice();
                return;
            }
            if (!run.IsChoosingReward || run.IsSavingRun || run.PendingBossChoice == null) return;
            IUIModule ui = GameEntry.Get<IUIModule>();
            if (ui != null) ui.OpenWindowAsync(ProjectServices.BossLootWindow,
                new GameWindowOpenArgs(run)).Forget();
        }

        /// <summary>
        /// 为摇杆创建可随 Canvas 缩放的圆形控件。
        /// </summary>
        /// <param name="parent">父节点。</param>
        /// <param name="name">控件名称。</param>
        /// <param name="color">圆盘颜色。</param>
        /// <returns>圆形控件矩形。</returns>
        private static RectTransform CreateDisc(Transform parent, string name, Color color)
        {
            var node = new GameObject(name, typeof(RectTransform), typeof(JoystickDiscGraphic));
            node.transform.SetParent(parent, false);
            node.GetComponent<JoystickDiscGraphic>().color = color;
            return (RectTransform)node.transform;
        }

    }
}
