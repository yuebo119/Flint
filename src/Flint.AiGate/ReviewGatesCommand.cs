// review-gate + review-scope：评审轮次路由与范围清单生成器
// .ai/scripts/review-gate.sh 与 review-scope.sh 的 C# 移植。

using System.CommandLine;

namespace Flint.AiGate;

/// <summary>
/// review-gate 命令：判断本次变更需要全量评审/增量评审/跳过
/// </summary>
internal static class ReviewGateCommand
{
    internal static Command Build()
    {
        var refArg = new Argument<string>("compareRef") { Description = "与哪个提交比较（默认 HEAD~1）", DefaultValueFactory = _ => "HEAD~1" };
        refArg.Arity = ArgumentArity.ZeroOrOne;
        var cmd = new Command("review-gate", "评审轮次路由");
        cmd.Arguments.Add(refArg);

        cmd.SetAction(parseResult => Run(parseResult.GetValue(refArg) ?? "HEAD~1"));
        return cmd;
    }

    private static int Run(string reference)
    {
        Console.WriteLine("════════════════════════════════════════════════");
        Console.WriteLine($" 评审轮次路由 (compare: {reference})");
        Console.WriteLine("════════════════════════════════════════════════");

        // git 可用性前置（降级必须可见，不得静默 no-op）
        var rootDir = RepoLocator.FindCodeRoot();
        if (!GitHelper.IsRepo(rootDir))
        {
            Console.WriteLine("\u001b[0;33mSKIP\u001b[0m — 非 git 仓库，diff 路由不可用（按全量轮人工判定）");
            return 0;
        }

        var changed = GitHelper.Output(rootDir, "diff", "--name-only", reference)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .ToList();
        if (changed.Count == 0)
        {
            Console.WriteLine("\u001b[0;32mSKIP\u001b[0m — 无变更");
            return 0;
        }

        var srcCs = changed.Count(f => System.Text.RegularExpressions.Regex.IsMatch(f, "^src/.*\\.cs$"));
        var testCs = changed.Count(f => System.Text.RegularExpressions.Regex.IsMatch(f, "^tests/.*\\.cs$"));
        var docs = changed.Count(f => System.Text.RegularExpressions.Regex.IsMatch(f, "\\.(md|txt)$|^docs/"));
        var aiMeta = changed.Count(f => f.StartsWith(".ai/", StringComparison.Ordinal));
        var csproj = changed.Count(f => System.Text.RegularExpressions.Regex.IsMatch(f, "\\.csproj$|\\.props$|\\.targets$"));

        Console.WriteLine();
        Console.WriteLine(" 变更分类：");
        Console.WriteLine($"   src/*.cs       : {srcCs}");
        Console.WriteLine($"   tests/*.cs     : {testCs}");
        Console.WriteLine($"   docs/md        : {docs}");
        Console.WriteLine($"   .ai/meta       : {aiMeta}");
        Console.WriteLine($"   csproj/props   : {csproj}");
        Console.WriteLine();

        if (srcCs > 0)
        {
            Console.WriteLine($"\u001b[0;36m→ 全量轮\u001b[0m — src/ 行为级代码变更（{srcCs} 个 .cs 文件）");
            Console.WriteLine("  评审深度：六片并行全量");
            Console.WriteLine("  修复策略：P0-P2 当轮修 + P3 批量");
            return 0;
        }

        if (csproj > 0)
        {
            Console.WriteLine("\u001b[0;36m→ 增量轮\u001b[0m — 项目配置变更（依赖/属性）");
            Console.WriteLine("  评审深度：抽查变更项目 + 随机 1 片");
            return 0;
        }

        if (testCs > 0 && srcCs == 0)
        {
            Console.WriteLine("\u001b[0;33m→ 增量轮\u001b[0m — 仅测试变更");
            Console.WriteLine("  评审深度：抽查测试断言质量");
            return 0;
        }

        if (docs > 0 || aiMeta > 0)
        {
            Console.WriteLine("\u001b[0;33m→ SKIP\u001b[0m — 仅文档/元数据变更，无行为影响");
            return 0;
        }

        Console.WriteLine("\u001b[0;32m→ SKIP\u001b[0m — 无需评审（无代码/配置/文档变更）");
        return 0;
    }
}

/// <summary>
/// review-scope 命令：地毯式逐行的覆盖度账本清单元机械生成
/// </summary>
internal static class ReviewScopeCommand
{
    internal static Command Build()
    {
        var cmd = new Command("review-scope", "review 范围清单生成器");

        var diffOpt = new Option<bool>("--diff") { Description = "标准档：本次 diff 触及文件" };
        var allOpt = new Option<bool>("--all") { Description = "全仓档：src+tests+docs+scripts" };
        var partitionsOpt = new Option<int>("--partitions") { Description = "分片数", DefaultValueFactory = _ => 4 };
        cmd.Options.Add(diffOpt);
        cmd.Options.Add(allOpt);
        cmd.Options.Add(partitionsOpt);

        cmd.SetAction(parseResult =>
        {
            var mode = parseResult.GetValue(diffOpt) ? "diff" : parseResult.GetValue(allOpt) ? "all" : "full";
            var parts = parseResult.GetValue(partitionsOpt);
            return Run(mode, parts);
        });

        return cmd;
    }

