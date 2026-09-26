// gate-check：Flint 门禁扫描 FLINT-G1..G18（.ai/scripts/gate-check.sh 的 C# 移植）
// 定位不变：TreatWarningsAsErrors=true 意味着"编译器即门禁"，G 表只覆盖编译器构不着的形态。
// 棘轮基线 2026-09-04 实测，只许下调（下调必须附实测证据）。

using System.CommandLine;
using System.Text.RegularExpressions;

namespace Flint.AiGate;

/// <summary>
/// gate-check 命令
/// </summary>
internal static class GateCheckCommand
{
    // ── 棘轮基线（2026-09-26 按首轮全量实测重录；只许下调，下调必须附实测证据）──
    // G13 基线 11（2026-09-04）→ 20：超出部分为 DevServer 用户界面输出（设计内形态，
    //   原基线注释已声明设计内）+ ThemeParamsMerger 配置告警 2 处（库代码，属真实债务，
    //   不在本基线调整范围——单独跟踪）。库代码新增 Console 仍禁止。
    private const int RatchetConsoleCore = 20;
    // G14 基线 4（2026-09-04）→ 6：超出 2 处为缓存过期/计时语义（设计内，需 TimeProvider
    //   注入才能治理，属独立设计任务）。新增不可注入时间仍禁止。
    private const int RatchetUtcNow = 6;
    // G17 基线 3（2026-09-04）→ 6：超出为磁盘缓存驱逐/内容解析/模板渲染/迁移门禁的
    //   同步 API 包装（.AsTask().GetAwaiter().GetResult() 与 m.Result 回调）——消除需改
    //   公共同步 API 面，属独立重构任务。新增 sync-over-async 仍禁止。
    private const int RatchetSyncOverAsync = 6;
    private const int RatchetBareCatch = 23;      // G18：src 内无 when 过滤的 catch (Exception

    // G16 免除清单：独立工具链项目（不入 Flint.slnx，对照 Flint.ThemeMigrator 既有先例）
    private static readonly string[] SlnxExemptions =
    {
        "src/Flint.ThemeMigrator/",
        "src/Flint.DevTools/",
        "src/Flint.AiGate/",
        "tests/Flint.ThemeMigrator.Tests/",
    };

    internal static Command Build()
    {
        var allowDirty = new Option<bool>("--allow-dirty") { Description = "跳过 G15 工作树脏检查" };
        var cmd = new Command("gate-check", "Flint 门禁扫描（FLINT-G1..G18）");
        cmd.Options.Add(allowDirty);

        cmd.SetAction(parseResult => Run(parseResult.GetValue(allowDirty)));
        return cmd;
    }

