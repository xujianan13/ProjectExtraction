using System;
using System.Collections.Generic;
using GameLogic.Project;
using GameLogic.Tables;

namespace GameLogic.Gameplay.Roguelike
{
    public sealed partial class RogueChoiceSession
    {
        /// <summary>
        /// 冻结候选、已选等级、来源、授权及随机状态，供统一存档入口提交。
        /// </summary>
        /// <returns>与会话内部集合隔离的完整保存状态。</returns>
        public RogueChoiceState CaptureState()
        {
            var state = new RogueChoiceState
            {
                RunId = runId, State = random.State, Policy = policy.Copy(), OfferId = offerId, SourceId = sourceId,
                Revision = revision, RefreshCount = refreshCount, OfferSerial = offerSerial, ExtraUnlocked = extraUnlocked,
                Cancelled = cancelled, PendingRequestId = pendingRequest?.RequestId,
                UsedSources = new List<string>(usedSources), UsedReceipts = new List<string>(usedReceipts)
            };
            state.UsedSources.Sort(StringComparer.Ordinal);
            state.UsedReceipts.Sort(StringComparer.Ordinal);
            foreach (CfgRogueRewardsRow row in rewards) state.PoolIds.Add(row.ID);
            foreach (RogueRewardDefinition reward in selectedRewards.Values) state.SelectedRewards.Add(RogueRewardState.FromReward(reward));
            state.SelectedRewards.Sort((left, right) => string.CompareOrdinal(left.Id, right.Id));
            foreach (KeyValuePair<string, string> group in selectedGroups)
                state.SelectedGroups.Add(new RogueSelectedGroupState { GroupId = group.Key, RewardId = group.Value });
            state.SelectedGroups.Sort((left, right) => string.CompareOrdinal(left.GroupId, right.GroupId));
            foreach (RogueChoiceOption option in options) state.Options.Add(RogueRewardState.FromReward(option.Reward));
            return state;
        }

