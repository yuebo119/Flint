// SSG 十一引擎万页矩阵复现器（口径说明见 ../README.md）
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
if (opt.Fast)
{
    opt.Warmup = 0;
    opt.Adaptive = true;
}
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

var corpora = MatrixCatalog.Corpora;

// --list 只查就绪状态，不为它生成全量语料（冷启动自查应当秒回）
if (!opt.SkipGen && !opt.List)
{
    Console.WriteLine("\n--- 语料 ---");
    foreach (var c in corpora)
    {
        if (opt.Only is not null && !opt.Only.Contains(c.Arm)) continue;
        Corpus.Ensure(Path.Combine(root, c.Dir), opt.Pages, c.Kind, c.Layout, opt.Regen);
    }
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

var builds = 0;
var runStartedUtc = DateTime.UtcNow;
var total = Stopwatch.StartNew();
var warmupElapsed = TimeSpan.Zero;
Preflight.Snapshot? snap = null;
var gateFailures = new List<string>();

if (opt.WaitQuiet > 0)
{
    var deadline = DateTime.UtcNow.AddMinutes(opt.WaitQuiet);
    var quietStreak = 0;
    Console.WriteLine($"\n--- 等待安静窗口（最多 {opt.WaitQuiet} 分钟，需连续 3 次采样低于 {opt.MaxLoad}%）---");
    while (DateTime.UtcNow < deadline)
    {
        var l = Preflight.CpuPercent(Environment.ProcessorCount, 1000);
        quietStreak = l <= opt.MaxLoad ? quietStreak + 1 : 0;
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] CPU {l:F1}%  连续安静 {quietStreak}/3");
        if (quietStreak >= 3) { Console.WriteLine("窗口已安静，进入门禁检查。"); break; }
        Thread.Sleep(TimeSpan.FromSeconds(15));
    }

    if (quietStreak < 3 && !opt.Force)
    {
        Console.WriteLine($"等待 {opt.WaitQuiet} 分钟仍未出现安静窗口。稍后重试，或用 --force 在嘈杂环境跑（结果不可发布）。");
        return;
    }
}

if (opt.Preflight)
{
    var (s, failures) = Preflight.Check(root, ready);
    snap = s;
    gateFailures.AddRange(failures);

    Console.WriteLine($"\n--- 运行前检查 ---");
    Console.WriteLine($"机器: {s.Cores} 逻辑核 · 当前 CPU 占用 {s.LoadPercent:F1}%");
    Console.WriteLine($"语料: {s.CorpusFiles} 个文件 · 指纹 {s.CorpusHash}");
    foreach (var v in s.Versions) Console.WriteLine($"  {v}");

    if (s.LoadPercent > opt.MaxLoad)
        gateFailures.Add($"CPU 占用 {s.LoadPercent:F1}% 超过门限 {opt.MaxLoad}%");

    if (gateFailures.Count > 0)
    {
        Console.WriteLine("\n门禁未通过:");
        foreach (var f in gateFailures) Console.WriteLine($"  - {f}");
        if (!opt.Force)
        {
            Console.WriteLine("请关闭占用进程后重试，或用 --force 跳过（跳过后数字不可用于对外发布）。");
            return;
        }
        Console.WriteLine("已用 --force 继续，结果不可用于对外发布。");
    }
    else
    {
        Console.WriteLine("门禁通过。");
    }
}

if (opt.Warmup > 0)
{
    Console.WriteLine($"\n--- 预热（{opt.Warmup} 轮，不计入）---");
    var warmupSw = Stopwatch.StartNew();
    foreach (var a in ready)
    {
        var m = Run.Once(a);
        builds++;
        Console.WriteLine($"{a.Name,-14} {m.Ms,9:F0} ms（预热）");
    }
    warmupSw.Stop();
    warmupElapsed = warmupSw.Elapsed;
}

var times = ready.Select(_ => new List<double>()).ToArray();
var mems = ready.Select(_ => new List<long>()).ToArray();
var armStart = new DateTime[ready.Count];
var discarded = new bool[ready.Count];
var loads = new List<double>();
var buildElapsed = TimeSpan.Zero;
var deleteElapsed = TimeSpan.Zero;

