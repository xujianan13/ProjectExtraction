using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;
using GameLogic.Gameplay.Expedition;

namespace GameLogic.Project
{
    /// <summary>
    /// 局外成长与单局出入的唯一项目会话，持久化确认后才公布交易结果。
    /// </summary>
    public sealed class ProjectSession : IDisposable
    {
        private readonly SaveSession<ProjectSaveData> saves;
        private ProjectSaveData profile;
        private ProjectSaveData pending;
        private Action pendingAccepted;
        private string pendingMessage;
        private PendingFollowUp pendingFollowUp;
        private bool pendingMarked;
        private long pendingRevision;
        private ExpeditionRun preparedRun;
        private ExpeditionRun retiringRun;
        private bool needsHomeReturn;
        private bool saving;
        private bool changingScene;
        private bool disposed;
        private int displayedStamina;
        private readonly ProjectLevelOption[] levels = PrototypeRules.CreateLevels();

        private enum PendingFollowUp { None, EnterBattle, ReturnHome }

        public event Action Changed;
        public int Level => profile.Level;
        public int Experience => profile.Experience;
        public int ExperienceToNextLevel => PrototypeRules.ExperienceRequired(Level);
        public int Stamina => displayedStamina;
        public int StaminaCap => PrototypeRules.StaminaCap;
        public int Gold => profile.Gold;
        public int Diamonds => profile.Diamonds;
        public int SkillPoints => profile.SkillPoints;
        public int PowerRank => profile.PowerRank;
        public int VitalityRank => profile.VitalityRank;
        public int AgilityRank => profile.AgilityRank;
        public string LastRunSummary => pending != null ? "保存未完成，请重试原操作。" :
            needsHomeReturn ? "结算已保存，返回局外失败，请重试。" : profile.LastRunSummary;
        public bool IsBusy => saving || changingScene || pending != null || needsHomeReturn || disposed;
        public bool HasPendingSave => (pending != null || needsHomeReturn) && !saving && !changingScene;
        public ExpeditionRun CurrentRun { get; private set; }
        public IReadOnlyList<ProjectLevelOption> Levels => levels;
        public IReadOnlyList<ProjectItem> Warehouse => profile.Warehouse.ConvertAll(item => item.Copy());
        public IReadOnlyList<ProjectSkill> Skills => new[]
        {
            CreateSkill("power", "锋刃研习", "每级提高 10% 近战伤害", profile.PowerRank),
            CreateSkill("vitality", "生存训练", "每级提高 15 点生命上限", profile.VitalityRank),
            CreateSkill("agility", "轻装步法", "每级提高 5% 移动速度", profile.AgilityRank)
        };

        /// <summary>
        /// 绑定已读取的存档会话，保留独立已提交快照。
        /// </summary>
        /// <param name="saves">项目保存会话。</param>
        /// <param name="data">已经验证的数据。</param>
        private ProjectSession(SaveSession<ProjectSaveData> saves, ProjectSaveData data)
        {
            this.saves = saves;
            profile = data.Copy();
            displayedStamina = profile.Stamina;
        }

        /// <summary>
        /// 打开项目存档；读取错误不会被当成新玩家。
        /// </summary>
        /// <param name="ct">初始化取消令牌。</param>
        /// <returns>成功会话，失败时为空。</returns>
        public static async UniTask<ProjectSession> OpenAsync(CancellationToken ct)
        {
            var definition = new SaveDefinition<ProjectSaveData>(1, ProjectSaveData.CreateDefault, ProjectSaveData.Validate);
            var policy = new SavePolicy { AutoSave = false, FlushOnBackground = false };
            SaveOpenResult<ProjectSaveData> opened = await GameEntry.Get<ISaveModule>().OpenAsync(
                new SaveIdentity("ProjectExtraction", "prototype", "local", "profile"), definition, policy, ct);
            if (!opened.Result.Success || opened.Session == null)
            {
                GameEntry.Get<ILogModule>()?.Error("ProjectSession", opened.Result.Message);
                return null;
            }
            var session = new ProjectSession(opened.Session, opened.Session.LoadResult.Data);
            opened.Session.BindSnapshot(() => (session.pending ?? session.profile).Copy());
            opened.Session.AcceptRecovery();
            SaveResult activation = opened.Session.Activate();
            if (!activation.Success)
            {
                GameEntry.Get<ILogModule>()?.Error("ProjectSession", activation.Message);
                session.Dispose();
                return null;
            }
            if (!string.IsNullOrEmpty(session.profile.ActiveRunId))
            {
                ProjectSaveData recovered = session.Candidate();
                if (ProjectProfileRules.TryRecoverInterruptedRun(recovered, "上次游戏中断"))
                    await session.CommitAsync(recovered, recovered.LastRunSummary);
            }
            session.RefreshClock();
            return session;
        }

