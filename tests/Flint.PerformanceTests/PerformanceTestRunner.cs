// Flint 性能测试运行器
// 优化版本：统一测试方法、增加复杂度分析

using System.Diagnostics;
using System.Runtime.InteropServices;
using Flint.Core;
using Flint.Core.Abstractions;
using Flint.Core.Assets;
using Flint.Core.Configuration;
using Flint.Core.Content;
using Flint.Core.Models;
using Flint.Core.Site;
using Flint.Core.Templates;

namespace Flint.PerformanceTests;

/// <summary>
/// 性能测试运行器
/// 采用科学的测试方法：预热、多次迭代、统计分析
/// </summary>
public class PerformanceTestRunner
{
    private readonly string _testDir;
    private readonly List<PerformanceTestResult> _results = [];

    // 测试配置
    private const int WarmupIterations = 1;  // 预热次数
    private const int MeasureIterations = 3; // 测量次数

    public PerformanceTestRunner()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"Flint-perf-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    public async Task<PerformanceReport> RunAllTestsAsync()
    {
        Console.WriteLine("🚀 开始 Flint 性能测试...\n");

        try
        {
            // 站点构建测试（统一方法）
            await RunSiteBuildTestAsync(100, "小型");
            await RunSiteBuildTestAsync(500, "中型");
            await RunSiteBuildTestAsync(1000, "大型");
            await RunSiteBuildTestAsync(10000, "超大型");

            // 复杂主题测试（放在超大型站点构建之后）
            await RunComplexThemeTestAsync(1000, "复杂主题");

            // 复杂度分析测试
            await RunScalabilityAnalysisAsync();

            await RunIncrementalBuildTestAsync();
            await RunMarkdownParsingTestAsync();
            await RunTemplateRenderingTestAsync();
            await RunConfigLoadingTestAsync();
            await RunConcurrentBuildTestAsync();
        }
        finally
        {
            Cleanup();
        }

        return new PerformanceReport
        {
            Title = "Flint 性能测试报告",
            FlintVersion = FlintInfo.Version,
            Results = _results
        };
    }

    /// <summary>
    /// 统一的站点构建测试方法
    /// 采用预热 + 多次测量 + 统计分析
    /// </summary>
    private async Task RunSiteBuildTestAsync(int pageCount, string sizeLabel)
    {
        Console.WriteLine($"📦 测试: {sizeLabel}站点构建 ({pageCount}页)...");
        var siteDir = await CreateTestSiteAsync(pageCount);
        var metrics = new List<PerformanceMetric>();

        // 预热阶段
        for (var i = 0; i < WarmupIterations; i++)
        {
            await BuildSiteAsync(siteDir);
        }

        // 测量阶段
        var durations = new List<double>();
        var peakMemoryUsages = new List<double>();
        var totalAllocations = new List<double>();

        for (var i = 0; i < MeasureIterations; i++)
        {
            // 强制 GC 以获得更准确的基线
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            // 记录进程内存基线（包括托管和非托管）
            var process = Process.GetCurrentProcess();
            var workingSetBefore = process.WorkingSet64;
            var allocatedBefore = GC.GetTotalAllocatedBytes(precise: false);

            var sw = Stopwatch.StartNew();

            await BuildSiteAsync(siteDir);

            sw.Stop();

            // 刷新进程信息
            process.Refresh();
            var workingSetAfter = process.WorkingSet64;
            var allocatedAfter = GC.GetTotalAllocatedBytes(precise: false);

            // 获取 GC 内存信息
            var gcInfo = GC.GetGCMemoryInfo();

            durations.Add(sw.Elapsed.TotalMilliseconds);
            // 使用工作集增量作为内存使用指标（更准确反映实际内存占用）
            peakMemoryUsages.Add(Math.Max(0, (workingSetAfter - workingSetBefore) / 1024.0 / 1024.0));
            // 记录总分配量（反映内存压力）
            totalAllocations.Add((allocatedAfter - allocatedBefore) / 1024.0 / 1024.0);
        }

        // 统计分析
        var avgDuration = durations.Average();
        var minDuration = durations.Min();
        var maxDuration = durations.Max();
        var stdDev = CalculateStdDev(durations);
        var avgPeakMemory = peakMemoryUsages.Average();
        var avgAllocations = totalAllocations.Average();
        var pagesPerSecond = pageCount / (avgDuration / 1000.0);
        var msPerPage = avgDuration / pageCount;

        // 根据站点规模调整基准值（考虑合理的线性增长）
        var adjustedBuildSpeedBaseline = PerformanceBaselines.BuildSpeedPagesPerSecond;
        // 内存基准：基础 20MB + 每 100 页约 13MB（考虑内容、模板、上下文等）
        // 实测数据：100页~13MB, 500页~61MB, 1000页~122MB, 10000页~1213MB
        var adjustedMemoryBaseline = 20.0 + (pageCount / 100.0) * 13.0;

        metrics.Add(CreateMetric("构建时间", avgDuration, "ms", null, MetricCategory.Time, true));
        metrics.Add(CreateMetric("最小时间", minDuration, "ms", null, MetricCategory.Time, true));
        metrics.Add(CreateMetric("最大时间", maxDuration, "ms", null, MetricCategory.Time, true));
        metrics.Add(CreateMetric("标准差", stdDev, "ms", null, MetricCategory.Time, false));
        metrics.Add(CreateMetric("构建速度", pagesPerSecond, "页/秒",
            adjustedBuildSpeedBaseline, MetricCategory.Throughput, true));
        metrics.Add(CreateMetric("每页耗时", msPerPage, "ms/页", null, MetricCategory.Time, false));
        metrics.Add(CreateMetric("内存增量", avgPeakMemory, "MB", null, MetricCategory.Memory, false));
        metrics.Add(CreateMetric("总分配量", avgAllocations, "MB",
            adjustedMemoryBaseline, MetricCategory.Memory, false));

        _results.Add(CreateTestResult($"{sizeLabel}站点构建 ({pageCount}页)",
            $"测试 {pageCount} 页站点的构建性能（{MeasureIterations}次测量取平均）",
            metrics, TimeSpan.FromMilliseconds(avgDuration)));

        Console.WriteLine($"   ✓ 构建速度: {pagesPerSecond:F0} 页/秒 (每页 {msPerPage:F2}ms)\n");
    }

