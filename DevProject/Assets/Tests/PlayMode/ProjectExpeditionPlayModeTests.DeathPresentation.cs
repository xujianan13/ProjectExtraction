using System;
using System.Collections;
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
    /// 验证死亡后先保存终局，再独立播放倒下动作并进入结果页。
    /// </summary>
    public sealed partial class ProjectExpeditionPlayModeTests
    {
        /// <summary>
        /// 死亡表现可被后台暂停，期间模拟与结算奖励不会重复推进。
        /// </summary>
        /// <returns>真实伤害、持久化、动画采样和结果页的逐帧测试。</returns>
        [UnityTest]
        public IEnumerator Death_PersistsBeforeAnimationAndPausesWithoutAdvancingCombat() => UniTask.ToCoroutine(async () =>
        {
            await SetupAsync(new MemorySaveBackend());
            ProjectRunCoordinator coordinator = ProjectServices.Coordinator;
            Assert.That(await coordinator.StartRunAsync(1).Timeout(TimeSpan.FromSeconds(20)), Is.True);
            ExpeditionRun run = ProjectServices.Session.CurrentRun;
            var gameplay = (GameplayModule)GameEntry.Get<IGameplayModule>();
            Assert.That(gameplay.Count, Is.EqualTo(1), "关卡必须由唯一存活玩法驱动。");
            TriggerDeath(run);
            Assert.That(run.World.TryGetActor(run.Player, out CombatActorSnapshot player), Is.True);
            Assert.That(player.Dead, Is.True);
            Assert.That(run.IsPresentingDeath, Is.True);
            Assert.That(coordinator.Phase, Is.EqualTo(ProjectRunPhase.FreezingOutcome));
            long finalTick = run.World.TickIndex;
            Assert.That(coordinator.TryEndRun(run, false), Is.False, "死亡事实不得重复提交。");

            await coordinator.SetApplicationPausedAsync(true);
            float frozenTime = run.DeathPresentationElapsed;
            for (int frame = 0; frame < 5; frame++) gameplay.OnUpdate(1f);
            Assert.That(gameplay.Count, Is.EqualTo(1), "死亡表现期间不得因更新异常退出玩法调度。");
            Assert.That(run.DeathPresentationElapsed, Is.EqualTo(frozenTime));
            await UniTask.WaitUntil(() => ProjectServices.Session.RecordedOutcome != null).Timeout(TimeSpan.FromSeconds(5));
            Assert.That(ProjectServices.Session.RecordedOutcome.Extracted, Is.False);
            Assert.That(ProjectServices.Session.CurrentRun, Is.SameAs(run));
            Assert.That(coordinator.Phase, Is.EqualTo(ProjectRunPhase.FreezingOutcome));
            Assert.That(run.World.TickIndex, Is.EqualTo(finalTick));
            await UniTask.Yield(PlayerLoopTiming.LastPostLateUpdate);
            gameplay.OnLateUpdate(0f);
            Assert.That(gameplay.Count, Is.EqualTo(1), "死亡快照采样失败不能被直接推进 run 时钟掩盖。");
            Transform model = GameObject.Find("ExpeditionPresentation").transform.Find("Actor_1");
            Assert.That(model.gameObject.activeInHierarchy, Is.True);
            Assert.That(model.GetComponent<Animator>().GetCurrentAnimatorStateInfo(0).IsName("Base Layer.Death"), Is.True);
            Quaternion frozenPose = model.Find("Mesh").localRotation;
            await UniTask.Yield(PlayerLoopTiming.LastPostLateUpdate);
            Assert.That(model.Find("Mesh").localRotation, Is.EqualTo(frozenPose));

            await coordinator.SetApplicationPausedAsync(false);
            Assert.That(gameplay.IsPaused, Is.False);
            Assert.That(gameplay.IsApplicationPaused, Is.False);
            gameplay.OnUpdate(1f);
            Assert.That(run.DeathPresentationElapsed, Is.EqualTo(frozenTime + 0.1f).Within(0.0001f), "恢复时限制单帧表现时间。");
            gameplay.OnUpdate(0.1f);
            gameplay.OnUpdate(0.1f);
            gameplay.OnLateUpdate(0f);
            Assert.That(gameplay.Count, Is.EqualTo(1), "终局保存后仍应调度独立死亡表现。");
            await UniTask.Yield(PlayerLoopTiming.LastPostLateUpdate);
            Assert.That(Quaternion.Angle(model.Find("Mesh").localRotation, frozenPose), Is.GreaterThan(10f));
            Assert.That(run.World.TickIndex, Is.EqualTo(finalTick));
            await WaitForPhaseAsync(ProjectRunPhase.ResultsReady);
            Assert.That(run.DeathPresentationElapsed, Is.EqualTo(ExpeditionRun.DeathPresentationDuration));
            Assert.That(ProjectServices.Session.LastResult.Outcome.RunId, Is.EqualTo(run.RunId));
            Assert.That(ProjectServices.Session.LastResult.Outcome.Extracted, Is.False);
            Assert.That(GameEntry.Get<ISceneModule>().CurrentScene, Is.Null);
            Assert.That(GameObject.Find("ExpeditionPresentation"), Is.Null);
        });
    }
}
