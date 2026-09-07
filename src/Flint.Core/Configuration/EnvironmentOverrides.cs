// Flint 静态站点生成器
// 环境变量覆盖处理器

using Flint.Core.Abstractions;

namespace Flint.Core.Configuration;

/// <summary>
/// 环境变量覆盖处理器
/// 支持通过环境变量覆盖配置文件中的值
/// </summary>
/// <remarks>
/// 环境变量命名规则：
/// - 前缀: Flint_
/// - 嵌套属性使用双下划线分隔: Flint_SECURITY__HTTPTIMEOUT
/// - 属性名转换为大写: Flint_BASEURL
/// </remarks>
public static class EnvironmentOverrides
{
    /// <summary>
    /// 环境变量前缀
    /// </summary>
    public const string Prefix = "Flint_";

    /// <summary>
    /// 嵌套属性分隔符
    /// </summary>
    public const string Separator = "__";

    /// <summary>
    /// 应用环境变量覆盖到配置
    /// </summary>
    /// <param name="config">原始配置</param>
    /// <returns>应用覆盖后的新配置</returns>
    public static SiteConfig ApplyOverrides(SiteConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        // 获取所有 Flint_ 开头的环境变量；键统一大写规范化，
        // 否则 Linux 上 flint_baseurl 被收集后查询 BASEURL（区分大小写）永不命中
        var envVars = Environment.GetEnvironmentVariables()
            .Cast<System.Collections.DictionaryEntry>()
            .Where(e => e.Key is string key && key.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            .GroupBy(e => ((string)e.Key)[Prefix.Length..].ToUpperInvariant())
            .ToDictionary(
                g => g.Key,
                g => g.First().Value?.ToString() ?? "");

        if (envVars.Count == 0)
        {
            return config;
        }

        return ApplyOverridesToConfig(config, envVars);
    }

    /// <summary>
    /// 应用环境变量覆盖到配置对象
    /// </summary>
    private static SiteConfig ApplyOverridesToConfig(SiteConfig config, Dictionary<string, string> envVars)
    {
        // 基本属性覆盖
        var baseUrl = GetEnvValue(envVars, "BASEURL") ?? config.BaseURL;
        var title = GetEnvValue(envVars, "TITLE") ?? config.Title;
        var languageCode = GetEnvValue(envVars, "LANGUAGECODE") ?? config.LanguageCode;
        var theme = GetEnvValue(envVars, "THEME") ?? config.Theme;
        var buildDrafts = GetEnvBool(envVars, "BUILDDRAFTS") ?? config.BuildDrafts;
        var buildFuture = GetEnvBool(envVars, "BUILDFUTURE") ?? config.BuildFuture;
        var buildExpired = GetEnvBool(envVars, "BUILDEXPIRED") ?? config.BuildExpired;
        var paginate = GetEnvInt(envVars, "PAGINATE") ?? config.Paginate;
        var paginatePath = GetEnvValue(envVars, "PAGINATEPATH") ?? config.PaginatePath;
        var enableGitInfo = GetEnvBool(envVars, "ENABLEGITINFO") ?? config.EnableGitInfo;
        var summaryLength = GetEnvInt(envVars, "SUMMARYLENGTH") ?? config.SummaryLength;
        var copyright = GetEnvValue(envVars, "COPYRIGHT") ?? config.Copyright;

        // 目录配置覆盖
        var contentDir = GetEnvValue(envVars, "CONTENTDIR") ?? config.ContentDir;
        var layoutDir = GetEnvValue(envVars, "LAYOUTDIR") ?? config.LayoutDir;
        var staticDir = GetEnvValue(envVars, "STATICDIR") ?? config.StaticDir;
        var assetDir = GetEnvValue(envVars, "ASSETDIR") ?? config.AssetDir;
        var dataDir = GetEnvValue(envVars, "DATADIR") ?? config.DataDir;
        var publishDir = GetEnvValue(envVars, "PUBLISHDIR") ?? config.PublishDir;
        var archetypeDir = GetEnvValue(envVars, "ARCHETYPEDIR") ?? config.ArchetypeDir;

        // 安全配置覆盖
        var security = ApplySecurityOverrides(config.Security, envVars);

        // 缓存配置覆盖
        var caches = ApplyCacheOverrides(config.Caches, envVars);

        return new SiteConfig
        {
            BaseURL = baseUrl,
            Title = title,
            LanguageCode = languageCode,
            Theme = theme,
            BuildDrafts = buildDrafts,
            BuildFuture = buildFuture,
            BuildExpired = buildExpired,
            Paginate = paginate,
            PaginatePath = paginatePath,
            EnableGitInfo = enableGitInfo,
            TimeZone = config.TimeZone,
            SummaryLength = summaryLength,
            Copyright = copyright,
            DisablePathToLower = config.DisablePathToLower,
            DisableKinds = config.DisableKinds,
            ContentDir = contentDir,
            LayoutDir = layoutDir,
            StaticDir = staticDir,
            AssetDir = assetDir,
            DataDir = dataDir,
            PublishDir = publishDir,
            ArchetypeDir = archetypeDir,
            Permalinks = config.Permalinks,
            Taxonomies = config.Taxonomies,
            Menus = config.Menus,
            Params = config.Params,
            Markup = config.Markup,
            Outputs = config.Outputs,
            Languages = config.Languages,
            Module = config.Module,
            Security = security,
            Caches = caches,
            Author = config.Author
        };
    }

    /// <summary>
    /// 应用安全配置覆盖
    /// </summary>
    private static SecurityConfig ApplySecurityOverrides(SecurityConfig config, Dictionary<string, string> envVars)
    {
        var httpTimeout = GetEnvInt(envVars, "SECURITY__HTTPTIMEOUT") ?? config.HttpTimeout;
        var allowedDomains = GetEnvList(envVars, "SECURITY__ALLOWEDDOMAINS") ?? config.AllowedDomains;
        var allowedCommands = GetEnvList(envVars, "SECURITY__ALLOWEDCOMMANDS") ?? config.AllowedCommands;

        return new SecurityConfig
        {
            HttpTimeout = httpTimeout,
            AllowedDomains = allowedDomains,
            AllowedCommands = allowedCommands
        };
    }

    /// <summary>
    /// 应用缓存配置覆盖
    /// </summary>
    private static CacheConfig ApplyCacheOverrides(CacheConfig config, Dictionary<string, string> envVars)
    {
        var enabled = GetEnvBool(envVars, "CACHES__ENABLED") ?? config.Enabled;
        var dir = GetEnvValue(envVars, "CACHES__DIR") ?? config.Dir;
        var maxSize = GetEnvInt(envVars, "CACHES__MAXSIZE") ?? config.MaxSize;

        return new CacheConfig
        {
            Enabled = enabled,
            Dir = dir,
            MaxSize = maxSize
        };
    }

    /// <summary>
    /// 获取环境变量字符串值
    /// </summary>
    private static string? GetEnvValue(Dictionary<string, string> envVars, string key)
    {
        return envVars.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value) ? value : null;
    }

    /// <summary>
    /// 获取环境变量布尔值
    /// </summary>
    private static bool? GetEnvBool(Dictionary<string, string> envVars, string key)
    {
        if (!envVars.TryGetValue(key, out var value) || string.IsNullOrEmpty(value))
            return null;

        return value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("1", StringComparison.Ordinal) ||
               value.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 获取环境变量整数值
    /// </summary>
    private static int? GetEnvInt(Dictionary<string, string> envVars, string key)
    {
        if (!envVars.TryGetValue(key, out var value) || string.IsNullOrEmpty(value))
            return null;

        return int.TryParse(value, out var result) ? result : null;
    }

    /// <summary>
    /// 获取环境变量列表值（逗号分隔）
    /// </summary>
    private static string[]? GetEnvList(Dictionary<string, string> envVars, string key)
    {
        if (!envVars.TryGetValue(key, out var value) || string.IsNullOrEmpty(value))
            return null;

        return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