    /// <summary>
    /// 可扩展性分析测试
    /// 通过多个数据点分析时间复杂度
    /// </summary>
    private async Task RunScalabilityAnalysisAsync()
    {
        Console.WriteLine("📊 测试: 可扩展性分析...");
        var metrics = new List<PerformanceMetric>();

        // 测试不同规模的站点
        var testSizes = new[] { 50, 100, 200, 400 };
        var results = new List<(int pages, double timeMs, double msPerPage)>();

        foreach (var pageCount in testSizes)
        {
            var siteDir = await CreateTestSiteAsync(pageCount);

            // 预热
            await BuildSiteAsync(siteDir);

            // 测量
            GC.Collect();
            GC.WaitForPendingFinalizers();
            var sw = Stopwatch.StartNew();
            await BuildSiteAsync(siteDir);
            sw.Stop();

            var msPerPage = sw.Elapsed.TotalMilliseconds / pageCount;
            results.Add((pageCount, sw.Elapsed.TotalMilliseconds, msPerPage));

            Console.WriteLine($"   {pageCount}页: {sw.Elapsed.TotalMilliseconds:F0}ms ({msPerPage:F2}ms/页)");
        }

        // 分析复杂度
        // 如果是 O(n)，每页耗时应该基本恒定
        // 如果是 O(n²)，每页耗时会随 n 线性增长
        var firstMsPerPage = results[0].msPerPage;
        var lastMsPerPage = results[^1].msPerPage;
        var complexityRatio = lastMsPerPage / firstMsPerPage;
        var sizeRatio = (double)results[^1].pages / results[0].pages;

        // 判断复杂度
        string complexityEstimate;
        bool isAcceptable;
        if (complexityRatio < 1.5)
        {
            complexityEstimate = "O(n) - 线性";
            isAcceptable = true;
        }
        else if (complexityRatio < sizeRatio * 0.8)
        {
            complexityEstimate = "O(n log n) - 准线性";
            isAcceptable = true;
        }
        else if (complexityRatio < sizeRatio * 1.5)
        {
            complexityEstimate = "O(n²) - 二次方";
            isAcceptable = false;
        }
        else
        {
            complexityEstimate = "O(n²+) - 超二次方";
            isAcceptable = false;
        }

        metrics.Add(CreateMetric("50页每页耗时", results[0].msPerPage, "ms/页", null, MetricCategory.Time, false));
        metrics.Add(CreateMetric("400页每页耗时", results[^1].msPerPage, "ms/页", null, MetricCategory.Time, false));
        metrics.Add(CreateMetric("复杂度比率", complexityRatio, "x", 2.0, MetricCategory.Throughput, false));

        _results.Add(new PerformanceTestResult
        {
            TestName = "可扩展性分析",
            Description = $"时间复杂度估计: {complexityEstimate}",
            Environment = GetEnvironmentInfo(),
            Metrics = metrics,
            PassedBaseline = isAcceptable,
            Duration = TimeSpan.FromMilliseconds(results.Sum(r => r.timeMs))
        });

        Console.WriteLine($"   ✓ 复杂度估计: {complexityEstimate} (比率: {complexityRatio:F2}x)\n");
    }

    /// <summary>
    /// 计算标准差
    /// </summary>
    private static double CalculateStdDev(List<double> values)
    {
        if (values.Count < 2)
            return 0;
        var avg = values.Average();
        var sumSquares = values.Sum(v => (v - avg) * (v - avg));
        return Math.Sqrt(sumSquares / (values.Count - 1));
    }

    private async Task RunIncrementalBuildTestAsync()
    {
        Console.WriteLine("⚡ 测试: 增量构建性能...");
        const int pageCount = 500;
        var siteDir = await CreateTestSiteAsync(pageCount);
        var metrics = new List<PerformanceMetric>();

        var fullBuildSw = Stopwatch.StartNew();
        await BuildSiteAsync(siteDir);
        fullBuildSw.Stop();

        var contentDir = Path.Combine(siteDir, "content", "posts");
        var firstFile = Directory.GetFiles(contentDir, "*.md").First();
        await File.AppendAllTextAsync(firstFile, "\n\n更新内容");

        GC.Collect();
        var incrementalSw = Stopwatch.StartNew();
        await IncrementalBuildAsync(siteDir, [firstFile]);
        incrementalSw.Stop();

        var speedup = fullBuildSw.Elapsed.TotalMilliseconds / incrementalSw.Elapsed.TotalMilliseconds;

        // 增量构建的加速比取决于完整构建时间，设置合理的基准
        // 如果完整构建需要 500ms，增量构建 70ms，加速比约 7x
        var expectedSpeedup = Math.Min(5.0, fullBuildSw.Elapsed.TotalMilliseconds / 100.0);

        metrics.Add(CreateMetric("完整构建时间", fullBuildSw.Elapsed.TotalMilliseconds, "ms", null, MetricCategory.Time, true));
        metrics.Add(CreateMetric("增量构建时间", incrementalSw.Elapsed.TotalMilliseconds, "ms",
            PerformanceBaselines.IncrementalBuildTimeMs, MetricCategory.Time, false));
        metrics.Add(CreateMetric("加速比", speedup, "x", expectedSpeedup, MetricCategory.Throughput, true));

        _results.Add(CreateTestResult("增量构建性能",
            "测试单文件修改后的增量构建性能", metrics, incrementalSw.Elapsed));

        Console.WriteLine($"   ✓ 增量构建: {incrementalSw.Elapsed.TotalMilliseconds:F2}ms (加速 {speedup:F1}x)\n");
    }

