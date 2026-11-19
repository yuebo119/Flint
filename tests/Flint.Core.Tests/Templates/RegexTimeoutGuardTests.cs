// Flint 静态站点生成器
// 2026-11-20 R1 审计修复锁定（HYG-J01/J02）：replaceRE/findRE 用户可控正则
// 统一 2 秒超时。修复前 .NET 默认 InfiniteMatchTimeout，病态 pattern
//（如 (a+)+$ 配 30 个 a）单线程回溯挂死构建；修复后超时抛
// RegexMatchTimeoutException 按模板错误收编（传播或收编均证明未挂死）。

using System.Diagnostics;
using Flint.Core.Abstractions;
using Flint.Core.Configuration;
using Flint.Core.Models;
using Flint.Core.Templates;
using Xunit;

namespace Flint.Core.Tests.Templates;

public sealed class RegexTimeoutGuardTests : IDisposable
{
    private readonly string _siteDir;
    private readonly ScribanTemplateRenderer _scriban;
    private readonly FluidTemplateRenderer _fluid;
    private int _tplSeq;

    public RegexTimeoutGuardTests()
    {
        _siteDir = Path.Combine(Path.GetTempPath(), $"Flint_retimeout_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_siteDir, "layouts"));
        _scriban = new ScribanTemplateRenderer(
            Path.Combine(_siteDir, "layouts"), "https://example.com");
        _fluid = new FluidTemplateRenderer(
            Path.Combine(_siteDir, "layouts"), "https://example.com");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_siteDir))
            {
                Directory.Delete(_siteDir, recursive: true);
            }
        }
        catch
        {
            // 忽略清理错误
        }
        GC.SuppressFinalize(this);
    }

    /// <summary>渲染一段 Scriban 模板串（唯一模板名避开加载器 mtime TTL 缓存）</summary>
    private async Task<string> RenderScribanAsync(string snippet)
    {
        var name = $"t{++_tplSeq}";
        File.WriteAllText(Path.Combine(_siteDir, "layouts", name + ".html"), snippet);
        return (await _scriban.RenderAsync(name, CreateContext()).ConfigureAwait(false)).Trim();
    }

    private async Task<string> RenderFluidAsync(string snippet)
    {
        var name = $"t{++_tplSeq}";
        File.WriteAllText(Path.Combine(_siteDir, "layouts", name + ".liquid"), snippet);
        return (await _fluid.RenderAsync(name, CreateContext()).ConfigureAwait(false)).Trim();
    }

    private static TemplateContext CreateContext() => new()
    {
        Page = new PageContext
        {
            Title = "正则页",
            Content = "<p>x</p>",
            Permalink = "https://example.com/p/",
            RelPermalink = "/p/",
            Tags = [],
            Categories = [],
            Date = DateTimeOffset.UtcNow,
            ReadingTime = TimeSpan.FromMinutes(1),
            WordCount = 10,
        },
        Site = new SiteContext
        {
            Title = "正则站",
            BaseURL = "https://example.com",
            Language = "zh-cn",
            Pages = [],
            RegularPages = [],
            Taxonomies = new TaxonomyCollection { Taxonomies = new Dictionary<string, IReadOnlyList<TaxonomyTerm>>() },
            Menus = new MenuCollection { Menus = new Dictionary<string, IReadOnlyList<MenuItem>>() },
            Data = new Dictionary<string, object>(),
            Params = new Dictionary<string, object>(),
            Translations = new Dictionary<string, string>(),
            PaginatorPages = [],
            Config = new SiteConfig { BaseURL = "https://example.com", Title = "正则站" },
        },
    };

    [Fact]
    public async Task Scriban_replaceRE_病态pattern_超时生效不挂死()
    {
        var sw = Stopwatch.StartNew();
        try
        {
            // 30 个 a 的不匹配输入：无超时的回溯是指数级（修复前挂死 30s+）
            await RenderScribanAsync(
                "{{ replaceRE \"(a+)+$\" \"x\" \"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaa!\" }}");
        }
        catch
        {
            // 异常传播路径同样证明超时生效（正则超时按模板错误收编或抛出）
        }
        Assert.True(sw.ElapsedMilliseconds < 10_000, $"病态 pattern 耗时 {sw.ElapsedMilliseconds}ms，超时未生效");
    }

    [Fact]
    public async Task Fluid_replaceRE_病态pattern_超时生效不挂死()
    {
        var sw = Stopwatch.StartNew();
        try
        {
            await RenderFluidAsync(
                "{{ \"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaa!\" | replaceRE: \"(a+)+$\", \"x\" }}");
        }
        catch
        {
            // 同上：传播即证明
        }
        Assert.True(sw.ElapsedMilliseconds < 10_000, $"病态 pattern 耗时 {sw.ElapsedMilliseconds}ms，超时未生效");
    }

    [Fact]
    public async Task Scriban_replaceRE_正常pattern_行为不变()
    {
        var out_ = await RenderScribanAsync("{{ replaceRE \"b+\" \"x\" \"abbbc\" }}");
        Assert.Equal("axc", out_);
    }

    [Fact]
    public async Task Scriban_find_re_正常pattern_行为不变()
    {
        var out_ = await RenderScribanAsync("{{ (find_re \"\\\\d+\" \"a12b34\").size }}");
        Assert.Equal("2", out_);
    }

    [Fact]
    public async Task Fluid_findRE_正常pattern_行为不变()
    {
        // Fluid 3.0 双引号串保留反斜杠原样（\\d 不会被解释为 \d），pattern 改单引号直书
        var out_ = await RenderFluidAsync("{{ 'a12b34' | find_re: '\\d+' | size }}");
        Assert.Equal("2", out_);
    }
}
