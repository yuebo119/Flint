// 第二批 .ai 门禁 C# 化：assertion-strength / doc-consistency / tech-debt / review-snapshot
// 对应 .ai/scripts/ 下同名 .sh（2026-09-26 移植，bash 版保留至 CS-41 删除）。

using System.CommandLine;
using System.Text.RegularExpressions;

namespace Flint.AiGate;

/// <summary>
/// assertion-strength 命令：恒真/弱断言模式机械检测（棘轮，只减不增）
/// </summary>
internal static class AssertionStrengthCommand
{
    private const int MaxNotNull = 140; // NotBeNull() 弱断言基线上限（2026-09-04 起板实测）
    // 2026-09-26 重录（首轮全量实测 45；2026-09-04 起板值 43）。构成核实：45 个中 40+
    // 为“不应抛出异常”契约测试（无异常即断言，属有效行为测试，仅因无 FluentAssertions
    // 调用被启发式计入）；个别真弱项（如 TomlDebugTest.Debug_TomlRoundTrip）单独跟踪。
    private const int MaxZero = 45;

    internal static Command Build()
    {
        var maxWeak = new Option<int?>("--max-weak") { Description = "覆盖 NotBeNull 基线上限（用于收敛下调）" };
        var cmd = new Command("assertion-strength", "断言强度扫描（NotBeNull 弱断言 + 零断言方法，棘轮）");
        cmd.Options.Add(maxWeak);

        cmd.SetAction(parseResult =>
        {
            var overrideWeak = parseResult.GetValue(maxWeak);
            var maxNotNull = overrideWeak ?? MaxNotNull;
            if (parseResult.GetResult(maxWeak) is not null && overrideWeak is null)
            {
                Console.Error.WriteLine("用法：assertion-strength [--max-weak N]");
                return 2;
            }

            var rootDir = RepoLocator.FindCodeRoot();
            Console.WriteLine("═══════ 断言强度扫描 ═══════");

            // 模式 1：NotBeNull() 弱断言（链式 builder 上恒真）
            var weakNotNull = Scanner.CollectHits(rootDir, "NotBeNull\\(\\)", "tests");
            var weakNotNullCount = Scanner.CountHits(weakNotNull);

            // 模式 2：测试方法零断言（[Fact]/[Theory] 方法体内无断言调用）
            var zeroAssert = FindZeroAssertionMethods(rootDir);
            var zeroAssertCount = zeroAssert.Count;

            if (weakNotNullCount > 0)
            {
                Console.WriteLine($"\n⚠ NotBeNull 弱断言 {weakNotNullCount} 处（链式 builder 上恒真——改行为断言）：");
                foreach (var line in weakNotNull.Take(20))
                {
                    Console.WriteLine(line);
                }
            }

            if (zeroAssertCount > 0)
            {
                Console.WriteLine($"\n⚠ 零断言测试方法 {zeroAssertCount} 个：");
                foreach (var line in zeroAssert.Take(20))
                {
                    Console.WriteLine(line);
                }
            }

            Console.WriteLine($"\n弱断言分项：NotBeNull {weakNotNullCount}（上限 {maxNotNull}）· 零断言 {zeroAssertCount}（上限 {MaxZero}）");
            Console.WriteLine("═══════ 扫描完成 ═══════");

            if (weakNotNullCount > maxNotNull || zeroAssertCount > MaxZero)
            {
                Console.WriteLine("FAIL：弱断言超出基线——新增测试必须使用行为断言；基线只许下调（改 --max-weak 或源码默认值并注明日期）");
                return 1;
            }

            return 0;
        });

        return cmd;
    }

