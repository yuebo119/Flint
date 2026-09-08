// Flint 静态站点生成器
// Front Matter 解析器——写回部分：三路序列化与 TOML 标量写出辅助
// （从 FrontMatterParser.cs 按 partial 拆出）

using System.Text.Json;
using Flint.Core.Abstractions;
using Flint.Core.Configuration;
using Flint.Core.Models;

namespace Flint.Core.Content;

/// <summary>
/// Front Matter 解析器——写回部分
/// （主文件：FrontMatterParser.cs，含类声明与 DetectFormat 公开方法）
/// </summary>
#pragma warning disable IL2026, IL3050 // AOT 警告在此类中被抑制
public sealed partial class FrontMatterParser
{
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

    /// <summary>
    /// 序列化为 YAML Front Matter
    /// </summary>
    private static string SerializeYaml(FrontMatter frontMatter)
    {
        var dict = ConvertFrontMatterToDict(frontMatter);
        var yaml = SharedYaml.Serializer.Serialize(dict);
        return $"{YamlDelimiter}\n{yaml}{YamlDelimiter}\n";
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
                sb.Append(culture, $"[menu.{TomlSyntax.EscapeBareKey(menuKey)}]\n");
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
        var escapedKey = TomlSyntax.EscapeBareKey(key);
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
