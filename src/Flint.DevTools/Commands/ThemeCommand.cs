// theme 子命令组：主题链路工具（clone-themes.sh / clone-candidates.sh / verify-themes.sh /
// theme-matrix20.sh 的 C# 移植）。
// - theme clone    ：基础池 + 候选池浅克隆（默认分支/clone 行为逐条对齐）
// - theme verify   ：候选主题 Hugo 侧三条件验证（exit=0 + 有页数 + 最小页 >200B）
// - theme matrix   ：21 主题横向兼容矩阵（Hugo 基线 × Flint 迁移产物 + 门禁④）

using System.CommandLine;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Flint.DevTools.Commands;

/// <summary>
/// theme 子命令组
/// </summary>
internal static class ThemeCommand
{
    /// <summary>21 个矩阵主题（与 theme-matrix20.sh / demo-sites.sh 总表一致，顺序即端口序）</summary>
    internal static readonly string[] MatrixThemes =
    {
        "ananke", "bearblog", "blog-awesome", "blowfish", "clarity", "console", "even",
        "fixit", "github-style", "hugo-book", "hugo-coder", "hugo-paper", "loveit", "m10c",
        "monochrome", "narrow", "papermod", "stack", "techdoc", "xmin", "yinyang",
    };

    private static string ThemesDir => Path.Combine(RepoPaths.RepoRoot, "theme-migrator", "themes");

    private static string CandidatesDir => Path.Combine(RepoPaths.RepoRoot, "theme-migrator", "candidates");

    private static string WorkRoot => Path.Combine(RepoPaths.RepoRoot, "theme-verify");

    private static string MatrixWorkRoot => Path.Combine(RepoPaths.RepoRoot, "matrix20");

    private static string HugoExe => Path.Combine(RepoPaths.RepoRoot, "..", "tools", "hugo-bin", "hugo.exe");

    private static string DartSassDir => Path.Combine(RepoPaths.RepoRoot, "..", "tools", "dart-sass");

    internal static Command Build()
    {
        var cmd = new Command("theme", "主题链路（克隆 / Hugo 侧验证 / 兼容矩阵）");
        cmd.Subcommands.Add(BuildClone());
        cmd.Subcommands.Add(BuildVerify());
        cmd.Subcommands.Add(BuildMatrix());
        return cmd;
    }

    // ---------------------------------------------------------------- clone

    // 名称|仓库|分支（基础池，与 clone-themes.sh 一致；已存在则跳过）
    private static readonly (string Name, string Repo, string Branch)[] BaseThemes =
    {
        ("hugo-book", "alex-shpak/hugo-book", "main"),
        ("hugo-coder", "luizdepra/hugo-coder", "main"),
        ("blowfish", "nunocoracao/blowfish", "main"),
        ("terminal", "panr/hugo-theme-terminal", "master"),
        ("hugo-paper", "nanxiaobei/hugo-paper", "main"),
        ("hextra", "imfing/hextra", "main"),
        ("even", "olOwOlo/hugo-theme-even", "master"),
        ("congo", "jpanther/congo", "dev"),
        ("bearblog", "janraasch/hugo-bearblog", "master"),
        ("archie", "athul/archie", "master"),
        ("hermit", "Track3/hermit", "master"),
        ("fixit", "hugo-fixit/FixIt", "main"),
        ("mainroad", "Vimux/Mainroad", "master"),
        ("jane", "xianmin/hugo-theme-jane", "master"),
        ("xmin", "yihui/hugo-xmin", "master"),
        ("blog-awesome", "hugo-sid/hugo-blog-awesome", "main"),
        ("console", "mrmierzejewski/hugo-theme-console", "master"),
        ("clarity", "chipzoller/hugo-clarity", "master"),
        ("risotto", "joeroe/risotto", "main"),
        ("relearn", "McShelby/hugo-theme-relearn", "main"),
    };