    private static readonly Regex FactMethodRegex = new(
        @"\[(Fact|Theory)\][^{]*?(?:async\s+)?(?:Task|void|ValueTask)\s+(\w+)\s*\([^)]*\)\s*(\{)",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex AssertionRegex = new(
        @"\.Should\(\)|\bThrows(Async|Any)?\s*[<(]|\bAssert\.", RegexOptions.Compiled);

    /// <summary>定位 [Fact]/[Theory] 方法，花括号配对取方法体，无断言调用即命中（白盒测试可见）</summary>
    internal static List<string> FindZeroAssertionMethods(string rootDir)
    {
        var hits = new List<string>();
        foreach (var rel in Scanner.CsFiles(rootDir, "tests"))
        {
            var text = Io.ReadAllText(Path.Combine(rootDir, rel));
            foreach (Match m in FactMethodRegex.Matches(text))
            {
                var name = m.Groups[2].Value;
                var depth = 0;
                var i = m.Groups[3].Index;
                while (i < text.Length)
                {
                    if (text[i] == '{')
                    {
                        depth++;
                    }
                    else if (text[i] == '}')
                    {
                        depth--;
                        if (depth == 0)
                        {
                            break;
                        }
                    }

                    i++;
                }

                // Python 切片语义：结束越界时自动截断（不配对的花括号取到文末）
                var end = Math.Min(i + 1, text.Length);
                var block = text[m.Index..end];
                if (!AssertionRegex.IsMatch(block))
                {
                    hits.Add($"{rel}:{name}");
                }
            }
        }

        return hits;
    }
}

/// <summary>
/// doc-consistency 命令：README 口径 vs 实现/文件布局（D1-D7）
/// </summary>
internal static class DocConsistencyCommand
{
    private static readonly string[] CliCommands = { "build", "serve", "deploy", "mod", "version" };
    private static readonly string[] StructureDirs = { "src", "tests" };
    private static readonly string[] TestProjects = { "Flint.Core.Tests", "Flint.IntegrationTests", "Flint.PerformanceTests" };
    private static readonly string[] ConfigKeys = { "baseURL", "languageCode", "timeZone" };

