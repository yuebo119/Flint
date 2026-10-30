// SSG 十四引擎万页矩阵复现器（口径说明见 ../README.md）
//
// 计时：每轮删输出（含各引擎持久缓存）后冷构建，外部计时含进程启动；
//       预热 1 次（不计入），N 轮取中位，轮序轮转。
// 内存：20ms 轮询采样峰值（RSS = WorkingSet64，USS 代理 = PrivateMemorySize64）。
// 审计：html 计数、零字节计数、正文特征抽样命中（语料尾句唯一串）。
//
// 用法（仓库根执行）：
//   dotnet run --project benchmarks/ssg-matrix/harness -c Release
//   dotnet run --project benchmarks/ssg-matrix/harness -c Release -- --list
//   dotnet run --project benchmarks/ssg-matrix/harness -c Release -- --pages 2000 --rounds 1 --only flint-liquid,hugo
//   dotnet run --project benchmarks/ssg-matrix/harness -c Release -- --flint <Flint.exe> --hugo <hugo.exe>

using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text;

var opt = Cli.Parse(args);
var root = Path.GetFullPath(opt.Root ?? Path.Combine(SourceDir(), ".."));
var tools = Path.GetFullPath(Path.Combine(root, "..", "tools"));
Paths.Root = root;

try { Console.OutputEncoding = Encoding.UTF8; } catch { /* 重定向环境可能不支持 */ }

Console.WriteLine($"矩阵根: {root}");
Console.WriteLine($"语料: {opt.Pages} 页/臂 · 轮数 {opt.Rounds} · 预热 {opt.Warmup}");

if (opt.Clean || opt.CleanAll)
{
    var freed = Cleaner.Run(root, opt.CleanAll);
    Console.WriteLine($"已删除 {freed.Files} 个文件，释放约 {freed.Bytes / 1048576.0:F0} MB");
    Console.WriteLine(opt.CleanAll
        ? "含依赖与构建缓存，下次运行需重新 npm install / dotnet build。"
        : "依赖（node_modules、bin/obj）保留；语料下次运行会自动重建。");
    return;
}

if (opt.Bootstrap)
    Setup.Run(root, tools, opt.Only);

var corpora = new[]
{
    new CorpusSpec("flint-scriban", "flint-scriban/content", "yaml", null),
    new CorpusSpec("flint-liquid", "flint-liquid/content", "yaml", null),
    new CorpusSpec("hugo", "hugo/content/posts", "yaml", null),
    new CorpusSpec("astro", "astro/src/pages/posts", "yaml", "../../layouts/post.astro"),
    new CorpusSpec("eleventy", "eleventy/src/posts", "yaml", "post.njk"),
    new CorpusSpec("jekyll", "jekyll/_docs", "yaml", "default"),
    new CorpusSpec("vitepress", "vitepress/docs/posts", "yaml", null),
    new CorpusSpec("vuepress", "vuepress/docs/posts", "yaml", null),
    new CorpusSpec("gatsby", "gatsby/content", "yaml", null),
    new CorpusSpec("hexo", "hexo/source/_posts", "yaml", null),
    new CorpusSpec("mkdocs", "mkdocs/docs", "yaml", null),
    new CorpusSpec("pelican", "pelican/content", "pelican", null),
    new CorpusSpec("zola", "zola/content/blog", "toml", null),
    new CorpusSpec("statiq", "statiq/input/posts", "yaml", "/_Layout.cshtml"),
};

if (!opt.SkipGen)
{
    Console.WriteLine("\n--- 语料 ---");
    foreach (var c in corpora)
        Corpus.Ensure(Path.Combine(root, c.Dir), opt.Pages, c.Kind, c.Layout, opt.Regen);
}

var all = Arms.Build(root, tools, opt);
var selected = opt.Only is null ? all : all.Where(a => opt.Only.Contains(a.Name)).ToList();

Console.WriteLine($"\n--- 就绪状态（{selected.Count} 臂）---");
foreach (var a in selected)
    Console.WriteLine($"{a.Name,-14} {(a.Ready ? "OK" : "缺依赖: " + string.Join(", ", a.Missing))}");

if (opt.List) return;

var ready = selected.Where(a => a.Ready).ToList();
var skipped = selected.Where(a => !a.Ready).ToList();
if (ready.Count == 0)
{
    Console.WriteLine("\n没有可运行的臂。安装步骤见 benchmarks/ssg-matrix/README.md");
    return;
}

