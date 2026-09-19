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
    private static readonly Regex VoidSelfClosing = VoidSelfClosingRegex();

    [GeneratedRegex(
        @"<(area|base|br|col|embed|hr|img|input|link|meta|param|source|track|wbr)((?:[^>""']|""[^""']*""|'[^']*')*?)\s*/>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex VoidSelfClosingRegex();

    public static string Normalize(string html)
    {
        if (string.IsNullOrEmpty(html) || !html.Contains("/>", StringComparison.Ordinal))
        {
            return html;
        }

        return VoidSelfClosing.Replace(html, static m =>
        {
            var inner = m.Groups[2].Value.TrimEnd();
            return $"<{m.Groups[1].Value}{inner}>";
        });
    }
}
