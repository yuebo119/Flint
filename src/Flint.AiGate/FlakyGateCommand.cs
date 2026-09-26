// flaky-gate：重跑式 flaky 检测门禁（.ai/scripts/flaky-gate.sh 的 C# 移植）
// 机制：同一测试项目连跑 N 次（默认 2），解析 TRX 报告逐测试比对——
//   状态不稳定的测试 = flaky；环境性失败（端口/文件锁/引擎初始化，T-FLINT-2 口径）单独归类不误判。
// 退出码：0=无代码性 flaky；1=检出 code-flaky；2=用法错误。
// --self-test 合成红测（不跑 dotnet）：coin-flip 双跑 P/F 必检出、稳定 P/P 与环境 E/P 不误判。

using System.CommandLine;
using System.Text.RegularExpressions;

namespace Flint.AiGate;

/// <summary>
/// flaky-gate 命令
/// </summary>
internal static class FlakyGateCommand
{
    // Flint 环境性失败指纹：端口/文件锁/引擎（V8/DartSass）/WebSocket 握手
    private static readonly string[] EnvPatterns =
    {
        "SocketException", "address already in use", "address in use", "port",
        "file being used by another process", "file in use", "locking violation",
        "V8", "Jint", "DartSass", "WebSocket", "timed out", "timeout",
        "Initialization method threw", "host stopped",
    };

    // 环境性/条件性不稳定集合（P/E/S 的任意不稳 → env_mixed 归类）
    private static readonly string[] EnvOutcomes = { "P", "E", "S" };

    internal static Command Build()
    {
        var cmd = new Command("flaky-gate", "重跑式 flaky 检测（<csproj> [--runs N] | --self-test）");

        var projectArg = new Argument<string>("csproj") { Description = "测试项目路径" };
        projectArg.Arity = ArgumentArity.ZeroOrOne;
        var runsOpt = new Option<int>("--runs") { Description = "跑数", DefaultValueFactory = _ => 2 };
        var selfTestOpt = new Option<bool>("--self-test") { Description = "合成红测（不跑 dotnet）" };

        cmd.Arguments.Add(projectArg);
        cmd.Options.Add(runsOpt);
        cmd.Options.Add(selfTestOpt);

        cmd.SetAction(parseResult =>
        {
            var proj = parseResult.GetValue(projectArg) ?? string.Empty;
            var runs = parseResult.GetValue(runsOpt);
            var selfTest = parseResult.GetValue(selfTestOpt);

            if (!selfTest && (string.IsNullOrEmpty(proj) || !File.Exists(proj)))
            {
                Console.Error.WriteLine("用法：flaky-gate <csproj> [--runs N] | --self-test");
                return 2;
            }

            var work = Directory.CreateTempSubdirectory("flint-flaky.");
            try
            {
                if (selfTest)
                {
                    return RunSelfTest(work.FullName);
                }

                Console.WriteLine($"═══════ flaky-gate：{proj} × {runs} 跑 ═══════");
                var build = Bench.RunTimed("dotnet", new[] { "build", proj, "-c", "Release", "--verbosity", "quiet" });
                if (build != 0)
                {
                    Console.Error.WriteLine("构建失败");
                    return 1;
                }

                for (var i = 1; i <= runs; i++)
                {
                    Console.WriteLine($"── 第 {i}/{runs} 跑 ──");
                    var runDir = Path.Combine(work.FullName, $"run_{i}");
                    Directory.CreateDirectory(runDir);
                    Bench.RunTimed(
                        "dotnet",
                        new[]
                        {
                            "test", proj, "--no-build", "-c", "Release",
                            "--logger", "trx;LogFileName=result.trx",
                            "--results-directory", runDir, "--verbosity", "quiet",
                        },
                        quiet: true);
                }

                return Analyze(work.FullName, runs);
            }
            finally
            {
                try
                {
                    work.Delete(recursive: true);
                }
                catch (IOException)
                {
                    // 临时目录清理失败不影响门禁结论
                }
            }
        });

        return cmd;
    }

    /// <summary>合成红测：coin-flip 双跑必检出；稳定用例与环境混跑不误判</summary>
    private static int RunSelfTest(string work)
    {
        var run1 = Directory.CreateDirectory(Path.Combine(work, "run_1"));
        var run2 = Directory.CreateDirectory(Path.Combine(work, "run_2"));
        Io.WriteAllText(Path.Combine(run1.FullName, "r.trx"),
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<TestRun><Results>\n" +
            "<UnitTestResult testName=\"T.Stable_Passes\" outcome=\"Passed\"><Output /></UnitTestResult>\n" +
            "<UnitTestResult testName=\"T.Flaky_CoinFlip\" outcome=\"Passed\"><Output /></UnitTestResult>\n" +
            "<UnitTestResult testName=\"T.Env_Toggle\" outcome=\"Passed\"><Output /></UnitTestResult>\n" +
            "</Results></TestRun>");
        Io.WriteAllText(Path.Combine(run2.FullName, "r.trx"),
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<TestRun><Results>\n" +
            "<UnitTestResult testName=\"T.Stable_Passes\" outcome=\"Passed\"><Output /></UnitTestResult>\n" +
            "<UnitTestResult testName=\"T.Flaky_CoinFlip\" outcome=\"Failed\"><Output><ErrorInfo><Message>Assert.Equal() Failure: Expected 0</Message></ErrorInfo></Output></UnitTestResult>\n" +
            "<UnitTestResult testName=\"T.Env_Toggle\" outcome=\"Failed\"><Output><ErrorInfo><Message>SocketException: Address already in use, port 1313</Message></ErrorInfo></Output></UnitTestResult>\n" +
            "</Results></TestRun>");

        var (rc, output) = AnalyzeCapture(work, 2);
        foreach (var line in output)
        {
            Console.WriteLine($"  {line}");
        }

        var detectsCoinFlip = output.Any(l => l.StartsWith("FAIL FLAKY T.Flaky_CoinFlip", StringComparison.Ordinal));
        var noStable = !output.Any(l => l.StartsWith("FAIL FLAKY T.Stable_Passes", StringComparison.Ordinal));
        var envWarn = output.Any(l => l.StartsWith("WARN ENVFLAKY T.Env_Toggle", StringComparison.Ordinal));

        if (rc == 1 && detectsCoinFlip && noStable && envWarn)
        {
            Console.WriteLine("PASS FLAKY-SELFTEST  coin-flip 检出且稳定/环境用例不误判");
            return 0;
        }

        Console.Error.WriteLine("FAIL FLAKY-SELFTEST  检测逻辑异常（见上）");
        return 1;
    }