if (opt.Warmup > 0)
{
    Console.WriteLine($"\n--- 预热（{opt.Warmup} 轮，不计入）---");
    foreach (var a in ready)
    {
        var m = Run.Once(a);
        Console.WriteLine($"{a.Name,-14} {m.Ms,9:F0} ms（预热）");
    }
}

var times = ready.Select(_ => new List<double>()).ToArray();
var mems = ready.Select(_ => new List<long>()).ToArray();

for (var r = 0; r < opt.Rounds; r++)
{
    var order = Enumerable.Range(0, ready.Count).Select(i => (i + r * 3) % ready.Count).ToArray();
    Console.WriteLine($"\n--- 第 {r + 1}/{opt.Rounds} 轮（首臂 {ready[order[0]].Name}，{DateTime.Now:HH:mm:ss}）---");
    foreach (var idx in order)
    {
        var m = Run.Once(ready[idx]);
        times[idx].Add(m.Ms);
        mems[idx].Add(m.PeakRss);
        Console.WriteLine($"{ready[idx].Name,-14} {m.Ms,9:F0} ms   峰值 RSS {m.PeakRss / 1048576.0,7:F0} MB");
    }
}

var medians = times.Select(Median).ToArray();
var fastest = medians.Min();

Console.WriteLine($"\n=== {opt.Pages} 页冷构建（{opt.Rounds} 轮中位，外部计时含进程启动）===");
Console.WriteLine($"{"臂",-14} {"中位(ms)",10} {"vs最快",8} {"峰值RSS(MB)",12} {"html",7} {"正文抽样",9} {"零字节",7}");
for (var i = 0; i < ready.Count; i++)
{
    var audit = Audit.Scan(ready[i].Pub);
    Console.WriteLine($"{ready[i].Name,-14} {medians[i],10:F0} {medians[i] / fastest,7:F2}x {mems[i].Max() / 1048576.0,11:F0} {audit.Html,7} {audit.BodySample,9} {audit.Zero,7}");
}

if (skipped.Count > 0)
{
    Console.WriteLine("\n--- 未运行（缺依赖）---");
    foreach (var a in skipped)
        Console.WriteLine($"{a.Name,-14} 缺: {string.Join(", ", a.Missing)}");
    Console.WriteLine("安装命令见 benchmarks/ssg-matrix/README.md");
}

static double Median(List<double> xs)
{
    var s = xs.OrderBy(x => x).ToList();
    return s.Count % 2 == 1 ? s[s.Count / 2] : (s[s.Count / 2 - 1] + s[s.Count / 2]) / 2;
}

static string SourceDir([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;

// ---------------------------------------------------------------- 类型

sealed record CorpusSpec(string Arm, string Dir, string Kind, string? Layout);

sealed record Arm(string Name, string Exe, string[] Args, string Cwd, string Pub, string[] Clean)
{
    public List<string> Missing { get; } = new();
    public bool Ready => Missing.Count == 0;
}

static class Cleaner
{
    // 语料、产出与各引擎持久缓存；node_modules 与 bin/obj 仅在 --clean-all 时删除
    static readonly string[] Generated =
    {
        "flint-scriban/content", "flint-liquid/content", "hugo/content/posts",
        "astro/src/pages/posts", "eleventy/src/posts", "jekyll/_docs",
        "vitepress/docs/posts", "vuepress/docs/posts", "gatsby/content",
        "hexo/source/_posts", "mkdocs/docs", "pelican/content",
        "zola/content/blog", "statiq/input/posts",
        "_out",
        "zola/public", "eleventy/_site", "astro/dist", "hexo/public", "hexo/db.json",
        "gatsby/public", "gatsby/.cache",
        "vitepress/docs/.vitepress/dist", "vitepress/docs/.vitepress/cache", "vitepress/docs/.vitepress/.temp",
        "vuepress/docs/.vuepress/dist", "vuepress/docs/.vuepress/cache", "vuepress/docs/.vuepress/.temp",
        "jekyll/.jekyll-cache", "jekyll/.jekyll-metadata",
        "statiq/output", "statiq/cache", "statiq/temp",
        "hugo/.hugo_build.lock",
    };

    static readonly string[] Dependencies =
    {
        "eleventy/node_modules", "hexo/node_modules", "vitepress/node_modules",
        "astro/node_modules", "gatsby/node_modules", "vuepress/node_modules",
        "statiq/bin", "statiq/obj", "harness/bin", "harness/obj",
    };

    public static (int Files, long Bytes) Run(string root, bool includeDeps)
    {
        var targets = Generated.Concat(includeDeps ? Dependencies : Array.Empty<string>())
            .Select(rel => Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar)))
            .Where(p => Directory.Exists(p) || File.Exists(p))
            .ToList();

        long bytes = 0;
        var files = 0;
        foreach (var target in targets)
        {
            long size;
            int count;
            if (Directory.Exists(target))
            {
                var infos = Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories)
                    .Select(f => new FileInfo(f)).ToList();
                size = infos.Sum(f => f.Length);
                count = infos.Count;
            }
            else
            {
                size = new FileInfo(target).Length;
                count = 1;
            }

            bytes += size;
            files += count;
            Console.WriteLine($"  删除 {Path.GetRelativePath(root, target),-38} {size / 1048576.0,8:F1} MB  ({count} 文件)");

            if (!Paths.IsInside(target))
                throw new InvalidOperationException($"拒绝删除矩阵目录之外的路径: {target}");

            if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
            else File.Delete(target);
        }

        return (files, bytes);
    }
}

