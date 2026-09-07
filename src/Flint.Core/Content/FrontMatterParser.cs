// Flint 静态站点生成器
// Front Matter 解析器实现

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using Flint.Core.Abstractions;
using Flint.Core.Configuration;
using Flint.Core.Models;
using Tomlyn;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Flint.Core.Content;

/// <summary>
/// Front Matter 解析器
/// 支持 YAML (---)、TOML (+++)、JSON ({}) 三种格式
/// </summary>
#pragma warning disable IL2026, IL3050 // AOT 警告在此类中被抑制
public sealed class FrontMatterParser : IFrontMatterParser
{
    /// <summary>
    /// YAML 分隔符
    /// </summary>
    private const string YamlDelimiter = "---";

    /// <summary>
    /// TOML 分隔符
    /// </summary>
    private const string TomlDelimiter = "+++";

    // YAML 反序列化器（延迟初始化）
    private static IDeserializer? _yamlDeserializer;
    private static IDeserializer YamlDeserializer => _yamlDeserializer ??= new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    // YAML 序列化器（延迟初始化）
    private static ISerializer? _yamlSerializer;
    private static ISerializer YamlSerializer => _yamlSerializer ??= new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitDefaults)
        .Build();

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
            FrontMatterFormat.Yaml => TryParseYaml(content, out frontMatter, out remainingContent, siteTimeZone),
            FrontMatterFormat.Toml => TryParseToml(content, out frontMatter, out remainingContent, siteTimeZone),
            FrontMatterFormat.Json => TryParseJson(content, out frontMatter, out remainingContent, siteTimeZone),
            _ => false
        };

        return result;
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

    /// <inheritdoc />
    public FrontMatterFormat DetectFormat(ReadOnlySpan<char> content)
    {
        return DetectFormatInternal(content) ?? FrontMatterFormat.Yaml;
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

    /// <inheritdoc />
    public string Serialize(FrontMatter frontMatter, FrontMatterFormat format = FrontMatterFormat.Yaml)
    {
        ArgumentNullException.ThrowIfNull(frontMatter);

        return format switch
        {
            FrontMatterFormat.Yaml => SerializeYaml(frontMatter),
            FrontMatterFormat.Toml => SerializeToml(frontMatter),
            FrontMatterFormat.Json => SerializeJson(frontMatter),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "不支持的 Front Matter 格式")
        };
    }

    #region YAML 解析

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
            var dict = YamlDeserializer.Deserialize<Dictionary<string, object>>(yamlContent)
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
    /// 序列化为 YAML Front Matter
    /// </summary>
    private static string SerializeYaml(FrontMatter frontMatter)
    {
        var dict = ConvertFrontMatterToDict(frontMatter);
        var yaml = YamlSerializer.Serialize(dict);
        return $"{YamlDelimiter}\n{yaml}{YamlDelimiter}\n";
    }

    #endregion

    #region TOML 解析

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
    /// 序列化为 TOML Front Matter
    /// </summary>
    private static string SerializeToml(FrontMatter frontMatter)
    {
        var sb = new System.Text.StringBuilder();
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        sb.AppendLine(TomlDelimiter);

        // 必需字段
        sb.Append(culture, $"title = \"{EscapeString(frontMatter.Title)}\"\n");

        // 日期字段
        if (frontMatter.Date.HasValue)
            sb.Append(culture, $"date = {frontMatter.Date.Value:yyyy-MM-ddTHH:mm:sszzz}\n");
        else if (!string.IsNullOrEmpty(frontMatter.DateSource))
            sb.Append(culture, $"date = \"{frontMatter.DateSource}\"\n"); // 特殊日期源（":git" 等）原样写出

        if (frontMatter.LastMod.HasValue)
            sb.Append(culture, $"lastmod = {frontMatter.LastMod.Value:yyyy-MM-ddTHH:mm:sszzz}\n");
        else if (!string.IsNullOrEmpty(frontMatter.LastModSource))
            sb.Append(culture, $"lastmod = \"{frontMatter.LastModSource}\"\n");

        if (frontMatter.ExpiryDate.HasValue)
            sb.Append(culture, $"expiryDate = {frontMatter.ExpiryDate.Value:yyyy-MM-ddTHH:mm:sszzz}\n");

        if (frontMatter.PublishDate.HasValue)
            sb.Append(culture, $"publishDate = {frontMatter.PublishDate.Value:yyyy-MM-ddTHH:mm:sszzz}\n");

        // 布尔字段
        if (frontMatter.Draft)
            sb.AppendLine("draft = true");

        // 字符串字段
        if (!string.IsNullOrEmpty(frontMatter.Description))
            sb.Append(culture, $"description = \"{EscapeString(frontMatter.Description)}\"\n");

        if (!string.IsNullOrEmpty(frontMatter.Summary))
            sb.Append(culture, $"summary = \"{EscapeString(frontMatter.Summary)}\"\n");

        if (!string.IsNullOrEmpty(frontMatter.Layout))
            sb.Append(culture, $"layout = \"{EscapeString(frontMatter.Layout)}\"\n");

        if (!string.IsNullOrEmpty(frontMatter.Slug))
            sb.Append(culture, $"slug = \"{EscapeString(frontMatter.Slug)}\"\n");

        if (!string.IsNullOrEmpty(frontMatter.Author))
            sb.Append(culture, $"author = \"{EscapeString(frontMatter.Author)}\"\n");

        if (!string.IsNullOrEmpty(frontMatter.Type))
            sb.Append(culture, $"type = \"{EscapeString(frontMatter.Type)}\"\n");

        // 数值字段
        if (frontMatter.Weight != 0)
            sb.Append(culture, $"weight = {frontMatter.Weight}\n");

        // 列表字段
        if (frontMatter.Tags.Count > 0)
            sb.Append(culture, $"tags = [{string.Join(", ", frontMatter.Tags.Select(t => $"\"{EscapeString(t)}\""))}]\n");

        if (frontMatter.Categories.Count > 0)
            sb.Append(culture, $"categories = [{string.Join(", ", frontMatter.Categories.Select(c => $"\"{EscapeString(c)}\""))}]\n");

        if (frontMatter.Authors.Count > 0)
            sb.Append(culture, $"authors = [{string.Join(", ", frontMatter.Authors.Select(a => $"\"{EscapeString(a)}\""))}]\n");

        if (frontMatter.Keywords.Count > 0)
            sb.Append(culture, $"keywords = [{string.Join(", ", frontMatter.Keywords.Select(k => $"\"{EscapeString(k)}\""))}]\n");

        if (frontMatter.Aliases.Count > 0)
            sb.Append(culture, $"aliases = [{string.Join(", ", frontMatter.Aliases.Select(a => $"\"{EscapeString(a)}\""))}]\n");

        if (frontMatter.Outputs.Count > 0)
            sb.Append(culture, $"outputs = [{string.Join(", ", frontMatter.Outputs.Select(o => $"\"{EscapeString(o)}\""))}]\n");

        // params/menu/cascade 与 YAML/JSON 路径（ConvertFrontMatterToDict）对称——
        // 漏写使 TOML FM 页面"加载→保存"丢自定义参数/菜单/级联配置
        if (frontMatter.Params.Count > 0)
        {
            sb.AppendLine("[params]");
            foreach (var kvp in frontMatter.Params)
            {
                AppendTomlParamValue(sb, kvp.Key, kvp.Value, culture);
            }
            sb.AppendLine();
        }

        if (frontMatter.Menus is { } menus && menus.Count > 0)
        {
            foreach (var (menuKey, entry) in menus)
            {
                sb.Append(culture, $"[menu.{EscapeTomlBareKey(menuKey)}]\n");
                if (!string.IsNullOrEmpty(entry.Name))
                    sb.Append(culture, $"name = \"{EscapeString(entry.Name)}\"\n");
                if (entry.Weight != 0)
                    sb.Append(culture, $"weight = {entry.Weight}\n");
                if (!string.IsNullOrEmpty(entry.Parent))
                    sb.Append(culture, $"parent = \"{EscapeString(entry.Parent)}\"\n");
                if (!string.IsNullOrEmpty(entry.Identifier))
                    sb.Append(culture, $"identifier = \"{EscapeString(entry.Identifier)}\"\n");
                sb.AppendLine();
            }
        }

        if (frontMatter.Cascade is { } cascade)
        {
            sb.AppendLine("[cascade]");
            if (cascade.Data is not null)
            {
                // cascade 数据不经顶层默认值填充的对称写法：仅当 title 非
                // 解析端默认值时写出（启发式：用户显式取名 "Untitled" 极罕见）
                if (cascade.Data.Title != "Untitled")
                    sb.Append(culture, $"title = \"{EscapeString(cascade.Data.Title)}\"\n");
                if (cascade.Data.Draft)
                    sb.AppendLine("draft = true");
                if (!string.IsNullOrEmpty(cascade.Data.Description))
                    sb.Append(culture, $"description = \"{EscapeString(cascade.Data.Description)}\"\n");
                if (cascade.Data.Layout is { } layout && !string.IsNullOrEmpty(layout))
                    sb.Append(culture, $"layout = \"{EscapeString(layout)}\"\n");
            }

            if (!string.IsNullOrEmpty(cascade.Target) || !string.IsNullOrEmpty(cascade.Kind))
            {
                sb.AppendLine("[cascade._target]");
                if (!string.IsNullOrEmpty(cascade.Target))
                    sb.Append(culture, $"path = \"{EscapeString(cascade.Target)}\"\n");
                if (!string.IsNullOrEmpty(cascade.Kind))
                    sb.Append(culture, $"kind = \"{EscapeString(cascade.Kind)}\"\n");
            }
            sb.AppendLine();
        }

        sb.AppendLine(TomlDelimiter);
        return sb.ToString();
    }

    /// <summary>params 值按 TOML 标量写出（字符串/布尔/数字/字符串列表）</summary>
    private static void AppendTomlParamValue(
        System.Text.StringBuilder sb, string key, object? value, System.Globalization.CultureInfo culture)
    {
        var escapedKey = EscapeTomlBareKey(key);
        switch (value)
        {
            case null:
                break; // 空值跳过
            case string s when string.IsNullOrEmpty(s):
                break;
            case string s:
                sb.Append(culture, $"{escapedKey} = \"{EscapeString(s)}\"\n");
                break;
            case bool b:
                sb.Append(culture, $"{escapedKey} = {(b ? "true" : "false")}\n");
                break;
            case int or long or double or float:
                sb.Append(culture, $"{escapedKey} = {Convert.ToString(value, culture)}\n");
                break;
            case System.Collections.IEnumerable list when value is not char:
            {
                // 标量数组直写；嵌套 dict/list 无法在单行 TOML 数组表达，
                // 静默滤空会丢数据（写出 params = [] 的空壳）——fail loud 指明字段
                var items = new List<string>();
                foreach (var v in list)
                {
                    if (v is string str)
                    {
                        items.Add($"\"{EscapeString(str)}\"");
                    }
                    else if (v is int or long or double or float or bool)
                    {
                        items.Add(Convert.ToString(v, culture) ?? "");
                    }
                    else
                    {
                        throw new NotSupportedException(
                            $"TOML front matter 写回不支持嵌套结构字段 \"{key}\"（元素类型 {v?.GetType().Name ?? "null"}），已拒绝以避免静默丢数据");
                    }
                }
                sb.Append(culture, $"{escapedKey} = [{string.Join(", ", items)}]\n");
                break;
            }
            default:
                sb.Append(culture, $"{escapedKey} = \"{EscapeString(value.ToString() ?? "")}\"\n");
                break;
        }
    }

    /// <summary>TOML 裸键白名单外的键加引号（复用 ConfigParser 的 TOML 键规则）</summary>
    private static string EscapeTomlBareKey(string key)
    {
        return System.Text.RegularExpressions.Regex.IsMatch(key, @"^[A-Za-z0-9_-]+$")
            ? key
            : $"\"{EscapeString(key)}\"";
    }

    #endregion

    #region JSON 解析

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
    /// 序列化为 JSON Front Matter
    /// </summary>
    private static string SerializeJson(FrontMatter frontMatter)
    {
        var dict = ConvertFrontMatterToDict(frontMatter);
        // source-gen context：反射序列化在 native AOT 下运行时失败（同 ConfigParser 修复）
        var json = JsonSerializer.Serialize(dict, FrontMatterJsonContext.Default.DictionaryStringObject);
        return json + "\n";
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

    #endregion

    #region 辅助方法

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
    /// 转义字符串
    /// </summary>
    private static string EscapeString(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal);
    }

    #endregion

    #region 转换方法

    /// <summary>
    /// 将字典转换为 FrontMatter
    /// </summary>
    private static FrontMatter ConvertDictToFrontMatter(
        Dictionary<string, object> dict,
        FrontMatterFormat format,
        TimeZoneInfo? siteTimeZone)
    {
        // Hugo 特殊日期源（date/lastmod = ":git"/":filemodtime"/":filename"）：
        // 解析期不产出时间，只记录源标记，由 FrontMatterExtensions.WithDateSources 兑现
        var (date, dateSource) = GetDictDateWithSource(dict, "date", siteTimeZone);
        var (lastMod, lastModSource) = GetDictDateWithSource(dict, "lastmod", siteTimeZone);
        lastMod ??= GetDictDateTime(dict, "lastMod", siteTimeZone)
            ?? GetDictDateTime(dict, "modified", siteTimeZone); // Hugo 别名（不带源标记）
        return new FrontMatter
        {
            Title = GetDictString(dict, "title") ?? "Untitled",
            Date = date,
            DateSource = dateSource,
            LastMod = lastMod,
            LastModSource = lastModSource,
            Draft = GetDictBool(dict, "draft") ?? false,
            ExpiryDate = GetDictDateTime(dict, "expiryDate", siteTimeZone) ?? GetDictDateTime(dict, "expirydate", siteTimeZone)
                ?? GetDictDateTime(dict, "unpublishdate", siteTimeZone), // Hugo 别名
            PublishDate = GetDictDateTime(dict, "publishDate", siteTimeZone) ?? GetDictDateTime(dict, "publishdate", siteTimeZone)
                ?? GetDictDateTime(dict, "pubdate", siteTimeZone) ?? GetDictDateTime(dict, "published", siteTimeZone), // Hugo 别名
            Tags = GetDictStringList(dict, "tags"),
            Categories = GetDictStringList(dict, "categories"),
            Layout = GetDictString(dict, "layout"),
            Slug = GetDictString(dict, "slug"),
            Aliases = GetDictStringList(dict, "aliases"),
            Description = GetDictString(dict, "description"),
            Summary = GetDictString(dict, "summary"),
            Weight = GetDictInt(dict, "weight") ?? 0,
            Author = GetDictString(dict, "author"),
            Authors = GetDictStringList(dict, "authors"),
            Keywords = GetDictStringList(dict, "keywords"),
            Type = GetDictString(dict, "type"),
            Outputs = GetDictStringList(dict, "outputs"),
            Menus = GetDictMenus(dict),
            Cascade = GetDictCascade(dict, format, siteTimeZone),
            Params = GetDictParams(dict),
            Format = format
        };
    }

    /// <summary>
    /// 解析页面级菜单配置（menu.main: {name, weight, parent, identifier}）
    /// 此前该配置被解析器静默丢弃（模型有属性但三路 Convert 均不填充）。
    /// 兼容 YamlDotNet 的 Dictionary&lt;object, object&gt; 嵌套产物——不兼容时
    /// YAML 格式的 menu 配置静默失效
    /// </summary>
    private static IReadOnlyDictionary<string, MenuEntry>? GetDictMenus(Dictionary<string, object> dict)
    {
        if (!dict.TryGetValue("menu", out var value))
        {
            return null;
        }
        var obj = AsStringKeyDict(value);
        if (obj is null)
        {
            return null;
        }

        var menus = new Dictionary<string, MenuEntry>();
        foreach (var (menuKey, entryObj) in obj)
        {
            var entry = AsStringKeyDict(entryObj);
            if (entry is null)
            {
                continue;
            }

            menus[menuKey] = new MenuEntry
            {
                Name = GetEntryString(entry, "name"),
                Weight = GetEntryInt(entry, "weight") ?? 0,
                Parent = GetEntryString(entry, "parent"),
                Identifier = GetEntryString(entry, "identifier")
            };
        }

        return menus.Count > 0 ? menus : null;
    }

    /// <summary>
    /// 嵌套字典统一为 string 键：YamlDotNet 对无类型 mapping 产出
    /// Dictionary&lt;object, object&gt;，与 TOML/JSON 路径的 string 键字典形态不一致
    /// </summary>
    private static Dictionary<string, object>? AsStringKeyDict(object? value)
    {
        return value switch
        {
            Dictionary<string, object> d => d,
            Dictionary<object, object> d => d.ToDictionary(
                kvp => kvp.Key?.ToString() ?? "",
                kvp => kvp.Value),
            _ => null
        };
    }

    /// <summary>
    /// 解析页面级级联配置（cascade: {&lt;下级默认键&gt;..., _target: {path, kind}}）
    /// </summary>
    private static CascadeConfig? GetDictCascade(
        Dictionary<string, object> dict,
        FrontMatterFormat format,
        TimeZoneInfo? siteTimeZone)
    {
        if (!dict.TryGetValue("cascade", out var value))
        {
            return null;
        }
        var obj = AsStringKeyDict(value);
        if (obj is null)
        {
            return null;
        }

        // _target 是控制键：从 Data 中剔除，避免泄漏进 cascade.Data.Params
        var targetDict = obj.TryGetValue("_target", out var t) ? AsStringKeyDict(t) : null;
        var dataObj = new Dictionary<string, object>(obj);
        dataObj.Remove("_target");

        return new CascadeConfig
        {
            Data = ConvertDictToFrontMatter(dataObj, format, siteTimeZone),
            Target = targetDict is not null ? GetEntryString(targetDict, "path") : null,
            Kind = targetDict is not null ? GetEntryString(targetDict, "kind") : null
        };
    }

    private static string? GetEntryString(Dictionary<string, object> dict, string key)
    {
        return dict.TryGetValue(key, out var value) && value is string s && s.Length > 0 ? s : null;
    }

    private static int? GetEntryInt(Dictionary<string, object> dict, string key)
    {
        if (!dict.TryGetValue(key, out var value))
            return null;
        return value switch
        {
            int i => i,
            // 超范围 unchecked 截断会把超大权重/行号变成负数——显式范围检查
            long l => l is >= int.MinValue and <= int.MaxValue ? (int)l : null,
            double d => d is >= int.MinValue and <= int.MaxValue ? (int)d : null,
            _ => null
        };
    }


    /// <summary>
    /// 将 FrontMatter 转换为字典
    /// </summary>
    private static Dictionary<string, object> ConvertFrontMatterToDict(FrontMatter fm)
    {
        var dict = new Dictionary<string, object>
        {
            ["title"] = fm.Title
        };

        if (fm.Date.HasValue)
            dict["date"] = fm.Date.Value.ToString("yyyy-MM-ddTHH:mm:sszzz", System.Globalization.CultureInfo.InvariantCulture);
        else if (!string.IsNullOrEmpty(fm.DateSource))
            dict["date"] = fm.DateSource; // 特殊日期源（":git" 等）原样写出，往返不丢

        if (fm.LastMod.HasValue)
            dict["lastmod"] = fm.LastMod.Value.ToString("yyyy-MM-ddTHH:mm:sszzz", System.Globalization.CultureInfo.InvariantCulture);
        else if (!string.IsNullOrEmpty(fm.LastModSource))
            dict["lastmod"] = fm.LastModSource;

        if (fm.Draft)
            dict["draft"] = true;

        if (!string.IsNullOrEmpty(fm.Description))
            dict["description"] = fm.Description;

        if (!string.IsNullOrEmpty(fm.Summary))
            dict["summary"] = fm.Summary;

        if (fm.Tags.Count > 0)
            dict["tags"] = fm.Tags.ToList();

        if (fm.Categories.Count > 0)
            dict["categories"] = fm.Categories.ToList();

        if (!string.IsNullOrEmpty(fm.Layout))
            dict["layout"] = fm.Layout;

        if (!string.IsNullOrEmpty(fm.Slug))
            dict["slug"] = fm.Slug;

        if (!string.IsNullOrEmpty(fm.Author))
            dict["author"] = fm.Author;

        if (fm.Weight != 0)
            dict["weight"] = fm.Weight;

        if (fm.ExpiryDate.HasValue)
            dict["expiryDate"] = fm.ExpiryDate.Value.ToString("yyyy-MM-ddTHH:mm:sszzz", System.Globalization.CultureInfo.InvariantCulture);

        if (fm.PublishDate.HasValue)
            dict["publishDate"] = fm.PublishDate.Value.ToString("yyyy-MM-ddTHH:mm:sszzz", System.Globalization.CultureInfo.InvariantCulture);

        if (fm.Aliases.Count > 0)
            dict["aliases"] = fm.Aliases.ToList();

        if (fm.Authors.Count > 0)
            dict["authors"] = fm.Authors.ToList();

        if (fm.Keywords.Count > 0)
            dict["keywords"] = fm.Keywords.ToList();

        if (fm.Outputs.Count > 0)
            dict["outputs"] = fm.Outputs.ToList();

        if (!string.IsNullOrEmpty(fm.Type))
            dict["type"] = fm.Type;

        // menu/cascade 键名与解析端约定一致（menu 单数、cascade._target 嵌套）
        if (fm.Menus is { } menus && menus.Count > 0)
        {
            var menusDict = new Dictionary<string, object>();
            foreach (var (menuKey, entry) in menus)
            {
                var entryDict = new Dictionary<string, object>();
                if (!string.IsNullOrEmpty(entry.Name))
                    entryDict["name"] = entry.Name;
                if (entry.Weight != 0)
                    entryDict["weight"] = entry.Weight;
                if (!string.IsNullOrEmpty(entry.Parent))
                    entryDict["parent"] = entry.Parent;
                if (!string.IsNullOrEmpty(entry.Identifier))
                    entryDict["identifier"] = entry.Identifier;
                menusDict[menuKey] = entryDict;
            }
            dict["menu"] = menusDict;
        }

        if (fm.Cascade is { } cascade)
        {
            var cascadeDict = new Dictionary<string, object>();
            if (cascade.Data is not null)
            {
                foreach (var kvp in ConvertFrontMatterToDict(cascade.Data))
                {
                    cascadeDict[kvp.Key] = kvp.Value;
                }
            }

            if (!string.IsNullOrEmpty(cascade.Target) || !string.IsNullOrEmpty(cascade.Kind))
            {
                var targetDict = new Dictionary<string, object>();
                if (!string.IsNullOrEmpty(cascade.Target))
                    targetDict["path"] = cascade.Target;
                if (!string.IsNullOrEmpty(cascade.Kind))
                    targetDict["kind"] = cascade.Kind;
                cascadeDict["_target"] = targetDict;
            }

            if (cascadeDict.Count > 0)
                dict["cascade"] = cascadeDict;
        }

        if (fm.Params.Count > 0)
        {
            foreach (var kvp in fm.Params)
            {
                dict[kvp.Key] = kvp.Value;
            }
        }

        return dict;
    }

    #endregion

    #region 字典辅助方法

    private static string? GetDictString(Dictionary<string, object> dict, string key)
    {
        return dict.TryGetValue(key, out var value) && value is string s ? s : null;
    }

    private static bool? GetDictBool(Dictionary<string, object> dict, string key)
    {
        if (!dict.TryGetValue(key, out var value))
            return null;
        return value switch
        {
            bool b => b,
            string s => bool.TryParse(s, out var result) ? result : null,
            _ => null
        };
    }

    private static int? GetDictInt(Dictionary<string, object> dict, string key)
    {
        if (!dict.TryGetValue(key, out var value))
            return null;
        return value switch
        {
            int i => i,
            // 超范围 unchecked 截断会静默产出负数（同 ConfigParser.GetDictInt 修复）
            long l => l is >= int.MinValue and <= int.MaxValue ? (int)l : null,
            // YAML "weight: 2.0" 解析为 double，缺分支会落 _ 静默变 0
            double d => d is >= int.MinValue and <= int.MaxValue ? (int)d : null,
            string s => int.TryParse(s, out var result) ? result : null,
            _ => null
        };
    }

    private static DateTimeOffset? GetDictDateTime(Dictionary<string, object> dict, string key, TimeZoneInfo? siteTimeZone)
    {
        return ToDateTimeOffset(dict, key, siteTimeZone);
    }

    /// <summary>
    /// 读取日期键并识别 Hugo 特殊日期源字符串（":git"/":filemodtime"/":filename"，大小写不敏感）。
    /// 特殊源时返回 (null, 源)；普通日期返回 (值, null)；键缺失返回 (null, null)。
    /// </summary>
    private static (DateTimeOffset? Value, string? Source) GetDictDateWithSource(
        Dictionary<string, object> dict, string key, TimeZoneInfo? siteTimeZone)
    {
        if (!dict.TryGetValue(key, out var value))
        {
            return (null, null);
        }

        if (value is string s && s.StartsWith(':'))
        {
            var source = s.ToLowerInvariant() switch
            {
                ":git" or ":filemodtime" or ":filename" => s.ToLowerInvariant(),
                _ => null // 未知源不记录，按缺省处理（与 Hugo 对未知源的宽容一致）
            };
            return (null, source);
        }

        return (ToDateTimeOffset(dict, key, siteTimeZone), null);
    }

    private static DateTimeOffset? ToDateTimeOffset(Dictionary<string, object> dict, string key, TimeZoneInfo? siteTimeZone)
    {
        if (!dict.TryGetValue(key, out var value))
            return null;
        return value switch
        {
            DateTimeOffset dto => dto,
            DateTime dt => ApplyTimeZone(dt, siteTimeZone),
            string s => ParseDateString(s, siteTimeZone),
            _ => null
        };
    }

    private static DateTimeOffset ApplyTimeZone(DateTime dt, TimeZoneInfo? siteTimeZone)
    {
        if (dt.Kind == DateTimeKind.Utc)
        {
            return new DateTimeOffset(dt.Ticks, TimeSpan.Zero);
        }

        // Unspecified/Local：无偏移信息，Hugo 语义为按站点时区解释；未配置站点时区时沿用本机时区（旧行为）
        var offset = siteTimeZone?.GetUtcOffset(dt) ?? TimeZoneInfo.Local.GetUtcOffset(dt);
        return new DateTimeOffset(dt.Ticks, offset);
    }

    private static DateTimeOffset? ParseDateString(string s, TimeZoneInfo? siteTimeZone)
    {
        // 带显式偏移（Z / ±hh:mm / ±hhmm）：偏移是作者意图，不重解释
        if (HasExplicitOffset(s))
        {
            return DateTimeOffset.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, DateTimeStyles.None, out var withOffset)
                ? withOffset
                : null;
        }

        // 无偏移：按站点时区补偏移；未配置时本机时区（与 DateTimeOffset.TryParse 旧行为等价）
        if (DateTime.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, DateTimeStyles.None, out var noOffset))
        {
            var offset = siteTimeZone?.GetUtcOffset(noOffset) ?? TimeZoneInfo.Local.GetUtcOffset(noOffset);
            return new DateTimeOffset(noOffset, offset);
        }

        return null;
    }

    /// <summary>
    /// 判断日期字符串是否带显式 UTC 偏移或 Z 后缀
    /// </summary>
    private static bool HasExplicitOffset(string s)
    {
        if (s.Length == 0)
        {
            return false;
        }

        var last = s[^1];
        if (last is 'Z' or 'z')
        {
            return true;
        }

        // ±hh:mm（末 6 位，第 3 位是冒号）或 ±hhmm（末 5 位）
        if (s.Length >= 6 && s[^6] is '+' or '-' && s[^3] == ':')
        {
            return true;
        }

        return s.Length >= 5 && s[^5] is '+' or '-';
    }

    private static List<string> GetDictStringList(Dictionary<string, object> dict, string key)
    {
        if (!dict.TryGetValue(key, out var value))
            return [];
        return value switch
        {
            IEnumerable<string> list => list.ToList(),
            IEnumerable<object> list => list.Select(o => o?.ToString() ?? "").Where(s => !string.IsNullOrEmpty(s)).ToList(),
            _ => []
        };
    }

    private static Dictionary<string, object> GetDictParams(Dictionary<string, object> dict)
    {
        // 收集所有非标准字段作为 params
        var standardKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "title", "date", "lastmod", "draft", "expirydate", "publishdate",
            // 日期别名键：解析端 modified/unpublishdate/pubdate/published 均会生效，
            // 不剔除则同一值既进别名字段又泄漏进 params，写回后持久重复
            "modified", "unpublishdate", "pubdate", "published",
            "tags", "categories", "layout", "slug", "aliases", "description",
            "summary", "weight", "author", "authors", "keywords", "type",
            "outputs", "menu", "cascade"
        };

        var result = new Dictionary<string, object>();
        foreach (var kvp in dict)
        {
            if (!standardKeys.Contains(kvp.Key))
            {
                result[kvp.Key] = kvp.Value;
            }
        }
        return result;
    }

    #endregion
}
#pragma warning restore IL2026, IL3050

