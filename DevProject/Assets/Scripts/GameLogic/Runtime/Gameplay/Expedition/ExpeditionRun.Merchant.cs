using System.Collections.Generic;
using GameLogic.Gameplay.Combat;
using GameLogic.Gameplay.Level;
using GameLogic.Project;

namespace GameLogic.Gameplay.Expedition
{
    /// <summary>
    /// NPC已经冻结的商品快照，界面读取不共享物品实例。
    /// </summary>
    public sealed class ExpeditionMerchantOffer
    {
        private readonly ProjectItem item;
        public string Id { get; }
        public ProjectItem Item => item.Copy();
        public int Price { get; }
        public bool Sold { get; }

        /// <summary>
        /// 从权威库存创建独立商品视图。
        /// </summary>
        /// <param name="id">稳定商品身份。</param>
        /// <param name="item">已冻结物品。</param>
        /// <param name="price">金币售价。</param>
        /// <param name="sold">是否已出售。</param>
        internal ExpeditionMerchantOffer(string id, ProjectItem item, int price, bool sold)
        { Id = id; this.item = item.Copy(); Price = price; Sold = sold; }
    }

    public sealed partial class ExpeditionRun
    {
        private string activeNpcId;

        /// <summary>
        /// 当前明确访问的NPC库存，包含已售出状态。
        /// </summary>
        public IReadOnlyList<ExpeditionMerchantOffer> MerchantOffers
        {
            get
            {
                var result = new List<ExpeditionMerchantOffer>();
                RegionNpcPlan npc = CurrentRegion?.Plan.Npcs.Find(value => value.Id == activeNpcId);
                if (npc == null) return result;
                RegionNpcState state = CurrentRegion.Npcs.Find(value => value.Id == activeNpcId);
                foreach (RegionNpcOfferPlan offer in npc.Offers)
                    result.Add(new ExpeditionMerchantOffer(offer.Id, offer.Item, offer.Price, state.SoldOfferIds.Contains(offer.Id)));
                return result;
            }
        }

        /// <summary>
        /// 从附近选中的NPC打开库存，不重新生成商品。
        /// </summary>
        /// <returns>已提交访问时为真。</returns>
        public bool TryOpenMerchant() => selectedInteraction.Kind == InteractionTargetKind.Merchant && TryOpenMerchant(selectedInteraction.Id);

        /// <summary>
        /// 保存访问事实后打开具体NPC的商品面板。
        /// </summary>
        /// <param name="npcId">区域NPC稳定身份。</param>
        /// <returns>访问合法且已进入保存时为真。</returns>
        private bool TryOpenMerchant(string npcId)
        {
            if (!CanInteract || !CanManagePanel || !World.TryGetActor(Player, out CombatActorSnapshot player)) return false;
            RegionNpcPlan npc = CurrentRegion.Plan.Npcs.Find(value => value.Id == npcId);
            float radius = CurrentRegion.Plan.InteractionRadius;
            if (npc == null || (player.Position - npc.Position).sqrMagnitude > radius * radius) return false;
            activeNpcId = npcId;
            CurrentRegion.TryVisitNpc(npcId);
            SaveMutation("商人库存已就绪。", () => SetActivePanel(ExpeditionPanelKind.Merchant));
            return true;
        }

        /// <summary>
        /// 将固定商品、金币支出和售出标记纳入同一保存候选。
        /// </summary>
        /// <param name="offerId">当前NPC的商品身份。</param>
        /// <returns>交易合法并进入保存时为真。</returns>
        public bool TryBuyMerchantOffer(string offerId)
        {
            if (!CanManageMerchant || !World.TryGetActor(Player, out CombatActorSnapshot player)) return false;
            RegionNpcPlan npc = CurrentRegion.Plan.Npcs.Find(value => value.Id == activeNpcId);
            float radius = CurrentRegion.Plan.InteractionRadius;
            if (npc == null || (player.Position - npc.Position).sqrMagnitude > radius * radius) return ReportInventoryFailure("已离开商人范围。");
            RegionNpcOfferPlan offer = npc.Offers.Find(value => value.Id == offerId);
            RegionNpcState state = CurrentRegion.Npcs.Find(value => value.Id == activeNpcId);
            if (offer == null || state.SoldOfferIds.Contains(offerId)) return ReportInventoryFailure("该商品已售出。");
            if (AvailableGold < offer.Price) return ReportInventoryFailure("金币不足。");
            if (PlayerLevel < offer.Item.RequiredLevel) return ReportInventoryFailure("角色等级不足。");
            if (!CurrentRegion.TrySellNpcOffer(activeNpcId, offerId)) return false;
            GoldSpent += offer.Price;
            inventory.Add(offer.Item.Copy());
            SaveMutation("已购买：" + offer.Item.Name);
            return true;
        }
    }
}
