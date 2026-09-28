using System;
using System.Threading;
using NUnit.Framework;
using UnityEngine;

namespace GameFramework.Tests.Modules.Tweening
{
    /// <summary>
    /// 验证补间组合、重入和生命周期边界。
    /// </summary>
    [TestFixture]
    public sealed class TweenModuleTests
    {
        private TweenModule _module;

        /// <summary>
        /// 创建可控时间源的模块。
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            _module = new TweenModule();
            _module.OnInit();
        }

        /// <summary>
        /// 清理活动句柄。
        /// </summary>
        [TearDown]
        public void TearDown() => _module.OnShutdown();

        /// <summary>
        /// 一次大时间步必须跨过前段并正确推进后段，且只有一个活动句柄。
        /// </summary>
        [Test]
        public void Sequence_LargeDeltaAdvancesLaterClipOnce()
        {
            float first = 0f, second = 0f;
            TweenClip clip = Tween.Sequence(Tween.To(0f, 1f, 1f, value => first = value), Tween.To(0f, 1f, 1f, value => second = value));
            TweenHandle handle = _module.Play(clip);
            Assert.AreEqual(1, _module.ActiveCount);
            _module.Advance(1.5f, 0f);
            Assert.AreEqual(1f, first);
            Assert.AreEqual(0.5f, second);
            _module.Advance(0.5f, 0f);
            Assert.AreEqual(TweenState.Completed, handle.WaitAsync().GetAwaiter().GetResult());
            Assert.AreEqual(0, _module.ActiveCount);
        }

        /// <summary>
        /// 往返倒退时，独立目标后段恢复起点，而前段回到对应时间。
        /// </summary>
        [Test]
        public void YoyoSequence_RewindsSeparateTargets()
        {
            float first = 0f, second = 0f;
            TweenClip sequence = Tween.Sequence(Tween.To(0f, 1f, 1f, value => first = value), Tween.To(0f, 1f, 1f, value => second = value));
            _module.Play(Tween.Loop(sequence, 2, true));
            _module.Advance(2.5f, 0f);
            Assert.AreEqual(1f, first);
            Assert.AreEqual(0.5f, second);
            _module.Advance(1f, 0f);
            Assert.AreEqual(0.5f, first);
            Assert.AreEqual(0f, second);
        }

        /// <summary>
        /// 倒退穿过多段时须逆序复位，同一属性最终回到最早片段的起点。
        /// </summary>
        [Test]
        public void YoyoSequence_RewindsMultipleSharedPropertyClips()
        {
            float value = -1f;
            TweenClip sequence = Tween.Sequence(Tween.Delay(1f), Tween.To(0f, 1f, 1f, next => value = next),
                Tween.To(1f, 2f, 1f, next => value = next));
            _module.Play(Tween.Loop(sequence, 2, true));
            _module.Advance(3.5f, 0f);
            Assert.AreEqual(1.5f, value);
            _module.Advance(2f, 0f);
            Assert.AreEqual(0f, value);
        }

        /// <summary>
        /// 跨完整循环时，尚未逐帧求值的后段独立目标也回到显式起点。
        /// </summary>
        [Test]
        public void RestartSequence_SkippedCyclesResetLaterTarget()
        {
            float first = -1f, later = 99f;
            TweenClip sequence = Tween.Sequence(Tween.To(0f, 1f, 1f, value => first = value),
                Tween.To(0f, 1f, 1f, value => later = value));
            _module.Play(Tween.Loop(sequence, 4));
            _module.Advance(0.5f, 0f);
            _module.Advance(4f, 0f);
            Assert.AreEqual(0.5f, first);
            Assert.AreEqual(0f, later);
        }

        /// <summary>
        /// 未开始的同一属性后段不能覆盖前段求值。
        /// </summary>
        [Test]
        public void YoyoSequence_SharedPropertyKeepsActiveClipValue()
        {
            float value = 0f;
            TweenClip sequence = Tween.Sequence(Tween.To(0f, 1f, 1f, next => value = next), Tween.To(1f, 2f, 1f, next => value = next));
            _module.Play(Tween.Loop(sequence, 2, true));
            _module.Advance(3.5f, 0f);
            Assert.AreEqual(0.5f, value);
        }

