// demo 子命令组：演示站编排（demo-sites.sh / build-gallery.sh / demo-stop.sh 的 C# 移植）
// - demo build   ：21 主题建站（统一语料 + 主题迁移 + 构建 + 端口表）
// - demo gallery ：画廊案例站（gallery-source → gallery，只覆盖可再生部分）
// - demo stop    ：停掉单进程 demo-serve 与 84xx 端口残留服务
//
// 差异说明（相对 bash 版）：画廊服务同样走 C# demo-serve（bash 版 gallery 分支还留着
// python http.server 一行，与 demo-sites.sh 的单进程服务形态不一致，此处统一）。

using System.CommandLine;
using System.Diagnostics;
using System.Text;

namespace Flint.DevTools.Commands;

/// <summary>
/// demo 子命令组
/// </summary>
internal static class DemoCommand
{
    internal static Command Build()
    {
        var cmd = new Command("demo", "演示站编排（build/gallery/stop）");
        cmd.Subcommands.Add(BuildDemoBuild());
        cmd.Subcommands.Add(BuildDemoGallery());
        cmd.Subcommands.Add(BuildDemoStop());
        return cmd;
    }

    private static string DemoSitesDir => Path.Combine(RepoPaths.RepoRoot, "demo-sites");

    private static string ThemesDir => Path.Combine(RepoPaths.RepoRoot, "theme-migrator", "themes");

    private static string FixturesCorpus => Path.Combine(DemoSitesDir, "fixtures", "corpus");

    private static string MigratorExe => Path.Combine(
        RepoPaths.RepoRoot, "src", "Flint.ThemeMigrator", "bin", "Debug", "net10.0", "Flint.ThemeMigrator.exe");

    private static string FlintExe => Path.Combine(
        RepoPaths.RepoRoot, "src", "Flint.Cli", "bin", "Release", "net10.0", "win-x64", "Flint.exe");

    private static string DemoServeExe => Path.Combine(
        DemoSitesDir, "demo-serve", "bin", "Release", "net10.0", "demo-serve.exe");

    private const int PortBase = 8401;
    private const int GalleryPort = 8400;

    // ---------------------------------------------------------------- demo build