static class Setup
{
    const string HugoVersion = "0.169.0";
    const string ZolaVersion = "0.23.6";

    public static void Run(string root, string tools, HashSet<string>? only)
    {
        bool Want(string name) => only is null || only.Contains(name);

        Console.WriteLine("\n--- 依赖装配（--bootstrap，已就绪的步骤自动跳过）---");
        Directory.CreateDirectory(tools);

        if (Want("hugo")) EnsureBinary(tools, "hugo", HugoBase(), HugoAsset(), HugoEntry());
        if (Want("zola")) EnsureBinary(tools, "zola", ZolaBase(), ZolaAsset(), ZolaEntry());
        if (Want("flint-scriban") || Want("flint-liquid")) EnsureFlint(root, tools);

        foreach (var name in new[] { "astro", "eleventy", "hexo", "gatsby", "vitepress", "vuepress" })
        {
            if (!Want(name)) continue;
            var site = Path.Combine(root, name);
            if (Directory.Exists(Path.Combine(site, "node_modules"))) { Skip($"{name}/node_modules 已存在"); continue; }
            var npm = Find("npm");
            if (npm is null) { Fail($"{name}: 未找到 npm"); continue; }
            Step($"{name}: npm install", () => Exec(npm, ["install", "--no-audit", "--no-fund"], site));
        }

        var python = Find("python") ?? Find("python3") ?? Find("py");
        foreach (var module in new[] { "pelican", "mkdocs" })
        {
            if (!Want(module)) continue;
            if (python is null) { Fail($"{module}: 未找到 python"); continue; }
            if (Exec(python, ["-c", $"import {module}"], root) == 0) { Skip($"{module} 已安装"); continue; }
            Step($"{module}: pip install", () => Exec(python, ["-m", "pip", "install", "--disable-pip-version-check", "-q", module], root));
        }

        if (Want("jekyll"))
        {
            var gem = Find("gem");
            if (gem is null) Fail("jekyll: 未找到 gem（需先安装 Ruby）");
            else if (Exec(gem, ["list", "-i", "jekyll"], root) == 0) Skip("jekyll 已安装");
            else Step("jekyll: gem install", () => Exec(gem, ["install", "jekyll", "--no-document"], root));
        }

        Console.WriteLine("--- 装配结束 ---");
    }

    static void EnsureBinary(string tools, string name, string urlBase, string asset, string entry)
    {
        var dest = Path.Combine(tools, ExecutableName(name));
        if (File.Exists(dest)) { Skip($"{name} 已存在: {Path.GetFileName(dest)}"); return; }

        Step($"{name}: 下载 {asset}", () =>
        {
            var tmp = Path.Combine(Path.GetTempPath(), $"{name}-{Guid.NewGuid():N}{Path.GetExtension(asset).Replace(".gz", "")}");
            using (var client = new HttpClient(HttpHandler(), disposeHandler: true) { Timeout = TimeSpan.FromMinutes(10) })
            using (var stream = client.GetStreamAsync($"{urlBase}/{asset}").GetAwaiter().GetResult())
            using (var file = File.Create(tmp))
                stream.CopyTo(file);

            Extract(tmp, entry, dest);
            File.Delete(tmp);
            return 0;
        });
    }

    static HttpClientHandler HttpHandler()
    {
        var handler = new HttpClientHandler();
        var proxy = Environment.GetEnvironmentVariable("HTTPS_PROXY")
            ?? Environment.GetEnvironmentVariable("https_proxy")
            ?? Environment.GetEnvironmentVariable("HTTP_PROXY")
            ?? Environment.GetEnvironmentVariable("http_proxy");
        if (!string.IsNullOrWhiteSpace(proxy)) handler.Proxy = new System.Net.WebProxy(proxy);
        return handler;
    }

