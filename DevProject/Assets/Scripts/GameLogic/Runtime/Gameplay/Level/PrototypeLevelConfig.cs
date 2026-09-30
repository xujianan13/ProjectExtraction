namespace GameLogic.Gameplay.Level
{
    /// <summary>
    /// 首版关卡原型参数；这些数值只用于可运行测试，不代表正式配置。
    /// </summary>
    public static class PrototypeLevelConfig
    {
        public const int GridWidth = 5;
        public const int GridHeight = 2;
        public const int RoomCount = GridWidth * GridHeight;
        public const int ExtraConnectionMin = 1;
        public const int ExtraConnectionMax = 2;
        public const int LootRoomCount = 2;
        public const float ExtraExtractionChance = 0.2f;
        public const float RoomHalfExtent = 12f;
        public const float WallHeight = 2.5f;
        public const float WallThickness = 0.6f;
        public const float ExitHalfWidth = 2f;
        public const float SpawnInset = 4f;
        public const float ExitInteractionRadius = 2.4f;
        public const float ObjectInteractionRadius = 2.2f;
    }
}