    private static Command BuildDemoBuild()
    {
        var themesArg = new Argument<string[]>("themes") { Description = "主题名；缺省全部 21 个" };
        themesArg.Arity = ArgumentArity.ZeroOrMore;
        var serveOpt = new Option<bool>("--serve") { Description = "构建后按端口表启动单进程服务" };

        var cmd = new Command("build", "全部主题演示站：建站 + 迁移 + 构建");
        cmd.Arguments.Add(themesArg);
        cmd.Options.Add(serveOpt);

        cmd.SetAction(parseResult =>
        {
            var requested = parseResult.GetValue(themesArg) ?? Array.Empty<string>();
            var serve = parseResult.GetValue(serveOpt);
            var names = requested.Length > 0 ? requested : ThemeCommand.MatrixThemes;

            // 端口按主题在总表中的固定位置分配：子集运行与全量运行端口一致
            var portOf = new Dictionary<string, int>(StringComparer.Ordinal);
            var port = PortBase;
            foreach (var t in ThemeCommand.MatrixThemes)
            {
                portOf[t] = port++;
            }

            Console.WriteLine(
                $"{PyCompat.PadRightBytes("主题", 14)} {PyCompat.PadRightBytes("构建", 8)} " +
                $"{PyCompat.PadRightBytes("页数", 6)} {PyCompat.PadRightBytes("最小页", 8)} URL");
            Console.WriteLine(new string('-', 74));

            var fail = 0;
            foreach (var name in names)
            {
                var src = Path.Combine(ThemesDir, name);
                if (!Directory.Exists(Path.Combine(src, "layouts")))
                {
                    Console.WriteLine($"{PyCompat.PadRightBytes(name, 14)} 跳过（主题不存在）");
                    continue;
                }

                var themePort = portOf.TryGetValue(name, out var p) ? p : port++;
                var site = Path.Combine(DemoSitesDir, name);
                CleanDir(site);
                Directory.CreateDirectory(site);

                // 统一内容集：corpus 的 content/ 与 static/ 分别并入站点
                CopyTree(Path.Combine(FixturesCorpus, "content"), Path.Combine(site, "content"));
                var staticDir = Path.Combine(site, "static");
                Directory.CreateDirectory(staticDir);
                CopyTree(Path.Combine(FixturesCorpus, "static"), staticDir);

                WriteDemoConfig(name, site, themePort);

                // 迁移主题 → 站点 themes/
                var mig = Bench.ProcessRunner.RunTimedAsync(
                    MigratorExe, new[] { src, Path.Combine(site, "themes", name) }).GetAwaiter().GetResult();
                if (mig.ExitCode != 0 || !Directory.Exists(Path.Combine(site, "themes", name)))
                {
                    Console.WriteLine($"{name,-14} 迁移失败");
                    fail++;
                    continue;
                }

                // 构建：单站 600s 上限（fixit 在 100 篇语料 + 全分类/标签分页下需 ~500s）
                var build = Bench.ProcessRunner.RunTimedAsync(
                    FlintExe,
                    FlintBuildArgs,
                    workingDirectory: site).GetAwaiter().GetResult();

                var publicDir = Path.Combine(site, "public");
                var pages = CountHtml(publicDir);
                var minSize = MinHtmlSize(publicDir);
                var url = $"http://127.0.0.1:{themePort}/";

                if (build.ExitCode == 0 && pages > 0)
                {
                    var minText = minSize == 0 ? "NA" : minSize.ToString();
                    Console.WriteLine(
                        $"{PyCompat.PadRightBytes(name, 14)} {PyCompat.PadRightBytes("通过", 8)} " +
                        $"{PyCompat.PadRightBytes(pages.ToString(), 6)} {PyCompat.PadRightBytes(minText, 8)} {url}");
                }
                else
                {
                    var minText = minSize == 0 ? "NA" : minSize.ToString();
                    Console.WriteLine(
                        $"{PyCompat.PadRightBytes(name, 14)} {PyCompat.PadRightBytes("失败(" + build.ExitCode + ")", 8)} " +
                        $"{PyCompat.PadRightBytes(pages.ToString(), 6)} {PyCompat.PadRightBytes(minText, 8)}");
                    var output = build.Stdout + build.Stderr;
                    foreach (var line in output.Split('\n').TakeLast(3))
                    {
                        Console.WriteLine($"    {line}");
                    }

                    fail++;
                }
            }

            Console.WriteLine(new string('-', 74));

            if (serve)
            {
                StartServe(portOf, names);
            }

            return fail > 0 ? 1 : 0;
        });

        return cmd;
    }

    private static void WriteDemoConfig(string name, string site, int port)
    {
        var sb = new StringBuilder();
        sb.Append($"baseURL = \"http://127.0.0.1:{port}/\"\n");
        sb.Append("languageCode = \"zh-cn\"\n");
        sb.Append($"title = \"{name} 演示站\"\n");
        sb.Append($"theme = \"{name}\"\n");
        sb.Append("paginate = 5\n");
        sb.Append("enableRobotsTXT = true\n\n");
        sb.Append("[pagination]\n");
        sb.Append("pagerSize = 5\n\n");
        sb.Append("[params]\n");
        sb.Append($"description = \"Flint 演示站点（{name} 主题）\"\n");

        // 主题专属 params（与 theme-matrix20 同源；含在 [params] 段内追加）
        sb.Append(name switch
        {
            "hugo-book" => "BookSection = \"posts\"\nBookTheme = \"light\"\nBookDateFormat = \"January 2, 2006\"\nBookComments = false\nBookSearch = false\n",
            "blog-awesome" => "Author.name = \"演示作者\"\nAuthor.avatar = \"icons/android-chrome-192x192.png\"\n",
            "loveit" or "fixit" => $"Author.name = \"演示作者\"\nAuthor.link = \"http://127.0.0.1:{port}/\"\nhome.profile.enable = true\n",
            "even" => "Author.name = \"演示作者\"\nversion = \"4.x\"\narchivePaginate = 50\nshowArchiveCount = false\n",
            "papermod" => "author = \"演示作者\"\nhomeInfoParams.Title = \"演示站点\"\nhomeInfoParams.Content = \"Flint 多主题演示\"\n",
            "stack" => "mainSections = [\"posts\"]\nsidebar.emoji = \"cat\"\nsidebar.subtitle = \"演示\"\n[[widgets.homepage]]\ntype = \"search\"\n[[widgets.homepage]]\ntype = \"archives\"\n[[widgets.page]]\ntype = \"toc\"\n",
            "blowfish" => "Author.name = \"演示作者\"\nAuthor.email = \"demo@example.com\"\n[params.homepage]\nshowRecent = true\nshowRecentItems = 5\n",
            "github-style" => "headerIcon = \"/images/github-mark.png\"\n",
            "clarity" => "Author.name = \"演示作者\"\nAuthor.email = \"demo@example.com\"\n",
            "ananke" => "author = \"演示作者\"\nananke.show_recent_posts = true\n",
            "congo" or "relearn" or "hextra" or "jane" or "mainroad" or "terminal" or "archie"
                or "hermit" or "xmin" or "bearblog" or "console" or "risotto" or "hugo-coder" or "hugo-paper"
                or "techdoc" or "m10c" or "monochrome" or "narrow" or "yinyang"
                => "author = \"演示作者\"\n",
            _ => string.Empty,
        });

        if (name == "yinyang")
        {
            sb.Append("headTitle = \"演示站点\"\nmainSections = [\"posts\"]\n");
        }

        // menus 收尾（TOML 表头后不能追加点号键）
        sb.Append("\n[menus]\n");
        sb.Append("[[menus.main]]\nname = \"首页\"\nurl = \"/\"\nweight = 1\n");
        sb.Append("[[menus.main]]\nname = \"归档\"\nurl = \"/posts/\"\nweight = 2\n");
        sb.Append("[[menus.main]]\nname = \"关于\"\nurl = \"/about/\"\nweight = 3\n");

        WriteAllText(Path.Combine(site, "hugo.toml"), sb.ToString());
        File.Copy(Path.Combine(site, "hugo.toml"), Path.Combine(site, "Flint.toml"), overwrite: true);
    }