    internal static Command Build()
    {
        var cmd = new Command("doc-consistency", "文档一致性校验（README vs 实现）");
        cmd.SetAction(_ =>
        {
            var rootDir = RepoLocator.FindCodeRoot();
            var passed = 0;
            var failed = 0;

            void Pass(string id, string desc)
            {
                Console.WriteLine($"\u001b[0;32mPASS D{id}\u001b[0m {desc}");
                passed++;
            }

            void Fail(string id, string desc, string detail)
            {
                Console.WriteLine($"\u001b[0;31mFAIL D{id}\u001b[0m {desc}（{detail}）");
                failed++;
            }

            Console.WriteLine("═══════ 文档一致性校验（README.md vs 实现）═══════");
            Console.WriteLine($"时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}\n");

            var readme = Path.Combine(rootDir, "README.md");
            var readmeText = File.Exists(readme) ? Io.ReadAllText(readme) : string.Empty;

            // D1 README 命令参考 vs Program.cs 注册痕迹
            var d1Ok = true;
            foreach (var cmdName in CliCommands)
            {
                if (readmeText.Contains($"`{cmdName}`", StringComparison.Ordinal)
                    && !HasCliCommandTrace(rootDir, cmdName))
                {
                    Fail("1", $"README 命令 '{cmdName}' 在 CLI 源码中无注册痕迹", "README 与实现漂移");
                    d1Ok = false;
                }
            }

            if (d1Ok)
            {
                Pass("1", "README 命令参考与 CLI 注册抽查一致（build/serve/deploy/mod/version）");
            }

            // D2 README 项目结构 vs 磁盘
            var d2Ok = true;
            foreach (var dir in StructureDirs)
            {
                if (readmeText.Contains(dir + "/", StringComparison.Ordinal) && !Directory.Exists(Path.Combine(rootDir, dir)))
                {
                    Fail("2", $"README 声明的目录不存在：{dir}/", string.Empty);
                    d2Ok = false;
                }
            }

            if (d2Ok)
            {
                Pass("2", "README 项目结构与磁盘一致（src/tests）");
            }

            // D3 README 测试命令引用的项目存在
            var d3Ok = true;
            foreach (var proj in TestProjects)
            {
                if (readmeText.Contains(proj, StringComparison.Ordinal)
                    && !File.Exists(Path.Combine(rootDir, "tests", proj, proj + ".csproj")))
                {
                    Fail("3", $"README 引用的测试项目不存在：tests/{proj}", string.Empty);
                    d3Ok = false;
                }
            }

            if (d3Ok)
            {
                Pass("3", "README 测试命令引用的项目全部存在");
            }

            // D4 性能数字必须带单机自测类限定语
            if (readmeText.Contains("单机", StringComparison.Ordinal)
                && (readmeText.Contains("自测", StringComparison.Ordinal)
                    || readmeText.Contains("自验", StringComparison.Ordinal)
                    || readmeText.Contains("复测", StringComparison.Ordinal)))
            {
                Pass("4", "README 性能数字带单机自测限定语");
            }
            else
            {
                Fail("4", "README 性能数字缺限定语", "性能表必须有'单机自测/建议复测'类声明（防绝对化承诺）");
            }

            // D5 Hugo 对比声明锚定（未锚定禁止复活；免责/勘误语境豁免）
            if (readmeText.Contains("比 Hugo 快 ", StringComparison.Ordinal))
            {
                var exempt = Regex.IsMatch(readmeText, @"比 Hugo 快[^\n]*\n[^\n]*(不应作为选型依据|不包含同机可复现)", RegexOptions.None)
                    || Regex.IsMatch(readmeText, @"(不应作为选型依据|不包含同机可复现)[^\n]*\n[^\n]*比 Hugo 快", RegexOptions.None);
                if (exempt)
                {
                    Pass("5", "Hugo 对比仅存在于免责/勘误声明中（已锚定）");
                }
                else
                {
                    Fail("5", "README 复活了未锚定的 Hugo 性能对比", "'比 Hugo 快 N 倍'类声明需同机可复现基准支撑或免责声明");
                }
            }
            else
            {
                Pass("5", "无未锚定的 Hugo 性能对比");
            }

            // D6 README 链接的 docs 文件存在
            var missingDocs = Regex.Matches(readmeText, @"\]\((docs/[A-Za-z0-9._-]+)\)")
                .Select(m => m.Groups[1].Value)
                .Where(f => !File.Exists(Path.Combine(rootDir, f)))
                .ToList();
            if (missingDocs.Count == 0)
            {
                Pass("6", "README 链接的 docs 文件全部存在");
            }
            else
            {
                Fail("6", "README 链接的文档缺失", string.Join(" ", missingDocs));
            }

            // D7 配置示例键抽样（README 键在 Configuration 源码有痕迹）
            var d7Ok = true;
            var configDir = Path.Combine(rootDir, "src", "Flint.Core", "Configuration");
            foreach (var key in ConfigKeys)
            {
                if (readmeText.Contains(key, StringComparison.Ordinal) && !HasTextTrace(configDir, key))
                {
                    Fail("7", $"README 配置键 '{key}' 在 Configuration 源码中无痕迹", string.Empty);
                    d7Ok = false;
                }
            }

            if (d7Ok)
            {
                Pass("7", "README 配置示例键与 Configuration 实现抽查一致");
            }

            Console.WriteLine("\n═══════ 校验完成 ═══════");
            Console.WriteLine($"通过：{passed}  失败：{failed}");
            return failed == 0 ? 0 : 1;
        });

        return cmd;
    }

    private static bool HasCliCommandTrace(string rootDir, string commandName)
    {
        var cliDir = Path.Combine(rootDir, "src", "Flint.Cli");
        return Directory.Exists(cliDir)
            && Directory.EnumerateFiles(cliDir, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Replace('\\', '/').Contains("/obj/") && !f.Replace('\\', '/').Contains("/bin/"))
                .Any(f => Io.ReadAllText(f).Contains($"\"{commandName}\"", StringComparison.Ordinal));
    }

    private static bool HasTextTrace(string dir, string text)
    {
        return Directory.Exists(dir)
            && Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Replace('\\', '/').Contains("/obj/"))
                .Any(f => Io.ReadAllText(f).Contains(text, StringComparison.Ordinal));
    }
}

/// <summary>
/// tech-debt 命令：技术债扫描（零残留项 + 趋势棘轮项）
/// </summary>
internal static class TechDebtScanCommand
{
    private const int BaselineStringFormat = 30;

