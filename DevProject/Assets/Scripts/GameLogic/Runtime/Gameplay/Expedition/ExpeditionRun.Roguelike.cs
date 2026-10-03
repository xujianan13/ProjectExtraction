using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;
using GameLogic.Gameplay.Combat;
using GameLogic.Gameplay.Roguelike;
using GameLogic.Project;
using UnityEngine;

namespace GameLogic.Gameplay.Expedition
{
    public sealed partial class ExpeditionRun
    {
        private readonly RogueChoiceSession rogueChoices;
        private readonly IRogueChoiceAccessProvider rogueAccessProvider;
        private readonly System.Random rogueLootRandom;
        private readonly CancellationTokenSource rogueLifetime = new();
        private bool requestingRogueAccess;

        public event Action RogueChoiceChanged;
        public RogueChoiceSnapshot PendingRogueChoice => rogueChoices.Active;
        public bool IsChoosingRogue => rogueChoices.HasPending;
        public string RogueMessage { get; private set; } = string.Empty;
        private bool CanAcceptRogueInput => IsRunning && IsReady && !paused && ActivePanel == ExpeditionPanelKind.None && !disposed && !stopped &&
            !GameEntry.IsInBackground && !session.HasUncommittedSave;

        /// <summary>
        /// 是否可以向已配置的授权适配器请求解锁第四选项。
        /// </summary>
        public bool CanRequestExtraRogueChoice
        {
            get
            {
                RogueChoiceSnapshot offer = PendingRogueChoice;
                return CanAcceptRogueInput && !requestingRogueAccess && rogueAccessProvider != null &&
                    offer != null && offer.Options.Count > 3 && !offer.ExtraUnlocked;
            }
        }

        /// <summary>
        /// 从唯一奖励来源开启三选一，并立即冻结本局战斗与交互。
        /// </summary>
        /// <param name="sourceId">本局稳定奖励来源，同一来源不能重复领取。</param>
        /// <returns>成功创建待选择奖励时为真。</returns>
        public bool TryBeginRogueChoice(string sourceId)
        {
            if (!CanInteract || !World.TryGetActor(Player, out CombatActorSnapshot player) || player.Dead ||
                !rogueChoices.TryBegin(sourceId)) return false;
            ClearRogueInput();
            RogueMessage = "战斗已暂停，请选择一项奖励。";
            PublishRogueChange();
            return true;
        }

        /// <summary>
        /// 校验选项身份并只兑现一次，未完成选择不会解除暂停。
        /// </summary>
        /// <param name="offerId">当前奖励批次。</param>
        /// <param name="revision">当前选项代次。</param>
        /// <param name="index">被选择的选项序号。</param>
        /// <returns>奖励已成功兑现时为真。</returns>
        public bool TryChooseRogue(string offerId, int revision, int index)
        {
            if (!CanAcceptRogueInput || requestingRogueAccess) return false;
            if (!rogueChoices.TrySelect(offerId, revision, index, ApplyRogueReward)) return false;
            ClearRogueInput();
            RogueMessage = "已获得奖励，继续探索。";
            PublishRogueChange();
            return true;
        }

        /// <summary>
        /// 在配置允许时刷新选项，旧按钮和授权回调自动失效。
        /// </summary>
        /// <param name="offerId">当前奖励批次。</param>
        /// <param name="revision">按钮绑定的选项代次。</param>
        /// <returns>成功生成下一代选项时为真。</returns>
        public bool TryRefreshRogue(string offerId, int revision)
        {
            if (!CanAcceptRogueInput || requestingRogueAccess || !rogueChoices.TryRefresh(offerId, revision)) return false;
            RogueMessage = "已刷新，请选择一项奖励。";
            PublishRogueChange();
            return true;
        }

