using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using GameFramework;
using GameLogic.Project;
using GameLogic.UI.Common;
using UnityEngine;

namespace GameLogic.UI.Run
{
    /// <summary>
    /// Home 中查看最近保留的已保存单局战绩。
    /// </summary>
    public sealed class RunHistoryWindow : GameWindow
    {
        private RectTransform summaries;
        private RectTransform entries;
        private UnityEngine.UI.Text details;
        private ProjectSession session;
        private IReadOnlyList<ProjectRunHistoryEntry> history;
        private int selected;

        /// <summary>
        /// 构建适配窄屏的可滚动汇总、列表和明细。
        /// </summary>
        protected override void Init()
        {
            RectTransform surface = ProjectUiFactory.Fill(RootTransform, "HistoryContent", new Color(0.035f, 0.07f, 0.11f, 0.98f));
            RectTransform layout = ProjectUiFactory.Vertical(surface, "Layout", 12f);
            ProjectUiFactory.Stretch(layout, 22f);
            RectTransform header = ProjectUiFactory.Horizontal(layout, "Header", 12f);
            ProjectUiFactory.Height(header, 72f);
            UnityEngine.UI.Text title = ProjectUiFactory.Label(header, "最近单局战绩", 31, TextAnchor.MiddleLeft, Color.white);
            title.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().flexibleWidth = 1f;
            UnityEngine.UI.Button close = ProjectUiFactory.ActionButton(header, "关闭", () => CloseSelfAsync().Forget());
            close.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().preferredWidth = 110f;

            UnityEngine.UI.ScrollRect summaryScroll = ProjectUiFactory.Scroll(layout, "LevelSummaries", out summaries);
            ProjectUiFactory.Height((RectTransform)summaryScroll.transform, 260f);
            UnityEngine.UI.ScrollRect historyScroll = ProjectUiFactory.Scroll(layout, "HistoryEntries", out entries);
            UnityEngine.UI.LayoutElement historySize = historyScroll.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            historySize.flexibleHeight = 1f;
            historySize.minHeight = 100f;
            UnityEngine.UI.ScrollRect detailScroll = ProjectUiFactory.Scroll(layout, "HistoryDetails", out RectTransform detailContent);
            ProjectUiFactory.Height((RectTransform)detailScroll.transform, 350f);
            details = ProjectUiFactory.Label(detailContent, string.Empty, 23, TextAnchor.UpperLeft, new Color(0.81f, 0.91f, 0.96f));
        }

        /// <summary>
        /// 只接收 Home 就绪且无事务时的已提交会话快照。
        /// </summary>
        /// <param name="args">窗口打开参数。</param>
        protected override void Open(GameWindowOpenArgs args)
        {
            session = ProjectServices.Session;
            ProjectRunCoordinator coordinator = ProjectServices.Coordinator;
            if (session == null || coordinator?.Phase != ProjectRunPhase.HomeReady || session.IsBusy)
            {
                CloseSelfAsync().Forget();
                return;
            }
            history = session.RunHistory;
            selected = history.Count - 1;
        }

        /// <summary>
        /// 显示当前保留样本的关卡汇总和单局列表。
        /// </summary>
        /// <param name="args">窗口显示参数。</param>
        protected override void Show(GameWindowOpenArgs args) => Refresh();

