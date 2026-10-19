// Flint 静态站点生成器
// 模板文本函数共享实现（FLUID-MIGRATION-PLAN P4-2）：humanize/urlize/
// singularize 等文本变换原为 BuiltinTemplateFunctions 的 private 实现，
// Fluid 过滤器是第二消费方——抽到本类供双引擎共用（单一事实源），
// Scriban 侧私有方法改为委派。

namespace Flint.Core.Templates;

/// <summary>
/// 双引擎共享的模板文本函数（Hugo 文本函数族的 Flint 语义实现）。
/// 纯函数、无 IO、AOT 安全（无反射）
/// </summary>
internal static class TemplateTextFunctions
{
    /// <summary>urlize/anchorize：小写、非字母数字转连字符、去首尾连字符（保留非 ASCII）</summary>
    public static string Urlize(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        var lastDash = false;
        foreach (var ch in text.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch) || ch > 127)
            {
                sb.Append(ch);
                lastDash = false;
            }
            else if (!lastDash && sb.Length > 0)
            {
                sb.Append('-');
                lastDash = true;
            }
        }
        return sb.ToString().Trim('-');
    }

    /// <summary>humanize：连字符/下划线转空格并首字母大写（Hugo 语义）</summary>
    public static string Humanize(string text)
    {
        if (text.Length == 0)
        {
            return "";
        }
        var spaced = text.Replace('-', ' ').Replace('_', ' ');
        return char.ToUpperInvariant(spaced[0]) + spaced[1..];
    }

    /// <summary>singularize：复数词转单数（Flint 既有规则：ies→y、去尾 s）</summary>
    public static string Singularize(string text) =>
        text.EndsWith("ies", StringComparison.OrdinalIgnoreCase) && text.Length > 3
            ? text[..^3] + "y"
            : text.EndsWith('s') && !text.EndsWith("ss", StringComparison.OrdinalIgnoreCase)
                ? text[..^1]
                : text;

    /// <summary>
    /// titleize：每个单词首字母大写（Hugo title/titleize 语义；
    /// 与 .NET TextInfo.ToTitleCase 的差异：不过滤虚词，逐词大写）
    /// </summary>
    public static string Titleize(string text)
    {
        if (text.Length == 0)
        {
            return "";
        }
        var sb = new System.Text.StringBuilder(text.Length);
        var wordStart = true;
        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(wordStart ? char.ToUpperInvariant(ch) : ch);
                wordStart = false;
            }
            else
            {
                sb.Append(ch);
                wordStart = true;
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// pluralize（计数形态）：count == 1 取单数词，否则取复数词；
    /// 复数词缺省时单数词加 "s"（Hugo pluralize 语义）
    /// </summary>
    public static string Pluralize(int count, string? singular, string? plural = null)
    {
        var s = singular ?? "";
        return count == 1 ? s : plural ?? (s + "s");
    }

    /// <summary>plainify：剥掉 HTML 标签的纯文本（Hugo plainify 的最小等价）</summary>
    public static string Plainify(string html)
    {
        if (string.IsNullOrEmpty(html))
        {
            return "";
        }
        var sb = new System.Text.StringBuilder(html.Length);
        var inTag = false;
        foreach (var ch in html)
        {
            switch (ch)
            {
                case '<':
                    inTag = true;
                    break;
                case '>':
                    inTag = false;
                    sb.Append(' ');
                    break;
                default:
                    if (!inTag)
                    {
                        sb.Append(ch);
                    }
                    break;
            }
        }
        return sb.ToString();
    }
}
