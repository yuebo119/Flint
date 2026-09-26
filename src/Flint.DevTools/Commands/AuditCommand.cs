// audit 子命令组：资产断链扫描（check-broken-assets.py）与元素签名差（audit-elements.py）的 C# 移植
// 输出格式、跳过规则、Top-N 口径与 Python 版逐字对拍（含 ← 标记与对齐宽度）

using System.CommandLine;
using System.Text.RegularExpressions;

namespace Flint.DevTools.Commands;

/// <summary>
/// audit 子命令组
/// </summary>
internal static class AuditCommand
{
    /// <summary>21 个矩阵主题（与 scripts/*.py 的 THEMES 列表一致）</summary>
    internal static readonly string[] Themes =
    {
        "ananke", "bearblog", "blog-awesome", "blowfish", "clarity", "console", "even",
        "fixit", "github-style", "hugo-book", "hugo-coder", "hugo-paper", "loveit", "m10c",
        "monochrome", "narrow", "papermod", "stack", "techdoc", "xmin", "yinyang",
    };

    internal static Command Build()
    {
        var cmd = new Command("audit", "主题产物审计（替代 scripts/check-broken-assets.py 与 scripts/audit-elements.py）");
        cmd.Subcommands.Add(BuildAssets());
        cmd.Subcommands.Add(BuildElements());
        return cmd;
    }

    // ---------------------------------------------------------------- audit assets

    private static readonly Regex RefRegex = new(
        @"(?:href|src|poster|data-src)\s*=\s*[""']([^""'#?]+)[""']|url\(([^)""']+)\)",
        RegexOptions.Compiled);

    private static readonly string[] ExternalPrefixes =
    {
        "http://", "https://", "//", "data:", "mailto:", "javascript:", "{",
    };

    private static Command BuildAssets()
    {
        var cmd = new Command("assets", "断链扫描：Flint 侧独有缺失逐条列出，两侧共有列前 3");

        cmd.SetAction(_ =>
        {
            foreach (var theme in Themes)
            {
                var flint = Check(Path.Combine(RepoPaths.Matrix20Root, theme, "public-flint"));
                var hugo = Check(Path.Combine(RepoPaths.Matrix20Root, theme, "public"));

                if (flint is null || hugo is null)
                {
                    Console.WriteLine($"{theme,-14} 缺产物目录");
                    continue;
                }

                var (flintTotal, flintMissing) = flint.Value;
                var (hugoTotal, hugoMissing) = hugo.Value;

                Console.WriteLine(
                    $"{theme,-14} Flint {flintMissing.KeyCount,3} 类缺失/{flintTotal,5} 引用" +
                    $"    Hugo {hugoMissing.KeyCount,3} 类缺失/{hugoTotal,5} 引用");

                // Flint 独有的一律列出（这是"Flint 的问题"清单）；两侧共有的只列前 3
                foreach (var (refKey, n) in flintMissing.MostCommon(int.MaxValue))
                {
                    if (hugoMissing[refKey] == 0)
                    {
                        Console.WriteLine($"      - {refKey} ×{n}  ← Flint 独有");
                    }
                }

                var shown = 0;
                foreach (var (refKey, n) in flintMissing.MostCommon(int.MaxValue))
                {
                    if (hugoMissing[refKey] > 0 && shown < 3)
                    {
                        Console.WriteLine($"      - {refKey} ×{n} （Hugo 侧也有）");
                        shown++;
                    }
                }
            }

            return 0;
        });

        return cmd;
    }

