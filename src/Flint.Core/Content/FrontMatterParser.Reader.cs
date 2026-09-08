// Flint 静态站点生成器
// Front Matter 解析器——读取部分：格式检测、分隔符扫描与三路解析
// （从 FrontMatterParser.cs 按 partial 拆出）

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Flint.Core.Abstractions;
using Flint.Core.Configuration;
using Flint.Core.Models;
using Tomlyn;

namespace Flint.Core.Content;

/// <summary>
/// Front Matter 解析器——读取部分
/// （主文件：FrontMatterParser.cs，含类声明与 DetectFormat 公开方法）
/// </summary>
#pragma warning disable IL2026, IL3050 // AOT 警告在此类中被抑制
public sealed partial class FrontMatterParser
{
    /// <inheritdoc />
    public bool TryParse(
        ReadOnlySpan<char> content,
        [NotNullWhen(true)] out FrontMatter? frontMatter,
        out ReadOnlySpan<char> remainingContent,
        TimeZoneInfo? siteTimeZone = null)
    {
        frontMatter = null;
        remainingContent = content;

        if (content.IsEmpty)
        {
            return false;
        }

        // 检测 Front Matter 格式
        var format = DetectFormatInternal(content);
        if (format is null)
        {
            return false;
        }

        var result = format.Value switch
        {
            FrontMatterFormat.Yaml => TryParseSimpleYaml(content, out frontMatter, out remainingContent, siteTimeZone)
                || TryParseYaml(content, out frontMatter, out remainingContent, siteTimeZone),
            FrontMatterFormat.Toml => TryParseToml(content, out frontMatter, out remainingContent, siteTimeZone),
            FrontMatterFormat.Json => TryParseJson(content, out frontMatter, out remainingContent, siteTimeZone),
            _ => false
        };

        return result;
    }

    /// <summary>
    /// 已知 Front Matter 标量字段白名单：快速路径只处理这些键，白名单外
    /// （自定义 params）整段回退完整解析器，保证不丢任何键
    /// </summary>
    private static readonly System.Collections.Frozen.FrozenSet<string> SimpleScalarKeys =
        System.Collections.Frozen.FrozenSet.ToFrozenSet(
            new[] { "title", "slug", "description", "summary", "type", "layout", "url",
                    "weight", "draft", "date", "lastmod", "publishdate", "expirydate",
                    "tags", "categories", "keywords", "authors", "aliases", "outputs" },
            StringComparer.OrdinalIgnoreCase);

    // 简单标量值的禁用字符：引号嵌套/结构括号——命中即回退完整解析器
    private static readonly System.Buffers.SearchValues<char> SimpleValueForbiddenChars =
        System.Buffers.SearchValues.Create(new[] { '"', '\'', '[', ']', '{', '}' });