        /// <summary>
        /// 无限循环持续活动，取消后不再写入。
        /// </summary>
        [Test]
        public void InfiniteLoop_CancelsCleanly()
        {
            int writes = 0;
            TweenHandle handle = _module.Play(Tween.Loop(Tween.To(0f, 1f, 1f, _ => writes++)));
            _module.Advance(12.5f, 0f);
            Assert.AreEqual(1, writes);
            Assert.AreEqual(1, _module.ActiveCount);
            handle.Cancel();
            _module.Advance(1f, 0f);
            Assert.AreEqual(1, writes);
            Assert.AreEqual(0, _module.ActiveCount);
        }

        /// <summary>
        /// 暂停句柄不推进，非缩放播放仅消费非缩放时间。
        /// </summary>
        [Test]
        public void PauseAndUnscaledTime_UseExpectedClock()
        {
            float value = 0f;
            TweenHandle handle = _module.Play(Tween.To(0f, 1f, 1f, next => value = next), unscaledTime: true);
            handle.Pause();
            _module.Advance(1f, 0.5f);
            Assert.AreEqual(0f, value);
            handle.Resume();
            _module.Advance(0f, 0.5f);
            Assert.AreEqual(0.5f, value);
        }

        /// <summary>
        /// 复用片段时，每次播放各自保存进度。
        /// </summary>
        [Test]
        public void ReusedClip_KeepsPerPlayElapsed()
        {
            var values = new System.Collections.Generic.List<float>();
            TweenClip clip = Tween.To(0f, 1f, 1f, values.Add);
            _module.Play(clip);
            _module.Advance(0.5f, 0f);
            _module.Play(clip);
            _module.Advance(0.25f, 0f);
            CollectionAssert.AreEqual(new[] { 0.5f, 0.75f, 0.25f }, values);
        }

        /// <summary>
        /// 零时长片段在下一次推进完成，延时后的片段尚未写入。
        /// </summary>
        [Test]
        public void ZeroDurationAndDelay_DoNotWriteFutureClip()
        {
            float first = 0f, future = -1f;
            TweenClip clip = Tween.Sequence(Tween.To(0f, 1f, 0f, value => first = value), Tween.Delay(1f), Tween.To(0f, 1f, 1f, value => future = value));
            _module.Play(clip);
            _module.Advance(0f, 0f);
            Assert.AreEqual(1f, first);
            Assert.AreEqual(-1f, future);
        }

        /// <summary>
        /// 写入回调取消播放后，并行后段不得写入。
        /// </summary>
        [Test]
        public void SetterCancel_StopsLaterWritesInSameFrame()
        {
            int laterWrites = 0;
            TweenHandle handle = null;
            TweenClip clip = Tween.Parallel(Tween.To(0f, 1f, 1f, _ => handle.Cancel()), Tween.To(0f, 1f, 1f, _ => laterWrites++));
            handle = _module.Play(clip);
            _module.Advance(0.5f, 0f);
            Assert.AreEqual(0, laterWrites);
            Assert.AreEqual(TweenState.Canceled, handle.State);
        }

        /// <summary>
        /// 写入回调停止作用域后，并行后段不得写入。
        /// </summary>
        [Test]
        public void SetterScopeStop_StopsLaterWritesInSameFrame()
        {
            using var scope = new LifetimeScope("TweenSetterStop");
            int laterWrites = 0;
            TweenClip clip = Tween.Parallel(Tween.To(0f, 1f, 1f, _ => scope.Stop()), Tween.To(0f, 1f, 1f, _ => laterWrites++));
            TweenHandle handle = _module.Play(clip, scope);
            _module.Advance(0.5f, 0f);
            Assert.AreEqual(0, laterWrites);
            Assert.AreEqual(TweenState.Canceled, handle.State);
        }

        /// <summary>
        /// 作用域 Stop 立即取消；自然完成会移除作用域登记。
        /// </summary>
        [Test]
        public void ScopeStopAndCompletion_ReleaseOwnership()
        {
            using var scope = new LifetimeScope("TweenTest");
            TweenHandle stopped = _module.Play(Tween.Delay(10f), scope);
            Assert.AreEqual(1, scope.RegistrationCount);
            scope.Stop();
            Assert.AreEqual(TweenState.Canceled, stopped.State);
            Assert.AreEqual(0, scope.RegistrationCount);

            using var second = new LifetimeScope("TweenCompletion");
            TweenHandle completed = _module.Play(Tween.Delay(0f), second);
            _module.Advance(0f, 0f);
            Assert.AreEqual(TweenState.Completed, completed.State);
            Assert.AreEqual(0, second.RegistrationCount);
        }

