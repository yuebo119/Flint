// bench complexity：复杂度阶梯对照基准（scripts/complexity-bench.py 的 C# 移植）
// 三级复杂度主题（L1/L2/L3）× 双引擎（Hugo/Scriban），同逻辑双语法实现；
// 每轮 rmtree pub（冷构建）+ 首轮预热 + 取中位数，含峰值 RSS 采样。

using System.CommandLine;

namespace Flint.DevTools.Bench;

/// <summary>
/// 复杂度阶梯基准
/// </summary>
internal static class ComplexityBench
{
    private const string Body =
        "Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.\n\n" +
        "Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris nisi ut aliquip ex ea commodo consequat.\n\n" +
        "Duis aute irure dolor in reprehenderit in voluptate velit esse cillum dolore eu fugiat nulla pariatur.";

    private const string SidebarHugo =
        "<aside>{{ range .Site.RegularPages }}<li><a href=\"{{ .RelPermalink }}\">{{ .Title }}</a></li>{{ end }}</aside>";

    private const string SidebarScriban =
        "<aside>{{ for p in site.regular_pages }}<li><a href=\"{{ p.rel_permalink }}\">{{ p.title }}</a></li>{{ end }}</aside>";

    private const string NavHugo =
        "<nav>{{ range .Site.RegularPages }}<a href=\"{{ .RelPermalink }}\">{{ .Title }}</a>{{ end }}</nav>";

    private const string NavScriban =
        "<nav>{{ for p in site.regular_pages }}<a href=\"{{ p.rel_permalink }}\">{{ p.title }}</a>{{ end }}</nav>";

    private const string RelatedHugo =
        "<div>{{ range .Site.RegularPages }}{{ if eq .Section \"posts\" }}<span>{{ .Title }}</span>{{ end }}{{ end }}</div>";

    private const string RelatedScriban =
        "<div>{{ for p in site.regular_pages }}{{ if p.type == \"page\" }}<span>{{ p.title }}</span>{{ end }}{{ end }}</div>";

    private const string Footer = "<footer>C</footer>";

    private static readonly string[] Levels = { "L1", "L2", "L3" };
    private static readonly string[] Engines = { "hugo", "flint" };

    internal static Command BuildCommand()
    {
        var pagesOpt = new Option<int>("--pages") { Description = "页数", DefaultValueFactory = _ => 1000 };
        var runsOpt = new Option<int>("--runs") { Description = "测量轮数（不含预热）", DefaultValueFactory = _ => 3 };

        var cmd = new Command("complexity", "L1/L2/L3 复杂度阶梯 × 双引擎对照基准");
        cmd.Options.Add(pagesOpt);
        cmd.Options.Add(runsOpt);

        cmd.SetAction(async parseResult =>
        {
            var pages = parseResult.GetValue(pagesOpt);
            var runs = parseResult.GetValue(runsOpt);
            var root = Path.GetFullPath(DefaultPaths.ComplexityRoot);

            GenCorpus(root, pages);

            var jobs = new List<(string Name, string Engine, string Site, string Pub)>();
            foreach (var level in Levels)
            {
                foreach (var engine in Engines)
                {
                    var site = Path.Combine(root, $"{engine}-{level}");
                    RepoGuard.DeleteTree(site);

                    // 复制共享语料
                    var src = Path.Combine(root, "hugo", "content");
                    CopyTree(src, Path.Combine(site, "content"));

                    switch (level)
                    {
                        case "L1":
                            ThemeL1(site, engine);
                            break;
                        case "L2":
                            ThemeL2(site, engine);
                            break;
                        default:
                            ThemeL3(site, engine);
                            break;
                    }

                    jobs.Add(($"{level}-{engine}", engine, site, Path.Combine(site, "pub")));
                }
            }

            Console.WriteLine($"\n=== 复杂度阶梯对照（{pages} 页 × {runs} 次中位数）===");

            var matrix = new Dictionary<string, (double MedianMs, double PeakRssMb)>();
            foreach (var (name, engine, site, pub) in jobs)
            {
                var (exe, args) = EngineCommand(engine, site, pub);
                ProcessRunner.RequireExe(exe);

                var times = new List<double>(runs);
                var peaks = new List<double>(runs);
                for (var i = 0; i < runs + 1; i++) // 首轮预热
                {
                    RepoGuard.DeleteTree(pub);
                    var result = await ProcessRunner.RunTimedAsync(exe, args).ConfigureAwait(false);
                    result.AssertSuccess(name);

                    if (i > 0)
                    {
                        times.Add(result.ElapsedMs);
                        peaks.Add(result.PeakRssBytes / 1048576.0);
                    }
                }

                var median = ProcessRunner.Median(times);
                var peakMedian = ProcessRunner.Median(peaks);
                matrix[name] = (median, peakMedian);

                var timesDesc = string.Join(' ', times.Select(t => $"{t:F0}"));
                Console.WriteLine($"{name}: {timesDesc} | median={median:F0}ms | peakRSS={peakMedian:F0}MB");
            }

            Console.WriteLine("\n=== 复杂度-性能曲线（中位数 ms | 峰值 RSS MB）===");
            Console.WriteLine($"{"层级",-6}{"Hugo",10}{"Flint",10}{"比值",8}{"RSS比",8}");
            foreach (var lv in Levels)
            {
                var h = matrix[$"{lv}-hugo"].MedianMs;
                var f = matrix[$"{lv}-flint"].MedianMs;
                Console.WriteLine($"{lv,-6}{h,10:F0}{f,10:F0}{f / h,8:F2}");
            }

            return 0;
        });

        return cmd;
    }