        /// <summary>
        /// 更新时间派生显示，不提前改动已提交存档。
        /// </summary>
        public void RefreshClock()
        {
            if (disposed) return;
            ProjectSaveData evaluated = Candidate();
            if (displayedStamina == evaluated.Stamina) return;
            displayedStamina = evaluated.Stamina;
            Changed?.Invoke();
        }

        /// <summary>
        /// 提交物品穿戴或卸下，同类只保留一个装备槽。
        /// </summary>
        /// <param name="itemId">仓库实例身份。</param>
        /// <returns>保存确认后的结果。</returns>
        public UniTask<ProjectOperationResult> EquipAsync(string itemId)
        {
            if (!CanEdit()) return Rejected("当前不能整理装备。");
            ProjectSaveData candidate = Candidate();
            ProjectItem item = candidate.Warehouse.Find(value => value.Id == itemId);
            if (item == null || item.Type == ProjectItemType.Item) return Rejected("请选择可穿戴的武器或装备。");
            if (item.RequiredLevel > candidate.Level) return Rejected("角色等级不足，无法穿戴。");
            bool equip = !item.Equipped;
            foreach (ProjectItem other in candidate.Warehouse)
                if (other.Type == item.Type) other.Equipped = false;
            item.Equipped = equip;
            return CommitAsync(candidate, equip ? "已穿戴。" : "已卸下。");
        }

        /// <summary>
        /// 同一次保存中移除出售物品并增加金币。
        /// </summary>
        /// <param name="itemId">出售实例身份。</param>
        /// <returns>交易结果。</returns>
        public UniTask<ProjectOperationResult> SellAsync(string itemId)
        {
            if (!CanEdit()) return Rejected("当前不能出售。");
            ProjectSaveData candidate = Candidate();
            ProjectItem item = candidate.Warehouse.Find(value => value.Id == itemId);
            if (item == null || item.Equipped) return Rejected("请先卸下要出售的物品。");
            if (candidate.Gold > int.MaxValue - item.SellGold) return Rejected("金币余额已达到上限。");
            candidate.Warehouse.Remove(item);
            candidate.Gold += item.SellGold;
            return CommitAsync(candidate, "出售成功，金币 +" + item.SellGold);
        }

        /// <summary>
        /// 在局外扣除技能点并提升角色技能。
        /// </summary>
        /// <param name="skillId">成长技能身份。</param>
        /// <returns>保存确认后的升级结果。</returns>
        public UniTask<ProjectOperationResult> UpgradeSkillAsync(string skillId)
        {
            if (!CanEdit()) return Rejected("技能只能在局外提升。");
            ProjectSaveData candidate = Candidate();
            int rank = skillId == "power" ? candidate.PowerRank : skillId == "vitality" ? candidate.VitalityRank :
                skillId == "agility" ? candidate.AgilityRank : -1;
            if (rank < 0 || rank >= PrototypeRules.SkillMaxRank) return Rejected("技能不存在或已经满级。");
            int cost = rank + 1;
            if (candidate.SkillPoints < cost) return Rejected("技能点不足。");
            candidate.SkillPoints -= cost;
            if (skillId == "power") candidate.PowerRank++;
            else if (skillId == "vitality") candidate.VitalityRank++;
            else candidate.AgilityRank++;
            return CommitAsync(candidate, "技能升级成功。");
        }

        /// <summary>
        /// 手动领取当前补给时段，测试规则允许补给超过自然恢复上限。
        /// </summary>
        /// <returns>领取结果。</returns>
        public UniTask<ProjectOperationResult> ClaimSupplyAsync()
        {
            if (!CanEdit()) return Rejected("当前不能领取补给。");
            ProjectSaveData candidate = Candidate();
            string key = ProjectProfileRules.SupplyKey(candidate.ClockUtc);
            if (candidate.LastSupplyKey == key) return Rejected("本时段补给已经领取。");
            if (candidate.Stamina > int.MaxValue - PrototypeRules.SupplyAmount) return Rejected("体力数值已达到上限。");
            candidate.LastSupplyKey = key;
            candidate.Stamina += PrototypeRules.SupplyAmount;
            return CommitAsync(candidate, "补给领取成功，体力 +40。");
        }

