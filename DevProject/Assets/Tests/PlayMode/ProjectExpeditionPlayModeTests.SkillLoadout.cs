using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using GameFramework;
using GameLogic.Gameplay.Combat;
using GameLogic.Gameplay.Expedition;
using GameLogic.Project;
using GameLogic.Tables;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MiniGame.Tests
{
    public sealed partial class ProjectExpeditionPlayModeTests
    {
        /// <summary>
        /// 真实单局安装首轮四技能，暂停冻结辅助与冷却，广告复活维护唯一剑阵。
        /// </summary>
        /// <returns>真实场景、暂停和广告复活的逐帧验证。</returns>
        [UnityTest]
        public IEnumerator SkillLoadout_DefaultSkillsPauseAndAdRevivalKeepUniqueOrbit() => UniTask.ToCoroutine(async () =>
        {
            await SetupAsync(new MemorySaveBackend());
            Assert.That(await ProjectServices.Coordinator.StartRunAsync(1), Is.True);
            ExpeditionRun run = ProjectServices.Session.CurrentRun;
            Assert.That(ProjectContentConfig.Player.SkillIDs, Is.EqualTo(new[] { 100, 101, 102, 103 }));
            Assert.That(ProjectWeaponCombat.TryGetBasicSkill(run.EntryEquipment, out int basicSkill), Is.True);
            var skills = new List<CombatSkillSnapshot>();
            run.World.GetSkillsSnapshot(run.Player, skills);
            Assert.That(skills.Select(skill => skill.SkillId), Is.EqualTo(ProjectWeaponCombat.CreateSkillLoadout(basicSkill)));
            run.Advance(CombatWorld.TickSeconds);
            run.World.GetSkillsSnapshot(run.Player, skills);
            Assert.That(skills.Single(skill => skill.SkillId == 102).IsPersistentActive, Is.True);
            AssertSkillOrbitCount(run, 3);
            Assert.That(run.TryOpenPause(), Is.True);
            long tick = run.World.TickIndex;
            var before = skills.Select(skill => (skill.SkillId, skill.CooldownTicks, skill.IsPersistentActive)).ToArray();
            var projectiles = new List<CombatProjectileSnapshot>();
            run.World.GetProjectilesSnapshot(projectiles);
            Vector3[] positions = projectiles.Select(projectile => projectile.Position).ToArray();
            run.Advance(10f);
            run.World.GetSkillsSnapshot(run.Player, skills);
            run.World.GetProjectilesSnapshot(projectiles);
            Assert.That(run.World.TickIndex, Is.EqualTo(tick));
            Assert.That(skills.Select(skill => (skill.SkillId, skill.CooldownTicks, skill.IsPersistentActive)), Is.EqualTo(before));
            Assert.That(projectiles.Select(projectile => projectile.Position), Is.EqualTo(positions));
            run.ClosePanel(ExpeditionPanelKind.Pause);
            run.Advance(CombatWorld.TickSeconds);
            TriggerDeath(run);
            await WaitForDeathChoiceAsync(run);
            run.World.GetSkillsSnapshot(run.Player, skills);
            var deadCooldowns = skills.Select(skill => (skill.SkillId, skill.CooldownTicks)).ToArray();
            AssertSkillOrbitCount(run, 0);
            Assert.That(await run.RequestAdReviveAsync(), Is.True);
            await WaitRunSavedAsync(run);
            run.World.GetSkillsSnapshot(run.Player, skills);
            Assert.That(skills.Select(skill => (skill.SkillId, skill.CooldownTicks)), Is.EqualTo(deadCooldowns));
            run.Advance(CombatWorld.TickSeconds);
            AssertSkillOrbitCount(run, 3);
            run.World.GetSkillsSnapshot(run.Player, skills);
            Assert.That(skills.Single(skill => skill.SkillId == 102).IsPersistentActive, Is.True);
            var buffs = new List<CombatBuffSnapshot>();
            run.World.GetBuffsSnapshot(buffs);
            Assert.That(buffs.Count(buff => buff.ActorId == 1 && buff.Lifetime == CombatBuffLifetime.Persistent), Is.EqualTo(1));
        });

        /// <summary>
        /// 真实切区保留技能列表和常驻技能冷却，恢复拒绝与装备不一致的合法普攻编号。
        /// </summary>
        /// <returns>跨区保存、快照恢复和装备契约门禁的逐帧验证。</returns>
        [UnityTest]
        public IEnumerator SkillLoadout_RegionTransferPreservesStateAndRestoreRejectsWeaponMismatch() => UniTask.ToCoroutine(async () =>
        {
            await SetupAsync(new MemorySaveBackend());
            Assert.That(await ProjectServices.Coordinator.StartRunAsync(1), Is.True);
            ExpeditionRun run = ProjectServices.Session.CurrentRun;
            run.Advance(CombatWorld.TickSeconds);
            Assert.That(run.TryCaptureState(out var saved, out string error), Is.True, error);
            Assert.That(ExpeditionRun.TryRestore(ProjectServices.Session, saved, out var restored, out error), Is.True, error);
            restored.Dispose();
            CombatPlayerCheckpoint player = saved.PlayerState;
            int alternate = player.Spec.BasicSkillId == 10 ? 11 : 10;
            player.Spec.BasicSkillId = alternate;
            player.BaseSpec.BasicSkillId = alternate;
            player.Spec.SkillIds[0] = alternate;
            player.BaseSpec.SkillIds[0] = alternate;
            Assert.That(ExpeditionRun.TryRestore(ProjectServices.Session, saved, out var rejected, out error), Is.False);
            Assert.That(rejected, Is.Null);
            Assert.That(error, Is.Not.Null.And.Not.Empty);

            await EnterExtractionConfirmationAsync(run);
            var skills = new List<CombatSkillSnapshot>();
            run.World.GetSkillsSnapshot(run.Player, skills);
            int[] skillIds = skills.Select(skill => skill.SkillId).ToArray();
            int remaining = skills.Single(skill => skill.SkillId == 102).CooldownTicks;
            int region = run.CurrentRoomIndex;
            await ClickExtractionConfirmationAsync("进入下一房");
            await WaitRunSavedAsync(run);
            run.World.GetSkillsSnapshot(run.Player, skills);
            Assert.That(run.CurrentRoomIndex, Is.EqualTo(region + 1));
            Assert.That(skills.Select(skill => skill.SkillId), Is.EqualTo(skillIds));
            Assert.That(skills.Single(skill => skill.SkillId == 102).CooldownTicks, Is.EqualTo(remaining));
            run.Advance(CombatWorld.TickSeconds);
            AssertSkillOrbitCount(run, 3);
            run.World.GetSkillsSnapshot(run.Player, skills);
            Assert.That(skills.Single(skill => skill.SkillId == 102).IsPersistentActive, Is.True);
            Assert.That(skills.Single(skill => skill.SkillId == 102).CooldownTicks, Is.EqualTo(Mathf.Max(0, remaining - 1)));
        });

        /// <summary>
        /// 检查当前地图中首轮剑阵的可见攻击体数量。
        /// </summary>
        /// <param name="run">当前真实单局。</param>
        /// <param name="count">期望剑阵攻击体数量。</param>
        private static void AssertSkillOrbitCount(ExpeditionRun run, int count)
        {
            var projectiles = new List<CombatProjectileSnapshot>();
            run.World.GetProjectilesSnapshot(projectiles);
            Assert.That(projectiles.Count(projectile => GameTables.CfgCombatProjectiles.GetById(projectile.ProjectileId).Motion ==
                (int)ProjectileMotionKind.Orbit), Is.EqualTo(count));
        }
    }
}