        /// <summary>
        /// 通过后续广告或其他接入方式请求授权，只解锁当前第四选项而不自动发奖。
        /// </summary>
        /// <param name="offerId">当前奖励批次。</param>
        /// <param name="revision">请求所对应的选项代次。</param>
        /// <returns>当前第四选项已获得有效授权时为真。</returns>
        public async UniTask<bool> RequestExtraRogueChoiceAsync(string offerId, int revision)
        {
            if (!CanRequestExtraRogueChoice || !rogueChoices.TryCreateExtraAccessRequest(offerId, revision, out var request)) return false;
            requestingRogueAccess = true;
            RogueMessage = "正在确认额外选项资格，战斗保持暂停。";
            PublishRogueChange();
            try
            {
                RogueChoiceAccessReceipt receipt = await rogueAccessProvider.RequestAsync(request, rogueLifetime.Token).AttachExternalCancellation(rogueLifetime.Token);
                if (disposed || stopped || !IsRunning) return false;
                bool granted = rogueChoices.TryGrantExtraAccess(request, receipt);
                RogueMessage = granted ? "第四选项已解锁，仍可从所有选项中选择一项。" : "未获得授权，可继续选择基础奖励。";
                return granted;
            }
            catch (OperationCanceledException) when (rogueLifetime.IsCancellationRequested)
            {
                return false;
            }
            catch (Exception error)
            {
                rogueChoices.TryGrantExtraAccess(request, null);
                if (!disposed && !stopped)
                {
                    GameEntry.Get<ILogModule>()?.Error("Roguelike", "额外选项授权失败：" + error.Message);
                    RogueMessage = "授权未完成，可重试或选择基础奖励。";
                }
                return false;
            }
            finally
            {
                requestingRogueAccess = false;
                if (!disposed && !stopped) PublishRogueChange();
            }
        }

        /// <summary>
        /// 将一次合法奖励送入 ECS 属性效果或本局背包。
        /// </summary>
        /// <param name="reward">已校验且被冻结的奖励定义。</param>
        /// <returns>奖励实际生效时为真。</returns>
        private bool ApplyRogueReward(RogueRewardDefinition reward)
        {
            if (!CanAcceptRogueInput || reward == null || !World.TryGetActor(Player, out CombatActorSnapshot player) || player.Dead) return false;
            if (reward.Kind == RogueRewardKind.AttributeBonus)
            {
                if (!TryMapRogueAttribute(reward.Attribute, out CombatAttributeKind attribute) ||
                    !World.TryAddAttributeBonus(Player, attribute, reward.Amount)) return false;
                Message = "本局强化：" + reward.Title;
                return true;
            }
            if (reward.Kind == RogueRewardKind.CombatAugment) return ApplyCombatAugmentReward(reward);
            if (reward.Kind != RogueRewardKind.RandomLoot) return false;
            ProjectItem item = ProjectEquipmentRules.CreateLoot(rogueLootRandom, LevelId, reward.LootType);
            if (ProjectEquipmentRules.ValidateItem(item) != null) return false;
            inventory.Add(item);
            Message = "获得战利品：" + item.Name;
            return true;
        }

        /// <summary>
        /// 将项目奖励属性转换为战斗层属性，不让战斗依赖局外模型。
        /// </summary>
        /// <param name="attribute">项目属性种类。</param>
        /// <param name="mapped">对应战斗属性。</param>
        /// <returns>存在明确映射时为真。</returns>
        private static bool TryMapRogueAttribute(ProjectAttributeType attribute, out CombatAttributeKind mapped)
        {
            mapped = default;
            switch (attribute)
            {
                case ProjectAttributeType.MaxHealth: mapped = CombatAttributeKind.MaxHealth; break;
                case ProjectAttributeType.Attack: mapped = CombatAttributeKind.Attack; break;
                case ProjectAttributeType.Defense: mapped = CombatAttributeKind.Defense; break;
                case ProjectAttributeType.CriticalRating: mapped = CombatAttributeKind.CriticalRating; break;
                case ProjectAttributeType.CriticalDamage: mapped = CombatAttributeKind.CriticalDamage; break;
                case ProjectAttributeType.DodgeChance: mapped = CombatAttributeKind.DodgeChance; break;
                case ProjectAttributeType.DamageIncrease: mapped = CombatAttributeKind.DamageIncrease; break;
                case ProjectAttributeType.DamageReduction: mapped = CombatAttributeKind.DamageReduction; break;
                default: return false;
            }
            return true;
        }

        /// <summary>
        /// 清空选项前后的残留移动、交互和不足一帧时间。
        /// </summary>
        private void ClearRogueInput()
        {
            touchMove = Vector2.zero;
            interactionRequested = false;
            World.SetMove(Player, Vector2.zero);
            World.ClearPendingTime();
        }

        /// <summary>
        /// 发布本局奖励和 HUD 的只读变化。
        /// </summary>
        private void PublishRogueChange()
        {
            RogueChoiceChanged?.Invoke();
            Changed?.Invoke();
        }

        /// <summary>
        /// 终止当前奖励和未返回的授权，防止旧回调影响新一局。
        /// </summary>
        private void StopRogueChoices()
        {
            rogueChoices.Cancel();
            if (!rogueLifetime.IsCancellationRequested) rogueLifetime.Cancel();
            RogueChoiceChanged?.Invoke();
        }
    }
}
