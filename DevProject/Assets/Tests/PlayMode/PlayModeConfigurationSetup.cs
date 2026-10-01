using GameLogic.Project;
using NUnit.Framework;
using ProjectExtraction.Tests.Shared;

/// <summary>
/// 在播放模式测试程序集的全部命名空间运行前加载真实配置表。
/// </summary>
[SetUpFixture]
public sealed class PlayModeConfigurationSetup
{
    /// <summary>
    /// 在编辑器中从内容源加载表并执行项目总校验。
    /// </summary>
    [OneTimeSetUp]
    public void LoadConfiguration()
    {
#if UNITY_EDITOR
        TestGameTables.LoadAll();
        Assert.That(ProjectContentConfig.Validate(out string error), Is.True, error);
#else
        Assert.Ignore("项目测试配置加载器仅支持 Unity Editor；Player 测试需接入实际内容包加载。");
#endif
    }
}
