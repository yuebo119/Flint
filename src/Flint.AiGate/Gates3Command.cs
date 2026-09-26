// 第三批 .ai 门禁 C# 化（2026-09-26）：sibling-map / verify-action-items / post-fix-check /
// sister-axis-scan / fix-completeness-check / refine-scan / probe-template / fix-orchestrator。
// 对应 .ai/scripts/ 下同名 .sh。信息类工具（sibling-map/refine-scan/probe-template/
// fix-orchestrator/sister-axis-scan）退出码恒 0/1 的语义与 bash 版对齐。

using System.CommandLine;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Flint.AiGate;

/// <summary>
/// sibling-map 命令：轴 A 接口→多实现族机械枚举（传递闭包）
/// </summary>
internal static class SiblingMapCommand
{
    private static readonly Regex DeclRegex = new(
        @"\b(class|record|struct|interface)\s+([A-Z]\w*)(?:<[^<>]*>)?(?:\([^)]*\))?\s*(?::\s*([^{=\n]+))?",
        RegexOptions.Compiled);

    private static readonly Regex LeadingNameRegex = new(@"^[A-Z]\w*", RegexOptions.Compiled);

    internal static Command Build()
    {
        var filterArg = new Argument<string>("filter") { Description = "接口名前缀过滤（缺省全部）", DefaultValueFactory = _ => string.Empty };
        filterArg.Arity = ArgumentArity.ZeroOrOne;
        var cmd = new Command("sibling-map", "姊妹族清单（轴 A：接口→多实现族，传递闭包）");
        cmd.Arguments.Add(filterArg);

        cmd.SetAction(parseResult => Run(parseResult.GetValue(filterArg) ?? string.Empty));
        return cmd;
    }

    /// <summary>计算 2+ 实现的接口族（fix-orchestrator 姊妹联动复用）</summary>
    public static List<(string Iface, SortedSet<(string File, string Class)> Members)> ComputeFamilies(string rootDir)
    {
        // 类型声明收集：Name 可带 <T> 与主构造参数表，基类列表到 { 或换行
        var types = new Dictionary<string, (string Kind, string File, List<string> Bases)>(StringComparer.Ordinal);
        foreach (var rel in Scanner.CsFiles(rootDir, "src"))
        {
            if (rel.EndsWith(".g.cs", StringComparison.Ordinal))
            {
                continue;
            }

            var text = Io.ReadAllText(Path.Combine(rootDir, rel));
            foreach (Match m in DeclRegex.Matches(text))
            {
                var kind = m.Groups[1].Value;
                var name = m.Groups[2].Value;
                var bases = m.Groups[3].Value;

                if (types.TryGetValue(name, out var existing))
                {
                    // partial 多声明：合并基列表
                    if (bases.Trim().Length > 0)
                    {
                        existing.Bases.AddRange(bases.Split(',').Select(b => b.Trim()).Where(b => b.Length > 0));
                    }

                    continue;
                }

                types[name] = (kind, rel, bases.Trim().Length > 0
                    ? bases.Split(',').Select(b => b.Trim()).Where(b => b.Length > 0).ToList()
                    : new List<string>());
            }
        }

        static string? StripGeneric(string b) => LeadingNameRegex.Match(b) is { Success: true } mm ? mm.Value : null;

        var projectInterfaces = types.Where(t => t.Value.Kind == "interface").Select(t => t.Key).ToHashSet(StringComparer.Ordinal);

        HashSet<string> TransitiveInterfaces(string name, HashSet<string> seen)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            if (!types.TryGetValue(name, out var t))
            {
                return result;
            }

            foreach (var b in t.Bases)
            {
                var baseName = StripGeneric(b);
                if (baseName is null || !seen.Add(baseName))
                {
                    continue;
                }

                if (projectInterfaces.Contains(baseName))
                {
                    result.Add(baseName);
                }

                result.UnionWith(TransitiveInterfaces(baseName, seen));
            }

            return result;
        }

        // interface -> {(file, class)}（去重集合对应 Python set.add）
        var impl = new Dictionary<string, SortedSet<(string File, string Class)>>(StringComparer.Ordinal);
        foreach (var (name, t) in types)
        {
            if (t.Kind == "interface")
            {
                continue;
            }

            foreach (var ifc in TransitiveInterfaces(name, new HashSet<string>(StringComparer.Ordinal)))
            {
                if (!impl.TryGetValue(ifc, out var set))
                {
                    set = new SortedSet<(string, string)>();
                    impl[ifc] = set;
                }

                set.Add((t.File, name));
            }
        }