        /// <summary>
        /// 校验完整状态后原子恢复会话，失败不改变现有选择或随机位置。
        /// </summary>
        /// <param name="state">已经持久确认的完整选择状态。</param>
        /// <param name="error">恢复失败的明确原因。</param>
        /// <returns>所有身份、候选、等级和随机状态有效时为真。</returns>
        public bool TryRestore(RogueChoiceState state, out string error)
        {
            error = configurationError;
            if (configurationError != null) return false;
            error = "强化选择保存状态无效。";
            if (applying || state == null || state.Version != 1 || state.RunId != runId || !ValidPolicy(state.Policy) ||
                state.OfferSerial < 0 || state.Revision < 0 || state.RefreshCount < 0 || state.Revision != state.RefreshCount ||
                state.RefreshCount > state.Policy.MaxRefreshes || state.UsedSources == null || state.UsedReceipts == null ||
                state.SelectedRewards == null || state.SelectedGroups == null || state.Options == null || state.PoolIds == null ||
                state.PoolIds.Count != rewards.Count ||
                !PersistentRandom.TryRestore(state.State, out PersistentRandom restoredRandom)) return false;
            for (int index = 0; index < rewards.Count; index++)
                if (state.PoolIds[index] != rewards[index].ID) return false;
            var restoredSources = new HashSet<string>(StringComparer.Ordinal);
            var restoredReceipts = new HashSet<string>(StringComparer.Ordinal);
            foreach (string source in state.UsedSources)
                if (string.IsNullOrWhiteSpace(source) || !restoredSources.Add(source)) return false;
            foreach (string receipt in state.UsedReceipts)
                if (string.IsNullOrWhiteSpace(receipt) || !restoredReceipts.Add(receipt)) return false;
            if (state.OfferSerial < restoredSources.Count) return false;
            var restoredRanks = new Dictionary<string, int>(StringComparer.Ordinal);
            var restoredGroups = new Dictionary<string, string>(StringComparer.Ordinal);
            long selectedCount = 0;
            foreach (RogueRewardState reward in state.SelectedRewards)
            {
                if (!ValidSavedReward(reward) || restoredRanks.ContainsKey(reward.Id)) return false;
                selectedCount += reward.Rank;
                restoredRanks.Add(reward.Id, reward.Rank);
                string groupId = rewardsById[reward.Id].ExclusiveGroup;
                if (string.IsNullOrEmpty(groupId)) continue;
                if (restoredGroups.ContainsKey(groupId)) return false;
                restoredGroups.Add(groupId, reward.Id);
            }
            foreach (RogueRewardState reward in state.SelectedRewards)
            {
                CfgRogueRewardsRow row = rewardsById[reward.Id];
                if (!string.IsNullOrEmpty(row.PrerequisiteID) &&
                    (!restoredRanks.TryGetValue(row.PrerequisiteID, out int rank) || rank < row.PrerequisiteRank)) return false;
            }
            if (state.SelectedGroups.Count != restoredGroups.Count) return false;
            var groupIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (RogueSelectedGroupState group in state.SelectedGroups)
                if (group == null || string.IsNullOrWhiteSpace(group.GroupId) || !groupIds.Add(group.GroupId) ||
                    !restoredGroups.TryGetValue(group.GroupId, out string rewardId) || rewardId != group.RewardId) return false;
            bool active = !string.IsNullOrEmpty(state.OfferId);
            if (selectedCount > restoredSources.Count - (active ? 1 : 0)) return false;
            if (active)
            {
                if (state.Cancelled || state.OfferSerial < 1 || state.OfferId != runId + ":" + state.OfferSerial ||
                    string.IsNullOrWhiteSpace(state.SourceId) || !restoredSources.Contains(state.SourceId) ||
                    state.Options.Count < 1 || state.Options.Count > (state.Policy.ExtraChoiceEnabled ? 4 : 3)) return false;
                var optionIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (RogueRewardState option in state.Options)
                {
                    if (!ValidSavedReward(option) || !optionIds.Add(option.Id)) return false;
                    CfgRogueRewardsRow row = rewardsById[option.Id];
                    int rank = restoredRanks.TryGetValue(option.Id, out int current) ? current : 0;
                    if (option.Rank != rank + 1 || !Eligible(row, restoredRanks, restoredGroups)) return false;
                }
            }
            else if (!string.IsNullOrEmpty(state.SourceId) || state.Options.Count != 0 || state.ExtraUnlocked ||
                !string.IsNullOrEmpty(state.PendingRequestId)) return false;
            if (state.ExtraUnlocked && (!state.Policy.ExtraChoiceEnabled || state.Options.Count != 4)) return false;
            if (!string.IsNullOrEmpty(state.PendingRequestId) && (!active || !state.Policy.ExtraChoiceEnabled ||
                state.ExtraUnlocked || state.Options.Count != 4 || string.IsNullOrWhiteSpace(state.PendingRequestId))) return false;
            if (!state.Policy.RefreshEnabled && state.RefreshCount != 0) return false;

            random = restoredRandom;
            policy = state.Policy.Copy();
            usedSources.Clear();
            usedSources.UnionWith(restoredSources);
            usedReceipts.Clear();
            usedReceipts.UnionWith(restoredReceipts);
            selectedRanks.Clear();
            selectedGroups.Clear();
            selectedRewards.Clear();
            upgrades.Clear();
            foreach (KeyValuePair<string, int> rank in restoredRanks) selectedRanks.Add(rank.Key, rank.Value);
            foreach (KeyValuePair<string, string> group in restoredGroups) selectedGroups.Add(group.Key, group.Value);
            foreach (RogueRewardState reward in state.SelectedRewards)
            {
                RogueRewardDefinition definition = reward.ToReward();
                selectedRewards.Add(reward.Id, definition);
                upgrades.Add(reward.Id, new RogueUpgradeSnapshot(definition));
            }
            offerId = active ? state.OfferId : null;
            sourceId = active ? state.SourceId : null;
            revision = state.Revision;
            refreshCount = state.RefreshCount;
            offerSerial = state.OfferSerial;
            extraUnlocked = state.ExtraUnlocked;
            cancelled = state.Cancelled;
            options.Clear();
            for (int index = 0; index < state.Options.Count; index++)
                options.Add(new RogueChoiceOption(index, index == 3, state.Options[index].ToReward()));
            pendingRequest = string.IsNullOrEmpty(state.PendingRequestId) ? null :
                new RogueChoiceAccessRequest(runId, offerId, revision, state.PendingRequestId);
            error = null;
            return true;
        }

        /// <summary>
        /// 冻结调用方提供的合法技能配置。
        /// </summary>
        /// <param name="rows">调用方提供的技能配置。</param>
        /// <returns>配置错误信息，通过时为空。</returns>
        private string FreezeRewards(IReadOnlyList<CfgRogueRewardsRow> rows)
        {
            if (string.IsNullOrWhiteSpace(runId) || rows == null) return "强化选择缺少单局身份或技能配置。";
            foreach (CfgRogueRewardsRow row in rows)
            {
                if (row == null) return "强化技能池包含空配置。";
                if (string.IsNullOrWhiteSpace(row.ID) || string.IsNullOrWhiteSpace(row.Title) || rewardsById.ContainsKey(row.ID) ||
                    row.Kind != (int)RogueRewardKind.AttributeBonus && row.Kind != (int)RogueRewardKind.CombatAugment ||
                    row.Weight < 1 || row.MaxRank < 0 || row.PrerequisiteRank < 0 ||
                    string.IsNullOrEmpty(row.PrerequisiteID) != (row.PrerequisiteRank == 0) ||
                    double.IsNaN(row.Amount) || double.IsInfinity(row.Amount) || row.Amount <= 0 || row.Amount > float.MaxValue ||
                    row.Kind == (int)RogueRewardKind.AttributeBonus && !Enum.IsDefined(typeof(ProjectAttributeType), row.Attribute) ||
                    row.Kind == (int)RogueRewardKind.CombatAugment && (row.MaxRank < 1 || string.IsNullOrWhiteSpace(row.AugmentID)))
                    return "强化技能配置无效：" + row.ID;
                var copy = new CfgRogueRewardsRow
                {
                    ID = row.ID, Title = row.Title, Description = row.Description ?? string.Empty, Kind = row.Kind, Attribute = row.Attribute,
                    Amount = row.Amount, Weight = row.Weight, AugmentID = row.AugmentID ?? string.Empty,
                    MaxRank = row.MaxRank, PrerequisiteID = row.PrerequisiteID ?? string.Empty, PrerequisiteRank = row.PrerequisiteRank,
                    ExclusiveGroup = row.ExclusiveGroup ?? string.Empty
                };
                rewards.Add(copy);
                rewardsById.Add(copy.ID, copy);
            }
            long weight = 0;
            foreach (CfgRogueRewardsRow row in rewards)
            {
                weight += row.Weight;
                var visited = new HashSet<string> { row.ID };
                var groups = new HashSet<string>(StringComparer.Ordinal);
                if (!string.IsNullOrEmpty(row.ExclusiveGroup)) groups.Add(row.ExclusiveGroup);
                CfgRogueRewardsRow current = row;
                while (!string.IsNullOrEmpty(current.PrerequisiteID))
                {
                    if (!rewardsById.TryGetValue(current.PrerequisiteID, out CfgRogueRewardsRow prior) || !visited.Add(prior.ID) ||
                        prior.MaxRank > 0 && current.PrerequisiteRank > prior.MaxRank ||
                        !string.IsNullOrEmpty(prior.ExclusiveGroup) && !groups.Add(prior.ExclusiveGroup))
                        return "强化技能前置缺失、循环或互斥导致不可达：" + row.ID;
                    current = prior;
                }
            }
            return weight > int.MaxValue / 16 ? "强化候选总权重越界。" : null;
        }