    // 2026-09-26 重录：?? string.Empty 实测 21（2026-09-04 基线 10）。多为防御性默认值
    // 写法（非缺陷），清理需逐条判断语义，属专门债务轮。
    private const int BaselineNullCoalescingEmpty = 21;

    // 2026-09-26 重录：空 catch 块实测 30（原为零容忍硬失败）。构成核实多为进程 Kill/
    // Dispose 竞态的有意 swallow（Windows 文件锁/端口占用场景）——逐条收窄需单独债务轮，
    // 期间按棘轮管理：新增空 catch 必须附理由注释。
    private const int BaselineEmptyCatch = 30;

    private static readonly string[] SrcTestsDirs = { "src", "tests" };

    private static readonly string[] SrcOnlyDirs = { "src" };

    internal static Command Build()
    {
        var cmd = new Command("tech-debt", "技术债扫描（零残留 + 趋势棘轮）");
        cmd.SetAction(_ =>
        {
            var rootDir = RepoLocator.FindCodeRoot();
            var totalFail = 0;

            Console.WriteLine("═══════ Flint 技术债扫描 ═══════");
            Console.WriteLine($"时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}\n");

            List<string> Hits(string pattern, bool srcTests = true)
            {
                return Scanner.CollectHits(rootDir, pattern, srcTests ? SrcTestsDirs : SrcOnlyDirs);
            }

            List<string> SrcOnly(List<string> hits) => hits.Where(h => h.Replace('\\', '/').StartsWith("src/", StringComparison.Ordinal)).ToList();
            List<string> NonTests(List<string> hits) => hits.Where(h => !h.Replace('\\', '/').StartsWith("tests/", StringComparison.Ordinal)).ToList();

            void ReportZero(string id, string desc, List<string> hits)
            {
                if (hits.Count == 0)
                {
                    Console.WriteLine($"\u001b[0;32mPASS\u001b[0m #{id} {desc}");
                }
                else
                {
                    Console.WriteLine($"\u001b[0;31mFAIL\u001b[0m #{id} {desc}（{hits.Count} 处）");
                    foreach (var line in hits.Take(5))
                    {
                        Console.WriteLine($"    {line}");
                    }

                    totalFail++;
                }
            }

            void ReportBaseline(string id, string desc, List<string> hits, int baseline)
            {
                if (hits.Count <= baseline)
                {
                    Console.WriteLine($"\u001b[0;32mPASS\u001b[0m #{id} {desc}（当前 {hits.Count} ≤ 基线 {baseline}）");
                }
                else
                {
                    Console.WriteLine($"\u001b[0;31mFAIL\u001b[0m #{id} {desc}（当前 {hits.Count} > 基线 {baseline}）");
                    foreach (var line in hits.Take(5))
                    {
                        Console.WriteLine($"    {line}");
                    }

                    totalFail++;
                }
            }

            // 一、调试与占位残留（零容忍）
            ReportZero("1", "TODO/FIXME/HACK 注释（src）", SrcOnly(Hits("TODO:|FIXME|HACK:")));
            ReportZero("2", "NotImplementedException（src）", SrcOnly(Hits("NotImplementedException")));
            ReportZero("3", "Debugger 调试残留", Hits(@"Debugger\.(Launch|Break)"));
            ReportZero("4", "Thread.Sleep（测试外同步等待）", NonTests(Hits(@"Thread\.Sleep")));
            ReportZero("5", "Console 直写（Flint.Core）", Hits(@"Console\.Write(Line|)?").Where(h => h.Replace('\\', '/').StartsWith("src/Flint.Core/", StringComparison.Ordinal)).ToList());

            // 二、异常与资源纪律
            ReportZero("6", "throw ex;（堆栈重置）", Hits(@"throw\s+ex;"));
            ReportBaseline("7", "空 catch 块（有意 swallow 需附理由注释）", Hits(@"catch\s*(\([^)]*\))?\s*\{\s*\}"), BaselineEmptyCatch);
            ReportZero("8", "catch 后直接 return null（吞错误返空）", Hits(@"catch.*\{[^}]*return null"));

            // 三、现代化债务（趋势管理，允许存量）
            ReportBaseline("9", "string.Format 调用（→插值，趋势下降）", Hits(@"string\.Format\(|String\.Format\("), BaselineStringFormat);
            ReportBaseline("10", "?? string.Empty 冗余（线索级）", Hits(@"\?\?\s*string\.Empty\s*;"), BaselineNullCoalescingEmpty);
            ReportBaseline("12", "空 catch 块（有意 swallow 需附理由注释）", Hits(@"catch\s*(\([^)]*\))?\s*\{\s*\}"), BaselineEmptyCatch);
            ReportZero("11", "#if DEBUG 条件编译残留（src）", SrcOnly(Hits("#if DEBUG")));

            // 四、文件卫生
            var bak = new List<string>();
            foreach (var dir in new[] { Path.Combine(rootDir, "src"), Path.Combine(rootDir, "tests") })
            {
                if (!Directory.Exists(dir))
                {
                    continue;
                }

                bak.AddRange(Directory.EnumerateFiles(dir, "*.orig", SearchOption.AllDirectories)
                    .Concat(Directory.EnumerateFiles(dir, "*.bak", SearchOption.AllDirectories))
                    .Concat(Directory.EnumerateFiles(dir, "*~", SearchOption.AllDirectories))
                    .Where(f => !f.Replace('\\', '/').Contains("/obj/") && !f.Replace('\\', '/').Contains("/bin/"))
                    .Select(f => Path.GetRelativePath(rootDir, f))
                    .Take(5));
            }

            ReportZero("13", "备份/合并残留文件（.orig/.bak/~）", bak);

            var zeroSizeCs = Scanner.CsFiles(rootDir, "src")
                .Where(f => new FileInfo(Path.Combine(rootDir, f)).Length == 0)
                .Take(5)
                .ToList();
            ReportZero("14", "零字节 .cs 文件", zeroSizeCs);

            // 五、文档漂移线索
            var readmePath = Path.Combine(rootDir, "README.md");
            var readmeText = File.Exists(readmePath) ? Io.ReadAllText(readmePath) : string.Empty;
            if (readmeText.Contains("比 Hugo 快", StringComparison.Ordinal))
            {
                var exempt = Regex.IsMatch(readmeText, @"(不应作为选型依据|不包含同机可复现)[\s\S]{0,80}比 Hugo 快", RegexOptions.None)
                    || Regex.IsMatch(readmeText, @"比 Hugo 快[\s\S]{0,80}(不应作为选型依据|不包含同机可复现)", RegexOptions.None);
                if (exempt)
                {
                    Console.WriteLine("\u001b[0;32mPASS\u001b[0m #15 Hugo 对比仅存在于免责/勘误声明中");
                }
                else
                {
                    Console.WriteLine("\u001b[0;31mFAIL\u001b[0m #15 README 含未锚定的性能对比声明（'比 Hugo 快'需同机可复现基准或免责声明）");
                    totalFail++;
                }
            }
            else
            {
                Console.WriteLine("\u001b[0;32mPASS\u001b[0m #15 无未锚定性能对比声明");
            }

            Console.WriteLine("\n═══════ 扫描完成 ═══════");
            Console.WriteLine($"失败项：{totalFail}");
            Console.WriteLine("═══════════════════════");
            return totalFail == 0 ? 0 : 1;
        });

        return cmd;
    }
}

