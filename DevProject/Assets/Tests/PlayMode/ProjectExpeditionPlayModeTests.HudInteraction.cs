using GameLogic.UI.Battle;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace MiniGame.Tests
{
    /// <summary>
    /// 验证房间战斗 HUD 的信息区与可伸缩操作区。
    /// </summary>
    public sealed partial class ProjectExpeditionPlayModeTests
    {
        /// <summary>
        /// 摘要和操作按钮在竖横屏及强化选择状态下保持在安全区内且不压住房间信息。
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
                float[] heights = { 170f, 52f, 90f, 90f, 90f, 90f };
                foreach (float height in heights)
                {
                    RectTransform child = HudChild(actions, "Action");
                    LayoutElement element = child.gameObject.AddComponent<LayoutElement>();
                    element.minHeight = height;
                    element.preferredHeight = height;
                }
                Text summary = actions.GetChild(0).gameObject.AddComponent<Text>();
                summary.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                summary.fontSize = 22;
                summary.horizontalOverflow = HorizontalWrapMode.Wrap;
                summary.text = "[优秀] 长柄武器 · 武器\n等级 3 · 售价 150 金币\n攻击 +16 · 暴击值 +8\n词条 2 · 高级 1";
                actions.GetChild(5).gameObject.SetActive(false);
                BattleHudLayout layout = host.AddComponent<BattleHudLayout>();
                layout.Configure(header, stick, actions);
                foreach (Vector2 size in new[] { new Vector2(1080f, 1920f), new Vector2(1920f, 1080f),
                    new Vector2(600f, 800f), new Vector2(640f, 480f) })
                {
                    area.sizeDelta = size;
                    area.ForceUpdateRectTransforms();
                    foreach (bool choice in new[] { false, true })
                    {
                        actions.GetChild(5).gameObject.SetActive(choice);
                        layout.Refresh();
                        LayoutRebuilder.ForceRebuildLayoutImmediate(actions);
                        if (size == new Vector2(600f, 800f) && !choice)
                            Assert.That(summary.preferredHeight, Is.LessThanOrEqualTo(summary.GetComponent<LayoutElement>().preferredHeight),
                                "窄屏摘要的实际首选高度不得超过分配空间。");
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
