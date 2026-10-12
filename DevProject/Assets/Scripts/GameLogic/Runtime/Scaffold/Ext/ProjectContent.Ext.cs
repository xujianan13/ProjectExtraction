using GameFramework;
using GameLogic.Project;
using GameLogic.Scenes.Expedition;
using GameLogic.UI.Battle;
using GameLogic.UI.Character;
using GameLogic.UI.Run;
using GameLogic.UI.Roguelike;
using GameLogic.UI.Warehouse;
using GameLogic.UI.Skills;
using GameLogic.UI.Shop;
using GameLogic.UI.Settings;
using GameLogic.UI.Guide;
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
            if (!ui.TryConfigureCanvas(new Vector2(1080f, 1920f), 0.5f))
            {
                GameEntry.Get<ILogModule>()?.Error("ProjectContent", "项目 UI Canvas 缩放配置失败，窗口内容未注册。");
                return;
            }
            ui.ConfigureWindows(new[]
            {
                CreateProjectWindow<WarehouseWindow>(2101, "Warehouse", GameUILayers.Popup, true),
                CreateProjectWindow<SkillsWindow>(2102, "Skills", GameUILayers.Popup, true),
                CreateProjectWindow<HomeShopWindow>(ProjectServices.HomeShopWindow, "HomeShop", GameUILayers.Popup, true),
                CreateProjectWindow<CharacterStatsWindow>(2400, "CharacterStats", GameUILayers.Popup, true),
                CreateProjectWindow<CollectionWindow>(ProjectServices.CollectionWindow, "Collection", GameUILayers.Popup, true),
                CreateProjectWindow<ProjectPauseWindow>(ProjectServices.PauseWindow, "Pause", GameUILayers.Popup, true),
                CreateProjectWindow<ProjectSettingsWindow>(ProjectServices.SettingsWindow, "Settings", GameUILayers.Popup, true),
                CreateProjectWindow<ProjectGuideWindow>(ProjectServices.GuideWindow, "Guide", GameUILayers.Popup, true),
                CreateProjectWindow<BattleHudWindow>(ProjectServices.BattleHudWindow, "BattleHud", GameUILayers.Hud, false),
                CreateProjectWindow<ExpeditionInventoryWindow>(ProjectServices.ExpeditionInventoryWindow, "ExpeditionInventory", GameUILayers.Popup, true),
                CreateProjectWindow<ExpeditionMerchantWindow>(ProjectServices.ExpeditionMerchantWindow, "ExpeditionMerchant", GameUILayers.Popup, true),
                CreateProjectWindow<ExpeditionMapWindow>(ProjectServices.ExpeditionMapWindow, "ExpeditionMap", GameUILayers.Popup, true),
                CreateProjectWindow<RegionExitWindow>(ProjectServices.RegionExitWindow, "RegionExit", GameUILayers.Popup, true),
                CreateProjectWindow<BossLootChoiceWindow>(ProjectServices.BossLootWindow, "BossLoot", GameUILayers.Popup, true),
                CreateProjectWindow<DeathChoiceWindow>(ProjectServices.DeathChoiceWindow, "DeathChoice", GameUILayers.Popup, true),
                CreateProjectWindow<RogueChoiceWindow>(ProjectServices.RogueChoiceWindow, "RogueChoice", GameUILayers.Popup, true),
                CreateProjectWindow<RogueOverviewWindow>(ProjectServices.RogueOverviewWindow, "RogueOverview", GameUILayers.Popup, true),
                CreateProjectWindow<RunTransitionWindow>(2300, "RunTransition", GameUILayers.Guide, false, GameWindowScope.Global),
                CreateProjectWindow<RunResultWindow>(2301, "RunResult", GameUILayers.Popup, false, GameWindowScope.Global),
                CreateProjectWindow<RunHistoryWindow>(ProjectServices.RunHistoryWindow, "RunHistory", GameUILayers.Popup, true)
            }, registry =>
            {
                registry.Register<WarehouseWindow>(2101);
                registry.Register<SkillsWindow>(2102);
                registry.Register<HomeShopWindow>(ProjectServices.HomeShopWindow);
                registry.Register<CharacterStatsWindow>(2400);
                registry.Register<CollectionWindow>(ProjectServices.CollectionWindow);
                registry.Register<ProjectPauseWindow>(ProjectServices.PauseWindow);
                registry.Register<ProjectSettingsWindow>(ProjectServices.SettingsWindow);
                registry.Register<ProjectGuideWindow>(ProjectServices.GuideWindow);
                registry.Register<BattleHudWindow>(ProjectServices.BattleHudWindow);
                registry.Register<ExpeditionInventoryWindow>(ProjectServices.ExpeditionInventoryWindow);
                registry.Register<ExpeditionMerchantWindow>(ProjectServices.ExpeditionMerchantWindow);
                registry.Register<ExpeditionMapWindow>(ProjectServices.ExpeditionMapWindow);
                registry.Register<RegionExitWindow>(ProjectServices.RegionExitWindow);
                registry.Register<BossLootChoiceWindow>(ProjectServices.BossLootWindow);
                registry.Register<DeathChoiceWindow>(ProjectServices.DeathChoiceWindow);
                registry.Register<RogueChoiceWindow>(ProjectServices.RogueChoiceWindow);
                registry.Register<RogueOverviewWindow>(ProjectServices.RogueOverviewWindow);
                registry.Register<RunTransitionWindow>(2300);
                registry.Register<RunResultWindow>(2301);
                registry.Register<RunHistoryWindow>(ProjectServices.RunHistoryWindow);
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
        /// <param name="scope">窗口生命周期作用域。</param>
        /// <returns>项目窗口配置。</returns>
        private static GameWindowConfig CreateProjectWindow<T>(int id, string name, int layer, bool modal, GameWindowScope scope = GameWindowScope.Scene) where T : GameWindow
            => new(id, name, typeof(T).FullName, "", layer, scope, GameWindowCacheMode.Immediate,
                0, false, modal, new Color(0, 0, 0, 0.7f));
    }
}