    /// <summary>
    /// YAML 快速路径：简单标量 front matter（key: 裸标量/引号串/行内数组）手写解析，
    /// 跳过 YamlDotNet 引擎（每页 100-200μs 的解析器构建与反射开销在万页站点放大
    /// 为秒级）。类型形态与 YamlDotNet object 反序列化严格对齐：标量一律 String
    /// 原样文本、行内数组 List&lt;object&gt;、空值 null——下游 ConvertDictToFrontMatter
    /// 的 string 分支不受影响。任何行不符合简单模式（嵌套结构/块标量/注释/白名单外
    /// 键/转义）即整体回退完整解析器，语义零漂移
    /// </summary>
    private static bool TryParseSimpleYaml(
        ReadOnlySpan<char> content,
        [NotNullWhen(true)] out FrontMatter? frontMatter,
        out ReadOnlySpan<char> remainingContent,
        TimeZoneInfo? siteTimeZone)
    {
        frontMatter = null;
        remainingContent = content;

        var trimmed = content.TrimStart();
        if (!trimmed.StartsWith(YamlDelimiter.AsSpan()))
        {
            return false;
        }

        var afterStart = TrimNewlines(trimmed[YamlDelimiter.Length..]);
        var endIndex = FindDelimiterIndex(afterStart, YamlDelimiter);
        if (endIndex < 0)
        {
            return false;
        }

        var yamlSpan = afterStart[..endIndex];
        remainingContent = TrimNewlines(afterStart[(endIndex + YamlDelimiter.Length)..]);

        var dict = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var lineSpan in yamlSpan.EnumerateLines())
        {
            var line = lineSpan.Trim();
            if (line.IsEmpty)
            {
                continue;
            }

            var colon = line.IndexOf(':');
            if (colon <= 0)
            {
                return false; // 无键或以冒号开头：非简单行
            }

            var key = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();

            // key 形态与白名单：未知键（自定义 params）不丢即回退
            if (key.Length == 0 || !IsSimpleKey(key) || !SimpleScalarKeys.Contains(key.ToString()))
            {
                return false;
            }

            // 空值 → 键不入字典（下游 GetDict 对缺键与 null 同为 null，语义等价）
            if (value.IsEmpty)
            {
                continue;
            }

            // 结构与块标量起始字符：YAML 语义超出简单标量，回退
            var v0 = value[0];
            if (v0 is '{' or '[' && !IsInlineArray(value) || v0 is '|' or '>' or '&' or '*' or '!')
            {
                return false;
            }

            // 行内数组：[a, "b", c] → List<object>（元素 String；嵌套/空元素回退）
            if (v0 == '[')
            {
                if (value[^1] != ']' || !TryParseInlineArray(value, out var list))
                {
                    return false;
                }
                dict[key.ToString()] = list;
                continue;
            }

            // 行内注释（" #"): YamlDotNet 会剥离——快速路径回退，不猜测边界
            if (value.Contains(" #", StringComparison.Ordinal))
            {
                return false;
            }

            // 引号字符串：成对闭合剥外壳；内部转义/不闭合回退
            if (v0 is '"' or '\'')
            {
                if (value.Length < 2 || value[^1] != v0 || value[1..^1].IndexOf(v0) >= 0)
                {
                    return false;
                }
                dict[key.ToString()] = value[1..^1].ToString();
                continue;
            }

            // 裸标量：原样文本（YamlDotNet object 模式对 false/10/日期都产 String，
            // 探针实证——下游 GetDictBool/GetDictInt/GetDictDateWithSource 自行解析）
            dict[key.ToString()] = value.ToString();
        }

        if (dict.Count == 0)
        {
            return false;
        }

