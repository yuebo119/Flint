// Flint 静态站点生成器
// i18n 翻译表加载（主题系统 P3）：站点 i18n 优先、主题回退，
// 文件形态三类——扁平键值（hello = "你好"）、Hugo 形态（[hello] other = "你好"）、
// 以及 Hugo 支持的三种文件格式（.toml/.yaml/.json，blowfish 等主题用 YAML）

using Tomlyn;
using Tomlyn.Model;

namespace Flint.Core.Configuration;

public static class Translations
{
    /// <summary>
    /// 加载语言翻译表：主题 i18n 先入、站点 i18n 覆盖（先入者胜的反向——主题先注册、
    /// 站点后注册覆盖）。回退链：精确语言码 → 语言主段（zh-cn → zh）→ 空表
    /// </summary>
    public static IReadOnlyDictionary<string, string> Load(
        string sourcePath, IReadOnlyList<string>? themeNames, string? language)
    {
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(language))
        {
            return merged;
        }

        // 主题在前（低优先），站点在后（覆盖）
        var roots = new List<string>();
        // 主题列表按序：后面的主题先入（低优先），前面的主题后入（覆盖），站点最后
        if (themeNames is not null)
        {
            foreach (var themeName in themeNames.Reverse())
            {
                roots.Add(Path.Combine(sourcePath, "themes", themeName, "i18n"));
            }
        }
        roots.Add(Path.Combine(sourcePath, "i18n"));

        foreach (var candidate in LanguageFileCandidates(language))
        {
            foreach (var root in roots)
            {
                // **同一语言可有多份不同格式的翻译文件**（Hugo 的 i18n 支持
                // .toml/.yaml/.json）：blowfish 的 i18n/en.yaml 此前整份被忽略
                //（只找 .toml）→ 该主题所有 i18n 文案为空
                //（"Skip to main content" 等 16 处实测）
                foreach (var extension in TranslationExtensions)
                {
                    var file = Path.Combine(root, candidate + extension);
                    if (File.Exists(file))
                    {
                        MergeFile(merged, file);
                    }
                }
            }
            // 一旦主段及以上有翻译，不再向更宽的回退展开（精确语言优先于主段）
            if (merged.Count > 0 && candidate.StartsWith(language, StringComparison.Ordinal))
            {
                break;
            }
        }

