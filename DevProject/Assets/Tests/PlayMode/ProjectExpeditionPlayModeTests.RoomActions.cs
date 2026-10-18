using System.Collections;
using System.Linq;
using Cysharp.Threading.Tasks;
using GameFramework;
using GameLogic.Gameplay.Combat;
using GameLogic.Gameplay.Expedition;
using GameLogic.Gameplay.Level;
using GameLogic.Project;
using NUnit.Framework;
using ProjectExtraction.Tests.Shared;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace MiniGame.Tests
{
    /// <summary>
    /// 真实击杀与窗口操作的共享夹具。
    /// </summary>
    public sealed partial class ProjectExpeditionPlayModeTests
    {
        /// <summary>
        /// 用真实基础攻击击败一个已经生成的槽位，所有业务保存完成后再返回。
        /// </summary>
        /// <param name="run">正式激活的单局。</param>
        /// <param name="slotId">计划内稳定槽位身份。</param>
        /// <returns>目标死亡且业务保存完成时为真。</returns>
        private static async UniTask<bool> DefeatSlotAsync(ExpeditionRun run, string slotId)
        {
            RegionSpawnSlotState slot = run.CurrentRoom.Slots.Single(value => value.SlotId == slotId);
            Assert.That(run.World.TryFindActor(slot.CurrentActorId, out CombatEntityRef enemy), Is.True);
            Assert.That(run.World.TryGetActor(run.Player, out CombatActorSnapshot player), Is.True);
            if (player.Attack < 100000) Assert.That(run.World.TryAddAttributeBonus(run.Player, CombatAttributeKind.Attack, 100000), Is.True);
            if (player.Defense < 100000) Assert.That(run.World.TryAddAttributeBonus(run.Player, CombatAttributeKind.Defense, 100000), Is.True);
            for (int tick = 0; tick < 600 && slot.Phase == RegionSpawnPhase.Alive; tick++)
            {
                await WaitRunSavedAsync(run);
                if (slot.Phase != RegionSpawnPhase.Alive) break;
                TryPlacePlayerNearActor(run, enemy);
                run.Advance(CombatWorld.TickSeconds);
            }
            await WaitRunSavedAsync(run);
            Assert.That(slot.Phase, Is.Not.EqualTo(RegionSpawnPhase.Alive));
            Assert.That(slot.LastProcessedLife, Is.EqualTo(slot.LifeSequence), "目标死亡收益必须已经确认。");
            return true;
        }

        /// <summary>
        /// 使用共享几何采样在目标的实际近战距离内尝试合法玩家站位。
        /// </summary>
        /// <param name="run">处于完整战斗边界的测试单局。</param>
        /// <param name="target">同地图存活目标。</param>
        /// <returns>权威碰撞入口接受某个候选站位时为真。</returns>
        private static bool TryPlacePlayerNearActor(ExpeditionRun run, CombatEntityRef target) =>
            CombatTestPositioning.TryPlaceAdjacent(run.World, run.Player, target);

        /// <summary>
        /// 触发真实窗口中指定文字的按钮。
        /// </summary>
        /// <param name="window">已打开的窗口。</param>
        /// <param name="caption">按钮文字。</param>
        private static void ClickButton(GameWindow window, string caption)
        {
            Button button = window.RootTransform.GetComponentsInChildren<Button>(false).Single(item =>
                item.GetComponentInChildren<Text>().text == caption || item.GetComponentInChildren<Text>().text.StartsWith(caption + " · "));
            Assert.That(button.interactable, Is.True);
            button.onClick.Invoke();
        }

    }
}
