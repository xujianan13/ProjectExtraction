using UnityEngine;

namespace GameFramework
{
    /// <summary>
    /// 普通组件的作用域宿主，默认随销毁结束；可显式选择每次禁用时结束、启用时新建。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LifetimeScopeBehaviour : MonoBehaviour
    {
        [SerializeField] private bool disposeOnDisable;

        /// <summary>
        /// 当前作用域；首次唤醒前为空。异步业务必须捕获此对象，不能在等待后重新读取新一轮属性。
        /// </summary>
        public LifetimeScope Scope { get; private set; }

        /// <summary>
        /// 是否随组件禁用结束本轮；默认关闭，避免隐藏对象时误结束整个实例生命周期。
        /// </summary>
        public bool DisposeOnDisable
        {
            get => disposeOnDisable;
            set
            {
                disposeOnDisable = value;
                if (value && !isActiveAndEnabled) Scope?.Dispose();
            }
        }

        /// <summary>
        /// 在组件首次唤醒时创建实例作用域。
        /// </summary>
        private void Awake() => Scope = new LifetimeScope($"Behaviour:{name}");

        /// <summary>
        /// 选择禁用结束模式时，为下一轮启用建立新作用域。
        /// </summary>
        private void OnEnable()
        {
            if (disposeOnDisable && Scope?.IsActive != true) Scope = new LifetimeScope($"Behaviour:{name}");
        }

        /// <summary>
        /// 仅在显式选择禁用结束模式时释放本轮登记。
        /// </summary>
        private void OnDisable()
        {
            if (disposeOnDisable) Scope?.Dispose();
        }

        /// <summary>
        /// 对象最终销毁时结束作用域。
        /// </summary>
        private void OnDestroy() => Scope?.Dispose();
    }
}
