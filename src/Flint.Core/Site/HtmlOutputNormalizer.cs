// Flint 静态站点生成器
// HTML 输出规范化：对齐 Go html/template 的序列化行为
//
// Hugo 的模板输出经过 html/template 解析再序列化——void 元素（meta/link/br…）
// 的自闭合斜杠被剥掉（`<meta ... />` → `<meta ...>`）。Flint 直接透传模板文本，
// 主题自带的 ` />` 全部保留 → 每页每个 void 标签都与 Hugo 差一个字符，
// 是结构相似度的最大单一噪声源（21 主题全量实测）。
// 仅处理 **void 元素**（HTML5 语义下自闭合斜杠本就无意义），引号内的 `/>`
// 不受影响；非 void 元素（script/div…）保持原样。

using System.Text.RegularExpressions;

namespace Flint.Core.Site;

internal static partial class HtmlOutputNormalizer
{
    // HTML 注释剥离：Go html/template 序列化时丢弃**普通**注释，但
    // **条件注释**（`<!--[if lt IE 9]>…<![endif]-->`）只剥标记、内容保留
    //（html/template 文档明载；console 的 html5shiv/respond 脚本实测——
    //  整段吞掉会让 Flint 独缺这两个脚本）。script/style 体内可能含
    //  "<!--"（旧式 JS 隐藏、字符串）——分段保护、原样保留
    //
    // 注意：Go html/template 对字面 HTML **逐字透传**——模板自写的
    // `<meta … />` 自闭合斜杠在 Hugo 产物里原样保留（xmin/narrow/console
    // 实测），故此处**不做** void 斜杠规范化（初版做过，S3 反向验证否决）
    private static readonly Regex Segment = ScriptStyleOrCommentRegex();
    private static readonly Regex ConditionalMarker = ConditionalCommentMarkerRegex();

    [GeneratedRegex(
        @"(?s)<script\b[^>]*>.*?</script\s*>|<style\b[^>]*>.*?</style\s*>|<!--(?!\[if).*?-->",
        RegexOptions.Compiled)]
    private static partial Regex ScriptStyleOrCommentRegex();

    // 条件注释标记：downlevel-revealed 开标记 `<!--[if …]>`、
    // downlevel-hidden 开标记 `<!--[if …]> -->`、
    // 闭标记 `<![endif]-->` 与 `<!--<![endif]-->`
    [GeneratedRegex(
        @"<!--\[if[^\]>]*\]\s*>\s*-->|<!--\[if[^\]>]*\]>|<!--\s*<!\[endif\]\s*-->|<!\[endif\]\s*-->",
        RegexOptions.Compiled)]
    private static partial Regex ConditionalCommentMarkerRegex();

    public static string Normalize(string html)
    {
        if (string.IsNullOrEmpty(html) || !html.Contains("<!--", StringComparison.Ordinal))
        {
            return html;
        }

        html = ConditionalMarker.Replace(html, "");
        html = Segment.Replace(html, static m =>
            m.Value.StartsWith("<!--", StringComparison.Ordinal) ? "" : m.Value);
        return html;
    }
}