    /// <summary>解析 + 跨跑比对：返回 (退出码, 输出行)。退出码 1 = 检出 code-flaky</summary>
    private static int Analyze(string root, int runs)
    {
        var (rc, output) = AnalyzeCapture(root, runs);
        foreach (var line in output)
        {
            Console.WriteLine(line);
        }

        return rc;
    }

    private static (int ExitCode, List<string> Output) AnalyzeCapture(string root, int runs)
    {
        var output = new List<string>();
        var perRun = new List<Dictionary<string, string>>();
        var totalTests = 0;

        for (var i = 1; i <= runs; i++)
        {
            var runDir = Path.Combine(root, $"run_{i}");
            if (!Directory.Exists(runDir))
            {
                continue;
            }

            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var trx in Directory.EnumerateFiles(runDir, "*.trx", SearchOption.AllDirectories))
            {
                foreach (var (name, outcome, body) in ParseTrx(trx))
                {
                    totalTests++;
                    // outcome 转小写是 TRX 属性值契约比较（Python 版同款 tolower 语义），
                    // 非大小写规范化判等场景，CA1308 的 ToUpperInvariant 建议不适用
#pragma warning disable CA1308
                    var s = outcome.ToLowerInvariant() switch
#pragma warning restore CA1308
                    {
                        "passed" or "executed" => "P",
                        "notexecuted" or "skipped" => "S",
                        _ => EnvPatterns.Any(p => body.Contains(p, StringComparison.OrdinalIgnoreCase)) ? "E" : "F",
                    };
                    var prev = map.TryGetValue(name, out var existing) ? existing : "P";
                    map[name] = s == "F" || prev == "F" ? "F" : (s == "E" || prev == "E" ? "E" : "P");
                }
            }

            perRun.Add(map);
        }

        if (perRun.Count == 0 || totalTests == 0)
        {
            output.Add("::error ::FLAKY-GATE: No test results found (runner crash or config error?) — treating as FAIL");
            output.Add($"SUMMARY runs={runs} code_flaky=0 env_mixed=0 reports=0 FAIL");
            return (1, output);
        }

        var codeFlaky = new List<string>();
        var envMixed = new List<string>();
        var allKeys = new HashSet<string>(perRun.SelectMany(m => m.Keys), StringComparer.Ordinal);
        foreach (var key in allKeys)
        {
            var seq = perRun.Select(m => m.TryGetValue(key, out var v) ? v : "P").ToList();
            var distinct = new HashSet<string>(seq);
            if (distinct.Count == 1)
            {
                continue;
            }

            if (distinct.IsSubsetOf(EnvOutcomes))
            {
                envMixed.Add($"{key}：{string.Join('/', seq)}（环境性/条件性不稳定——查环境或 skip 条件）");
            }
            else
            {
                codeFlaky.Add($"{key}：{string.Join('/', seq)}（代码性 flaky——重跑阈值内不稳定）");
            }
        }

        foreach (var c in codeFlaky.Order(StringComparer.Ordinal))
        {
            output.Add("FAIL FLAKY " + c);
        }

        foreach (var e in envMixed.Order(StringComparer.Ordinal))
        {
            output.Add("WARN ENVFLAKY " + e);
        }

        output.Add($"SUMMARY runs={runs} code_flaky={codeFlaky.Count} env_mixed={envMixed.Count}");
        return (codeFlaky.Count > 0 ? 1 : 0, output);
    }

    private static readonly Regex UnitTestResultRegex = new(
        @"<UnitTestResult\b([^>]*)>(.*?)</UnitTestResult>", RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>xUnit VSTest TRX：UnitTestResult 的 testName/outcome 属性 + Output 体</summary>
    private static IEnumerable<(string Name, string Outcome, string Body)> ParseTrx(string path)
    {
        var text = Io.ReadAllText(path);
        foreach (Match m in UnitTestResultRegex.Matches(text))
        {
            var attrs = m.Groups[1].Value;
            var body = m.Groups[2].Value;
            var name = Regex.Match(attrs, "testName=\"([^\"]*)\"");
            var outcome = Regex.Match(attrs, "outcome=\"([^\"]*)\"");
            if (!name.Success || !outcome.Success)
            {
                continue;
            }

            yield return (name.Groups[1].Value, outcome.Groups[1].Value, body);
        }
    }
}
