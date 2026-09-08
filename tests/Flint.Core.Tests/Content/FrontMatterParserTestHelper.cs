// 测试辅助：直接驱动 YamlDotNet 完整解析路径（绕过快速路径），
// 供等价性对比测试使用

using Flint.Core.Configuration;
using Flint.Core.Models;

namespace Flint.Core.Tests.Content;

public static class FrontMatterParserTestHelper
{
    /// <summary>强制走 YamlDotNet 完整解析（绕过快速路径的测试钩子）</summary>
    public static FrontMatter ParseViaYamlEngine(string yaml)
        => Flint.Core.Content.FrontMatterParser.ParseViaYamlEngineForTest(yaml);

    /// <summary>同上，但失败返回 false 而不抛出</summary>
    public static bool ParseViaYamlEngineOrNull(string yaml, out FrontMatter? frontMatter)
    {
        try
        {
            frontMatter = ParseViaYamlEngine(yaml);
            return true;
        }
        catch
        {
            frontMatter = null;
            return false;
        }
    }
}
