// Flint 静态站点生成器
// 主题默认参数合并（主题系统 P1-2 + A1）：
//   ① theme.toml 的 [params] 段（旧式元数据形态）
//   ② config/_default/params.{toml,yaml,json}（Hugo 标准形态，优先级更高）
// 两者作为默认值层，站点配置深覆盖——主题"开箱默认 + 站点定制"语义。
// 合并语义：站点键保留站点值；仅主题有的键采用主题值；双方均为嵌套表时递归合并

using Tomlyn;
using Tomlyn.Model;

namespace Flint.Core.Configuration;

public static class ThemeParamsMerger
{
    /// <summary>
    /// 主题默认 params 合并进站点（config/_default/params.* 优先于 theme.toml 的 [params]）；
    /// 主题未安装/无 params/解析失败时返回原 config（合并是尽力而为，不阻断构建）
    /// </summary>
    public static SiteConfig Merge(SiteConfig config, string sourcePath)
    {
        var themeNames = config.ThemeNames;
        if (themeNames.Count == 0)
        {
            return config;
        }

        // 多主题 defaults 层叠：从最后面的主题往前合并 defaults（前面的覆盖后面的），
        // 最终站点 DeepMerge 覆盖全部主题默认——对齐 Hugo theme 数组优先级
        var layered = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var themeName in themeNames.Reverse())
        {
            var themeRoot = Path.Combine(sourcePath, "themes", themeName);

            // ① theme.toml 的 [params] 段（旧式元数据形态）
            var themeToml = Path.Combine(themeRoot, "theme.toml");
            if (File.Exists(themeToml))
            {
                var table = TryReadTomlTable(themeToml);
                if (table is not null && table.TryGetValue("params", out var raw) && raw is TomlTable themeParams)
                {
                    layered = DeepMerge(ToPlainDictionary(themeParams), layered);
                }
            }

            // ② config/_default/params.{toml,yaml,json} 顶层（Hugo 标准形态，优先于旧式）
            var configParams = ReadThemeConfigSection(themeRoot, "params");
            if (configParams is not null)
            {
                layered = DeepMerge(configParams, layered);
            }
        }

        if (layered.Count == 0)
        {
            return config;
        }