/// <summary>
/// Front Matter JSON 写回的 source-gen 上下文：值域与 ConfigJsonContext 对齐
/// （标量/List/嵌套 dict/DateTimeOffset）。替代 ConvertFrontMatterToDict 路径上
/// 最后一个反射式 JSON 序列化点——native AOT 下反射序列化 object 值运行时失败。
/// </summary>
[System.Text.Json.Serialization.JsonSourceGenerationOptions(WriteIndented = true)]
[System.Text.Json.Serialization.JsonSerializable(typeof(Dictionary<string, object>))]
[System.Text.Json.Serialization.JsonSerializable(typeof(List<object>))]
[System.Text.Json.Serialization.JsonSerializable(typeof(List<string>))]
[System.Text.Json.Serialization.JsonSerializable(typeof(string))]
[System.Text.Json.Serialization.JsonSerializable(typeof(bool))]
[System.Text.Json.Serialization.JsonSerializable(typeof(long))]
[System.Text.Json.Serialization.JsonSerializable(typeof(int))]
[System.Text.Json.Serialization.JsonSerializable(typeof(double))]
[System.Text.Json.Serialization.JsonSerializable(typeof(float))]
[System.Text.Json.Serialization.JsonSerializable(typeof(decimal))]
[System.Text.Json.Serialization.JsonSerializable(typeof(DateTimeOffset))]
internal sealed partial class FrontMatterJsonContext : System.Text.Json.Serialization.JsonSerializerContext
{
}
