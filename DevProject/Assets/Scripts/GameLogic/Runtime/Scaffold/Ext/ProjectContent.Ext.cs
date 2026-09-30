using GameFramework;
using GameLogic.Project;
using GameLogic.Scenes.Expedition;
using GameLogic.UI.Battle;
using GameLogic.UI.Warehouse;
using GameLogic.UI.Skills;
using UnityEngine;

namespace GameLogic.Scaffold
{
    /// <summary>
    /// 项目登记后的手写扩展。
    /// </summary>
    public static partial class ProjectContent
    {
        private static ISceneModule projectScenes;
        private static IUIModule projectUi;
        /// <summary>
        /// 当前框架实例登记完成后的业务扩展。
        /// </summary>
        /// <param name="scenes">当前场景模块。</param>
        /// <param name="ui">当前窗口模块。</param>
        static partial void OnRegistered(ISceneModule scenes, IUIModule ui)
        {
            if (!ReferenceEquals(projectScenes, scenes))
            {
                RegisterScene<ExpeditionScene>(scenes, ProjectServices.BattleSceneName);
                projectScenes = scenes;
            }
            if (ReferenceEquals(projectUi, ui)) return;
            ui.ConfigureWindows(new[]
            {
                CreateProjectWindow<WarehouseWindow>(2101, "Warehouse", GameUILayers.Popup, true),
                CreateProjectWindow<SkillsWindow>(2102, "Skills", GameUILayers.Popup, true),
                CreateProjectWindow<BattleHudWindow>(ProjectServices.BattleHudWindow, "BattleHud", GameUILayers.Hud, false)
            }, registry =>
            {
                registry.Register<WarehouseWindow>(2101);
                registry.Register<SkillsWindow>(2102);
                registry.Register<BattleHudWindow>(ProjectServices.BattleHudWindow);
            });
            projectUi = ui;
            GameEntry.ApplicationScope.Defer(() => { projectScenes = null; projectUi = null; });
        }

        /// <summary>
        /// 创建由代码构建内容的项目窗口配置。
        /// </summary>
        /// <typeparam name="T">窗口逻辑类型。</typeparam>
        /// <param name="id">窗口身份。</param>
        /// <param name="name">窗口名称。</param>
        /// <param name="layer">框架渲染层。</param>
        /// <param name="modal">是否启用模态遮罩。</param>
        /// <returns>场景作用域窗口配置。</returns>
        private static GameWindowConfig CreateProjectWindow<T>(int id, string name, int layer, bool modal) where T : GameWindow
            => new(id, name, typeof(T).FullName, "", layer, GameWindowScope.Scene, GameWindowCacheMode.Immediate,
                0, false, modal, new Color(0, 0, 0, 0.7f));
    }
}
