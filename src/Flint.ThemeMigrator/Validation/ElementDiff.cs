// Flint 主题迁移工具
// 门禁④的结构化对比：把"相似度分数"换成"缺了哪些元素 / 多了哪些元素"
//
// 动机：结构相似度（标签序列逐位命中率）与文本覆盖度是**标量**——它只说
// "像不像"，不说"差在哪"。迁移一个主题时真正要回答的是具体问题：
// "首页少了 post-meta 区块"、"列表页每个 li 少了 <time>"、
// "Flint 多输出了整块 pagination"。
//
// 做法：把每个 HTML 的上场元素压成**签名多重集**（标签 + 排序后的 class 列表，
// 如 `div.post-meta.wide`），按页做多重集差，再把差聚合到"主题级 top-N"。
// 签名刻意不含 id 与文本（id 多为唯一值、文本噪声大，会淹没结构差异）；
// script/style 内容不参与（归一化后仍是脚本）。
//
// 不引入 HTML 解析器依赖：正则抽取 + 显式栈式忽略 script/style，AOT 安全、零依赖。

using System.Text.RegularExpressions;

namespace Flint.ThemeMigrator.Validation;

/// <summary>一个元素签名在两引擎下的计数差</summary>
/// <param name="Signature">元素签名（tag.class1.class2，class 按字典序）</param>
/// <param name="HugoCount">Hugo 侧出现次数（聚合后为总和）</param>
/// <param name="FlintCount">Flint 侧出现次数</param>
/// <param name="Pages">涉及页面数（聚合结果才有意义）</param>
internal sealed record ElementSignatureCount(
    string Signature, int HugoCount, int FlintCount, int Pages = 0)
{
    /// <summary>绝对差量（缺为正、多为负由调用方决定语义）</summary>
    public int Delta => Math.Abs(HugoCount - FlintCount);
}

/// <summary>单页的结构化差异</summary>
internal sealed record PageElementDiff(
    string Page,
    IReadOnlyList<ElementSignatureCount> Missing,
    IReadOnlyList<ElementSignatureCount> Extra,
    IReadOnlyList<string> MissingTextTokens)
{
    /// <summary>该页缺元素总量（排序用）</summary>
    public int MissingTotal => Missing.Sum(m => m.Delta);
}

/// <summary>签名多重集的构造与比较</summary>
internal static partial class ElementDiff
{
    /// <summary>元素签名提取：`tag` 或 `tag.class1.class2`（class 排序、小写）</summary>
    internal static IReadOnlyDictionary<string, int> SignatureCounts(string html)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        // 先剔除 script/style 的**内容**（保留标签本身），避免脚本里的尖括号噪声
        var body = ScriptStyleContent().Replace(html, "");
        foreach (Match m in TagStart().Matches(body))
        {
            var tag = m.Groups[1].Value.ToLowerInvariant();
            string signature;
            if (tag == "meta")
            {
                // `<meta property="og:locale">` / `<meta name="twitter:card">` 是主题的
                // 大头（OG/Twitter 卡片），**要**计入签名——此前一律当"空元素"跳过，
                // 使"缺一个 og:locale"这类差异在报告里完全不可见。
                // 其余 meta（charset/viewport/generator）无语义，跳过
                var key = MetaKey().Match(m.Value) is { Success: true } km ? km.Groups[1].Value : "";
                if (key.Length == 0)
                {
                    continue;
                }

                signature = "meta[" + key.ToLowerInvariant() + "]";
            }
            else if (tag is "br" or "link" or "input" or "img" or "hr" or "source" or "track")
            {
                // 其余空元素不承载结构语义（样式/资源引用各自的 class 已由宿主元素表达）
                continue;
            }
            else
            {
                var classes = ClassAttr().Match(m.Value) is { Success: true } cm
                    ? cm.Groups[1].Value
                        .Split([' ', '\t', '\n'], StringSplitOptions.RemoveEmptyEntries)
                        .Select(c => c.ToLowerInvariant())
                        .OrderBy(c => c, StringComparer.Ordinal)
                        .ToArray()
                    : [];
                var cls = string.Join(".", classes);
                signature = cls.Length == 0 ? tag : tag + "." + cls;
            }

            counts[signature] = counts.TryGetValue(signature, out var n) ? n + 1 : 1;
        }

