using System;
using UnityEngine;

namespace GameLogic.Gameplay.Level
{
    /// <summary>
    /// 直接实例化完整地图资源的表现租约，不生成正式敌人或交互内容。
    /// </summary>
    public sealed class RegionMapView : IDisposable
    {
        private bool disposed;
        public GameObject Root { get; }
        public RegionMapGeometry Geometry { get; }

        /// <summary>
        /// 创建已经完成绑定校验的完整地图表现。
        /// </summary>
        /// <param name="map">对应地图几何。</param>
        /// <param name="prefab">完整原生地图资源。</param>
        /// <param name="parent">场景父节点。</param>
        private RegionMapView(RegionMapDefinition map, GameObject prefab, Transform parent)
        {
            Geometry = new RegionMapGeometry(map);
            Root = UnityEngine.Object.Instantiate(prefab, parent, false);
            Root.name = "RegionMap_" + map.MapId;
        }

        /// <summary>
        /// 以有效区域运行态和绑定地图建立完整表现。
        /// </summary>
        /// <param name="runtime">已恢复或持久确认的区域状态。</param>
        /// <param name="map">与冻结版本匹配的几何描述。</param>
        /// <param name="prefab">已经加载的完整地图资源。</param>
        /// <param name="parent">可选场景父节点。</param>
        /// <returns>有效视图，输入或版本不匹配时为空。</returns>
        public static RegionMapView Create(RegionRuntimeState runtime, RegionMapDefinition map, GameObject prefab, Transform parent = null)
        {
            if (runtime == null || prefab == null || !runtime.TryValidate(map, out _) ||
                prefab.transform.Find("RegionMap_" + map.MapId + "_" + map.Version) == null) return null;
            return new RegionMapView(map, prefab, parent);
        }

        /// <summary>
        /// 释放当前场景地图实例，不改变区域运行记录。
        /// </summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Root.SetActive(false);
            if (Application.isPlaying) UnityEngine.Object.Destroy(Root);
            else UnityEngine.Object.DestroyImmediate(Root);
        }
    }
}