for (var r = 0; r < opt.Rounds; r++)
{
    var roundLoad = Preflight.CpuPercent(Environment.ProcessorCount, 500);
    loads.Add(roundLoad);
    var order = Enumerable.Range(0, ready.Count).Select(i => (i + r * 3) % ready.Count).ToArray();
    Console.WriteLine($"\n--- 第 {r + 1}/{opt.Rounds} 轮（首臂 {ready[order[0]].Name}，{DateTime.Now:HH:mm:ss}，CPU 占用 {roundLoad:F1}%）---");
    foreach (var idx in order)
    {
        var m = Run.Once(ready[idx]);
        builds++;
        if (armStart[idx] == default) armStart[idx] = m.StartedUtc;
        times[idx].Add(m.Ms);
        mems[idx].Add(m.PeakRss);
        buildElapsed += TimeSpan.FromMilliseconds(m.Ms);
        deleteElapsed += TimeSpan.FromMilliseconds(m.DeleteMs);

        // 自适应：慢臂首轮按预热处理并丢弃（首轮受机器漂移偏慢，直接计入会抬高慢臂）
        var note = "";
        if (opt.Adaptive && r == 0 && opt.Rounds >= 3 && m.Ms > opt.SlowThresholdMs)
        {
            times[idx].Clear();
            mems[idx].Clear();
            discarded[idx] = true;
            note = "  （慢臂，首轮不计入）";
        }

        Console.WriteLine($"{ready[idx].Name,-14} {m.Ms,9:F0} ms   峰值 RSS {m.PeakRss / 1048576.0,7:F0} MB{note}");
    }
}

total.Stop();

var medians = times.Select(Stats.Median).ToArray();
var fastest = medians.Min();

Console.WriteLine($"\n=== {opt.Pages} 页冷构建（{opt.Rounds} 轮中位，外部计时含进程启动）===");
Console.WriteLine($"{"臂",-14} {"中位(ms)",10} {"vs最快",8} {"产出(MB)",10} {"html",7} {"正文抽样",9} {"零字节",7} {"新生成",7} {"峰值RSS(MB)",12}");
var audits = new Audit.Result[ready.Count];
for (var i = 0; i < ready.Count; i++)
{
    audits[i] = Audit.Scan(ready[i].Pub, armStart[i]);
    Console.WriteLine($"{ready[i].Name,-14} {medians[i],10:F0} {medians[i] / fastest,7:F2}x {audits[i].Bytes / 1048576.0,10:F1} {audits[i].Html,7} {audits[i].BodySample,9} {audits[i].Zero,7} {audits[i].Fresh,7} {mems[i].Max() / 1048576.0,11:F0}");
}

if (skipped.Count > 0)
{
    Console.WriteLine("\n--- 未运行（缺依赖）---");
    foreach (var a in skipped)
        Console.WriteLine($"{a.Name,-14} 缺: {string.Join(", ", a.Missing)}");
    Console.WriteLine("安装命令见 benchmarks/ssg-matrix/README.md");
}

var overhead = total.Elapsed - buildElapsed - deleteElapsed;
var elapsed = total.Elapsed.TotalSeconds;
string Pct(TimeSpan t) => elapsed > 0 ? $"{t.TotalSeconds / elapsed:P0}" : "0%";

Console.WriteLine("\n--- 用时构成 ---");
Console.WriteLine($"构建合计   {buildElapsed.TotalSeconds,7:F0}s  {Pct(buildElapsed),5}");
Console.WriteLine($"删除输出   {deleteElapsed.TotalSeconds,7:F0}s  {Pct(deleteElapsed),5}");
Console.WriteLine($"其他       {overhead.TotalSeconds,7:F0}s  {Pct(overhead),5}");
Console.WriteLine($"总用时     {elapsed,7:F0}s  共 {builds} 次构建（{ready.Count} 臂）");
if (warmupElapsed > TimeSpan.Zero)
    Console.WriteLine($"预热另计   {warmupElapsed.TotalSeconds,7:F0}s（含在上方总用时与构建次数里，不计入中位）");
if (discarded.Any(x => x))
    Console.WriteLine($"自适应：{discarded.Count(x => x)} 个慢臂首轮按预热丢弃，用后 {opt.Rounds - 1} 个样本取中位");

if (opt.Report)
{
    var path = Path.Combine(root, "results", $"run-{DateTime.Now:yyyyMMdd-HHmmss}.md");
    var timedBuilds = builds - (opt.Warmup > 0 ? ready.Count : 0);
    var written = Report.Write(path, opt, snap, gateFailures, ready, times, mems, audits, armStart, loads,
        builds, timedBuilds, buildElapsed.TotalSeconds, deleteElapsed.TotalSeconds, warmupElapsed.TotalSeconds, elapsed, runStartedUtc);
    Console.WriteLine($"\n报告已写入 {written}");
}

