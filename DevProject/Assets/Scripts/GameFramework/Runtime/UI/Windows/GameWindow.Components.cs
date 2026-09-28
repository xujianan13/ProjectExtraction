using System;
using UnityEngine;

namespace GameFramework
{
    /// <summary>
    /// 管理窗口 Prefab 上的 Unity 组件引用。
    /// </summary>
    public abstract partial class GameWindow
    {
        /// <summary>
        /// 窗口实例对象。
        /// </summary>
        public GameObject Root { get; private set; }

        /// <summary>
        /// 窗口根 RectTransform。
        /// </summary>
        public RectTransform RootTransform { get; private set; }

        /// <summary>
        /// 按相对路径查找必需组件。
        /// </summary>
        /// <typeparam name="T">目标组件类型。</typeparam>
        /// <param name="relativePath">相对于窗口根节点的路径；空字符串表示根节点。</param>
        /// <returns>找到的组件。</returns>
        protected T FindRequired<T>(string relativePath) where T : Component
        {
            if (RootTransform == null)
                throw new InvalidOperationException("窗口组件尚未绑定。");
            Transform target = string.IsNullOrEmpty(relativePath) ? RootTransform : RootTransform.Find(relativePath);
            if (target == null)
                throw new InvalidOperationException($"窗口 {Id} 缺少节点：{relativePath}");
            T component = target.GetComponent<T>();
            if (component == null)
                throw new InvalidOperationException($"窗口 {Id} 节点 {relativePath} 缺少组件 {typeof(T).Name}。");
            return component;
        }

        /// <summary>
        /// 在窗口初始化前绑定组件引用。
        /// </summary>
        protected virtual void BindComponents()
        {
        }

        /// <summary>
        /// 将窗口实例对象与组件引用绑定。
        /// </summary>
        /// <param name="root">窗口实例对象。</param>
        private void AttachComponentsInternal(GameObject root)
        {
            Root = root;
            RootTransform = root.GetComponent<RectTransform>();
            if (RootTransform == null)
                throw new InvalidOperationException($"窗口根节点必须包含 RectTransform：{root.name}");
            BindComponents();
        }

        /// <summary>
        /// 清除窗口实例相关的组件引用。
        /// </summary>
        private void ClearComponentsInternal()
        {
            RootTransform = null;
            Root = null;
        }
    }
}