        /// <summary>
        /// 获取当前等级、前置和互斥允许的候选。
        /// </summary>
        /// <returns>按配置顺序排列的合法候选。</returns>
        private List<CfgRogueRewardsRow> EligibleRewards()
        {
            var pool = new List<CfgRogueRewardsRow>();
            foreach (CfgRogueRewardsRow row in rewards)
                if (Eligible(row, selectedRanks, selectedGroups)) pool.Add(row);
            return pool;
        }

        /// <summary>
        /// 根据给定等级与互斥状态判断候选资格。
        /// </summary>
        /// <param name="row">候选配置。</param>
        /// <param name="ranks">已选等级。</param>
        /// <param name="groups">已选互斥组。</param>
        /// <returns>候选可以继续强化时为真。</returns>
        private static bool Eligible(CfgRogueRewardsRow row, Dictionary<string, int> ranks, Dictionary<string, string> groups)
        {
            int rank = ranks.TryGetValue(row.ID, out int current) ? current : 0;
            return rank < int.MaxValue && (row.MaxRank == 0 || rank < row.MaxRank) &&
                (string.IsNullOrEmpty(row.PrerequisiteID) || ranks.TryGetValue(row.PrerequisiteID, out int prior) && prior >= row.PrerequisiteRank) &&
                (string.IsNullOrEmpty(row.ExclusiveGroup) || !groups.TryGetValue(row.ExclusiveGroup, out string chosen) || chosen == row.ID);
        }

        /// <summary>
        /// 检查保存候选与本关冻结配置完全对应。
        /// </summary>
        /// <param name="reward">保存的完整候选。</param>
        /// <returns>身份、数值和等级均有效时为真。</returns>
        private bool ValidSavedReward(RogueRewardState reward)
        {
            if (reward == null || string.IsNullOrWhiteSpace(reward.Id) || !rewardsById.TryGetValue(reward.Id, out CfgRogueRewardsRow row) ||
                reward.Rank < 1 || row.MaxRank > 0 && reward.Rank > row.MaxRank) return false;
            return reward.Title == row.Title && reward.Description == row.Description && reward.Kind == (RogueRewardKind)row.Kind &&
                reward.Attribute == (ProjectAttributeType)row.Attribute && reward.Amount == (float)row.Amount &&
                reward.AugmentId == row.AugmentID && reward.MaxRank == row.MaxRank;
        }

        /// <summary>
        /// 将冻结配置与本次等级合成为不可变候选。
        /// </summary>
        /// <param name="row">冻结的配置行。</param>
        /// <param name="rank">本次强化后的等级。</param>
        /// <returns>可以展示或保存的候选。</returns>
        private static RogueRewardDefinition CreateReward(CfgRogueRewardsRow row, int rank) => new(row.ID, row.Title, row.Description,
            (RogueRewardKind)row.Kind, (ProjectAttributeType)row.Attribute, (float)row.Amount,
            row.AugmentID, rank, row.MaxRank);

        /// <summary>
        /// 校验会话策略可以安全参与候选抽取。
        /// </summary>
        /// <param name="value">会话功能与权重策略。</param>
        /// <returns>刷新次数和倍率有效时为真。</returns>
        private static bool ValidPolicy(RogueChoicePolicy value) => value != null && value.MaxRefreshes >= 0 &&
            value.SelectedUpgradeWeightMultiplier >= 1 && value.SelectedUpgradeWeightMultiplier <= 16 &&
            value.UnlockedComboWeightMultiplier >= 1 && value.UnlockedComboWeightMultiplier <= 16;
    }
}