if (opt.CleanAfter)
{
    var freed = Cleaner.Run(root, includeDeps: false);
    Console.WriteLine($"清理完成：删除 {freed.Files} 个文件，释放约 {freed.Bytes / 1048576.0:F0} MB");
}

static string SourceDir([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;

// ---------------------------------------------------------------- 类型

// 统计单一实现：主流程与报告共用
static class Stats
{
    public static double Median(List<double> xs)
    {
        var s = xs.OrderBy(x => x).ToList();
        return s.Count == 0 ? 0 : s.Count % 2 == 1 ? s[s.Count / 2] : (s[s.Count / 2 - 1] + s[s.Count / 2]) / 2;
    }
}

sealed record CorpusSpec(string Arm, string Dir, string Kind, string? Layout);

// 语料清单单一事实源：生成（Corpus）、指纹（Preflight）、清理（Cleaner）共用，杜绝三处硬编码漂移
static class MatrixCatalog
{
    public static readonly CorpusSpec[] Corpora =
    {
        new("flint-scriban", "flint-scriban/content", "yaml", null),
        new("flint-liquid", "flint-liquid/content", "yaml", null),
        new("hugo", "hugo/content/posts", "yaml", null),
        new("astro", "astro/src/pages/posts", "yaml", "../../layouts/post.astro"),
        new("eleventy", "eleventy/src/posts", "yaml", "post.njk"),
        new("jekyll", "jekyll/_docs", "yaml", "default"),
        new("vitepress", "vitepress/docs/posts", "yaml", null),
        new("gatsby", "gatsby/content", "yaml", null),
        new("hexo", "hexo/source/_posts", "yaml", null),
        new("mkdocs", "mkdocs/docs", "yaml", null),
        new("zola", "zola/content/blog", "toml", null),
    };

    public static readonly string[] CorpusDirs = Corpora.Select(c => c.Dir).ToArray();
}

sealed record Arm(string Name, string Exe, string[] Args, string Cwd, string Pub, string[] Clean)
{
    public Dictionary<string, string>? Env { get; init; }
    public List<string> Missing { get; } = new();
    public bool Ready => Missing.Count == 0;
}

static class Cleaner
{
    // 语料目录只删生成的 page-*.md（目录内的 index.md / _index.md 是脚手架）
    static readonly string[] CorpusDirs = MatrixCatalog.CorpusDirs;

    // 产出与各引擎持久缓存：整目录删除
    static readonly string[] Outputs =
    {
        "_out",
        "zola/public", "eleventy/_site", "astro/dist", "hexo/public", "hexo/db.json",
        "astro/node_modules/.astro",
        "gatsby/public", "gatsby/.cache",
        "vitepress/.vitepress/dist", "vitepress/.vitepress/cache", "vitepress/.vitepress/.temp",
        "jekyll/.jekyll-cache", "jekyll/.jekyll-metadata",
        "hugo/.hugo_build.lock",
    };

    static readonly string[] Dependencies =
    {
        "eleventy/node_modules", "hexo/node_modules", "vitepress/node_modules",
        "astro/node_modules", "gatsby/node_modules",
        "harness/bin", "harness/obj",
    };

    public static (int Files, long Bytes) Run(string root, bool includeDeps)
    {
        var files = new List<string>();
        var dirs = new List<string>();

        // 语料：逐个删除 page-*.md
        foreach (var rel in CorpusDirs)
        {
            var dir = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
            if (Directory.Exists(dir))
                files.AddRange(Directory.EnumerateFiles(dir, "page-*.md", SearchOption.TopDirectoryOnly));
        }

        foreach (var rel in Outputs.Concat(includeDeps ? Dependencies : Array.Empty<string>()))
        {
            var path = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
            if (Directory.Exists(path)) dirs.Add(path);
            else if (File.Exists(path)) files.Add(path);
        }

        long bytes = 0;
        var count = 0;

        foreach (var dir in dirs)
        {
            if (!Paths.IsInside(dir)) throw new InvalidOperationException($"拒绝删除矩阵目录之外的路径: {dir}");
            var infos = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                .Select(f => new FileInfo(f)).ToList();
            var size = infos.Sum(f => f.Length);
            bytes += size;
            count += infos.Count;
            Console.WriteLine($"  删除 {Path.GetRelativePath(root, dir),-38} {size / 1048576.0,8:F1} MB  ({infos.Count} 文件)");
            Directory.Delete(dir, recursive: true);
        }

        foreach (var file in files)
        {
            if (!Paths.IsInside(file)) throw new InvalidOperationException($"拒绝删除矩阵目录之外的路径: {file}");
            try
            {
                bytes += new FileInfo(file).Length;
                File.Delete(file);
            }
            catch (FileNotFoundException) { /* 并发运行可能已先删除 */ }
            count += 1;
        }

        Console.WriteLine($"  删除生成语料 page-*.md {files.Count} 个文件");
        return (count, bytes);
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

        foreach (var name in new[] { "astro", "eleventy", "hexo", "gatsby", "vitepress" })
        {
            if (!Want(name)) continue;
            var site = Path.Combine(root, name);
            if (Directory.Exists(Path.Combine(site, "node_modules"))) { Skip($"{name}/node_modules 已存在"); continue; }
            var npm = Find("npm");
            if (npm is null) { Fail($"{name}: 未找到 npm"); continue; }
            Step($"{name}: npm install", () => Exec(npm, ["install", "--no-audit", "--no-fund"], site));
        }

        var python = Find("python") ?? Find("python3") ?? Find("py");
        foreach (var module in new[] { "mkdocs" })
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
        var dest = Path.Combine(tools, ToolLocator.Exe(name));
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

        // 显式环境变量优先，不做探测（用户设置即意图）
        if (string.IsNullOrWhiteSpace(proxy))
        {
            // 常见冷启动形态：git 配了代理、环境与系统代理都没配。代理可能已停，
            // 死代理会让下载失败或挂起，所以先探活再采用；探活失败回退直连
            var gitProxy = TryReadGitProxy();
            if (gitProxy is not null && ProxyAlive(gitProxy)) proxy = gitProxy;
        }

        if (!string.IsNullOrWhiteSpace(proxy)) handler.Proxy = new System.Net.WebProxy(proxy);
        return handler;
    }

    static string? TryReadGitProxy()
    {
        try
        {
            var psi = new ProcessStartInfo("git", "config --get http.proxy")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };
            using var p = Process.Start(psi);
            var line = p?.StandardOutput.ReadToEnd().Trim();
            p?.WaitForExit(2000);
            return string.IsNullOrWhiteSpace(line) ? null : line;
        }
        catch { return null; }
    }

    static bool ProxyAlive(string proxyUrl)
    {
        try
        {
            var uri = new Uri(proxyUrl);
            using var tcp = new System.Net.Sockets.TcpClient();
            var connect = tcp.ConnectAsync(uri.Host, uri.Port);
            connect.Wait(1500);
            return connect.IsCompleted && tcp.Connected;
        }
        catch { return false; }
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
        var dest = Path.Combine(tools, "flint-aot", ToolLocator.Exe("Flint"));
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
        if (!p.WaitForExit(10 * 60_000))
        {
            // npm 的孙进程（postinstall 遥测走坏代理等）会长期握住 stdout 管道，
            // 不设超时的一键装配会永久卡死；超时按装配失败处理，该引擎跳过
            try { p.Kill(entireProcessTree: true); } catch { /* 进程已退出 */ }
            return 124;
        }
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

    static string? Find(string name) => ToolLocator.Find(null, null, name);
}

static class Preflight
{
    public sealed record Snapshot(int Cores, double LoadPercent, string CorpusHash, int CorpusFiles, List<string> Versions);

    public static (Snapshot Snap, List<string> Failures) Check(string root, List<Arm> arms)
    {
        var cores = Environment.ProcessorCount;
        var load = CpuPercent(cores, 1000);

        var (hash, files) = CorpusHash(root);
        var versions = Versions(root);

        var failures = new List<string>();
        if (files == 0) failures.Add("语料尚未生成（先跑一次不带 --preflight 的运行）");
        for (var i = 0; i < arms.Count; i++)
        {
            if (arms[i].Ready) continue;
            failures.Add($"{arms[i].Name} 缺依赖: {string.Join(", ", arms[i].Missing)}");
        }

        return (new Snapshot(cores, load, hash, files, versions), failures);
    }

    public static double CpuPercent(int cores, int ms)
    {
        var before = new Dictionary<int, TimeSpan>();
        foreach (var p in Process.GetProcesses())
        {
            try { before[p.Id] = p.TotalProcessorTime; } catch { /* 权限不足的进程跳过 */ }
            p.Dispose();
        }

        Thread.Sleep(ms);

        double busyMs = 0;
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (before.TryGetValue(p.Id, out var t0))
                    busyMs += Math.Max(0, (p.TotalProcessorTime - t0).TotalMilliseconds);
            }
            catch { /* 同上 */ }
            p.Dispose();
        }

        return busyMs / (ms * (double)cores) * 100.0;
    }

    static (string Hash, int Files) CorpusHash(string root)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        using var stream = new MemoryStream();
        var count = 0;

        foreach (var rel in MatrixCatalog.CorpusDirs)
        {
            var dir = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(dir)) continue;

            var filesInDir = Directory.EnumerateFiles(dir, "page-*.md").OrderBy(x => x, StringComparer.Ordinal).ToArray();
            foreach (var f in filesInDir)
            {
                var name = Encoding.UTF8.GetBytes(Path.GetFileName(f) + "\n");
                stream.Write(name, 0, name.Length);
                using var fs = File.OpenRead(f);
                fs.CopyTo(stream);
                count++;
            }
        }

        var digest = sha.ComputeHash(stream.ToArray());
        return (Convert.ToHexString(digest)[..16].ToLowerInvariant(), count);
    }

    static List<string> Versions(string root)
    {
        var list = new List<string>();
        var tools = Path.GetFullPath(Path.Combine(root, "..", "tools"));

        void Probe(string label, string exe, string[] args, string cwd)
        {
            try
            {
                var psi = new ProcessStartInfo(exe)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    WorkingDirectory = cwd,
                };
                foreach (var a in args) psi.ArgumentList.Add(a);

                using var p = Process.Start(psi);
                if (p is null) { list.Add($"{label}: 不可用"); return; }
                if (!p.WaitForExit(5000)) { try { p.Kill(true); } catch { } list.Add($"{label}: 超时"); return; }
                var text = (p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd()).Trim();
                var first = text.Split('\n').FirstOrDefault()?.Trim() ?? "";
                list.Add($"{label}: {(first.Length > 90 ? first[..90] : first)}");
            }
            catch (Exception ex)
            {
                list.Add($"{label}: {ex.Message}");
            }
        }

        var hugo = Path.Combine(tools, OperatingSystem.IsWindows() ? "hugo.exe" : "hugo");
        var zola = Path.Combine(tools, OperatingSystem.IsWindows() ? "zola.exe" : "zola");
        var flint = Path.Combine(tools, "flint-aot", OperatingSystem.IsWindows() ? "Flint.exe" : "Flint");

        if (File.Exists(hugo)) Probe("hugo", hugo, ["version"], tools);
        if (File.Exists(zola)) Probe("zola", zola, ["--version"], tools);
        if (File.Exists(flint)) Probe("flint", flint, ["--version"], tools);
        Probe("node", "node", ["--version"], root);
        Probe("dotnet", "dotnet", ["--version"], root);

        return list;
    }
}

