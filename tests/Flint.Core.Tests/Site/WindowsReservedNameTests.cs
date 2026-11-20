// Flint 静态站点生成器
// Windows 保留名规避锁定（R1 审计 HYG-B1）：CON/PRN/AUX/NUL/COM1-9/LPT1-9
// 及带扩展形态（con.txt）在 Windows 上无法作为文件名，直落会抛 IOException
// 并把整次构建截断成 BUILD001（alias/资产分支尤其严重，后续 sitemap/feed
// 全不产出）。修复：slug 生成层追加 "-" 后缀，跨平台产物一致。

using Flint.Core.Site;
using Xunit;

namespace Flint.Core.Tests.Site;

public sealed class WindowsReservedNameTests
{
    [Theory]
    [InlineData("con")]
    [InlineData("CON")]
    [InlineData("prn")]
    [InlineData("aux")]
    [InlineData("nul")]
    [InlineData("com1")]
    [InlineData("com9")]
    [InlineData("lpt1")]
    [InlineData("lpt9")]
    public void 保留名_追加后缀(string reserved)
    {
        var safe = PermalinkEngine.SanitizeWindowsReservedName(reserved);
        Assert.NotEqual(reserved, safe);
        Assert.EndsWith("-", safe, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("con.txt", "con-.txt")]
    [InlineData("NUL.md", "NUL-.md")]
    [InlineData("com3.tar", "com3-.tar")]
    public void 带扩展形态_按扩展名前插入后缀(string input, string expected)
    {
        Assert.Equal(expected, PermalinkEngine.SanitizeWindowsReservedName(input));
    }

    [Theory]
    [InlineData("console")]      // 前缀相近但不是保留名
    [InlineData("com0")]         // COM0 不是保留名（只有 1-9）
    [InlineData("com10")]        // 两位数字不是保留名
    [InlineData("lpt0")]
    [InlineData("my-con")]       // 段中间出现不算
    [InlineData("posts")]
    [InlineData("文章")]
    public void 非保留名_原样返回(string normal)
    {
        Assert.Equal(normal, PermalinkEngine.SanitizeWindowsReservedName(normal));
    }

    [Fact]
    public void 空串_原样返回()
    {
        Assert.Equal("", PermalinkEngine.SanitizeWindowsReservedName(""));
    }

    [Fact]
    public void GenerateSlug_保留名自动规避()
    {
        // 标题 "CON" 经 GenerateSlug 后不落保留名
        var slug = PermalinkEngine.GenerateSlug("CON");
        Assert.False(
            slug.Equals("con", StringComparison.OrdinalIgnoreCase),
            $"GenerateSlug(\"CON\") = {slug}，仍是保留名");
    }
}
