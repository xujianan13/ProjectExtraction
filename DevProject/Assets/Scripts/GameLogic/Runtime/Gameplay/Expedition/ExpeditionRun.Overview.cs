using System.Collections.Generic;
using GameLogic.Gameplay.Level;
using GameLogic.Project;
using GameLogic.Tables;

namespace GameLogic.Gameplay.Expedition
{
    /// <summary>
    /// 冻结区域请求的独立只读展示投影，不创建尚未进入的区域内容。
    /// </summary>
    public sealed class ExpeditionRegionOverview
    {
        public int RegionIndex { get; }
        public int BossId { get; }
        public int MapId { get; }
        public string BossName { get; }
        public string MapName { get; }
        public string Weakness { get; }
        public bool IsFinal { get; }
        public bool IsGenerated { get; }
        public bool IsCurrent { get; }
        public bool IsPassed { get; }
        public IReadOnlyList<string> CollectionPreviews { get; }

        /// <summary>
        /// 将冻结身份和当前进度投影为独立的玩家可读信息。
        /// </summary>
        /// <param name="request">开局已经保存的区域请求。</param>
        /// <param name="final">是否属于最终场地。</param>
        /// <param name="generated">区域具体内容是否已经生成。</param>
        /// <param name="current">是否属于当前区域。</param>
        /// <param name="passed">是否已经完成并离开的区域。</param>
        /// <param name="collected">已经入库并持久确认的收藏身份。</param>
        internal ExpeditionRegionOverview(RegionGenerationRequest request, bool final, bool generated, bool current, bool passed,
            IReadOnlyList<string> collected)
        {
            CfgBossesRow boss = GameTables.CfgBosses.GetById(request.BossDefinitionId);
            RegionIndex = request.RegionIndex;
            BossId = request.BossDefinitionId;
            MapId = request.MapId;
            BossName = boss.Name;
            MapName = GameTables.CfgRegionMaps.GetById(request.MapId).Name;
            Weakness = boss.WeaknessDescription;
            IsFinal = final;
            IsGenerated = generated;
            IsCurrent = current;
            IsPassed = passed;
            var previews = new List<string>();
            foreach (string id in boss.CollectionPreviewIDs)
                previews.Add(ProjectV2RewardConfig.Collection(id).Name + " · " + (Contains(collected, id) ? "已收藏" : "未收藏"));
            CollectionPreviews = previews.AsReadOnly();
        }

        /// <summary>
        /// 检查已经持久化的图鉴登记，不把局内获得的实例当成入库。
        /// </summary>
        /// <param name="collected">已经确认的图鉴身份。</param>
        /// <param name="id">需要检查的稳定收藏身份。</param>
        /// <returns>该收藏已经登记时为真。</returns>
        private static bool Contains(IReadOnlyList<string> collected, string id)
        {
            foreach (string value in collected) if (value == id) return true;
            return false;
        }
    }

    public sealed partial class ExpeditionRun
    {
        /// <summary>
        /// 读取开局冻结的整关 Boss 与地图计划，不触发未来区域生成或修改运行状态。
        /// </summary>
        /// <returns>各阶段身份、状态和收藏预览的独立只读投影。</returns>
        public IReadOnlyList<ExpeditionRegionOverview> GetRegionOverview()
        {
            var overview = new List<ExpeditionRegionOverview>(regionRequests.Count);
            IReadOnlyList<string> collected = session.CollectedCollectionIds;
            foreach (RegionGenerationRequest request in regionRequests)
                overview.Add(new ExpeditionRegionOverview(request, request.RegionIndex == RegionCount,
                    regions.Exists(value => value.Plan.Request.RegionIndex == request.RegionIndex), request.RegionIndex == CurrentRegionIndex,
                    request.RegionIndex < CurrentRegionIndex, collected));
            return overview.AsReadOnly();
        }
    }
}
