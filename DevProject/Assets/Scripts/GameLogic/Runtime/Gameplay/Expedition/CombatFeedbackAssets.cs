using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;
using UnityEngine;
using GameLogic.Project;

namespace GameLogic.Gameplay.Expedition
{
    /// <summary>
    /// 血条、命中反馈与短音效的热更资源集，租约由场景作用域统一拥有。
    /// </summary>
    public sealed class CombatFeedbackAssets
    {
        private static readonly string[] roles = { "HealthBar", "DamageText", "Hit", "Telegraph", "SwingSound", "HitSound", "CriticalSound" };
        public static IReadOnlyList<string> Locations
        {
            get
            {
                var names = new string[roles.Length];
                for (int i = 0; i < roles.Length; i++) names[i] = ProjectContentConfig.Asset(roles[i]);
                return System.Array.AsReadOnly(names);
            }
        }
        public GameObject HealthBar { get; private set; }
        public GameObject DamageText { get; private set; }
        public GameObject Hit { get; private set; }
        public GameObject Telegraph { get; private set; }

        /// <summary>
        /// 加载实际反馈 Prefab 和音效，任何缺失都使本轮入场失败。
        /// </summary>
        /// <param name="scope">场景生命周期。</param>
        /// <param name="resources">项目资源模块。</param>
        /// <param name="ct">入场取消令牌。</param>
        /// <returns>完整资源集；无效资产时为空。</returns>
        public static async UniTask<CombatFeedbackAssets> LoadAsync(LifetimeScope scope, IResourceModule resources, CancellationToken ct)
        {
            var names = Locations;
            var items = new ResourceGroupDefinition.Item[names.Count];
            for (int i = 0; i < names.Count; i++)
                items[i] = new ResourceGroupDefinition.Item(names[i], names[i], i < 4 ?
                    ResourceGroupDefinition.AssetKind.GameObject : ResourceGroupDefinition.AssetKind.AudioClip);
            ResourceGroupLease group = await resources.AcquireGroupAsync(new ResourceGroupDefinition("CombatFeedback", items), ct: ct);
            if (!PlayerLoopHelper.IsMainThread) await UniTask.SwitchToMainThread();
            if (group == null || !scope.TryOwn(group)) return null;
            var result = new CombatFeedbackAssets();
            group.TryGet(names[0], out GameObject health);
            group.TryGet(names[1], out GameObject text);
            group.TryGet(names[2], out GameObject hit);
            group.TryGet(names[3], out GameObject telegraph);
            if (health == null || health.transform.Find("Fill")?.GetComponent<UnityEngine.UI.Image>() == null ||
                text == null || text.transform.Find("Label")?.GetComponent<UnityEngine.UI.Text>() == null ||
                hit == null || hit.GetComponentInChildren<Renderer>() == null || telegraph == null || telegraph.GetComponent<LineRenderer>() == null)
            {
                GameEntry.Get<ILogModule>()?.Error("CombatFeedback", "反馈预制件结构无效，拒绝进入战斗。");
                return null;
            }
            for (int i = 4; i < names.Count; i++)
                if (!group.TryGet(names[i], out AudioClip clip) || clip.length <= 0f) return null;
            result.HealthBar = health;
            result.DamageText = text;
            result.Hit = hit;
            result.Telegraph = telegraph;
            return result;
        }
    }
}
