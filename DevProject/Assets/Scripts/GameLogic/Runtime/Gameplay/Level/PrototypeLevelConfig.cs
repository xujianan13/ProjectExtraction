using GameLogic.Tables;

namespace GameLogic.Gameplay.Level
{
    /// <summary>
    /// 旧关卡入口使用的显式默认配置。
    /// </summary>
    public static class PrototypeLevelConfig
    {
        public static int GridWidth => DefaultLevel.Row.GridWidth;
        public static int GridHeight => DefaultLevel.Row.GridHeight;
        public static int RoomCount => GridWidth * GridHeight;
        public static int ExtraConnectionMin => DefaultLevel.Row.ExtraConnectionMin;
        public static int ExtraConnectionMax => DefaultLevel.Row.ExtraConnectionMax;
        public static int LootRoomCount => DefaultLevel.Row.LootRoomCount;
        public static float RoomHalfExtent => (float)DefaultLevel.Layout.HalfExtent;
        public static float WallThickness => (float)DefaultLevel.Layout.WallThickness;

        /// <summary>
        /// 取得配置表中唯一标记为默认的关卡。
        /// </summary>
        /// <returns>默认关卡；表未加载或内容无效时为空。</returns>
        public static LevelDefinition DefaultLevel
        {
            get
            {
                if (GameTables.CfgLevels == null) return null;
                foreach (CfgLevelsRow row in GameTables.CfgLevels.GetAll())
                    if (row.IsDefault && LevelContentConfig.TryGetLevel(row.ID, out LevelDefinition level)) return level;
                return null;
            }
        }
    }
}
