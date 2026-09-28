using NUnit.Framework;

namespace GameFramework.Tests.Modules.Resource
{
    /// <summary>ResourceModule 初始化状态与参数校验测试（不涉及真实 YooAsset 包）。</summary>
    [TestFixture]
    public sealed class ResourceModuleTests
    {
        private ResourceModule _module;

        [SetUp]
        public void SetUp()
        {
            FrameworkTestHelper.SetupWithLog();
            _module = new ResourceModule();
            _module.OnInit();
        }

        [TearDown]
        public void TearDown()
        {
            _module.OnShutdown();
            FrameworkTestHelper.ResetFramework();
        }

        /// <summary>未初始化 Package 前 IsInitialized 应为 false。</summary>
        [Test]
        public void IsInitialized_BeforePackageInit_ReturnsFalse()
        {
            Assert.IsFalse(_module.IsInitialized);
        }

        /// <summary>未设置 DefaultPackage 时 LoadSync 应抛 InvalidOperationException。</summary>
        [Test]
        public void LoadSync_BeforeInit_Throws()
        {
            Assert.Throws<System.InvalidOperationException>(() => _module.LoadSync<UnityEngine.Object>("test"));
        }

        /// <summary>
        /// 初始化回调未抛异常，但没有成功初始化 YooAsset 包时不能绑定默认包或报告成功。
        /// </summary>
        [Test]
        public void InitializePackageAsync_UninitializedPackage_ReturnsFalse()
        {
            string name = "Uninitialized_" + System.Guid.NewGuid().ToString("N");
            bool result = _module.InitializePackageAsync(name, _ => Cysharp.Threading.Tasks.UniTask.CompletedTask).GetAwaiter().GetResult();
            Assert.IsFalse(result);
            Assert.IsFalse(_module.IsInitialized);
            Assert.IsNull(_module.DefaultPackageName);
            YooAsset.YooAssets.RemovePackage(name);
        }

        /// <summary>
        /// 关闭后的模块不得调用初始化器或创建全局 YooAsset 包。
        /// </summary>
        [Test]
        public void InitializePackageAsync_AfterShutdown_DoesNotCreatePackage()
        {
            string name = "Closed_" + System.Guid.NewGuid().ToString("N");
            _module.OnShutdown();
            bool initializerCalled = false;
            bool result = _module.InitializePackageAsync(name, _ =>
            {
                initializerCalled = true;
                return Cysharp.Threading.Tasks.UniTask.CompletedTask;
            }).GetAwaiter().GetResult();
            Assert.IsFalse(result);
            Assert.IsFalse(initializerCalled);
            Assert.IsFalse(YooAsset.YooAssets.ContainsPackage(name));
            Assert.IsFalse(_module.BindDefaultPackage(name));
            Assert.IsNull(_module.DefaultPackageName);
        }

        /// <summary>InitializePackageAsync 传入 null packageName 应抛 ArgumentNullException。</summary>
        [Test]
        public void InitializePackageAsync_NullPackageName_Throws()
        {
            Assert.Throws<System.ArgumentNullException>(() =>
                _module.InitializePackageAsync(null, _ => Cysharp.Threading.Tasks.UniTask.CompletedTask)
                    .GetAwaiter().GetResult());
        }

        /// <summary>InitializePackageAsync 传入 null initializer 应抛 ArgumentNullException。</summary>
        [Test]
        public void InitializePackageAsync_NullInitializer_Throws()
        {
            Assert.Throws<System.ArgumentNullException>(() =>
                _module.InitializePackageAsync("pkg", null).GetAwaiter().GetResult());
        }
    }
}
