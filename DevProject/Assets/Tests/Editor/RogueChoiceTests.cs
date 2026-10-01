using System.Linq;
using GameLogic.Gameplay.Roguelike;
using NUnit.Framework;

namespace ProjectExtraction.Tests
{
    /// <summary>
    /// 验证本局强化选择的身份、刷新和授权边界。
    /// </summary>
    public sealed class RogueChoiceTests
    {
        /// <summary>
        /// 默认只显示三个不重复选项，并关闭刷新与第四项。
        /// </summary>
        [Test]
        public void DefaultPolicyOffersThreeChoicesWithoutOptionalActions()
        {
            var session = new RogueChoiceSession("run", 11);
            Assert.IsFalse(session.TryBegin(""));
            Assert.IsTrue(session.TryBegin("room"));
            RogueChoiceSnapshot active = session.Active;
            Assert.AreEqual(3, active.Options.Count);
            Assert.AreEqual(3, active.Options.Select(option => option.Reward.Id).Distinct().Count());
            Assert.IsFalse(active.CanRefresh);
            Assert.IsFalse(active.ExtraUnlocked);
            Assert.IsFalse(session.TryRefresh(active.OfferId, active.Revision));
            Assert.IsFalse(session.TryCreateExtraAccessRequest(active.OfferId, active.Revision, out _));
            Assert.IsFalse(session.TrySelect(active.OfferId, active.Revision, 3, reward => true));
            Assert.IsFalse(session.TryBegin("other"));
        }

        /// <summary>
        /// 发奖失败保留原选择，成功后旧回调和来源都不能再次兑现。
        /// </summary>
        [Test]
        public void FailedApplyCanRetryButSuccessfulApplyIsOnceOnly()
        {
            var session = new RogueChoiceSession("run", 12);
            Assert.IsTrue(session.TryBegin("room"));
            RogueChoiceSnapshot active = session.Active;
            int attempts = 0;
            Assert.IsFalse(session.TrySelect(active.OfferId, active.Revision, 0, reward =>
            {
                attempts++;
                return false;
            }));
            Assert.AreEqual(active.OfferId, session.Active.OfferId);
            Assert.IsTrue(session.TrySelect(active.OfferId, active.Revision, 0, reward =>
            {
                attempts++;
                return true;
            }));
            Assert.AreEqual(2, attempts);
            Assert.IsNull(session.Active);
            Assert.IsFalse(session.TrySelect(active.OfferId, active.Revision, 0, reward =>
            {
                attempts++;
                return true;
            }));
            Assert.AreEqual(2, attempts);
            Assert.IsFalse(session.TryBegin("room"));
            Assert.IsTrue(session.TryBegin("next"));
            session.Cancel();
            Assert.IsFalse(session.TryBegin("next"));
            Assert.IsFalse(session.TryBegin("after_cancel"));
        }

        /// <summary>
        /// 刷新创建新代次，旧选项回调不得发奖，快照集合不可改写。
        /// </summary>
        [Test]
        public void RefreshRejectsOldRevisionAndExhaustsLimit()
        {
            var session = new RogueChoiceSession("run", 13, new RogueChoicePolicy { RefreshEnabled = true });
            Assert.IsTrue(session.TryBegin("room"));
            RogueChoiceSnapshot old = session.Active;
            Assert.IsTrue(old.CanRefresh);
            Assert.IsTrue(session.TryRefresh(old.OfferId, old.Revision));
            Assert.AreEqual(0, old.Revision);
            Assert.AreEqual(3, old.Options.Count);
            Assert.AreEqual(1, session.Active.Revision);
            Assert.IsFalse(session.Active.CanRefresh);
            Assert.IsFalse(session.TryRefresh(old.OfferId, old.Revision));
            Assert.IsFalse(session.TryRefresh(old.OfferId, session.Active.Revision));
            Assert.IsFalse(session.TrySelect(old.OfferId, old.Revision, 0, reward => true));
        }

