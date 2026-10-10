using System;
using System.Collections.Generic;
using GameFramework;
using GameLogic.Gameplay.Combat;
using GameLogic.Gameplay.Level;
using GameLogic.Project;

namespace GameLogic.Gameplay.Expedition
{
    /// <summary>
    /// 单局 NPC 商店的一件商品快照，物品读取时始终返回独立副本。
    /// </summary>
    public sealed class ExpeditionMerchantOffer
    {
        private readonly ProjectItem item;

        public string Id { get; }
        public ProjectItem Item => item.Copy();
        public int Price { get; }
        public bool Sold { get; }

        /// <summary>
        /// 创建不共享物品可写状态的商品快照。
        /// </summary>
        /// <param name="id">单局内稳定的商品身份。</param>
        /// <param name="item">已经生成并冻结属性的商品。</param>
        /// <param name="price">金币售价。</param>
        /// <param name="sold">是否已经出售。</param>
        internal ExpeditionMerchantOffer(string id, ProjectItem item, int price, bool sold)
        {
            Id = id;
            this.item = item.Copy();
            Price = price;
            Sold = sold;
        }
    }

    /// <summary>
    /// 单局 NPC 商品生成、展示与购买逻辑。
    /// </summary>
    public sealed partial class ExpeditionRun
    {
        private sealed class MerchantStock
        {
            public string Id;
            public ProjectItem Item;
            public int Price;
            public bool Sold;
        }

        private readonly Dictionary<int, List<MerchantStock>> merchantStocks = new();
        private bool merchantsInitialized;

        /// <summary>
        /// 获取当前房间的全部商品，包括已经售出的商品。
        /// </summary>
        public IReadOnlyList<ExpeditionMerchantOffer> MerchantOffers
        {
            get
            {
                var snapshot = new List<ExpeditionMerchantOffer>();
                if (CurrentRoom?.Type != LevelRoomType.Npc || !merchantStocks.TryGetValue(Maps.CurrentRoomId, out List<MerchantStock> stocks))
                    return snapshot.AsReadOnly();
                foreach (MerchantStock stock in stocks)
                    snapshot.Add(new ExpeditionMerchantOffer(stock.Id, stock.Item, stock.Price, stock.Sold));
                return snapshot.AsReadOnly();
            }
        }

        /// <summary>
        /// 在单局建立时预生成所有 NPC 房间的商品，回访不重新抽取。
        /// </summary>
        /// <returns>全部房间商品成功生成时为真。</returns>
        private bool InitializeMerchants()
        {
            if (merchantsInitialized) return true;
            var prepared = new Dictionary<int, List<MerchantStock>>();
            foreach (LevelRoomState room in Maps.Rooms)
            {
                if (room.Type != LevelRoomType.Npc) continue;
                LevelRoomPlan plan = room.Plan;
                if (plan == null || plan.NpcStock < 1 || plan.NpcPrice < 1 ||
                    !ProjectEquipmentRules.HasLoot(plan.LootPoolID, LootLevel))
                {
                    GameEntry.Get<ILogModule>()?.Error("Expedition", "NPC 房间商品配置不可用：" + room.RoomId);
                    return false;
                }
                var stocks = new List<MerchantStock>(plan.NpcStock);
                var merchantRandom = new Random(unchecked((Seed * 397) ^ room.RoomId ^ 0x4D455243));
                for (int index = 0; index < plan.NpcStock; index++)
                {
                    ProjectItem item = ProjectEquipmentRules.CreateLootFromPool(merchantRandom, LootLevel, plan.LootPoolID);
                    if (item == null)
                    {
                        GameEntry.Get<ILogModule>()?.Error("Expedition", "NPC 房间商品生成失败：" + room.RoomId + "/" + index);
                        return false;
                    }
                    string id = RunId + ":merchant:" + room.RoomId + ":" + index;
                    item.Id = id;
                    item.Equipped = false;
                    stocks.Add(new MerchantStock { Id = id, Item = item.Copy(), Price = plan.NpcPrice });
                }
                prepared.Add(room.RoomId, stocks);
            }
            foreach (KeyValuePair<int, List<MerchantStock>> entry in prepared) merchantStocks.Add(entry.Key, entry.Value);
            merchantsInitialized = true;
            return true;
        }

        /// <summary>
        /// 购买当前 NPC 房间的一件未售商品。
        /// </summary>
        /// <param name="offerId">商品的单局稳定身份。</param>
        /// <returns>商品、金币和房间剩余库存已一起更新时为真。</returns>
        public bool TryPurchase(string offerId)
        {
            LevelRoomState room = CurrentRoom;
            if (!CanManageMerchant || room?.Type != LevelRoomType.Npc || RoomView == null ||
                !World.TryGetActor(Player, out CombatActorSnapshot player) || player.Dead ||
                !RoomView.TryGetInteractionNear(player.Position, out LevelInteractionKind kind) || kind != LevelInteractionKind.Npc ||
                string.IsNullOrEmpty(offerId) || !merchantStocks.TryGetValue(room.RoomId, out List<MerchantStock> stocks)) return false;

            MerchantStock selected = stocks.Find(stock => stock.Id == offerId);
            if (selected == null || selected.Sold || selected.Item == null || selected.Price < 1) return false;
            if (AvailableGold < selected.Price) { Message = "金币不足。"; Changed?.Invoke(); return false; }

            int remaining = 0;
            foreach (MerchantStock stock in stocks) if (!stock.Sold) remaining++;
            int currentStock = room.TryGetNpcStock("relic", out int recorded) ? recorded : room.Plan.NpcStock;
            if (remaining != currentStock || remaining < 1 || !room.TrySetNpcStock("relic", remaining - 1))
            {
                GameEntry.Get<ILogModule>()?.Error("Expedition", "NPC 房间商品库存不一致：" + room.RoomId);
                return false;
            }

            ProjectItem purchased = selected.Item.Copy();
            purchased.Equipped = false;
            selected.Sold = true;
            GoldSpent += selected.Price;
            inventory.Add(purchased);
            Message = "购买成功，金币 -" + selected.Price;
            Changed?.Invoke();
            return true;
        }

        /// <summary>
        /// 玩家靠近 NPC 时打开当前房间商店。
        /// </summary>
        /// <returns>商店面板已打开时为真。</returns>
        private bool TryOpenMerchant()
        {
            if (!CanInteract || ActivePanel != ExpeditionPanelKind.None || CurrentRoom?.Type != LevelRoomType.Npc || RoomView == null ||
                !World.TryGetActor(Player, out CombatActorSnapshot player) || player.Dead ||
                !RoomView.TryGetInteractionNear(player.Position, out LevelInteractionKind kind) || kind != LevelInteractionKind.Npc) return false;
            SetActivePanel(ExpeditionPanelKind.Merchant);
            Message = "选择一件商品购买。";
            Changed?.Invoke();
            return true;
        }
    }
}
