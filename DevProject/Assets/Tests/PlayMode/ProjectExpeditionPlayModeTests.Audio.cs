using System;
using System.Collections;
using Cysharp.Threading.Tasks;
using GameFramework;
using GameLogic.Gameplay.Combat;
using GameLogic.Gameplay.Expedition;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MiniGame.Tests
{
    /// <summary>
    /// 使用项目热更资源检查战斗音效只管理自身播放句柄。
    /// </summary>
    public sealed partial class ProjectExpeditionPlayModeTests
    {
        /// <summary>
        /// 验证反馈资源完整加载及暂停、换房和退出的音效所有权。
        /// </summary>
        /// <returns>等待项目资源包和音频异步加载完成。</returns>
        [UnityTest]
        public IEnumerator CombatAudio_UsesHotfixClipsWithoutStoppingExternalChannels() => UniTask.ToCoroutine(async () =>
        {
            await SetupAsync(new MemorySaveBackend());
            IResourceModule resources = GameEntry.Get<IResourceModule>();
            IAudioModule audio = GameEntry.Get<IAudioModule>();
            Assert.That(resources, Is.Not.Null);
            Assert.That(audio, Is.Not.Null);
            using var assetsScope = new LifetimeScope("CombatAudioAssetsTest");
            CombatFeedbackAssets assets = await CombatFeedbackAssets.LoadAsync(assetsScope, resources, default);
            Assert.That(assets, Is.Not.Null, "四个反馈 Prefab 与三段热更音效应完整加载");
            Assert.That(assets.HealthBar, Is.Not.Null);
            Assert.That(assets.DamageText, Is.Not.Null);
            Assert.That(assets.Hit, Is.Not.Null);
            Assert.That(assets.Telegraph, Is.Not.Null);

            const string swing = "SFX_CombatSwing";
            const string hit = "SFX_CombatHit";
            const string critical = "SFX_CombatCritical";
            var clipItems = new[]
            {
                new ResourceGroupDefinition.Item(swing, swing, ResourceGroupDefinition.AssetKind.AudioClip),
                new ResourceGroupDefinition.Item(hit, hit, ResourceGroupDefinition.AssetKind.AudioClip),
                new ResourceGroupDefinition.Item(critical, critical, ResourceGroupDefinition.AssetKind.AudioClip)
            };
            ResourceGroupLease clipGroup = await resources.AcquireGroupAsync(new ResourceGroupDefinition("CombatAudioClipsTest", clipItems));
            Assert.That(clipGroup, Is.Not.Null);
            Assert.That(assetsScope.TryOwn(clipGroup), Is.True);
            foreach (string name in new[] { swing, hit, critical })
            {
                Assert.That(clipGroup.TryGet(name, out AudioClip clip), Is.True, name);
                Assert.That(clip.length, Is.GreaterThan(0f), name);
            }

            AudioHandle uiLoop = await audio.PlayAsync(hit, AudioChannel.Sfx, loop: true);
            AudioHandle musicLoop = await audio.PlayAsync(swing, AudioChannel.Music, loop: true);
            Assert.That(uiLoop.IsValid && audio.IsPlaying(uiLoop), Is.True);
            Assert.That(musicLoop.IsValid && audio.IsPlaying(musicLoop), Is.True);
            int externalSfx = audio.GetActiveCount(AudioChannel.Sfx);
            Assert.That(externalSfx, Is.EqualTo(1));
            var feedback = new CombatFeedbackAudio();
            feedback.Refresh(false, 1);

            feedback.Present(new CombatEvent(CombatEventKind.Cue, 10, 1, 1, 0, 1, 0f, Vector3.zero));
            Assert.That(audio.GetActiveCount(AudioChannel.Sfx), Is.GreaterThan(externalSfx), "挥击应请求战斗 Sfx");
            feedback.Refresh(true, 1);
            await UniTask.WaitUntil(() => audio.GetActiveCount(AudioChannel.Sfx) == externalSfx).Timeout(TimeSpan.FromSeconds(3));
            Assert.That(audio.GetActiveCount(AudioChannel.Sfx), Is.EqualTo(externalSfx));
            Assert.That(audio.IsPlaying(uiLoop) && audio.IsPlaying(musicLoop), Is.True);

            feedback.Present(new CombatEvent(CombatEventKind.Damage, 20, 1, 2, 1, 3, 12f, Vector3.zero));
            Assert.That(audio.GetActiveCount(AudioChannel.Sfx), Is.EqualTo(externalSfx), "暂停期间不应请求新音效");
            feedback.Refresh(false, 1);
            feedback.Present(new CombatEvent(CombatEventKind.Damage, 21, 1, 2, 1, 3, 12f, Vector3.zero));
            Assert.That(audio.GetActiveCount(AudioChannel.Sfx), Is.GreaterThan(externalSfx), "正伤害应请求战斗 Sfx");
            feedback.Refresh(false, 2);
            await UniTask.WaitUntil(() => audio.GetActiveCount(AudioChannel.Sfx) == externalSfx).Timeout(TimeSpan.FromSeconds(3));
            Assert.That(audio.GetActiveCount(AudioChannel.Sfx), Is.EqualTo(externalSfx));
            Assert.That(audio.IsPlaying(uiLoop) && audio.IsPlaying(musicLoop), Is.True);

            feedback.Present(new CombatEvent(CombatEventKind.Damage, 22, 1, 2, 1, 3, 12f, Vector3.zero));
            Assert.That(audio.GetActiveCount(AudioChannel.Sfx), Is.EqualTo(externalSfx), "旧房间事件不应请求新音效");
            feedback.Present(new CombatEvent(CombatEventKind.Damage, 23, 2, 2, 1, 3, 0f, Vector3.zero));
            Assert.That(audio.GetActiveCount(AudioChannel.Sfx), Is.EqualTo(externalSfx), "零伤害不应请求命中音效");
            feedback.Present(new CombatEvent(CombatEventKind.Cue, 24, 2, 1, 0, 1, 0f, Vector3.zero));
            Assert.That(audio.GetActiveCount(AudioChannel.Sfx), Is.GreaterThan(externalSfx));
            feedback.Dispose();
            await UniTask.WaitUntil(() => audio.GetActiveCount(AudioChannel.Sfx) == externalSfx).Timeout(TimeSpan.FromSeconds(3));
            Assert.That(audio.GetActiveCount(AudioChannel.Sfx), Is.EqualTo(externalSfx));
            Assert.That(audio.IsPlaying(uiLoop) && audio.IsPlaying(musicLoop), Is.True);
            audio.Stop(uiLoop);
            audio.Stop(musicLoop);
        });
    }
}