    static void Extract(string archive, string entry, string dest)
    {
        if (archive.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            using var zip = System.IO.Compression.ZipFile.OpenRead(archive);
            var e = zip.Entries.First(x => x.Name.Equals(entry, StringComparison.OrdinalIgnoreCase));
            e.ExtractToFile(dest, overwrite: true);
            return;
        }

        using var file = File.OpenRead(archive);
        using var gzip = new System.IO.Compression.GZipStream(file, System.IO.Compression.CompressionMode.Decompress);
        using var tar = new System.Formats.Tar.TarReader(gzip);
        while (tar.GetNextEntry() is { } e)
        {
            if (!e.Name.EndsWith(entry, StringComparison.OrdinalIgnoreCase)) continue;
            using var output = File.Create(dest);
            e.DataStream?.CopyTo(output);
            return;
        }

        throw new InvalidOperationException($"归档中未找到 {entry}");
    }

    static void EnsureFlint(string root, string tools)
    {
        var dest = Path.Combine(tools, "flint-aot", ExecutableName("Flint"));
        if (File.Exists(dest)) { Skip("Flint AOT 已存在"); return; }

        Step("Flint: dotnet publish AOT（约 1-3 分钟）", () =>
        {
            var repoRoot = Path.GetFullPath(Path.Combine(root, "..", ".."));
            var output = Path.Combine(tools, "flint-aot");
            return Exec("dotnet",
                ["publish", Path.Combine(repoRoot, "src", "Flint.Cli"), "-c", "Release", "-r", Rid(), "-p:PublishAot=true", "-o", output],
                repoRoot);
        });
    }

    static int Exec(string exe, string[] args, string cwd)
    {
        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = cwd,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var p = Process.Start(psi) ?? throw new InvalidOperationException($"无法启动 {exe}");
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        p.WaitForExit();
        var output = stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult();
        if (p.ExitCode != 0) Console.WriteLine($"    {Tail(output)}");

        return p.ExitCode;
    }

    static void Step(string label, Func<int> action)
    {
        Console.WriteLine($"  {label} …");
        try
        {
            var code = action();
            Console.WriteLine(code == 0 ? "    完成" : $"    失败（exit={code}），该引擎将跳过");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"    失败: {ex.Message}");
        }
    }

    static void Skip(string label) => Console.WriteLine($"  跳过 {label}");
    static void Fail(string label) => Console.WriteLine($"  失败 {label}");
    static string Tail(string s) => s.Length <= 600 ? s : s[^600..];

    static string ExecutableName(string name) => OperatingSystem.IsWindows() ? name + ".exe" : name;

    static string Rid() => (OperatingSystem.IsWindows(), OperatingSystem.IsMacOS(), System.Runtime.InteropServices.RuntimeInformation.OSArchitecture) switch
    {
        (true, _, System.Runtime.InteropServices.Architecture.Arm64) => "win-arm64",
        (true, _, _) => "win-x64",
        (_, true, System.Runtime.InteropServices.Architecture.Arm64) => "osx-arm64",
        (_, true, _) => "osx-x64",
        (_, _, System.Runtime.InteropServices.Architecture.Arm64) => "linux-arm64",
        _ => "linux-x64",
    };

    static string HugoBase() => $"https://github.com/gohugoio/hugo/releases/download/v{HugoVersion}";
    static string ZolaBase() => $"https://github.com/getzola/zola/releases/download/v{ZolaVersion}";

    static string HugoAsset()
    {
        if (OperatingSystem.IsWindows()) return $"hugo_extended_{HugoVersion}_windows-amd64.zip";
        if (OperatingSystem.IsMacOS()) return $"hugo_extended_{HugoVersion}_darwin-universal.tar.gz";
        return $"hugo_extended_{HugoVersion}_linux-amd64.tar.gz";
    }

    static string HugoEntry() => OperatingSystem.IsWindows() ? "hugo.exe" : "hugo";

    static string ZolaAsset()
    {
        if (OperatingSystem.IsWindows()) return $"zola-v{ZolaVersion}-x86_64-pc-windows-msvc.zip";
        if (OperatingSystem.IsMacOS())
            return System.Runtime.InteropServices.RuntimeInformation.OSArchitecture == System.Runtime.InteropServices.Architecture.Arm64
                ? $"zola-v{ZolaVersion}-aarch64-apple-darwin.tar.gz"
                : $"zola-v{ZolaVersion}-x86_64-apple-darwin.tar.gz";
        return $"zola-v{ZolaVersion}-x86_64-unknown-linux-gnu.tar.gz";
    }

    static string ZolaEntry() => OperatingSystem.IsWindows() ? "zola.exe" : "zola";

    static string? Find(string name)
    {
        var exts = OperatingSystem.IsWindows() ? new[] { ".exe", ".cmd", ".bat", "" } : new[] { "" };
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var ext in exts)
            {
                var candidate = Path.Combine(dir.Trim(), name + ext);
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }
}

