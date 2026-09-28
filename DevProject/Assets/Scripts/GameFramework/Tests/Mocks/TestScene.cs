using System.Threading;
using Cysharp.Threading.Tasks;

namespace GameFramework.Tests.Mocks
{
    /// <summary>
    /// 可追踪生命周期的测试用逻辑场景。
    /// </summary>
    internal sealed class TestScene : SceneBase
    {
        public int EnterCount { get; private set; }
        public int ExitCount { get; private set; }
        public string LastEnterDataName { get; private set; }

        public TestScene(string name) : base(name) { }

        protected override UniTask OnEnterSceneAsync(SceneEnterData enterData, CancellationToken ct)
        {
            EnterCount++;
            LastEnterDataName = enterData?.GetType().Name;
            return UniTask.CompletedTask;
        }

        protected override UniTask OnExitSceneAsync(CancellationToken ct)
        {
            ExitCount++;
            return UniTask.CompletedTask;
        }
    }

    /// <summary>TypedSceneBase 测试用进入参数。</summary>
    internal sealed class TestEnterData : SceneEnterData { }

    /// <summary>TypedSceneBase 测试用错误类型进入参数。</summary>
    internal sealed class WrongEnterData : SceneEnterData { }

    /// <summary>强类型进入参数场景。</summary>
    internal sealed class TypedTestScene : TypedSceneBase<TestEnterData>
    {
        public TypedTestScene(string name) : base(name) { }
    }
}
