using System.Collections;
using System.Collections.Generic;
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
    /// <summary>
    /// 验证真实热更角色动画与战斗状态、暂停和死亡表现的连接。
    /// </summary>
    public sealed partial class ProjectExpeditionPlayModeTests
    {
        /// <summary>
        /// 移动与攻击使用实际动画，暂停冻结姿态，敌人死亡仍短暂显示倒下动作。
        /// </summary>
        /// <returns>加载实际预制体并推进战斗的测试流程。</returns>
        [UnityTest]
        public IEnumerator Animation_FollowsCombatAndFreezesWithRun() => UniTask.ToCoroutine(async () =>
        {
            await SetupAsync(new MemorySaveBackend());
            using var assetsScope = new LifetimeScope("AnimationIntegration");
            ExpeditionVisualAssets assets = await ExpeditionVisualAssets.LoadAsync(assetsScope, GameEntry.Get<IResourceModule>());
            Assert.That(assets, Is.Not.Null);
            Assert.That(ExpeditionRun.TryCreate(ProjectServices.Session, "animation-test", 1, 42,
                new List<ProjectItem>(), out ExpeditionRun run), Is.True);
            using var ownedRun = run;
            using var presentation = new ExpeditionPresentation(run, assets);
            run.AttachScene(assets, presentation.Root);
            Assert.That(run.MarkReady() && run.Activate(), Is.True);
            Assert.That(run.World.TrySetPosition(run.Player, new Vector3(-8f, 0f, -8f)), Is.True);
            run.SetTouchMove(Vector2.right);
            run.Advance(CombatWorld.TickSeconds * 4);
            presentation.Refresh();
            Animator player = presentation.Root.Find("Actor_1").GetComponent<Animator>();
            Assert.That(player.GetCurrentAnimatorStateInfo(0).IsName("Base Layer.Move"), Is.True);
            Transform mesh = player.transform.Find("Mesh");
            Vector3 frozenPosition = mesh.localPosition;
            Quaternion frozenRotation = mesh.localRotation;
            long frozenTick = run.World.TickIndex;
            run.SetPaused(true);
            for (int frame = 0; frame < 3; frame++)
            {
                await UniTask.Yield();
                run.Advance(0.5f);
                presentation.Refresh();
                Assert.That(mesh.localPosition, Is.EqualTo(frozenPosition));
                Assert.That(mesh.localRotation, Is.EqualTo(frozenRotation));
            }
            Assert.That(run.World.TickIndex, Is.EqualTo(frozenTick));
            run.SetPaused(false);
            run.Advance(CombatWorld.TickSeconds);
            presentation.Refresh();
            Assert.That(player.GetCurrentAnimatorStateInfo(0).IsName("Base Layer.Idle"), Is.True);

            var actors = new List<CombatActorSnapshot>();
            run.World.GetActorsSnapshot(actors);
            CombatActorSnapshot enemy = actors.First(actor => actor.ActorId != 1);
            Assert.That(run.World.TrySetPosition(run.Player, enemy.Position - Vector3.forward * 1.4f), Is.True);
            run.World.Step();
            presentation.Refresh();
            Assert.That(player.GetCurrentAnimatorStateInfo(0).IsName("Base Layer.Attack"), Is.True);
            for (int index = 0; index < 4; index++) run.World.Step();
            presentation.Refresh();
            Assert.That(Quaternion.Angle(mesh.localRotation, Quaternion.identity), Is.GreaterThan(1f));
            Assert.That(run.World.TryGetActor(run.Player, out CombatActorSnapshot afterPose), Is.True);
            Vector3 simulationPosition = afterPose.Position;
            presentation.Refresh();
            Assert.That(run.World.TryGetActor(run.Player, out afterPose), Is.True);
            Assert.That(afterPose.Position, Is.EqualTo(simulationPosition), "动画采样不得修改 ECS 坐标。");

            bool sawHit = false;
            bool sawDeath = false;
            for (int tick = 0; tick < 180 && !sawDeath; tick++)
            {
                run.World.Step();
                presentation.Refresh();
                sawHit |= player.GetCurrentAnimatorStateInfo(0).IsName("Base Layer.Hit");
                run.World.GetActorsSnapshot(actors);
                foreach (CombatActorSnapshot actor in actors)
                {
                    if (actor.ActorId == 1 || !actor.Dead) continue;
                    Transform dying = presentation.Root.Find("Actor_" + actor.ActorId);
                    Assert.That(dying.gameObject.activeSelf, Is.True);
                    Assert.That(dying.GetComponent<Animator>().GetCurrentAnimatorStateInfo(0).IsName("Base Layer.Death"), Is.True);
                    for (int frame = 0; frame < 18; frame++) run.World.Step();
                    presentation.Refresh();
                    Assert.That(Quaternion.Angle(dying.Find("Mesh").localRotation, Quaternion.identity), Is.GreaterThan(10f));
                    sawDeath = true;
                    break;
                }
            }
            Assert.That(sawHit, Is.True, "正伤害应驱动受击表现。");
            Assert.That(sawDeath, Is.True, "应观察到敌人的死亡动作。");
        });
    }
}