    private static void StartServe(Dictionary<string, int> portOf, string[] names)
    {
        Console.WriteLine("启动全部服务（单进程多端口）…");

        // 先停旧服务：Windows 下进程 CWD 在 public/ 内会锁目录，且重复启动残留僵孤进程
        StopServe();

        // 映射走命令行参数（不用临时文件：后台进程可能还没读文件就被删）
        var serveArgs = new List<string> { $"{GalleryPort}={Path.Combine(DemoSitesDir, "gallery", "public")}" };
        foreach (var name in names)
        {
            var publicDir = Path.Combine(DemoSitesDir, name, "public");
            if (Directory.Exists(publicDir) && portOf.TryGetValue(name, out var port))
            {
                serveArgs.Add($"{port}={publicDir}");
            }
        }

        // 直启已构建 apphost（dotnet run 会多挂一个宿主父进程，直启单进程 ~22MB）
        var build = Bench.ProcessRunner.RunTimedAsync(
            "dotnet",
            new[] { "build", Path.Combine(DemoSitesDir, "demo-serve"), "-c", "Release", "-v", "q", "--nologo" },
            workingDirectory: RepoPaths.RepoRoot).GetAwaiter().GetResult();
        if (build.ExitCode != 0)
        {
            Console.Error.WriteLine("demo-serve 构建失败，服务未启动");
            return;
        }

        var psi = new ProcessStartInfo(DemoServeExe)
        {
            UseShellExecute = false,
            WorkingDirectory = RepoPaths.RepoRoot,
        };
        foreach (var a in serveArgs)
        {
            psi.ArgumentList.Add(a);
        }

        Process.Start(psi);
        Console.WriteLine($"全部服务已在单进程内启动（画廊 http://127.0.0.1:{GalleryPort}/）。");
    }

    // ---------------------------------------------------------------- demo gallery

    private static readonly string[] GalleryRegenerableDirs = { "public", "layouts", "content" };

    private static readonly string[] GalleryConfigFiles = { "hugo.toml", "Flint.toml" };

    private static readonly string[] FlintBuildArgs = { "build", "-s", ".", "-o", "public", "--clean" };

    private static readonly string[] NetstatArgs = { "-ano" };

