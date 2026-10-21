// Flint 静态站点生成器
// 审计修复回归测试（2026-10-21 全面审计轮）：
// 1. router 懒构造的并发安全（Lazy<T>，并行渲染只构造一个 Scriban 实例）
// 2. include 路径/文本缓存（命中免重复 stat 与读盘，构建边界失效）
// 3. resource_concat 的字符串路径实参

using Flint.Core.Abstractions;
using Flint.Core.Configuration;
using Flint.Core.Templates;
using Xunit;

namespace Flint.Core.Tests.Templates;

/// <summary>router 懒构造并发安全（P2-4 竞态修复回归）</summary>
public sealed class RouterLazyConstructionTests : IDisposable
{
    private readonly string _tempDir;

    public RouterLazyConstructionTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"Flint_lazy_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        Directory.CreateDirectory(Path.Combine(_tempDir, "layouts"));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch
        {
            // 忽略清理错误
        }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task 并发触碰_ScribanRenderer_只构造一个实例()
    {
        var constructCount = 0;
        var router = new TemplateEngineRouter(
            new FluidTemplateRenderer(Path.Combine(_tempDir, "layouts"), "https://example.com"),
            () =>
            {
                Interlocked.Increment(ref constructCount);
                return new ScribanTemplateRenderer(Path.Combine(_tempDir, "layouts"), "https://example.com");
            },
            TemplateEnginePolicy.Auto);

        Assert.False(router.IsScribanConstructed);

        // 32 个并发任务同时触碰 ScribanRenderer（模拟并行渲染）
        var tasks = Enumerable.Range(0, 32)
            .Select(_ => Task.Run(() =>
            {
                var r = router.ScribanRenderer;
                Assert.NotNull(r);
            }))
            .ToArray();
        await Task.WhenAll(tasks);

        Assert.True(router.IsScribanConstructed);
        // Lazy ExecutionAndPublication：工厂只跑一次
        Assert.Equal(1, constructCount);
    }

    [Fact]
    public void 纯_Fluid_站点不触碰则不构造()
    {
        // 纯 Fluid 站点 = 有 .liquid 模板、无 .html 模板
        File.WriteAllText(Path.Combine(_tempDir, "layouts", "index.liquid"), "<h1>{{ site.title }}</h1>");

        var constructCount = 0;
        var router = new TemplateEngineRouter(
            new FluidTemplateRenderer(Path.Combine(_tempDir, "layouts"), "https://example.com"),
            () =>
            {
                Interlocked.Increment(ref constructCount);
                return new ScribanTemplateRenderer(Path.Combine(_tempDir, "layouts"), "https://example.com");
            },
            TemplateEnginePolicy.Auto);

        // 只查 Fluid 侧能力（纯 Fluid 站点的真实访问模式）
        _ = router.FluidRenderer;
        _ = router.TemplateExists("index");

        Assert.False(router.IsScribanConstructed);
        Assert.Equal(0, constructCount);
    }
}

