using System;
using NUnit.Framework;

namespace GameFramework.Tests.Modules.Model
{
    /// <summary>ModelModule 注册、有序生命周期与作用域重置测试。</summary>
    [TestFixture]
    public sealed class ModelModuleTests
    {
        private ModelModule _module;

        [SetUp]
        public void SetUp()
        {
            ModelOrderTracker.Reset();
            FrameworkTestHelper.SetupWithLog();
            _module = new ModelModule();
            _module.OnInit();
        }

        [TearDown]
        public void TearDown()
        {
            _module.OnShutdown();
            ModelOrderTracker.Reset();
            FrameworkTestHelper.ResetFramework();
        }

        /// <summary>注册后应按具体类型返回同一模型实例。</summary>
        [Test]
        public void RegisterModel_AndGet_ReturnsSameInstance()
        {
            var model = new TestModel();

            _module.RegisterModel(model);

            Assert.AreSame(model, _module.Get<TestModel>());
        }

        /// <summary>初始化后不允许继续注册模型。</summary>
        [Test]
        public void RegisterModel_AfterInitAll_Throws()
        {
            _module.InitAll();

            Assert.Throws<InvalidOperationException>(
                () => _module.RegisterModel(new TestModel()));
        }

        /// <summary>InitAll 幂等且 OnUpdate 仅在初始化后驱动。</summary>
        [Test]
        public void InitAll_AndUpdate_DriveModelOncePerCall()
        {
            var model = new TestModel();
            _module.RegisterModel(model);
            _module.OnUpdate(0.1f);

            _module.InitAll();
            _module.InitAll();
            _module.OnUpdate(0.1f);

            Assert.AreEqual(1, model.InitCount);
            Assert.AreEqual(1, model.UpdateCount);
            Assert.IsTrue(_module.IsInitialized);
        }

        /// <summary>账号重置应清理账号和玩家模型，但保留应用模型。</summary>
        [Test]
        public void ResetAll_AccountScope_ResetsAccountAndPlayerOnly()
        {
            var application = new TestModel(scope: ModelScope.Application);
            var account = new AccountTestModel();
            var player = new PlayerTestModel();
            _module.RegisterModel(application);
            _module.RegisterModel(account);
            _module.RegisterModel(player);
            _module.InitAll();

            _module.ResetAll(ModelScope.Account, ModelResetReason.AccountChanged);

            Assert.AreEqual(0, application.ResetCount);
            Assert.AreEqual(1, account.ResetCount);
            Assert.AreEqual(1, player.ResetCount);
            Assert.AreEqual(ModelResetReason.AccountChanged, player.LastResetReason);
        }

        /// <summary>玩家重置只应清理玩家作用域模型。</summary>
        [Test]
        public void ResetAll_PlayerScope_ResetsPlayerOnly()
        {
            var account = new AccountTestModel();
            var player = new PlayerTestModel();
            _module.RegisterModel(account);
            _module.RegisterModel(player);
            _module.InitAll();

            _module.ResetAll(ModelScope.Player, ModelResetReason.PlayerChanged);

            Assert.AreEqual(0, account.ResetCount);
            Assert.AreEqual(1, player.ResetCount);
        }

        /// <summary>重置应按 Order 降序执行，先清理依赖方。</summary>
        [Test]
        public void ResetAll_CallsModelsInReverseOrder()
        {
            var first = new AccountTestModel(order: 0);
            var second = new PlayerTestModel(order: 10);
            _module.RegisterModel(first);
            _module.RegisterModel(second);
            _module.InitAll();

            _module.ResetAll(ModelScope.Account, ModelResetReason.Logout);

            Assert.Less(second.ResetOrder, first.ResetOrder);
        }

        /// <summary>单个模型重置失败不应阻断其他模型清理。</summary>
        [Test]
        public void ResetAll_ModelThrows_DoesNotBlockOthers()
        {
            var ok = new AccountTestModel(order: 0);
            var failing = new PlayerTestModel(order: 10) { ThrowOnReset = true };
            _module.RegisterModel(ok);
            _module.RegisterModel(failing);
            _module.InitAll();

            _module.ResetAll(ModelScope.Account, ModelResetReason.Logout);

            Assert.AreEqual(1, ok.ResetCount);
            Assert.AreEqual(1, failing.ResetCount);
        }

