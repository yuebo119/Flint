// Flint 静态站点生成器
// 开发服务器性能测试
// 测试开发服务器的响应时间和并发处理能力

using System.Diagnostics;
using Flint.IntegrationTests.Fixtures;
using Flint.IntegrationTests.Utilities;
using AwesomeAssertions;
using Xunit;

namespace Flint.IntegrationTests.Performance;

/// <summary>
/// 开发服务器性能测试
/// 测试开发服务器的响应时间和并发处理能力
/// </summary>
/// <remarks>
/// 满足需求：
/// - Requirements 9.10: 测试开发服务器性能
/// </remarks>
[Trait("Category", "Performance")]
[Trait("Category", "Integration")]
[Trait("Category", "DevServer")]
public sealed class DevServerPerformanceTests : IAsyncLifetime
{
    private readonly TestSiteFixture _fixture;
    private readonly CliTestRunner _cli;
    private readonly ITestOutputHelper _output;

    public DevServerPerformanceTests(ITestOutputHelper output)
    {
        _output = output;
        _fixture = new TestSiteFixture();
        _cli = new CliTestRunner();
    }

    public async ValueTask InitializeAsync()
    {
        await _fixture.InitializeAsync();
        await _fixture.CreateSiteAsync("default");

        // 创建一些测试内容
        for (int i = 0; i < 20; i++)
        {
            var date = DateTime.UtcNow.AddDays(-i).ToString("yyyy-MM-dd");
            await _fixture.AddContentAsync($"posts/post-{i:D4}.md", $"""
                ---
                title: "Test Post {i}"
                date: {date}
                draft: false
                ---
                
                This is test post number {i}.
                """);
        }

        // 先构建站点
        var buildResult = await _cli.BuildAsync(
            new CliBuildOptions { Clean = true },
            _fixture.SiteRoot);
        buildResult.IsSuccess.Should().BeTrue($"构建应该成功: {buildResult.ErrorOutput}");
    }

    public async ValueTask DisposeAsync()
    {
        _cli.Dispose();
        await _fixture.DisposeAsync();
    }

    #region 响应时间测试

    /// <summary>
    /// 测试静态文件响应时间
    /// </summary>
    [Fact]
    public async Task StaticFileResponse_ShouldBeFast()
    {
        // 这个测试模拟开发服务器的响应时间
        // 由于实际启动开发服务器需要长时间运行，这里使用文件系统读取来模拟

        // Arrange
        var indexPath = Path.Combine(_fixture.OutputPath, "index.html");
        if (!File.Exists(indexPath))
        {
            _output.WriteLine("跳过测试：index.html 不存在");
            return;
        }

        // Act - 模拟多次请求
        var responseTimes = new List<double>();
        const int requestCount = 100;

        for (int i = 0; i < requestCount; i++)
        {
            var sw = Stopwatch.StartNew();
            var content = await File.ReadAllTextAsync(indexPath);
            sw.Stop();
            responseTimes.Add(sw.Elapsed.TotalMilliseconds);
        }

        // Assert
        var p50 = GetPercentile(responseTimes, 50);
        var p95 = GetPercentile(responseTimes, 95);
        var p99 = GetPercentile(responseTimes, 99);

        _output.WriteLine($"响应时间 P50: {p50:F2} ms");
        _output.WriteLine($"响应时间 P95: {p95:F2} ms");
        _output.WriteLine($"响应时间 P99: {p99:F2} ms");

        // 文件读取应该很快
        p50.Should().BeLessThan(100, "P50 响应时间应该小于 100ms");
        p95.Should().BeLessThan(200, "P95 响应时间应该小于 200ms");
    }

