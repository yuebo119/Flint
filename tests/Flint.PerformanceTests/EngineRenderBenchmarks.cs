// Flint 性能测试
// 双引擎渲染 BenchmarkDotNet 基准（FLUID-MIGRATION-PLAN P6-1）：
// 同逻辑模板分别经 Scriban（.html）与 Fluid（.liquid）渲染，BDN 口径
// （预热 + 统计 + 内存诊断）。**模板级**基准——纯内存渲染无 IO，
// 是 BDN 的适用域；站点级端到端 A/B（构建含重 IO）是 BDN 反模式，
// 走 scripts/engine-ab.sh 的交错三轮中位数（EPSL 站点级口径），
// 两个口径都进 perf 报告与 LIQUID-GUIDE §5。
//
// 运行（必须 Release——BDN 在 Debug 下拒绝运行）：
//   dotnet run -c Release --project tests/Flint.PerformanceTests -- bench --filter *EngineRenderBenchmarks*

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using Flint.Core.Abstractions;
using Flint.Core.Configuration;
using Flint.Core.Templates;

namespace Flint.PerformanceTests;

/// <summary>双引擎模板渲染基准（Scriban vs Fluid，同逻辑模板）</summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class EngineRenderBenchmarks
{
    private readonly string _dir;
    private readonly ScribanTemplateRenderer _scriban;
    private readonly FluidTemplateRenderer _fluid;
    private readonly TemplateContext _context;

    public EngineRenderBenchmarks()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"Flint-bdn-engine-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
        // 同逻辑模板：页面标题 + 正文 + 成员访问（word_count 走 snake_case 映射）
        File.WriteAllText(Path.Combine(_dir, "test.html"),
            "<h1>{{ page.title }}</h1><div>{{ page.content }}</div><span>{{ page.word_count }}</span>");
        File.WriteAllText(Path.Combine(_dir, "test.liquid"),
            "<h1>{{ page.title }}</h1><div>{{ page.content }}</div><span>{{ page.word_count }}</span>");

        _scriban = new ScribanTemplateRenderer(_dir);
        _fluid = new FluidTemplateRenderer(_dir);
        _context = CreateContext();
    }

    private static TemplateContext CreateContext() =>
        new()
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
                Pages = [], RegularPages = [],
                Taxonomies = new TaxonomyCollection { Taxonomies = new Dictionary<string, IReadOnlyList<TaxonomyTerm>>() },
                Menus = new MenuCollection { Menus = new Dictionary<string, IReadOnlyList<MenuItem>>() },
                Config = new SiteConfig { BaseURL = "https://example.com", Title = "基准站点" }
            }
        };

    /// <summary>Scriban 渲染（.html 模板）</summary>
    [Benchmark(Baseline = true)]
    public async Task<string> ScribanRender() =>
        await _scriban.RenderAsync("test", _context);

    /// <summary>Fluid 渲染（.liquid 模板）</summary>
    [Benchmark]
    public async Task<string> FluidRender() =>
        await _fluid.RenderAsync("test", _context);

    /// <summary>清理临时目录（BDN 不调用 Dispose，析构兜底）</summary>
    ~EngineRenderBenchmarks()
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
            // 清理失败不影响基准结论
        }
    }
}