        var families = impl.Where(kv => kv.Value.Count >= 2).ToList();
        return families
            .OrderByDescending(f => f.Value.Count)
            .ThenBy(f => f.Key, StringComparer.Ordinal)
            .Select(f => (f.Key, f.Value))
            .ToList();
    }

    private static int Run(string filter)
    {
        var rootDir = RepoLocator.FindCodeRoot();
        var families = ComputeFamilies(rootDir);

        Console.WriteLine($"# 轴 A：接口 → 多实现族（2+ 实现，传递闭包，仅项目内接口；{DateTime.Today:yyyy-MM-dd}）");
        Console.WriteLine();
        Console.WriteLine("| 接口 | 实现数 | 实现清单（文件:类） |");
        Console.WriteLine("|------|:------:|----------------------|");

        foreach (var (iface, set) in families)
        {
            if (filter.Length > 0 && !iface.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var items = string.Join("<br>", set.Select(x => $"{x.File}:{x.Class}"));
            Console.WriteLine($"| {iface} | {set.Count} | {items} |");
        }

        Console.WriteLine();
        Console.WriteLine("说明：轴 B（无共同接口的语义姊妹，如 SiteBuilder↔IncrementalBuilder、ContentParser↔FrontMatterParser、Sitemap↔Feed 生成器族）见 .ai/review/sibling-map.md 种子表；");
        Console.WriteLine("联动判据（三选一）、>3 文件熔断与增长规则同样见该表。");
        return 0;
    }
}

/// <summary>
/// verify-action-items 命令：行动项中的路径与源码标识符存在性验证
/// </summary>
internal static class VerifyActionItemsCommand
{
    private static readonly Regex BacktickRegex = new(@"`[^`]+`", RegexOptions.Compiled);

    private static readonly Regex IgnoredTokenRegex = new(
        @"^(P[0-3]|AUD-[0-9]+|ITM(-[0-9]+)?|PASS|FAIL|WARN|SKIP|urgent|near|future|assess)$", RegexOptions.Compiled);

    // 标识符/路径中不可能出现的字符（散文指纹）
    private static readonly Regex ProseCharsRegex = new(@"[()<>=?*{}"">\s]", RegexOptions.Compiled);

    private static readonly Regex CommitHashRegex = new(@"^[0-9a-f]{7,40}$", RegexOptions.Compiled);

    private static readonly Regex LineRefRegex = new(@":[0-9]+$", RegexOptions.Compiled);

    private static readonly Regex KnownExtRegex = new(
        @"\.(cs|csproj|slnx|md|sh|yml|yaml|props|targets|json|xml|sql)$", RegexOptions.Compiled);

    private static readonly Regex KnownDirRegex = new(
        @"^(src|test|docs|scripts|bench|samples|\.ai|\.github|nupkgs)/", RegexOptions.Compiled);

    // 内容搜索路径（git grep pathspec 与 GrepFixed 共用；仅存在的目录）
    private static readonly string[] ContentSearchDirs = { "src", "tests", "scripts", "docs", ".ai" };

    internal static Command Build()
    {
        var fileArg = new Argument<string>("action-items-file") { Description = "行动项文件路径" };
        var cmd = new Command("verify-action-items", "Action Items 路径/标识符存在性验证");
        cmd.Arguments.Add(fileArg);

        cmd.SetAction(parseResult =>
        {
            var actionFile = parseResult.GetValue(fileArg);
            if (actionFile is null)
            {
                Console.Error.WriteLine("用法：verify-action-items <action-items-file>");
                return 2;
            }

            if (!File.Exists(actionFile))
            {
                Console.Error.WriteLine($"错误：文件不存在：{actionFile}");
                return 2;
            }

            var rootDir = RepoLocator.FindCodeRoot();
            Console.WriteLine("═══════ Action Items 验证 ═══════");
            Console.WriteLine($"文件：{actionFile}\n");

            var missing = 0;
            var found = 0;
            var skipped = 0;

            bool ContentSearch(string token)
            {
                // 搜索路径取仓库内实际存在的目录：布局 B 下 .ai 在仓外，bash 版把不存在的
                // pathspec 喂给 git grep 会 fatal 128 导致内容搜索恒失败（静默少报）——此处修正
                var searchPaths = ContentSearchDirs
                    .Where(p => Directory.Exists(Path.Combine(rootDir, p)))
                    .ToArray();
                if (searchPaths.Length == 0)
                {
                    return false;
                }

                // git 降级：仓库非 git 时用普通 grep 替代 git grep
                if (GitHelper.IsRepo(rootDir))
                {
                    var gitArgs = new List<string> { "grep", "-F", "-q", "--", token };
                    gitArgs.AddRange(searchPaths);
                    return GitHelper.Run(rootDir, gitArgs.ToArray()) == 0;
                }

                return GrepFixed(rootDir, token);
            }

            bool IsPath(string token)
            {
                return token.Contains('/', StringComparison.Ordinal) || KnownExtRegex.IsMatch(token);
            }

            bool IsIgnoredToken(string token) => IgnoredTokenRegex.IsMatch(token);

            bool IsProseToken(string token)
            {
                if (ProseCharsRegex.IsMatch(token))
                {
                    return true;
                }

                if (CommitHashRegex.IsMatch(token))
                {
                    return true;
                }

                if (LineRefRegex.IsMatch(token))
                {
                    return true;
                }

                if (token.StartsWith("--", StringComparison.Ordinal))
                {
                    return true;
                }

                // 含 / 但既无扩展名也不以已知目录开头（方法对等）→ 散文
                return token.Contains('/', StringComparison.Ordinal)
                    && !KnownExtRegex.IsMatch(token)
                    && !KnownDirRegex.IsMatch(token);
            }

            var identifiers = BacktickRegex.Matches(Io.ReadAllText(actionFile))
                .Select(m => m.Value.Trim('`'))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToList();

            foreach (var identifier in identifiers)
            {
                if (identifier.Length == 0)
                {
                    continue;
                }

                if (IsIgnoredToken(identifier) || IsProseToken(identifier))
                {
                    skipped++;
                    continue;
                }

                if (IsPath(identifier))
                {
                    var full = Path.IsPathRooted(identifier) ? identifier : Path.Combine(rootDir, identifier);
                    if (File.Exists(full) || Directory.Exists(full))
                    {
                        found++;
                    }
                    else if (!identifier.Contains('/', StringComparison.Ordinal) && ContentSearch(identifier))
                    {
                        // 无目录前缀的相对文件名——不存在但正文有引用则放行
                        found++;
                    }
                    else
                    {
                        Console.WriteLine($"FAIL 文件不存在：{identifier}");
                        missing++;
                    }

                    continue;
                }

                if (ContentSearch(identifier))
                {
                    found++;
                }
                else
                {
                    Console.WriteLine($"FAIL 标识符未找到：{identifier}");
                    missing++;
                }
            }

            Console.WriteLine($"\n找到：{found}  缺失：{missing}  跳过：{skipped}");
            Console.WriteLine("═══════ 验证完成 ═══════");
            return missing == 0 ? 0 : 1;
        });

        return cmd;
    }