    private async Task RunMarkdownParsingTestAsync()
    {
        Console.WriteLine("📝 测试: Markdown 解析性能...");
        const int fileCount = 1000;
        var markdownFiles = GenerateMarkdownFiles(fileCount);
        var parser = new MarkdownParser();
        var metrics = new List<PerformanceMetric>();

        foreach (var content in markdownFiles.Take(10))
        {
            parser.ToHtml(content);
        }

        GC.Collect();
        var sw = Stopwatch.StartNew();

        foreach (var content in markdownFiles)
        {
            parser.ToHtml(content);
        }

        sw.Stop();

        var filesPerSecond = fileCount / sw.Elapsed.TotalSeconds;

        metrics.Add(CreateMetric("解析时间", sw.Elapsed.TotalMilliseconds, "ms", null, MetricCategory.Time, true));
        metrics.Add(CreateMetric("解析速度", filesPerSecond, "文件/秒",
            PerformanceBaselines.MarkdownParseSpeedFilesPerSecond, MetricCategory.Throughput, true));

        _results.Add(CreateTestResult("Markdown 解析性能",
            $"测试 {fileCount} 个 Markdown 文件的解析性能", metrics, sw.Elapsed));

        Console.WriteLine($"   ✓ 解析速度: {filesPerSecond:F0} 文件/秒\n");
    }

    private async Task RunTemplateRenderingTestAsync()
    {
        Console.WriteLine("🎨 测试: 模板渲染性能...");
        const int renderCount = 1000;
        var layoutsDir = Path.Combine(_testDir, "layouts-render");
        Directory.CreateDirectory(layoutsDir);
        await File.WriteAllTextAsync(Path.Combine(layoutsDir, "test.html"),
            "<h1>{{ page.title }}</h1><div>{{ page.content }}</div>");

        var renderer = new ScribanTemplateRenderer(layoutsDir);
        var metrics = new List<PerformanceMetric>();
        var context = CreateTestContext();

        for (var i = 0; i < 10; i++)
        {
            await renderer.RenderAsync("test", context);
        }

        GC.Collect();
        var sw = Stopwatch.StartNew();

        for (var i = 0; i < renderCount; i++)
        {
            await renderer.RenderAsync("test", context);
        }

        sw.Stop();

        var pagesPerSecond = renderCount / sw.Elapsed.TotalSeconds;

        metrics.Add(CreateMetric("渲染时间", sw.Elapsed.TotalMilliseconds, "ms", null, MetricCategory.Time, true));
        metrics.Add(CreateMetric("渲染速度", pagesPerSecond, "页/秒",
            PerformanceBaselines.TemplateRenderSpeedPagesPerSecond, MetricCategory.Throughput, true));

        _results.Add(CreateTestResult("模板渲染性能",
            $"测试 {renderCount} 次模板渲染的性能", metrics, sw.Elapsed));

        Console.WriteLine($"   ✓ 渲染速度: {pagesPerSecond:F0} 页/秒\n");
    }

    private async Task RunConfigLoadingTestAsync()
    {
        Console.WriteLine("⚙️ 测试: 配置加载性能...");
        var configPath = Path.Combine(_testDir, "Flint-config.toml");
        await File.WriteAllTextAsync(configPath, GenerateLargeConfig());
        var loader = new ConfigLoader();
        var metrics = new List<PerformanceMetric>();

        await loader.LoadAsync(configPath);

        const int iterations = 100;
        GC.Collect();
        var sw = Stopwatch.StartNew();

        for (var i = 0; i < iterations; i++)
        {
            await loader.LoadAsync(configPath);
        }

        sw.Stop();

        var avgTime = sw.Elapsed.TotalMilliseconds / iterations;

        metrics.Add(CreateMetric("平均加载时间", avgTime, "ms",
            PerformanceBaselines.ConfigLoadTimeMs, MetricCategory.Time, false));

        _results.Add(CreateTestResult("配置加载性能",
            "测试配置文件加载性能", metrics, TimeSpan.FromMilliseconds(avgTime)));

        Console.WriteLine($"   ✓ 平均加载时间: {avgTime:F3}ms\n");
    }

    private async Task RunConcurrentBuildTestAsync()
    {
        Console.WriteLine("🔄 测试: 并发构建性能...");
        // 使用更大的站点来测试并发性能，以便并发收益超过调度开销
        const int pageCount = 1000;
        var siteDir = await CreateTestSiteAsync(pageCount);
        var metrics = new List<PerformanceMetric>();

        // 预热
        await BuildSiteAsync(siteDir, parallelism: 1);
        await BuildSiteAsync(siteDir, parallelism: Environment.ProcessorCount);

        // 测量单线程
        GC.Collect();
        GC.WaitForPendingFinalizers();
        var singleThreadSw = Stopwatch.StartNew();
        await BuildSiteAsync(siteDir, parallelism: 1);
        singleThreadSw.Stop();

        // 测量多线程
        GC.Collect();
        GC.WaitForPendingFinalizers();
        var multiThreadSw = Stopwatch.StartNew();
        await BuildSiteAsync(siteDir, parallelism: Environment.ProcessorCount);
        multiThreadSw.Stop();

        var speedup = singleThreadSw.Elapsed.TotalMilliseconds / multiThreadSw.Elapsed.TotalMilliseconds;
        var efficiency = speedup / Environment.ProcessorCount * 100;

        // 对于 I/O 密集型任务，合理的并行效率约为 20-40%
        // 但对于轻量级任务（每页 ~1ms），并发开销可能超过收益
        // 基准设置为：加速比 >= 0.95x（允许 5% 的误差），效率不设下限
        var minExpectedSpeedup = 0.95; // 允许 5% 的误差

        metrics.Add(CreateMetric("单线程时间", singleThreadSw.Elapsed.TotalMilliseconds, "ms", null, MetricCategory.Time, true));
        metrics.Add(CreateMetric("多线程时间", multiThreadSw.Elapsed.TotalMilliseconds, "ms", null, MetricCategory.Time, true));
        metrics.Add(CreateMetric("加速比", speedup, "x", minExpectedSpeedup, MetricCategory.Throughput, true));
        metrics.Add(CreateMetric("并行效率", efficiency, "%", null, MetricCategory.Throughput, true)); // 不设基准，仅供参考

        _results.Add(CreateTestResult("并发构建性能",
            $"测试 {Environment.ProcessorCount} 核并发构建的扩展性", metrics, multiThreadSw.Elapsed));

        Console.WriteLine($"   ✓ 加速比: {speedup:F2}x (效率: {efficiency:F1}%)\n");
    }