        /// <summary>
        /// 准备关卡，保存体力与携装变更后进入战斗逻辑场景。
        /// </summary>
        /// <param name="levelId">关卡配置身份。</param>
        /// <returns>是否成功进入关卡。</returns>
        public async UniTask<bool> StartRunAsync(int levelId)
        {
            if (!CanEdit()) return false;
            ProjectLevelOption level = Array.Find(levels, value => value.Id == levelId);
            ProjectSaveData candidate = Candidate();
            if (level == null || candidate.Level < level.RequiredLevel || candidate.Stamina < PrototypeRules.EntryCost) return false;
            string runId = Guid.NewGuid().ToString("N");
            var items = candidate.Warehouse.FindAll(item => item.Equipped);
            if (!ExpeditionRun.TryCreate(this, runId, levelId, items, out ExpeditionRun run)) return false;
            candidate.Stamina -= PrototypeRules.EntryCost;
            candidate.ActiveRunId = runId;
            candidate.ActiveRunEntryCost = PrototypeRules.EntryCost;
            candidate.EntryItems = items;
            candidate.Warehouse.RemoveAll(item => item.Equipped);
            preparedRun = run;
            ProjectOperationResult result = await CommitAsync(candidate, "入场准备完成。", () =>
            {
                CurrentRun = preparedRun;
                preparedRun = null;
            }, PendingFollowUp.EnterBattle);
            if (!result.Success)
            {
                if (pending == null)
                {
                    preparedRun?.Dispose();
                    preparedRun = null;
                }
                return false;
            }
            return await EnterPreparedRunAsync();
        }

        /// <summary>
        /// 将唯一终局结果和完整携出计划一次提交。
        /// </summary>
        /// <param name="extracted">是否正常撤离。</param>
        /// <returns>结算是否保存。</returns>
        public async UniTask<bool> FinishRunAsync(bool extracted)
        {
            ExpeditionRun run = CurrentRun;
            if (run == null || IsBusy || profile.LastSettledRunId == run.RunId) return false;
            run.Stop();
            ProjectSaveData candidate = Candidate();
            if (candidate.ActiveRunId != run.RunId || run.GoldSpent < 0 || run.GoldSpent > candidate.Gold) return false;
            IReadOnlyList<ProjectItem> carried = run.GetRetainedItems(extracted);
            foreach (ProjectItem item in carried) candidate.Warehouse.Add(item.Copy());
            candidate.EntryItems.Clear();
            candidate.ActiveRunId = "";
            candidate.ActiveRunEntryCost = 0;
            candidate.LastSettledRunId = run.RunId;
            candidate.Gold -= run.GoldSpent;
            int experience = extracted || PrototypeRules.KeepKillExperienceOnDeath ? run.KillExperience : 0;
            if (extracted) experience += PrototypeRules.ExtractionExperience;
            ProjectProfileRules.AddExperience(candidate, experience);
            candidate.LastRunSummary = (extracted ? "撤离成功" : "角色死亡") + " · 携回 " + carried.Count + " 件 · 经验 +" + experience;
            ProjectOperationResult result = await CommitAsync(candidate, candidate.LastRunSummary, () =>
            {
                retiringRun = CurrentRun;
                CurrentRun = null;
            }, PendingFollowUp.ReturnHome);
            if (!result.Success) return false;
            return await ReturnHomeAsync();
        }

        /// <summary>
        /// 异常中断时退回本局原入场装备和入场体力，并丢弃未结算收益。
        /// </summary>
        /// <param name="reason">异常原因，仅用于结算说明。</param>
        /// <returns>是否已保存回退并返回局外。</returns>
        public async UniTask<bool> RecoverInterruptedRunAsync(string reason)
        {
            if (disposed || pending != null || saving || changingScene || string.IsNullOrEmpty(profile.ActiveRunId)) return false;
            CurrentRun?.Stop();
            ProjectSaveData candidate = Candidate();
            if (!ProjectProfileRules.TryRecoverInterruptedRun(candidate, reason)) return false;
            ProjectOperationResult result = await CommitAsync(candidate, candidate.LastRunSummary, () =>
            {
                retiringRun = CurrentRun;
                CurrentRun = null;
            }, PendingFollowUp.ReturnHome);
            if (!result.Success) return false;
            return await ReturnHomeAsync();
        }