    /// <summary>扫描一个产出目录：(总根相对引用数, 缺失引用计数)</summary>
    private static (int Total, OrderedCounter Missing)? Check(string root)
    {
        if (!Directory.Exists(root))
        {
            return null;
        }

        var missing = new OrderedCounter();
        var total = 0;

        foreach (var html in Directory.EnumerateFiles(root, "*.html", SearchOption.AllDirectories))
        {
            var txt = File.ReadAllText(html);

            foreach (Match m in RefRegex.Matches(txt))
            {
                var refKey = (m.Groups[1].Value.Length > 0 ? m.Groups[1].Value : m.Groups[2].Value).Trim();
                if (refKey.Length == 0 || ExternalPrefixes.Any(p => refKey.StartsWith(p, StringComparison.Ordinal)))
                {
                    continue;
                }

                refKey = refKey.Split('#')[0].Split('?')[0];
                if (!refKey.StartsWith('/'))
                {
                    continue;
                }

                total++;

                var target = Path.Combine(root, refKey.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                if (Directory.Exists(target))
                {
                    target = Path.Combine(target, "index.html");
                }

                if (!File.Exists(target))
                {
                    missing.Add(refKey);
                }
            }
        }

        return (total, missing);
    }

    // ---------------------------------------------------------------- audit elements

    private static readonly Regex ScriptStyleRegex = new(
        @"<script[^>]*>.*?</script>|<style[^>]*>.*?</style>",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

    private static readonly Regex HashRegex = new(@"[0-9a-f]{8,64}", RegexOptions.Compiled);

    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

    private static readonly Regex TagRegex = new(@"<([a-zA-Z][\w-]*)\b[^>]*>", RegexOptions.Compiled);

    private static readonly Regex ClassRegex = new(@"\bclass\s*=\s*""([^""]*)""", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex MetaNameRegex = new(@"\b(?:property|name)\s*=\s*""([^""]*)""", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly HashSet<string> VoidTags = new(StringComparer.Ordinal)
    {
        "br", "link", "input", "img", "hr", "source", "track",
    };

    private static Command BuildElements()
    {
        var cmd = new Command("elements", "元素签名多重集差：缺元素 Top 25 / 多元素 Top 15（按涉及页面数）");

        cmd.SetAction(_ =>
        {
            var missTotal = new OrderedCounter();
            var missPages = new OrderedCounter();
            var extraTotal = new OrderedCounter();
            var extraPages = new OrderedCounter();

            foreach (var theme in Themes)
            {
                var hugoPages = CollectPages(Path.Combine(RepoPaths.Matrix20Root, theme, "public"));
                var flintPages = CollectPages(Path.Combine(RepoPaths.Matrix20Root, theme, "public-flint"));

                foreach (var rel in hugoPages.Keys.Intersect(flintPages.Keys).Order(StringComparer.Ordinal))
                {
                    var hugoSig = Sigs(Norm(File.ReadAllText(hugoPages[rel])));
                    var flintSig = Sigs(Norm(File.ReadAllText(flintPages[rel])));

                    foreach (var (key, n) in hugoSig.Minus(flintSig))
                    {
                        missTotal.Add(key, n);
                        missPages.Add(key);
                    }

                    foreach (var (key, n) in flintSig.Minus(hugoSig))
                    {
                        extraTotal.Add(key, n);
                        extraPages.Add(key);
                    }
                }
            }

            Console.WriteLine("=== 缺元素 Top 25（Hugo 有、Flint 缺；按涉及页面数）===");
            foreach (var (key, n) in missPages.MostCommon(25))
            {
                Console.WriteLine($"{n,4} 页 / {missTotal[key],5} 处   {Truncate(key, 100)}");
            }

            Console.WriteLine();
            Console.WriteLine("=== 多元素 Top 15（Flint 多出）===");
            foreach (var (key, n) in extraPages.MostCommon(15))
            {
                Console.WriteLine($"{n,4} 页 / {extraTotal[key],5} 处   {Truncate(key, 100)}");
            }

            return 0;
        });

        return cmd;
    }

    private static string Truncate(string s, int max)
    {
        return s.Length <= max ? s : s[..max];
    }

    private static string Norm(string html)
    {
        var h = ScriptStyleRegex.Replace(html, string.Empty);
        h = HashRegex.Replace(h, "HASH");
        h = WhitespaceRegex.Replace(h, " ");
        return h.Trim();
    }

    private static OrderedCounter Sigs(string html)
    {
        var counter = new OrderedCounter();

        foreach (Match m in TagRegex.Matches(html))
        {
            // 小写是输出键的显示格式（与 Python .lower() 逐字对拍），非大小写规范化判等场景，
            // 故 CA1308 建议的 ToUpperInvariant 不适用
#pragma warning disable CA1308
            var tag = m.Groups[1].Value.ToLowerInvariant();

            if (tag == "meta")
            {
                var k = MetaNameRegex.Match(m.Value);
                if (!k.Success)
                {
                    continue;
                }

                counter.Add("meta[" + k.Groups[1].Value.ToLowerInvariant() + "]");
                continue;
            }

            if (VoidTags.Contains(tag))
            {
                continue;
            }

            var cm = ClassRegex.Match(m.Value);
            var cls = cm.Success
                ? string.Join('.', PyCompat.SplitWhitespace(cm.Groups[1].Value.ToLowerInvariant())
                    .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
                : string.Empty;
#pragma warning restore CA1308

            counter.Add(cls.Length > 0 ? tag + "." + cls : tag);
        }

        return counter;
    }

    private static Dictionary<string, string> CollectPages(string root)
    {
        var pages = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!Directory.Exists(root))
        {
            return pages;
        }

        foreach (var html in Directory.EnumerateFiles(root, "*.html", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(root, html).Replace('\\', '/');
            pages[rel] = html;
        }

        return pages;
    }
}
