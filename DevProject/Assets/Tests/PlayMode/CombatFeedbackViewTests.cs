#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using GameLogic.Gameplay.Combat;
using GameLogic.Gameplay.Expedition;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace MiniGame.Tests
{
    /// <summary>
    /// 使用真实反馈预制体检查战斗表现与模拟时间边界。
    /// </summary>
    public sealed class CombatFeedbackViewTests
    {
        private GameObject host;
        private Camera camera;
        private GameObject playerModel;
        private GameObject enemyModel;
        private CombatWorld world;
        private CombatFeedbackView view;

        /// <summary>
        /// 测试失败时也释放战斗世界和全部 Unity 表现对象。
        /// </summary>
        /// <returns>等待 Unity 完成延迟销毁。</returns>
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            view?.Dispose();
            world?.Dispose();
            if (playerModel != null) UnityEngine.Object.Destroy(playerModel);
            if (enemyModel != null) UnityEngine.Object.Destroy(enemyModel);
            if (camera != null) UnityEngine.Object.Destroy(camera.gameObject);
            if (host != null) UnityEngine.Object.Destroy(host);
            yield return null;
        }

        /// <summary>
        /// 验证血条宽度、短暂反馈对象池、暂停时间、换房清理和销毁。
        /// </summary>
        /// <returns>等待 Unity 完成延迟销毁。</returns>
        [UnityTest]
        public IEnumerator Feedback_UsesRealPrefabsAndSimulationTime()
        {
            CombatFeedbackAssets assets = LoadAssets();
            host = new GameObject("FeedbackTestHost");
            camera = new GameObject("FeedbackTestCamera").AddComponent<Camera>();
            playerModel = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            enemyModel = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            var models = new Dictionary<int, GameObject> { [1] = playerModel, [2] = enemyModel };
            world = new CombatWorld(ProjectExtraction.Tests.Shared.TestCombatCatalog.CreatePrototype());
            world.SetRoomBounds(1, new Rect(-20f, -20f, 40f, 40f));
            CombatEntityRef player = world.CreateActor(new CombatActorSpec
            {
                ActorId = 1, RoomId = 1, Team = 1, IsPlayer = true, Position = Vector3.zero,
                MaxHealth = 100f, MoveSpeed = 0f, Radius = 0.3f
            });
            CombatEntityRef enemy = world.CreateActor(new CombatActorSpec
            {
                ActorId = 2, RoomId = 1, Team = 2, Position = new Vector3(0f, 0f, -1f),
                MaxHealth = 100f, MoveSpeed = 0f, Radius = 0.3f
            });
            playerModel.transform.position = Vector3.up;
            enemyModel.transform.position = new Vector3(0f, 1f, -1f);
            view = new CombatFeedbackView(host.transform, camera, assets);
            Transform feedbackRoot = host.transform.Find("CombatFeedback");
            Assert.That(feedbackRoot, Is.Not.Null);
            view.Refresh(world, 1, models, 0d);
            GameObject health = FindClone(feedbackRoot, assets.HealthBar.name);
            Assert.That(health, Is.Not.Null);
            RectTransform fill = health.transform.Find("Fill") as RectTransform;
            Assert.That(fill, Is.Not.Null);
            float fullWidth = fill.sizeDelta.x;
            Assert.That(fullWidth, Is.GreaterThan(0f));

            Assert.That(world.TryStartSkill(enemy, 1, player), Is.True);
            view.Refresh(world, 1, models, 0.1d);
            LineRenderer line = feedbackRoot.GetComponentInChildren<LineRenderer>(true);
            Assert.That(line, Is.Not.Null);
            Assert.That(line.gameObject.activeInHierarchy, Is.True);
            Assert.That(line.positionCount, Is.GreaterThan(3));

            view.Present(new CombatEvent(CombatEventKind.Damage, 0, 1, 2, 1, 0, 23f, Vector3.zero, true));
            view.Present(new CombatEvent(CombatEventKind.Dodged, 0, 1, 2, 1, 0, 0f, Vector3.zero));
            UnityEngine.UI.Text[] labels = feedbackRoot.GetComponentsInChildren<UnityEngine.UI.Text>();
            Assert.That(labels.Length, Is.EqualTo(2));
            Assert.That(System.Array.Exists(labels, item => item.text.Contains("暴击")), Is.True);
            Assert.That(System.Array.Exists(labels, item => item.text == "闪避"), Is.True);
            GameObject textClone = FindClone(feedbackRoot, assets.DamageText.name);
            Assert.That(textClone.transform.localScale.x, Is.EqualTo(assets.DamageText.transform.localScale.x * 1.25f).Within(0.0001f));
            Assert.That(ActiveClones(feedbackRoot, assets.Hit.name), Is.EqualTo(1));

            Vector3 textPosition = textClone.transform.position;
            float textAlpha = textClone.transform.Find("Label").GetComponent<UnityEngine.UI.Text>().color.a;
            view.Refresh(world, 1, models, 0.1d);
            Assert.That(textClone.transform.position, Is.EqualTo(textPosition));
            Assert.That(textClone.transform.Find("Label").GetComponent<UnityEngine.UI.Text>().color.a, Is.EqualTo(textAlpha));
            view.Refresh(world, 2, models, 0.2d);
            Assert.That(ActiveClones(feedbackRoot, assets.DamageText.name), Is.Zero);
            Assert.That(ActiveClones(feedbackRoot, assets.Telegraph.name), Is.Zero);
            Assert.That(ActiveClones(feedbackRoot, assets.HealthBar.name), Is.Zero);

            view.Refresh(world, 1, models, 0.2d);
            for (int i = 0; i < 11; i++) world.Step();
            Assert.That(world.TryGetActor(player, out CombatActorSnapshot damagedPlayer), Is.True);
            Assert.That(damagedPlayer.Health, Is.LessThan(damagedPlayer.MaxHealth), "施法方向应让真实目标受伤");
            view.Refresh(world, 1, models, 0.4d);
            health = FindClone(feedbackRoot, assets.HealthBar.name);
            Assert.That(health, Is.Not.Null);
            fill = health.transform.Find("Fill") as RectTransform;
            Assert.That(fill.sizeDelta.x, Is.GreaterThan(0f).And.LessThan(fullWidth));
            view.Present(new CombatEvent(CombatEventKind.Damage, world.TickIndex, 1, 2, 1, 0, 12f, Vector3.zero));
            Assert.That(ActiveClones(feedbackRoot, assets.DamageText.name), Is.GreaterThan(0));
            view.Refresh(world, 1, models, 0.4d);
            Assert.That(ActiveClones(feedbackRoot, assets.DamageText.name), Is.GreaterThan(0));
            view.Refresh(world, 1, models, 2d);
            Assert.That(ActiveClones(feedbackRoot, assets.DamageText.name), Is.Zero);
            Assert.That(ActiveClones(feedbackRoot, assets.Hit.name), Is.Zero);

            view.Dispose();
            Assert.That(feedbackRoot.gameObject.activeSelf, Is.False);
            yield return null;
            Assert.That(feedbackRoot == null, Is.True);
        }

        /// <summary>
        /// 从项目实际资源中加载反馈预制体，不构建测试替身。
        /// </summary>
        /// <returns>已设置真实预制体的资源集。</returns>
        private static CombatFeedbackAssets LoadAssets()
        {
            var result = new CombatFeedbackAssets();
            SetAsset(result, nameof(CombatFeedbackAssets.HealthBar), "PF_CombatHealthBar");
            SetAsset(result, nameof(CombatFeedbackAssets.DamageText), "PF_CombatDamageText");
            SetAsset(result, nameof(CombatFeedbackAssets.Hit), "PF_CombatHit");
            SetAsset(result, nameof(CombatFeedbackAssets.Telegraph), "PF_CombatTelegraph");
            return result;
        }

        /// <summary>
        /// 按资源名称查找项目预制体并注入只读资源集。
        /// </summary>
        /// <param name="target">待注入的反馈资源集。</param>
        /// <param name="property">目标资源属性名称。</param>
        /// <param name="name">预制体资源名称。</param>
        private static void SetAsset(CombatFeedbackAssets target, string property, string name)
        {
            string[] ids = AssetDatabase.FindAssets(name + " t:Prefab");
            GameObject prefab = null;
            foreach (string id in ids)
            {
                GameObject candidate = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(id));
                if (candidate != null && candidate.name == name) { prefab = candidate; break; }
            }
            Assert.That(prefab, Is.Not.Null, name + " 预制体缺失");
            typeof(CombatFeedbackAssets).GetProperty(property).GetSetMethod(true).Invoke(target, new object[] { prefab });
        }

        /// <summary>
        /// 查找指定预制体名称的活跃实例。
        /// </summary>
        /// <param name="root">反馈对象根节点。</param>
        /// <param name="name">预制体资源名称。</param>
        /// <returns>首个活跃实例；未找到时为空。</returns>
        private static GameObject FindClone(Transform root, string name)
        {
            foreach (Transform child in root)
                if (child.gameObject.activeSelf && child.name.StartsWith(name)) return child.gameObject;
            return null;
        }

        /// <summary>
        /// 统计指定预制体名称的活跃实例。
        /// </summary>
        /// <param name="root">反馈对象根节点。</param>
        /// <param name="name">预制体资源名称。</param>
        /// <returns>当前活跃实例数。</returns>
        private static int ActiveClones(Transform root, string name)
        {
            int count = 0;
            foreach (Transform child in root)
                if (child.gameObject.activeSelf && child.name.StartsWith(name)) count++;
            return count;
        }
    }
}
#endif