static class Paths
{
    public static string Root = "";

    public static bool IsInside(string path)
    {
        var full = Path.GetFullPath(path);
        return full.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}

static class Corpus
{
    public const string Body =
        "Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed do eiusmod tempor incididunt ut labore et dolore magna aliqua. Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris nisi ut aliquip ex ea commodo consequat.\n\n" +
        "Duis aute irure dolor in reprehenderit in voluptate velit esse cillum dolore eu fugiat nulla pariatur. Excepteur sint occaecat cupidatat non proident, sunt in culpa qui officia deserunt mollit anim id est laborum.\n\n" +
        "Sed ut perspiciatis unde omnis iste natus error sit voluptatem accusantium doloremque laudantium, totam rem aperiam, eaque ipsa quae ab illo inventore veritatis et quasi architecto beatae vitae dicta sunt explicabo.";

    public static void Ensure(string dir, int pages, string kind, string? layout, bool force)
    {
        var have = Directory.Exists(dir) ? Directory.GetFiles(dir, "page-*.md").Length : 0;
        if (!force && have == pages)
        {
            Console.WriteLine($"  {Short(dir)}: 就绪（{pages} 页）");
            return;
        }

        Directory.CreateDirectory(dir);
        foreach (var f in Directory.GetFiles(dir, "page-*.md")) File.Delete(f);

        var utf8 = new UTF8Encoding(false);
        var baseDate = new DateOnly(2026, 9, 8);
        for (var i = 0; i < pages; i++)
        {
            var d = baseDate.AddDays(-i);
            var text = kind switch
            {
                "toml" => $"+++\ntitle = \"Page {i}\"\ndate = {d:yyyy-MM-dd}T10:00:00+08:00\n+++\n\n{Body}\n",
                "pelican" => $"Title: Page {i}\nDate: {d:yyyy-MM-dd} 10:00:00\n\n{Body}\n",
                _ => $"---\ntitle: \"Page {i}\"\n{(layout is null ? "" : $"layout: {layout}\n")}date: {d:yyyy-MM-dd}T10:00:00+08:00\n---\n\n{Body}\n",
            };
            File.WriteAllText(Path.Combine(dir, $"page-{i:D5}.md"), text, utf8);
        }

        Console.WriteLine($"  {Short(dir)}: 生成 {pages} 页");
    }

    static string Short(string dir)
    {
        var rel = Path.GetRelativePath(Paths.Root, dir).Replace('\\', '/');
        return rel;
    }
}

static class Audit
{
    public sealed record Result(int Html, int Zero, string BodySample);

    public static Result Scan(string pub)
    {
        if (!Directory.Exists(pub)) return new Result(0, 0, "0/0");

        var files = Directory.EnumerateFiles(pub, "*.html", SearchOption.AllDirectories).ToArray();
        var zero = files.Count(f => new FileInfo(f).Length == 0);
        if (files.Length == 0) return new Result(0, zero, "0/0");

        var take = Math.Min(100, files.Length);
        var step = Math.Max(1, files.Length / take);
        var sample = new List<string>(take);
        for (var i = 0; i < files.Length && sample.Count < take; i += step) sample.Add(files[i]);

        var hit = sample.Count(HasBody);
        return new Result(files.Length, zero, $"{hit}/{sample.Count}");
    }