    // 名称|仓库（候选池，默认分支由远端决定，与 clone-candidates.sh 一致）
    private static readonly (string Name, string Repo)[] Candidates =
    {
        ("terminal", "panr/hugo-theme-terminal"),
        ("hextra", "imfing/hextra"),
        ("relearn", "McShelby/hugo-theme-relearn"),
        ("jane", "xianmin/hugo-theme-jane"),
        ("mainroad", "Vimux/Mainroad"),
        ("hermit", "Track3/hermit"),
        ("eureka", "wangchucheng/hugo-eureka"),
        ("meme", "reuixiy/hugo-theme-meme"),
        ("intro", "victoriadrake/hugo-theme-introduction"),
        ("learn", "matcornic/hugo-theme-learn"),
        ("gallery", "nicokaiser/hugo-theme-gallery"),
        ("fresh", "StefMa/hugo-fresh"),
        ("zzo", "zzossig/hugo-theme-zzo"),
        ("hello-friend", "panr/hugo-theme-hello-friend"),
        ("gokarna", "526avijitgupta/gokarna"),
        ("yinyang", "joway/hugo-theme-yinyang"),
        ("m10c", "vaga/hugo-theme-m10c"),
        ("hugo-profile", "gurusabarish/hugo-profile"),
        ("lynx", "jpanther/lynx"),
        ("monochrome", "kaiiiz/hugo-theme-monochrome"),
        ("hugo-tania", "WingLim/hugo-tania"),
        ("techdoc", "thingsym/hugo-theme-techdoc"),
        ("etch", "LukasJoswiak/etch"),
        ("noteworthy", "kimcc/hugo-theme-noteworthy"),
        ("adritian", "zetxek/adritian-free-hugo-theme"),
        ("narrow", "tom2almighty/hugo-narrow"),
    };

    private static Command BuildClone()
    {
        var cmd = new Command("clone", "克隆 Hugo 主题（基础池 20 + 候选池 26，浅克隆）");

        var poolOpt = new Option<string>("--pool")
        {
            Description = "basic=基础池；candidates=候选池；both=两者（默认）",
            DefaultValueFactory = _ => "both",
        };
        cmd.Options.Add(poolOpt);

        cmd.SetAction(async parseResult =>
        {
            var pool = parseResult.GetValue(poolOpt)!;
            if (pool is "basic" or "both")
            {
                Console.WriteLine(
                    $"{PyCompat.PadRightBytes("名称", 16)} {PyCompat.PadRightBytes("仓库", 42)} 结果");
                Console.WriteLine(new string('-', 79));
                foreach (var (name, repo, branch) in BaseThemes)
                {
                    await CloneTheme(ThemesDir, name, repo, branch, 42).ConfigureAwait(false);
                }
            }

            if (pool is "candidates" or "both")
            {
                if (pool == "both")
                {
                    Console.WriteLine();
                }

                Console.WriteLine(
                    $"{PyCompat.PadRightBytes("名称", 16)} {PyCompat.PadRightBytes("仓库", 48)} 克隆");
                Console.WriteLine(new string('-', 84));
                foreach (var (name, repo) in Candidates)
                {
                    await CloneTheme(CandidatesDir, name, repo, branch: null, 48).ConfigureAwait(false);
                }
            }

            return 0;
        });

        return cmd;
    }

    private static async Task CloneTheme(string destDir, string name, string repo, string? branch, int repoWidth)
    {
        Directory.CreateDirectory(destDir);
        var target = Path.Combine(destDir, name);

        if (Directory.Exists(Path.Combine(target, "layouts")))
        {
            Console.WriteLine($"{PyCompat.PadRightBytes(name, 16)} {PyCompat.PadRightBytes(repo, repoWidth)} 已存在");
            return;
        }

        if (Directory.Exists(target))
        {
            Directory.Delete(target, recursive: true);
        }

        var args = new List<string> { "clone", "--depth", "1", "--quiet" };
        if (branch is not null)
        {
            args.Add("--branch");
            args.Add(branch);
        }

        args.Add($"https://github.com/{repo}.git");
        args.Add(target);

        var result = await Bench.ProcessRunner.RunTimedAsync("git", args, workingDirectory: RepoPaths.RepoRoot).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            Console.WriteLine($"{PyCompat.PadRightBytes(name, 16)} {PyCompat.PadRightBytes(repo, repoWidth)} 失败");
            return;
        }