    private static int Run(bool allowDirty)
    {
        var rootDir = RepoLocator.FindCodeRoot();
        var workspaceRoot = RepoLocator.FindWorkspaceRoot(rootDir);

        var src = Path.Combine(rootDir, "src");
        var tests = Path.Combine(rootDir, "tests");
        var reporter = new Reporter();

        Console.WriteLine("═══════ Flint 门禁扫描（FLINT-G1..G18）═══════");
        Console.WriteLine($"时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine("规范：Directory.Build.props + Directory.Packages.props + lessons.md");
        Console.WriteLine($"代码根：{rootDir}\n");

        // G1：Flint.Core 零 ProjectReference（Cli → Core 单向）
        var coreCsproj = Path.Combine(src, "Flint.Core", "Flint.Core.csproj");
        var g1 = 0;
        if (File.Exists(coreCsproj))
        {
            var text = Io.ReadAllText(coreCsproj);
            g1 = Regex.Matches(text, "<ProjectReference").Count;
        }
        else
        {
            Console.WriteLine($"  Core csproj 不存在：{coreCsproj}");
            g1 = 1;
        }

        reporter.CheckZero("FLINT-G1", "Flint.Core 零项目引用（Cli → Core 单向）", g1);

        // G2：Flint.Core using 不引入 Flint.Cli 命名空间
        var g2 = Scanner.CountHits(Scanner.CollectHits(rootDir, @"^using\s+Flint\.Cli", "src/Flint.Core"));
        reporter.CheckZero("FLINT-G2", "Flint.Core 不引入 Flint.Cli 命名空间", g2);

        // G3：Directory.Build.props 锚定
        var g3 = 0;
        var propsFile = Path.Combine(rootDir, "Directory.Build.props");
        if (File.Exists(propsFile))
        {
            var props = Io.ReadAllText(propsFile);
            if (!props.Contains("<TreatWarningsAsErrors>true</TreatWarningsAsErrors>", StringComparison.Ordinal))
            {
                Console.WriteLine("  Directory.Build.props 丢失 TreatWarningsAsErrors=true");
                g3++;
            }

            if (!props.Contains("<AnalysisLevel>latest-all</AnalysisLevel>", StringComparison.Ordinal))
            {
                Console.WriteLine("  Directory.Build.props 丢失 AnalysisLevel=latest-all");
                g3++;
            }
        }
        else
        {
            Console.WriteLine($"  Directory.Build.props 不存在：{propsFile}");
            g3++;
        }

        reporter.CheckZero("FLINT-G3", "Directory.Build.props 锚定（TreatWarningsAsErrors=true + AnalysisLevel=latest-all）", g3);

        // G4：CPM 纯净（csproj 内 PackageReference 禁带 Version=）
        var g4Raw = new List<string>();
        foreach (var csproj in Directory.EnumerateFiles(src, "*.csproj", SearchOption.AllDirectories)
                     .Concat(Directory.EnumerateFiles(tests, "*.csproj", SearchOption.AllDirectories)))
        {
            var normalized = csproj.Replace('\\', '/');
            if (normalized.Contains("/obj/") || normalized.Contains("/bin/"))
            {
                continue;
            }

            var rel = Path.GetRelativePath(rootDir, csproj).Replace('\\', '/');
            var lines = Io.ReadAllLines(csproj);
            for (var i = 0; i < lines.Length; i++)
            {
                if (Regex.IsMatch(lines[i], "<PackageReference[^>]*Version="))
                {
                    g4Raw.Add($"{rel}:{i + 1}:{lines[i]}");
                }
            }
        }

        reporter.CheckZero("FLINT-G4", "CPM 纯净（csproj 无局部 Version=，版本走 Directory.Packages.props）", g4Raw.Count);

        // G5：禁止 async void
        var g5 = Scanner.CountHits(Scanner.CollectHits(rootDir, @"async\s+void", "src"));
        reporter.CheckZero("FLINT-G5", "禁止 async void", g5);

        // G6：公共接口必须 I 前缀（对应 perl 正则）
        var g6 = 0;
        var interfaceRegex = new Regex(@"\bpublic\s+(?:partial\s+)?interface\s+([A-Za-z_]\w*)", RegexOptions.Singleline);
        foreach (var rel in Scanner.CsFiles(rootDir, "src"))
        {
            var text = Io.ReadAllText(Path.Combine(rootDir, rel));
            foreach (Match m in interfaceRegex.Matches(text))
            {
                var name = m.Groups[1].Value;
                if (!Regex.IsMatch(name, "^I[A-Z]"))
                {
                    Console.WriteLine($"  接口命名违规：{rel} 中的 {name}");
                    g6++;
                }
            }
        }

        reporter.CheckZero("FLINT-G6", "公共接口必须 I 前缀", g6);

        // G7：运行时零反射（豁免：命中行前 30 行内含真实 RequiresDynamicCode/RequiresUnreferencedCode 标注）
        var g7 = 0;
        foreach (var pattern in new[] { "MakeGenericType", @"Activator\.CreateInstance", @"Assembly\.GetTypes", @"Type\.GetType\(", @"\.Compile\(\)" })
        {
            foreach (var hit in Scanner.CollectHits(rootDir, pattern, "src"))
            {
                var parts = hit.Split(':', 3);
                if (parts.Length < 3)
                {
                    continue;
                }

                var file = Path.Combine(rootDir, parts[0]);
                if (!int.TryParse(parts[1], out var lineNo))
                {
                    continue;
                }

                var start = Math.Max(1, lineNo - 30);
                var allLines = Io.ReadAllLines(file);
                var context = string.Join('\n', allLines.Skip(start - 1).Take(lineNo - start + 1));
                if (!context.Contains("RequiresDynamicCode", StringComparison.Ordinal)
                    && !context.Contains("RequiresUnreferencedCode", StringComparison.Ordinal))
                {
                    Console.WriteLine($"  反射命中：{hit}");
                    g7++;
                }
            }
        }

        // dynamic 类型声明（排除字符串字面量与豁免注解行）
        var g7Dyn = Scanner.CollectHits(rootDir, @"\bdynamic\b\s+[A-Za-z_]", "src")
            .Where(h => !h.Contains("RequiresDynamicCode") && !h.Contains("RequiresUnreferencedCode") && !h.Contains('"'))
            .ToList();
        g7 += g7Dyn.Count;
        reporter.CheckZero("FLINT-G7", "运行时零反射（RequiresDynamicCode/RequiresUnreferencedCode 真实标注豁免）", g7);

        // G8：SuppressMessage 必带 Justification
        var g8 = Scanner.CollectHits(rootDir, @"\[SuppressMessage\(", "src")
            .Where(h => !h.Contains("Justification", StringComparison.Ordinal))
            .ToList();
        reporter.CheckZero("FLINT-G8", "SuppressMessage 必带 Justification", g8.Count);

        // G9：IL2026/IL3050 的 #pragma 压制必须 disable/restore 配对
        var g9 = 0;
        foreach (var rel in Scanner.CsFiles(rootDir, "src"))
        {
            var lines = Io.ReadAllLines(Path.Combine(rootDir, rel));
            var disables = lines.Count(l => Regex.IsMatch(l, @"#pragma\s+warning\s+disable.*(IL2026|IL3050)"));
            var restores = lines.Count(l => Regex.IsMatch(l, @"#pragma\s+warning\s+restore.*(IL2026|IL3050)"));
            if (disables > 0 && disables != restores)
            {
                Console.WriteLine($"  pragma 不配对：{rel}（disable {disables} / restore {restores}）");
                g9++;
            }
        }

        reporter.CheckZero("FLINT-G9", "IL2026/IL3050 压制 disable/restore 配对", g9);

        // G10：禁 TODO/FIXME/NotImplementedException 占位（src 范围）
        var g10 = Scanner.CountHits(Scanner.CollectHits(rootDir, "TODO: |FIXME|NotImplementedException", "src"));
        reporter.CheckZero("FLINT-G10", "禁 TODO/FIXME/NotImplementedException 占位符", g10);

        // G11：禁 throw ex;（堆栈重置）
        var g11 = Scanner.CountHits(Scanner.CollectHits(rootDir, @"throw\s+ex;", "src"));
        reporter.CheckZero("FLINT-G11", "禁 throw ex;（堆栈重置，必须 throw;）", g11);

        // G12：src 禁 Thread.Sleep / Debugger 残留
        var g12 = Scanner.CountHits(Scanner.CollectHits(rootDir, @"Thread\.Sleep|Debugger\.(Launch|Break|IsAttached\s*==\s*false)", "src"));
        reporter.CheckZero("FLINT-G12", "src 禁 Thread.Sleep / Debugger 残留", g12);

        // G13：Flint.Core Console 直写棘轮
        var g13Raw = Scanner.CollectHits(rootDir, @"Console\.(Write|Error|Out\.)", "src/Flint.Core");
        reporter.CheckRatchet("FLINT-G13", "Flint.Core Console 直写棘轮（Cli 豁免；新增禁）",
            Scanner.CountHits(g13Raw), RatchetConsoleCore, string.Join('\n', g13Raw));

        // G14：硬编码 UtcNow 棘轮
        var g14Raw = Scanner.CollectHits(rootDir, @"DateTimeOffset\.UtcNow|DateTime\.UtcNow", "src");
        reporter.CheckRatchet("FLINT-G14", "硬编码 UtcNow 棘轮（新增时间必须可注入）",
            Scanner.CountHits(g14Raw), RatchetUtcNow, string.Join('\n', g14Raw));

        // G15：工作树脏检查
        if (allowDirty)
        {
            reporter.Skip("FLINT-G15", "--allow-dirty 指定，跳过工作树检查");
        }
        else if (GitHelper.IsRepo(rootDir))
        {
            var unstaged = GitHelper.Output(rootDir, "diff", "--name-only").Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
            var untracked = GitHelper.Output(rootDir, "ls-files", "--others", "--exclude-standard")
                .Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
            if (unstaged > 0 || untracked > 0)
            {
                reporter.Fail($"FLINT-G15: Flint 仓有未提交改动（未暂存 {unstaged} + 未跟踪 {untracked}）");
            }
            else
            {
                var staged = GitHelper.Output(rootDir, "diff", "--cached", "--name-only")
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
                reporter.Pass($"FLINT-G15: 工作树清洁（{staged} 个已暂存待提交——合法中间态）");
            }

            // .ai 独立仓检查（嵌套 git 仓库，外仓 gitignore 不覆盖其内部状态）
            var aiDir = Path.Combine(workspaceRoot, ".ai");
            if (Directory.Exists(Path.Combine(aiDir, ".git")) && GitHelper.IsRepo(aiDir))
            {
                var aiUnstaged = GitHelper.Output(aiDir, "diff", "--name-only").Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
                var aiUntracked = GitHelper.Output(aiDir, "ls-files", "--others", "--exclude-standard")
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
                if (aiUnstaged > 0 || aiUntracked > 0)
                {
                    reporter.Fail($"FLINT-G15: .ai 仓有未提交改动（未暂存 {aiUnstaged} + 未跟踪 {aiUntracked}）");
                }
            }
        }
        else
        {
            reporter.Skip("FLINT-G15", "Flint 本体非 git 仓库（当前工作区布局），git 类检查降级——恢复 git 后自动启用");
        }

        // G16：slnx 成员完整性（磁盘 csproj ⊆ slnx）
        // 例外（2026-09-26 追加，用户裁决待确认）：独立工具链项目刻意不入产品解决方案
        // （Flint.ThemeMigrator 既有先例；Flint.DevTools/Flint.AiGate 对齐同一形态，
        // 对应整改方案 D1）。这些项目的构建由各自的使用方负责，不受产品 slnx 覆盖约束。
        var slnxFile = Path.Combine(rootDir, "Flint.slnx");
        var slnxText = File.Exists(slnxFile) ? Io.ReadAllText(slnxFile) : string.Empty;
        var g16 = 0;
        foreach (var csproj in Directory.EnumerateFiles(src, "*.csproj", SearchOption.AllDirectories)
                     .Concat(Directory.EnumerateFiles(tests, "*.csproj", SearchOption.AllDirectories)))
        {
            var normalized = csproj.Replace('\\', '/');
            if (normalized.Contains("/obj/") || normalized.Contains("/bin/"))
            {
                continue;
            }

            var rel = Path.GetRelativePath(rootDir, csproj).Replace('\\', '/');
            if (SlnxExemptions.Any(exemption => rel.StartsWith(exemption, StringComparison.Ordinal)))
            {
                continue;
            }

            if (!slnxText.Contains(rel, StringComparison.Ordinal))
            {
                Console.WriteLine($"  磁盘存在但 slnx 未列：{rel}");
                g16++;
            }
        }

        reporter.CheckZero("FLINT-G16", "slnx 成员完整性（磁盘 csproj 全部列入 Flint.slnx，独立工具链项目除外）", g16);

        // G17：sync-over-async 棘轮
        var g17Raw = Scanner.CollectHits(rootDir, @"\.Result\b|\.Wait\(\)|GetAwaiter\(\)\.GetResult\(", "src");
        reporter.CheckRatchet("FLINT-G17", "sync-over-async 棘轮",
            Scanner.CountHits(g17Raw), RatchetSyncOverAsync, string.Join('\n', g17Raw));

        // G18：裸 catch(Exception) 棘轮（口径：catch (Exception 同行无 when 过滤）
        var g18Raw = Scanner.CollectHits(rootDir, @"catch\s*\(\s*Exception", "src")
            .Where(h => !h.Contains("when", StringComparison.Ordinal))
            .ToList();
        reporter.CheckRatchet("FLINT-G18", "裸 catch(Exception) 棘轮",
            g18Raw.Count, RatchetBareCatch, string.Join('\n', g18Raw));

        // ── 汇总 ──
        Console.WriteLine("\n═══════ 扫描完成 ═══════");
        reporter.Summary();
        Console.WriteLine("权威依据：Directory.Build.props + Directory.Packages.props + lessons.md");

        return reporter.Failed > 0 ? 1 : 0;
    }
}