    static bool HasBody(string path)
    {
        try
        {
            using var reader = new StreamReader(path);
            var buffer = new char[64 * 1024];
            var window = new StringBuilder();
            int n;
            while ((n = reader.Read(buffer, 0, buffer.Length)) > 0)
            {
                window.Append(buffer, 0, n);
                if (window.ToString().Contains("dicta sunt explicabo", StringComparison.Ordinal)) return true;
                if (window.Length > 2_000_000) return false;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }
}

static class Run
{
    public sealed record Measured(double Ms, long PeakRss);

    public static Measured Once(Arm a)
    {
        foreach (var p in a.Clean.Append(a.Pub)) Delete(p);

        var psi = new ProcessStartInfo(a.Exe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = a.Cwd,
        };
        foreach (var x in a.Args) psi.ArgumentList.Add(x);

        var sw = Stopwatch.StartNew();
        using var p2 = Process.Start(psi) ?? throw new InvalidOperationException($"无法启动: {a.Exe}");
        var outTask = p2.StandardOutput.ReadToEndAsync();
        var errTask = p2.StandardError.ReadToEndAsync();

        long rss = 0;
        while (!p2.WaitForExit(20))
        {
            try
            {
                p2.Refresh();
                rss = Math.Max(rss, p2.WorkingSet64);
            }
            catch (InvalidOperationException) { break; }
            catch (System.ComponentModel.Win32Exception) { break; }
        }

        sw.Stop();
        var stdout = outTask.GetAwaiter().GetResult();
        var stderr = errTask.GetAwaiter().GetResult();

        if (p2.ExitCode != 0)
            throw new InvalidOperationException(
                $"{a.Name} 构建失败 exit={p2.ExitCode}\n--- stdout ---\n{Tail(stdout)}\n--- stderr ---\n{Tail(stderr)}");

        return new Measured(sw.Elapsed.TotalMilliseconds, rss);
    }

    static string Tail(string s) => s.Length <= 1500 ? s : s[^1500..];

    static void Delete(string path)
    {
        if (!Directory.Exists(path) && !File.Exists(path)) return;
        if (!Paths.IsInside(path)) throw new InvalidOperationException($"拒绝删除矩阵目录之外的路径: {path}");

        if (Directory.Exists(path)) Directory.Delete(path, true);
        else File.Delete(path);
    }
}

static class Arms
{
    public static List<Arm> Build(string root, string tools, Cli opt)
    {
        var list = new List<Arm>();

        var flint = Find(opt.Flint, Path.Combine(tools, "flint-aot", Exe("Flint")), "flint");
        var hugo = Find(opt.Hugo, Path.Combine(tools, Exe("hugo")), "hugo");
        var zola = Find(opt.Zola, Path.Combine(tools, Exe("zola")), "zola");
        var node = Find(opt.Node, null, "node");
        var python = opt.Python ?? DefaultPython();
        var jekyll = Find(null, null, "jekyll");

        string P(params string[] parts) => Path.Combine(new[] { root }.Concat(parts).ToArray());
        var outRoot = P("_out");

        // --- 编译型与自带运行时的引擎 ---
        Add("flint-scriban", flint, ["build", "-s", P("flint-scriban"), "-o", Path.Combine(outRoot, "flint-scriban")],
            root, Path.Combine(outRoot, "flint-scriban"), [], "Flint 可执行文件（--flint 或 benchmarks/tools/flint-aot）");

        Add("flint-liquid", flint, ["build", "-s", P("flint-liquid"), "-o", Path.Combine(outRoot, "flint-liquid")],
            root, Path.Combine(outRoot, "flint-liquid"), [], "Flint 可执行文件（--flint 或 benchmarks/tools/flint-aot）");

        Add("hugo", hugo, ["-s", P("hugo"), "-d", Path.Combine(outRoot, "hugo"), "--quiet"],
            root, Path.Combine(outRoot, "hugo"), [], "hugo（--hugo、benchmarks/tools/hugo.exe 或 PATH）");

        Add("zola", zola, ["build"], P("zola"), P("zola", "public"), [], "zola（--zola、benchmarks/tools 或 PATH）");

        // --- Node 系 ---
        var astro = P("astro");
        Add("astro", node, [Path.Combine(astro, "node_modules", "astro", "bin", "astro.mjs"), "build"],
            astro, Path.Combine(astro, "dist"), [], "node");
        RequireDir(list, "astro", Path.Combine(astro, "node_modules", "astro"), "astro/node_modules（npm install）");

        var eleventy = P("eleventy");
        Add("eleventy", node, [Path.Combine(eleventy, "node_modules", "@11ty", "eleventy", "cmd.cjs")],
            eleventy, Path.Combine(eleventy, "_site"), [], "node");
        RequireDir(list, "eleventy", Path.Combine(eleventy, "node_modules", "@11ty", "eleventy"), "eleventy/node_modules（npm install）");

        var hexo = P("hexo");
        Add("hexo", node, ["run-hexo.js"], hexo, Path.Combine(hexo, "public"),
            [Path.Combine(hexo, "db.json")], "node");
        RequireDir(list, "hexo", Path.Combine(hexo, "node_modules", "hexo"), "hexo/node_modules（npm install）");

        var gatsby = P("gatsby");
        Add("gatsby", node, [Path.Combine(gatsby, "node_modules", "gatsby", "dist", "bin", "gatsby.js"), "build"],
            gatsby, Path.Combine(gatsby, "public"), [Path.Combine(gatsby, ".cache")], "node");
        RequireDir(list, "gatsby", Path.Combine(gatsby, "node_modules", "gatsby"), "gatsby/node_modules（npm install）");

        var vitepress = P("vitepress");
        Add("vitepress", node, [Path.Combine(vitepress, "node_modules", "vitepress", "bin", "vitepress.js"), "build"],
            vitepress, Path.Combine(vitepress, "docs", ".vitepress", "dist"),
            [Path.Combine(vitepress, "docs", ".vitepress", "cache"), Path.Combine(vitepress, "docs", ".vitepress", ".temp")], "node");
        RequireDir(list, "vitepress", Path.Combine(vitepress, "node_modules", "vitepress"), "vitepress/node_modules（npm install）");

        var vuepress = P("vuepress");
        Add("vuepress", node, [Path.Combine(vuepress, "node_modules", "vuepress", "bin", "vuepress.js"), "build", "docs"],
            vuepress, Path.Combine(vuepress, "docs", ".vuepress", "dist"),
            [Path.Combine(vuepress, "docs", ".vuepress", "cache"), Path.Combine(vuepress, "docs", ".vuepress", ".temp")], "node");
        RequireDir(list, "vuepress", Path.Combine(vuepress, "node_modules", "vuepress"), "vuepress/node_modules（npm install）");

        // --- Python 系 ---
        var pelican = P("pelican");
        Add("pelican", python, ["-m", "pelican", "content", "-o", Path.Combine(outRoot, "pelican"), "-s", "pelicanconf.py"],
            pelican, Path.Combine(outRoot, "pelican"), [], "python（--python）");

        var mkdocs = P("mkdocs");
        Add("mkdocs", python, ["-m", "mkdocs", "build", "-d", Path.Combine(outRoot, "mkdocs")],
            mkdocs, Path.Combine(outRoot, "mkdocs"), [], "python（--python）");

        // --- Ruby 系：用 ruby -S jekyll 直调，避免 cmd.exe 包装器污染内存采样 ---
        var jekyllSite = P("jekyll");
        var jekyllOut = Path.Combine(outRoot, "jekyll");
        var jekyllClean = new[] { Path.Combine(jekyllSite, ".jekyll-cache"), Path.Combine(jekyllSite, ".jekyll-metadata") };
        var ruby = Find(null, null, "ruby");
        if (ruby is not null)
            Add("jekyll", ruby, ["-S", "jekyll", "build", "-s", jekyllSite, "-d", jekyllOut], jekyllSite, jekyllOut, jekyllClean,
                "ruby（gem install jekyll）");
        else if (OperatingSystem.IsWindows())
            Add("jekyll", jekyll is null ? null : "cmd.exe",
                ["/c", "jekyll", "build", "-s", jekyllSite, "-d", jekyllOut], jekyllSite, jekyllOut, jekyllClean,
                "jekyll（gem install jekyll）");
        else
            Add("jekyll", jekyll, ["build", "-s", jekyllSite, "-d", jekyllOut], jekyllSite, jekyllOut, jekyllClean,
                "jekyll（gem install jekyll）");

        // --- Statiq：先构建 runner，再直接跑 dll（构建时间不计入） ---
        var statiqSite = P("statiq");
        var statiqDll = Path.Combine(statiqSite, "bin", "Release", "net10.0", "SsgMatrix.Statiq.dll");
        var wantStatiq = opt.Only is null || opt.Only.Contains("statiq");
        if (wantStatiq && !File.Exists(statiqDll))
        {
            Console.WriteLine("\n构建 Statiq runner（一次性，不计入计时）…");
            statiqDll = BuildStatiq(statiqSite) ?? statiqDll;
        }
        var dotnet = Find(null, null, "dotnet");
        Add("statiq", File.Exists(statiqDll) ? dotnet : null,
            [statiqDll, "--root", statiqSite], root, Path.Combine(statiqSite, "output"),
            [Path.Combine(statiqSite, "cache"), Path.Combine(statiqSite, "temp")],
            "dotnet SDK + Statiq runner 构建（见 README）");

        return list;

        void Add(string name, string? exe, string[] a, string cwd, string pub, string[] clean, string need)
        {
            var arm = new Arm(name, exe ?? "dotnet", a, cwd, pub, clean);
            if (exe is null) arm.Missing.Add(need);
            list.Add(arm);
        }

        static void RequireDir(List<Arm> arms, string name, string dir, string hint)
        {
            if (Directory.Exists(dir)) return;
            arms.First(a => a.Name == name).Missing.Add(hint);
        }
    }

    static string? BuildStatiq(string site)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = site,
        };
        foreach (var x in new[] { "build", "-c", "Release", "--nologo", "-v", "q" }) psi.ArgumentList.Add(x);

        try
        {
            using var p = Process.Start(psi);
            if (p is null) return null;
            var stdout = p.StandardOutput.ReadToEndAsync();
            var stderr = p.StandardError.ReadToEndAsync();
            p.WaitForExit();
            if (p.ExitCode != 0)
            {
                Console.WriteLine($"Statiq runner 构建失败:\n{stdout.GetAwaiter().GetResult()}\n{stderr.GetAwaiter().GetResult()}");
                return null;
            }
            var dll = Path.Combine(site, "bin", "Release", "net10.0", "SsgMatrix.Statiq.dll");
            return File.Exists(dll) ? dll : null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Statiq runner 构建异常: {ex.Message}");
            return null;
        }
    }

