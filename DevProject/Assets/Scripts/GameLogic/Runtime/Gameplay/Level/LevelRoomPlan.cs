using System.Collections.Generic;

namespace GameLogic.Gameplay.Level
{
    /// <summary>
    /// 单局房间已抽定的内容和几何定义。
    /// </summary>
    public sealed class LevelRoomPlan
    {
        public int TemplateID { get; }
        public int EncounterID { get; }
        public LevelDefinition Definition { get; }
        public IReadOnlyList<SpawnDefinition> Spawns { get; }
        public string LootPoolID { get; }
        public int NpcPrice { get; }
        public int NpcStock { get; }

        /// <summary>
        /// 固定一次模板、遭遇和对应内容。
        /// </summary>
        /// <param name="templateId">模板编号。</param>
        /// <param name="encounterId">遭遇编号；非战斗房间为零。</param>
        /// <param name="definition">选中布局的关卡定义。</param>
        /// <param name="spawns">选中遭遇的出生组。</param>
        /// <param name="lootPoolId">战利品池标识。</param>
        /// <param name="npcPrice">NPC 商品价格。</param>
        /// <param name="npcStock">NPC 商品库存。</param>
        internal LevelRoomPlan(int templateId, int encounterId, LevelDefinition definition, IReadOnlyList<SpawnDefinition> spawns,
            string lootPoolId, int npcPrice, int npcStock)
        {
            TemplateID = templateId;
            EncounterID = encounterId;
            Definition = definition;
            Spawns = spawns;
            LootPoolID = lootPoolId;
            NpcPrice = npcPrice;
            NpcStock = npcStock;
        }
    }
}