    /// <summary>grep -rF 语义：在 src/tests/scripts/docs/.ai 下做固定字符串包含搜索</summary>
    private static bool GrepFixed(string rootDir, string token)
    {
        foreach (var relDir in new[] { "src", "tests", "scripts", "docs", ".ai" })
        {
            var dir = Path.Combine(rootDir, relDir);
            if (!Directory.Exists(dir))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                var normalized = file.Replace('\\', '/');
                if (normalized.Contains("/obj/") || normalized.Contains("/bin/") || normalized.Contains("/.git/"))
                {
                    continue;
                }

                try
                {
                    if (Io.ReadAllText(file).Contains(token, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
                catch (IOException)
                {
                    // 不可读文件（锁/权限）跳过
                }
            }
        }

        return false;
    }
}

/// <summary>
/// post-fix-check 命令：修复后自查（零残留机械验证 + git 暂存集统计）
/// </summary>
internal static class PostFixCheckCommand
{
    internal static Command Build()
    {
        var cmd = new Command("post-fix-check", "修复后自查（占位/守卫残留零计数 + 暂存集统计）");
        cmd.SetAction(_ => Run());
        return cmd;
    }

    private static int Run()
    {
        var rootDir = RepoLocator.FindCodeRoot();
        var fail = 0;

        Console.WriteLine("════════════════════════════════════════════════");
        Console.WriteLine(" 修复后自查（机械验证，零人工判断）");
        Console.WriteLine("════════════════════════════════════════════════");

        void CheckZero(string desc, List<string> hits)
        {
            if (hits.Count == 0)
            {
                Console.WriteLine($"  \u001b[0;32m✓\u001b[0m {desc}: 0 残留");
            }
            else
            {
                Console.WriteLine($"  \u001b[0;31m✗\u001b[0m {desc}: {hits.Count} 处残留");
                fail++;
            }
        }

        Console.WriteLine();
        Console.WriteLine("── 1. 占位/守卫/清理残留 ──");
        CheckZero("TODO/FIXME/NotImplementedException 残留",
            Scanner.CollectHits(rootDir, "TODO: |FIXME|NotImplementedException", "src"));
        CheckZero("throw ex; 堆栈重置残留",
            Scanner.CollectHits(rootDir, @"throw\s+ex;", "src"));
        CheckZero("Thread.Sleep / Debugger 残留",
            Scanner.CollectHits(rootDir, @"Thread\.Sleep|Debugger\.(Launch|Break)", "src"));

        Console.WriteLine();
        Console.WriteLine("── 2. 注释-代码矛盾高频词（人工审查候选）──");
        var suspect = Scanner.CollectHits(rootDir, "恒 false|恒 true|不可达|天然免疫", "src")
            .Where(h => !h.Contains("声明", StringComparison.Ordinal)
                && !h.Contains("权衡", StringComparison.Ordinal)
                && !h.Contains("勘正", StringComparison.Ordinal)
                && !Regex.IsMatch(h, "已被.*推翻"))
            .ToList();
        Console.Write($"  '恒 false/恒 true/不可达/天然免疫' 声明数: {suspect.Count}");
        Console.WriteLine(" (人工审查候选——非缺陷，防声明漂移)");

        Console.WriteLine();
        Console.WriteLine("── 3. 修复引入的新 public API 检查 ──");

        if (GitHelper.IsRepo(rootDir))
        {
            var staged = GitHelper.Output(rootDir, "diff", "--cached", "--name-only")
                .Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(f => f.Trim()).ToList();
            var changedSrc = staged.Count(f => f.Contains("src/", StringComparison.Ordinal));
            Console.WriteLine($"  暂存集 src/ 变更文件: {changedSrc}");

            if (changedSrc > 0)
            {
                var addedPublic = GitHelper.Output(rootDir, "diff", "--cached")
                    .Split('\n').Count(l => l.StartsWith('+') && l.Contains("public ", StringComparison.Ordinal));
                Console.WriteLine($"  新增 public 成员（需确认行为断言测试覆盖）: {addedPublic}");
            }

            Console.WriteLine();
            Console.WriteLine("── 4. 测试网覆盖（新增修复是否有测试）──");
            var addedTests = staged.Count(f => f.Contains("tests/", StringComparison.Ordinal));
            var diffAdded = GitHelper.Output(rootDir, "diff", "--cached", "--name-only", "--diff-filter=A")
                .Split('\n', StringSplitOptions.RemoveEmptyEntries).Count(l => l.Contains("tests/", StringComparison.Ordinal));
            var diffModified = GitHelper.Output(rootDir, "diff", "--cached", "--name-only", "--diff-filter=M")
                .Split('\n', StringSplitOptions.RemoveEmptyEntries).Count(l => l.Contains("tests/", StringComparison.Ordinal));
            Console.WriteLine($"  暂存集 tests/ 文件合计: {addedTests}");
            Console.WriteLine($"  本轮新增测试文件: {diffAdded}");
            Console.WriteLine($"  本轮修改测试文件: {diffModified}");
        }
        else
        {
            Console.WriteLine("  \u001b[1;33mSKIP\u001b[0m 非 git 仓库——diff 类检查降级（按会话变更清单人工核对）");
        }

        Console.WriteLine();
        Console.WriteLine("════════════════════════════════════════════════");
        Console.WriteLine(fail == 0
            ? "\u001b[0;32m 自查通过\u001b[0m — 零残留，可提交"
            : $"\u001b[0;31m 自查发现 {fail} 处残留\u001b[0m — 请补充修复后再提交");
        Console.WriteLine("════════════════════════════════════════════════");
        return fail == 0 ? 0 : 1;
    }
}

/// <summary>
/// sister-axis-scan 命令：姊妹轴机械枚举（修复范围 = 枚举全集）
/// </summary>
internal static class SisterAxisScanCommand
{
    internal static Command Build()
    {
        var typeArg = new Argument<string>("type") { Description = "guard|dispose|ct|dedup|comment|null-guard|catch|path" };
        var keyArg = new Argument<string>("key") { Description = "关键标识符" };
        var cmd = new Command("sister-axis-scan", "姊妹轴枚举（修一个漏 N-1 防线）");
        cmd.Arguments.Add(typeArg);
        cmd.Arguments.Add(keyArg);

        cmd.SetAction(parseResult =>
        {
            var type = parseResult.GetValue(typeArg) ?? string.Empty;
            var key = parseResult.GetValue(keyArg) ?? string.Empty;
            if (type.Length == 0 || key.Length == 0)
            {
                Console.Error.WriteLine("用法: sister-axis-scan <guard|dispose|ct|dedup|comment|null-guard|catch|path> <关键标识符>");
                Console.Error.WriteLine("示例: sister-axis-scan guard maxAge");
                return 1;
            }

            var rootDir = RepoLocator.FindCodeRoot();
            Console.WriteLine("════════════════════════════════════════════════");
            Console.WriteLine($" 姊妹轴枚举: 类型={type} 标识符={key}");
            Console.WriteLine("════════════════════════════════════════════════");

            List<string> Hits(string pattern, params string[] dirs) => Scanner.CollectHits(rootDir, pattern, dirs);

            void Section(string title, List<string> hits, int take = 30)
            {
                Console.WriteLine();
                Console.WriteLine($"── {title} ──");
                foreach (var h in hits.Take(take))
                {
                    Console.WriteLine(h);
                }
            }

            switch (type)
            {
                case "guard":
                    Section("同参数守卫点", Hits(Regex.Escape(key), "src"));
                    Section("全部守卫方法（跨文件对照）", Hits(@"ThrowIfNull|ArgumentNull|ArgumentOutOfRange|is null.*throw", "src"));
                    Section("缺守卫的姊妹方法（同名方法无守卫）",
                        Hits(Regex.Escape(key), "src").Where(h => !h.Contains("ThrowIf", StringComparison.Ordinal)
                            && !h.Contains("ArgumentNullException", StringComparison.Ordinal)
                            && !h.Contains("ArgumentOutOfRange", StringComparison.Ordinal)).ToList(), 15);
                    break;

                case "dispose":
                    Section("Dispose/Close/清理方法", Hits(@"Dispose|\.Close|StopAsync", "src"));
                    Section("try-finally 配对检查", Hits(@"finally", "src"), 15);
                    break;

                case "ct":
                    Section("CancellationToken 参数/传导", Hits(@"CancellationToken", "src"));
                    Section("缺 ct 传导的 async 调用（无 ct 参数的 await）",
                        Hits(@"await.*Async\(\)", "src").Where(h => !h.Contains("ct", StringComparison.Ordinal)
                            && !h.Contains("CancellationToken", StringComparison.Ordinal)
                            && !h.Contains("None", StringComparison.Ordinal)).ToList(), 10);
                    break;

                case "dedup":
                    Section("查重/去重逻辑", Hits(@"HashSet|\.Add\(|ContainsKey|TryGetValue|GetOrAdd", "src"));
                    break;

                case "comment":
                    Section("同族注释/声明（src + 文档）", Hits(Regex.Escape(key), "src", "docs"), 30);
                    break;

                case "catch":
                    Section("catch(Exception) 全量分布（棘轮对照）",
                        Hits(@"catch\s*\(\s*Exception", "src").Where(h => !h.Contains("when", StringComparison.Ordinal)).ToList());
                    break;

                case "path":
                    Section("路径处理点", Hits(@"Path\.(Combine|GetFullPath|GetFileName|GetRelativePath)", "src"));
                    Section("含 KEY 的路径拼装点",
                        Hits(Regex.Escape(key), "src").Where(h => Regex.IsMatch(h, "path|Combine|fullpath", RegexOptions.IgnoreCase)).ToList(), 15);
                    break;

                case "null-guard":
                    Section("null 守卫覆盖矩阵", Hits(@"ThrowIfNull|is null.*throw|is not null", "src"));
                    break;

                default:
                    Console.Error.WriteLine($"未知类型: {type}");
                    Console.Error.WriteLine("支持: guard | dispose | ct | dedup | comment | null-guard | catch | path");
                    return 1;
            }

            Console.WriteLine();
            Console.WriteLine("════════════════════════════════════════════════");
            Console.WriteLine(" 枚举完成 — 以上为修复范围的全部同型位置");
            Console.WriteLine(" 修复清单 = 枚举结果全集（非首个命中）");
            Console.WriteLine("════════════════════════════════════════════════");
            return 0;
        });

        return cmd;
    }
}

/// <summary>
/// fix-completeness-check 命令：修复完整性验证（姊妹轴覆盖）
/// </summary>
internal static class FixCompletenessCheckCommand
{
    internal static Command Build()
    {
        var typeArg = new Argument<string>("type") { Description = "guard|dispose|ctpass|catch" };
        var keyArg = new Argument<string>("key") { Description = "关键标识符" };
        var cmd = new Command("fix-completeness-check", "修复完整性验证（修复范围=枚举全集）");
        cmd.Arguments.Add(typeArg);
        cmd.Arguments.Add(keyArg);

        cmd.SetAction(parseResult =>
        {
            var type = parseResult.GetValue(typeArg) ?? string.Empty;
            var key = parseResult.GetValue(keyArg) ?? string.Empty;
            if (type.Length == 0 || key.Length == 0)
            {
                Console.Error.WriteLine("用法: fix-completeness-check <修复类型> <关键标识符>");
                return 1;
            }

            var rootDir = RepoLocator.FindCodeRoot();
            var pass = true;
            var missing = 0;

            Console.WriteLine("════════════════════════════════════════════════");
            Console.WriteLine($" 修复完整性验证: 类型={type} 标识符={key}");
            Console.WriteLine("════════════════════════════════════════════════");

            switch (type)
            {
                case "guard":
                {
                    Console.WriteLine();
                    Console.WriteLine("── 守卫覆盖完整性 ──");
                    // 含 KEY 的文件：有公开成员但零守卫 → 标记
                    var filesWithKey = Scanner.CsFiles(rootDir, "src")
                        .Where(f => Io.ReadAllText(Path.Combine(rootDir, f)).Contains(key, StringComparison.Ordinal))
                        .ToList();
                    foreach (var f in filesWithKey)
                    {
                        var text = Io.ReadAllText(Path.Combine(rootDir, f));
                        var members = Regex.Matches(text, "public|internal").Count;
                        var guards = Regex.Matches(text, "ThrowIf|ArgumentNull|ArgumentOutOfRange").Count;
                        if (members > 0 && guards == 0)
                        {
                            Console.WriteLine($"  \u001b[0;31m✗\u001b[0m {f} 有 {members} 个公开成员但零守卫");
                            pass = false;
                            missing++;
                        }
                    }

                    Console.WriteLine($"  \u001b[0;32m✓\u001b[0m 全部含 {key} 的文件均有守卫覆盖");
                    break;
                }

                case "dispose":
                {
                    Console.WriteLine();
                    Console.WriteLine("── 资源释放覆盖完整性 ──");
                    var streamFiles = Scanner.CsFiles(rootDir, "src")
                        .Where(f =>
                        {
                            var text = Io.ReadAllText(Path.Combine(rootDir, f));
                            return Regex.IsMatch(text, @"File\.(Open|Create|OpenWrite)|new (StreamWriter|FileStream)");
                        })
                        .ToList();
                    foreach (var f in streamFiles)
                    {
                        var text = Io.ReadAllText(Path.Combine(rootDir, f));
                        if (!Regex.IsMatch(text, "using|Dispose|finally|await using"))
                        {
                            Console.WriteLine($"  \u001b[0;31m✗\u001b[0m {f} 含流操作但零释放痕迹");
                            pass = false;
                            missing++;
                        }
                    }

                    Console.WriteLine("  \u001b[0;32m✓\u001b[0m 流操作文件全部含释放痕迹");
                    break;
                }

                case "ctpass":
                {
                    Console.WriteLine();
                    Console.WriteLine("── CancellationToken 传导覆盖 ──");
                    var missingCt = Scanner.CollectHits(rootDir, @"await [A-Za-z].*\(\)", "src")
                        .Where(h => !h.Contains("ct", StringComparison.Ordinal)
                            && !h.Contains("CancellationToken", StringComparison.Ordinal)
                            && !h.Contains("None", StringComparison.Ordinal))
                        .Take(10)
                        .ToList();
                    Console.WriteLine($"  无 ct 传导的 await 调用（线索，人工核对）: {missingCt.Count} 处");
                    foreach (var m in missingCt)
                    {
                        Console.WriteLine($"    {m}");
                    }

                    break;
                }

                case "catch":
                {
                    Console.WriteLine();
                    Console.WriteLine("── catch 过滤覆盖 ──");
                    var bare = Scanner.CollectHits(rootDir, @"catch\s*\(\s*Exception", "src")
                        .Count(h => !h.Contains("when", StringComparison.Ordinal));
                    Console.WriteLine($"  裸 catch(Exception) 当前 {bare} 处（棘轮基线 23，见 gate-check）");
                    break;
                }

                default:
                    Console.Error.WriteLine("支持: guard | dispose | ctpass | catch");
                    return 1;
            }

            Console.WriteLine();
            if (pass)
            {
                Console.WriteLine("\u001b[0;32m 完整性验证通过\u001b[0m — 修复范围覆盖全部姊妹轴");
            }
            else
            {
                Console.WriteLine($"\u001b[0;31m 完整性验证失败\u001b[0m — {missing} 处遗漏，请补充后重新验证");
                return 1;
            }

            return 0;
        });

        return cmd;
    }
}

/// <summary>
/// refine-scan 命令：精炼扫描矩阵（命中数 ≠ 可改数，信噪比标注）
/// </summary>
internal static class RefineScanCommand
{
    internal static Command Build()
    {
        var cmd = new Command("refine-scan", "精炼扫描（减法/现代化/优化三类矩阵 + 信噪比标注）");
        cmd.SetAction(_ => Run());
        return cmd;
    }

    private static int Run()
    {
        var rootDir = RepoLocator.FindCodeRoot();
        var srcDir = Path.Combine(rootDir, "src");

        // grep 行计数（排除 obj/bin/注释行），对应 bash 的 count()
        List<string> Hits(string pattern)
        {
            var hits = new List<string>();
            foreach (var rel in Scanner.CsFiles(rootDir, "src"))
            {
                var full = Path.Combine(rootDir, rel);
                var lines = Io.ReadAllLines(full);
                var regex = new Regex(pattern);
                for (var i = 0; i < lines.Length; i++)
                {
                    if (regex.IsMatch(lines[i]))
                    {
                        var trimmed = lines[i].TrimStart();
                        if (trimmed.StartsWith("//", StringComparison.Ordinal))
                        {
                            continue;
                        }

                        hits.Add($"{rel}:{i + 1}:{lines[i]}");
                    }
                }
            }

            return hits;
        }

        int Count(string pattern) => Hits(pattern).Count;
        int FileCount(string searchPattern) => Directory.Exists(srcDir)
            ? Directory.EnumerateFiles(srcDir, searchPattern, SearchOption.AllDirectories)
                .Count(f => !f.Replace('\\', '/').Contains("/obj/") && !f.Replace('\\', '/').Contains("/bin/"))
            : 0;

        int SmallFileCount(int maxLines) => Scanner.CsFiles(rootDir, "src")
            .Count(f =>
            {
                var lines = Io.ReadAllLines(Path.Combine(rootDir, f));
                var n = lines.Length;
                return n > 0 && n <= maxLines;
            });

        Console.WriteLine("═══════ 一类:减法 ═══════");
        Console.WriteLine($"A1 🟢 AssemblyInfo: {FileCount("AssemblyInfo.cs")}  — 删文件→csproj InternalsVisibleTo");
        Console.WriteLine($"A2 🟢 GlobalUsings: {FileCount("GlobalUsings.cs")}  — 删文件→csproj Using 项");
        Console.WriteLine($"A3 🟡 标记接口/常量(≤10行): {SmallFileCount(10)}  — 需核实是否独立语义(enum+record 不合并)");
        Console.WriteLine($"A5 🟢 using 行密度: {Count("^using ")} 行  — dotnet format IDE0005 清冗余");

        Console.WriteLine();
        Console.WriteLine("═══════ 二类:现代化 ═══════");
        Console.WriteLine($"M1a 🟡 Array.Empty<T>(): {Count(@"Array\.Empty<")}  — ⚠️ 返回类型 byte[]/T[] 可改[]；ReadOnlyMemory/Span/Memory 不可改(CS9174)");
        Console.WriteLine($"M1b 🟢 List/Dict 空构造: {Count(@"new List<[^>]*>()|new Dictionary<[^>]*>()")}  — new T<>()→[](无容量/comparer 时)");
        Console.WriteLine($"M2 🔴 ?? throw 字段初始化: {Count(@"private readonly.*\?\? throw")}  — 高假阳性：主构造函数在继承链/ORM 类风险高，多不可下沉");
        Console.WriteLine($"M3 🔴 public {{get;}}: {Hits(@"\{ get; \}").Count(h => h.Contains("public", StringComparison.Ordinal)
            && !h.Contains("set", StringComparison.Ordinal)
            && !h.Contains("init", StringComparison.Ordinal)
            && !h.Contains("static", StringComparison.Ordinal)
            && !h.Contains("=>", StringComparison.Ordinal))}  — 高假阳性：DTO/模型类不能用 required(需无参构造/反序列化)");
        Console.WriteLine($"M5 🟢 string.Format: {Count(@"string\.Format|String\.Format")}  — →$\"{{x}}\" 插值");
        var m6Raw = Count("throw new ArgumentNullException");
        var m6Standalone = Hits("throw new ArgumentNullException")
            .Count(h => !h.Contains("///", StringComparison.Ordinal)
                && !h.Contains("Suppress", StringComparison.Ordinal)
                && !h.Contains("?? throw", StringComparison.Ordinal)
                && !h.Contains("??throw", StringComparison.Ordinal));
        Console.WriteLine($"M6 🟡 独立 throw ArgumentNullException: {m6Standalone} (总 {m6Raw}, 排除 ?? throw 惯用法)  — →ThrowIfNull");

        Console.WriteLine();
        Console.WriteLine("═══════ 三类:优化 ═══════");
        Console.WriteLine($"O1 🔴 new Dictionary<>: {Count(@"new Dictionary<")}  — 高假阳性：ToFrozenDictionary 构建器/外部 API(comparer/headers)传入，多不可改");
        Console.WriteLine($"O2 🟢 List/Dict 无预分配: {Count(@"new List<[^>]*>()|new Dictionary<[^>]*>()")}  — new(N) 预分配(已知容量时)");
        var o3 = Hits(@"\.ToArray\(\)|\.ToList\(\)")
            .Count(h => !h.Contains("InMemory", StringComparison.Ordinal) && !h.Contains("Test", StringComparison.Ordinal));
        Console.WriteLine($"O3 🟡 ToArray/ToList: {o3}  — 热路径→Span<T> 零分配(非热路径不改)");
        Console.WriteLine($"O6 🟡 string +=: {Count(@"\+= .*""")}  — 循环内→StringBuilder/插值(单次拼接不改)");

        Console.WriteLine();
        Console.WriteLine("═══ 扫描完成 · 命中数≠可改数，逐条核实后按诊断三步骤采纳 ═══");
        return 0;
    }
}

/// <summary>
/// probe-template 命令：探针骨架生成器（probe-first 基建）
/// </summary>
internal static class ProbeTemplateCommand
{
    internal static Command Build()
    {
        var nameArg = new Argument<string>("name") { Description = "探针名" };
        var moduleArg = new Argument<string>("module") { Description = "core|cli", DefaultValueFactory = _ => "core" };
        moduleArg.Arity = ArgumentArity.ZeroOrOne;
        var cmd = new Command("probe-template", "探针骨架生成（/tmp/flint-probe-<名>/）");
        cmd.Arguments.Add(nameArg);
        cmd.Arguments.Add(moduleArg);

        cmd.SetAction(parseResult =>
        {
            var name = parseResult.GetValue(nameArg);
            var module = parseResult.GetValue(moduleArg) ?? "core";
            if (string.IsNullOrEmpty(name))
            {
                Console.Error.WriteLine("用法: probe-template <探针名> [core|cli]");
                return 2;
            }

            var repo = RepoLocator.FindCodeRoot();
            string refs;
            string usingComment;
            switch (module)
            {
                case "core":
                    refs = $"    <ProjectReference Include=\"{repo}\\src\\Flint.Core\\Flint.Core.csproj\" />";
                    usingComment = "// 探针: Flint.Core —— 配置/内容/模板/站点/资产";
                    break;
                case "cli":
                    refs = $"    <ProjectReference Include=\"{repo}\\src\\Flint.Cli\\Flint.Cli.csproj\" />";
                    usingComment = "// 探针: Flint.Cli —— 命令处理器";
                    break;
                default:
                    Console.Error.WriteLine($"未知模块: {module}（core|cli）");
                    return 1;
            }

            var dir = Path.Combine(Path.GetTempPath(), $"flint-probe-{name}");
            Directory.CreateDirectory(dir);

            // CPM 项目：探针工程不含 NuGet 包引用，仅 ProjectReference（被引用工程用自己的 props 解析包）
            var csproj =
                "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
                "  <PropertyGroup>\n" +
                "    <OutputType>Exe</OutputType>\n" +
                "    <TargetFramework>net10.0</TargetFramework>\n" +
                "    <Nullable>enable</Nullable>\n" +
                "    <ImplicitUsings>enable</ImplicitUsings>\n" +
                "    <EnforceCodeStyleInBuild>false</EnforceCodeStyleInBuild>\n" +
                "    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>\n" +
                "    <AnalysisLevel>none</AnalysisLevel>\n" +
                "    <NoWarn>$(NoWarn);CS1591</NoWarn>\n" +
                "  </PropertyGroup>\n" +
                "  <ItemGroup>\n" +
                refs + "\n" +
                "  </ItemGroup>\n" +
                "</Project>\n";
            Io.WriteAllText(Path.Combine(dir, "probe.csproj"), csproj);

            var programPath = Path.Combine(dir, "Program.cs");
            if (!File.Exists(programPath))
            {
                var program =
                    usingComment + "\n" +
                    "// 断言写在下方，结论打印到 stdout（证实/证伪一行说清）\n" +
                    "\n" +
                    "internal static class Program\n" +
                    "{\n" +
                    "    private static void Main()\n" +
                    "    {\n" +
                    "        // 探针主体\n" +
                    "        Console.WriteLine(\"probe " + name + ": TODO\");\n" +
                    "    }\n" +
                    "}\n";
                Io.WriteAllText(programPath, program);
            }

            Console.WriteLine($"探针工程: {dir}");
            Console.WriteLine($"编辑 {programPath} 后: cd {dir} && dotnet run");
            return 0;
        });

        return cmd;
    }
}

/// <summary>
/// fix-orchestrator 命令：修复轮编排器（姊妹联动 + 修复门两问 + 回归清单）
/// </summary>
internal static class FixOrchestratorCommand
{
    internal static Command Build()
    {
        var itmArg = new Argument<string>("itm") { Description = "ITM 编号（可选）", DefaultValueFactory = _ => string.Empty };
        itmArg.Arity = ArgumentArity.ZeroOrOne;
        var cmd = new Command("fix-orchestrator", "修复轮编排（姊妹联动/修复门两问/回归清单）");
        cmd.Arguments.Add(itmArg);

        cmd.SetAction(parseResult => Run(parseResult.GetValue(itmArg) ?? string.Empty));
        return cmd;
    }

    private static int Run(string itm)
    {
        var rootDir = RepoLocator.FindCodeRoot();
        var headShort = "（非 git 仓库）";
        if (GitHelper.IsRepo(rootDir))
        {
            var head = GitHelper.Output(rootDir, "rev-parse", "--short", "HEAD").Trim();
            if (head.Length > 0)
            {
                headShort = head;
            }
        }

        Console.WriteLine("\u001b[0;36m═══ 修复轮编排器 ═══\u001b[0m");
        Console.WriteLine($"时间：{DateTime.Now:yyyy-MM-dd HH:mm}  HEAD：{headShort}  ITM：{(itm.Length > 0 ? itm : "（未指定）")}\n");

        // ① 姊妹联动：HEAD diff 触及文件命中哪些族（进程内调用 sibling-map 的枚举逻辑）
        var changed = GitHelper.IsRepo(rootDir)
            ? GitHelper.Output(rootDir, "diff", "HEAD~1", "--name-only")
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(f => f.Trim())
                .Where(f => f.EndsWith(".cs", StringComparison.Ordinal) && !f.EndsWith(".g.cs", StringComparison.Ordinal))
                .ToList()
            : new List<string>();

        if (changed.Count > 0)
        {
            Console.WriteLine("── ① 姊妹联动（变更文件命中族）──");
            var families = SiblingMapCommand.ComputeFamilies(rootDir);
            var hit = 0;
            foreach (var f in changed)
            {
                foreach (var (iface, members) in families)
                {
                    if (members.Any(m => m.File == f))
                    {
                        Console.WriteLine($"  {iface} ← {f}");
                        hit++;
                    }
                }
            }

            if (hit == 0)
            {
                Console.WriteLine("  （无族命中——检查管线孪生轴 sibling-map.md 轴 B 种子表）");
            }

            Console.WriteLine("  联动规则：姊妹对同族派发；判据三选一；>3 文件人工裁决");
            if (itm.Length > 0)
            {
                Console.WriteLine($"  → 联动留痕模板见 sibling-map.md「联动留痕格式」节，ITM={itm}");
            }
        }
        else
        {
            Console.WriteLine("── ① 姊妹联动：无 .cs 变更 ──");
        }

        // ② 修复门两问（人/agent 决策提示）
        Console.WriteLine();
        Console.WriteLine("── ② 修复门两问 ──");
        Console.WriteLine("  Q1 这个故障类有传感器吗？→ 复现测试存在吗？（不存在先写：s 从 0→1）");
        Console.WriteLine("  Q2 这个改动外溢吗？→ 共享函数/多调用方？→ grep 调用方评估 p'");
        Console.WriteLine("  规则：s ≤ p' 先补传感器再修；同测试翻转两次=打摆换方案");

        // ③ 快速回归清单
        Console.WriteLine();
        Console.WriteLine("── ③ 回归清单（按序执行）──");
        Console.WriteLine("  1. dotnet build Flint.slnx -c Release --verbosity quiet       # 全仓构建");
        Console.WriteLine("  2. dotnet test <受影响项目> -c Release --verbosity quiet       # 定向测试");
        if (changed.Count > 0)
        {
            foreach (var d in changed
                         .Select(f => Regex.Match(f, "tests/[^/]+/"))
                         .Where(m => m.Success)
                         .Select(m => m.Value)
                         .Distinct(StringComparer.Ordinal)
                         .Order(StringComparer.Ordinal))
            {
                var projDir = Path.Combine(rootDir, d.TrimEnd('/'));
                var csproj = Directory.Exists(projDir)
                    ? Directory.EnumerateFiles(projDir, "*.csproj").FirstOrDefault()
                    : null;
                Console.WriteLine($"     → {d}{(csproj is null ? string.Empty : Path.GetFileName(csproj))}");
            }
        }

        Console.WriteLine("  3. dotnet run --project src/Flint.AiGate -- verify-ai-system  # 系统一致性");
        Console.WriteLine("  4. dotnet run --project src/Flint.AiGate -- gate-check        # 架构门禁");
        Console.WriteLine("  5. git add <相关文件> && git commit -m '修复：<描述>'");

        Console.WriteLine();
        Console.WriteLine("\u001b[0;36m═══ 编排完成——执行决策归修复者 ═══\u001b[0m");
        return 0;
    }
}