        /// <summary>ShutdownAll 应逆序卸载并清空模型注册表。</summary>
        [Test]
        public void ShutdownAll_ShutsDownInReverseOrderAndClearsRegistry()
        {
            var first = new AccountTestModel(order: 0);
            var second = new PlayerTestModel(order: 10);
            _module.RegisterModel(first);
            _module.RegisterModel(second);
            _module.InitAll();

            _module.ShutdownAll();

            Assert.Less(second.ShutdownOrder, first.ShutdownOrder);
            Assert.IsNull(_module.Get<AccountTestModel>());
            Assert.IsFalse(_module.IsInitialized);
        }

        /// <summary>空模型不可注册。</summary>
        [Test]
        public void RegisterModel_Null_Throws()
        {
            Assert.Throws<ArgumentNullException>(
                () => _module.RegisterModel<TestModel>(null));
        }

        /// <summary>
        /// 初始化失败的模型只清理一次，后续更新和重置仅作用于成功模型。
        /// </summary>
        [Test]
        public void InitFailure_CleansUpAndSkipsLaterLifecycle()
        {
            var failing = new AccountTestModel { ThrowOnInit = true };
            var healthy = new PlayerTestModel();
            _module.RegisterModel(failing);
            _module.RegisterModel(healthy);
            _module.InitAll();
            _module.OnUpdate(0.1f);
            Assert.IsNull(_module.Get<AccountTestModel>());
            _module.ResetAll(ModelScope.Application, ModelResetReason.Logout);
            _module.ShutdownAll();
            Assert.AreEqual(1, failing.ShutdownCount);
            Assert.AreEqual(0, failing.UpdateCount);
            Assert.AreEqual(0, failing.ResetCount);
            Assert.AreEqual(1, healthy.UpdateCount);
            Assert.AreEqual(1, healthy.ResetCount);
            Assert.AreEqual(1, healthy.ShutdownCount);
        }

        private class TestModel : ModelBase
        {
            private readonly int _order;
            private readonly ModelScope _scope;

            public TestModel(
                int order = 0,
                ModelScope scope = ModelScope.Account)
            {
                _order = order;
                _scope = scope;
            }

            public override int Order => _order;
            public override ModelScope Scope => _scope;
            public int InitCount { get; private set; }
            public int UpdateCount { get; private set; }
            public int ResetCount { get; private set; }
            public int ResetOrder { get; private set; }
            public int ShutdownOrder { get; private set; }
            public ModelResetReason LastResetReason { get; private set; }
            public bool ThrowOnReset { get; set; }
            public bool ThrowOnInit { get; set; }
            public int ShutdownCount { get; private set; }

            public override void OnInit()
            {
                InitCount++;
                if (ThrowOnInit) throw new InvalidOperationException("Init failed");
            }

            public override void OnUpdate(float deltaTime)
            {
                UpdateCount++;
            }

            public override void OnReset(ModelResetReason reason)
            {
                ResetCount++;
                ResetOrder = ModelOrderTracker.Next();
                LastResetReason = reason;
                if (ThrowOnReset)
                    throw new InvalidOperationException("Reset failed");
            }

            public override void OnShutdown()
            {
                ShutdownCount++;
                ShutdownOrder = ModelOrderTracker.Next();
            }
        }

        private sealed class AccountTestModel : TestModel
        {
            public AccountTestModel(int order = 0)
                : base(order, ModelScope.Account)
            {
            }
        }

        private sealed class PlayerTestModel : TestModel
        {
            public PlayerTestModel(int order = 0)
                : base(order, ModelScope.Player)
            {
            }
        }

        private static class ModelOrderTracker
        {
            private static int _sequence;

            public static int Next() => ++_sequence;

            public static void Reset() => _sequence = 0;
        }
    }
}
