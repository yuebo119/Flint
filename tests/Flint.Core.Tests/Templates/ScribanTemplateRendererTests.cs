// Flint 静态站点生成器
// Scriban 模板渲染器单元测试

using Flint.Core.Abstractions;
using Flint.Core.Templates;
using Xunit;

namespace Flint.Core.Tests.Templates;

/// <summary>
/// Scriban 模板渲染器单元测试
/// </summary>
public class ScribanTemplateRendererTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ScribanTemplateRenderer _renderer;

    public ScribanTemplateRendererTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"Flint_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _renderer = new ScribanTemplateRenderer(_tempDir, "https://example.com");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
        GC.SuppressFinalize(this);
    }

    private TemplateContext CreateTestContext(string title = "Test Page", string content = "<p>Hello</p>")
    {
        return new TemplateContext
        {
            Page = new PageContext
            {
                Title = title,
                Content = content,
                Permalink = "https://example.com/test/",
                RelPermalink = "/test/",
                Date = new DateTimeOffset(2024, 1, 15, 10, 30, 0, TimeSpan.Zero),
                Tags = ["tag1", "tag2"],
                Categories = ["cat1"],
                WordCount = 100,
                ReadingTime = TimeSpan.FromMinutes(1)
            },
            Site = new SiteContext
            {
                Title = "Test Site",
                BaseURL = "https://example.com",
                Language = "en",
                Pages = [],
                RegularPages = [],
                Taxonomies = new TaxonomyCollection
                {
                    Taxonomies = new Dictionary<string, IReadOnlyList<TaxonomyTerm>>()
                },
                Menus = new MenuCollection
                {
                    Menus = new Dictionary<string, IReadOnlyList<MenuItem>>()
                },
                Config = new { }
            }
        };
    }

    [Fact]
    public async Task RenderAsync_SimpleTemplate_ReturnsRenderedContent()
    {
        // Arrange
        var templateContent = "<h1>{{ page.title }}</h1>";
        File.WriteAllText(Path.Combine(_tempDir, "test.html"), templateContent);
        var context = CreateTestContext("Hello World");

        // Act
        var result = await _renderer.RenderAsync("test.html", context);

        // Assert
        Assert.Equal("<h1>Hello World</h1>", result);
    }

    [Fact]
    public async Task RenderAsync_WithPageContent_RendersContent()
    {
        // Arrange
        var templateContent = "<article>{{ page.content }}</article>";
        File.WriteAllText(Path.Combine(_tempDir, "article.html"), templateContent);
        var context = CreateTestContext(content: "<p>Test content</p>");

        // Act
        var result = await _renderer.RenderAsync("article.html", context);

        // Assert
        Assert.Equal("<article><p>Test content</p></article>", result);
    }


    [Fact]
    public async Task RenderAsync_WithSiteData_RendersSiteInfo()
    {
        // Arrange
        var templateContent = "<title>{{ site.title }}</title>";
        File.WriteAllText(Path.Combine(_tempDir, "head.html"), templateContent);
        var context = CreateTestContext();

        // Act
        var result = await _renderer.RenderAsync("head.html", context);

        // Assert
        Assert.Equal("<title>Test Site</title>", result);
    }

    [Fact]
    public async Task RenderAsync_WithLoop_RendersAllItems()
    {
        // Arrange - Scriban 使用 {{ for }} 语法
        var templateContent = "{{ for tag in page.tags }}{{ tag }},{{ end }}";
        File.WriteAllText(Path.Combine(_tempDir, "tags.html"), templateContent);
        var context = CreateTestContext();

        // Act
        var result = await _renderer.RenderAsync("tags.html", context);

        // Assert
        Assert.Equal("tag1,tag2,", result);
    }

    [Fact]
    public async Task RenderAsync_WithConditional_RendersCorrectBranch()
    {
        // Arrange - Scriban 使用 {{ if }} 语法
        var templateContent = "{{ if page.draft }}DRAFT{{ else }}PUBLISHED{{ end }}";
        File.WriteAllText(Path.Combine(_tempDir, "status.html"), templateContent);
        var context = CreateTestContext();

        // Act
        var result = await _renderer.RenderAsync("status.html", context);

        // Assert
        Assert.Equal("PUBLISHED", result);
    }

    [Fact]
    public async Task RenderStringAsync_InlineTemplate_ReturnsRenderedContent()
    {
        // Arrange
        var templateContent = "Hello, {{ page.title }}!";
        var context = CreateTestContext("World");

        // Act
        var result = await _renderer.RenderStringAsync(templateContent, context);

        // Assert
        Assert.Equal("Hello, World!", result);
    }

    [Fact]
    public async Task RenderAsync_WithBuiltinFunction_Upper()
    {
        // Arrange
        var templateContent = "{{ page.title | upper }}";
        File.WriteAllText(Path.Combine(_tempDir, "upper.html"), templateContent);
        var context = CreateTestContext("hello");

        // Act
        var result = await _renderer.RenderAsync("upper.html", context);

        // Assert
        Assert.Equal("HELLO", result);
    }

    [Fact]
    public async Task RenderAsync_WithBuiltinFunction_Lower()
    {
        // Arrange
        var templateContent = "{{ page.title | lower }}";
        File.WriteAllText(Path.Combine(_tempDir, "lower.html"), templateContent);
        var context = CreateTestContext("HELLO");

        // Act
        var result = await _renderer.RenderAsync("lower.html", context);

        // Assert
        Assert.Equal("hello", result);
    }

    [Fact]
    public async Task RenderAsync_WithBuiltinFunction_Truncate()
    {
        // Arrange
        var templateContent = "{{ page.title | truncate 10 '...' }}";
        File.WriteAllText(Path.Combine(_tempDir, "truncate.html"), templateContent);
        var context = CreateTestContext("This is a very long title");

        // Act
        var result = await _renderer.RenderAsync("truncate.html", context);

        // Assert
        Assert.Equal("This is...", result);
    }

    [Fact]
    public async Task RenderAsync_WithBuiltinFunction_DateFormat()
    {
        // Arrange
        var templateContent = "{{ page.date | date_format 'yyyy-MM-dd' }}";
        File.WriteAllText(Path.Combine(_tempDir, "date.html"), templateContent);
        var context = CreateTestContext();

        // Act
        var result = await _renderer.RenderAsync("date.html", context);

        // Assert
        Assert.Equal("2024-01-15", result);
    }

    [Fact]
    public async Task RenderAsync_WithBuiltinFunction_Len()
    {
        // Arrange
        var templateContent = "{{ page.tags | len }}";
        File.WriteAllText(Path.Combine(_tempDir, "len.html"), templateContent);
        var context = CreateTestContext();

        // Act
        var result = await _renderer.RenderAsync("len.html", context);

        // Assert
        Assert.Equal("2", result);
    }

    [Fact]
    public async Task RenderAsync_WithBuiltinFunction_First()
    {
        // Arrange
        var templateContent = "{{ page.tags | first }}";
        File.WriteAllText(Path.Combine(_tempDir, "first.html"), templateContent);
        var context = CreateTestContext();

        // Act
        var result = await _renderer.RenderAsync("first.html", context);

        // Assert
        Assert.Equal("tag1", result);
    }

    [Fact]
    public async Task RenderAsync_WithBuiltinFunction_Last()
    {
        // Arrange
        var templateContent = "{{ page.tags | last }}";
        File.WriteAllText(Path.Combine(_tempDir, "last.html"), templateContent);
        var context = CreateTestContext();

        // Act
        var result = await _renderer.RenderAsync("last.html", context);

        // Assert
        Assert.Equal("tag2", result);
    }

    [Fact]
    public async Task RenderAsync_WithBuiltinFunction_AbsURL()
    {
        // Arrange
        var templateContent = "{{ '/about/' | absURL }}";
        File.WriteAllText(Path.Combine(_tempDir, "absurl.html"), templateContent);
        var context = CreateTestContext();

        // Act
        var result = await _renderer.RenderAsync("absurl.html", context);

        // Assert
        Assert.Equal("https://example.com/about/", result);
    }

    [Fact]
    public async Task RenderAsync_WithBuiltinFunction_RelURL()
    {
        // Arrange
        var templateContent = "{{ 'about' | relURL }}";
        File.WriteAllText(Path.Combine(_tempDir, "relurl.html"), templateContent);
        var context = CreateTestContext();

        // Act
        var result = await _renderer.RenderAsync("relurl.html", context);

        // Assert
        Assert.Equal("/about", result);
    }


    [Fact]
    public async Task RenderAsync_WithBuiltinFunction_Join()
    {
        // Arrange
        var templateContent = "{{ page.tags | join ', ' }}";
        File.WriteAllText(Path.Combine(_tempDir, "join.html"), templateContent);
        var context = CreateTestContext();

        // Act
        var result = await _renderer.RenderAsync("join.html", context);

        // Assert
        Assert.Equal("tag1, tag2", result);
    }

    [Fact]
    public async Task RenderAsync_WithBuiltinFunction_HasPrefix()
    {
        // Arrange - Scriban 使用 {{ if }} 语法
        var templateContent = "{{ if page.title | has_prefix 'Test' }}YES{{ else }}NO{{ end }}";
        File.WriteAllText(Path.Combine(_tempDir, "hasprefix.html"), templateContent);
        var context = CreateTestContext("Test Page");

        // Act
        var result = await _renderer.RenderAsync("hasprefix.html", context);

        // Assert
        Assert.Equal("YES", result);
    }

    [Fact]
    public async Task RenderAsync_WithBuiltinFunction_HasSuffix()
    {
        // Arrange - Scriban 使用 {{ if }} 语法
        var templateContent = "{{ if page.title | has_suffix 'Page' }}YES{{ else }}NO{{ end }}";
        File.WriteAllText(Path.Combine(_tempDir, "hassuffix.html"), templateContent);
        var context = CreateTestContext("Test Page");

        // Act
        var result = await _renderer.RenderAsync("hassuffix.html", context);

        // Assert
        Assert.Equal("YES", result);
    }

    [Fact]
    public async Task RenderAsync_WithBuiltinFunction_Replace()
    {
        // Arrange
        var templateContent = "{{ page.title | replace 'World' 'Universe' }}";
        File.WriteAllText(Path.Combine(_tempDir, "replace.html"), templateContent);
        var context = CreateTestContext("Hello World");

        // Act
        var result = await _renderer.RenderAsync("replace.html", context);

        // Assert
        Assert.Equal("Hello Universe", result);
    }

    [Fact]
    public async Task RenderAsync_WithBuiltinFunction_Trim()
    {
        // Arrange
        var templateContent = "{{ '  hello  ' | trim }}";
        File.WriteAllText(Path.Combine(_tempDir, "trim.html"), templateContent);
        var context = CreateTestContext();

        // Act
        var result = await _renderer.RenderAsync("trim.html", context);

        // Assert
        Assert.Equal("hello", result);
    }

    [Fact]
    public async Task RenderAsync_WithBuiltinFunction_Add()
    {
        // Arrange
        var templateContent = "{{ 5 | add 3 }}";
        File.WriteAllText(Path.Combine(_tempDir, "add.html"), templateContent);
        var context = CreateTestContext();

        // Act
        var result = await _renderer.RenderAsync("add.html", context);

        // Assert
        Assert.Equal("8", result);
    }

    [Fact]
    public async Task RenderAsync_WithBuiltinFunction_Mul()
    {
        // Arrange
        var templateContent = "{{ 5 | mul 3 }}";
        File.WriteAllText(Path.Combine(_tempDir, "mul.html"), templateContent);
        var context = CreateTestContext();

        // Act
        var result = await _renderer.RenderAsync("mul.html", context);

        // Assert
        Assert.Equal("15", result);
    }

    [Fact]
    public async Task RenderAsync_WithBuiltinFunction_Base64Encode()
    {
        // Arrange
        var templateContent = "{{ 'hello' | base64Encode }}";
        File.WriteAllText(Path.Combine(_tempDir, "base64.html"), templateContent);
        var context = CreateTestContext();

        // Act
        var result = await _renderer.RenderAsync("base64.html", context);

        // Assert
        Assert.Equal("aGVsbG8=", result);
    }

    [Fact]
    public async Task RenderAsync_WithBuiltinFunction_Sha256()
    {
        // Arrange
        var templateContent = "{{ 'hello' | sha256 }}";
        File.WriteAllText(Path.Combine(_tempDir, "sha256.html"), templateContent);
        var context = CreateTestContext();

        // Act
        var result = await _renderer.RenderAsync("sha256.html", context);

        // Assert
        Assert.Equal("2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824", result);
    }

    [Fact]
    public async Task RenderAsync_WithHugoCompatibleVariables_Page()
    {
        // Arrange - 使用 Hugo 风格的大写变量名
        var templateContent = "{{ page.Title }} - {{ page.WordCount }}";
        File.WriteAllText(Path.Combine(_tempDir, "hugo.html"), templateContent);
        var context = CreateTestContext("Hugo Style");

        // Act
        var result = await _renderer.RenderAsync("hugo.html", context);

        // Assert
        Assert.Equal("Hugo Style - 100", result);
    }

    [Fact]
    public async Task RenderAsync_WithHugoCompatibleVariables_Site()
    {
        // Arrange
        var templateContent = "{{ site.Title }} - {{ site.BaseURL }}";
        File.WriteAllText(Path.Combine(_tempDir, "hugosite.html"), templateContent);
        var context = CreateTestContext();

        // Act
        var result = await _renderer.RenderAsync("hugosite.html", context);

        // Assert
        Assert.Equal("Test Site - https://example.com", result);
    }

    [Fact]
    public void TemplateExists_ExistingTemplate_ReturnsTrue()
    {
        // Arrange
        File.WriteAllText(Path.Combine(_tempDir, "exists.html"), "content");

        // Act
        var result = _renderer.TemplateExists("exists.html");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void TemplateExists_NonExistingTemplate_ReturnsFalse()
    {
        // Act
        var result = _renderer.TemplateExists("nonexistent.html");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task RenderAsync_InvalidTemplate_ThrowsException()
    {
        // Arrange - 使用真正的语法错误
        var templateContent = "{{ if }}";  // 缺少条件
        File.WriteAllText(Path.Combine(_tempDir, "invalid.html"), templateContent);
        var context = CreateTestContext();

        // Act & Assert - Scriban 会抛出解析异常
        await Assert.ThrowsAnyAsync<Exception>(
            () => _renderer.RenderAsync("invalid.html", context).AsTask());
    }

    [Fact]
    public async Task RenderAsync_NonExistingTemplate_ThrowsTemplateNotFoundException()
    {
        // Arrange
        var context = CreateTestContext();

        // Act & Assert
        await Assert.ThrowsAsync<TemplateNotFoundException>(
            () => _renderer.RenderAsync("nonexistent.html", context).AsTask());
    }

    [Fact]
    public async Task RenderAsync_WithPartial_RendersIncludedTemplate()
    {
        // Arrange - 直接在模板目录创建 partial
        File.WriteAllText(Path.Combine(_tempDir, "header.html"), "<header>{{ site.title }}</header>");
        var templateContent = "{{ include 'header.html' }}<main>{{ page.content }}</main>";
        File.WriteAllText(Path.Combine(_tempDir, "main.html"), templateContent);
        var context = CreateTestContext(content: "<p>Content</p>");

        // Act
        var result = await _renderer.RenderAsync("main.html", context);

        // Assert
        Assert.Equal("<header>Test Site</header><main><p>Content</p></main>", result);
    }

    [Fact]
    public void ClearCache_ClearsAllCachedTemplates()
    {
        // Arrange
        File.WriteAllText(Path.Combine(_tempDir, "cached.html"), "original");
        _ = _renderer.RenderAsync("cached.html", CreateTestContext()).AsTask().Result;

        // 修改模板内容
        File.WriteAllText(Path.Combine(_tempDir, "cached.html"), "modified");

        // Act
        _renderer.ClearCache();
        var result = _renderer.RenderAsync("cached.html", CreateTestContext()).AsTask().Result;

        // Assert
        Assert.Equal("modified", result);
    }

    [Fact]
    public void InvalidateTemplate_RemovesSpecificTemplateFromCache()
    {
        // Arrange
        File.WriteAllText(Path.Combine(_tempDir, "specific.html"), "original");
        _ = _renderer.RenderAsync("specific.html", CreateTestContext()).AsTask().Result;

        // 修改模板内容
        File.WriteAllText(Path.Combine(_tempDir, "specific.html"), "modified");

        // Act
        _renderer.InvalidateTemplate("specific.html");
        var result = _renderer.RenderAsync("specific.html", CreateTestContext()).AsTask().Result;

        // Assert
        Assert.Equal("modified", result);
    }

    [Fact]
    public async Task RenderAsync_ThemeLayoutFallback_UsedWhenSiteMissing()
    {
        // Arrange - 站点 layouts 无 single，主题目录有
        var themeDir = Path.Combine(_tempDir, "themes", "mytheme", "layouts");
        Directory.CreateDirectory(themeDir);
        File.WriteAllText(Path.Combine(themeDir, "single.html"), "THEME {{ page.title }}");
        var renderer = new ScribanTemplateRenderer(_tempDir, "https://example.com", themeDir);

        // Act
        var result = await renderer.RenderAsync("single", CreateTestContext("FromTheme"));

        // Assert
        Assert.Contains("THEME FromTheme", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RenderAsync_SiteLayoutOverridesTheme()
    {
        // Arrange - 站点与主题都有 single：站点优先（Hugo 先挂载者赢）
        var themeDir = Path.Combine(_tempDir, "themes", "mytheme", "layouts");
        Directory.CreateDirectory(themeDir);
        File.WriteAllText(Path.Combine(themeDir, "single.html"), "THEME {{ page.title }}");
        File.WriteAllText(Path.Combine(_tempDir, "single.html"), "SITE {{ page.title }}");
        var renderer = new ScribanTemplateRenderer(_tempDir, "https://example.com", themeDir);

        // Act
        var result = await renderer.RenderAsync("single", CreateTestContext("Both"));

        // Assert
        Assert.Contains("SITE Both", result, StringComparison.Ordinal);
        Assert.DoesNotContain("THEME", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Include_ThemePartialFallback_ResolvesFromTheme()
    {
        // Arrange - partials 只在主题目录：站点模板 include 它应回退解析
        var themeDir = Path.Combine(_tempDir, "themes", "mytheme", "layouts");
        Directory.CreateDirectory(Path.Combine(themeDir, "partials"));
        File.WriteAllText(Path.Combine(themeDir, "partials", "badge.html"), "BADGE");
        File.WriteAllText(Path.Combine(_tempDir, "page.html"), "[{{ include \"badge\" }}]");
        var renderer = new ScribanTemplateRenderer(_tempDir, "https://example.com", themeDir);

        // Act
        var result = await renderer.RenderAsync("page", CreateTestContext());

        // Assert
        Assert.Contains("[BADGE]", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PartialCached_SameVariants_CachesResult()
    {
        // Arrange - partial 记录求值序号：同 variants 第二次调用必须命中缓存
        var partialDir = Path.Combine(_tempDir, "partials");
        Directory.CreateDirectory(partialDir);
        File.WriteAllText(Path.Combine(partialDir, "counter.html"), "CACHED-CONTENT");
        File.WriteAllText(
            Path.Combine(_tempDir, "page.html"),
            "[{{ partialcached \"counter\" \"v1\" }}][{{ partialcached \"counter\" \"v1\" }}]");
        var renderer = new ScribanTemplateRenderer(_tempDir, "https://example.com");

        // Act
        var result = await renderer.RenderAsync("page", CreateTestContext());

        // Assert - 两次调用输出一致（结果被缓存）
        Assert.Contains("[CACHED-CONTENT][CACHED-CONTENT]", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PartialCached_DifferentVariants_CacheSeparately()
    {
        // Arrange - partial 内经 variants 数组访问点参数；不同 variants 各自缓存
        var partialDir = Path.Combine(_tempDir, "partials");
        Directory.CreateDirectory(partialDir);
        File.WriteAllText(Path.Combine(partialDir, "labeled.html"), "LBL{{ variants[0] }}");
        File.WriteAllText(
            Path.Combine(_tempDir, "page.html"),
            "[{{ partialcached \"labeled\" \"x\" }}][{{ partialcached \"labeled\" \"y\" }}]");
        var renderer = new ScribanTemplateRenderer(_tempDir, "https://example.com");

        // Act
        var result = await renderer.RenderAsync("page", CreateTestContext());

        // Assert - 不同 variants 各自渲染，不串缓存
        Assert.Contains("[LBLx][LBLy]", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PartialCached_ClearCache_InvalidatesResult()
    {
        // Arrange
        var partialDir = Path.Combine(_tempDir, "partials");
        Directory.CreateDirectory(partialDir);
        File.WriteAllText(Path.Combine(partialDir, "note.html"), "OLD");
        File.WriteAllText(Path.Combine(_tempDir, "page.html"), "[{{ partialcached \"note\" }}]");
        var renderer = new ScribanTemplateRenderer(_tempDir, "https://example.com");
        var first = await renderer.RenderAsync("page", CreateTestContext());
        Assert.Contains("[OLD]", first, StringComparison.Ordinal);

        // Act - 改模板内容 + 清缓存（dev server 增量路径）
        File.WriteAllText(Path.Combine(partialDir, "note.html"), "NEW");
        renderer.ClearCache();

        // Assert
        var second = await renderer.RenderAsync("page", CreateTestContext());
        Assert.Contains("[NEW]", second, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PartialCached_PartialFileModified_InvalidatesWithoutClearCache()
    {
        // 长驻渲染器（dev server）：partial 变化而主模板未变时结果缓存必须失效——
        // 回归防护：缓存条目曾按渲染器生命周期存活且无 mtime 感知，
        // 改 partial 源文件后页面仍渲染旧输出
        var partialDir = Path.Combine(_tempDir, "partials");
        Directory.CreateDirectory(partialDir);
        var partialPath = Path.Combine(partialDir, "note.html");
        File.WriteAllText(partialPath, "OLD");
        File.WriteAllText(Path.Combine(_tempDir, "page.html"), "[{{ partialcached \"note\" }}]");
        var renderer = new ScribanTemplateRenderer(_tempDir, "https://example.com");
        var first = await renderer.RenderAsync("page", CreateTestContext());
        Assert.Contains("[OLD]", first, StringComparison.Ordinal);

        // Act - 仅改 partial（主模板 page.html 不动），mtime 前移保证变化可见
        File.WriteAllText(partialPath, "NEW");
        File.SetLastWriteTimeUtc(partialPath, DateTime.UtcNow.AddSeconds(2));
        // mtime 查询有 50ms 短窗缓存（热路径 stat 减频），变化最多延迟一个
        // 窗口被发现——等待跨过窗口对齐该语义
        Thread.Sleep(60);

        var second = await renderer.RenderAsync("page", CreateTestContext());

        Assert.Contains("[NEW]", second, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HtmlEscape_ShouldEncodeUntrustedContent()
    {
        // 不可信内容显式转义（HTML 转义契约：Flint 不自动转义模板输出）
        File.WriteAllText(Path.Combine(_tempDir, "esc.html"),
            "{{ html.escape \"<script>alert(1)</script>\" }}");
        var renderer = new ScribanTemplateRenderer(_tempDir, "https://example.com");

        var result = await renderer.RenderAsync("esc", CreateTestContext());

        Assert.Contains("&lt;script&gt;", result, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HtmlStrip_ShouldRemoveMarkup()
    {
        // Scriban 内置 html.strip 剥标签（html.escape 同为内置；转义契约见 README）
        File.WriteAllText(Path.Combine(_tempDir, "strip.html"),
            "{{ html.strip \"<p>Hello</p>\" }}");
        var renderer = new ScribanTemplateRenderer(_tempDir, "https://example.com");

        var result = await renderer.RenderAsync("strip", CreateTestContext());

        Assert.Contains("Hello", result, StringComparison.Ordinal);
        Assert.DoesNotContain("<p>", result, StringComparison.Ordinal);
    }
}
