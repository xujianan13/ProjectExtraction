using System.Collections.Generic;
using Cysharp.Threading.Tasks;

namespace GameLogic.Project
{
    /// <summary>
    /// 局外商店与项目会话的事务接入。
    /// </summary>
    public sealed partial class ProjectSession
    {
        /// <summary>
        /// 已提交存档对应的局外商店报价快照。
        /// </summary>
        public IReadOnlyList<ProjectHomeShopOffer> HomeShopOffers => ProjectHomeShopRules.CreateOffers(profile, CanEdit());

        /// <summary>
        /// 按报价修订号购买商品，保存成功后才公布金币、物品和库存变化。
        /// </summary>
        /// <param name="offerId">商品身份。</param>
        /// <param name="expectedRevision">用户看到报价时的购买修订号。</param>
        /// <returns>持久化确认后的购买结果。</returns>
        public UniTask<ProjectOperationResult> BuyHomeShopItemAsync(string offerId, long expectedRevision)
        {
            if (!CanEdit()) return Rejected("当前不能购买。");
            ProjectSaveData candidate = Candidate();
            if (!ProjectHomeShopRules.TryPurchase(candidate, offerId, expectedRevision, out string error)) return Rejected(error);
            return CommitAsync(candidate, "购买成功，物品已进入仓库。");
        }
    }
}
