// bench ssg：Flint vs Hugo 万页同数据集对比基准（scripts/ssg-bench.py 的 C# 移植）
// 口径四件套（与 Python 版逐字一致）：外部高精度计时含进程启动、每次测量前删输出目录
// （冷构建）、预热 1 次、3 次测量取中位数。

using System.CommandLine;

namespace Flint.DevTools.Bench;

/// <summary>
/// ssg 万页对比基准
/// </summary>
internal static class SsgBench
{
    private const string Body =
        "Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed do eiusmod tempor incididunt ut labore et dolore magna aliqua. Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris nisi ut aliquip ex ea commodo consequat.\n\n" +
        "Duis aute irure dolor in reprehenderit in voluptate velit esse cillum dolore eu fugiat nulla pariatur. Excepteur sint occaecat cupidatat non proident, sunt in culpa qui officia deserunt mollit anim id est laborum.\n\n" +
        "Sed ut perspiciatis unde omnis iste natus error sit voluptatem accusantium doloremque laudantium, totam rem aperiam, eaque ipsa quae ab illo inventore veritatis et quasi architecto beatae vitae dicta sunt explicabo.";

    internal static Command BuildCommand()
    {
        var pagesOpt = new Option<int>("--pages") { Description = "页数", DefaultValueFactory = _ => 10000 };
        var runsOpt = new Option<int>("--runs") { Description = "测量轮数（不含预热）", DefaultValueFactory = _ => 3 };
        var flintOpt = new Option<string>("--flint") { Description = "Flint.exe 路径", DefaultValueFactory = _ => DefaultPaths.FlintExe };
        var hugoOpt = new Option<string>("--hugo") { Description = "hugo.exe 路径", DefaultValueFactory = _ => DefaultPaths.HugoExe };
        var rootOpt = new Option<string>("--root") { Description = "语料根", DefaultValueFactory = _ => DefaultPaths.SsgBenchRoot };

        var cmd = new Command("ssg", "Flint vs Hugo 万页同数据集对比基准");
        cmd.Options.Add(pagesOpt);
        cmd.Options.Add(runsOpt);
        cmd.Options.Add(flintOpt);
        cmd.Options.Add(hugoOpt);
        cmd.Options.Add(rootOpt);

        cmd.SetAction(async parseResult =>
        {
            var pages = parseResult.GetValue(pagesOpt);
            var runs = parseResult.GetValue(runsOpt);
        var flint = parseResult.GetValue(flintOpt)!;
        var hugo = parseResult.GetValue(hugoOpt)!;
        var root = Path.GetFullPath(parseResult.GetValue(rootOpt)!);

        Generate(root, pages);
        Console.WriteLine($"语料: {pages} 页 x 2 引擎");

        var hugoPub = Path.Combine(root, "hugo-pub");
            var flintPub = Path.Combine(root, "flint-pub");

            var hm = await BenchAsync("Hugo", hugo, new[] { "-s", Path.Combine(root, "hugo"), "-d", hugoPub, "--quiet" }, hugoPub, runs).ConfigureAwait(false);
            var fm = await BenchAsync("Flint", flint, new[] { "build", "-s", Path.Combine(root, "flint"), "-o", flintPub }, flintPub, runs).ConfigureAwait(false);

            Console.WriteLine($"=== {pages} 页冷构建（{runs} 次中位数，外部计时含进程启动）===");
            Console.WriteLine($"Hugo : {ProcessRunner.Ms(hm)}");
            Console.WriteLine($"Flint: {ProcessRunner.Ms(fm)}");
            Console.WriteLine($"比值 Flint/Hugo = {fm / hm:F2}x");
            return 0;
        });

        return cmd;
    }

    /// <summary>双引擎同 content 同模板：每页 title+date front matter + 三段正文</summary>
    private static void Generate(string root, int pages)
    {
        var baseDate = new DateOnly(2026, 9, 8);

        foreach (var engine in new[] { "hugo", "flint" })
        {
            var site = Path.Combine(root, engine);
            var postsDir = Path.Combine(site, "content", "posts");
            var defaultDir = Path.Combine(site, "layouts", "_default");
            Directory.CreateDirectory(postsDir);
            Directory.CreateDirectory(defaultDir);
            Directory.CreateDirectory(Path.Combine(site, "layouts"));

            for (var i = 0; i < pages; i++)
            {
                var d = baseDate.AddDays(-i);
                var content = $"---\ntitle: \"Page {i}\"\ndate: {d:yyyy-MM-dd}T10:00:00+08:00\n---\n\n{Body}\n";
                File.WriteAllText(Path.Combine(postsDir, $"page-{i:D5}.md"), content, Utf8.NoBom);
            }

            if (engine == "hugo")
            {
                File.WriteAllText(Path.Combine(site, "hugo.toml"),
                    "baseURL = \"http://localhost:1313/\"\ntitle = \"SSG Bench\"\n", Utf8.NoBom);
                File.WriteAllText(Path.Combine(defaultDir, "single.html"),
                    "<article><h1>{{ .Title }}</h1>{{ .Content }}</article>", Utf8.NoBom);
                File.WriteAllText(Path.Combine(site, "layouts", "index.html"),
                    "<h1>Home</h1><ul>{{ range .Site.RegularPages }}<li>{{ .Title }}</li>{{ end }}</ul>", Utf8.NoBom);
            }
            else
            {
                File.WriteAllText(Path.Combine(site, "Flint.toml"),
                    "baseURL = \"http://localhost:1313/\"\ntitle = \"SSG Bench\"\n", Utf8.NoBom);
                File.WriteAllText(Path.Combine(site, "layouts", "single.html"),
                    "<article><h1>{{ page.title }}</h1>{{ page.content }}</article>", Utf8.NoBom);
                File.WriteAllText(Path.Combine(site, "layouts", "index.html"),
                    "<h1>Home</h1><ul>{{ for p in site.regular_pages }}<li>{{ p.title }}</li>{{ end }}</ul>", Utf8.NoBom);
            }
        }
    }

    private static async Task<double> BenchAsync(
        string name, string exe, IReadOnlyList<string> args, string pub, int runs)
    {
        ProcessRunner.RequireExe(exe);

        // 预热（不删 pub，与 Python 版一致）
        (await ProcessRunner.RunTimedAsync(exe, args).ConfigureAwait(false)).AssertSuccess($"{name} 预热");

        var times = new List<double>(runs);
        for (var i = 0; i < runs; i++)
        {
            RepoGuard.DeleteTree(pub);
            var result = await ProcessRunner.RunTimedAsync(exe, args).ConfigureAwait(false);
            result.AssertSuccess($"{name} r{i + 1}");
            times.Add(result.ElapsedMs);
        }

        var median = ProcessRunner.Median(times);
        var runsDesc = string.Join(' ', times.Select((t, i) => $"r{i + 1}={ProcessRunner.Ms(t)}"));
        Console.WriteLine($"{name}: {runsDesc} median={ProcessRunner.Ms(median)}");
        return median;
    }
}