static class Report
{
    public static string Write(
        string path,
        Cli opt,
        Preflight.Snapshot? snap,
        List<string> failures,
        List<Arm> arms,
        List<double>[] times,
        List<long>[] mems,
        Audit.Result[] audits,
        DateTime[] armStart,
        List<double> loads,
        int builds,
        int timedBuilds,
        double buildSec,
        double deleteSec,
        double warmupSec,
        double totalSec,
        DateTime startedUtc)
    {
        var sb = new StringBuilder();
        var medians = times.Select(Stats.Median).ToArray();
        var fastest = medians.Min();

        sb.AppendLine("# SSG 万页矩阵性能测试报告");
        sb.AppendLine();
        sb.AppendLine($"- 生成时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}（运行开始 {startedUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}）");
        sb.AppendLine($"- 语料：每臂 {opt.Pages} 页；预热 {opt.Warmup} 轮；计时 {opt.Rounds} 轮取中位");
        sb.AppendLine($"- 自适应：{(opt.Adaptive ? $"开（慢臂阈值 {opt.SlowThresholdMs}ms）" : "关")}；构建次数 {builds}");
        if (snap is not null)
        {
            sb.AppendLine($"- 机器：{snap.Cores} 逻辑核，运行前 CPU 占用 {snap.LoadPercent:F1}%");
            sb.AppendLine($"- 语料指纹：{snap.CorpusHash}（{snap.CorpusFiles} 个文件）");
            if (loads.Count > 0)
                sb.AppendLine($"- 运行中每轮 CPU 占用：{loads.Min():F1}% 到 {loads.Max():F1}%");
            sb.AppendLine();
            sb.AppendLine("## 环境版本");
            sb.AppendLine();
            foreach (var v in snap.Versions) sb.AppendLine($"- {v}");
        }

        if (failures.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## 门禁告警");
            sb.AppendLine();
            foreach (var f in failures) sb.AppendLine($"- {f}");
        }

        sb.AppendLine();
        sb.AppendLine("## 结果");
        sb.AppendLine();
        sb.AppendLine("| 排名 | 引擎 | 中位(ms) | vs 最快 | 产出(MB) | html | 正文抽样 | 零字节 | 本次新生成 | 判定 | 峰值RSS(MB) |");
        sb.AppendLine("| --: | --- | --: | --: | --: | --: | --: | --: | --: | --- | --: |");

        var order = Enumerable.Range(0, arms.Count).OrderBy(i => medians[i]).ToArray();
        for (var r = 0; r < order.Length; r++)
        {
            var i = order[r];
            var a = audits[i];
            var ok = a.Html > 0 && a.Zero == 0 && a.Fresh > 0 && a.BodySample.Split('/')[0] != "0";
            sb.AppendLine($"| {r + 1} | {arms[i].Name} | {medians[i]:F0} | {medians[i] / fastest:F2}x | {a.Bytes / 1048576.0:F1} | {a.Html} | {a.BodySample} | {a.Zero} | {a.Fresh} | {(ok ? "通过" : "复核")} | {mems[i].Max() / 1048576.0:F0} |");
        }

        sb.AppendLine();
        sb.AppendLine("## 用时构成");
        sb.AppendLine();
        sb.AppendLine($"- 构建合计 {buildSec:F0}s，删除输出 {deleteSec:F0}s，总用时 {totalSec:F0}s（含全部构建，计时口径见下）");
        if (warmupSec > 0)
            sb.AppendLine($"- 其中预热 {warmupSec:F0}s（不计入中位样本）；「构建合计」仅计 {timedBuilds} 次计时构建");
        sb.AppendLine();
        sb.AppendLine("## 每臂各轮明细");
        sb.AppendLine();
        sb.AppendLine("| 引擎 | 各轮(ms) | 中位(ms) |");
        sb.AppendLine("| --- | --- | --: |");
        for (var i = 0; i < arms.Count; i++)
            sb.AppendLine($"| {arms[i].Name} | {string.Join(" / ", times[i].Select(t => t.ToString("F0")))} | {medians[i]:F0} |");
        sb.AppendLine();
        sb.AppendLine("## 产物校验说明");
        sb.AppendLine();
        sb.AppendLine("- 「本次新生成」统计输出目录中修改时间晚于本轮运行开始的文件数，用来排除沿用了上次运行残留产物的情况");
        sb.AppendLine("- 「正文抽样」抽取至多 100 个页面，检查语料尾句是否出现在产物中，用来排除只产出空壳页面的情况");
        sb.AppendLine("- 「零字节」为输出目录中 0 字节 html 的数量");
        sb.AppendLine("- 判定「复核」表示上述任一项不达标，该臂数字不可直接采用");

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        return path;
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

// 可执行文件定位单一事实源：Arms/Setup/Preflight 共用（原三处各有一份 PATH 搜索与 exe 名逻辑）
static class ToolLocator
{
    public static string? Find(string? explicitPath, string? preferred, string name)
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

    public static string Exe(string name) => OperatingSystem.IsWindows() ? name + ".exe" : name;
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
    public sealed record Result(int Html, int Zero, string BodySample, int Fresh, long Bytes);

    public static Result Scan(string pub, DateTime notBeforeUtc = default)
    {
        if (!Directory.Exists(pub)) return new Result(0, 0, "0/0", 0, 0);

        var files = Directory.EnumerateFiles(pub, "*.html", SearchOption.AllDirectories).ToArray();
        var zero = files.Count(f => new FileInfo(f).Length == 0);
        if (files.Length == 0) return new Result(0, zero, "0/0", 0, 0);

        var cutoff = notBeforeUtc == default ? DateTime.MinValue : notBeforeUtc.AddSeconds(-2);
        var fresh = 0;
        long bytes = 0;
        foreach (var f in files)
        {
            var info = new FileInfo(f);
            bytes += info.Length;
            if (info.LastWriteTimeUtc >= cutoff) fresh++;
        }

        var take = Math.Min(100, files.Length);
        var step = Math.Max(1, files.Length / take);
        var sample = new List<string>(take);
        for (var i = 0; i < files.Length && sample.Count < take; i += step) sample.Add(files[i]);

        var hit = sample.Count(HasBody);
        return new Result(files.Length, zero, $"{hit}/{sample.Count}", fresh, bytes);
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
    public sealed record Measured(double Ms, long PeakRss, double DeleteMs, DateTime StartedUtc);

    public static Measured Once(Arm a)
    {
        var deleteSw = Stopwatch.StartNew();
        foreach (var p in a.Clean.Append(a.Pub)) Delete(p);
        deleteSw.Stop();
        var startedUtc = DateTime.UtcNow;

        var psi = new ProcessStartInfo(a.Exe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = a.Cwd,
        };
        if (a.Env is not null)
            foreach (var kv in a.Env) psi.EnvironmentVariables[kv.Key] = kv.Value;
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

        return new Measured(sw.Elapsed.TotalMilliseconds, rss, deleteSw.Elapsed.TotalMilliseconds, startedUtc);
    }

    static string Tail(string s) => s.Length <= 1500 ? s : s[^1500..];

    static void Delete(string path)
    {
        if (!Directory.Exists(path) && !File.Exists(path)) return;
        if (!Paths.IsInside(path)) throw new InvalidOperationException($"拒绝删除矩阵目录之外的路径: {path}");

        if (File.Exists(path))
        {
            File.Delete(path);
            return;
        }

        // 并行删文件再删空目录树：Directory.Delete(recursive) 逐文件串行，
        // 万页产物 1 万+ 文件时删除占全矩阵总用时 16%。
        // 2026-10-31 反向验证：串行/并行删除对紧随其后的 hexo/flint 构建计时无影响
        //（hexo 串行 44.2s vs 并行 35.9s 中位，方向相反），非污染源，因子保留。
        var files = Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).ToArray();
        if (files.Length > 500)
        {
            Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = Math.Min(16, Environment.ProcessorCount) },
                f => { try { File.Delete(f); } catch (FileNotFoundException) { } catch (DirectoryNotFoundException) { } });
        }
        else
        {
            foreach (var f in files) File.Delete(f);
        }

        Directory.Delete(path, true);
    }
}