        frontMatter = ConvertDictToFrontMatter(dict, FrontMatterFormat.Yaml, siteTimeZone);
        return true;
    }

    /// <summary>key 形态：字母开头的字母/数字/连字符/下划线</summary>
    private static bool IsSimpleKey(ReadOnlySpan<char> key)
    {
        if (!(char.IsAsciiLetter(key[0]) || key[0] == '_'))
        {
            return false;
        }
        foreach (var c in key[1..])
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_'))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// 行内数组形态判定：value 以 [ 开头且以 ] 结尾、无嵌套括号、无块标量字符。
    /// 产出 List&lt;object&gt;（元素剥引号的 String，与 YamlDotNet 对齐）
    /// </summary>
    private static bool TryParseInlineArray(ReadOnlySpan<char> value, out List<object> list)
    {
        list = [];
        var inner = value[1..^1].Trim();
        if (inner.IsEmpty)
        {
            return true; // 空数组 []
        }

        var innerText = inner.ToString();
        foreach (var item in innerText.Split(','))
        {
            var t = item.Trim();
            if (t.Length == 0)
            {
                return false;
            }
            if (t[0] is '"' or '\'')
            {
                if (t.Length < 2 || t[^1] != t[0] || t[1..^1].Contains(t[0]))
                {
                    return false;
                }
                list.Add(t[1..^1]);
            }
            else if (t.AsSpan().IndexOfAny(SimpleValueForbiddenChars) >= 0)
            {
                return false;
            }
            else
            {
                list.Add(t);
            }
        }
        return true;
    }

    /// <summary>行内数组判定：首 [ 尾 ] 且内部无嵌套括号</summary>
    private static bool IsInlineArray(ReadOnlySpan<char> value)
    {
        return value[0] == '[' && value[^1] == ']' &&
               value[1..^1].IndexOfAny(stackalloc char[] { '[', ']', '{', '}' }) < 0;
    }

    /// <inheritdoc />
    public (FrontMatter FrontMatter, string RemainingContent) Parse(
        string content,
        TimeZoneInfo? siteTimeZone = null)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (TryParse(content.AsSpan(), out var frontMatter, out var remaining, siteTimeZone))
        {
            return (frontMatter, remaining.ToString());
        }

        throw new FormatException("无法解析 Front Matter，请检查格式是否正确");
    }

    /// <summary>
    /// 内部格式检测方法
    /// </summary>
    private static FrontMatterFormat? DetectFormatInternal(ReadOnlySpan<char> content)
    {
        var trimmed = content.TrimStart();

        if (trimmed.IsEmpty)
        {
            return null;
        }

        // 检查 YAML 分隔符 (---)——要求整行仅分隔符：
        // 正文以 ---- 开头（Markdown 水平线）会被 StartsWith 误判为 Front Matter
        if (IsDelimiterLine(trimmed, YamlDelimiter))
        {
            return FrontMatterFormat.Yaml;
        }

        // 检查 TOML 分隔符 (+++)
        if (IsDelimiterLine(trimmed, TomlDelimiter))
        {
            return FrontMatterFormat.Toml;
        }

        // 检查 JSON 格式 ({)
        if (trimmed[0] == '{')
        {
            return FrontMatterFormat.Json;
        }

        return null;
    }

    /// <summary>分隔符必须独占一行（其后无字符或仅空白），避免 ----/++++ 类正文误判</summary>
    private static bool IsDelimiterLine(ReadOnlySpan<char> line, string delimiter)
    {
        return line.StartsWith(delimiter.AsSpan(), StringComparison.Ordinal) &&
               (line.Length == delimiter.Length || char.IsWhiteSpace(line[delimiter.Length]));
    }

    /// <summary>
    /// 尝试解析 YAML Front Matter
    /// </summary>
    private static bool TryParseYaml(
        ReadOnlySpan<char> content,
        [NotNullWhen(true)] out FrontMatter? frontMatter,
        out ReadOnlySpan<char> remainingContent,
        TimeZoneInfo? siteTimeZone)
    {
        frontMatter = null;
        remainingContent = content;

        var trimmed = content.TrimStart();

        // 查找开始分隔符
        if (!trimmed.StartsWith(YamlDelimiter.AsSpan()))
        {
            return false;
        }

        // 跳过开始分隔符和换行
        var afterStart = TrimNewlines(trimmed[YamlDelimiter.Length..]);

        // 查找结束分隔符
        var endIndex = FindDelimiterIndex(afterStart, YamlDelimiter);
        if (endIndex < 0)
        {
            return false;
        }

        // 提取 YAML 内容
        var yamlContent = afterStart[..endIndex].ToString();

        // 计算剩余内容的起始位置
        var afterEnd = TrimNewlines(afterStart[(endIndex + YamlDelimiter.Length)..]);
        remainingContent = afterEnd;

        // 解析 YAML
        try
        {
            var dict = SharedYaml.Deserializer.Deserialize<Dictionary<string, object>>(yamlContent)
                ?? new Dictionary<string, object>();
            frontMatter = ConvertDictToFrontMatter(dict, FrontMatterFormat.Yaml, siteTimeZone);
            return true;
        }
        catch (Exception ex) when (ex is YamlDotNet.Core.YamlException or InvalidOperationException)
        {
            // 分隔符完整但内容格式错误：必须让构建失败，否则草稿/日期等元数据会被静默丢弃。
            // YamlException 自带流内位置（文档级行号 = FM 起始行 + 流内行号）
            if (ex is YamlDotNet.Core.YamlException ye)
            {
                // NativeAOT 发布形态：YamlDotNet 依赖运行时代码生成，反序列化必然失败
                //（内层反射异常被包装为带位置的 YamlException）——给可行动指引
                if (!System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported)
                {
                    throw new FrontMatterParseException(
                        "Yaml",
                        "YAML 解析在 NativeAOT 发布形态不受支持（YamlDotNet 依赖运行时代码生成）。" +
                        "请改用 TOML（+++）或 JSON（{}）Front Matter，或使用 Trim+R2R 发布形态",
                        ye,
                        line: (int)Math.Min(ye.Start.Line, int.MaxValue),
                        column: (int)Math.Min(ye.Start.Column, int.MaxValue));
                }

                throw new FrontMatterParseException(
                    "Yaml",
                    $"YAML Front Matter 格式错误（第 {ye.Start.Line} 行, 第 {ye.Start.Column} 列）: {ye.Message}",
                    ye,
                    line: (int)Math.Min(ye.Start.Line, int.MaxValue),
                    column: (int)Math.Min(ye.Start.Column, int.MaxValue));
            }

            // NativeAOT 发布形态：YamlDotNet 依赖运行时代码生成，反序列化必然失败——
            // 给出可行动指引而非裸的 "Exception during deserialization"
            if (ex is InvalidOperationException && !System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported)
            {
                throw new FrontMatterParseException(
                    "Yaml",
                    "YAML 解析在 NativeAOT 发布形态不受支持（YamlDotNet 依赖运行时代码生成）。" +
                    "请改用 TOML（+++）或 JSON（{}）Front Matter，或使用 Trim+R2R 发布形态",
                    ex);
            }

            throw new FrontMatterParseException(
                "Yaml",
                $"YAML Front Matter 格式错误: {ex.Message}",
                ex);
        }
    }

    /// <summary>
    /// 尝试解析 TOML Front Matter
    /// </summary>
    private static bool TryParseToml(
        ReadOnlySpan<char> content,
        [NotNullWhen(true)] out FrontMatter? frontMatter,
        out ReadOnlySpan<char> remainingContent,
        TimeZoneInfo? siteTimeZone)
    {
        frontMatter = null;
        remainingContent = content;

        var trimmed = content.TrimStart();

        // 查找开始分隔符
        if (!trimmed.StartsWith(TomlDelimiter.AsSpan()))
        {
            return false;
        }

        // 跳过开始分隔符和换行
        var afterStart = TrimNewlines(trimmed[TomlDelimiter.Length..]);

        // 查找结束分隔符
        var endIndex = FindDelimiterIndex(afterStart, TomlDelimiter);
        if (endIndex < 0)
        {
            return false;
        }

        // 提取 TOML 内容
        var tomlContent = afterStart[..endIndex].ToString();

        // 计算剩余内容的起始位置
        var afterEnd = TrimNewlines(afterStart[(endIndex + TomlDelimiter.Length)..]);
        remainingContent = afterEnd;

        // 解析 TOML
        try
        {
            var model = TomlynCompat.ParseTable(tomlContent);
            frontMatter = ConvertDictToFrontMatter(ConfigNormalizer.NormalizeToml(model), FrontMatterFormat.Toml, siteTimeZone);
            return true;
        }
        catch (Exception ex) when (ex is Tomlyn.TomlException or InvalidOperationException)
        {
            throw new FrontMatterParseException(
                "Toml",
                $"TOML Front Matter 格式错误: {ex.Message}",
                ex);
        }
    }

    /// <summary>
    /// 尝试解析 JSON Front Matter
    /// </summary>
    private static bool TryParseJson(
        ReadOnlySpan<char> content,
        [NotNullWhen(true)] out FrontMatter? frontMatter,
        out ReadOnlySpan<char> remainingContent,
        TimeZoneInfo? siteTimeZone)
    {
        frontMatter = null;
        remainingContent = content;

        var trimmed = content.TrimStart();

        if (trimmed.IsEmpty || trimmed[0] != '{')
        {
            return false;
        }

        // 查找匹配的闭合括号
        var endIndex = FindMatchingBrace(trimmed);
        if (endIndex < 0)
        {
            return false;
        }

        // 提取 JSON 内容
        var jsonContent = trimmed[..(endIndex + 1)].ToString();

        // 计算剩余内容
        var afterEnd = TrimNewlines(trimmed[(endIndex + 1)..]);
        remainingContent = afterEnd;

        // 解析 JSON
        try
        {
            using var doc = JsonDocument.Parse(jsonContent, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });
            frontMatter = ConvertDictToFrontMatter(ConfigNormalizer.NormalizeJson(doc.RootElement), FrontMatterFormat.Json, siteTimeZone);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            throw new FrontMatterParseException(
                "Json",
                $"JSON Front Matter 格式错误: {ex.Message}",
                ex);
        }
    }

    /// <summary>
    /// 去除开头的换行符
    /// </summary>
    private static ReadOnlySpan<char> TrimNewlines(ReadOnlySpan<char> content)
    {
        var start = 0;
        while (start < content.Length && (content[start] == '\r' || content[start] == '\n'))
        {
            start++;
        }
        return content[start..];
    }

    /// <summary>
    /// 在内容中查找分隔符的位置
    /// </summary>
    private static int FindDelimiterIndex(ReadOnlySpan<char> content, string delimiter)
    {
        var delimiterSpan = delimiter.AsSpan();
        var index = 0;

        while (index < content.Length)
        {
            // 查找换行符
            var newlineIndex = content[index..].IndexOfAny('\r', '\n');
            if (newlineIndex < 0)
            {
            // 检查最后一行（要求分隔符独占行：其后无字符或仅空白——
            // 正文 ---- 水平线不是结束分隔符）
            var lastLine = content[index..].TrimStart();
            if (lastLine.StartsWith(delimiterSpan) &&
                (lastLine.Length == delimiterSpan.Length || char.IsWhiteSpace(lastLine[delimiterSpan.Length])))
            {
                return index + (content.Length - index - lastLine.Length);
            }
            break;
            }

            // 检查当前行是否是分隔符（独占行判定，同开头检测的 IsDelimiterLine 语义）
            var lineStart = index;
            var lineEnd = index + newlineIndex;
            var line = content[lineStart..lineEnd].TrimStart();

            if (line.StartsWith(delimiterSpan) &&
                (line.Length == delimiterSpan.Length || char.IsWhiteSpace(line[delimiterSpan.Length])))
            {
                // 找到分隔符，返回行首位置
                return lineStart + (lineEnd - lineStart - line.Length);
            }

            // 跳过换行符
            index = lineEnd + 1;
            if (index < content.Length && content[index - 1] == '\r' && content[index] == '\n')
            {
                index++;
            }
        }

        return -1;
    }

    /// <summary>
    /// 查找匹配的闭合大括号
    /// </summary>
    private static int FindMatchingBrace(ReadOnlySpan<char> content)
    {
        var depth = 0;
        var inString = false;
        var escape = false;

        for (var i = 0; i < content.Length; i++)
        {
            var c = content[i];

            if (escape)
            {
                escape = false;
                continue;
            }

            if (c == '\\' && inString)
            {
                escape = true;
                continue;
            }

            if (c == '"')
            {
                inString = !inString;
                continue;
            }

            if (inString)
            {
                continue;
            }

            if (c == '{')
            {
                depth++;
            }
            else if (c == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }
            }
        }

        return -1;
    }
}
#pragma warning restore IL2026, IL3050
