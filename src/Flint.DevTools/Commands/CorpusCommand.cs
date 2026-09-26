// corpus 子命令组：MDN 语料转换（corpus-convert.py）、演示语料统计（count_chars.py）、
// 占位插图生成（gen_images.py）的 C# 移植。
// 图片产物与入库 SVG 逐字节对拍（含 Python float repr 显示格式的复刻）。

using System.CommandLine;
using System.Text;
using System.Text.RegularExpressions;

namespace Flint.DevTools.Commands;

/// <summary>
/// corpus 子命令组
/// </summary>
internal static class CorpusCommand
{
    private static readonly Regex CjkRegex = new(@"[\u4e00-\u9fff]", RegexOptions.Compiled);
    private static readonly Regex MacroLineRegex = new(@"^\s*\{\{[^}]*\}\}\s*$", RegexOptions.Compiled);
    private static readonly Regex InlineMacroRegex = new(@"\{\{[^}]*\}\}", RegexOptions.Compiled);
    private static readonly Regex HeadingRegex = new(@"^# .*$", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly string[] Engines = { "hugo", "flint" };

    internal static Command Build()
    {
        var cmd = new Command("corpus", "语料工具（替代 scripts/corpus-convert.py 与 demo-sites/fixtures/corpus/*.py）");
        cmd.Subcommands.Add(BuildConvert());
        cmd.Subcommands.Add(BuildFixtures());
        return cmd;
    }

    // ---------------------------------------------------------------- convert

    private static Command BuildConvert()
    {
        var cmd = new Command("convert", "MDN 语料转 Flint/Hugo 同构站点（--out 默认 benchmarks/corpus/corpus-merged）");

        var mdnOpt = new Option<string>("--mdn")
        {
            Description = "MDN 内容根（其下按 files/en-us 寻页）",
            DefaultValueFactory = _ => Path.Combine(RepoPaths.RepoRoot, "benchmarks", "corpus", "mdn-content"),
        };
        var outOpt = new Option<string>("--out")
        {
            Description = "输出根（生成 <out>/{hugo,flint}/）",
            DefaultValueFactory = _ => Path.Combine(RepoPaths.RepoRoot, "benchmarks", "corpus", "corpus-merged"),
        };
        cmd.Options.Add(mdnOpt);
        cmd.Options.Add(outOpt);

        cmd.SetAction(parseResult =>
        {
            var outRoot = Path.GetFullPath(parseResult.GetValue(outOpt)!);
            var mdnRoot = Path.GetFullPath(parseResult.GetValue(mdnOpt)!);

            GuardDeletableOutput(outRoot);

            if (Directory.Exists(outRoot))
            {
                Console.WriteLine($"删除并重建 {outRoot}");
                Directory.Delete(outRoot, recursive: true);
            }

            foreach (var engine in Engines)
            {
                var site = Path.Combine(outRoot, engine);
                var count = ConvertMdn(mdnRoot, site);
                WriteLayouts(site);
                Console.WriteLine($"{engine}: mdn={count}");
            }

            return 0;
        });

        return cmd;
    }

    // 删除前白名单守卫：仓库内只允许删 benchmarks/corpus 下（可再生语料），仓库外允许临时工作区
    private static void GuardDeletableOutput(string outRoot)
    {
        var repoRoot = Path.GetFullPath(RepoPaths.RepoRoot);
        if (!outRoot.StartsWith(repoRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            return;
        }

        var corpusRoot = Path.GetFullPath(Path.Combine(repoRoot, "benchmarks", "corpus"));
        var allowed = outRoot.Equals(corpusRoot, StringComparison.OrdinalIgnoreCase)
            || outRoot.StartsWith(corpusRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        if (!allowed)
        {
            throw new InvalidOperationException($"拒绝删除：仓库内 --out 必须在 benchmarks/corpus 下（当前 {outRoot}）");
        }
    }

    private static int ConvertMdn(string mdnRoot, string site)
    {
        var src = Path.Combine(mdnRoot, "files", "en-us");
        var targetDir = Path.Combine(site, "content", "mdn");
        Directory.CreateDirectory(targetDir);

        if (!Directory.Exists(src))
        {
            return 0;
        }

        var count = 0;
        foreach (var dir in Directory.EnumerateDirectories(src, "*", SearchOption.AllDirectories))
        {
            var srcFile = Path.Combine(dir, "index.md");
            if (!File.Exists(srcFile))
            {
                continue;
            }

            var rel = Path.GetRelativePath(src, dir).Replace('\\', '/');
            if (rel == ".")
            {
                continue; // MDN 根页面跳过
            }

            // 扁平化命名：web/api/gamepad → mdn-web-api-gamepad.md（目录与同名页面文件在
            // Flint 树中 key 冲突 BUILD001；Hugo 虽容忍但为两引擎同构统一扁平化）
            var fname = "mdn-" + rel.Replace("/", "-") + ".md";
            var raw = File.ReadAllText(srcFile);
            var cleaned = StripMacros(raw);

            // 剥离 MDN 原文件自带的 front matter 块（避免与生成 FM 双重嵌套）
            if (cleaned.StartsWith("---", StringComparison.Ordinal))
            {
                var end = cleaned.IndexOf("\n---", 3, StringComparison.Ordinal);
                if (end >= 0)
                {
                    var next = cleaned.IndexOf('\n', end + 1);
                    cleaned = next >= 0 ? cleaned[next..].TrimStart('\n') : string.Empty;
                }
            }

            var title = ExtractTitle(cleaned);
            var body = HeadingRegex.Replace(cleaned, string.Empty, count: 1).Trim();

            File.WriteAllText(
                Path.Combine(targetDir, fname),
                $"---\ntitle: \"{title}\"\n---\n\n{body}\n",
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            count++;
        }

        return count;
    }

    private static string StripMacros(string text)
    {
        var kept = new StringBuilder();
        foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
        {
            if (MacroLineRegex.IsMatch(line))
            {
                continue;
            }

            kept.Append(InlineMacroRegex.Replace(line, string.Empty)).Append('\n');
        }

        // Python 版为 "\n".join(kept)：末行也会带回车换行差异；去掉 join 产生的尾换行保持等价
        return kept.Length > 0 ? kept.ToString(0, kept.Length - 1) : string.Empty;
    }

    private static string ExtractTitle(string text)
    {
        foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
        {
            if (line.StartsWith("# ", StringComparison.Ordinal))
            {
                return line[2..].Trim().Trim('"');
            }
        }

        return "Untitled";
    }

    private static void WriteLayouts(string site)
    {
        // 与 Python 版行为一致：layouts/_default 两个引擎分支都创建（hugo 用，flint 为空目录）
        Directory.CreateDirectory(Path.Combine(site, "layouts"));
        Directory.CreateDirectory(Path.Combine(site, "layouts", "_default"));
        var isHugo = Path.GetFileName(site.TrimEnd(Path.DirectorySeparatorChar)) == "hugo";

        if (isHugo)
        {
            WriteAllTextUtf8(Path.Combine(site, "hugo.toml"),
                "baseURL = \"http://localhost:1313/\"\ntitle = \"Corpus\"\ndisableKinds = [\"taxonomy\", \"term\", \"RSS\", \"sitemap\"]\n");
            WriteAllTextUtf8(Path.Combine(site, "layouts", "_default", "single.html"),
                "<article><h1>{{ .Title }}</h1>{{ .Content }}</article>");
            WriteAllTextUtf8(Path.Combine(site, "layouts", "index.html"), "<h1>Home</h1>");
            WriteAllTextUtf8(Path.Combine(site, "layouts", "_default", "list.html"),
                "<ul>{{ range .Pages }}<li>{{ .Title }}</li>{{ end }}</ul>");
        }
        else
        {
            WriteAllTextUtf8(Path.Combine(site, "Flint.toml"),
                "baseURL = \"http://localhost:1313/\"\ntitle = \"Corpus\"\ndisableKinds = [\"taxonomy\", \"term\", \"rss\", \"sitemap\"]\n");
            WriteAllTextUtf8(Path.Combine(site, "layouts", "single.html"),
                "<article><h1>{{ page.title }}</h1>{{ page.content }}</article>");
            WriteAllTextUtf8(Path.Combine(site, "layouts", "index.html"), "<h1>Home</h1>");
            WriteAllTextUtf8(Path.Combine(site, "layouts", "list.html"),
                "<ul>{{ for p in pages }}<li>{{ p.title }}</li>{{ end }}</ul>");
        }
    }

    private static void WriteAllTextUtf8(string path, string content)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    // ---------------------------------------------------------------- fixtures

    private static Command BuildFixtures()
    {
        var cmd = new Command("fixtures", "演示 fixture：中文字数统计 / 占位插图生成");

        var count = new Command("count", "统计语料每 section 中文字符数（front matter 不计），不足 2000 字退出码 1");
        var sectionsArg = new Argument<string[]>("sections") { Description = "section 名；缺省扫全部" };
        sectionsArg.Arity = ArgumentArity.ZeroOrMore;
        count.Arguments.Add(sectionsArg);
        count.SetAction(parseResult => RunCount(parseResult.GetValue(sectionsArg) ?? Array.Empty<string>()));
        cmd.Subcommands.Add(count);

        var images = new Command("images", "生成 12 张占位插图 SVG 到 fixtures/corpus/static/images");
        images.SetAction(_ => RunImages());
        cmd.Subcommands.Add(images);

        return cmd;
    }

    private static int RunCount(string[] requestedSections)
    {
        var contentDir = Path.Combine(RepoPaths.FixturesCorpus, "content");

        var sections = requestedSections.Length > 0
            ? requestedSections
            : Directory.GetDirectories(contentDir).Select(d => Path.GetFileName(d)!).Order(StringComparer.Ordinal).ToArray()!;

        var total = 0;
        var bad = new List<(string Section, string File, int Count)>();

        foreach (var sec in sections.Order(StringComparer.Ordinal))
        {
            var dir = Path.Combine(contentDir, sec);
            if (!Directory.Exists(dir))
            {
                continue;
            }

            var files = Directory.GetFiles(dir, "*.md").Select(f => Path.GetFileName(f)!).Order(StringComparer.Ordinal).ToArray()!;
            var secTotal = 0;

            foreach (var f in files)
            {
                var path = Path.Combine(dir, f!);
                var body = SplitFrontMatter(File.ReadAllText(path));
                var n = CjkRegex.Matches(body).Count;
                secTotal += n;
                total++;

                if (n < 2000 && f != "_index.md")
                {
                    bad.Add((sec, f!, n));
                }
            }

            Console.WriteLine($"[{sec}] {files.Length} 篇，中文字符合计 {secTotal}");
        }

        Console.WriteLine($"---- 共 {total} 个 markdown 文件");

        if (bad.Count > 0)
        {
            Console.WriteLine("!! 不足 2000 字:");
            foreach (var (sec, f, n) in bad)
            {
                Console.WriteLine($"   {sec}/{f}: {n}");
            }

            return 1;
        }

        Console.WriteLine("全部达标（>=2000 中文字符）");
        return 0;
    }

    /// <summary>剥离 YAML front matter（与 Python 版切分位置逐字一致：从结束 "---" 之后起算）</summary>
    private static string SplitFrontMatter(string text)
    {
        if (!text.StartsWith("---", StringComparison.Ordinal))
        {
            return text;
        }

        var end = text.IndexOf("\n---", 3, StringComparison.Ordinal);
        return end < 0 ? text : text[(end + 4)..];
    }

    private static int RunImages()
    {
        var outDir = Path.Combine(RepoPaths.FixturesCorpus, "static", "images");
        Directory.CreateDirectory(outDir);

        var specs = new (string Main, string Sub, string Label, string Kind)[]
        {
            ("#2563eb", "#93c5fd", "架构示意图", "boxes"),
            ("#059669", "#6ee7b7", "流程图", "flow"),
            ("#d97706", "#fcd34d", "数据表", "table"),
            ("#dc2626", "#fca5a5", "告警面板", "gauge"),
            ("#7c3aed", "#c4b5fd", "拓扑图", "net"),
            ("#0891b2", "#a5f3fc", "时间线", "time"),
            ("#db2777", "#f9a8d4", "增长曲线", "chart"),
            ("#65a30d", "#d9f99d", "对比柱状", "bars"),
            ("#475569", "#cbd5e1", "代码片段", "code"),
            ("#e11d48", "#fda4af", "状态灯", "dots"),
            ("#0d9488", "#99f6e4", "分层结构", "layers"),
            ("#4f46e5", "#a5b4fc", "概念模型", "circle"),
        };

        for (var idx = 0; idx < specs.Length; idx++)
        {
            var (main, sub, label, kind) = specs[idx];
            var svg = Head
                + "<rect width=\"800\" height=\"400\" fill=\"#f8fafc\"/>"
                + Shapes(kind, main, sub)
                + $"<text x=\"400\" y=\"370\" text-anchor=\"middle\" font-size=\"26\" fill=\"#334155\" letter-spacing=\"4\">{label}</text>"
                + Foot;

            WriteAllTextUtf8(
                Path.Combine(outDir, $"demo-{idx + 1}.svg"),
                svg);

            Console.WriteLine($"demo-{idx + 1}.svg  {label}");
        }

        return 0;
    }

    private const string Head =
        "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"800\" height=\"400\" viewBox=\"0 0 800 400\" font-family=\"sans-serif\">";

    private const string Foot = "</svg>";

    private static string Shapes(string kind, string main, string sub)
    {
        switch (kind)
        {
            case "boxes":
            {
                var sb = new StringBuilder();
                sb.Append($"<rect x=\"80\" y=\"90\" width=\"180\" height=\"80\" rx=\"8\" fill=\"{sub}\"/>");
                sb.Append($"<rect x=\"310\" y=\"90\" width=\"180\" height=\"80\" rx=\"8\" fill=\"{sub}\"/>");
                sb.Append($"<rect x=\"540\" y=\"90\" width=\"180\" height=\"80\" rx=\"8\" fill=\"{sub}\"/>");
                sb.Append($"<rect x=\"195\" y=\"230\" width=\"180\" height=\"80\" rx=\"8\" fill=\"{main}\"/>");
                sb.Append($"<rect x=\"425\" y=\"230\" width=\"180\" height=\"80\" rx=\"8\" fill=\"{main}\"/>");
                sb.Append($"<path d=\"M170 170 L285 230 M400 170 L400 230 M630 170 L515 230\" stroke=\"{main}\" stroke-width=\"3\" fill=\"none\"/>");
                return sb.ToString();
            }

            case "flow":
            {
                var sb = new StringBuilder();
                for (var i = 0; i < 4; i++)
                {
                    var x = 90 + (i * 180);
                    sb.Append($"<rect x=\"{x}\" y=\"150\" width=\"120\" height=\"70\" rx=\"35\" fill=\"{sub}\"/>");
                    if (i < 3)
                    {
                        sb.Append($"<path d=\"M{x + 120} 185 L{x + 180} 185\" stroke=\"{main}\" stroke-width=\"3\" marker-end=\"url(#a)\"/>");
                    }
                }

                sb.Append($"<defs><marker id=\"a\" markerWidth=\"10\" markerHeight=\"10\" refX=\"8\" refY=\"3\" orient=\"auto\"><path d=\"M0 0 L8 3 L0 6 z\" fill=\"{main}\"/></marker></defs>");
                return sb.ToString();
            }

            case "table":
            {
                var sb = new StringBuilder();
                for (var r = 0; r < 4; r++)
                {
                    var y = 110 + (r * 55);
                    var rowOpacity = r % 2 == 1 ? "0.9" : "0.75";
                    sb.Append($"<rect x=\"150\" y=\"{y}\" width=\"500\" height=\"45\" rx=\"4\" fill=\"{(r % 2 == 1 ? sub : main)}\" opacity=\"{rowOpacity}\"/>");
                    for (var c = 0; c < 3; c++)
                    {
                        sb.Append($"<rect x=\"{170 + (c * 160)}\" y=\"{y + 12}\" width=\"120\" height=\"20\" rx=\"3\" fill=\"#ffffff\" opacity=\"0.85\"/>");
                    }
                }

                return sb.ToString();
            }

            case "gauge":
                return $"<path d=\"M 200 280 A 200 200 0 0 1 600 280\" fill=\"none\" stroke=\"{sub}\" stroke-width=\"34\" stroke-linecap=\"round\"/>"
                    + $"<path d=\"M 200 280 A 200 200 0 0 1 470 130\" fill=\"none\" stroke=\"{main}\" stroke-width=\"34\" stroke-linecap=\"round\"/>"
                    + $"<circle cx=\"400\" cy=\"280\" r=\"14\" fill=\"{main}\"/>";

            case "net":
            {
                var pts = new (int X, int Y)[]
                {
                    (400, 90), (200, 200), (600, 200), (280, 320), (520, 320),
                };
                var edges = new (int A, int B)[]
                {
                    (0, 1), (0, 2), (1, 3), (2, 4), (1, 2), (3, 4),
                };
                var sb = new StringBuilder();
                foreach (var (a, b) in edges)
                {
                    sb.Append($"<path d=\"M{pts[a].X} {pts[a].Y} L{pts[b].X} {pts[b].Y}\" stroke=\"{sub}\" stroke-width=\"3\"/>");
                }

                foreach (var (x, y) in pts)
                {
                    sb.Append($"<circle cx=\"{x}\" cy=\"{y}\" r=\"26\" fill=\"{main}\"/>");
                }

                return sb.ToString();
            }

            case "time":
            {
                var sb = new StringBuilder();
                sb.Append($"<path d=\"M 100 200 L 700 200\" stroke=\"{main}\" stroke-width=\"4\"/>");
                for (var i = 0; i < 5; i++)
                {
                    var x = 130 + (i * 135);
                    sb.Append($"<circle cx=\"{x}\" cy=\"200\" r=\"16\" fill=\"{sub}\" stroke=\"{main}\" stroke-width=\"4\"/>");
                }

                return sb.ToString();
            }

            case "chart":
                return $"<path d=\"M 100 330 C 250 320 350 260 450 190 S 620 90 700 70\" fill=\"none\" stroke=\"{main}\" stroke-width=\"5\"/>"
                    + $"<path d=\"M 100 330 L 700 330 M 100 330 L 100 60\" stroke=\"{sub}\" stroke-width=\"3\"/>";

            case "bars":
            {
                var sb = new StringBuilder();
                sb.Append($"<path d=\"M 100 330 L 700 330\" stroke=\"{main}\" stroke-width=\"3\"/>");
                var heights = new[] { 60, 130, 200, 150, 240, 110 };
                for (var i = 0; i < heights.Length; i++)
                {
                    var x = 130 + (i * 95);
                    var h = heights[i];
                    sb.Append($"<rect x=\"{x}\" y=\"{330 - h}\" width=\"56\" height=\"{h}\" rx=\"4\" fill=\"{(i % 2 == 1 ? sub : main)}\"/>");
                }

                return sb.ToString();
            }

            case "code":
            {
                var sb = new StringBuilder();
                var widths = new[] { 280, 340, 220, 380, 260 };
                for (var i = 0; i < widths.Length; i++)
                {
                    var w = widths[i];
                    sb.Append($"<rect x=\"160\" y=\"{90 + (i * 45)}\" width=\"{w}\" height=\"18\" rx=\"4\" fill=\"{(i % 2 == 1 ? sub : main)}\" opacity=\"0.8\"/>");
                }

                return sb.ToString();
            }

            case "dots":
            {
                var sb = new StringBuilder();
                var lamps = new (int Cx, bool On)[] { (200, true), (320, false), (440, true), (560, false), (660, true) };
                foreach (var (cx, on) in lamps)
                {
                    sb.Append($"<circle cx=\"{cx}\" cy=\"200\" r=\"34\" fill=\"{(on ? main : sub)}\"/>");
                }

                return sb.ToString();
            }

            case "layers":
            {
                var sb = new StringBuilder();
                for (var i = 0; i < 4; i++)
                {
                    var y = 90 + (i * 60);
                    var opacity = PyCompat.FormatDouble(1 - (i * 0.15));
                    sb.Append($"<rect x=\"{180 + (i * 30)}\" y=\"{y}\" width=\"{440 - (i * 60)}\" height=\"46\" rx=\"8\" fill=\"{(i == 0 ? main : sub)}\" opacity=\"{opacity}\"/>");
                }

                return sb.ToString();
            }

            default:
                // circle
                return $"<circle cx=\"400\" cy=\"200\" r=\"120\" fill=\"none\" stroke=\"{main}\" stroke-width=\"6\"/>"
                    + $"<circle cx=\"400\" cy=\"200\" r=\"70\" fill=\"{sub}\"/>"
                    + $"<circle cx=\"400\" cy=\"200\" r=\"24\" fill=\"{main}\"/>";
        }
    }
}
