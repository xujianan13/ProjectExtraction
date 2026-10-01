#if UNITY_EDITOR
using System.Collections;
using GameFramework;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace MiniGame.Tests
{
    /// <summary>
    /// 在真实角色控制器和网格曲线上验证独立 Animator 播放，不启动游戏框架。
    /// </summary>
    public sealed class AnimatorPlaybackPlayModeTests
    {
        private const string PrefabPath = "Assets/GameResources/Gameplay/Expedition/Characters/PF_TestPlayer.prefab";
        private GameObject _instance;
        private Animator _animator;
        private Transform _mesh;
        private AnimatorPlayback _playback;

        /// <summary>
        /// 实例化真实角色资产并取得网格和控制器。
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(prefab, Is.Not.Null, "请先生成真实角色动画资产。");
            _instance = Object.Instantiate(prefab);
            _animator = _instance.GetComponent<Animator>();
            _mesh = _instance.transform.Find("Mesh");
            Assert.That(_animator, Is.Not.Null);
            Assert.That(_animator.runtimeAnimatorController, Is.Not.Null);
            Assert.That(_mesh, Is.Not.Null);
        }

        /// <summary>
        /// 清理测试播放器和角色实例。
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            _playback?.Dispose();
            if (_instance) Object.DestroyImmediate(_instance);
        }

        /// <summary>
        /// 验证不启动框架时也能在五个真实状态间采样，并驱动网格变形。
        /// </summary>
        /// <returns>PlayMode 单帧协程。</returns>
        [UnityTest]
        public IEnumerator RealPrefab_PlaysEveryStateAndSamplesMeshPose()
        {
            _playback = new AnimatorPlayback(_animator);
            Assert.That(_playback.IsValid, Is.True);
            foreach (string name in new[] { "Idle", "Move", "Attack", "Hit", "Death" })
            {
                Assert.That(_playback.TrySample("Base Layer." + name, 0f), Is.True, name);
                Assert.That(_playback.TryGetState(0, out AnimationPlaybackState state), Is.True);
                Assert.That(state.StateName, Is.EqualTo("Base Layer." + name));
                Assert.That(state.ExternalClock, Is.True);
            }
            Assert.That(_playback.TrySample("Base Layer.Idle", 0f), Is.True);
            Vector3 idlePose = _mesh.localPosition;
            Assert.That(_playback.TrySample("Base Layer.Attack", 0.625f), Is.True);
            Assert.That(_mesh.localPosition.y, Is.GreaterThan(idlePose.y + 0.03f));
            Assert.That(_mesh.localPosition.z, Is.GreaterThan(idlePose.z + 0.1f));
            yield return null;
            Assert.That(_mesh.localPosition.y, Is.GreaterThan(idlePose.y + 0.03f), "Unity 自动时间不应覆盖显式采样姿态。");
        }

        /// <summary>
        /// 验证片段长度换算、循环、倍率、暂停和非循环完成状态。
        /// </summary>
        /// <returns>PlayMode 单帧协程。</returns>
        [UnityTest]
        public IEnumerator LocalClock_UsesClipLengthAndHonorsLoopSpeedPauseCompletion()
        {
            _playback = new AnimatorPlayback(_animator);
            Assert.That(_playback.TryPlay("Base Layer.Move", true, 2f), Is.True);
            _playback.Advance(0.15f);
            Assert.That(_playback.TryGetState(0, out AnimationPlaybackState moving), Is.True);
            Assert.That(moving.NormalizedTime, Is.EqualTo(0.5f).Within(0.02f));
            Assert.That(moving.Completed, Is.False);
            _playback.Advance(0.3f);
            Assert.That(_playback.TryGetState(0, out moving), Is.True);
            Assert.That(moving.NormalizedTime, Is.EqualTo(0.5f).Within(0.02f));
            _playback.SetPaused(true);
            _playback.Advance(0.15f);
            Assert.That(_playback.TryGetState(0, out moving), Is.True);
            Assert.That(moving.NormalizedTime, Is.EqualTo(0.5f).Within(0.02f));
            _playback.SetPaused(false);
            Assert.That(_playback.TrySetSpeed(1f), Is.True);
            _playback.Advance(0.15f);
            Assert.That(_playback.TryGetState(0, out moving), Is.True);
            Assert.That(moving.NormalizedTime, Is.EqualTo(0.75f).Within(0.02f));
            Assert.That(_playback.TryPlay("Base Layer.Attack"), Is.True);
            _playback.Advance(2f);
            Assert.That(_playback.TryGetState(0, out AnimationPlaybackState attack), Is.True);
            Assert.That(attack.NormalizedTime, Is.EqualTo(1f));
            Assert.That(attack.Completed, Is.True);
            _playback.Advance(1f);
            Assert.That(_playback.TryGetState(0, out attack), Is.True);
            Assert.That(attack.NormalizedTime, Is.EqualTo(1f));
            yield return null;
        }

        /// <summary>
        /// 验证外部时钟采样不会被本地推进改变，暂停时仍可定位新姿态。
        /// </summary>
        /// <returns>PlayMode 单帧协程。</returns>
        [UnityTest]
        public IEnumerator ExternalClock_IgnoresAdvanceAndAllowsSeekingWhilePaused()
        {
            _playback = new AnimatorPlayback(_animator);
            Assert.That(_playback.TrySample("Base Layer.Attack", 0.625f), Is.True);
            Vector3 attackPose = _mesh.localPosition;
            _playback.Advance(10f);
            Assert.That(_playback.TryGetState(0, out AnimationPlaybackState state), Is.True);
            Assert.That(state.NormalizedTime, Is.EqualTo(0.625f));
            Assert.That(_mesh.localPosition, Is.EqualTo(attackPose));
            _playback.SetPaused(true);
            Assert.That(_playback.TrySample("Base Layer.Hit", 0.08f / 0.18f), Is.True);
            Assert.That(_mesh.localPosition.y, Is.LessThan(attackPose.y - 0.05f));
            Assert.That(_playback.IsPaused, Is.True);
            yield return null;
        }

        /// <summary>
        /// 验证非法状态、层号和时间不会改变已有轨道或姿态。
        /// </summary>
        /// <returns>PlayMode 单帧协程。</returns>
        [UnityTest]
        public IEnumerator InvalidInput_PreservesCurrentStateAndPose()
        {
            _playback = new AnimatorPlayback(_animator);
            Assert.That(_playback.TrySample("Base Layer.Attack", 0.625f), Is.True);
            Vector3 pose = _mesh.localPosition;
            Assert.That(_playback.TryPlay(null), Is.False);
            Assert.That(_playback.TryPlay("Base Layer.Missing"), Is.False);
            Assert.That(_playback.TryPlay("Base Layer.Idle", layer: 1), Is.False);
            Assert.That(_playback.TryPlay("Base Layer.Idle", speed: float.NaN), Is.False);
            Assert.That(_playback.TryPlay("Base Layer.Idle", speed: -1f), Is.False);
            Assert.That(_playback.TryPlay("Base Layer.Idle", startNormalizedTime: float.PositiveInfinity), Is.False);
            Assert.That(_playback.TrySample("Base Layer.Idle", -1f), Is.False);
            Assert.That(_playback.TrySample("Base Layer.Idle", float.NaN), Is.False);
            Assert.That(_playback.TrySetSpeed(float.NegativeInfinity), Is.False);
            _playback.Advance(float.NaN);
            _playback.Advance(-1f);
            Assert.That(_playback.TryGetState(0, out AnimationPlaybackState state), Is.True);
            Assert.That(state.StateName, Is.EqualTo("Base Layer.Attack"));
            Assert.That(state.NormalizedTime, Is.EqualTo(0.625f));
            Assert.That(_mesh.localPosition, Is.EqualTo(pose));
            yield return null;
        }

        /// <summary>
        /// 验证停止保留姿态、重置清空状态以及释放恢复 Animator 配置和所有权。
        /// </summary>
        /// <returns>PlayMode 单帧协程。</returns>
        [UnityTest]
        public IEnumerator Lifecycle_StopResetDisposePreserveOwnership()
        {
            _animator.speed = 1.4f;
            _animator.cullingMode = AnimatorCullingMode.CullCompletely;
            _playback = new AnimatorPlayback(_animator);
            Assert.That(_animator.speed, Is.Zero);
            Assert.That(_animator.cullingMode, Is.EqualTo(AnimatorCullingMode.AlwaysAnimate));
            Assert.That(_playback.TrySample("Base Layer.Attack", 0.625f), Is.True);
            Vector3 pose = _mesh.localPosition;
            _playback.Stop();
            Assert.That(_playback.TryGetState(0, out _), Is.False);
            Assert.That(_mesh.localPosition, Is.EqualTo(pose));
            Assert.That(_playback.TryPlay("Base Layer.Death"), Is.True);
            _playback.Reset();
            Assert.That(_playback.TryGetState(0, out _), Is.False);
            _playback.Dispose();
            Assert.That(_playback.IsValid, Is.False);
            Assert.That(_animator, Is.Not.Null);
            Assert.That(_animator.runtimeAnimatorController, Is.Not.Null);
            Assert.That(_animator.speed, Is.EqualTo(1.4f));
            Assert.That(_animator.cullingMode, Is.EqualTo(AnimatorCullingMode.CullCompletely));
            Assert.That(_playback.TryPlay("Base Layer.Idle"), Is.False);
            yield return null;
        }

        /// <summary>
        /// 验证 OverrideController 无需资源系统适配即可被同一接口播放。
        /// </summary>
        /// <returns>PlayMode 单帧协程。</returns>
        [UnityTest]
        public IEnumerator OverrideController_UsesTheSamePlaybackApi()
        {
            RuntimeAnimatorController controller = _animator.runtimeAnimatorController;
            var overrideController = new AnimatorOverrideController(controller);
            _animator.runtimeAnimatorController = overrideController;
            _playback = new AnimatorPlayback(_animator);
            Assert.That(_playback.TryPlay("Base Layer.Move", true), Is.True);
            _playback.Advance(0.15f);
            Assert.That(_playback.TryGetState(0, out AnimationPlaybackState state), Is.True);
            Assert.That(state.NormalizedTime, Is.EqualTo(0.25f).Within(0.02f));
            _playback.Dispose();
            _animator.runtimeAnimatorController = controller;
            Object.DestroyImmediate(overrideController);
            yield return null;
        }
    }
}
#endif