        /// <summary>
        /// 回执必须对应本次请求；拒绝后可重试，成功后只能授权一次。
        /// </summary>
        [Test]
        public void ExtraAccessRequiresMatchingUniqueReceipt()
        {
            var session = new RogueChoiceSession("run", 14, new RogueChoicePolicy { ExtraChoiceEnabled = true });
            Assert.IsTrue(session.TryBegin("room"));
            RogueChoiceSnapshot active = session.Active;
            Assert.AreEqual(4, active.Options.Count);
            Assert.IsTrue(active.Options[3].IsExtra);
            Assert.IsFalse(session.TrySelect(active.OfferId, active.Revision, 3, reward => true));
            Assert.IsTrue(session.TryCreateExtraAccessRequest(active.OfferId, active.Revision, out RogueChoiceAccessRequest request));
            Assert.IsTrue(session.TryCreateExtraAccessRequest(active.OfferId, active.Revision, out RogueChoiceAccessRequest repeated));
            Assert.AreSame(request, repeated);
            Assert.IsFalse(session.TryGrantExtraAccess(request, Receipt(request, true, "receipt", "wrong")));
            Assert.IsFalse(session.TryGrantExtraAccess(request, null));
            Assert.IsTrue(session.TryCreateExtraAccessRequest(active.OfferId, active.Revision, out request));
            Assert.IsFalse(session.TryGrantExtraAccess(request, Receipt(request, false, "rejected")));
            Assert.IsTrue(session.TryCreateExtraAccessRequest(active.OfferId, active.Revision, out RogueChoiceAccessRequest retry));
            Assert.AreNotEqual(request.RequestId, retry.RequestId);
            Assert.IsFalse(session.TryGrantExtraAccess(request, Receipt(request, true, "old")));
            Assert.IsTrue(session.TryGrantExtraAccess(retry, Receipt(retry, true, "receipt")));
            Assert.IsTrue(session.Active.ExtraUnlocked);
            Assert.AreEqual(4, session.Active.Options.Count);
            Assert.AreEqual(4, session.Active.Options.Select(option => option.Reward.Id).Distinct().Count());
            Assert.IsTrue(session.Active.Options[3].IsExtra);
            Assert.IsFalse(session.TryGrantExtraAccess(retry, Receipt(retry, true, "receipt")));
            Assert.IsFalse(session.TryCreateExtraAccessRequest(active.OfferId, active.Revision, out _));
        }

        /// <summary>
        /// 刷新或取消后，尚未返回的授权回执不得解锁新选择。
        /// </summary>
        [Test]
        public void RefreshAndCancelExpirePendingAccess()
        {
            var session = new RogueChoiceSession("run", 15,
                new RogueChoicePolicy { ExtraChoiceEnabled = true, RefreshEnabled = true });
            Assert.IsTrue(session.TryBegin("room"));
            RogueChoiceSnapshot first = session.Active;
            Assert.IsTrue(session.TryCreateExtraAccessRequest(first.OfferId, first.Revision, out RogueChoiceAccessRequest old));
            Assert.IsTrue(session.TryRefresh(first.OfferId, first.Revision));
            Assert.IsFalse(session.TryGrantExtraAccess(old, Receipt(old, true, "stale")));
            Assert.IsTrue(session.TryCreateExtraAccessRequest(session.Active.OfferId, session.Active.Revision,
                out RogueChoiceAccessRequest current));
            session.Cancel();
            Assert.IsFalse(session.TryGrantExtraAccess(current, Receipt(current, true, "cancelled")));
        }

        /// <summary>
        /// 相同种子和事件顺序给出相同选项，其他随机流不参与。
        /// </summary>
        [Test]
        public void SameSeedAndEventSequenceProduceSameChoices()
        {
            var first = new RogueChoiceSession("a", 1234, new RogueChoicePolicy { RefreshEnabled = true });
            var second = new RogueChoiceSession("b", 1234, new RogueChoicePolicy { RefreshEnabled = true });
            Assert.IsTrue(first.TryBegin("one"));
            Assert.IsTrue(second.TryBegin("one"));
            CollectionAssert.AreEqual(first.Active.Options.Select(option => option.Reward.Id).ToArray(),
                second.Active.Options.Select(option => option.Reward.Id).ToArray());
            Assert.IsTrue(first.TryRefresh(first.Active.OfferId, first.Active.Revision));
            Assert.IsTrue(second.TryRefresh(second.Active.OfferId, second.Active.Revision));
            CollectionAssert.AreEqual(first.Active.Options.Select(option => option.Reward.Id).ToArray(),
                second.Active.Options.Select(option => option.Reward.Id).ToArray());
        }

        /// <summary>
        /// 发奖回调不能重入选择、刷新或签发额外授权。
        /// </summary>
        [Test]
        public void ApplyingRewardRejectsReentrantCommands()
        {
            var session = new RogueChoiceSession("run", 16,
                new RogueChoicePolicy { RefreshEnabled = true, ExtraChoiceEnabled = true });
            Assert.IsTrue(session.TryBegin("room"));
            RogueChoiceSnapshot offer = session.Active;
            int grants = 0;
            Assert.IsTrue(session.TrySelect(offer.OfferId, offer.Revision, 0, reward =>
            {
                grants++;
                Assert.IsFalse(session.TrySelect(offer.OfferId, offer.Revision, 1, nested => true));
                Assert.IsFalse(session.TryRefresh(offer.OfferId, offer.Revision));
                Assert.IsFalse(session.TryCreateExtraAccessRequest(offer.OfferId, offer.Revision, out _));
                return true;
            }));
            Assert.AreEqual(1, grants);
            Assert.IsFalse(session.HasPending);
        }

        /// <summary>
        /// 创建外部提供者格式的测试回执。
        /// </summary>
        /// <param name="request">当前授权请求。</param>
        /// <param name="granted">是否授权。</param>
        /// <param name="receiptId">回执身份。</param>
        /// <param name="requestId">可选的覆盖请求身份。</param>
        /// <returns>用于测试的不可变回执。</returns>
        private static RogueChoiceAccessReceipt Receipt(RogueChoiceAccessRequest request, bool granted, string receiptId,
            string requestId = null) => new(request.RunId, request.OfferId, request.Revision, requestId ?? request.RequestId, granted, receiptId);
    }
}
