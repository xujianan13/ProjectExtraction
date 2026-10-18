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
using UnityEngine.TestTools;

namespace MiniGame.Tests
{
    /// <summary>
    /// 验证不同武器进入正式准入关卡后的属性与范围。
    /// </summary>
    public sealed partial class ProjectExpeditionPlayModeTests
    {


        /// <summary>
        /// 不同携入武器建立对应普攻，关卡背包中的其他武器不改变固定战斗属性。
        /// </summary>
        /// <returns>三类武器准入与固定装备验证任务。</returns>
        [UnityTest]
        public IEnumerator ExpandedWeapons_EntryEquipmentDeterminesBasicSkill() => UniTask.ToCoroutine(async () =>
        {
            foreach (string id in new[] { "ruin_heavy_blade", "ruin_poleblade", "guard_blade" })
            {
                await SetupAsync(new MemorySaveBackend());
                ProjectItem starter = ProjectEquipmentRules.CreateConfiguredItem(new System.Random(24), 1, ProjectItemQuality.Common, id);
                starter.Equipped = true;
                ExpeditionRun run = await StartWithEquipmentFixturesAsync(starter, new ProjectItem[0]);
                Assert.That(ProjectWeaponCombat.TryGetBasicSkill(run.EntryEquipment, out int basic), Is.True);
                var skills = new List<CombatSkillSnapshot>();
                run.World.GetSkillsSnapshot(run.Player, skills);
                Assert.That(skills.Select(skill => skill.SkillId), Is.EqualTo(ProjectWeaponCombat.CreateSkillLoadout(basic)));
                Assert.That(run.Inventory, Is.Empty);
                Assert.That(run.EntryEquipment.Single().Id, Is.EqualTo(starter.Id));
                Assert.That(run.World.Faulted, Is.False);
            }
        });

        /// <summary>
        /// 通过正式准入携带固定武器，额外物品通过实际掉落领取入口进入关卡背包。
        /// </summary>
        /// <param name="starter">唯一穿戴的初始武器。</param>
        /// <param name="fixtures">只用于本项测试的额外物品。</param>
        /// <returns>入场和领取保存均完成的正式单局。</returns>
        private static async UniTask<ExpeditionRun> StartWithEquipmentFixturesAsync(ProjectItem starter, IReadOnlyList<ProjectItem> fixtures)
        {
            ProjectSaveData initial = ProjectSaveData.CreateDefault();
            initial.Warehouse.Clear();
            initial.Warehouse.Add(starter.Copy());
            Assert.That(await ProjectServices.Coordinator.CloseAsync(), Is.True);
            await SetupAsync(new MemorySaveBackend(), initial);
            Assert.That(await ProjectServices.Coordinator.StartRunAsync(1), Is.True);
            ExpeditionRun run = ProjectServices.Session.CurrentRun;
            foreach (ProjectItem item in fixtures)
            {
                ExpeditionDrop drop = AddInteractionDrop(run, item.Id, InputPersistencePlayer(run).Position);
                drop.Item = item.Copy();
                drop.Item.Equipped = false;
                run.Advance(CombatWorld.TickSeconds);
                SelectInteractionLoot(run, item.Id);
                run.RequestInteraction();
                run.Advance(CombatWorld.TickSeconds);
                await WaitRunSavedAsync(run);
                Assert.That(run.Inventory.Count(value => value.Id == item.Id), Is.EqualTo(1));
            }
            return run;
        }
    }
}
