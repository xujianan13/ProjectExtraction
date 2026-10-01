using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;
using GameLogic.Gameplay.Expedition;
using GameLogic.Tables;

namespace GameLogic.Project
{
    /// <summary>
    /// 局外状态与单局事务的唯一存档入口，持久化确认后才公布交易结果。
    /// </summary>
    public sealed class ProjectSession : IDisposable
    {
        private readonly SaveSession<ProjectSaveData> saves;
        private ProjectSaveData profile;
        private ProjectSaveData pending;
        private Action pendingAccepted;
        private string pendingMessage;
        private bool pendingMarked;
        private long pendingRevision;
        private bool saving;
        private bool disposed;
        private bool homeEditable;
        private int displayedStamina;
        private readonly ProjectLevelOption[] levels = PrototypeRules.CreateLevels();


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

        /// <summary>
        /// 按已保存的穿戴与技能成长计算独立角色属性。
        /// </summary>
        public ProjectCharacterStats CharacterStats => ProjectEquipmentRules.Calculate(profile.Warehouse, profile.PowerRank, profile.VitalityRank, profile.AgilityRank);
        public string LastRunSummary => pending != null ? "保存未完成，请重试原操作。" : profile.LastRunSummary;
        public bool IsBusy => saving || pending != null || !homeEditable || disposed;
        public bool HasPendingSave => pending != null && !saving && !disposed;
        public ExpeditionRun CurrentRun { get; private set; }

        /// <summary>
        /// 当前持久化单局身份。
        /// </summary>
        public string ActiveRunId => profile.ActiveRunId;

        /// <summary>
        /// 当前持久化单局阶段。
        /// </summary>
        public ProjectRunStage RunStage => profile.Stage;

        /// <summary>
        /// 已记录的终局副本，不暴露存档中的可写集合。
        /// </summary>
        public ProjectRunOutcome RecordedOutcome => profile.Outcome?.Copy();

        /// <summary>
        /// 最近已保存结果的独立副本。
        /// </summary>
        public ProjectRunResult LastResult => profile.LastResult?.Copy();

        public IReadOnlyList<ProjectLevelOption> Levels => levels;
        public IReadOnlyList<ProjectItem> Warehouse => profile.Warehouse.ConvertAll(item => item.Copy());
        public IReadOnlyList<ProjectSkill> Skills => new[]
        {
            CreateSkill("power", profile.PowerRank),
            CreateSkill("vitality", profile.VitalityRank),
            CreateSkill("agility", profile.AgilityRank)
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
            var definition = new SaveDefinition<ProjectSaveData>(3, ProjectSaveData.CreateDefault, ProjectSaveData.Validate,
                codec: new ProjectSaveCodec(), migrations: new[] { ProjectRunRecordRules.CreateMigration(), ProjectEquipmentRules.CreateEquipmentMigration() });
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
                if (other.Type == item.Type || ProjectEquipmentRules.IsArmor(other.Type) && ProjectEquipmentRules.IsArmor(item.Type)) other.Equipped = false;
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
            CfgSkillsRow skill = ProjectRulesConfig.Skill(skillId);
            if (rank < 0 || skill == null || rank >= skill.MaxRank) return Rejected("技能不存在或已经满级。");
            int cost = skill.CostBase + rank * skill.CostPerRank;
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
            return CommitAsync(candidate, "补给领取成功，体力 +" + PrototypeRules.SupplyAmount + "。");
        }

        /// <summary>
        /// 由协调器设置局外变更门禁。
        /// </summary>
        /// <param name="editable">是否允许新局外事务。</param>
        internal void SetHomeEditable(bool editable)
        {
            homeEditable = editable;
            Changed?.Invoke();
        }

        /// <summary>
        /// 绑定或解除当前运行对象；资源释放归单局会话。
        /// </summary>
        /// <param name="run">当前运行对象，退出后传空。</param>
        internal void BindRun(ExpeditionRun run) => CurrentRun = run;