        var n = Directory.Exists(Path.Combine(target, "layouts"))
            ? Directory.EnumerateFiles(Path.Combine(target, "layouts"), "*.html", SearchOption.AllDirectories).Count()
            : 0;
        Console.WriteLine($"{PyCompat.PadRightBytes(name, 16)} {PyCompat.PadRightBytes(repo, repoWidth)} OK（{n} 个布局文件）");
    }

    // ---------------------------------------------------------------- verify

    private static Command BuildVerify()
    {
        var themesArg = new Argument<string[]>("themes") { Description = "主题名；缺省扫主题池全部" };
        themesArg.Arity = ArgumentArity.ZeroOrMore;

        var cmd = new Command("verify", "候选主题 Hugo 侧三条件验证（不影响 Flint）");
        cmd.Arguments.Add(themesArg);

        cmd.SetAction(async parseResult =>
        {
            var requested = parseResult.GetValue(themesArg) ?? Array.Empty<string>();
            var names = requested.Length > 0 ? requested : ListThemeNames(ThemesDir).ToArray();

            Console.WriteLine(
                $"{PyCompat.PadRightBytes("主题", 16)} {PyCompat.PadRightBytes("配置源", 11)} " +
                $"{PyCompat.PadRightBytes("页数", 7)} {PyCompat.PadRightBytes("最小页", 9)} " +
                $"{PyCompat.PadRightBytes("结论/首错", 30)}");
            Console.WriteLine(new string('-', 95));

            foreach (var name in names)
            {
                await VerifyOne(name).ConfigureAwait(false);
            }

            Console.WriteLine(new string('-', 95));
            Console.WriteLine("（配置源：exampleSite=作者验证过的配置；最小配置=通用占位）");
            return 0;
        });

        return cmd;
    }

    private static List<string> ListThemeNames(string dir)
    {
        return Directory.Exists(dir)
            ? Directory.GetDirectories(dir).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToList()!
            : new List<string>();
    }

    private static async Task VerifyOne(string name)
    {
        var src = Path.Combine(ThemesDir, name);
        if (!Directory.Exists(Path.Combine(src, "layouts")))
        {
            return;
        }

        var site = Path.Combine(WorkRoot, name);
        CleanDir(site);
        Directory.CreateDirectory(site);

        var cfgSrc = "最小配置";
        var themeName = name;
        if (Directory.Exists(Path.Combine(src, "exampleSite")))
        {
            CopyTree(Path.Combine(src, "exampleSite"), site);
            StripThemesDirAndGitInfo(site);
            themeName = ReadThemeName(site, name);
            cfgSrc = "exampleSite";
        }

        var themeTarget = Path.Combine(site, "themes", themeName);
        Directory.CreateDirectory(Path.Combine(site, "themes"));
        CopyTree(src, themeTarget);
        CleanDir(Path.Combine(themeTarget, "exampleSite"));
        CleanDir(Path.Combine(themeTarget, ".git"));

        if (cfgSrc == "最小配置" || !Directory.Exists(Path.Combine(site, "content")))
        {
            MakeMinContent(site);
        }

        if (cfgSrc == "最小配置")
        {
            MakeMinConfig(themeName, site, name);
        }

        // run_hugo + verdict
        async Task<(int ExitCode, string Output, int Pages, int MinSize, int MaxSize)> RunHugo()
        {
            CleanDir(Path.Combine(site, "public"));
            var raw = await RunHugoAsync(site).ConfigureAwait(false);
            var pages = CountHtml(Path.Combine(site, "public"));
            var min = MinHtmlSize(Path.Combine(site, "public"));
            var max = MaxHtmlSize(Path.Combine(site, "public"));
            return (raw.ExitCode, raw.Output, pages, min, max);
        }

        var run = await RunHugo().ConfigureAwait(false);
        if (run.ExitCode != 0)
        {
            // 首次失败重试一次：Hugo 偶发失败不该判主题不可用
            run = await RunHugo().ConfigureAwait(false);
        }

        var out_ = run.Output;
        var exitCode = run.ExitCode;
        var pages = run.Pages;
        var minSize = run.MinSize;
        var maxSize = run.MaxSize;
        var contentNote = "";

        if (cfgSrc == "exampleSite" && exitCode != 0)
        {
            // exampleSite 失败换最小内容重试（保留配置）：内容过时 ≠ 主题不可用
            CleanDir(Path.Combine(site, "content"));
            MakeMinContent(site);
            run = await RunHugo().ConfigureAwait(false);
            exitCode = run.ExitCode;
            pages = run.Pages;
            minSize = run.MinSize;
            maxSize = run.MaxSize;
            if (exitCode == 0 && pages > 0 && minSize >= 200)
            {
                contentNote = "（exampleSite 内容过时，换最小内容后可用）";
                cfgSrc = "exampleSite*";
            }
            else
            {
                exitCode = 1;
            }
        }

        var verdict = "可用";
        var detail = "";
        if (exitCode != 0 && pages > 0 && maxSize >= 1000)
        {
            verdict = "可用*";
            detail = "（非致命：" + (FirstError(out_) ?? $"exit={exitCode}") + "）";
        }
        else if (exitCode != 0)
        {
            verdict = "不可用";
            detail = FirstError(out_) ?? $"exit={exitCode}（无错误输出，疑似超时或静默失败）";
        }
        else if (pages == 0)
        {
            verdict = "不可用";
            detail = "构建成功但零产出";
        }
        else if (maxSize < 1000)
        {
            verdict = "空页";
            detail = $"最大页仅 {maxSize}B（主题未渲染出内容）";
        }

        // 结论列按 bash printf 的字节宽左对齐 30（CJK 按 3 字节计）
        var cell = $"【{verdict}】{detail}{contentNote}";
        Console.WriteLine(
            $"{PyCompat.PadRightBytes(name, 16)} {PyCompat.PadRightBytes(cfgSrc, 11)} " +
            $"{PyCompat.PadRightBytes(pages.ToString(), 7)} {PyCompat.PadRightBytes(minSize + "B", 9)} " +
            $"{PyCompat.PadRightBytes(cell, 30)}");
    }

    private static async Task<(int ExitCode, string Output)> RunHugoAsync(string site)
    {
        var hugo = Path.GetFullPath(HugoExe);
        var sass = Directory.Exists(DartSassDir) ? Path.GetFullPath(DartSassDir) : null;

        string? oldPath = null;
        var psi = new ProcessStartInfo(hugo)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = site,
        };

        // Dart Sass 入 PATH（Hugo v0.153+ LibSass 弃用，toCSS/Sass 需外部实现）
        if (sass is not null)
        {
            oldPath = Environment.GetEnvironmentVariable("PATH");
            psi.Environment["PATH"] = sass + Path.PathSeparator + (oldPath ?? string.Empty);
        }

        // Hugo 有内建超时风险时由外部 stopwatch 兜底（与 bash timeout 600 对齐由调用方负责）
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("启动 hugo 失败");
        var stdout = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
        var stderr = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
        await process.WaitForExitAsync().ConfigureAwait(false);

        if (oldPath is not null)
        {
            Environment.SetEnvironmentVariable("PATH", oldPath);
        }

        // 合并输出（bash 版 2>&1 语义），退出码由进程直接给出
        return (process.ExitCode, stdout + stderr);
    }

    private static string? FirstError(string output)
    {
        var m = Regex.Match(output, @"(ERROR|error)[^|]{0,110}");
        if (!m.Success)
        {
            return null;
        }

        var text = m.Value;
        return Regex.Replace(text, "^[A-Za-z]* *", "");
    }

    private static void MakeMinContent(string site)
    {
        WriteAllText(Path.Combine(site, "content", "_index.md"), "---\ntitle: Home\n---\nWelcome.\n");
        WriteAllText(Path.Combine(site, "content", "posts", "_index.md"), "---\ntitle: Posts\n---\nAll posts.\n");
        WriteAllText(
            Path.Combine(site, "content", "posts", "first.md"),
            "---\ntitle: First Post\ndate: 2026-01-15\ntags: [intro, test]\ncategories: [general]\ndescription: The first post\n---\nFirst post body with some text.\n\n## A heading\n\nMore content here.\n");
        WriteAllText(
            Path.Combine(site, "content", "posts", "second.md"),
            "---\ntitle: Second Post\ndate: 2026-02-20\ntags: [test]\ncategories: [general]\n---\nSecond post body.\n");
        WriteAllText(
            Path.Combine(site, "content", "docs", "getting-started.md"),
            "---\ntitle: Getting Started\nweight: 10\n---\nGuide body.\n");
        WriteAllText(Path.Combine(site, "content", "about.md"), "---\ntitle: About\n---\nAbout page.\n");
    }

    private static void MakeMinConfig(string themeName, string site, string repoDir)
    {
        var sb = new StringBuilder();
        sb.Append("baseURL = \"https://example.com/\"\n");
        sb.Append("title = \"Verify Site\"\n");
        sb.Append($"theme = \"{themeName}\"\n");
        sb.Append("paginate = 2\n");
        sb.Append("params.description = \"Verify site\"\n\n");
        sb.Append("[menus]\n");
        sb.Append("[[menus.main]]\nname = \"Home\"\nurl = \"/\"\nweight = 1\n");
        sb.Append("[[menus.main]]\nname = \"Posts\"\nurl = \"/posts/\"\nweight = 2\n");

        // author 形状按主题期望给（同 bash 版 case 分支：
        // loveit/fixit/stack 要求 Author 映射，其余用字符串）
        if (repoDir is "loveit" or "fixit" or "stack")
        {
            sb.Append("params.Author.name = \"Tester\"\n");
            sb.Append("params.Author.link = \"https://example.com/\"\n");
        }
        else
        {
            sb.Append("params.author = \"Tester\"\n");
        }

        WriteAllText(Path.Combine(site, "hugo.toml"), sb.ToString());
    }

    private static readonly Regex ThemeKeyRegex = new(@"^\s*theme\s*[:=]\s*[""']?([\w.\-/]+)", RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>读 config 的 theme 值（TOML/YAML 单文件或 config/_default 目录），读不到回落目录名</summary>
    private static string ReadThemeName(string site, string fallback)
    {
        var candidates = new List<string>();
        foreach (var c in new[] { "hugo.toml", "config.toml", "hugo.yaml", "config.yaml", "hugo.yml", "config.yml" })
        {
            candidates.Add(Path.Combine(site, c));
        }

        var configDefault = Path.Combine(site, "config", "_default");
        if (Directory.Exists(configDefault))
        {
            candidates.AddRange(Directory.GetFiles(configDefault).Order(StringComparer.Ordinal));
        }

        foreach (var path in candidates)
        {
            if (!File.Exists(path))
            {
                continue;
            }

            var text = File.ReadAllText(path);
            var m = ThemeKeyRegex.Match(text);
            if (m.Success)
            {
                return m.Groups[1].Value;
            }
        }

        return fallback;
    }

    private static readonly Regex ThemesDirRegex = new(@"^\s*themesDir\s*[:=].*\n", RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.IgnoreCase);
    private static readonly Regex EnableGitInfoRegex = new(@"^\s*enableGitInfo\s*[:=].*\n", RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.IgnoreCase);

    /// <summary>删 themesDir（复制后相对路径失效）与 enableGitInfo（非 git 站点硬失败）</summary>
    private static void StripThemesDirAndGitInfo(string site)
    {
        var files = new List<string>();
        foreach (var pattern in new[] { "hugo.*", "config.*" })
        {
            files.AddRange(Directory.GetFiles(site, pattern));
        }

        var configDir = Path.Combine(site, "config");
        if (Directory.Exists(configDir))
        {
            files.AddRange(Directory.GetFiles(configDir, "*", SearchOption.AllDirectories));
        }

        foreach (var p in files)
        {
            if (!File.Exists(p))
            {
                continue;
            }

            var text = File.ReadAllText(p);
            var updated = ThemesDirRegex.Replace(text, string.Empty);
            updated = EnableGitInfoRegex.Replace(updated, string.Empty);
            if (updated != text)
            {
                WriteAllText(p, updated);
            }
        }
    }

    // ---------------------------------------------------------------- matrix

    private static Command BuildMatrix()
    {
        var themesArg = new Argument<string[]>("themes") { Description = "主题名；缺省为 21 个矩阵主题" };
        themesArg.Arity = ArgumentArity.ZeroOrMore;

        var cmd = new Command("matrix", "21 主题横向兼容矩阵（Hugo 基线 × Flint 迁移产物 + 门禁④）");
        cmd.Arguments.Add(themesArg);

        cmd.SetAction(async parseResult =>
        {
            var requested = parseResult.GetValue(themesArg) ?? Array.Empty<string>();
            await RunMatrix(requested.Length > 0 ? requested : MatrixThemes).ConfigureAwait(false);
            return 0;
        });

        return cmd;
    }

    private static readonly string[] HugoQuietArgs = { "--quiet" };

    private static readonly string[] FlintMatrixBuildArgs =
    {
        "build", "-s", ".", "-o", "public-flint", "--clean", "--missing-layout", "skip",
    };

    private static async Task RunMatrix(string[] requested)
    {
        var names = requested.Length > 0 ? requested : MatrixThemes;
        var migrator = Path.Combine(RepoPaths.RepoRoot, "src", "Flint.ThemeMigrator", "bin", "Debug", "net10.0", "Flint.ThemeMigrator.exe");
        var flint = Path.Combine(RepoPaths.RepoRoot, "src", "Flint.Cli", "bin", "Release", "net10.0", "win-x64", "Flint.exe");
        var hugo = Path.GetFullPath(HugoExe);

        Console.WriteLine(
            $"{PyCompat.PadRightBytes("主题", 14)} {PyCompat.PadRightBytes("hugo", 6)} {PyCompat.PadRightBytes("页数", 6)} " +
            $"{PyCompat.PadRightBytes("flint构建", 11)} {PyCompat.PadRightBytes("页数", 6)} " +
            $"{PyCompat.PadRightBytes("最小页", 6)} {PyCompat.PadRightBytes("对称", 9)} {PyCompat.PadRightBytes("结构/文本", 7)}");
        Console.WriteLine(new string('-', 86));

        foreach (var name in names)
        {
            await MatrixOne(name, migrator, flint, hugo).ConfigureAwait(false);
        }

        Console.WriteLine(new string('-', 86));
        Console.WriteLine("（hugo/flint构建：通过=exit0 且产出非空；对称=1 表示门禁④通过）");
    }

    private static async Task MatrixOne(string name, string migrator, string flint, string hugo)
    {
        var src = Path.Combine(ThemesDir, name);
        if (!Directory.Exists(Path.Combine(src, "layouts")) && !Directory.Exists(Path.Combine(src, "layout")))
        {
            Console.WriteLine($"{PyCompat.PadRightBytes(name, 14)} 跳过（主题不存在）");
            return;
        }

        var site = Path.Combine(MatrixWorkRoot, name);
        await PrepareMatrixSite(name, site, src).ConfigureAwait(false);

        var report = Path.Combine(MatrixWorkRoot, $"{name}-report.txt");
        File.WriteAllText(report, string.Empty);

        // ---- Hugo 侧 ----
        var hugoRun = await Bench.ProcessRunner.RunTimedAsync(hugo, HugoQuietArgs, workingDirectory: site).ConfigureAwait(false);
        var hugoPages = CountHtml(Path.Combine(site, "public"));
        var hugoOk = hugoRun.ExitCode == 0 && hugoPages > 0 ? "通过" : "失败";

        // ---- 迁移 + Flint 侧 ----
        var migratedDir = Path.Combine(site, "themes-migrated", name);
        var mig = await Bench.ProcessRunner.RunTimedAsync(migrator, new[] { src, migratedDir }).ConfigureAwait(false);
        var migOk = "OK";
        var rateMatch = Regex.Match(mig.Stdout + mig.Stderr, @"rate=([0-9.]+)");
        if (!rateMatch.Success)
        {
            migOk = "迁移异常";
        }

        var siteTheme = Path.Combine(site, "themes", name);
        if (migOk == "OK")
        {
            CleanDir(siteTheme);
            CopyTree(migratedDir, siteTheme);
        }

        var build = await Bench.ProcessRunner.RunTimedAsync(
            flint,
            FlintMatrixBuildArgs,
            workingDirectory: site).ConfigureAwait(false);

        var flintPages = CountHtml(Path.Combine(site, "public-flint"));
        var flintMin = MinHtmlSize(Path.Combine(site, "public-flint"));
        var flintVerdict = "通过";
        if (build.ExitCode != 0)
        {
            flintVerdict = "构建失败";
        }
        else if (flintMin < 200)
        {
            flintVerdict = "空页";
        }

        File.AppendAllText(report, $"=== {name} ===\n");
        File.AppendAllText(report, $"hugo exit={hugoRun.ExitCode} pages={hugoPages}\n");
        var buildOut = build.Stdout + build.Stderr;
        foreach (var line in Regex.Matches(buildOut, @"[^ ]*\.html\([0-9]+,[0-9]+\) : error : .{0,70}")
                     .Select(m => m.Value).Distinct(StringComparer.Ordinal).Take(12))
        {
            File.AppendAllText(report, line + "\n");
        }

        File.AppendAllText(report, "--- 无位置信息 ---\n");
        foreach (var line in Regex.Matches(buildOut, @"error : .{0,80}")
                     .Select(m => m.Value).Distinct(StringComparer.Ordinal).Take(10))
        {
            File.AppendAllText(report, line + "\n");
        }

        // ---- 门禁④（仅基线有效时）----
        var sym = "-";
        var sim = "-";
        if (hugoOk == "通过" && flintPages > 0)
        {
            var gate = await Bench.ProcessRunner.RunTimedAsync(
                migrator,
                new[] { src, migratedDir, "--verify", site, "--site-output", Path.Combine(site, "public-flint"), "--hugo-output", Path.Combine(site, "public") }).ConfigureAwait(false);
            var gateOut = gate.Stdout + gate.Stderr;
            var symMatch = Regex.Match(gateOut, @"symmetric=([01])");
            var stMatch = Regex.Match(gateOut, @"struct=([0-9.]+)");
            var txMatch = Regex.Match(gateOut, @"text=([0-9.]+)");
            if (symMatch.Success)
            {
                sym = symMatch.Groups[1].Value;
            }

            if (stMatch.Success && txMatch.Success)
            {
                sim = $"{stMatch.Groups[1].Value}/{txMatch.Groups[1].Value}";
            }
        }

        Console.WriteLine(
            $"{PyCompat.PadRightBytes(name, 14)} {PyCompat.PadRightBytes(hugoOk, 6)} " +
            $"{PyCompat.PadRightBytes(hugoPages.ToString(), 6)} {PyCompat.PadRightBytes(flintVerdict, 11)} " +
            $"{PyCompat.PadRightBytes(flintPages.ToString(), 6)} " +
            $"{PyCompat.PadRightBytes(flintMin == 0 ? "NA" : flintMin.ToString(), 6)} " +
            $"{PyCompat.PadRightBytes(sym, 9)} {PyCompat.PadRightBytes(sim, 7)}");
    }

    /// <summary>准备矩阵站点：统一内容集 + 基础配置 + 主题专属 params + menus 收尾 + junction 挂载</summary>
    private static async Task PrepareMatrixSite(string name, string site, string themeSrc)
    {
        CleanDir(Path.Combine(site, "themes", name));
        CleanDir(site);
        Directory.CreateDirectory(Path.Combine(site, "themes"));

        MakeMatrixContent(site);
        WriteMatrixConfig(name, site);

        // 主题用 junction 挂载（零拷贝；失败回退复制）——与 bash 版 mklink /J 同机制
        var themeTarget = Path.Combine(site, "themes", name);
        var junction = await Bench.ProcessRunner.RunTimedAsync(
            "cmd",
            new[] { "/c", $"mklink /J \"{themeTarget}\" \"{themeSrc}\"" },
            workingDirectory: RepoPaths.RepoRoot).ConfigureAwait(false);
        if (junction.ExitCode != 0)
        {
            CopyTree(themeSrc, themeTarget);
        }
    }

    private static void MakeMatrixContent(string site)
    {
        WriteAllText(Path.Combine(site, "content", "_index.md"), "---\ntitle: Home\n---\nWelcome to the matrix site.\n");
        WriteAllText(Path.Combine(site, "content", "posts", "_index.md"), "---\ntitle: Posts\n---\nAll posts.\n");
        WriteAllText(
            Path.Combine(site, "content", "posts", "first.md"),
            "---\ntitle: First Post\ndate: 2026-01-15\nlastmod: 2026-02-01\ntags: [intro, test]\ncategories: [general]\ndescription: The first post\nsummary: Summary of the first post\n---\nFirst post body with some text.\n\n## A heading\n\nMore content here.\n\n```go\nfunc main() { fmt.Println(\"hi\") }\n```\n\n### Sub heading\n\nDeeper content.\n");
        WriteAllText(
            Path.Combine(site, "content", "posts", "second.md"),
            "---\ntitle: Second Post\ndate: 2026-02-20\ntags: [test]\ncategories: [general]\ndescription: The second post\n---\nSecond post body.\n");
        WriteAllText(
            Path.Combine(site, "content", "posts", "third.md"),
            "---\ntitle: Third Post\ndate: 2026-03-10\ntags: [misc]\n---\nThird post body.\n");
        WriteAllText(Path.Combine(site, "content", "docs", "_index.md"), "---\ntitle: Docs\n---\nDocumentation section.\n");
        WriteAllText(
            Path.Combine(site, "content", "docs", "guide", "getting-started.md"),
            "---\ntitle: Getting Started\nweight: 10\n---\nGuide body.\n");
        WriteAllText(Path.Combine(site, "content", "about.md"), "---\ntitle: About\n---\nAbout page.\n");
    }

    private static void WriteMatrixConfig(string name, string site)
    {
        var sb = new StringBuilder();
        sb.Append("baseURL = \"https://example.com/\"\n");
        sb.Append("title = \"Matrix Site\"\n");
        sb.Append($"theme = \"{name}\"\n");
        sb.Append("paginate = 2\n");
        sb.Append("enableRobotsTXT = true\n\n");
        sb.Append("[pagination]\n");
        sb.Append("pagerSize = 2\n\n");
        // TOML 铁律：站点参数必须显式 [params] 段，主题参数在此段内追加，menus 永远收尾
        sb.Append("[params]\n");
        sb.Append("description = \"Matrix test site\"\n");

        sb.Append(name switch
        {
            "hugo-book" => "BookSection = \"docs\"\nBookTheme = \"light\"\nBookDateFormat = \"January 2, 2006\"\nBookComments = false\nBookSearch = false\n",
            "blog-awesome" => "Author.name = \"Tester\"\nAuthor.avatar = \"icons/android-chrome-192x192.png\"\n",
            "fixit" => "Author.name = \"Tester\"\nAuthor.link = \"https://example.com/\"\nhome.profile.enable = true\nword_count = true\nreading_time = true\n",
            "loveit" => "Author.name = \"Tester\"\nAuthor.link = \"https://example.com/\"\nhome.profile.enable = true\n",
            "even" => "Author.name = \"Tester\"\nversion = \"4.x\"\narchivePaginate = 50\nshowArchiveCount = false\n",
            "papermod" => "author = \"Tester\"\nhomeInfoParams.Title = \"Matrix Site\"\nhomeInfoParams.Content = \"Matrix test\"\n",
            "stack" => "sidebar.emoji = \"cat\"\nsidebar.subtitle = \"Matrix\"\n[[widgets.homepage]]\ntype = \"search\"\n[[widgets.homepage]]\ntype = \"archives\"\n[[widgets.page]]\ntype = \"toc\"\n",
            "ananke" => "author = \"Tester\"\nananke.show_recent_posts = true\n",
            "blowfish" or "clarity" => "Author.name = \"Tester\"\nAuthor.email = \"tester@example.com\"\n",
            "congo" or "relearn" or "hextra" or "jane" or "mainroad" or "terminal" or "archie"
                or "hermit" or "xmin" or "bearblog" or "console" or "risotto" or "hugo-coder" or "hugo-paper"
                => "author = \"Tester\"\n",
            _ => string.Empty,
        });

        // menus 收尾：TOML 表头之后点号键全挂进该表，必须最后写
        sb.Append("\n[menus]\n");
        sb.Append("[[menus.main]]\nname = \"Home\"\nurl = \"/\"\nweight = 1\n");
        sb.Append("[[menus.main]]\nname = \"Posts\"\nurl = \"/posts/\"\nweight = 2\n");

        WriteAllText(Path.Combine(site, "hugo.toml"), sb.ToString());
        File.Copy(Path.Combine(site, "hugo.toml"), Path.Combine(site, "Flint.toml"), overwrite: true);
    }

    // ---------------------------------------------------------------- 共享 IO

    private static int CountHtml(string dir)
    {
        return Directory.Exists(dir)
            ? Directory.EnumerateFiles(dir, "*.html", SearchOption.AllDirectories).Count()
            : 0;
    }

    private static int MinHtmlSize(string dir)
    {
        return Directory.Exists(dir)
            ? Directory.EnumerateFiles(dir, "*.html", SearchOption.AllDirectories)
                .Select(f => (int)new FileInfo(f).Length).DefaultIfEmpty(0).Min()
            : 0;
    }

    private static int MaxHtmlSize(string dir)
    {
        return Directory.Exists(dir)
            ? Directory.EnumerateFiles(dir, "*.html", SearchOption.AllDirectories)
                .Select(f => (int)new FileInfo(f).Length).DefaultIfEmpty(0).Max()
            : 0;
    }

    private static void WriteAllText(string path, string content)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static void CopyTree(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var file in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(src, file);
            var target = Path.Combine(dst, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    /// <summary>
    /// 目录清理：先清只读属性再删（git pack/Hugo 缓存文件常带只读位，.NET 递归删除会拒），
    /// 普通删除失败时回退 Windows rmdir /s /q（Git Bash rm -rf 的 busy 兜底）
    /// </summary>
    private static void CleanDir(string dir)
    {
        if (!Directory.Exists(dir))
        {
            return;
        }

        try
        {
            ClearReadOnly(dir);
            Directory.Delete(dir, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Bench.ProcessRunner.RunSyncQuiet("cmd", new[] { "/c", $"rmdir /s /q \"{Path.GetFullPath(dir)}\"" });
        }

        if (Directory.Exists(dir))
        {
            Console.Error.WriteLine($"  [警告] 目录未能清理: {dir}");
        }
    }

    private static void ClearReadOnly(string dir)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }
        }
        catch (UnauthorizedAccessException)
        {
            // 个别文件无权限改属性：交给删除/回退路径处理
        }
    }
}