    private static int Run(string mode, int partitions)
    {
        var rootDir = RepoLocator.FindCodeRoot();
        var gitOk = GitHelper.IsRepo(rootDir);

        List<string> files;
        if (mode == "diff")
        {
            if (!gitOk)
            {
                Console.WriteLine("SKIP: --diff 档需要 git 仓库（当前非 git 仓库）——改用全量档或 --all 档");
                return 0;
            }

            files = GitHelper.Output(rootDir, "diff", "HEAD~1", "--name-only", "--", "src/**/*.cs")
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(f => f.Trim())
                .Where(f => f.Length > 0 && !f.EndsWith(".g.cs", StringComparison.Ordinal))
                .ToList();
            if (files.Count == 0)
            {
                Console.WriteLine("本次 diff 未触及 src/ 手写代码");
                return 0;
            }
        }
        else if (mode == "all")
        {
            files = Scanner.CsFiles(rootDir, "src", "tests")
                .Where(f => !f.EndsWith(".g.cs", StringComparison.Ordinal))
                .ToList();
            foreach (var md in Directory.EnumerateFiles(Path.Combine(rootDir, "docs"), "*.md", SearchOption.AllDirectories))
            {
                files.Add(Path.GetRelativePath(rootDir, md).Replace('\\', '/'));
            }

            var scriptsDir = Path.Combine(rootDir, "scripts");
            if (Directory.Exists(scriptsDir))
            {
                foreach (var s in Directory.EnumerateFiles(scriptsDir, "*.*", SearchOption.AllDirectories)
                             .Where(f => f.EndsWith(".sh", StringComparison.Ordinal)
                                 || f.EndsWith(".ps1", StringComparison.Ordinal)
                                 || f.EndsWith(".cmd", StringComparison.Ordinal)))
                {
                    files.Add(Path.GetRelativePath(rootDir, s).Replace('\\', '/'));
                }
            }

            files = files.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
            Console.WriteLine("排除项：obj/bin 生成物 + brain-data 运行时记忆");
        }
        else
        {
            files = Scanner.CsFiles(rootDir, "src")
                .Where(f => !f.EndsWith(".g.cs", StringComparison.Ordinal))
                .ToList();
        }

        Console.WriteLine($"═══════ review 范围清单（{mode} 档）═══════");
        Console.WriteLine($"生成: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine();

        var manifest = new List<(int Lines, string File)>();
        var total = 0;
        foreach (var f in files)
        {
            if (!File.Exists(Path.Combine(rootDir, f)))
            {
                continue;
            }

            var n = Io.ReadAllLines(Path.Combine(rootDir, f)).Length;
            total += n;
            manifest.Add((n, f));
        }

        var sorted = manifest.OrderByDescending(m => m.Lines).ToList();

        Console.WriteLine($"─── 应读清单（{manifest.Count} 文件 · {total} 行）───");
        foreach (var (n, f) in sorted)
        {
            Console.WriteLine($"  {n,5} 行  {f}");
        }

        Console.WriteLine();
        Console.WriteLine($"─── 分片方案（{partitions} 片按行数均衡——供并行子代理各领一片地毯）───");

        // 贪心最小负载分片（与 awk 版同算法：按文件行数降序，逐文件挂当前负载最小的片）
        var loads = new int[partitions + 1];
        var assignment = new Dictionary<string, int>();
        foreach (var (n, f) in sorted)
        {
            var min = 1;
            for (var p = 2; p <= partitions; p++)
            {
                if (loads[p] < loads[min])
                {
                    min = p;
                }
            }

            assignment[f] = min;
            loads[min] += n;
        }

        for (var p = 1; p <= partitions; p++)
        {
            var members = sorted.Where(m => assignment[m.File] == p).Select(m => m.File).ToList();
            Console.WriteLine($"  片 {p}（{loads[p]} 行）:{string.Concat(members.Select(m => " " + m))}");
        }

        Console.WriteLine();
        Console.WriteLine("─── 覆盖度账本模板（粘贴报告段 1，逐文件勾销）───");
        foreach (var (n, f) in sorted)
        {
            Console.WriteLine($"- [ ] {f} ({n} 行)");
        }

        Console.WriteLine();
        Console.WriteLine("账本规则: 全部勾销 = 地毯完成;未勾销文件出现在报告 = 报告视为草稿。");

        // ── 姊妹对照清单（sibling-map.sh 尚未 C# 化：继续调用 bash 脚本，缺失时人工兜底）──
        var sibMap = Path.Combine(rootDir, "..", ".ai", "scripts", "sibling-map.sh");
        if (File.Exists(sibMap))
        {
            var sibOut = Bench.RunTimedCapture("bash", new[] { sibMap });
            Console.WriteLine();
            Console.WriteLine("─── 姊妹对照清单（范围内文件命中的多实现族——逐对核对留痕）───");
            var sibLines = sibOut.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var sibHits = 0;
            foreach (var f in files)
            {
                foreach (var line in sibLines)
                {
                    var parts2 = line.Split('|');
                    if (parts2.Length >= 3 && parts2[1].TrimStart().StartsWith('I') && line.Contains(f, StringComparison.Ordinal))
                    {
                        Console.WriteLine($"  {parts2[1].Trim()} ← {f}");
                        sibHits++;
                    }
                }
            }

            if (sibHits == 0)
            {
                Console.WriteLine("  （范围内无多实现族命中——语义孪生轴仍须对照 .ai/review/sibling-map.md 种子表）");
            }

            Console.WriteLine("规则: 每个命中的族，横向并列全部实现对照守卫/异常转换/参数消费三类对称性；核对结果记入报告。");
        }
        else
        {
            Console.WriteLine();
            Console.WriteLine("⚠ sibling-map.sh 缺失——姊妹对照退化为人工 checklist");
        }

        return 0;
    }
}