        /// <summary>
        /// 重建只读列表与选中详情。
        /// </summary>
        private void Refresh()
        {
            ProjectUiFactory.Clear(summaries);
            ProjectUiFactory.Clear(entries);
            if (history == null || history.Count == 0)
            {
                AddText(summaries, "暂无已保存单局战绩。旧档不会补造历史局。", 92f);
                details.text = "完成一局并成功保存结算后，这里会显示战绩。";
                return;
            }
            AddText(summaries, $"仅统计最近保留的 {history.Count} 局；撤离率分母包括死亡和放弃。", 86f);
            foreach (ProjectRunHistorySummary row in ProjectRunHistoryRules.Summarize(history))
            {
                string net = row.NetAssetSampleCount == 0 ? "无法计算（有效样本 0）" :
                    $"{row.AverageNetAssetValue:+0.#;-0.#;0}（有效样本 {row.NetAssetSampleCount}）";
                AddText(summaries, $"关卡 {row.LevelId}  ·  样本 {row.Count}  ·  撤离率 {(double)row.ExtractedCount / row.Count:P0}\n" +
                    $"均耗时 {row.AverageElapsedSeconds:0.#} 秒  ·  净资产均值 {net}", 106f);
            }
            for (int index = history.Count - 1; index >= 0; index--)
            {
                int choice = index;
                ProjectRunHistoryEntry entry = history[index];
                string label = $"{(selected == index ? "●" : "○")} {OutcomeText(entry.Outcome)} · 关卡 {entry.LevelId} · " +
                    $"{DateTimeOffset.FromUnixTimeSeconds(entry.EndUtc).UtcDateTime:yyyy-MM-dd HH:mm} UTC";
                UnityEngine.UI.Button button = ProjectUiFactory.ActionButton(entries, label, () => Select(choice));
                ProjectUiFactory.Height((RectTransform)button.transform, 68f);
            }
            ShowDetails();
        }

        /// <summary>
        /// 选择战绩并刷新明细。
        /// </summary>
        /// <param name="index">列表中的战绩索引。</param>
        private void Select(int index)
        {
            selected = index;
            Refresh();
        }

        /// <summary>
        /// 显示冻结的单局表现和估值。
        /// </summary>
        private void ShowDetails()
        {
            if (selected < 0 || selected >= history.Count) return;
            ProjectRunHistoryEntry entry = history[selected];
            ProjectRunStatistics stats = entry.Statistics;
            string performance = stats?.HasData == true
                ? $"房间 {stats.VisitedRooms}/{stats.TotalRooms} · 清房 {stats.ClearedCombatRooms} · 击杀 {stats.EnemyKills}\n" +
                  $"Boss房击杀 {stats.BossRoomKills} · 三选一 {stats.RogueChoices}\n" +
                  $"造成伤害 {stats.DamageDealt:0.#} · 承受伤害 {stats.DamageTaken:0.#} · 治疗 {stats.HealingReceived:0.#}"
                : "本局未记录完整表现统计。";
            string entryValue = entry.HasNetAssetValue ? entry.EntrySellValue.ToString() : "未记录";
            string netValue = entry.HasNetAssetValue ? entry.NetAssetValue.ToString("+0;-0;0") : "无法计算（缺入场估值）";
            details.text = $"{OutcomeText(entry.Outcome)} · 关卡 {entry.LevelId} · {entry.ElapsedSeconds:0.#} 秒\n" +
                $"结束时间 {DateTimeOffset.FromUnixTimeSeconds(entry.EndUtc).UtcDateTime:yyyy-MM-dd HH:mm:ss} UTC\n" +
                $"打怪经验 {entry.KillExperience} · 撤离经验 {entry.ExtractionExperience}\n{performance}\n" +
                $"入场估值 {entryValue} · 带回估值 {entry.RetainedSellValue} · 丢失估值 {entry.LostSellValue}\n" +
                $"金币支出 {entry.GoldSpent} · 净资产变化 {netValue}\n估值按结算时物品出售价格冻结。";
        }

        /// <summary>
        /// 在滚动内容中添加固定高度的说明。
        /// </summary>
        /// <param name="parent">滚动内容节点。</param>
        /// <param name="value">展示文字。</param>
        /// <param name="height">最低布局高度，窄屏可随文字增高。</param>
        private static void AddText(RectTransform parent, string value, float height)
        {
            UnityEngine.UI.Text label = ProjectUiFactory.Label(parent, value, 23, TextAnchor.UpperLeft, new Color(0.81f, 0.91f, 0.96f));
            label.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().minHeight = height;
        }

        /// <summary>
        /// 获取明确的终局文案。
        /// </summary>
        /// <param name="outcome">终局类型。</param>
        /// <returns>中文终局名称。</returns>
        private static string OutcomeText(ProjectRunHistoryOutcome outcome) => outcome == ProjectRunHistoryOutcome.Extracted ? "撤离" :
            outcome == ProjectRunHistoryOutcome.Abandoned ? "放弃" : "死亡";
    }
}