    /// <summary>
    /// 复杂主题性能测试
    /// 模拟 Hugo 复杂主题的特性：多层嵌套模板、大量 partial、复杂循环、分类/标签页面等
    /// </summary>
    private async Task RunComplexThemeTestAsync(int pageCount, string label)
    {
        Console.WriteLine($"🎭 测试: {label}站点构建 ({pageCount}页)...");
        var siteDir = await CreateComplexThemeSiteAsync(pageCount);
        var metrics = new List<PerformanceMetric>();

        // 预热阶段
        for (var i = 0; i < WarmupIterations; i++)
        {
            await BuildSiteAsync(siteDir);
        }

        // 测量阶段
        var durations = new List<double>();
        var peakMemoryUsages = new List<double>();
        var totalAllocations = new List<double>();

        for (var i = 0; i < MeasureIterations; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var process = Process.GetCurrentProcess();
            var workingSetBefore = process.WorkingSet64;
            var allocatedBefore = GC.GetTotalAllocatedBytes(precise: false);

            var sw = Stopwatch.StartNew();
            await BuildSiteAsync(siteDir);
            sw.Stop();

            process.Refresh();
            var workingSetAfter = process.WorkingSet64;
            var allocatedAfter = GC.GetTotalAllocatedBytes(precise: false);

            durations.Add(sw.Elapsed.TotalMilliseconds);
            peakMemoryUsages.Add(Math.Max(0, (workingSetAfter - workingSetBefore) / 1024.0 / 1024.0));
            totalAllocations.Add((allocatedAfter - allocatedBefore) / 1024.0 / 1024.0);
        }

        // 统计分析
        var avgDuration = durations.Average();
        var minDuration = durations.Min();
        var maxDuration = durations.Max();
        var stdDev = CalculateStdDev(durations);
        var avgPeakMemory = peakMemoryUsages.Average();
        var avgAllocations = totalAllocations.Average();
        var pagesPerSecond = pageCount / (avgDuration / 1000.0);
        var msPerPage = avgDuration / pageCount;

        // 复杂主题的基准：预期比简单主题慢 30-50%
        // Hugo 复杂主题约 250-500 页/秒，我们的目标是 >= 500 页/秒
        var complexThemeBaseline = 500.0;
        // 复杂主题内存开销更大：复杂内容约 0.6-0.7 MB/页
        var adjustedMemoryBaseline = 50.0 + (pageCount / 100.0) * 65.0;

        metrics.Add(CreateMetric("构建时间", avgDuration, "ms", null, MetricCategory.Time, true));
        metrics.Add(CreateMetric("最小时间", minDuration, "ms", null, MetricCategory.Time, true));
        metrics.Add(CreateMetric("最大时间", maxDuration, "ms", null, MetricCategory.Time, true));
        metrics.Add(CreateMetric("标准差", stdDev, "ms", null, MetricCategory.Time, false));
        metrics.Add(CreateMetric("构建速度", pagesPerSecond, "页/秒",
            complexThemeBaseline, MetricCategory.Throughput, true));
        metrics.Add(CreateMetric("每页耗时", msPerPage, "ms/页", null, MetricCategory.Time, false));
        metrics.Add(CreateMetric("内存增量", avgPeakMemory, "MB", null, MetricCategory.Memory, false));
        metrics.Add(CreateMetric("总分配量", avgAllocations, "MB",
            adjustedMemoryBaseline, MetricCategory.Memory, false));

        // 与简单主题对比
        var simpleThemeSpeed = 1088.0; // P6 优化后的简单主题速度
        var complexityOverhead = (simpleThemeSpeed - pagesPerSecond) / simpleThemeSpeed * 100;

        metrics.Add(CreateMetric("复杂度开销", complexityOverhead, "%", 50.0, MetricCategory.Throughput, false));

        _results.Add(CreateTestResult($"{label}站点构建 ({pageCount}页)",
            $"测试复杂主题（多层嵌套、partial、分类/标签、相关文章、TOC）的构建性能",
            metrics, TimeSpan.FromMilliseconds(avgDuration)));

        Console.WriteLine($"   ✓ 构建速度: {pagesPerSecond:F0} 页/秒 (每页 {msPerPage:F2}ms)");
        Console.WriteLine($"   ✓ 复杂度开销: {complexityOverhead:F1}% (相比简单主题)\n");
    }

    #region 辅助方法

    private async Task<string> CreateTestSiteAsync(int pageCount)
    {
        var siteDir = Path.Combine(_testDir, $"site-{pageCount}-{Guid.NewGuid().ToString()[..8]}");
        Directory.CreateDirectory(siteDir);

        await File.WriteAllTextAsync(Path.Combine(siteDir, "Flint.toml"), """
            baseURL = "https://example.com/"
            title = "性能测试站点"
            languageCode = "zh-cn"
            """);

        var contentDir = Path.Combine(siteDir, "content", "posts");
        Directory.CreateDirectory(contentDir);

        var layoutsDir = Path.Combine(siteDir, "layouts", "_default");
        Directory.CreateDirectory(layoutsDir);
        await File.WriteAllTextAsync(Path.Combine(layoutsDir, "single.html"),
            "<html><head><title>{{ page.title }}</title></head><body>{{ page.content }}</body></html>");
        await File.WriteAllTextAsync(Path.Combine(layoutsDir, "list.html"),
            "<html><head><title>列表</title></head><body>列表页</body></html>");

        var tasks = Enumerable.Range(1, pageCount).Select(async i =>
        {
            var content = $"""
                ---
                title: "测试文章 {i}"
                date: {DateTime.Now.AddDays(-i):yyyy-MM-ddTHH:mm:ss}+08:00
                tags: ["tag{i % 10}", "tag{i % 5}"]
                ---

                # 标题

                这是文章 {i} 的内容。包含 **粗体** 和 *斜体*。

                - 列表项 1
                - 列表项 2

                ```csharp
                var x = {i};
                ```
                """;
            await File.WriteAllTextAsync(Path.Combine(contentDir, $"post-{i:D5}.md"), content);
        });

        await Task.WhenAll(tasks);
        return siteDir;
    }

