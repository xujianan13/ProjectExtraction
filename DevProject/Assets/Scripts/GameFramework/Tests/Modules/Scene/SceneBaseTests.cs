using GameFramework.Tests.Mocks;
using NUnit.Framework;

namespace GameFramework.Tests.Modules.Scene
{
    /// <summary>SceneBase 状态机、幂等性与 TypedSceneBase 类型校验测试。</summary>
    [TestFixture]
    public sealed class SceneBaseTests
    {
        /// <summary>InitSceneAsync 重复调用应幂等。</summary>
        [Test]
        public void InitSceneAsync_IsIdempotent()
        {
            var scene = new TestScene("test");
            scene.InitSceneAsync().GetAwaiter().GetResult();
            scene.InitSceneAsync().GetAwaiter().GetResult();
            Assert.IsTrue(scene.IsInited);
        }

        /// <summary>Enter 后 IsActive/State 应为 Running，Exit 后恢复 Idle。</summary>
        [Test]
        public void EnterAndExit_TransitionsState()
        {
            var scene = new TestScene("test");
            scene.EnterSceneAsync().GetAwaiter().GetResult();
            Assert.IsTrue(scene.IsActive);
            Assert.AreEqual(SceneState.Running, scene.State);
            scene.ExitSceneAsync().GetAwaiter().GetResult();
            Assert.IsFalse(scene.IsActive);
            Assert.AreEqual(SceneState.Idle, scene.State);
        }

        /// <summary>已 Active 时重复 EnterSceneAsync 应被忽略。</summary>
        [Test]
        public void EnterSceneAsync_DuplicateEnter_IsIgnored()
        {
            var scene = new TestScene("test");
            scene.EnterSceneAsync().GetAwaiter().GetResult();
            scene.EnterSceneAsync().GetAwaiter().GetResult();
            Assert.AreEqual(1, scene.EnterCount);
        }

        /// <summary>未 Active 时 ExitSceneAsync 应被忽略。</summary>
        [Test]
        public void ExitSceneAsync_WhenNotActive_IsIgnored()
        {
            var scene = new TestScene("test");
            scene.ExitSceneAsync().GetAwaiter().GetResult();
            Assert.AreEqual(0, scene.ExitCount);
        }

        /// <summary>ForceExit 应取消 CTS 并重置 Entering/Exiting/Active 状态。</summary>
        [Test]
        public void ForceExit_CancelsAndResetsState()
        {
            var scene = new TestScene("test");
            scene.EnterSceneAsync().GetAwaiter().GetResult();
            scene.ForceExit();
            Assert.IsFalse(scene.IsActive);
            Assert.IsFalse(scene.IsEntering);
            Assert.IsFalse(scene.IsExiting);
        }

        /// <summary>TypedSceneBase 传入错误 enterData 类型应抛 ArgumentException。</summary>
        [Test]
        public void TypedSceneBase_WrongEnterDataType_Throws()
        {
            var scene = new TypedTestScene("typed");
            Assert.Throws<System.ArgumentException>(() =>
                scene.EnterSceneAsync(new WrongEnterData()).GetAwaiter().GetResult());
        }

        /// <summary>TypedSceneBase 传入正确 enterData 类型应成功进入。</summary>
        [Test]
        public void TypedSceneBase_CorrectEnterDataType_Succeeds()
        {
            var scene = new TypedTestScene("typed");
            scene.EnterSceneAsync(new TestEnterData()).GetAwaiter().GetResult();
            Assert.IsTrue(scene.IsActive);
        }
    }
}
