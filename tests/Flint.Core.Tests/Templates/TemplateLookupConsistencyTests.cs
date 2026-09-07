// Flint 静态站点生成器
// TemplateLookup（加权匹配）与 ResolveTemplatePath（文件名约定）的一致性对比测试

using Flint.Core.Templates;
using Xunit;

namespace Flint.Core.Tests.Templates;

/// <summary>
/// 新旧模板查找器一致性对比：在快照矩阵的每种布局下，
/// TemplateLookup.Resolve 与 ScribanTemplateRenderer.TemplateExists
/// 对同名请求的"存在性"判定必须一致（找到/都找不到）。
/// 替换默认查找路径前的安全验证。
/// </summary>
public sealed class TemplateLookupConsistencyTests : IDisposable
{
    private readonly string _siteDir;
    private readonly string _themeDir;

    public TemplateLookupConsistencyTests()
    {
        _siteDir = Path.Combine(Path.GetTempPath(), $"flint-tlcons-{Guid.NewGuid():N}");
        _themeDir = Path.Combine(_siteDir, "themes", "t1", "layouts");
        Directory.CreateDirectory(_siteDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_siteDir)) Directory.Delete(_siteRoot(), recursive: true); }
        catch (IOException) { }
    }

    private string SiteLayouts() => Path.Combine(_siteDir, "layouts");

    private string _siteRoot() => _siteDir;

    private ScribanTemplateRenderer CreateRenderer()
    {
        return new ScribanTemplateRenderer(SiteLayouts(), "https://example.com", _themeDir);
    }

    private TemplateLookup CreateLookup()
    {
        return new TemplateLookup(SiteLayouts(), _themeDir);
    }

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
    public void Consistency_单根形态_新旧查找一致()
    {
        WriteSite("single.html", "S");
        var renderer = CreateRenderer();
        var lookup = CreateLookup();

        // 旧查找：TemplateExists 走 ResolveTemplatePath（相对 layouts 根）
        var oldFound = renderer.TemplateExists("single");
        // 新查找：加权器在站点根中找 single.html
        var newFound = lookup.Resolve("single", _siteRoot()) is not null;

        Assert.Equal(oldFound, newFound);
        Assert.True(newFound);
    }

    [Fact]
    public void Consistency_输出格式变体_新旧查找一致()
    {
        WriteSite("single.json.html", "{\"t\":1}");
        var renderer = CreateRenderer();
        var lookup = CreateLookup();

        var oldFound = renderer.TemplateExists("single.json");
        var newFound = lookup.Resolve("single.json", _siteRoot()) is not null;

        Assert.True(oldFound);
        Assert.True(newFound);
        Assert.Equal(oldFound, newFound);
    }

    [Fact]
    public void Consistency_不存在的模板_新旧都报缺失()
    {
        var renderer = CreateRenderer();
        var lookup = CreateLookup();

        Assert.False(renderer.TemplateExists("nosuch"));
        Assert.False(lookup.Resolve("nosuch", _siteRoot()) is not null);
    }

    [Fact]
    public void Consistency_主题回退_新旧查找一致()
    {
        WriteTheme("single.html", "T");
        var renderer = CreateRenderer();
        var lookup = CreateLookup();

        var oldFound = renderer.TemplateExists("single");
        var newFound = lookup.Resolve("single", _siteRoot()) is not null;

        Assert.True(oldFound);
        Assert.True(newFound);
        Assert.Equal(oldFound, newFound);
    }

    [Fact]
    public void Consistency_优先级_新旧查找选中同一物理文件()
    {
        // 站点与主题同名并存：两者都应选中站点版本
        WriteSite(Path.Combine("layouts", "single.html"), "SITE");
        WriteTheme("single.html", "THEME");
        var renderer = CreateRenderer();
        var lookup = CreateLookup();

        var htmlTask = renderer.RenderAsync("single", RendererTestsContext.Default);
        var lookupPath = lookup.Resolve("single", _siteRoot());
        var html = htmlTask.AsTask().GetAwaiter().GetResult();

        Assert.Contains("SITE", html, StringComparison.Ordinal);
        Assert.NotNull(lookupPath);
        Assert.Contains("SITE", File.ReadAllText(lookupPath), StringComparison.Ordinal);
    }

}
