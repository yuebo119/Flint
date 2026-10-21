// Flint 性能测试
// Fluid 渲染路径细粒度基准（2026-10-21）：精确测量 Fluid 渲染的各个组件
// （成员访问/过滤器/循环/include/条件），找出可优化的热点。
//
// 运行（必须 Release）：
//   dotnet run -c Release --project tests/Flint.PerformanceTests -- bench --filter *FluidComponentBenchmarks*

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using Flint.Core.Abstractions;
using Flint.Core.Configuration;
using Flint.Core.Templates;

namespace Flint.PerformanceTests;

/// <summary>Fluid 渲染路径组件级基准</summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class FluidComponentBenchmarks
{
    private readonly string _dir;
    private readonly FluidTemplateRenderer _fluid;
    private readonly TemplateContext _context;

    public FluidComponentBenchmarks()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"Flint-bdn-fluid-comp-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);

        // 1. 纯插值（成员访问基线）
        File.WriteAllText(Path.Combine(_dir, "interp.liquid"),
            "<h1>{{ page.title }}</h1><div>{{ page.content }}</div>");

        // 2. snake_case 成员映射
        File.WriteAllText(Path.Combine(_dir, "snake.liquid"),
            "<span>{{ page.word_count }}</span><span>{{ page.rel_permalink }}</span>");

        // 3. 过滤器链
        File.WriteAllText(Path.Combine(_dir, "filter.liquid"),
            "{{ page.title | upcase | truncate: 20 }}");

        // 4. Hugo 名过滤器
        File.WriteAllText(Path.Combine(_dir, "hugo_filter.liquid"),
            "{{ page.title | humanize | titleize }}");

        // 5. 循环 + 成员访问
        File.WriteAllText(Path.Combine(_dir, "loop.liquid"),
            "{% for p in site.regular_pages %}<a href=\"{{ p.permalink }}\">{{ p.title }}</a>{% endfor %}");

        // 6. 条件分支
        File.WriteAllText(Path.Combine(_dir, "cond.liquid"),
            "{% if page.title %}<h1>{{ page.title }}</h1>{% else %}<h1>Untitled</h1>{% endif %}");

        // 7. where 过滤器（集合过滤）
        File.WriteAllText(Path.Combine(_dir, "where.liquid"),
            "{% assign filtered = pages | where: 'word_count', '>', '50' %}{{ filtered.size }}");

        // 8. assign + 计算
        File.WriteAllText(Path.Combine(_dir, "assign.liquid"),
            "{% assign wc = page.word_count | plus: 10 %}{{ wc }}");

        _fluid = new FluidTemplateRenderer(_dir);
        _context = CreateContext();
    }

    private static TemplateContext CreateContext()
    {
        var pages = new List<PageContext>();
        for (var i = 0; i < 10; i++)
        {
            pages.Add(new PageContext
            {
                Title = $"文章 {i}", Content = $"<p>内容 {i}</p>",
                Permalink = $"https://example.com/p{i}/", RelPermalink = $"/p{i}/",
                Date = DateTimeOffset.UnixEpoch, Tags = [], Categories = [],
                WordCount = 100 + i * 10, ReadingTime = TimeSpan.FromMinutes(1)
            });
        }
        return new TemplateContext
        {
            Page = new PageContext
            {
                Title = "基准页面", Content = "<p>基准内容</p>",
                Permalink = "https://example.com/bench/", RelPermalink = "/bench/",
                Date = DateTimeOffset.UnixEpoch, Tags = ["t"], Categories = [],
                WordCount = 128, ReadingTime = TimeSpan.FromMinutes(1)
            },
            Site = new SiteContext
            {
                Title = "基准站点", BaseURL = "https://example.com", Language = "zh-cn",
                Pages = pages, RegularPages = pages,
                Taxonomies = new TaxonomyCollection { Taxonomies = new Dictionary<string, IReadOnlyList<TaxonomyTerm>>() },
                Menus = new MenuCollection { Menus = new Dictionary<string, IReadOnlyList<MenuItem>>() },
                Config = new SiteConfig { BaseURL = "https://example.com", Title = "基准站点" }
            },
            Pages = pages
        };
    }

    [Benchmark]
    public async Task<string> 插值_成员访问() =>
        await _fluid.RenderAsync("interp", _context);

    [Benchmark]
    public async Task<string> snake_case_成员映射() =>
        await _fluid.RenderAsync("snake", _context);

    [Benchmark]
    public async Task<string> 内置过滤器链() =>
        await _fluid.RenderAsync("filter", _context);

    [Benchmark]
    public async Task<string> Hugo名过滤器() =>
        await _fluid.RenderAsync("hugo_filter", _context);

    [Benchmark]
    public async Task<string> 循环_成员访问() =>
        await _fluid.RenderAsync("loop", _context);

    [Benchmark]
    public async Task<string> 条件分支() =>
        await _fluid.RenderAsync("cond", _context);

    [Benchmark]
    public async Task<string> where_集合过滤() =>
        await _fluid.RenderAsync("where", _context);

    [Benchmark]
    public async Task<string> assign_计算() =>
        await _fluid.RenderAsync("assign", _context);

    ~FluidComponentBenchmarks()
    {
        try
        {
            if (Directory.Exists(_dir))
            {
                Directory.Delete(_dir, recursive: true);
            }
        }
        catch
        {
        }
    }
}