/// <summary>Fluid include 缓存行为（审计修复回归）</summary>
public sealed class FluidIncludeCacheTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FluidTemplateRenderer _renderer;

    public FluidIncludeCacheTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"Flint_inc_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        Directory.CreateDirectory(Path.Combine(_tempDir, "layouts", "partials"));
        _renderer = new FluidTemplateRenderer(Path.Combine(_tempDir, "layouts"), "https://example.com");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch
        {
            // 忽略清理错误
        }
        GC.SuppressFinalize(this);
    }

    private static TemplateContext CreateContext() =>
        new()
        {
            Page = new PageContext
            {
                Title = "T", Content = "", Permalink = "https://example.com/t/",
                RelPermalink = "/t/", Date = DateTimeOffset.UnixEpoch, Tags = [], Categories = [],
                WordCount = 1, ReadingTime = TimeSpan.Zero
            },
            Site = new SiteContext
            {
                Title = "S", BaseURL = "https://example.com", Language = "zh-cn",
                Pages = [], RegularPages = [],
                Taxonomies = new TaxonomyCollection { Taxonomies = new Dictionary<string, IReadOnlyList<TaxonomyTerm>>() },
                Menus = new MenuCollection { Menus = new Dictionary<string, IReadOnlyList<MenuItem>>() },
                Config = new SiteConfig { BaseURL = "https://example.com", Title = "S" }
            }
        };

    private async Task<string> RenderAsync(string template)
    {
        var file = Path.Combine(_tempDir, "t.liquid");
        await File.WriteAllTextAsync(file, template).ConfigureAwait(false);
        return await _renderer.RenderTemplateFileAsync(file, CreateContext()).ConfigureAwait(false);
    }

    [Fact]
    public async Task include_多次渲染结果一致_且缓存不破坏正确性()
    {
        File.WriteAllText(Path.Combine(_tempDir, "layouts", "partials", "head.liquid"),
            "<head>{{ site.title }}</head>");

        // 同一模板渲染多次（缓存命中路径）
        for (var i = 0; i < 5; i++)
        {
            var result = await RenderAsync("{% include 'partials/head' %}");
            Assert.Equal("<head>S</head>", result);
        }
    }

    [Fact]
    public async Task include_内容变更_InvalidateMtimeCache_后生效()
    {
        var partial = Path.Combine(_tempDir, "layouts", "partials", "head.liquid");
        File.WriteAllText(partial, "<head>v1</head>");

        Assert.Equal("<head>v1</head>", await RenderAsync("{% include 'partials/head' %}"));

        // 改 partial 内容（serve 热更新语义：构建边界失效后必须看到新内容）
        await Task.Delay(50);
        File.WriteAllText(partial, "<head>v2</head>");
        _renderer.InvalidateMtimeCache();

        Assert.Equal("<head>v2</head>", await RenderAsync("{% include 'partials/head' %}"));
    }

    [Fact]
    public async Task include_未命中仍抛错_缓存不掩盖缺失()
    {
        await Assert.ThrowsAsync<TemplateRenderException>(
            () => RenderAsync("{% include 'partials/missing' %}"));
    }
}

/// <summary>resource_concat 字符串路径实参（审计修复回归）</summary>
public sealed class ResourceConcatPathTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FluidTemplateRenderer _renderer;

    public ResourceConcatPathTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"Flint_concat_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        Directory.CreateDirectory(Path.Combine(_tempDir, "assets", "css"));
        File.WriteAllText(Path.Combine(_tempDir, "assets", "css", "a.css"), "AAA");
        File.WriteAllText(Path.Combine(_tempDir, "assets", "css", "b.css"), "BBB");
        var provider = new FileSystemResourceProvider(
            "https://example.com", [Path.Combine(_tempDir, "assets")]);
        _renderer = new FluidTemplateRenderer(
            _tempDir, "https://example.com", builtins: null, resources: provider);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch
        {
            // 忽略清理错误
        }
        GC.SuppressFinalize(this);
    }

    private static TemplateContext CreateContext() =>
        new()
        {
            Page = new PageContext
            {
                Title = "T", Content = "", Permalink = "https://example.com/t/",
                RelPermalink = "/t/", Date = DateTimeOffset.UnixEpoch, Tags = [], Categories = [],
                WordCount = 1, ReadingTime = TimeSpan.Zero
            },
            Site = new SiteContext
            {
                Title = "S", BaseURL = "https://example.com", Language = "zh-cn",
                Pages = [], RegularPages = [],
                Taxonomies = new TaxonomyCollection { Taxonomies = new Dictionary<string, IReadOnlyList<TaxonomyTerm>>() },
                Menus = new MenuCollection { Menus = new Dictionary<string, IReadOnlyList<MenuItem>>() },
                Config = new SiteConfig { BaseURL = "https://example.com", Title = "S" }
            }
        };

    [Fact]
    public async Task concat_字符串路径实参_经提供者解析并拼接()
    {
        var file = Path.Combine(_tempDir, "t.liquid");
        await File.WriteAllTextAsync(file,
            "{{ 'css/a.css' | resource_concat: 'css/b.css', target: 'all.css' | jsonify }}");
        var result = await _renderer.RenderTemplateFileAsync(file, CreateContext());

        // 产物名与拼接内容（AAABBB）都在 jsonify 输出里
        Assert.Contains("all.css", result);
        Assert.Contains("AAABBB", result);
    }
}
