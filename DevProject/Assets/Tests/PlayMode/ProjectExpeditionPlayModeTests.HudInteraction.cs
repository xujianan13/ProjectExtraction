using System.Collections;
using System.Linq;
using Cysharp.Threading.Tasks;
using GameFramework;
using GameLogic.Gameplay.Expedition;
using GameLogic.Project;
using GameLogic.UI.Battle;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace MiniGame.Tests
{
    /// <summary>
    /// 验证房间战斗 HUD 的信息区与可伸缩操作区。
    /// </summary>
    public sealed partial class ProjectExpeditionPlayModeTests
    {
        /// <summary>
        /// 主界面只保留移动和三个菜单入口，真实菜单按钮打开暂停面板并可正常返回。
        /// </summary>
        /// <returns>真实 HUD 与三个菜单按钮验证任务。</returns>
        [UnityTest]
        public IEnumerator HudActions_OnlyMovementAndMenuEntriesRemainFunctional() => UniTask.ToCoroutine(async () =>
        {
            await SetupAsync(new MemorySaveBackend());
            Assert.That(await ProjectServices.Coordinator.StartRunAsync(1), Is.True);
            ExpeditionRun run = ProjectServices.Session.CurrentRun;
            GameWindow hud = await WaitForWindowAsync(ProjectServices.BattleHudWindow);
            await WaitRunSavedAsync(run);
            string[] labels = hud.Root.GetComponentsInChildren<UnityEngine.UI.Button>(false)
                .Select(button => button.GetComponentInChildren<UnityEngine.UI.Text>().text).ToArray();
            Assert.That(labels, Is.EquivalentTo(new[] { "背包", "成长与进度", "暂停" }));
            Assert.That(hud.Root.GetComponentInChildren<ExpeditionJoystick>(false).enabled, Is.True);
            foreach (var menu in new[] { (caption: "背包", id: ProjectServices.ExpeditionInventoryWindow, kind: ExpeditionPanelKind.Inventory),
                (caption: "成长与进度", id: ProjectServices.ProgressionWindow, kind: ExpeditionPanelKind.Progression),
                (caption: "暂停", id: ProjectServices.PauseWindow, kind: ExpeditionPanelKind.Pause) })
            {
                ClickButton(hud, menu.caption);
                GameWindow panel = await WaitForWindowAsync(menu.id);
                Assert.That(run.ActivePanel, Is.EqualTo(menu.kind));
                Assert.That(run.CanAcceptPlayerInput, Is.False);
                ClickButton(panel, menu.kind == ExpeditionPanelKind.Pause ? "继续游戏" : "关闭");
                await WaitForAsync(() => run.CanAcceptPlayerInput);
            }
        });

        /// <summary>
        /// 菜单按钮在竖横屏及待选奖励状态下保持在安全区内且不压住房间信息。
        /// </summary>
        [Test]
        public void HudActions_FitVisiblePreferredHeightsInPortraitAndLandscape()
        {
            var host = new GameObject("HudActionGeometry", typeof(RectTransform));
            try
            {
                var area = (RectTransform)host.transform;
                RectTransform header = HudChild(area, "Status");
                RectTransform stick = HudChild(area, "MoveStick");
                RectTransform actions = HudChild(area, "ControlActions");
                actions.gameObject.AddComponent<VerticalLayoutGroup>().spacing = 8f;
                float[] heights = { 90f, 90f, 90f };
                foreach (float height in heights)
                {
                    RectTransform child = HudChild(actions, "Action");
                    LayoutElement element = child.gameObject.AddComponent<LayoutElement>();
                    element.minHeight = height;
                    element.preferredHeight = height;
                }
                UnityEngine.UI.Text caption = actions.GetChild(0).gameObject.AddComponent<UnityEngine.UI.Text>();
                caption.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                caption.fontSize = 32;
                caption.text = "背包";
                actions.GetChild(2).gameObject.SetActive(false);
                BattleHudLayout layout = host.AddComponent<BattleHudLayout>();
                layout.Configure(header, stick, actions);
                foreach (Vector2 size in new[] { new Vector2(1080f, 1920f), new Vector2(1920f, 1080f),
                    new Vector2(600f, 800f), new Vector2(640f, 480f) })
                {
                    area.sizeDelta = size;
                    area.ForceUpdateRectTransforms();
                    foreach (bool choice in new[] { false, true })
                    {
                        actions.GetChild(2).gameObject.SetActive(choice);
                        layout.Refresh();
                        LayoutRebuilder.ForceRebuildLayoutImmediate(actions);
                        Assert.That(caption.fontSize, Is.GreaterThanOrEqualTo(10), "窄屏菜单文字应保留可读字号。");
                        Rect bounds = HudRect(area, actions);
                        Assert.That(bounds.yMin, Is.GreaterThanOrEqualTo(area.rect.yMin), size.ToString());
                        Assert.That(bounds.yMax, Is.LessThanOrEqualTo(area.rect.yMax), size.ToString());
                        Assert.That(bounds.Overlaps(HudRect(area, header)), Is.False, size.ToString());
                        foreach (Transform child in actions)
                        {
                            if (!child.gameObject.activeSelf) continue;
                            LayoutElement element = child.GetComponent<LayoutElement>();
                            Assert.That(element.minHeight, Is.EqualTo(element.preferredHeight).Within(0.01f));
                        }
                    }
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        /// <summary>
        /// 创建几何布局测试节点。
        /// </summary>
        /// <param name="parent">父级矩形。</param>
        /// <param name="name">节点名称。</param>
        /// <returns>已挂到父节点的矩形。</returns>
        private static RectTransform HudChild(RectTransform parent, string name)
        {
            var child = new GameObject(name, typeof(RectTransform));
            child.transform.SetParent(parent, false);
            return (RectTransform)child.transform;
        }

        /// <summary>
        /// 将控件角点换算到安全区坐标。
        /// </summary>
        /// <param name="area">安全区矩形。</param>
        /// <param name="child">待检查控件。</param>
        /// <returns>安全区内的控件边界。</returns>
        private static Rect HudRect(RectTransform area, RectTransform child)
        {
            var corners = new Vector3[4];
            child.GetWorldCorners(corners);
            Vector3 lower = area.InverseTransformPoint(corners[0]);
            Vector3 upper = area.InverseTransformPoint(corners[2]);
            return Rect.MinMaxRect(lower.x, lower.y, upper.x, upper.y);
        }
    }
}
