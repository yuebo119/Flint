// Flint 静态站点生成器
// 主题默认参数合并（主题系统 P1-2）：主题 theme.toml 的 [params] 段作为默认值，
// 站点配置深覆盖——主题"开箱默认 + 站点定制"语义（对齐 Hugo 主题 params 继承）。
// 合并语义：站点键保留站点值；仅主题有的键采用主题值；双方均为嵌套表时递归合并

using Tomlyn;
using Tomlyn.Model;

namespace Flint.Core.Configuration;

public static class ThemeParamsMerger
{
    /// <summary>
    /// 以主题 theme.toml 的 [params] 为默认值合并进站点 params；
    /// 主题未安装/无 params 段/解析失败时返回原 config（合并是尽力而为，不阻断构建）
    /// </summary>
    public static SiteConfig Merge(SiteConfig config, string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(config.Theme))
        {
            return config;
        }

        var themeToml = Path.Combine(sourcePath, "themes", config.Theme, "theme.toml");
        if (!File.Exists(themeToml))
        {
            return config;
        }

        var table = TomlynCompat.TryParseTable(File.ReadAllText(themeToml));
        if (table is null || !table.TryGetValue("params", out var raw) || raw is not TomlTable themeParams)
        {
            return config;
        }

        var themeDefaults = ToPlainDictionary(themeParams);
        var merged = DeepMerge(ToMutable(config.Params), themeDefaults);
        return RebuildWithParams(config, merged);
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