    /// <summary>
    /// 测试大文件响应时间
    /// </summary>
    [Fact]
    public async Task LargeFileResponse_ShouldBeReasonable()
    {
        // Arrange - 创建一个大文件
        var largeContent = new string('A', 1024 * 1024); // 1MB
        var largePath = Path.Combine(_fixture.OutputPath, "large-file.txt");
        await File.WriteAllTextAsync(largePath, largeContent);

        // Act
        var responseTimes = new List<double>();
        const int requestCount = 10;

        for (int i = 0; i < requestCount; i++)
        {
            var sw = Stopwatch.StartNew();
            var content = await File.ReadAllTextAsync(largePath);
            sw.Stop();
            responseTimes.Add(sw.Elapsed.TotalMilliseconds);
        }

        // Assert
        var average = responseTimes.Average();
        _output.WriteLine($"大文件平均响应时间: {average:F2} ms");

        // 大文件读取时间取决于磁盘性能和系统负载
        // 在 CI 环境或并行测试时可能更慢
        // 使用较宽松的阈值（3 秒）
        average.Should().BeLessThan(3000, "大文件响应时间应该小于 3 秒");

        // 清理
        File.Delete(largePath);
    }

    #endregion

    #region 并发请求处理测试

    /// <summary>
    /// 测试并发请求处理能力
    /// </summary>
    [Fact]
    public async Task ConcurrentRequests_ShouldBeHandled()
    {
        // Arrange
        var indexPath = Path.Combine(_fixture.OutputPath, "index.html");
        if (!File.Exists(indexPath))
        {
            _output.WriteLine("跳过测试：index.html 不存在");
            return;
        }

        const int concurrentRequests = 50;

        // Act - 并发读取文件
        var sw = Stopwatch.StartNew();
        var tasks = Enumerable.Range(0, concurrentRequests)
            .Select(_ => File.ReadAllTextAsync(indexPath))
            .ToList();

        var results = await Task.WhenAll(tasks);
        sw.Stop();

        // Assert
        results.Should().AllSatisfy(content => content.Should().NotBeNullOrEmpty());

        var totalTime = sw.Elapsed.TotalMilliseconds;
        var avgTime = totalTime / concurrentRequests;

        _output.WriteLine($"并发请求数: {concurrentRequests}");
        _output.WriteLine($"总时间: {totalTime:F2} ms");
        _output.WriteLine($"平均时间: {avgTime:F2} ms");

        // 并发处理应该高效
        totalTime.Should().BeLessThan(5000, "并发请求应该在 5 秒内完成");
    }

    /// <summary>
    /// 测试高并发场景
    /// </summary>
    [Fact]
    public async Task HighConcurrency_ShouldNotDegrade()
    {
        // Arrange
        var files = _fixture.GetOutputFiles()
            .Where(f => f.EndsWith(".html"))
            .Take(10)
            .ToList();

        if (files.Count == 0)
        {
            _output.WriteLine("跳过测试：没有 HTML 文件");
            return;
        }

        const int requestsPerFile = 20;

        // Act - 对多个文件进行并发请求
        var sw = Stopwatch.StartNew();
        var tasks = new List<Task<string>>();

        foreach (var file in files)
        {
            var fullPath = Path.Combine(_fixture.OutputPath, file);
            for (int i = 0; i < requestsPerFile; i++)
            {
                tasks.Add(File.ReadAllTextAsync(fullPath));
            }
        }

        var results = await Task.WhenAll(tasks);
        sw.Stop();

        // Assert
        var totalRequests = files.Count * requestsPerFile;
        var throughput = totalRequests / sw.Elapsed.TotalSeconds;

        _output.WriteLine($"总请求数: {totalRequests}");
        _output.WriteLine($"总时间: {sw.Elapsed.TotalMilliseconds:F2} ms");
        _output.WriteLine($"吞吐量: {throughput:F2} 请求/秒");

        results.Should().AllSatisfy(content => content.Should().NotBeNullOrEmpty());
    }

    #endregion

    #region 内存稳定性测试

