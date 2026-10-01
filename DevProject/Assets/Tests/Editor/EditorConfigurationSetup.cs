using GameLogic.Project;
using NUnit.Framework;
using ProjectExtraction.Tests.Shared;

/// <summary>
/// 在编辑器测试程序集的全部命名空间运行前加载真实配置表。
/// </summary>
[SetUpFixture]
public sealed class EditorConfigurationSetup
{
    /// <summary>
    /// 从已导出的内容源加载表并执行项目总校验。
    /// </summary>
    [OneTimeSetUp]
    public void LoadConfiguration()
    {
        TestGameTables.LoadAll();
        Assert.That(ProjectContentConfig.Validate(out string error), Is.True, error);
    }
}