    static string? Find(string? explicitPath, string? preferred, string name)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
            return File.Exists(explicitPath) ? Path.GetFullPath(explicitPath) : null;

        if (preferred is not null && File.Exists(preferred))
            return Path.GetFullPath(preferred);

        var exts = OperatingSystem.IsWindows() ? new[] { ".exe", ".cmd", ".bat", "" } : new[] { "" };
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var ext in exts)
            {
                var candidate = Path.Combine(dir.Trim(), name + ext);
                if (File.Exists(candidate)) return candidate;
            }
        }

        return null;
    }

    static string Exe(string name) => OperatingSystem.IsWindows() ? name + ".exe" : name;

    static string? DefaultPython()
    {
        // 优先真实解释器（python.exe 直接托管引擎进程）；py.exe 是启动器，
        // 内存采样会落在启动器上而不是引擎进程，只在必须时兜底
        return Find(null, null, "python")
            ?? Find(null, null, "python3")
            ?? Find(null, null, "py");
    }
}

sealed class Cli
{
    public int Pages = 10000;
    public int Rounds = 3;
    public int Warmup = 1;
    public bool SkipGen;
    public bool Regen;
    public bool List;
    public bool Clean;
    public bool CleanAll;
    public bool Bootstrap;
    public string? Root;
    public string? Flint;
    public string? Hugo;
    public string? Zola;
    public string? Node;
    public string? Python;
    public HashSet<string>? Only;

