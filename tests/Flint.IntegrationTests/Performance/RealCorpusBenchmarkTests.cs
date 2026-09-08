// 真实语料增量基线测量：对 benchmarks/corpus/corpus-merged（MDN+k8s 14974 页）
// 测全量与增量构建时间。语料不存在时跳过（CI 无本地语料）。
// 输出走 ITestOutputHelper，用于 cascade 增量化的收益评估。

using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Flint.IntegrationTests.Performance;

public sealed class RealCorpusBenchmarkTests
{
    private readonly string _siteRoot = FindCorpusSite();

    /// <summary>从测试程序位置向上探测仓库根下的语料站点</summary>
    private static string FindCorpusSite()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && dir is not null; i++)
        {
            var candidate = Path.Combine(dir.FullName, "benchmarks", "corpus", "corpus-merged", "flint");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
            dir = dir.Parent!;
        }
        return Path.Combine("benchmarks", "corpus", "corpus-merged", "flint");
    }
    private readonly ITestOutputHelper _output;

    public RealCorpusBenchmarkTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task RealCorpus_FullVsIncremental_Baseline()
    {
        if (!Directory.Exists(_siteRoot))
        {
            _output.WriteLine("本地语料不存在，跳过（benchmarks/corpus/corpus-merged）");
            return;
        }

        var outDir = Path.Combine(Path.GetTempPath(), $"flint-realbench-{Guid.NewGuid():N}");
        var builder = new Flint.Core.Site.SiteBuilder(
            new Flint.Core.Content.ContentParser(),
            new Flint.Core.Templates.ScribanTemplateRenderer(Path.Combine(_siteRoot, "layouts")),
            new Flint.Core.Assets.AssetPipeline(),
            new Flint.Core.Configuration.ConfigLoader());
        var options = new Flint.Core.Models.BuildOptions
        {
            SourcePath = Path.GetFullPath(_siteRoot),
            OutputPath = outDir,
            Parallelism = Environment.ProcessorCount
        };

        // 预热 + 全量计时
        await builder.BuildAsync(options);
        var fullTimes = new System.Collections.Generic.List<double>();
        for (var i = 0; i < 3; i++)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var r = await builder.BuildAsync(options);
            sw.Stop();
            r.Success.Should().BeTrue();
            fullTimes.Add(sw.Elapsed.TotalMilliseconds);
        }
        _output.WriteLine($"全量(3): {string.Join(", ", fullTimes)}");

        // 增量：改 1 个深层页面
        var target = Directory.GetFiles(Path.Combine(_siteRoot, "content", "mdn"), "*.md", SearchOption.AllDirectories).First();
        var original = await File.ReadAllTextAsync(target);
        await File.WriteAllTextAsync(target, original + "\n\n增量更新内容");
        try
        {
            var incTimes = new System.Collections.Generic.List<double>();
            for (var i = 0; i < 3; i++)
            {
                if (i > 0) await File.AppendAllTextAsync(target, "\n追加");
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var r = await builder.IncrementalBuildAsync(options, new List<string> { Path.GetFullPath(target) });
                sw.Stop();
                r.Success.Should().BeTrue();
                incTimes.Add(sw.Elapsed.TotalMilliseconds);
            }
            _output.WriteLine($"增量(3): {string.Join(", ", incTimes)}");
        }
        finally
        {
            await File.WriteAllTextAsync(target, original);
        }

        // 不做断言——本测试只产出基线数据
    }
}