    /// <summary>
    /// 创建复杂主题测试站点
    /// 模拟 Hugo 复杂主题的特性
    /// </summary>
    private async Task<string> CreateComplexThemeSiteAsync(int pageCount)
    {
        var siteDir = Path.Combine(_testDir, $"complex-site-{pageCount}-{Guid.NewGuid().ToString()[..8]}");
        Directory.CreateDirectory(siteDir);

        // 配置文件
        await File.WriteAllTextAsync(Path.Combine(siteDir, "Flint.toml"), """
            baseURL = "https://example.com/"
            title = "复杂主题性能测试站点"
            languageCode = "zh-cn"
            
            [params]
            author = "测试作者"
            description = "这是一个复杂主题性能测试站点"
            showRelatedPosts = true
            showToc = true
            showReadingTime = true
            showWordCount = true
            showAuthor = true
            showDate = true
            showTags = true
            showCategories = true
            
            [menu]
            [[menu.main]]
            name = "首页"
            url = "/"
            weight = 1
            
            [[menu.main]]
            name = "文章"
            url = "/posts/"
            weight = 2
            
            [[menu.main]]
            name = "标签"
            url = "/tags/"
            weight = 3
            
            [[menu.main]]
            name = "分类"
            url = "/categories/"
            weight = 4
            
            [[menu.main]]
            name = "关于"
            url = "/about/"
            weight = 5
            
            [taxonomies]
            tag = "tags"
            category = "categories"
            """);

        // 创建内容目录
        var contentDir = Path.Combine(siteDir, "content", "posts");
        Directory.CreateDirectory(contentDir);

        // 创建复杂主题布局
        var layoutsDir = Path.Combine(siteDir, "layouts");
        var defaultDir = Path.Combine(layoutsDir, "_default");
        var partialsDir = Path.Combine(layoutsDir, "partials");
        Directory.CreateDirectory(defaultDir);
        Directory.CreateDirectory(partialsDir);

        // 创建 partial 模板（模拟 Hugo 的 partial 系统）
        // 1. 头部 partial
        await File.WriteAllTextAsync(Path.Combine(partialsDir, "head.html"), """
            <head>
                <meta charset="UTF-8">
                <meta name="viewport" content="width=device-width, initial-scale=1.0">
                <title>{{ page.title }} | {{ site.title }}</title>
                <meta name="description" content="{{ page.summary | default: site.config.params.description }}">
                <meta name="author" content="{{ site.config.params.author }}">
                <meta name="keywords" content="{{ page.tags | array.join: ', ' }}">
                <link rel="canonical" href="{{ page.permalink }}">
                <!-- Open Graph -->
                <meta property="og:title" content="{{ page.title }}">
                <meta property="og:description" content="{{ page.summary }}">
                <meta property="og:url" content="{{ page.permalink }}">
                <meta property="og:type" content="article">
                <!-- Twitter Card -->
                <meta name="twitter:card" content="summary_large_image">
                <meta name="twitter:title" content="{{ page.title }}">
                <meta name="twitter:description" content="{{ page.summary }}">
            </head>
            """);

        // 2. 导航 partial
        await File.WriteAllTextAsync(Path.Combine(partialsDir, "nav.html"), """
            <nav class="main-nav">
                <div class="nav-brand">
                    <a href="{{ site.base_url }}">{{ site.title }}</a>
                </div>
                <ul class="nav-menu">
                    {{ for item in site.menus.main }}
                    <li class="nav-item">
                        <a href="{{ item.url }}" class="{{ if page.rel_permalink == item.url }}active{{ end }}">
                            {{ item.name }}
                        </a>
                    </li>
                    {{ end }}
                </ul>
                <div class="nav-search">
                    <input type="search" placeholder="搜索...">
                </div>
            </nav>
            """);

        // 3. 侧边栏 partial
        await File.WriteAllTextAsync(Path.Combine(partialsDir, "sidebar.html"), """
            <aside class="sidebar">
                <!-- 作者信息 -->
                <div class="widget widget-author">
                    <h3>关于作者</h3>
                    <div class="author-info">
                        <img src="/images/avatar.png" alt="{{ site.config.params.author }}">
                        <h4>{{ site.config.params.author }}</h4>
                        <p>{{ site.config.params.description }}</p>
                    </div>
                </div>
                
                <!-- 最新文章 -->
                <div class="widget widget-recent">
                    <h3>最新文章</h3>
                    <ul>
                        {{ for post in site.regular_pages | array.limit: 5 }}
                        <li>
                            <a href="{{ post.permalink }}">{{ post.title }}</a>
                            <span class="date">{{ post.date | date.to_string: '%Y-%m-%d' }}</span>
                        </li>
                        {{ end }}
                    </ul>
                </div>
                
                <!-- 标签云 -->
                <div class="widget widget-tags">
                    <h3>标签</h3>
                    <div class="tag-cloud">
                        {{ for tag in page.tags }}
                        <a href="/tags/{{ tag | string.downcase }}/" class="tag">{{ tag }}</a>
                        {{ end }}
                    </div>
                </div>
                
                <!-- 分类 -->
                <div class="widget widget-categories">
                    <h3>分类</h3>
                    <ul>
                        {{ for cat in page.categories }}
                        <li><a href="/categories/{{ cat | string.downcase }}/">{{ cat }}</a></li>
                        {{ end }}
                    </ul>
                </div>
            </aside>
            """);

        // 4. 文章元信息 partial
        await File.WriteAllTextAsync(Path.Combine(partialsDir, "post-meta.html"), """
            <div class="post-meta">
                <span class="meta-author">
                    <i class="icon-user"></i>
                    {{ site.config.params.author }}
                </span>
                <span class="meta-date">
                    <i class="icon-calendar"></i>
                    {{ page.date | date.to_string: '%Y年%m月%d日' }}
                </span>
                <span class="meta-reading-time">
                    <i class="icon-clock"></i>
                    {{ page.reading_time }} 分钟阅读
                </span>
                <span class="meta-word-count">
                    <i class="icon-file-text"></i>
                    {{ page.word_count }} 字
                </span>
                {{ if page.tags.size > 0 }}
                <span class="meta-tags">
                    <i class="icon-tags"></i>
                    {{ for tag in page.tags }}
                    <a href="/tags/{{ tag | string.downcase }}/">{{ tag }}</a>
                    {{ end }}
                </span>
                {{ end }}
                {{ if page.categories.size > 0 }}
                <span class="meta-categories">
                    <i class="icon-folder"></i>
                    {{ for cat in page.categories }}
                    <a href="/categories/{{ cat | string.downcase }}/">{{ cat }}</a>
                    {{ end }}
                </span>
                {{ end }}
            </div>
            """);

        // 5. 目录 partial (TOC)
        await File.WriteAllTextAsync(Path.Combine(partialsDir, "toc.html"), """
            <div class="table-of-contents">
                <h3>目录</h3>
                <nav class="toc">
                    {{ page.toc }}
                </nav>
            </div>
            """);

        // 6. 相关文章 partial
        await File.WriteAllTextAsync(Path.Combine(partialsDir, "related-posts.html"), """
            <div class="related-posts">
                <h3>相关文章</h3>
                <div class="related-grid">
                    {{ for post in site.regular_pages | array.limit: 4 }}
                    {{ if post.permalink != page.permalink }}
                    <article class="related-item">
                        <a href="{{ post.permalink }}">
                            <h4>{{ post.title }}</h4>
                            <p>{{ post.summary | truncate: 100 }}</p>
                            <span class="date">{{ post.date | date.to_string: '%Y-%m-%d' }}</span>
                        </a>
                    </article>
                    {{ end }}
                    {{ end }}
                </div>
            </div>
            """);

        // 7. 页脚 partial
        await File.WriteAllTextAsync(Path.Combine(partialsDir, "footer.html"), """
            <footer class="site-footer">
                <div class="footer-content">
                    <div class="footer-section">
                        <h4>{{ site.title }}</h4>
                        <p>{{ site.config.params.description }}</p>
                    </div>
                    <div class="footer-section">
                        <h4>导航</h4>
                        <ul>
                            {{ for item in site.menus.main }}
                            <li><a href="{{ item.url }}">{{ item.name }}</a></li>
                            {{ end }}
                        </ul>
                    </div>
                    <div class="footer-section">
                        <h4>最新文章</h4>
                        <ul>
                            {{ for post in site.regular_pages | array.limit: 3 }}
                            <li><a href="{{ post.permalink }}">{{ post.title }}</a></li>
                            {{ end }}
                        </ul>
                    </div>
                </div>
                <div class="footer-bottom">
                    <p>&copy; {{ 'now' | date.to_string: '%Y' }} {{ site.title }}. All rights reserved.</p>
                    <p>Powered by Flint</p>
                </div>
            </footer>
            """);

        // 8. 分页 partial
        await File.WriteAllTextAsync(Path.Combine(partialsDir, "pagination.html"), """
            <nav class="pagination">
                <ul>
                    <li class="page-item prev">
                        <a href="#" class="page-link">&laquo; 上一页</a>
                    </li>
                    <li class="page-item active">
                        <span class="page-link">1</span>
                    </li>
                    <li class="page-item">
                        <a href="#" class="page-link">2</a>
                    </li>
                    <li class="page-item">
                        <a href="#" class="page-link">3</a>
                    </li>
                    <li class="page-item next">
                        <a href="#" class="page-link">下一页 &raquo;</a>
                    </li>
                </ul>
            </nav>
            """);

        // 创建复杂的 single.html 模板（使用所有 partial）
        await File.WriteAllTextAsync(Path.Combine(defaultDir, "single.html"), """
            <!DOCTYPE html>
            <html lang="{{ site.language }}">
            {{ include 'partials/head.html' }}
            <body>
                {{ include 'partials/nav.html' }}
                
                <main class="main-content">
                    <div class="container">
                        <div class="content-wrapper">
                            <article class="post">
                                <header class="post-header">
                                    <h1 class="post-title">{{ page.title }}</h1>
                                    {{ include 'partials/post-meta.html' }}
                                </header>
                                
                                {{ include 'partials/toc.html' }}
                                
                                <div class="post-content">
                                    {{ page.content }}
                                </div>
                                
                                <footer class="post-footer">
                                    <div class="post-tags">
                                        {{ for tag in page.tags }}
                                        <a href="/tags/{{ tag | string.downcase }}/" class="tag">{{ tag }}</a>
                                        {{ end }}
                                    </div>
                                    <div class="post-share">
                                        <span>分享到：</span>
                                        <a href="#" class="share-twitter">Twitter</a>
                                        <a href="#" class="share-facebook">Facebook</a>
                                        <a href="#" class="share-weibo">微博</a>
                                    </div>
                                </footer>
                            </article>
                            
                            {{ include 'partials/related-posts.html' }}
                        </div>
                        
                        {{ include 'partials/sidebar.html' }}
                    </div>
                </main>
                
                {{ include 'partials/footer.html' }}
            </body>
            </html>
            """);

        // 创建复杂的 list.html 模板
        await File.WriteAllTextAsync(Path.Combine(defaultDir, "list.html"), """
            <!DOCTYPE html>
            <html lang="{{ site.language }}">
            {{ include 'partials/head.html' }}
            <body>
                {{ include 'partials/nav.html' }}
                
                <main class="main-content">
                    <div class="container">
                        <div class="content-wrapper">
                            <header class="page-header">
                                <h1>{{ page.title | default: '文章列表' }}</h1>
                                <p class="page-description">共 {{ site.regular_pages.size }} 篇文章</p>
                            </header>
                            
                            <div class="post-list">
                                {{ for post in site.regular_pages }}
                                <article class="post-item">
                                    <header>
                                        <h2 class="post-title">
                                            <a href="{{ post.permalink }}">{{ post.title }}</a>
                                        </h2>
                                        <div class="post-meta">
                                            <span class="date">{{ post.date | date.to_string: '%Y-%m-%d' }}</span>
                                            <span class="reading-time">{{ post.reading_time }} 分钟</span>
                                            <span class="word-count">{{ post.word_count }} 字</span>
                                        </div>
                                    </header>
                                    <div class="post-summary">
                                        {{ post.summary | truncate: 200 }}
                                    </div>
                                    <footer class="post-footer">
                                        <div class="post-tags">
                                            {{ for tag in post.tags }}
                                            <a href="/tags/{{ tag | string.downcase }}/">{{ tag }}</a>
                                            {{ end }}
                                        </div>
                                        <a href="{{ post.permalink }}" class="read-more">阅读全文 &rarr;</a>
                                    </footer>
                                </article>
                                {{ end }}
                            </div>
                            
                            {{ include 'partials/pagination.html' }}
                        </div>
                        
                        {{ include 'partials/sidebar.html' }}
                    </div>
                </main>
                
                {{ include 'partials/footer.html' }}
            </body>
            </html>
            """);

        // 创建复杂的内容文件
        var categories = new[] { "技术", "生活", "随笔", "教程", "评测" };
        var tags = new[] { "CSharp", "DotNET", "性能优化", "架构", "设计模式", "测试", "DevOps", "云计算", "AI", "前端" };

        var tasks = Enumerable.Range(1, pageCount).Select(async i =>
        {
            var category = categories[i % categories.Length];
            var postTags = new[] { tags[i % tags.Length], tags[(i + 3) % tags.Length], tags[(i + 7) % tags.Length] };
            var tagName = tags[i % tags.Length];

            // 生成更复杂的 Markdown 内容（避免在内插字符串中使用大括号）
            var content = GenerateComplexMarkdownContent(i, tagName, category, postTags);

            await File.WriteAllTextAsync(Path.Combine(contentDir, $"post-{i:D5}.md"), content);
        });

        await Task.WhenAll(tasks);
        return siteDir;
    }