static class Arms
{
    public static List<Arm> Build(string root, string tools, Cli opt)
    {
        var list = new List<Arm>();

        var flint = ToolLocator.Find(opt.Flint, Path.Combine(tools, "flint-aot", Exe("Flint")), "flint");
        var hugo = ToolLocator.Find(opt.Hugo, Path.Combine(tools, Exe("hugo")), "hugo");
        var zola = ToolLocator.Find(opt.Zola, Path.Combine(tools, Exe("zola")), "zola");
        var node = ToolLocator.Find(opt.Node, null, "node");
        var python = opt.Python ?? DefaultPython();
        var jekyll = ToolLocator.Find(null, null, "jekyll");

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
            astro, Path.Combine(astro, "dist"), [Path.Combine(astro, "node_modules", ".astro")], "node");
        RequireDir(list, "astro", Path.Combine(astro, "node_modules", "astro"), "astro/node_modules（npm install）");

        var eleventy = P("eleventy");
        Add("eleventy", node, [Path.Combine(eleventy, "node_modules", "@11ty", "eleventy", "cmd.cjs"), "--quiet"],
            eleventy, Path.Combine(eleventy, "_site"), [], "node");
        RequireDir(list, "eleventy", Path.Combine(eleventy, "node_modules", "@11ty", "eleventy"), "eleventy/node_modules（npm install）");