        /// <summary>
        /// 令牌取消后即使推进时间也不写入。
        /// </summary>
        [Test]
        public void CancellationToken_StopsBeforeWrite()
        {
            using var source = new CancellationTokenSource();
            int writes = 0;
            TweenHandle handle = _module.Play(Tween.To(0f, 1f, 1f, _ => writes++), cancellationToken: source.Token);
            source.Cancel();
            _module.Advance(1f, 0f);
            Assert.AreEqual(TweenState.Canceled, handle.State);
            Assert.AreEqual(0, writes);
        }

        /// <summary>
        /// 销毁绑定目标后不调用 setter，并报告目标销毁状态。
        /// </summary>
        [Test]
        public void DestroyedTarget_StopsWithoutWriting()
        {
            GameObject target = new GameObject("TweenTarget");
            int writes = 0;
            TweenHandle handle = _module.Play(Tween.To(0f, 1f, 1f, _ => writes++, target: target));
            UnityEngine.Object.DestroyImmediate(target);
            _module.Advance(1f, 0f);
            Assert.AreEqual(TweenState.TargetDestroyed, handle.State);
            Assert.AreEqual(0, writes);
        }

        /// <summary>
        /// 首个写入销毁后续片段目标时，本帧立即停止并保留目标销毁原因。
        /// </summary>
        [Test]
        public void SetterDestroysLaterTarget_StopsLaterWrite()
        {
            GameObject target = new GameObject("TweenLaterTarget");
            int laterWrites = 0;
            TweenClip clip = Tween.Parallel(Tween.To(0f, 1f, 1f, _ => UnityEngine.Object.DestroyImmediate(target)),
                Tween.To(0f, 1f, 1f, _ => laterWrites++, target: target));
            TweenHandle handle = _module.Play(clip);
            _module.Advance(0.5f, 0f);
            Assert.AreEqual(0, laterWrites);
            Assert.AreEqual(TweenState.TargetDestroyed, handle.State);
        }

        /// <summary>
        /// setter 抛异常时结束播放并报告故障终态。
        /// </summary>
        [Test]
        public void SetterThrows_ReportsFaulted()
        {
            TweenHandle handle = _module.Play(Tween.To(0f, 1f, 1f, _ => throw new InvalidOperationException("expected")));
            Assert.DoesNotThrow(() => _module.Advance(0.5f, 0f));
            Assert.AreEqual(TweenState.Faulted, handle.State);
            Assert.AreEqual(0, _module.ActiveCount);
        }

        /// <summary>
        /// 完成回调中的创建与取消不改变当前帧的推进快照。
        /// </summary>
        [Test]
        public void CompletionCallback_CanCreateAndCancel()
        {
            int writes = 0;
            TweenHandle next = null;
            TweenHandle first = _module.Play(Tween.Delay(0f));
            first.Completed += _ =>
            {
                next = _module.Play(Tween.To(0f, 1f, 1f, value => writes++));
                next.Cancel();
            };
            Assert.DoesNotThrow(() => _module.Advance(0f, 0f));
            Assert.AreEqual(TweenState.Canceled, next.State);
            Assert.AreEqual(0, writes);
            Assert.AreEqual(0, _module.ActiveCount);
        }

        /// <summary>
        /// CancelAll 的停止回调可重入取消，不会因列表压缩越界。
        /// </summary>
        [Test]
        public void CancelAll_ReentrantCallbackRemainsStable()
        {
            TweenHandle first = _module.Play(Tween.Delay(10f));
            TweenHandle second = _module.Play(Tween.Delay(10f));
            first.Stopped += _ => _module.CancelAll();
            Assert.DoesNotThrow(() => _module.CancelAll());
            Assert.AreEqual(TweenState.Canceled, second.State);
            Assert.AreEqual(0, _module.ActiveCount);
        }

        /// <summary>
        /// 关闭模块的回调不会继续访问已清空的句柄列表。
        /// </summary>
        [Test]
        public void CompletionCallback_ShutdownStopsRemaining()
        {
            TweenHandle first = _module.Play(Tween.Delay(0f));
            TweenHandle second = _module.Play(Tween.Delay(10f));
            first.Completed += _ => _module.OnShutdown();
            Assert.DoesNotThrow(() => _module.Advance(0f, 0f));
            Assert.AreEqual(TweenState.Canceled, second.State);
        }
    }
}