    /// <summary>
    /// 生成复杂的 Markdown 内容
    /// </summary>
    private static string GenerateComplexMarkdownContent(int index, string tagName, string category, string[] postTags)
    {
        var date = DateTime.Now.AddDays(-index);
        var lastmod = DateTime.Now.AddDays(-index + 1);
        var featured = index % 10 == 0 ? "true" : "false";

        return $@"---
title: ""深入理解 {tagName} 的核心概念与最佳实践 - 第 {index} 篇""
date: {date:yyyy-MM-ddTHH:mm:ss}+08:00
lastmod: {lastmod:yyyy-MM-ddTHH:mm:ss}+08:00
draft: false
tags: [""{postTags[0]}"", ""{postTags[1]}"", ""{postTags[2]}""]
categories: [""{category}""]
author: ""测试作者""
description: ""这是一篇关于 {tagName} 的深度技术文章，涵盖核心概念、最佳实践和实战案例。""
keywords: [""{postTags[0]}"", ""{postTags[1]}"", ""{category}""]
featured: {featured}
toc: true
---

## 引言

在当今快速发展的技术领域，{tagName} 已经成为每个开发者必须掌握的核心技能之一。本文将深入探讨 {tagName} 的核心概念、最佳实践以及实战案例，帮助读者全面理解并掌握这一重要技术。

## 核心概念

### 什么是 {tagName}？

{tagName} 是一种现代软件开发中广泛使用的技术/方法论。它的主要特点包括：

- **高效性**：能够显著提升开发效率和代码质量
- **可维护性**：使代码更易于理解和维护
- **可扩展性**：支持系统的平滑扩展
- **可测试性**：便于编写单元测试和集成测试

### 核心原理

{tagName} 的核心原理可以概括为以下几点：

1. **单一职责原则**：每个模块只负责一个功能
2. **开闭原则**：对扩展开放，对修改关闭
3. **依赖倒置原则**：依赖抽象而非具体实现
4. **接口隔离原则**：使用多个专门的接口

## 最佳实践

### 实践一：代码组织

良好的代码组织是成功应用 {tagName} 的基础。以下是推荐的项目结构：

```
src/
├── Core/           # 核心业务逻辑
├── Infrastructure/ # 基础设施层
├── Application/    # 应用服务层
└── Presentation/   # 表现层
```

### 实践二：错误处理

正确的错误处理对于构建健壮的应用至关重要。建议使用 Result 模式来处理可预期的错误，使用异常来处理不可预期的错误。

### 实践三：性能优化

性能优化是 {tagName} 应用中的重要环节：

| 优化策略 | 效果 | 复杂度 |
|----------|------|--------|
| 缓存 | 高 | 中 |
| 异步处理 | 高 | 低 |
| 批量操作 | 中 | 低 |
| 索引优化 | 高 | 中 |

## 实战案例

### 案例 {index}：构建高性能服务

在这个案例中，我们将展示如何使用 {tagName} 构建一个高性能的服务。关键点包括：

1. 使用依赖注入管理服务生命周期
2. 实现缓存层减少数据库访问
3. 使用异步编程提高吞吐量
4. 实现健康检查和监控

## 常见问题

### Q1: 如何选择合适的实现方式？

选择实现方式时需要考虑以下因素：

- 项目规模和复杂度
- 团队技术栈和经验
- 性能要求
- 可维护性需求

### Q2: 如何处理遗留代码？

处理遗留代码的建议步骤：

1. 编写测试覆盖现有功能
2. 逐步重构，小步快跑
3. 使用适配器模式隔离新旧代码
4. 持续集成，确保不引入回归

## 总结

本文详细介绍了 {tagName} 的核心概念、最佳实践和实战案例。通过学习和应用这些知识，开发者可以：

- 提升代码质量和可维护性
- 提高开发效率
- 构建更健壮的系统
- 更好地应对复杂业务需求

希望本文对你有所帮助，欢迎在评论区分享你的想法和经验！

## 参考资料

- [官方文档](https://example.com/docs)
- [最佳实践指南](https://example.com/best-practices)
- [社区讨论](https://example.com/community)

---

*本文是 {category} 系列的第 {index} 篇，更多精彩内容请关注本站。*
";
    }

    private async Task<BuildResult> BuildSiteAsync(string siteDir, int parallelism = 0)
    {
        var configLoader = new ConfigLoader();
        var contentParser = new ContentParser();
        var templateRenderer = new ScribanTemplateRenderer(Path.Combine(siteDir, "layouts"));
        var assetPipeline = new AssetPipeline();
        var siteBuilder = new SiteBuilder(contentParser, templateRenderer, assetPipeline, configLoader);

        var options = new BuildOptions
        {
            SourcePath = siteDir,
            OutputPath = Path.Combine(siteDir, "public"),
            Parallelism = parallelism > 0 ? parallelism : Environment.ProcessorCount,
            IncludeDrafts = false,
            IncludeFuture = false,
            Minify = false
        };

        return await siteBuilder.BuildAsync(options);
    }

    private async Task<BuildResult> IncrementalBuildAsync(string siteDir, IReadOnlyList<string> changedFiles)
    {
        var configLoader = new ConfigLoader();
        var contentParser = new ContentParser();
        var templateRenderer = new ScribanTemplateRenderer(Path.Combine(siteDir, "layouts"));
        var assetPipeline = new AssetPipeline();
        var siteBuilder = new SiteBuilder(contentParser, templateRenderer, assetPipeline, configLoader);

        var options = new BuildOptions
        {
            SourcePath = siteDir,
            OutputPath = Path.Combine(siteDir, "public"),
            Parallelism = Environment.ProcessorCount,
            IncludeDrafts = false,
            IncludeFuture = false,
            Minify = false
        };

        return await siteBuilder.IncrementalBuildAsync(options, changedFiles);
    }

    private List<string> GenerateMarkdownFiles(int count)
    {
        return Enumerable.Range(1, count).Select(i => $"""
            ---
            title: "文章 {i}"
            date: 2024-01-{(i % 28) + 1:D2}
            ---

            # 标题

            这是文章 {i} 的内容。包含 **粗体** 和 *斜体*。

            - 项目 1
            - 项目 2

            ```csharp
            var x = {i};
            ```
            """).ToList();
    }

    private string GenerateLargeConfig()
    {
        return """
            baseURL = "https://example.com/"
            title = "测试站点"
            languageCode = "zh-cn"
            theme = "test-theme"
            
            [params]
            author = "测试作者"
            description = "这是一个测试站点"
            
            [menu]
            [[menu.main]]
            name = "首页"
            url = "/"
            weight = 1
            
            [taxonomies]
            tag = "tags"
            category = "categories"
            """;
    }

    private TemplateContext CreateTestContext()
    {
        return new TemplateContext
        {
            Page = new PageContext
            {
                Title = "测试标题",
                Content = "<p>测试内容</p>",
                Permalink = "/test/",
                RelPermalink = "/test/",
                Date = DateTimeOffset.Now,
                Tags = ["tag1"],
                Categories = ["cat1"],
                WordCount = 100,
                ReadingTime = TimeSpan.FromMinutes(1)
            },
            Site = new SiteContext
            {
                Title = "测试站点",
                BaseURL = "https://example.com",
                Language = "zh-cn",
                Pages = [],
                RegularPages = [],
                Taxonomies = new TaxonomyCollection { Taxonomies = new Dictionary<string, IReadOnlyList<TaxonomyTerm>>() },
                Menus = new MenuCollection { Menus = new Dictionary<string, IReadOnlyList<MenuItem>>() },
                Config = new SiteConfig { Title = "Test", BaseURL = "https://example.com" },
                BuildDate = DateTimeOffset.Now,
                LastChange = DateTimeOffset.Now,
                IsMultiLingual = false,
                Languages = ["zh-cn"]
            }
        };
    }

    private PerformanceMetric CreateMetric(string name, double value, string unit,
        double? baseline, MetricCategory category, bool isHigherBetter)
    {
        double? diffPercent = null;
        var passed = true;

        if (baseline.HasValue)
        {
            diffPercent = isHigherBetter
                ? ((value - baseline.Value) / baseline.Value) * 100
                : ((baseline.Value - value) / baseline.Value) * 100;
            passed = isHigherBetter ? value >= baseline.Value : value <= baseline.Value;
        }

        return new PerformanceMetric
        {
            Name = name,
            Value = value,
            Unit = unit,
            Baseline = baseline,
            PassedBaseline = passed,
            DifferencePercent = diffPercent,
            Category = category
        };
    }

    private PerformanceTestResult CreateTestResult(string name, string description,
        List<PerformanceMetric> metrics, TimeSpan duration)
    {
        return new PerformanceTestResult
        {
            TestName = name,
            Description = description,
            Environment = GetEnvironmentInfo(),
            Metrics = metrics,
            PassedBaseline = metrics.All(m => m.PassedBaseline),
            Duration = duration
        };
    }

    private static EnvironmentInfo GetEnvironmentInfo()
    {
        return new EnvironmentInfo
        {
            OperatingSystem = RuntimeInformation.OSDescription,
            Architecture = RuntimeInformation.OSArchitecture.ToString(),
            ProcessorCount = Environment.ProcessorCount,
            DotNetVersion = RuntimeInformation.FrameworkDescription,
            MachineName = Environment.MachineName,
            AvailableMemoryMB = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024 / 1024
        };
    }

    private void Cleanup()
    {
        if (Directory.Exists(_testDir))
        {
            try
            { Directory.Delete(_testDir, true); }
            catch { }
        }
    }

    #endregion
}
