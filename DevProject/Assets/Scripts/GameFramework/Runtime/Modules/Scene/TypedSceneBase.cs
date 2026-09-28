using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace GameFramework
{
    /// <summary>
    /// 带强类型进入参数的场景基类。业务场景继承此类并指定 <typeparamref name="TEnterData"/>。
    /// </summary>
    /// <typeparam name="TEnterData">该场景的进入参数类型，必须继承 <see cref="SceneEnterData"/>。</typeparam>
    public abstract class TypedSceneBase<TEnterData> : SceneBase where TEnterData : SceneEnterData
    {
        /// <summary>创建带类型进入参数的场景实例。</summary>
        /// <param name="name">场景唯一名称。</param>
        protected TypedSceneBase(string name) : base(name) { }

        /// <inheritdoc />
        protected sealed override UniTask OnEnterSceneAsync(SceneEnterData enterData, CancellationToken ct)
        {
            if (enterData == null)
                return OnEnterSceneAsync(null, ct);

            if (enterData is TEnterData typed)
                return OnEnterSceneAsync(typed, ct);

            throw new ArgumentException(
                $"Scene \"{Name}\" expects enter data of type {typeof(TEnterData).Name}, got {enterData.GetType().Name}.");
        }

        /// <summary>进入场景（强类型参数）。</summary>
        protected virtual UniTask OnEnterSceneAsync(TEnterData enterData, CancellationToken ct) => UniTask.CompletedTask;
    }
}