        var merged = DeepMerge(ToMutable(config.Params), layered);
        return RebuildWithParams(config, merged);
    }

    /// <summary>
    /// 读取主题 config/_default/&lt;section&gt;.{toml,yaml,json} 的指定段（A1）：
    /// Hugo 标准形态的主题配置目录；TOML/YAML/JSON 三格式，TOML 优先。
    /// 该段须为表（Hugo 中 [params] / [menus] 等），否则返回 null
    /// </summary>
    internal static Dictionary<string, object>? ReadThemeConfigSection(string themeRoot, string section)
    {
        var configDir = Path.Combine(themeRoot, "config", "_default");
        if (!Directory.Exists(configDir))
        {
            return null;
        }

        foreach (var ext in new[] { ".toml", ".yaml", ".yml", ".json" })
        {
            var file = Path.Combine(configDir, section + ext);
            if (!File.Exists(file))
            {
                continue;
            }

            try
            {
                Dictionary<string, object>? dict = ext switch
                {
                    ".toml" => TryReadTomlTable(file) is { } t ? ToPlainDictionary(t) : null,
                    ".json" => ReadJsonDict(file),
                    _ => ReadYamlDict(file)
                };
                if (dict is not null)
                {
                    return dict;
                }
            }
            catch (Exception ex) when (ex is IOException or Tomlyn.TomlException
                or System.Text.Json.JsonException or InvalidOperationException)
            {
                // 主题配置解析失败不阻断构建（尽力而为语义，与 theme.toml 合并一致）
                Console.Error.WriteLine($"[警告] 主题配置解析失败，已跳过: {file} — {ex.Message}");
            }
        }

        return null;
    }

    private static TomlTable? TryReadTomlTable(string file)
    {
        var table = TomlynCompat.TryParseTable(File.ReadAllText(file));
        return table;
    }

    private static Dictionary<string, object>? ReadJsonDict(string file)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(file));
        return doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object
            ? JsonElementToDict(doc.RootElement)
            : null;
    }

    private static Dictionary<string, object> JsonElementToDict(System.Text.Json.JsonElement element)
    {
        var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in element.EnumerateObject())
        {
            dict[prop.Name] = ConvertJsonElement(prop.Value);
        }
        return dict;
    }

    private static object ConvertJsonElement(System.Text.Json.JsonElement element) => element.ValueKind switch
    {
        System.Text.Json.JsonValueKind.Object => JsonElementToDict(element),
        System.Text.Json.JsonValueKind.Array => element.EnumerateArray()
            .Select(ConvertJsonElement).ToArray(),
        System.Text.Json.JsonValueKind.String => element.GetString() ?? "",
        System.Text.Json.JsonValueKind.Number => element.GetDouble(),
        System.Text.Json.JsonValueKind.True => true,
        System.Text.Json.JsonValueKind.False => false,
        _ => ""
    };

    private static Dictionary<string, object>? ReadYamlDict(string file)
    {
        var dict = SharedYaml.Deserializer
            .Deserialize<Dictionary<string, object>>(File.ReadAllText(file));
        return dict is null ? null : NormalizeYamlValue(dict) as Dictionary<string, object>;
    }

    /// <summary>
    /// YamlDotNet 产出非泛型 Dictionary&lt;object,object&gt; 嵌套（运行时类型），
    /// 编译期只见 object —— 归一为字符串键字典（值类型不匹配时原样返回）
    /// </summary>
    private static object NormalizeYamlValue(object value)
    {
        if (value is System.Collections.IDictionary dict)
        {
            var normalized = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (System.Collections.DictionaryEntry entry in dict)
            {
                normalized[entry.Key?.ToString() ?? ""] = NormalizeYamlValue(entry.Value!);
            }
            return normalized;
        }
        if (value is System.Collections.IEnumerable list and not string)
        {
            return list.Cast<object?>().Select(v => NormalizeYamlValue(v!)).ToArray();
        }
        return value ?? "";
    }

    private static Dictionary<string, object> ToMutable(IReadOnlyDictionary<string, object> source)
    {
        var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in source)
        {
            dict[key] = value;
        }
        return dict;
    }

    /// <summary>深合并：defaults 只补缺，overrides 优先；双方同键且均为表时递归</summary>
    internal static Dictionary<string, object> DeepMerge(
        Dictionary<string, object> overrides, Dictionary<string, object> defaults)
    {
        var merged = new Dictionary<string, object>(overrides, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, defaultValue) in defaults)
        {
            if (!merged.TryGetValue(key, out var overrideValue))
            {
                merged[key] = defaultValue;
                continue;
            }
            if (overrideValue is Dictionary<string, object> overrideDict &&
                defaultValue is Dictionary<string, object> defaultDict)
            {
                merged[key] = DeepMerge(overrideDict, defaultDict);
            }
            // 其余形态：站点值优先，主题默认被覆盖（无动作）
        }
        return merged;
    }

    private static Dictionary<string, object> ToPlainDictionary(TomlTable table)
    {
        var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in table)
        {
            dict[key] = ConvertTomlValue(value);
        }
        return dict;
    }

    private static object ConvertTomlValue(object value) => value switch
    {
        TomlTable table => ToPlainDictionary(table),
        TomlArray array => array.Select(item => ConvertTomlValue(item!)).ToArray(),
        _ => value
    };

    /// <summary>
    /// 重建携带合并后 Params 的 SiteConfig。
    /// 同步义务：SiteConfig 新增属性时需与本清单、EnvironmentOverrides 的重建块三处同步
    /// </summary>
    private static SiteConfig RebuildWithParams(SiteConfig config, Dictionary<string, object> params_)
    {
        return new SiteConfig
        {
            BaseURL = config.BaseURL,
            Title = config.Title,
            LanguageCode = config.LanguageCode,
            Theme = config.Theme,
            BuildDrafts = config.BuildDrafts,
            BuildFuture = config.BuildFuture,
            BuildExpired = config.BuildExpired,
            Paginate = config.Paginate,
            PaginatePath = config.PaginatePath,
            EnableGitInfo = config.EnableGitInfo,
            SummaryLength = config.SummaryLength,
            Copyright = config.Copyright,
            TimeZone = config.TimeZone,
            DisablePathToLower = config.DisablePathToLower,
            DisableKinds = config.DisableKinds,
            ContentDir = config.ContentDir,
            LayoutDir = config.LayoutDir,
            StaticDir = config.StaticDir,
            AssetDir = config.AssetDir,
            DataDir = config.DataDir,
            PublishDir = config.PublishDir,
            ArchetypeDir = config.ArchetypeDir,
            Permalinks = config.Permalinks,
            Taxonomies = config.Taxonomies,
            Menus = config.Menus,
            Params = params_,
            Markup = config.Markup,
            Outputs = config.Outputs,
            Languages = config.Languages,
            Module = config.Module,
            Security = config.Security,
            Caches = config.Caches,
            Author = config.Author
        };
    }
}
