using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;

namespace GameFramework.Tests.Modules.Gameplay
{
    /// <summary>GameplayRoot 状态机、局部对象查询与生命周期顺序测试。</summary>
    [TestFixture]
    public sealed class GameplayRootTests
    {
        /// <summary>Init / Enter 应按 Root、Feature、Model 的约定顺序执行并进入 Running。</summary>
        [Test]
        public void InitAndEnter_CallsPartsInOrder()
        {
            var gameplay = new TestGameplay();

            gameplay.EnterGameplayAsync().GetAwaiter().GetResult();

            CollectionAssert.AreEqual(
                new[]
                {
                    "root:init",
                    "feature:init",
                    "model:init",
                    "feature:enter",
                    "model:enter",
                    "root:enter",
                },
                gameplay.Events);
            Assert.IsTrue(gameplay.IsInited);
            Assert.IsTrue(gameplay.IsActive);
            Assert.AreEqual(GameplayState.Running, gameplay.State);
        }

        /// <summary>Update 应先驱动 Model，再驱动 Feature，最后驱动 Root。</summary>
        [Test]
        public void Update_WhenActive_CallsModelFeatureRoot()
        {
            var gameplay = new TestGameplay();
            gameplay.EnterGameplayAsync().GetAwaiter().GetResult();
            gameplay.Events.Clear();

            gameplay.Update(0.1f);

            CollectionAssert.AreEqual(
                new[] { "model:update", "feature:update", "root:update" },
                gameplay.Events);
        }

        /// <summary>Exit / Destroy 应按反向依赖顺序清理局部对象。</summary>
        [Test]
        public void ExitAndDestroy_CleansUpInReverseOrder()
        {
            var gameplay = new TestGameplay();
            gameplay.EnterGameplayAsync().GetAwaiter().GetResult();
            gameplay.Events.Clear();

            gameplay.ExitGameplayAsync().GetAwaiter().GetResult();
            gameplay.DestroyGameplay();

            CollectionAssert.AreEqual(
                new[]
                {
                    "root:exit",
                    "model:exit",
                    "feature:exit",
                    "model:destroy",
                    "feature:destroy",
                    "root:destroy",
                },
                gameplay.Events);
            Assert.IsTrue(gameplay.IsDestroyed);
            Assert.AreEqual(GameplayState.Destroyed, gameplay.State);
        }

        /// <summary>同类型 Feature / Model 重复添加应抛异常。</summary>
        [Test]
        public void AddDuplicatePart_Throws()
        {
            var gameplay = new EmptyGameplay();
            gameplay.AddFeature<EmptyFeature>();
            gameplay.AddModel<EmptyModel>();

            Assert.Throws<InvalidOperationException>(() => gameplay.AddFeature<EmptyFeature>());
            Assert.Throws<InvalidOperationException>(() => gameplay.AddModel<EmptyModel>());
        }

        /// <summary>进入运行期后不允许继续添加 Feature / Model。</summary>
        [Test]
        public void AddPart_AfterInit_Throws()
        {
            var gameplay = new EmptyGameplay();
            gameplay.InitGameplayAsync().GetAwaiter().GetResult();

            Assert.Throws<InvalidOperationException>(() => gameplay.AddFeature<EmptyFeature>());
            Assert.Throws<InvalidOperationException>(() => gameplay.AddModel<EmptyModel>());
        }

        /// <summary>GetFeature / GetModel 应返回已添加的局部对象实例。</summary>
        [Test]
        public void GetPart_ReturnsRegisteredInstance()
        {
            var gameplay = new EmptyGameplay();
            var feature = gameplay.AddFeature<EmptyFeature>();
            var model = gameplay.AddModel<EmptyModel>();

            Assert.AreSame(feature, gameplay.GetFeature<EmptyFeature>());
            Assert.AreSame(model, gameplay.GetModel<EmptyModel>());
        }

        /// <summary>强类型 Feature 应能直接访问具体玩法 Root 成员。</summary>
        [Test]
        public void TypedFeature_CanAccessConcreteRoot()
        {
            var gameplay = new TestGameplay();
            var feature = gameplay.GetFeature<TestFeature>();

            Assert.AreEqual(7, feature.ReadRootValue());
        }

        /// <summary>泛型父类用法示例：Feature 内 Root 应直接是具体玩法类型。</summary>
        [Test]
        public void GenericParentExample_FeatureUsesConcreteRootWithoutCast()
        {
            var gameplay = new ExampleGameplay();

            Assert.AreEqual(99, gameplay.Feature.ReadScore());
            Assert.IsTrue(gameplay.Feature.IsSameRoot(gameplay));
        }

