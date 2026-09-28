using System;
using NUnit.Framework;
using UnityEngine;

namespace GameFramework.Tests.Modules.Update
{
    /// <summary>
    /// 验证内容包版本事务在重启和失败后仍保留上次可用版本。
    /// </summary>
    [TestFixture]
    public sealed class PlayerPrefsVersionStateStoreTests
    {
        private string _scope;
        private const string PackageName = "GamePackage";

        /// <summary>
        /// 为每个测试创建隔离的持久化范围。
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            _scope = "VersionStateTests." + Guid.NewGuid().ToString("N");
        }

        /// <summary>
        /// 删除测试创建的持久化状态。
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            PlayerPrefs.DeleteKey("GameFramework.UpdateVersionState." + _scope + "." + PackageName);
        }

        /// <summary>
        /// 候选失败后重开仓库仍保留旧版并拒绝同一失败版本。
        /// </summary>
        [Test]
        public void Rollback_RejectedPending_PersistsLastGoodAcrossStoreInstances()
        {
            var store = new PlayerPrefsVersionStateStore(_scope);
            store.InitializeCurrent(PackageName, "1.0.0");
            store.StagePending(PackageName, "1.1.0");
            store.IncrementPendingAttempt(PackageName);
            store.Rollback(PackageName, UpdateErrorCode.ActivationFailed, true);

            PackageVersionState state = new PlayerPrefsVersionStateStore(_scope).Load(PackageName);
            Assert.AreEqual("1.0.0", state.CurrentVersion);
            Assert.AreEqual("1.0.0", state.LastGoodVersion);
            Assert.IsFalse(state.HasPending);
            Assert.AreEqual("1.1.0", state.RejectedVersion);
            Assert.Throws<InvalidOperationException>(() => store.StagePending(PackageName, "1.1.0"));
            Assert.AreEqual(UpdateErrorCode.ActivationFailed, state.LastFailureCode);
        }

        /// <summary>
        /// 临时失败清除候选状态但不阻止重试同版本。
        /// </summary>
        [Test]
        public void Rollback_TransientFailure_DoesNotRejectCandidate()
        {
            var store = new PlayerPrefsVersionStateStore(_scope);
            store.InitializeCurrent(PackageName, "1.0.0");
            store.StagePending(PackageName, "1.1.0");
            store.Rollback(PackageName, UpdateErrorCode.DownloadFailed);

            PackageVersionState state = store.Load(PackageName);
            Assert.AreEqual(string.Empty, state.RejectedVersion);
            store.StagePending(PackageName, "1.1.0");
            Assert.AreEqual("1.1.0", store.Load(PackageName).PendingVersion);
        }

        /// <summary>
        /// 游戏启动确认后新版本成为上次可用版本。
        /// </summary>
        [Test]
        public void CommitReady_AdvancesLastGoodOnlyAfterReady()
        {
            var store = new PlayerPrefsVersionStateStore(_scope);
            store.InitializeCurrent(PackageName, "1.0.0");
            store.StagePending(PackageName, "1.1.0");
            Assert.AreEqual("1.0.0", store.Load(PackageName).LastGoodVersion);

            store.CommitReady(PackageName, "1.1.0");
            PackageVersionState state = store.Load(PackageName);
            Assert.AreEqual("1.1.0", state.CurrentVersion);
            Assert.AreEqual("1.1.0", state.LastGoodVersion);
            Assert.IsFalse(state.HasPending);
        }
        /// <summary>
        /// 首次安装没有旧版时也能在启动成功后建立可用版本。
        /// </summary>
        [Test]
        public void InitialInstall_CommitsFirstReadyVersion()
        {
            var store = new PlayerPrefsVersionStateStore(_scope);
            store.StagePending(PackageName, "1.0.0");
            Assert.IsEmpty(store.Load(PackageName).LastGoodVersion);

            store.CommitReady(PackageName, "1.0.0");
            Assert.AreEqual("1.0.0", store.Load(PackageName).LastGoodVersion);
        }
    }
}