    private static (string Exe, string[] Args) EngineCommand(string engine, string site, string pub)
    {
        return engine == "hugo"
            ? (DefaultPaths.HugoExe, new[] { "-s", site, "-d", pub, "--quiet" })
            : (DefaultPaths.FlintExe, new[] { "build", "-s", site, "-o", pub });
    }

    /// <summary>固定语料：双引擎共享 content 副本</summary>
    private static void GenCorpus(string root, int pages)
    {
        var baseDate = new DateOnly(2026, 9, 8);

        foreach (var engine in Engines)
        {
            var cdir = Path.Combine(root, engine, "content", "posts");
            RepoGuard.DeleteTree(cdir);
            Directory.CreateDirectory(cdir);

            for (var i = 0; i < pages; i++)
            {
                var d = baseDate.AddDays(-(i % 900));
                var content = $"---\ntitle: \"Page {i}\"\ndate: {d:yyyy-MM-dd}T10:00:00+08:00\ntags: [\"t{i % 20}\"]\n---\n\n{Body}\n";
                File.WriteAllText(Path.Combine(cdir, $"page-{i:D5}.md"), content, Utf8.NoBom);
            }
        }
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

    // ---------------------------------------------------------------- 主题三档

    private static void ThemeL1(string site, string engine)
    {
        var ld = Path.Combine(site, "layouts", "_default");
        Directory.CreateDirectory(ld);

        if (engine == "hugo")
        {
            File.WriteAllText(Path.Combine(site, "hugo.toml"), "baseURL = \"http://x/\"\ntitle = \"C\"\n", Utf8.NoBom);
            File.WriteAllText(Path.Combine(ld, "single.html"), "<article><h1>{{ .Title }}</h1>{{ .Content }}</article>", Utf8.NoBom);
            File.WriteAllText(Path.Combine(ld, "list.html"), "<h1>{{ .Title }}</h1>", Utf8.NoBom);
            File.WriteAllText(Path.Combine(site, "layouts", "index.html"), "<h1>{{ .Site.Title }}</h1>", Utf8.NoBom);
        }
        else
        {
            File.WriteAllText(Path.Combine(site, "Flint.toml"), "baseURL = \"http://x/\"\ntitle = \"C\"\n", Utf8.NoBom);
            File.WriteAllText(Path.Combine(ld, "single.html"), "<article><h1>{{ page.title }}</h1>{{ page.content }}</article>", Utf8.NoBom);
            File.WriteAllText(Path.Combine(ld, "list.html"), "<h1>{{ page.title }}</h1>", Utf8.NoBom);
            File.WriteAllText(Path.Combine(site, "layouts", "index.html"), "<h1>{{ site.title }}</h1>", Utf8.NoBom);
        }
    }

    private static void ThemeL2(string site, string engine)
    {
        ThemeL1(site, engine);
        var ld = Path.Combine(site, "layouts", "_default");
        var sc = Path.Combine(site, "layouts", "partials");
        Directory.CreateDirectory(sc);

        File.WriteAllText(Path.Combine(sc, "sidebar.html"),
            engine == "hugo" ? SidebarHugo : SidebarScriban, Utf8.NoBom);

        File.WriteAllText(Path.Combine(ld, "single.html"),
            engine == "hugo"
                ? "<nav>{{ partial \"sidebar.html\" . }}</nav><article><h1>{{ .Title }}</h1>{{ .Content }}</article>"
                : "<nav>{{ include \"sidebar\" }}</nav><article><h1>{{ page.title }}</h1>{{ page.content }}</article>",
            Utf8.NoBom);
    }

    private static void ThemeL3(string site, string engine)
    {
        ThemeL2(site, engine);
        var ld = Path.Combine(site, "layouts", "_default");
        var sc = Path.Combine(site, "layouts", "partials");

        File.WriteAllText(Path.Combine(sc, "nav.html"), engine == "hugo" ? NavHugo : NavScriban, Utf8.NoBom);
        File.WriteAllText(Path.Combine(sc, "related.html"), engine == "hugo" ? RelatedHugo : RelatedScriban, Utf8.NoBom);
        File.WriteAllText(Path.Combine(sc, "footer.html"), Footer, Utf8.NoBom);

        File.WriteAllText(Path.Combine(ld, "single.html"),
            engine == "hugo"
                ? "<nav>{{ partial \"nav.html\" . }}</nav>{{ partial \"sidebar.html\" . }}<article><h1>{{ .Title }}</h1>{{ .Content }}</article>{{ partial \"related.html\" . }}{{ partialCached \"footer.html\" . }}"
                : "<nav>{{ include \"nav\" }}</nav>{{ include \"sidebar\" }}<article><h1>{{ page.title }}</h1>{{ page.content }}</article>{{ include \"related\" }}{{ partialcached \"footer\" }}",
            Utf8.NoBom);
    }
}
