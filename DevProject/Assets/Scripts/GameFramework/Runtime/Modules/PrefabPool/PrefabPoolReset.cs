using System;
using UnityEngine;

namespace GameFramework
{
    /// <summary>
    /// 保存预制体原始层级状态，并在实例归还时恢复可复用的 Unity 状态。
    /// </summary>
    internal sealed class PrefabPoolReset
    {
        private readonly Node[] _nodes;
        private readonly Rigidbody[] _bodies;
        private readonly Rigidbody2D[] _bodies2D;
        private readonly ParticleSystem[] _particles;
        private readonly TrailRenderer[] _trails;

        private readonly struct Node
        {
            internal readonly Transform Transform;
            internal readonly Transform Parent;
            internal readonly Vector3 Position;
            internal readonly Quaternion Rotation;
            internal readonly Vector3 Scale;
            internal readonly bool Active;
            internal readonly Vector2 AnchorMin;
            internal readonly Vector2 AnchorMax;
            internal readonly Vector2 Pivot;
            internal readonly Vector2 SizeDelta;

            /// <summary>
            /// 记录一个节点的原始局部状态。
            /// </summary>
            /// <param name="transform">目标节点。</param>
            internal Node(Transform transform)
            {
                Transform = transform;
                Parent = transform.parent;
                Position = transform.localPosition;
                Rotation = transform.localRotation;
                Scale = transform.localScale;
                Active = transform.gameObject.activeSelf;
                if (transform is RectTransform rect)
                {
                    AnchorMin = rect.anchorMin;
                    AnchorMax = rect.anchorMax;
                    Pivot = rect.pivot;
                    SizeDelta = rect.sizeDelta;
                }
                else
                {
                    AnchorMin = AnchorMax = Pivot = SizeDelta = default;
                }
            }

            /// <summary>
            /// 恢复一个节点的层级和局部状态。
            /// </summary>
            /// <param name="restoreActive">是否恢复节点的原始激活状态。</param>
            internal void Restore(bool restoreActive)
            {
                if (Transform == null) return;
                if (Transform.parent != Parent) Transform.SetParent(Parent, false);
                if (Transform is RectTransform rect)
                {
                    rect.anchorMin = AnchorMin;
                    rect.anchorMax = AnchorMax;
                    rect.pivot = Pivot;
                    rect.sizeDelta = SizeDelta;
                }
                Transform.localPosition = Position;
                Transform.localRotation = Rotation;
                Transform.localScale = Scale;
                if (restoreActive) Transform.gameObject.SetActive(Active);
            }
        }

        /// <summary>
        /// 记录实例生成时的完整子节点状态。
        /// </summary>
        /// <param name="root">预制体实例根节点。</param>
        internal PrefabPoolReset(GameObject root)
        {
            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            _nodes = new Node[transforms.Length];
            for (int i = 0; i < transforms.Length; i++) _nodes[i] = new Node(transforms[i]);
            _bodies = root.GetComponentsInChildren<Rigidbody>(true);
            _bodies2D = root.GetComponentsInChildren<Rigidbody2D>(true);
            _particles = root.GetComponentsInChildren<ParticleSystem>(true);
            _trails = root.GetComponentsInChildren<TrailRenderer>(true);
        }

        /// <summary>
        /// 销毁已经脱离根对象的原始子节点，防止关闭池后遗留资源引用。
        /// </summary>
        /// <param name="root">实例根对象。</param>
        /// <param name="destroy">统一安排 Unity 对象销毁的入口。</param>
        internal void DestroyDetachedNodes(GameObject root, Action<GameObject> destroy)
        {
            for (int i = 1; i < _nodes.Length; i++)
            {
                Transform child = _nodes[i].Transform;
                if (child == null || (root != null && child.IsChildOf(root.transform))) continue;
                destroy(child.gameObject);
            }
        }

        /// <summary>
        /// 恢复原始节点，并清除物理和渲染残留。
        /// </summary>
        /// <param name="root">待恢复的实例。</param>
        internal void Restore(GameObject root)
        {
            if (root == null) return;
            foreach (Node node in _nodes) node.Restore(node.Transform != root.transform);
            foreach (Rigidbody body in _bodies)
            {
                if (body == null || body.isKinematic) continue;
                body.velocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
            foreach (Rigidbody2D body in _bodies2D)
            {
                if (body == null) continue;
                body.velocity = Vector2.zero;
                body.angularVelocity = 0f;
            }
            foreach (ParticleSystem particle in _particles)
                if (particle != null) particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            foreach (TrailRenderer trail in _trails)
                if (trail != null) trail.Clear();
        }
    }
}
