// Flint 主题迁移工具
// 内联 partial 定义提取（Hugo 的 `{{ define "_partials/X.html" }}` 形态）
//
// Hugo 允许在同一模板内联声明 partial，供本文件或其他文件通过 partial 调用：
//   {{ define "_partials/AnankeGetResource.html" }} ... {{ end }}
//   {{ partial "AnankeGetResource.html" . }}
//
// Scriban 无此机制（define 是块继承语义）——必须把内联块提取为独立文件
// `_partials/AnankeGetResource.html`，使 include 能命中。
//
// 实测依据：Ananke 的 site-style.html 用此形态定义 AnankeGetResource，
// 不提取则构建报 "Could not find a part of the path .../AnankeGetResource.html"。

using System.Text.RegularExpressions;
using Flint.ThemeMigrator.Parsing;

namespace Flint.ThemeMigrator.Migration;

/// <summary>提取出的内联 partial</summary>
internal sealed record ExtractedInlinePartial(string RelativePath, string Content);

/// <summary>内联 partial 提取器（在转换前处理源文本）</summary>
internal static partial class InlinePartialExtractor
{
    /// <summary>
    /// 从模板源文本中提取内联 partial 定义。
    /// 返回（剥离内联定义后的文本, 提取出的 partial 列表）。
    /// </summary>
    public static (string RemainingText, IReadOnlyList<ExtractedInlinePartial> Partials) Extract(string source)
    {
        var partials = new List<ExtractedInlinePartial>();
        if (!source.Contains("define \"", StringComparison.Ordinal))
        {
            return (source, partials);
        }

        var tokens = new GoTemplateLexer(source).Tokenize();
        var parser = new GoTemplateParser(tokens);
        var parts = parser.Parse();

        // 扫描 define 关键字动作，配对到同名 end
        var remaining = new List<TemplatePart>();
        var i = 0;
        while (i < parts.Count)
        {
            if (parts[i] is ActionPart { Body: KeywordBody { Name: "define", Names.Count: > 0 } def }
                && def.Names[0].Contains('/', StringComparison.Ordinal))
            {
                // 收集到匹配的 end（含嵌套层数）
                var depth = 1;
                var body = new List<TemplatePart>();
                i++;
                while (i < parts.Count && depth > 0)
                {
                    if (parts[i] is ActionPart { Body: KeywordBody inner })
                    {
                        if (inner.Name == "define" || inner.Name == "if" || inner.Name == "with"
                            || inner.Name == "range" || inner.Name == "block")
                        {
                            depth++;
                        }
                        else if (inner.Name == "end")
                        {
                            depth--;
                            if (depth == 0)
                            {
                                i++;
                                break;
                            }
                        }
                    }
                    body.Add(parts[i]);
                    i++;
                }

                // 规范化路径：`_partials/AnankeGetResource.html`（Hugo 的虚拟路径）
                var rel = def.Names[0].Replace('\\', '/').TrimStart('/');
                if (!rel.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
                {
                    rel += ".html";
                }

                // 内容：重新序列化为原文拼接（保持模板语法，供后续转换）
                var content = string.Concat(body.Select(SerializePart));
                partials.Add(new ExtractedInlinePartial(rel, content));
                continue;
            }

            remaining.Add(parts[i]);
            i++;
        }

        if (partials.Count == 0)
        {
            return (source, partials);
        }

        return (string.Concat(remaining.Select(SerializePart)), partials);
    }

    /// <summary>把 AST 片段还原为源文本（保持模板语法）</summary>
    private static string SerializePart(TemplatePart part) => part switch
    {
        TextPart t => t.Text,
        CommentPart c => "{{" + c.Raw + "}}",
        ActionPart a => "{{" + (a.TrimLeft ? "- " : " ") + a.Raw + (a.TrimRight ? " -" : " ") + "}}",
        _ => ""
    };

    /// <summary>是否为可提取的内联 partial 定义（供诊断）</summary>
    public static bool IsInlinePartialDefinition(string templatePart) =>
        InlineDefineRegex().IsMatch(templatePart);

    [GeneratedRegex(@"\{\{-?\s*define\s+""[^""]*/[^""]*""\s*-?\}\}", RegexOptions.CultureInvariant)]
    private static partial Regex InlineDefineRegex();
}