        /// <summary>
        /// 只重试原候选；标记失败时先获取新修订号，绝不凭旧回执提交候选。
        /// </summary>
        /// <returns>原交易保存或返场结果。</returns>
        public async UniTask<ProjectOperationResult> RetryPendingSaveAsync()
        {
            if (disposed || saving || changingScene) return new ProjectOperationResult(false, "当前不能重试。");
            if (pending == null)
            {
                if (!needsHomeReturn) return new ProjectOperationResult(false, "没有可重试的保存。");
                bool returned = await ReturnHomeAsync();
                return new ProjectOperationResult(returned, returned ? "已返回局外。" : "返回局外失败，请重试。");
            }
            saving = true;
            Changed?.Invoke();
            if (!pendingMarked)
            {
                SaveResult marked = saves.MarkDirty();
                if (!marked.Success)
                {
                    saving = false;
                    GameEntry.Get<ILogModule>()?.Error("ProjectSession", marked.Message);
                    Changed?.Invoke();
                    return new ProjectOperationResult(false, "保存标记失败，请重试：" + marked.Message);
                }
                pendingMarked = true;
                pendingRevision = saves.GetStatus().Revision;
            }
            PendingFollowUp followUp = pendingFollowUp;
            SaveFlushResult receipt;
            try { receipt = await saves.RetryAsync(); }
            catch (Exception error)
            {
                saving = false;
                GameEntry.Get<ILogModule>()?.Error("ProjectSession", "重试保存异常：" + error.Message);
                Changed?.Invoke();
                return new ProjectOperationResult(false, "保存失败，请重试：" + error.Message);
            }
            ProjectOperationResult result = AcceptReceipt(receipt);
            if (!result.Success) return result;
            if (followUp == PendingFollowUp.EnterBattle)
            {
                bool entered = await EnterPreparedRunAsync();
                return new ProjectOperationResult(entered, entered ? result.Message : "入场失败，正在恢复原入场资源。");
            }
            if (followUp == PendingFollowUp.ReturnHome)
            {
                bool returned = await ReturnHomeAsync();
                return new ProjectOperationResult(returned, returned ? result.Message : "结算已保存，返回局外失败，请重试。");
            }
            return result;
        }

        /// <summary>
        /// 释放本次应用会话，框架负责在途物理保存的收束。
        /// </summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            CurrentRun?.Dispose();
            CurrentRun = null;
            preparedRun?.Dispose();
            preparedRun = null;
            retiringRun?.Dispose();
            retiringRun = null;
            saves.Dispose();
            Changed = null;
        }