    private static Command BuildDemoGallery()
    {
        var serveOpt = new Option<bool>("--serve") { Description = "构建后在 8400 端口启动服务" };
        var cmd = new Command("gallery", "画廊案例站：gallery-source → gallery 构建");
        cmd.Options.Add(serveOpt);

        cmd.SetAction(parseResult =>
        {
            var serve = parseResult.GetValue(serveOpt);
            var flint = FlintExe;
            if (!File.Exists(flint))
            {
                Console.Error.WriteLine($"未找到 Flint.exe: {flint}");
                return 1;
            }

            var gallerySrc = Path.Combine(DemoSitesDir, "gallery-source");
            var site = Path.Combine(DemoSitesDir, "gallery");

            // 只清理可再生部分（public/layouts/content/配置）；static/shots/ 下的主题预览图
            // 由截图流程生成（先截图后构建），必须保留——整站 rm -rf 会把 21 张预览图一起删掉
            foreach (var sub in GalleryRegenerableDirs)
            {
                CleanDir(Path.Combine(site, sub));
            }

            foreach (var f in GalleryConfigFiles)
            {
                var path = Path.Combine(site, f);
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }

            Directory.CreateDirectory(Path.Combine(site, "static", "shots"));
            File.Copy(Path.Combine(gallerySrc, "hugo.toml"), Path.Combine(site, "hugo.toml"), overwrite: true);
            CopyTree(Path.Combine(gallerySrc, "layouts"), Path.Combine(site, "layouts"));
            CopyTree(Path.Combine(gallerySrc, "content"), Path.Combine(site, "content"));

            var build = Bench.ProcessRunner.RunTimedAsync(
                flint,
                FlintBuildArgs,
                workingDirectory: site).GetAwaiter().GetResult();

            // 与 bash 版一致：画廊构建透传 Flint 输出（demo-sites 只在失败时打印）
            if (build.Stdout.Length > 0)
            {
                Console.WriteLine(build.Stdout);
            }

            var publicDir = Path.Combine(site, "public");
            var pages = CountHtml(publicDir);
            var shots = Directory.Exists(Path.Combine(site, "static", "shots"))
                ? Directory.EnumerateFiles(Path.Combine(site, "static", "shots"), "*.png").Count()
                : 0;
            Console.WriteLine($"画廊构建: exit={build.ExitCode} 页数={pages} 预览图={shots}");

            if (build.ExitCode != 0 || pages == 0)
            {
                Console.Error.WriteLine("画廊构建失败");
                return 1;
            }

            if (serve)
            {
                StartServe(new Dictionary<string, int>(StringComparer.Ordinal), Array.Empty<string>());
            }

            return 0;
        });

        return cmd;
    }

    // ---------------------------------------------------------------- demo stop

    private static Command BuildDemoStop()
    {
        var cmd = new Command("stop", "停掉演示站服务（demo-serve 单进程 + 84xx 端口残留）");

        cmd.SetAction(_ =>
        {
            StopServe();
            Console.WriteLine("演示站服务已停止。");
            return 0;
        });

        return cmd;
    }

    private static void StopServe()
    {
        // 单进程服务：按进程名杀（demo-serve 是 .NET 自建服务，进程名唯一）
        foreach (var processName in new[] { "demo-serve", "demo-serve.exe" })
        {
            foreach (var p in Process.GetProcessesByName(processName))
            {
                try
                {
                    p.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // 进程已在退出流程中
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    // 无权限杀掉（已退出的极少见）
                }

                p.Dispose();
            }
        }

        // 兜底：按 84xx 监听端口反查 PID（netstat 输出解析，与 bash 版同源）
        try
        {
            var netstat = Bench.ProcessRunner.RunTimedAsync(
                "netstat", NetstatArgs).GetAwaiter().GetResult();
            foreach (var line in netstat.Stdout.Split('\n'))
            {
                if (!line.Contains(":84", StringComparison.Ordinal) || !line.Contains("LISTENING", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0 || !int.TryParse(parts[^1], out var pid))
                {
                    continue;
                }

                try
                {
                    Process.GetProcessById(pid).Kill(entireProcessTree: true);
                }
                catch (ArgumentException)
                {
                    // 端口已释放
                }
                catch (InvalidOperationException)
                {
                    // 进程已退出
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    // 无权限
                }
            }
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // netstat 不在 PATH（非 Windows 环境）：跳过兜底
        }
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
        if (!Directory.Exists(src))
        {
            return;
        }

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
    /// 普通删除失败时回退 Windows rmdir /s /q
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
            Bench.ProcessRunner.RunTimedAsync(
                "cmd", new[] { "/c", $"rmdir /s /q \"{Path.GetFullPath(dir)}\"" }).GetAwaiter().GetResult();
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