        /// <summary>
        /// 按最新存档校验并生成尚未激活的单局。
        /// </summary>
        /// <param name="runId">框架生成的唯一单局身份。</param>
        /// <param name="levelId">选定关卡。</param>
        /// <param name="run">准备成功的单局。</param>
        /// <param name="error">失败原因。</param>
        /// <returns>是否准备成功。</returns>
        internal bool TryPrepareRun(string runId, int levelId, out ExpeditionRun run, out string error)
        {
            run = null;
            error = "当前单局或存档事务尚未结束。";
            if (disposed || saving || pending != null || CurrentRun != null || !string.IsNullOrEmpty(profile.ActiveRunId)) return false;
            ProjectSaveData candidate = Candidate();
            ProjectLevelOption level = Array.Find(levels, value => value.Id == levelId);
            error = "关卡未解锁。";
            if (level == null || candidate.Level < level.RequiredLevel) return false;
            error = "体力不足，需要 " + PrototypeRules.EntryCost + " 点体力。";
            if (candidate.Stamina < PrototypeRules.EntryCost) return false;
            var items = candidate.Warehouse.FindAll(item => item.Equipped);
            error = "携入装备等级不符合要求。";
            if (items.Exists(item => item.RequiredLevel > candidate.Level)) return false;
            error = "关卡配置或地图准备失败。";
            if (!ExpeditionRun.TryCreate(this, runId, levelId, items, out run)) return false;
            error = null;
            return true;
        }

        /// <summary>
        /// 一次保存扣体力、携入托管和单局身份。
        /// </summary>
        /// <param name="run">已经准备但未激活的单局。</param>
        /// <returns>入场持久化结果。</returns>
        internal UniTask<ProjectOperationResult> AdmitAsync(ExpeditionRun run)
        {
            if (run == null) return Rejected("缺少单局上下文。");
            ProjectSaveData candidate = Candidate();
            if (!ProjectRunRecordRules.TryAdmit(candidate, run.RunId, run.LevelId, run.Maps.Seed,
                candidate.Warehouse.FindAll(item => item.Equipped), out string error)) return Rejected(error);
            return CommitAsync(candidate, "入场记录已保存。");
        }

        /// <summary>
        /// 将开始授权持久化，保存成功前不得运行模拟。
        /// </summary>
        /// <param name="runId">当前单局身份。</param>
        /// <returns>授权保存结果。</returns>
        internal UniTask<ProjectOperationResult> MarkRunningAsync(string runId)
        {
            ProjectSaveData candidate = Candidate();
            if (!ProjectRunRecordRules.TryMarkRunning(candidate, runId, out string error)) return Rejected(error);
            return CommitAsync(candidate, "关卡已准备完成。");
        }

        /// <summary>
        /// 保存冻结的终局事实，本阶段不发放奖励。
        /// </summary>
        /// <param name="outcome">不可再变化的终局快照。</param>
        /// <returns>终局记录保存结果。</returns>
        internal UniTask<ProjectOperationResult> RecordOutcomeAsync(ProjectRunOutcome outcome)
        {
            ProjectSaveData candidate = Candidate();
            if (!ProjectRunRecordRules.TryRecordOutcome(candidate, outcome, out string error)) return Rejected(error);
            return CommitAsync(candidate, "终局记录已保存。");
        }

        /// <summary>
        /// 根据已持久化终局统一结算；只重试属于此结果的候选。
        /// </summary>
        /// <param name="runId">需要结算的稳定单局身份。</param>
        /// <returns>结算持久化结果。</returns>
        internal UniTask<ProjectOperationResult> SettleOutcomeAsync(string runId)
        {
            if (string.IsNullOrEmpty(runId)) return Rejected("结算身份无效。");
            if (pending != null)
            {
                if (pending.LastSettledRunId != runId || pending.LastResult == null) return Rejected("存在其他未完成保存。");
                return RetryPendingSaveAsync();
            }
            if (profile.LastSettledRunId == runId && profile.LastResult != null)
                return UniTask.FromResult(new ProjectOperationResult(true, profile.LastRunSummary));
            if (profile.ActiveRunId != runId) return Rejected("结算不是当前单局。");
            ProjectSaveData candidate = Candidate();
            if (!ProjectRunRecordRules.TrySettleOutcome(candidate, out string error)) return Rejected(error);
            return CommitAsync(candidate, candidate.LastRunSummary);
        }