        /// <summary>
        /// 生成按当前游戏时间结算的交易副本。
        /// </summary>
        /// <returns>独立候选。</returns>
        private ProjectSaveData Candidate()
        {
            ProjectSaveData candidate = profile.Copy();
            ProjectProfileRules.AdvanceClock(candidate, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            return candidate;
        }

        /// <summary>
        /// 判断是否可以开始新的局外事务。
        /// </summary>
        /// <returns>是否处于可编辑状态。</returns>
        private bool CanEdit() => !IsBusy && CurrentRun == null && string.IsNullOrEmpty(profile.ActiveRunId);

        /// <summary>
        /// 原子保存候选，结果不确定时锁住候选等待显式重试。
        /// </summary>
        /// <param name="candidate">交易后的独立快照。</param>
        /// <param name="message">成功提示。</param>
        /// <param name="accepted">保存确认后的内存状态更新。</param>
        /// <param name="followUp">持久化确认后的场景操作。</param>
        /// <returns>持久化确认结果。</returns>
        private async UniTask<ProjectOperationResult> CommitAsync(ProjectSaveData candidate, string message, Action accepted = null,
            PendingFollowUp followUp = PendingFollowUp.None)
        {
            if (disposed || pending != null || saving) return new ProjectOperationResult(false, "上一操作尚未保存。");
            string invalid = ProjectSaveData.Validate(candidate);
            if (invalid != null) return new ProjectOperationResult(false, invalid);
            pending = candidate;
            pendingMessage = message;
            pendingAccepted = accepted;
            pendingFollowUp = followUp;
            pendingMarked = false;
            pendingRevision = 0;
            saving = true;
            Changed?.Invoke();
            SaveResult dirty = saves.MarkDirty();
            if (!dirty.Success)
            {
                saving = false;
                GameEntry.Get<ILogModule>()?.Error("ProjectSession", dirty.Message);
                Changed?.Invoke();
                return new ProjectOperationResult(false, "保存标记失败，请重试：" + dirty.Message);
            }
            pendingMarked = true;
            pendingRevision = saves.GetStatus().Revision;
            try { return AcceptReceipt(await saves.FlushAsync()); }
            catch (Exception error)
            {
                saving = false;
                GameEntry.Get<ILogModule>()?.Error("ProjectSession", "保存异常：" + error.Message);
                Changed?.Invoke();
                return new ProjectOperationResult(false, "保存失败，请重试：" + error.Message);
            }
        }

        /// <summary>
        /// 仅接受本候选修订号的持久化回执，失败时保留原公开状态。
        /// </summary>
        /// <param name="receipt">框架保存回执。</param>
        /// <returns>对应操作结果。</returns>
        private ProjectOperationResult AcceptReceipt(SaveFlushResult receipt)
        {
            saving = false;
            if (disposed) return new ProjectOperationResult(false, "会话已结束。");
            if (pending == null || !ProjectProfileRules.CanAcceptSaveReceipt(pendingMarked, pendingRevision,
                receipt.TargetRevision, receipt.SavedRevision, receipt.Result.Success))
            {
                string reason = receipt.Result.Success ? "保存回执未覆盖本次修订号。" : receipt.Result.Message;
                GameEntry.Get<ILogModule>()?.Warn("ProjectSession", reason);
                Changed?.Invoke();
                return new ProjectOperationResult(false, "保存失败，请重试：" + reason);
            }
            profile = pending;
            pending = null;
            Action accepted = pendingAccepted;
            pendingAccepted = null;
            pendingMarked = false;
            pendingRevision = 0;
            pendingFollowUp = PendingFollowUp.None;
            accepted?.Invoke();
            displayedStamina = profile.Stamina;
            Changed?.Invoke();
            return new ProjectOperationResult(true, pendingMessage);
        }

        /// <summary>
        /// 进入已经持久化入场扣费的关卡，场景异常时恢复入场资源。
        /// </summary>
        /// <returns>是否成功进入战斗场景。</returns>
        private async UniTask<bool> EnterPreparedRunAsync()
        {
            if (CurrentRun == null || disposed) return false;
            changingScene = true;
            bool entered = false;
            try
            {
                await GameEntry.Get<ISceneModule>().ChangeSceneAsync(ProjectServices.BattleSceneName);
                entered = GameEntry.Get<ISceneModule>().CurrentScene?.Name == ProjectServices.BattleSceneName;
            }
            catch (Exception error)
            {
                GameEntry.Get<ILogModule>()?.Error("ProjectSession", "进入关卡失败：" + error.Message);
            }
            finally
            {
                changingScene = false;
                Changed?.Invoke();
            }
            if (!entered) await RecoverInterruptedRunAsync("进入关卡失败");
            return entered;
        }

        /// <summary>
        /// 在结算保存后返回局外；失败保留可重试状态并解除场景切换锁。
        /// </summary>
        /// <returns>是否已进入局外场景。</returns>
        private async UniTask<bool> ReturnHomeAsync()
        {
            if (disposed) return false;
            changingScene = true;
            bool returned = false;
            try
            {
                await GameEntry.Get<ISceneModule>().ChangeSceneAsync(GameLogic.Scaffold.ContentIds.HomeScene);
                returned = GameEntry.Get<ISceneModule>().CurrentScene?.Name == GameLogic.Scaffold.ContentIds.HomeScene;
            }
            catch (Exception error)
            {
                GameEntry.Get<ILogModule>()?.Error("ProjectSession", "返回局外失败：" + error.Message);
            }
            finally
            {
                changingScene = false;
                needsHomeReturn = !returned;
                if (returned)
                {
                    retiringRun?.Dispose();
                    retiringRun = null;
                }
                Changed?.Invoke();
            }
            return returned;
        }

        /// <summary>
        /// 创建技能显示快照。
        /// </summary>
        /// <param name="id">成长技能身份。</param>
        /// <param name="name">显示名称。</param>
        /// <param name="description">升级效果。</param>
        /// <param name="rank">已保存等级。</param>
        /// <returns>技能快照。</returns>
        private static ProjectSkill CreateSkill(string id, string name, string description, int rank) => new()
        {
            Id = id, Name = name, Description = description, Rank = rank, MaxRank = PrototypeRules.SkillMaxRank, NextCost = rank + 1,
            CurrentEffect = SkillEffect(id, rank), NextEffect = rank < PrototypeRules.SkillMaxRank ? SkillEffect(id, rank + 1) : "已满级"
        };

        /// <summary>
        /// 生成当前等级对应的精确效果文字。
        /// </summary>
        /// <param name="id">成长技能身份。</param>
        /// <param name="rank">技能等级。</param>
        /// <returns>效果文字。</returns>
        private static string SkillEffect(string id, int rank) => id == "power" ? "伤害 +" + rank * 10 + "%" :
            id == "vitality" ? "生命上限 +" + rank * 15 : "移动速度 +" + rank * 5 + "%";

        /// <summary>
        /// 返回未发生持久化变化的拒绝结果。
        /// </summary>
        /// <param name="message">拒绝原因。</param>
        /// <returns>已经完成的失败结果。</returns>
        private static UniTask<ProjectOperationResult> Rejected(string message) => UniTask.FromResult(new ProjectOperationResult(false, message));
    }
}