        var hexo = P("hexo");
        Add("hexo", node, ["run-hexo.js"], hexo, Path.Combine(hexo, "public"),
            [Path.Combine(hexo, "db.json")], "node");
        RequireDir(list, "hexo", Path.Combine(hexo, "node_modules", "hexo"), "hexo/node_modules（npm install）");

        var gatsby = P("gatsby");
        // GATSBY_CPU_COUNT：默认（核数-1）在 32 核机上 worker 抖动，实测 4 最快（81.7s→64.2s）；
        // GATSBY_TELEMETRY_DISABLED 关闭构建期遥测网络调用
        Add("gatsby", node, [Path.Combine(gatsby, "node_modules", "gatsby", "dist", "bin", "gatsby.js"), "build"],
            gatsby, Path.Combine(gatsby, "public"), [Path.Combine(gatsby, ".cache")], "node",
            new Dictionary<string, string> { ["GATSBY_CPU_COUNT"] = "4", ["GATSBY_TELEMETRY_DISABLED"] = "1" });
        RequireDir(list, "gatsby", Path.Combine(gatsby, "node_modules", "gatsby"), "gatsby/node_modules（npm install）");

        var vitepress = P("vitepress");
        Add("vitepress", node, [Path.Combine(vitepress, "node_modules", "vitepress", "bin", "vitepress.js"), "build"],
            vitepress, Path.Combine(vitepress, ".vitepress", "dist"),
            [Path.Combine(vitepress, ".vitepress", "cache"), Path.Combine(vitepress, ".vitepress", ".temp")], "node");
        RequireDir(list, "vitepress", Path.Combine(vitepress, "node_modules", "vitepress"), "vitepress/node_modules（npm install）");