        /// <summary>ForceExit 应取消运行态并同步清理局部对象。</summary>
        [Test]
        public void ForceExit_ResetsActiveStateAndCallsParts()
        {
            var gameplay = new TestGameplay();
            gameplay.EnterGameplayAsync().GetAwaiter().GetResult();
            gameplay.Events.Clear();

            gameplay.ForceExit();

            CollectionAssert.AreEqual(
                new[] { "root:forceExit", "model:forceExit", "feature:forceExit" },
                gameplay.Events);
            Assert.IsFalse(gameplay.IsActive);
            Assert.IsFalse(gameplay.IsEntering);
            Assert.IsFalse(gameplay.IsExiting);
            Assert.AreEqual(GameplayState.Ready, gameplay.State);
        }

        private sealed class EmptyGameplay : GameplayRoot<EmptyGameplay>
        {
            public EmptyGameplay() : base("empty")
            {
            }
        }

        private sealed class TestGameplay : GameplayRoot<TestGameplay>
        {
            public readonly List<string> Events = new(16);

            public int RootValue => 7;

            public TestGameplay() : base("test")
            {
                AddFeature(new TestFeature(Events));
                AddModel(new TestModel(Events));
            }

            protected override UniTask OnInitGameplayAsync(CancellationToken ct)
            {
                Events.Add("root:init");
                return UniTask.CompletedTask;
            }

            protected override UniTask OnEnterGameplayAsync(GameplayEnterData enterData, CancellationToken ct)
            {
                Events.Add("root:enter");
                return UniTask.CompletedTask;
            }

            protected override UniTask OnExitGameplayAsync(CancellationToken ct)
            {
                Events.Add("root:exit");
                return UniTask.CompletedTask;
            }

            protected override void OnForceExit()
            {
                Events.Add("root:forceExit");
            }

            protected override void OnDestroyGameplay()
            {
                Events.Add("root:destroy");
            }

            protected override void OnUpdateGameplay(float deltaTime)
            {
                Events.Add("root:update");
            }
        }

        private sealed class EmptyFeature : GameplayFeature<EmptyGameplay>
        {
        }

        private sealed class EmptyModel : GameplayModel<EmptyGameplay>
        {
        }

        private sealed class TestFeature : GameplayFeature<TestGameplay>
        {
            private readonly List<string> _events;

            public TestFeature()
            {
            }

            public TestFeature(List<string> events)
            {
                _events = events;
            }

            public override UniTask InitAsync(CancellationToken ct = default)
            {
                _events?.Add("feature:init");
                return UniTask.CompletedTask;
            }

            public override UniTask EnterAsync(GameplayEnterData enterData = null, CancellationToken ct = default)
            {
                _events?.Add("feature:enter");
                return UniTask.CompletedTask;
            }

            public override UniTask ExitAsync(CancellationToken ct = default)
            {
                _events?.Add("feature:exit");
                return UniTask.CompletedTask;
            }

            public override void Update(float deltaTime)
            {
                _events?.Add("feature:update");
            }

            public override void ForceExit()
            {
                _events?.Add("feature:forceExit");
            }

            public override void Destroy()
            {
                _events?.Add("feature:destroy");
            }

            public int ReadRootValue()
            {
                return Root.RootValue;
            }
        }

        private sealed class TestModel : GameplayModel<TestGameplay>
        {
            private readonly List<string> _events;

            public TestModel()
            {
            }

            public TestModel(List<string> events)
            {
                _events = events;
            }

            public override UniTask InitAsync(CancellationToken ct = default)
            {
                _events?.Add("model:init");
                return UniTask.CompletedTask;
            }

            public override UniTask EnterAsync(GameplayEnterData enterData = null, CancellationToken ct = default)
            {
                _events?.Add("model:enter");
                return UniTask.CompletedTask;
            }

            public override UniTask ExitAsync(CancellationToken ct = default)
            {
                _events?.Add("model:exit");
                return UniTask.CompletedTask;
            }

            public override void Update(float deltaTime)
            {
                _events?.Add("model:update");
            }

            public override void ForceExit()
            {
                _events?.Add("model:forceExit");
            }

            public override void Destroy()
            {
                _events?.Add("model:destroy");
            }
        }

        private sealed class ExampleGameplay : GameplayRoot<ExampleGameplay>
        {
            public ExampleGameplay() : base("example")
            {
                AddFeature<ExampleFeature>();
            }

            public int Score => 99;

            public ExampleFeature Feature => GetFeature<ExampleFeature>();
        }

        private sealed class ExampleFeature : GameplayFeature<ExampleGameplay>
        {
            public int ReadScore()
            {
                return Root.Score;
            }

            public bool IsSameRoot(ExampleGameplay gameplay)
            {
                return ReferenceEquals(Root, gameplay);
            }
        }
    }
}
