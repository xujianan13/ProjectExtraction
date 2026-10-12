using System.Collections.Generic;
using System.Reflection;
using GameLogic.Gameplay.Expedition;
using NUnit.Framework;
using UnityEngine;

namespace ProjectExtraction.Tests.Editor
{
    /// <summary>
    /// 地图目标接近停点跨多段路线裁剪的几何回归测试。
    /// </summary>
    public sealed class ExpeditionNavigationApproachTests
    {
        /// <summary>
        /// 末段短于接近距离时，停点应落在更早的合法线段上。
        /// </summary>
        [Test]
        public void ShortFinalSegment_TrimsEarlierSegmentToRequiredDistance()
        {
            var route = new List<Vector3>
            {
                new Vector3(0f, 2f, 0f), new Vector3(4f, 2f, 0f),
                new Vector3(4.8f, 2f, 0f), new Vector3(5f, 2f, 0f)
            };
            Trim(route, 1.5f);
            Assert.That(route.Count, Is.EqualTo(2));
            AssertPoint(route[0], new Vector3(0f, 2f, 0f));
            AssertPoint(route[1], new Vector3(3.5f, 2f, 0f));
            Assert.That(Vector3.Distance(route[1], new Vector3(5f, 2f, 0f)), Is.EqualTo(1.5f).Within(0.00001f));
        }

        /// <summary>
        /// 目标圆内有多个拐点时，应裁去整个后缀并在斜向线段上保留准确交点。
        /// </summary>
        [Test]
        public void MultipleInsideCorners_PreservesPrefixAndStopsAtDiagonalIntersection()
        {
            var route = new List<Vector3>
            {
                new Vector3(-4f, 2f, -2f), new Vector3(-2f, 2f, -2f), new Vector3(0.5f, 2f, 0.5f),
                new Vector3(0.5f, 2f, 0f), new Vector3(0f, 2f, 0f)
            };
            Trim(route, 1f);
            Assert.That(route.Count, Is.EqualTo(3));
            AssertPoint(route[0], new Vector3(-4f, 2f, -2f));
            AssertPoint(route[1], new Vector3(-2f, 2f, -2f));
            float component = -Mathf.Sqrt(0.5f);
            AssertPoint(route[2], new Vector3(component, 2f, component));
            Assert.That(Vector3.Distance(route[2], new Vector3(0f, 2f, 0f)), Is.EqualTo(1f).Within(0.00001f));
        }

        /// <summary>
        /// 起点已经足够接近目标时，不应继续沿绕路移动，并保持完整路线的双端点契约。
        /// </summary>
        [Test]
        public void OriginAlreadyNearTarget_ReturnsRepeatedOrigin()
        {
            var origin = new Vector3(0.25f, 2f, 0.25f);
            var route = new List<Vector3> { origin, new Vector3(2f, 2f, 2f), new Vector3(0f, 2f, 0f) };
            Trim(route, 1f);
            Assert.That(route, Is.EqualTo(new[] { origin, origin }));
        }

        /// <summary>
        /// 直达路线应沿原线段缩短指定距离，保留原起点与统一高度。
        /// </summary>
        [Test]
        public void StraightRoute_StopsAtRequestedOffset()
        {
            var route = new List<Vector3> { new Vector3(-5f, 2f, 3f), new Vector3(5f, 2f, 3f) };
            Trim(route, 2f);
            Assert.That(route.Count, Is.EqualTo(2));
            AssertPoint(route[0], new Vector3(-5f, 2f, 3f));
            AssertPoint(route[1], new Vector3(3f, 2f, 3f));
        }

        /// <summary>
        /// 起点正好位于目标圆上时同样应立即到达，不增加向内移动。
        /// </summary>
        [Test]
        public void OriginOnApproachBoundary_ReturnsRepeatedOrigin()
        {
            var origin = new Vector3(-1f, 2f, 0f);
            var route = new List<Vector3> { origin, new Vector3(0f, 2f, 0f) };
            Trim(route, 1f);
            Assert.That(route, Is.EqualTo(new[] { origin, origin }));
        }

        /// <summary>
        /// 圆内重复路点不产生零长度除法，裁剪结果保持有限并满足目标间距。
        /// </summary>
        [Test]
        public void RepeatedInsideWaypoints_StillFindsFiniteStop()
        {
            var inside = new Vector3(0.2f, 2f, 0f);
            var route = new List<Vector3>
            {
                new Vector3(-3f, 2f, 0f), inside, inside, new Vector3(0f, 2f, 0f), new Vector3(0f, 2f, 0f)
            };
            Trim(route, 1f);
            Assert.That(route.Count, Is.EqualTo(2));
            AssertPoint(route[1], new Vector3(-1f, 2f, 0f));
        }

        /// <summary>
        /// 在不启动局内生命周期的情况下调用接近路线裁剪，隔离验证几何算法。
        /// </summary>
        /// <param name="route">待裁剪的完整路线。</param>
        /// <param name="distance">目标接近圆半径。</param>
        private static void Trim(List<Vector3> route, float distance)
        {
            MethodInfo method = typeof(ExpeditionRun).GetMethod("TrimNavigationApproach", BindingFlags.NonPublic | BindingFlags.Static);
            method.Invoke(null, new object[] { route, distance });
        }

        /// <summary>
        /// 比较期望坐标，允许二次求交的浮点误差。
        /// </summary>
        /// <param name="actual">实际坐标。</param>
        /// <param name="expected">期望坐标。</param>
        private static void AssertPoint(Vector3 actual, Vector3 expected) =>
            Assert.That((actual - expected).sqrMagnitude, Is.LessThan(0.00000001f));
    }
}