        /// <summary>
        /// 按开发期策略补偿未记录终局的入场资源。
        /// </summary>
        /// <param name="reason">中断原因。</param>
        /// <returns>补偿保存结果。</returns>
        internal UniTask<ProjectOperationResult> CompensateEntryAsync(string reason)
        {
            ProjectSaveData candidate = Candidate();
            if (!ProjectProfileRules.TryRecoverInterruptedRun(candidate, reason)) return Rejected("该单局不能按中断退回。");
            return CommitAsync(candidate, candidate.LastRunSummary);
        }

        /// <summary>
        /// 单独确认结果已查看，不重复执行结算。
        /// </summary>
        /// <param name="runId">结果所属单局。</param>
        /// <returns>查看标记保存结果。</returns>
        internal UniTask<ProjectOperationResult> AcknowledgeResultAsync(string runId)
        {
            ProjectSaveData candidate = Candidate();
            if (!ProjectRunRecordRules.TryAcknowledgeResult(candidate, runId, out string error)) return Rejected(error);
            return CommitAsync(candidate, "已返回主界面。");
        }

        /// <summary>
        /// 只重试原候选；标记失败时先获取新修订号，绝不凭旧回执提交候选。
        /// </summary>
        /// <returns>原交易保存或返场结果。</returns>
        public async UniTask<ProjectOperationResult> RetryPendingSaveAsync()
        {
            if (disposed || saving || pending == null) return new ProjectOperationResult(false, "没有可重试的保存。");
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
            SaveFlushResult receipt;
            try { receipt = await saves.RetryAsync(); }
            catch (Exception error)
            {
                saving = false;
                GameEntry.Get<ILogModule>()?.Error("ProjectSession", "重试保存异常：" + error.Message);
                Changed?.Invoke();
                return new ProjectOperationResult(false, "保存失败，请重试：" + error.Message);
            }
            return AcceptReceipt(receipt);
        }

        /// <summary>
        /// 释放本次应用会话，框架负责在途物理保存的收束。
        /// </summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            CurrentRun = null;
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
        /// <returns>持久化确认结果。</returns>
        private async UniTask<ProjectOperationResult> CommitAsync(ProjectSaveData candidate, string message, Action accepted = null)
        {
            if (disposed || pending != null || saving) return new ProjectOperationResult(false, "上一操作尚未保存。");
            string invalid = ProjectSaveData.Validate(candidate);
            if (invalid != null) return new ProjectOperationResult(false, invalid);
            pending = candidate;
            pendingMessage = message;
            pendingAccepted = accepted;
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
            accepted?.Invoke();
            displayedStamina = profile.Stamina;
            Changed?.Invoke();
            return new ProjectOperationResult(true, pendingMessage);
        }

        /// <summary>
        /// 创建技能显示快照。
        /// </summary>
        /// <param name="id">成长技能身份。</param>
        /// <param name="rank">已保存等级。</param>
        /// <returns>技能快照。</returns>
        private static ProjectSkill CreateSkill(string id, int rank)
        {
            CfgSkillsRow skill = ProjectRulesConfig.Skill(id);
            return new ProjectSkill
            {
                Id = id, Name = skill.Name, Description = skill.Description, Rank = rank,
                MaxRank = skill.MaxRank, NextCost = skill.CostBase + rank * skill.CostPerRank,
                CurrentEffect = SkillEffect(skill, rank),
                NextEffect = rank < skill.MaxRank ? SkillEffect(skill, rank + 1) : "已满级"
            };
        }

        /// <summary>
        /// 生成当前等级对应的精确效果文字。
        /// </summary>
        /// <param name="skill">成长技能配置。</param>
        /// <param name="rank">技能等级。</param>
        /// <returns>效果文字。</returns>
        private static string SkillEffect(CfgSkillsRow skill, int rank) =>
            skill.DisplayPrefix + (rank * skill.EffectPerRank * skill.DisplayMultiplier).ToString("0.###") + skill.DisplaySuffix;

        /// <summary>
        /// 返回未发生持久化变化的拒绝结果。
        /// </summary>
        /// <param name="message">拒绝原因。</param>
        /// <returns>已经完成的失败结果。</returns>
        private static UniTask<ProjectOperationResult> Rejected(string message) => UniTask.FromResult(new ProjectOperationResult(false, message));
    }
}