    public static Cli Parse(string[] args)
    {
        var c = new Cli();
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--pages": c.Pages = int.Parse(args[++i]); break;
                case "--rounds": c.Rounds = int.Parse(args[++i]); break;
                case "--warmup": c.Warmup = int.Parse(args[++i]); break;
                case "--skip-gen": c.SkipGen = true; break;
                case "--regen": c.Regen = true; break;
                case "--list": c.List = true; break;
                case "--clean": c.Clean = true; break;
                case "--clean-all": c.CleanAll = true; break;
                case "--bootstrap": c.Bootstrap = true; break;
                case "--root": c.Root = args[++i]; break;
                case "--flint": c.Flint = args[++i]; break;
                case "--hugo": c.Hugo = args[++i]; break;
                case "--zola": c.Zola = args[++i]; break;
                case "--node": c.Node = args[++i]; break;
                case "--python": c.Python = args[++i]; break;
                case "--only":
                    c.Only = args[++i]
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                    break;
                case "-h":
                case "--help":
                    Console.WriteLine(
                        """
                        选项:
                          --pages N      每臂页数（默认 10000）
                          --rounds N     计时轮数（默认 3，取中位）
                          --warmup N     预热轮数（默认 1，不计入）
                          --only a,b     只跑指定臂
                          --list         只打印就绪状态
                          --clean        清理语料、产出与各引擎缓存（保留依赖）
                          --clean-all    清理上述内容加 node_modules 与 bin/obj
                          --bootstrap    自动装配依赖（下载 hugo/zola、构建 Flint AOT、
                                         npm/pip/gem 安装），已就绪的步骤自动跳过
                          --skip-gen     跳过语料生成
                          --regen        强制重建语料
                          --root PATH    矩阵目录（默认 harness 的上级）
                          --flint PATH / --hugo PATH / --zola PATH / --node PATH / --python PATH
                                         显式指定可执行文件（默认找 benchmarks/tools 与 PATH）
                        """);
                    Environment.Exit(0);
                    break;
                default:
                    Console.Error.WriteLine($"未知参数: {args[i]}（--help 查看用法）");
                    Environment.Exit(2);
                    break;
            }
        }

        return c;
    }
}
