namespace GameFramework
{
    /// <summary>统一 Package 收集规则生成的稳定逻辑地址。</summary>
    public static class HotUpdatePackageLocations
    {
        /// <summary>UpdaterPackage 包级配置地址。</summary>
        public const string UpdaterSettings = "UpdaterPackageSettings";

        /// <summary>GamePackage 包级配置地址。</summary>
        public const string GameSettings = "GamePackageSettings";

        /// <summary>UpdateLogic 程序集字节地址。</summary>
        public const string UpdaterAssembly = "UpdateLogic.dll";

        /// <summary>GameLogic 程序集字节地址。</summary>
        public const string GameAssembly = "GameLogic.dll";
    }
}
