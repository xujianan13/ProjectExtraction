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

namespace MiniGame.Tests
{
    public sealed partial class ProjectExpeditionPlayModeTests
    {
        /// <summary>
        /// 完整快照恢复同一地图、角色位置、生成计划和未购买库存。
        /// </summary>
        /// <returns>真实资源与恢复单局的验证任务。</returns>
        [UnityTest]
        public IEnumerator RegionState_RoundTripKeepsWorldPlanAndFrozenOffers() => UniTask.ToCoroutine(async () =>
        {
            await SetupAsync(new MemorySaveBackend());
            Assert.That(await ProjectServices.Coordinator.StartRunAsync(1), Is.True);
            ExpeditionRun run = ProjectServices.Session.CurrentRun;
            run.SetTouchMove(Vector2.right);
            run.Advance(CombatWorld.TickSeconds * 5);
            run.SetTouchMove(Vector2.zero);
            Assert.That(run.World.TryStartSkill(run.Player, 1, default), Is.True);
            run.Advance(CombatWorld.TickSeconds);
            await WaitRunSavedAsync(run);
            Assert.That(run.TryCaptureState(out ExpeditionRunSnapshot snapshot, out string error), Is.True, error);
            Assert.That(snapshot.World.Casts, Is.Not.Empty);
            Assert.That(ExpeditionSnapshotCodec.TryEncode(snapshot, out var parts, out error), Is.True, error);
            Assert.That(ExpeditionSnapshotCodec.TryDecode(parts, out var decoded, out error), Is.True, error);
            Assert.That(ExpeditionRun.TryRestore(ProjectServices.Session, decoded, out ExpeditionRun restored, out error), Is.True, error);
            using (restored)
            {
                using var scope = new LifetimeScope("RestoredRegionScene");
                ExpeditionVisualAssets assets = await ExpeditionVisualAssets.LoadAsync(scope, GameEntry.Get<IResourceModule>());
                restored.AttachScene(assets);
                Assert.That(restored.TryCaptureState(out ExpeditionRunSnapshot attached, out error), Is.True, error);
                Assert.That(JsonUtility.ToJson(attached.World), Is.EqualTo(JsonUtility.ToJson(snapshot.World)),
                    "场景绑定不能清空恢复中的施法、位移或动画提示。");
                Assert.That(restored.RunId, Is.EqualTo(run.RunId));
                Assert.That(restored.World.TickIndex, Is.EqualTo(run.World.TickIndex));
                Assert.That(JsonUtility.ToJson(restored.CurrentRegion.Plan), Is.EqualTo(JsonUtility.ToJson(run.CurrentRegion.Plan)));
                Assert.That(restored.World.TryGetActor(restored.Player, out CombatActorSnapshot actual), Is.True);
                Assert.That(run.World.TryGetActor(run.Player, out CombatActorSnapshot expected), Is.True);
                Assert.That(actual.Position, Is.EqualTo(expected.Position));
                Assert.That(actual.Health, Is.EqualTo(expected.Health));
                Assert.That(restored.CurrentRegion.Plan.Npcs.SelectMany(value => value.Offers).Select(value => value.Item.Id),
                    Is.EquivalentTo(run.CurrentRegion.Plan.Npcs.SelectMany(value => value.Offers).Select(value => value.Item.Id)));
            }
        });

        /// <summary>
        /// 未领取的箱内完整物品已经冻结，开箱不触发技能候选且不能重复领取。
        /// </summary>
        /// <returns>真实宝箱与保存门禁验证任务。</returns>
        [UnityTest]
        public IEnumerator RegionChest_UsesFrozenItemsAndDoesNotGrantSkillChoice() => UniTask.ToCoroutine(async () =>
        {
            await SetupAsync(new MemorySaveBackend());
            Assert.That(await ProjectServices.Coordinator.StartRunAsync(1), Is.True);
            ExpeditionRun run = ProjectServices.Session.CurrentRun;
            var chest = run.CurrentRegion.Plan.Chests.First();
            int before = run.Inventory.Count;
            Assert.That(run.World.TrySetPosition(run.Player, chest.Position), Is.True);
            run.Advance(CombatWorld.TickSeconds);
            run.RequestInteraction();
            run.Advance(CombatWorld.TickSeconds);
            await WaitRunSavedAsync(run);
            Assert.That(run.Inventory.Count, Is.EqualTo(before + chest.Items.Count));
            foreach (var item in chest.Items) Assert.That(run.Inventory.Count(value => value.Id == item.Id), Is.EqualTo(1));
            Assert.That(run.PendingRogueChoice, Is.Null);
            run.RequestInteraction();
            run.Advance(CombatWorld.TickSeconds);
            await WaitRunSavedAsync(run);
            Assert.That(run.Inventory.Count, Is.EqualTo(before + chest.Items.Count));
        });
    }
}
