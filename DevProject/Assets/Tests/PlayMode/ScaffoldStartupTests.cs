using System.Collections;
using GameFramework;
using GameLogic.Scaffold;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace MiniGame.Tests
{
    /// <summary>
    /// 验证项目登记守卫按框架运行轮次更新，而非只记住模块对象。
    /// </summary>
    public sealed class ScaffoldStartupTests
    {
        /// <summary>
        /// 每项测试结束后停止框架，清除模块和应用作用域。
        /// </summary>
        [TearDown]
        public void TearDown() => GameEntry.StopFramework();

        /// <summary>
        /// 同一轮生成内容和项目扩展各登记一项清理，复用模块重启后仍重新登记。
        /// </summary>
        /// <returns>等待 Unity 完成上一轮 UI 宿主销毁的逐帧测试。</returns>
        [UnityTest]
        public IEnumerator Register_ReusedModulesAcrossFrameworkRestart_UsesFreshScope()
        {
            GameEntry.StopFramework();
            var scenes = new SceneModule();
            var ui = new UIModule();
            RegisterModules(scenes, ui);
            GameEntry.StartFramework();

            LifetimeScope first = GameEntry.ApplicationScope;
            Assert.That(first.IsActive, Is.True);
            int firstBaseline = first.RegistrationCount;
            ProjectScaffoldBridge.Register(scenes, ui);
            Assert.That(first.RegistrationCount, Is.EqualTo(firstBaseline + 2));
            ProjectContent.Register(scenes, ui);
            ProjectScaffoldBridge.Register(scenes, ui);
            Assert.That(first.RegistrationCount, Is.EqualTo(firstBaseline + 2), "同轮重复调用添加了多余清理项。");

            GameEntry.StopFramework();
            Assert.That(first.State, Is.EqualTo(LifetimeScopeState.Disposed));
            Assert.That(first.RegistrationCount, Is.Zero);
            yield return null;

            RegisterModules(scenes, ui);
            GameEntry.StartFramework();
            Assert.That(GameEntry.Get<ISceneModule>(), Is.SameAs(scenes));
            Assert.That(GameEntry.Get<IUIModule>(), Is.SameAs(ui));
            LifetimeScope second = GameEntry.ApplicationScope;
            Assert.That(second, Is.Not.SameAs(first));
            int secondBaseline = second.RegistrationCount;
            ProjectScaffoldBridge.Register(scenes, ui);
            Assert.That(second.RegistrationCount, Is.EqualTo(secondBaseline + 2), "新运行轮次未重新登记清理项。");
            ProjectContent.Register(scenes, ui);
            Assert.That(second.RegistrationCount, Is.EqualTo(secondBaseline + 2));
            GameEntry.StopFramework();
            Assert.That(second.State, Is.EqualTo(LifetimeScopeState.Disposed));
            Assert.That(second.RegistrationCount, Is.Zero);
        }

        /// <summary>
        /// 把相同场景与 UI 模块实例注册到尚未启动的框架。
        /// </summary>
        /// <param name="scenes">跨轮复用的场景模块。</param>
        /// <param name="ui">跨轮复用的 UI 模块。</param>
        private static void RegisterModules(SceneModule scenes, UIModule ui)
        {
            GameEntry.Register<ILogModule>(new LogModule());
            GameEntry.Register<IEventModule>(new EventModule());
            GameEntry.Register<ISceneModule>(scenes);
            GameEntry.Register<IUIModule>(ui);
            GameEntry.RegisterDefaults();
        }
    }
}