        return merged;
    }

    /// <summary>语言文件候选：原样码 → 小写码 → 主段（zh-cn → zh）；去重保序（不含扩展名）</summary>
    internal static IEnumerable<string> LanguageFileCandidates(string language)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in new[] { language, language.ToLowerInvariant(), language.Split('-')[0] })
        {
            if (!string.IsNullOrWhiteSpace(candidate) && seen.Add(candidate))
            {
                yield return candidate;
            }
        }
    }

    /// <summary>Hugo 支持的 i18n 文件格式（按优先级）</summary>
    private static readonly string[] TranslationExtensions = [".toml", ".yaml", ".yml", ".json"];

    /// <summary>Hugo 的复数形式键（go-i18n 的 CLDR 分类）</summary>
    private static readonly string[] PluralForms =
        ["zero", "one", "two", "few", "many", "other"];

    private static void MergeFile(Dictionary<string, string> target, string file)
    {
        var text = File.ReadAllText(file);
        var extension = Path.GetExtension(file).ToLowerInvariant();
        var map = extension switch
        {
            ".toml" => Normalize(TomlynCompat.TryParseTable(text)),
            ".yaml" or ".yml" => NormalizeYaml(text),
            ".json" => NormalizeJson(text),
            _ => null
        };
        if (map is null)
        {
            return;
        }

        Flatten(target, "", map);
    }

    /// <summary>YAML 解析（复用站点配置的同一反序列化器；失败返回 null，不影响其它文件）</summary>
    private static Dictionary<string, object?>? NormalizeYaml(string text)
    {
        try
        {
            return Normalize(SharedYaml.Deserializer.Deserialize<Dictionary<string, object>>(text));
        }
        catch (YamlDotNet.Core.YamlException)
        {
            return null;
        }
    }

    /// <summary>JSON 解析（Hugo 的第三种 i18n 文件格式）</summary>
    private static Dictionary<string, object?>? NormalizeJson(string text)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(text);
            return NormalizeJsonElement(document.RootElement) as Dictionary<string, object?>;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static object? NormalizeJsonElement(System.Text.Json.JsonElement element) => element.ValueKind switch
    {
        System.Text.Json.JsonValueKind.Object => element.EnumerateObject().ToDictionary(
            property => property.Name,
            property => NormalizeJsonElement(property.Value),
            StringComparer.Ordinal),
        System.Text.Json.JsonValueKind.Array => element.EnumerateArray().Select(NormalizeJsonElement).ToList(),
        System.Text.Json.JsonValueKind.String => element.GetString(),
        System.Text.Json.JsonValueKind.Number => element.TryGetInt64(out var number) ? number : element.GetDouble(),
        System.Text.Json.JsonValueKind.True => true,
        System.Text.Json.JsonValueKind.False => false,
        _ => null
    };

    /// <summary>
    /// 统一成 <c>Dictionary&lt;string, object?&gt;</c>：TOML 的 <c>TomlTable</c> 与 YamlDotNet
    /// 的 <c>Dictionary&lt;object, object&gt;</c> 都归一，使摊平逻辑只面对一种形态
    /// </summary>
    private static Dictionary<string, object?>? Normalize(object? value)
    {
        switch (value)
        {
            case null:
                return null;
            // TomlTable 只实现**泛型** IDictionary<string,object>（无非泛型 IDictionary），
            // 且用 IEnumerable<KeyValuePair<string,object>> 枚举——显式分支最稳妥
            case TomlTable toml:
            {
                var result = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (var (key, item) in toml)
                {
                    result[key] = item;
                }

                return result;
            }
            case IDictionary<string, object?> typed:
                return new Dictionary<string, object?>(typed, StringComparer.Ordinal);
            case System.Collections.IDictionary dictionary:
            {
                var result = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (System.Collections.DictionaryEntry entry in dictionary)
                {
                    result[entry.Key?.ToString() ?? ""] = entry.Value;
                }

                return result;
            }
            default:
                return null;
        }
    }

    /// <summary>
    /// 递归摊平为**点分键**（`[article.readingTime] one/other` → `article.readingTime.one`
    /// 与 `article.readingTime.other`）。此前只处理**一层**且只认 `other`：
    /// 嵌套子表（主题里普遍存在的 `[section.key]` 形态）整块被丢弃 →
    /// `i18n "article.readingTime" N` 输出空（stack 的阅读时长实测）。
    /// 同时把 `other` 形另存为**主键**，使不带复数的调用（`i18n "key"`）仍能取到值
    /// </summary>
    private static void Flatten(
        Dictionary<string, string> target, string prefix, Dictionary<string, object?> table)
    {
        foreach (var (key, value) in table)
        {
            var path = prefix.Length == 0 ? key : prefix + "." + key;
            switch (value)
            {
                case TomlTable or System.Collections.IDictionary:
                {
                    var nested = Normalize(value);
                    if (nested is null)
                    {
                        break;
                    }

                    Flatten(target, path, nested);
                    var hasPlural = PluralForms.Any(nested.ContainsKey);
                    if (hasPlural)
                    {
                        // 复数键另存主键：`i18n "key"`（无计数）用 other 形（Hugo 探针：
                        // 无计数时选 other，`.Count` 缺失渲染为 `<no value>`）；
                        // 主键取 **other** 形优先
                        var fallback = PluralForms
                            .OrderBy(form => form == "other" ? 0 : 1)
                            .Select(form => nested.TryGetValue(form, out var text) ? text?.ToString() : null)
                            .FirstOrDefault(v => !string.IsNullOrEmpty(v));
                        if (!string.IsNullOrEmpty(fallback))
                        {
                            target[path] = fallback;
                        }
                    }

                    break;
                }
                case System.Collections.IEnumerable when value is not string:
                    break; // 列表值（Hugo 的 i18n 无此形态）：忽略
                default:
                    target[path] = value?.ToString() ?? "";
                    break;
            }
        }
    }
}
