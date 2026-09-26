// perf 子命令组：性能测试入口与回归门禁（scripts/perf-gate.ps1 与 run-performance-tests.ps1 的 C# 移植）
// 口径沿用 PS 版：三轮跑性能套件取逐键中位数（压单轮环境摆动）、HTML 报告正则解析、
// 基线 JSON 阈值对比（棘轮：基线只随确认的改进更新）。

using System.CommandLine;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Flint.DevTools.Commands;

/// <summary>
/// perf 子命令组
/// </summary>
internal static class PerfCommand
{
    private const string PerfProjectRelative = "tests/Flint.PerformanceTests/Flint.PerformanceTests.csproj";

    /// <summary>基线键 → (报告测试名, 指标名)，与 PS 版映射表一致</summary>
    private static readonly Dictionary<string, (string Test, string Metric)> BaselineMap = new(StringComparer.Ordinal)
    {
        ["site_build_100"] = ("小型站点构建 (100页)", "构建时间"),
        ["site_build_500"] = ("中型站点构建 (500页)", "构建时间"),
        ["site_build_1000"] = ("大型站点构建 (1000页)", "构建时间"),
        ["site_build_10000"] = ("超大型站点构建 (10000页)", "构建时间"),
        ["complex_theme_1000"] = ("复杂主题站点构建 (1000页)", "构建时间"),
        ["incremental_build"] = ("增量构建性能", "增量构建时间"),
        ["markdown_parse_1000"] = ("Markdown 解析性能", "解析时间"),
        ["template_render_1000"] = ("模板渲染性能", "渲染时间"),
        ["concurrent_build"] = ("并发构建性能", "多线程时间"),
    };