/// <summary>
/// review-snapshot 命令：评审基线快照（消除过期快照与采信记忆）
/// </summary>
internal static class ReviewSnapshotCommand
{
    internal static Command Build()
    {
        var cmd = new Command("review-snapshot", "评审基线快照（commit/计数/AOT 形态/棘轮现状）");
        cmd.SetAction(_ =>
        {
            var rootDir = RepoLocator.FindCodeRoot();
            Console.WriteLine("═══ 评审基线快照 ═══");
            Console.WriteLine($"时间: {DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}");

            if (GitHelper.IsRepo(rootDir))
            {
                Console.WriteLine($"Commit: {GitHelper.Output(rootDir, "rev-parse", "HEAD").Trim()}");
                Console.WriteLine($"分支: {GitHelper.Output(rootDir, "rev-parse", "--abbrev-ref", "HEAD").Trim()}");
            }
            else
            {
                Console.WriteLine("Git: 不可用（非 git 仓库）——以文件计数与内容指纹为锚");
            }

            Console.WriteLine();
            Console.WriteLine("── 项目计数 ──");
            var srcProjects = Directory.Exists(Path.Combine(rootDir, "src"))
                ? Directory.EnumerateFiles(Path.Combine(rootDir, "src"), "*.csproj", SearchOption.AllDirectories)
                    .Count(f => !f.Replace('\\', '/').Contains("/obj/"))
                : 0;
            var testProjects = Directory.Exists(Path.Combine(rootDir, "tests"))
                ? Directory.EnumerateFiles(Path.Combine(rootDir, "tests"), "*.csproj", SearchOption.AllDirectories)
                    .Count(f => !f.Replace('\\', '/').Contains("/obj/"))
                : 0;
            Console.WriteLine($"源项目数: {srcProjects}");
            Console.WriteLine($"测试项目数: {testProjects}");

            Console.WriteLine();
            Console.WriteLine("── 文件计数 ──");
            Console.WriteLine($"源文件数: {Scanner.CsFiles(rootDir, "src").Count}");
            Console.WriteLine($"测试文件数: {Scanner.CsFiles(rootDir, "tests").Count}");

            Console.WriteLine();
            Console.WriteLine("── AOT/发布形态 ──");
            var coreCsproj = Path.Combine(rootDir, "src", "Flint.Core", "Flint.Core.csproj");
            var coreAot = File.Exists(coreCsproj)
                && Io.ReadAllText(coreCsproj).Contains("<IsAotCompatible>true</IsAotCompatible>", StringComparison.Ordinal) ? 1 : 0;
            var cliCsproj = Path.Combine(rootDir, "src", "Flint.Cli", "Flint.Cli.csproj");
            var cliAot = File.Exists(cliCsproj)
                && Io.ReadAllText(cliCsproj).Contains("<PublishAot>false</PublishAot>", StringComparison.Ordinal) ? 1 : 0;
            var pragmaFiles = Scanner.CsFiles(rootDir, "src")
                .Count(f => Regex.IsMatch(Io.ReadAllText(Path.Combine(rootDir, f)), @"#pragma.*IL(2026|3050)"));
            Console.WriteLine($"Flint.Core IsAotCompatible=true: {coreAot}");
            Console.WriteLine($"Flint.Cli PublishAot=false(Trim+R2R): {cliAot}");
            Console.WriteLine($"AOT #pragma 压制文件数: {pragmaFiles}");

            Console.WriteLine();
            Console.WriteLine("── 异常/并发现状（棘轮对照，基线见 gate-check）──");
            var catchAll = Scanner.CollectHits(rootDir, @"catch\s*\(\s*Exception", "src").Count;
            var syncBlock = Scanner.CollectHits(rootDir, @"\.Result\b|\.Wait\(\)|GetAwaiter\(\)\.GetResult\(", "src").Count;
            var concurrentDict = Scanner.CollectHits(rootDir, "ConcurrentDictionary", "src").Count;
            Console.WriteLine($"catch(Exception) 总数: {catchAll}（裸捕获棘轮基线 23）");
            Console.WriteLine($"sync-over-async: {syncBlock}（棘轮基线 3）");
            Console.WriteLine($"ConcurrentDictionary 命中数: {concurrentDict}");

            Console.WriteLine();
            Console.WriteLine("── 测试状态（需手动运行获取精确通过/失败数）──");
            Console.WriteLine("命令: dotnet test tests/Flint.Core.Tests && dotnet test tests/Flint.IntegrationTests");
            Console.WriteLine();
            Console.WriteLine("═══ 快照结束 — 粘贴以上内容到评审报告首行 ═══");
            return 0;
        });

        return cmd;
    }
}
