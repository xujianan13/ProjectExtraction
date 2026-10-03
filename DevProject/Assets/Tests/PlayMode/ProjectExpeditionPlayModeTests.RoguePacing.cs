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
    /// 验证真实清房事件接入强化保底、暂停与回访去重。
    /// </summary>
    public sealed partial class ProjectExpeditionPlayModeTests
    {
        /// <summary>
        /// 首个小怪房即使基础概率为零也只发放一次奖励，并暂停至选择完成。
        /// </summary>
        /// <returns>真实房间、战斗和三选一的逐帧测试。</returns>
        [UnityTest]
        public IEnumerator RoguePacing_FirstMobClearRewardsOnceAndPausesBattle() => UniTask.ToCoroutine(async () =>
        {
            await SetupAsync(new MemorySaveBackend());
            using var scope = new LifetimeScope("RoguePacingIntegration");
            ExpeditionVisualAssets assets = await ExpeditionVisualAssets.LoadAsync(scope, GameEntry.Get<IResourceModule>());
            var policy = ProjectRulesConfig.RoguePolicy;
            double originalChance = policy.MobClearChance;
            scope.Defer(() => policy.MobClearChance = originalChance);
            policy.MobClearChance = 0d;
            Assert.That(ExpeditionRun.TryCreate(ProjectServices.Session, "pacing-first-room", 1, 42,
                new List<ProjectItem>(), out ExpeditionRun run), Is.True);
            using var ownedRun = run;
            run.AttachScene(assets);
            Assert.That(run.MarkReady() && run.Activate(), Is.True);
            var room = run.CurrentRoom;
            Assert.That(ProjectRulesConfig.RoguePolicy.FirstMobClearReward, Is.True);
            Assert.That(run.World.TryAddAttributeBonus(run.Player, CombatAttributeKind.Attack, 10000f), Is.True);
            Assert.That(run.World.TryAddAttributeBonus(run.Player, CombatAttributeKind.Defense, 10000f), Is.True);
            var actors = new List<CombatActorSnapshot>();
            for (int tick = 0; tick < 240 && !room.IsCleared; tick++)
            {
                run.World.GetActorsSnapshot(actors);
                CombatActorSnapshot target = actors.FirstOrDefault(actor => actor.RoomId == room.RoomId && actor.ActorId != 1 && !actor.Dead);
                if (target.ActorId != 0)
                    Assert.That(run.World.TrySetPosition(run.Player, target.Position - Vector3.forward * 1.1f), Is.True);
                run.Advance(CombatWorld.TickSeconds);
            }
            Assert.That(room.IsCleared, Is.True);
            Assert.That(run.IsChoosingRogue, Is.True);
            Assert.That(run.PendingRogueChoice.SourceId, Is.EqualTo("clear:" + room.RoomId));
            long pausedTick = run.World.TickIndex;
            run.Advance(1f);
            Assert.That(run.World.TickIndex, Is.EqualTo(pausedTick));
            var offer = run.PendingRogueChoice;
            Assert.That(run.TryChooseRogue(offer.OfferId, offer.Revision, 0), Is.True);
            Assert.That(run.TryChooseRogue(offer.OfferId, offer.Revision, 0), Is.False);
            MoveToRoom(run, room.Exits.Values.First());
            MoveToRoom(run, room.RoomId);
            Assert.That(run.IsChoosingRogue, Is.False);
            Assert.That(room.IsCleared, Is.True);
        });
    }
}