        // --- Python 系 ---

        var mkdocs = P("mkdocs");
        Add("mkdocs", python, ["-m", "mkdocs", "build", "-d", Path.Combine(outRoot, "mkdocs")],
            mkdocs, Path.Combine(outRoot, "mkdocs"), [], "python（--python）");

        // --- Ruby 系：用 ruby -S jekyll 直调，避免 cmd.exe 包装器污染内存采样 ---
        var jekyllSite = P("jekyll");
        var jekyllOut = Path.Combine(outRoot, "jekyll");
        var jekyllClean = new[] { Path.Combine(jekyllSite, ".jekyll-cache"), Path.Combine(jekyllSite, ".jekyll-metadata") };
        var ruby = ToolLocator.Find(null, null, "ruby");
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

        return list;

        void Add(string name, string? exe, string[] a, string cwd, string pub, string[] clean, string need,
            Dictionary<string, string>? env = null)
        {
            var arm = new Arm(name, exe ?? "dotnet", a, cwd, pub, clean) { Env = env };
            if (exe is null) arm.Missing.Add(need);
            list.Add(arm);
        }

        static void RequireDir(List<Arm> arms, string name, string dir, string hint)
        {
            if (Directory.Exists(dir)) return;
            arms.First(a => a.Name == name).Missing.Add(hint);
        }
    }


    static string Exe(string name) => ToolLocator.Exe(name);

    static string? DefaultPython()
    {
        // 优先真实解释器（python.exe 直接托管引擎进程）；py.exe 是启动器，
        // 内存采样会落在启动器上而不是引擎进程，只在必须时兜底
        return ToolLocator.Find(null, null, "python")
            ?? ToolLocator.Find(null, null, "python3")
            ?? ToolLocator.Find(null, null, "py");
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
    public bool Fast;
    public bool Adaptive;
    public bool Preflight;
    public bool Force;
    public bool Report;
    public bool CleanAfter;
    public int WaitQuiet;
    public int MaxLoad = 15;
    public int SlowThresholdMs = 60000;
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
                case "--fast": c.Fast = true; break;
                case "--adaptive": c.Adaptive = true; break;
                case "--slow-threshold": c.SlowThresholdMs = int.Parse(args[++i]); break;
                case "--preflight": c.Preflight = true; break;
                case "--force": c.Force = true; break;
                case "--max-load": c.MaxLoad = int.Parse(args[++i]); break;
                case "--report": c.Report = true; break;
                case "--clean-after": c.CleanAfter = true; break;
                case "--wait-quiet": c.WaitQuiet = int.Parse(args[++i]); break;
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
                          --fast         提速模式 = 关闭预热 + 自适应轮数（慢臂少跑一轮）
                          --adaptive     仅开自适应轮数（首轮超过 --slow-threshold 的臂减一轮）
                          --slow-threshold N  自适应阈值，毫秒（默认 60000）
                          --preflight    运行前检查：机器负载、语料指纹、版本、各臂就绪
                          --max-load N   负载门限，百分比（默认 15）
                          --force        门禁未通过时仍然运行（结果不可对外发布）
                          --report       运行结束后写 markdown 报告到 results/
                          --clean-after  报告写完后清理语料与产物
                          --wait-quiet N 等待安静窗口，最多 N 分钟（连续 3 次采样低于 --max-load）
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