    private static readonly Regex TitleRegex = new(@"test-title[^>]*>([^<]+)<", RegexOptions.Compiled);
    private static readonly Regex HeadingRegex = new(@"<h[23][^>]*>([^<]+)<", RegexOptions.Compiled);
    private static readonly Regex MetricPairRegex = new(
        @"metric-name"">([^<]+)</div>.*?metric-actual"">([^<]+)<",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly string[] BuildArgs = { "build", "-c", "Release", "--nologo", "-v", "q" };

    internal static Command Build()
    {
        var cmd = new Command("perf", "性能测试与回归门禁（替代 perf-gate.ps1 / run-performance-tests.*）");
        cmd.Subcommands.Add(BuildRun());
        cmd.Subcommands.Add(BuildGate());
        return cmd;
    }

    // ---------------------------------------------------------------- perf run

    private static Command BuildRun()
    {
        var outputOpt = new Option<string>("--output")
        {
            Description = "HTML 报告输出路径",
            DefaultValueFactory = _ => Path.Combine(RepoPaths.RepoRoot, "TestResults", "performance-report.html"),
        };
        var openOpt = new Option<bool>("--open") { Description = "结束后打开报告" };

        var cmd = new Command("run", "构建 Release 并运行性能套件，生成 HTML 报告");
        cmd.Options.Add(outputOpt);
        cmd.Options.Add(openOpt);

        cmd.SetAction(parseResult =>
        {
            var outputPath = Path.GetFullPath(parseResult.GetValue(outputOpt)!);
            var open = parseResult.GetValue(openOpt);

            Console.WriteLine("🚀 Flint 性能测试");
            Console.WriteLine("==================");
            Console.WriteLine();

            Console.WriteLine("📦 构建项目...");
            var build = Bench.ProcessRunner.RunTimedAsync(
                "dotnet",
                BuildArgs,
                workingDirectory: RepoPaths.RepoRoot).GetAwaiter().GetResult();
            if (build.ExitCode != 0)
            {
                throw new InvalidOperationException($"构建失败: {build.Stderr}");
            }

            Console.WriteLine("   ✓ 构建完成");
            Console.WriteLine();

            Console.WriteLine("🧪 运行性能测试...");
            Console.WriteLine();

            var outputDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            var suite = Bench.ProcessRunner.RunTimedAsync(
                "dotnet",
                new[] { "run", "--project", PerfProjectRelative, "-c", "Release", "--", outputPath },
                workingDirectory: RepoPaths.RepoRoot).GetAwaiter().GetResult();
            if (suite.Stdout.Length > 0)
            {
                Console.WriteLine(suite.Stdout);
            }

            Console.WriteLine();
            Console.WriteLine(suite.ExitCode == 0 ? "✅ 所有性能测试通过!" : "⚠️ 部分性能测试未达到基准");
            Console.WriteLine();
            Console.WriteLine($"📄 报告位置: {outputPath}");

            if (open && File.Exists(outputPath))
            {
                Console.WriteLine("🌐 正在打开报告...");
                OpenWithShell(outputPath);
            }

            return suite.ExitCode;
        });

        return cmd;
    }

    private static void OpenWithShell(string path)
    {
        var psi = new System.Diagnostics.ProcessStartInfo(path)
        {
            UseShellExecute = true,
        };
        System.Diagnostics.Process.Start(psi);
    }

    // ---------------------------------------------------------------- perf gate

    private static Command BuildGate()
    {
        var runsOpt = new Option<int>("--runs") { Description = "跑几轮取中位", DefaultValueFactory = _ => 3 };
        var baselineOpt = new Option<string>("--baseline")
        {
            Description = "基线 JSON 路径",
            DefaultValueFactory = _ => Path.Combine(RepoPaths.RepoRoot, "scripts", "perf-baseline.json"),
        };
        var outputOpt = new Option<string>("--output")
        {
            Description = "门禁报告路径（首轮写此路径）",
            DefaultValueFactory = _ => Path.Combine(RepoPaths.RepoRoot, "TestResults", "perf-gate-run.html"),
        };
        var updateOpt = new Option<bool>("--update-baseline") { Description = "提示基线更新流程（棘轮语义）" };

        var cmd = new Command("gate", "三轮中位数 vs 基线阈值，显著劣化即 FAIL（棘轮语义）");
        cmd.Options.Add(runsOpt);
        cmd.Options.Add(baselineOpt);
        cmd.Options.Add(outputOpt);
        cmd.Options.Add(updateOpt);

        cmd.SetAction(parseResult => RunGateAsync(
            parseResult.GetValue(runsOpt),
            parseResult.GetValue(baselineOpt)!,
            parseResult.GetValue(outputOpt)!,
            parseResult.GetValue(updateOpt)).GetAwaiter().GetResult());

        return cmd;
    }

    private static async Task<int> RunGateAsync(int runCount, string baselinePath, string outputPath, bool updateBaseline)
    {
        Console.WriteLine("🚦 Flint 性能门禁");
        Console.WriteLine("==================");

        if (!File.Exists(baselinePath))
        {
            throw new InvalidOperationException($"基线文件不存在: {baselinePath}");
        }

        using var doc = JsonDocument.Parse(File.ReadAllText(baselinePath));
        var root = doc.RootElement;
        var threshold = root.GetProperty("threshold_percent").GetDouble();
        var metricsElement = root.GetProperty("metrics");

        Console.WriteLine($"🧪 运行性能套件 ×{runCount}（逐键 {runCount} 轮中位）...");
        Console.WriteLine();

        var runs = new Dictionary<int, Dictionary<string, double>>();
        for (var i = 1; i <= runCount; i++)
        {
            // 首轮写标准输出路径（供既有消费方），其余轮写并列文件
            var runPath = i == 1
                ? outputPath
                : Path.Combine(
                    Path.GetDirectoryName(outputPath) ?? ".",
                    Path.GetFileNameWithoutExtension(outputPath) + $".run{i}" + Path.GetExtension(outputPath));

            Console.WriteLine($"  ── 轮 {i}/{runCount} ──");
            var result = await Bench.ProcessRunner.RunTimedAsync(
                "dotnet",
                new[] { "run", "--project", PerfProjectRelative, "-c", "Release", "--", runPath },
                workingDirectory: RepoPaths.RepoRoot).ConfigureAwait(false);
            if (result.ExitCode != 0)
            {
                Console.WriteLine($"  ⚠️ 第 {i} 轮套件自身有未达标项（阈值判定），继续做基线对比");
            }

            if (!File.Exists(runPath))
            {
                throw new InvalidOperationException($"第 {i} 轮报告未生成: {runPath}");
            }

            runs[i] = ParseGateReport(File.ReadAllText(runPath));
        }

        // 逐键多轮中位（某轮缺键时按现有轮取，不整键丢弃）
        var results = new Dictionary<string, double>(StringComparer.Ordinal);
        var allKeys = runs.Values.SelectMany(r => r.Keys).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
        foreach (var key in allKeys)
        {
            var values = new List<double>();
            for (var i = 1; i <= runCount; i++)
            {
                if (runs[i].TryGetValue(key, out var v))
                {
                    values.Add(v);
                }
            }

            if (values.Count == 0)
            {
                continue;
            }

            results[key] = Bench.ProcessRunner.Median(values);
            if (values.Count > 1)
            {
                Console.WriteLine(
                    $"    {key}: 中位 {results[key]:F1}ms（{runCount} 轮 {values.Min():F1}~{values.Max():F1}）");
            }
        }

        Console.WriteLine();
        var fail = 0;
        foreach (var prop in metricsElement.EnumerateObject())
        {
            if (!BaselineMap.TryGetValue(prop.Name, out var lookup))
            {
                continue;
            }

            var entry = prop.Value;
            if (!entry.TryGetProperty("ms", out var msElement))
            {
                continue;
            }

            var baseMs = msElement.GetDouble();
            var label = entry.TryGetProperty("label", out var labelEl) ? labelEl.GetString() ?? prop.Name : prop.Name;

            var actualKey = $"{lookup.Test}|{lookup.Metric}";
            if (!results.TryGetValue(actualKey, out var actual))
            {
                Console.WriteLine($"  ⚠️ 未在报告中找到: {actualKey}");
                continue;
            }

            var limit = baseMs * (1 + (threshold / 100));
            var pct = baseMs > 0 ? Math.Round((actual - baseMs) / baseMs * 100, 1) : 0;

            if (actual > limit)
            {
                Console.WriteLine(
                    $"  [FAIL] {label}: {actual:F1}ms > 基线 {baseMs}ms x (1+{threshold}%) = {limit:F1}ms (+{pct.ToString("F1", CultureInfo.InvariantCulture)}%)");
                fail++;
            }
            else
            {
                Console.WriteLine(
                    $"  [PASS] {label}: {actual:F1}ms (基线 {baseMs}ms, {pct.ToString("F1", CultureInfo.InvariantCulture)}%)");
            }
        }

        if (updateBaseline)
        {
            Console.WriteLine($"基线更新请手工编辑 {baselinePath}（确认改进后才允许上调，棘轮语义）");
        }

        if (fail > 0)
        {
            Console.WriteLine($"\n🚫 性能门禁 FAIL：{fail} 项显著劣化（>{threshold}%）");
            return 1;
        }

        Console.WriteLine("\n✅ 性能门禁通过：无显著劣化");
        return 0;
    }

    // ---------------------------------------------------------------- HTML 解析

    /// <summary>解析门禁 HTML 报告：键 = "测试名|指标名"（正则与 PS 版逐字一致）</summary>
    private static Dictionary<string, double> ParseGateReport(string html)
    {
        var parsed = new Dictionary<string, double>(StringComparer.Ordinal);

        foreach (var card in html.Split("class=\"test-card", StringSplitOptions.None))
        {
            var titleMatch = TitleRegex.Match(card);
            if (!titleMatch.Success)
            {
                titleMatch = HeadingRegex.Match(card);
            }

            if (!titleMatch.Success)
            {
                continue;
            }

            var testName = titleMatch.Groups[1].Value.Trim();
            foreach (Match p in MetricPairRegex.Matches(card))
            {
                var key = $"{testName}|{p.Groups[1].Value.Trim()}";
                if (double.TryParse(p.Groups[2].Value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                {
                    parsed[key] = value;
                }
            }
        }

        return parsed;
    }
}