        return counts;
    }

    /// <summary>单页差：Hugo 有而 Flint 缺（或更少）为 Missing，反之为 Extra</summary>
    internal static PageElementDiff Compare(string page, string hugoHtml, string flintHtml)
    {
        var hugo = SignatureCounts(hugoHtml);
        var flint = SignatureCounts(flintHtml);
        return Build(page, hugo, flint);
    }

    /// <summary>由两侧签名计数构造差（供已算过计数的调用方复用）</summary>
    internal static PageElementDiff Build(
        string page,
        IReadOnlyDictionary<string, int> hugo,
        IReadOnlyDictionary<string, int> flint)
    {
        var missing = new List<ElementSignatureCount>();
        var extra = new List<ElementSignatureCount>();
        foreach (var (sig, hc) in hugo)
        {
            var fc = flint.TryGetValue(sig, out var n) ? n : 0;
            if (hc > fc)
            {
                missing.Add(new ElementSignatureCount(sig, hc, fc));
            }
        }

        foreach (var (sig, fc) in flint)
        {
            var hc = hugo.TryGetValue(sig, out var n) ? n : 0;
            if (fc > hc)
            {
                extra.Add(new ElementSignatureCount(sig, hc, fc));
            }
        }

        missing.Sort((a, b) => b.Delta.CompareTo(a.Delta));
        extra.Sort((a, b) => b.Delta.CompareTo(a.Delta));
        return new PageElementDiff(page, missing, extra, []);
    }

    /// <summary>
    /// 聚合到主题级：按差量总和排序取前 N，并记下涉及页面数
    /// （"12 处缺失集中在 8 个页面"比"缺 12 个"更可执行）
    /// </summary>
    internal static IReadOnlyList<ElementSignatureCount> Aggregate(
        IEnumerable<PageElementDiff> diffs, bool missing, int topN)
    {
        var totals = new Dictionary<string, (int Delta, int Pages)>(StringComparer.Ordinal);
        foreach (var d in diffs)
        {
            foreach (var item in missing ? d.Missing : d.Extra)
            {
                var cur = totals.TryGetValue(item.Signature, out var t) ? t : (0, 0);
                totals[item.Signature] = (cur.Item1 + item.Delta, cur.Item2 + 1);
            }
        }

        return totals
            .OrderByDescending(kv => kv.Value.Delta)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Take(topN)
            .Select(kv => new ElementSignatureCount(
                kv.Key,
                missing ? kv.Value.Delta : 0,
                missing ? 0 : kv.Value.Delta,
                kv.Value.Pages))
            .ToList();
    }

    /// <summary>报告里的一行：`div.post-meta ×12（8 页）`</summary>
    internal static string Describe(ElementSignatureCount c) =>
        $"{c.Signature} ×{c.Delta}（{c.Pages} 页）";

    private const string ScriptStylePattern =
        @"<(script|style)\b[^>]*>.*?</\1\s*>";

    private const string TagStartPattern = @"<([a-zA-Z][\w-]*)\b[^>]*>";

    private const string ClassAttrPattern = @"\bclass\s*=\s*""([^""]*)""";

    /// <summary>meta 的语义键：property 优先，其次 name（值取到引号前）</summary>
    private const string MetaKeyPattern = @"\b(?:property|name)\s*=\s*""([^""]*)""";

    [GeneratedRegex(ScriptStylePattern, RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ScriptStyleContent();

    [GeneratedRegex(TagStartPattern)]
    private static partial Regex TagStart();

    [GeneratedRegex(ClassAttrPattern, RegexOptions.IgnoreCase)]
    private static partial Regex ClassAttr();

    [GeneratedRegex(MetaKeyPattern, RegexOptions.IgnoreCase)]
    private static partial Regex MetaKey();
}
