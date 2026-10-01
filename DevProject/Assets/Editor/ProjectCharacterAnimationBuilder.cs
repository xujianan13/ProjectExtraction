using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace MiniGame.Editor
{
    /// <summary>
    /// 创建不依赖热更脚本的胶囊体角色动画资源，并为测试角色预制体补齐 Animator。
    /// </summary>
    public static class ProjectCharacterAnimationBuilder
    {
        private const string AnimationRoot = "Assets/GameResources/Art/Prototype/Animations";
        private const string ClipFolder = AnimationRoot + "/Clips";
        private const string ControllerFolder = AnimationRoot + "/Controllers";
        private const string ControllerPath = ControllerFolder + "/AC_TestCharacter.controller";
        private const string CharacterFolder = "Assets/GameResources/Gameplay/Expedition/Characters";
        private static readonly string[] CharacterNames = { "PF_TestPlayer", "PF_TestEnemy", "PF_TestBoss", "PF_TestNpc" };
        private static readonly string[] StateNames = { "Idle", "Move", "Attack", "Hit", "Death" };
        private static readonly string[] ClipNames = { "AN_TestIdle", "AN_TestMove", "AN_TestAttack", "AN_TestHit", "AN_TestDeath" };

        private struct Pose
        {
            public float Time;
            public Vector3 Position;
            public Vector3 Euler;
            public Vector3 Scale;

            /// <summary>
            /// 定义一个子网格的局部姿态关键帧。
            /// </summary>
            /// <param name="time">片段内的秒数。</param>
            /// <param name="position">相对于预制体根节点的位置。</param>
            /// <param name="euler">子网格的欧拉旋转角度。</param>
            /// <param name="scale">子网格的局部缩放。</param>
            public Pose(float time, Vector3 position, Vector3 euler, Vector3 scale)
            {
                Time = time;
                Position = position;
                Euler = euler;
                Scale = scale;
            }
        }

        /// <summary>
        /// 从菜单创建尚不存在的角色动画资产并更新测试角色预制体。
        /// </summary>
        [MenuItem("Tools/ProjectExtraction/Create Character Animations")]
        public static void CreateMissing()
        {
            EnsureMissing();
        }

        /// <summary>
        /// 补齐五个动画片段、单层控制器和四个测试角色的 Animator。
        /// </summary>
        /// <returns>所有必需资源均可用且预制体处理成功时为真。</returns>
        public static bool EnsureMissing()
        {
            EnsureFolder(ClipFolder);
            EnsureFolder(ControllerFolder);
            AnimationClip[] clips = new AnimationClip[ClipNames.Length];
            Pose[][] poses = CreatePoses();
            float[] lengths = { 1f, 0.6f, 1f, 0.18f, 0.6f };
            for (int index = 0; index < clips.Length; index++)
            {
                if (!TryEnsureClip(ClipNames[index], lengths[index], index < 2, poses[index], out clips[index])) return false;
            }
            if (!TryEnsureController(clips, out AnimatorController controller)) return false;
            foreach (string name in CharacterNames)
            {
                if (!TryEnsureAnimator(CharacterFolder + "/" + name + ".prefab", controller)) return false;
            }
            AssetDatabase.SaveAssets();
            Debug.Log("角色动画资源已就绪：五个片段、单层控制器和四个测试角色 Animator。");
            return true;
        }

        /// <summary>
        /// 建立各片段的网格局部姿态；攻击在 0.625 秒到达挥击命中姿态。
        /// </summary>
        /// <returns>与片段名称顺序一致的姿态序列。</returns>
        private static Pose[][] CreatePoses()
        {
            return new[]
            {
                new[]
                {
                    P(0f, 0f, 0f, 0f, 1f, 1f, 1f), P(0.5f, 0f, 0.015f, 0f, 1.01f, 0.99f, 1.01f),
                    P(1f, 0f, 0f, 0f, 1f, 1f, 1f)
                },
                new[]
                {
                    P(0f, 0f, 0f, 0f, 1f, 1f, 1f), P(0.15f, 0.08f, 0f, -5f, 1f, 1f, 1f),
                    P(0.3f, 0f, 0f, 0f, 1f, 1f, 1f), P(0.45f, 0.08f, 0f, 5f, 1f, 1f, 1f),
                    P(0.6f, 0f, 0f, 0f, 1f, 1f, 1f)
                },
                new[]
                {
                    P(0f, 0f, 0f, 0f, 1f, 1f, 1f), P(0.25f, -0.03f, -0.08f, -14f, 1f, 1f, 1f),
                    P(0.48f, 0.03f, 0.08f, 10f, 1f, 1f, 1f), P(0.625f, 0.05f, 0.16f, 20f, 1f, 1f, 1f),
                    P(0.8f, 0.02f, 0.06f, 8f, 1f, 1f, 1f), P(1f, 0f, 0f, 0f, 1f, 1f, 1f)
                },
                new[]
                {
                    P(0f, 0f, 0f, 0f, 1f, 1f, 1f), P(0.08f, -0.04f, -0.12f, -16f, 0.98f, 0.98f, 0.98f),
                    P(0.18f, 0f, 0f, 0f, 1f, 1f, 1f)
                },
                new[]
                {
                    P(0f, 0f, 0f, 0f, 1f, 1f, 1f), P(0.15f, -0.12f, -0.08f, -30f, 1f, 0.96f, 1f),
                    P(0.35f, -0.38f, -0.16f, -68f, 1.08f, 0.75f, 1f),
                    P(0.6f, -0.5f, -0.18f, -80f, 1.12f, 0.64f, 1f)
                }
            };
        }

        /// <summary>
        /// 创建单个只包含局部位置、旋转和缩放的姿态。
        /// </summary>
        /// <param name="time">关键帧秒数。</param>
        /// <param name="height">子网格局部高度。</param>
        /// <param name="depth">子网格局部纵深。</param>
        /// <param name="pitch">围绕 X 轴的角度。</param>
        /// <param name="scaleX">X 轴缩放。</param>
        /// <param name="scaleY">Y 轴缩放。</param>
        /// <param name="scaleZ">Z 轴缩放。</param>
        /// <returns>可用于生成动画曲线的姿态。</returns>
        private static Pose P(float time, float height, float depth, float pitch, float scaleX, float scaleY, float scaleZ)
        {
            return new Pose(time, new Vector3(0f, height, depth), new Vector3(pitch, 0f, 0f), new Vector3(scaleX, scaleY, scaleZ));
        }

        /// <summary>
        /// 确保动画片段存在；已有片段不被覆盖。
        /// </summary>
        /// <param name="name">片段资产名。</param>
        /// <param name="length">片段时长。</param>
        /// <param name="loop">是否循环。</param>
        /// <param name="poses">完整的姿态关键帧。</param>
        /// <param name="clip">可用的动画片段。</param>
        /// <returns>片段可用时为真。</returns>
        private static bool TryEnsureClip(string name, float length, bool loop, Pose[] poses, out AnimationClip clip)
        {
            string path = ClipFolder + "/" + name + ".anim";
            Object existing = AssetDatabase.LoadMainAssetAtPath(path);
            if (existing != null)
            {
                clip = existing as AnimationClip;
                if (clip != null) return true;
                Debug.LogError("角色动画生成失败：已有资产不是 AnimationClip：" + path);
                return false;
            }
            clip = new AnimationClip { name = name, frameRate = 60f };
            string[] properties =
            {
                "m_LocalPosition.x", "m_LocalPosition.y", "m_LocalPosition.z",
                "localEulerAnglesRaw.x", "localEulerAnglesRaw.y", "localEulerAnglesRaw.z",
                "m_LocalScale.x", "m_LocalScale.y", "m_LocalScale.z"
            };
            for (int channel = 0; channel < properties.Length; channel++)
            {
                Keyframe[] keys = new Keyframe[poses.Length];
                for (int index = 0; index < poses.Length; index++) keys[index] = new Keyframe(poses[index].Time, PoseChannel(poses[index], channel));
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Mesh", typeof(Transform), properties[channel]), new AnimationCurve(keys));
            }
            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.startTime = 0f;
            settings.stopTime = length;
            settings.loopTime = loop;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            AssetDatabase.CreateAsset(clip, path);
            if (AssetDatabase.LoadAssetAtPath<AnimationClip>(path) != null) return true;
            Debug.LogError("角色动画生成失败：无法保存片段 " + path);
            return false;
        }

        /// <summary>
        /// 提取指定的局部姿态曲线通道。
        /// </summary>
        /// <param name="pose">源姿态。</param>
        /// <param name="channel">位置、旋转或缩放分量索引。</param>
        /// <returns>该通道的关键帧数值。</returns>
        private static float PoseChannel(Pose pose, int channel)
        {
            if (channel < 3) return pose.Position[channel];
            if (channel < 6) return pose.Euler[channel - 3];
            return pose.Scale[channel - 6];
        }

        /// <summary>
        /// 创建只有 Base Layer 的五状态控制器，不设置自动状态转换。
        /// </summary>
        /// <param name="clips">按状态顺序排列的五个片段。</param>
        /// <param name="controller">现有或新建的控制器。</param>
        /// <returns>控制器可用时为真。</returns>
        private static bool TryEnsureController(AnimationClip[] clips, out AnimatorController controller)
        {
            Object existing = AssetDatabase.LoadMainAssetAtPath(ControllerPath);
            if (existing != null)
            {
                controller = existing as AnimatorController;
                if (controller != null) return true;
                Debug.LogError("角色动画生成失败：已有资产不是 AnimatorController：" + ControllerPath);
                return false;
            }
            controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            if (controller == null || controller.layers.Length != 1)
            {
                Debug.LogError("角色动画生成失败：无法创建单层控制器 " + ControllerPath);
                return false;
            }
            AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;
            for (int index = 0; index < StateNames.Length; index++)
            {
                AnimatorState state = stateMachine.AddState(StateNames[index]);
                state.motion = clips[index];
                if (index == 0) stateMachine.defaultState = state;
            }
            return true;
        }

        /// <summary>
        /// 在角色预制体根节点补齐 Animator，保留其他控制器的人工配置。
        /// </summary>
        /// <param name="path">角色预制体资产路径。</param>
        /// <param name="controller">测试角色控制器。</param>
        /// <returns>预制体可用或因人工控制器跳过时为真。</returns>
        private static bool TryEnsureAnimator(string path, AnimatorController controller)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                Debug.LogError("角色动画生成失败：缺少测试角色预制体 " + path);
                return false;
            }
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            if (root == null)
            {
                Debug.LogError("角色动画生成失败：无法加载测试角色预制体 " + path);
                return false;
            }
            Animator animator = root.GetComponent<Animator>();
            if (animator != null && animator.runtimeAnimatorController != null && animator.runtimeAnimatorController != controller)
            {
                Debug.Log("角色动画跳过人工控制器：" + path);
                PrefabUtility.UnloadPrefabContents(root);
                return true;
            }
            if (root.transform.Find("Mesh") == null)
            {
                Debug.LogError("角色动画生成失败：预制体缺少 Mesh 子节点 " + path);
                PrefabUtility.UnloadPrefabContents(root);
                return false;
            }
            if (animator == null) animator = root.AddComponent<Animator>();
            bool changed = animator.runtimeAnimatorController != controller || animator.applyRootMotion || animator.cullingMode != AnimatorCullingMode.AlwaysAnimate || animator.speed != 1f;
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.speed = 1f;
            if (!changed) { PrefabUtility.UnloadPrefabContents(root); return true; }
            PrefabUtility.SaveAsPrefabAsset(root, path, out bool saved);
            PrefabUtility.UnloadPrefabContents(root);
            if (saved) return true;
            Debug.LogError("角色动画生成失败：无法保存测试角色预制体 " + path);
            return false;
        }

        /// <summary>
        /// 逐级补齐 Unity 资产目录。
        /// </summary>
        /// <param name="path">Assets 下的目标目录。</param>
        private static void EnsureFolder(string path)
        {
            string[] parts = path.Split('/');
            string parent = parts[0];
            for (int index = 1; index < parts.Length; index++)
            {
                string current = parent + "/" + parts[index];
                if (!AssetDatabase.IsValidFolder(current)) AssetDatabase.CreateFolder(parent, parts[index]);
                parent = current;
            }
        }
    }
}
