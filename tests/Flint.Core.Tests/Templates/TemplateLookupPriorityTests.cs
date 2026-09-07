// Flint 静态站点生成器
// TemplateLookup 加权匹配的优先级行为测试。
// 回归防护：Score 的逻辑名精确分曾因带 .html 后缀比较恒不触发，
// 同分胜者由目录枚举顺序决定（Windows 碰巧正确、Linux 无保证）。

using Flint.Core.Templates;
using Xunit;

namespace Flint.Core.Tests.Templates;

public sealed class TemplateLookupPriorityTests : IDisposable
{
    private readonly string _siteDir;
    private readonly string _themeDir;

    public TemplateLookupPriorityTests()
    {
        _siteDir = Path.Combine(Path.GetTempPath(), $"flint-tlpri-{Guid.NewGuid():N}");
        _themeDir = Path.Combine(_siteDir, "themes", "t1", "layouts");
        Directory.CreateDirectory(_siteDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_siteDir)) Directory.Delete(_siteDir, recursive: true); }
        catch (IOException) { }
    }

    private string SiteLayouts() => Path.Combine(_siteDir, "layouts");

    private void WriteSite(string relative, string content)
    {
        var path = Path.Combine(SiteLayouts(), relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private void WriteTheme(string relative, string content)
    {
        var path = Path.Combine(_themeDir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    [Fact]
    public void 根形态优先于_default形态_同根内()
    {
        WriteSite("single.html", "ROOT");
        WriteSite(Path.Combine("_default", "single.html"), "DEFAULT");
        var lookup = new TemplateLookup(SiteLayouts());

        var resolved = lookup.Resolve("single", _siteDir);

        Assert.EndsWith("single.html", resolved, StringComparison.Ordinal);
        Assert.DoesNotContain("_default", resolved, StringComparison.Ordinal);
    }

    [Fact]
    public void 站点_default形态优先于主题根形态()
    {
        WriteSite(Path.Combine("_default", "single.html"), "SITE-DEFAULT");
        WriteTheme("single.html", "THEME-ROOT");
        var lookup = new TemplateLookup(SiteLayouts(), _themeDir);

        var resolved = lookup.Resolve("single", _siteDir);

        Assert.Contains("_default", resolved, StringComparison.Ordinal);
        Assert.StartsWith(SiteLayouts(), resolved, StringComparison.Ordinal);
    }

    [Fact]
    public void 请求带_default前缀时_default形态胜出()
    {
        // 带目录前缀的请求走全串精确（+10），必须压过根形态的文件名段匹配（+6）
        WriteSite("single.html", "ROOT");
        WriteSite(Path.Combine("_default", "single.html"), "DEFAULT");
        var lookup = new TemplateLookup(SiteLayouts());

        var resolved = lookup.Resolve("_default/single", _siteDir);

        Assert.Contains("_default", resolved, StringComparison.Ordinal);
    }

    [Fact]
    public void 输出格式变体精确匹配()
    {
        WriteSite("single.html", "HTML");
        WriteSite("single.json.html", "JSON");
        var lookup = new TemplateLookup(SiteLayouts());

        Assert.EndsWith("single.json.html", lookup.Resolve("single.json", _siteDir), StringComparison.Ordinal);
        Assert.EndsWith("single.html", lookup.Resolve("single", _siteDir), StringComparison.Ordinal);
    }

    [Fact]
    public void 同根同分时路径字典序决定胜者()
    {
        // 文档契约：同分并列按路径字典序（此前未实现，由枚举顺序决定）
        WriteSite(Path.Combine("a", "single.html"), "A");
        WriteSite(Path.Combine("b", "single.html"), "B");
        var lookup = new TemplateLookup(SiteLayouts());

        // 两者同为文件名段匹配（+6）同分 → 字典序 a/single.html 胜
        Assert.Contains(Path.DirectorySeparatorChar + "a" + Path.DirectorySeparatorChar,
            lookup.Resolve("single", _siteDir), StringComparison.Ordinal);
    }
}