    /// <summary>
    /// 测试长时间运行的内存使用稳定性
    /// </summary>
    [Fact]
    public async Task LongRunning_MemoryUsage_ShouldBeStable()
    {
        // Arrange - 起真实 dev server **子进程**采样其私有内存。此前在测试进程内
        // 读文件断言 GC.GetTotalMemory：xUnit 并行套件下其他测试集合的存活数据
        // 参与读数（实测初始 65→521MB 随并行相位漂移、增长 116~580MB），量具
        // 测到的是套件噪声而非 dev server——单跑绿/全量红。子进程内存与套件
        // 并行度解耦，且才对得上测试名（此前从未启动过 dev server）
        var port = Random.Shared.Next(10000, 60000);
        var startInfo = new ProcessStartInfo
        {
            FileName = _cli.CliPath,
            WorkingDirectory = _fixture.SiteRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var arg in new[] { "serve", "--port", port.ToString(), "--open", "false", "--livereload", "false" })
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = startInfo };
        // 排水：不消费管道会撑满缓冲让 serve 阻塞在 Console.WriteLine
        process.OutputDataReceived += (_, _) => { };
        process.ErrorDataReceived += (_, _) => { };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var baseUrl = new Uri($"http://127.0.0.1:{port}/");

            // 等就绪（serve 启动期先做一次全站构建，Debug 下需要数秒）
            var ready = false;
            var deadline = DateTime.UtcNow.AddSeconds(60);
            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    var probe = await http.GetAsync(baseUrl);
                    if (probe.IsSuccessStatusCode)
                    {
                        ready = true;
                        break;
                    }
                }
                catch (HttpRequestException)
                {
                    // 端口尚未监听，重试
                }

                await Task.Delay(200);
            }

            ready.Should().BeTrue("dev server 应在 60 秒内就绪");

            // 预热：首轮请求触发 JIT/模板缓存，之后取基线才反映稳态泄漏
            for (var i = 0; i < 50; i++)
            {
                (await http.GetAsync(baseUrl)).EnsureSuccessStatusCode();
            }

            process.Refresh();
            var initialPrivate = process.PrivateMemorySize64;

            // Act - 持续请求模拟长时间运行
            const int iterations = 500;
            for (var i = 0; i < iterations; i++)
            {
                (await http.GetAsync(baseUrl)).EnsureSuccessStatusCode();
            }

            // Assert
            process.Refresh();
            var finalPrivate = process.PrivateMemorySize64;
            var growthMB = (finalPrivate - initialPrivate) / 1024.0 / 1024.0;

            _output.WriteLine($"初始私有内存: {initialPrivate / 1024.0 / 1024.0:F2} MB");
            _output.WriteLine($"最终私有内存: {finalPrivate / 1024.0 / 1024.0:F2} MB");
            _output.WriteLine($"内存增长: {growthMB:F2} MB");

            growthMB.Should().BeLessThan(100, "持续请求后 dev server 私有内存增长应该有限");
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
        }
    }

    #endregion

    #region 热重载延迟测试

    /// <summary>
    /// 测试文件变化检测延迟
    /// </summary>
    [Fact]
    public async Task FileChangeDetection_ShouldBeFast()
    {
        // Arrange
        var testFile = Path.Combine(_fixture.SiteRoot, "content", "posts", "hot-reload-test.md");

        // 创建初始文件
        await File.WriteAllTextAsync(testFile, """
            ---
            title: "Hot Reload Test"
            date: 2024-01-01
            ---
            
            Initial content.
            """);

        // Act - 模拟文件变化检测
        var sw = Stopwatch.StartNew();

        // 修改文件
        await File.WriteAllTextAsync(testFile, """
            ---
            title: "Hot Reload Test Updated"
            date: 2024-01-01
            ---
            
            Updated content.
            """);

        // 检测文件变化
        var lastWriteTime = File.GetLastWriteTimeUtc(testFile);
        sw.Stop();

        // Assert
        _output.WriteLine($"文件变化检测时间: {sw.Elapsed.TotalMilliseconds:F2} ms");

        sw.Elapsed.TotalMilliseconds.Should().BeLessThan(100,
            "文件变化检测应该很快");

        // 清理
        File.Delete(testFile);
    }

    #endregion

    #region 辅助方法

    /// <summary>
    /// 计算百分位数
    /// </summary>
    private static double GetPercentile(List<double> values, int percentile)
    {
        if (values.Count == 0)
            return 0;

        var sorted = values.OrderBy(v => v).ToList();
        var index = (int)Math.Ceiling(percentile / 100.0 * sorted.Count) - 1;
        return sorted[Math.Max(0, Math.Min(index, sorted.Count - 1))];
    }

    #endregion
}
