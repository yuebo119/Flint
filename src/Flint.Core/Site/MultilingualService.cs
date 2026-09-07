// Flint 静态站点生成器
// 多语言支持服务实现

using System.Text.RegularExpressions;

namespace Flint.Core.Site;

/// <summary>
/// 多语言服务
/// 管理多语言站点的 URL 结构和语言切换
/// </summary>
public sealed partial class MultilingualService
{
    private readonly LanguageConfig _defaultLanguage;
    private readonly Dictionary<string, LanguageConfig> _languages;
    private readonly MultilingualUrlStrategy _urlStrategy;

    /// <summary>
    /// 创建多语言服务
    /// </summary>
    /// <param name="languages">语言配置列表</param>
    /// <param name="defaultLanguageCode">默认语言代码</param>
    /// <param name="urlStrategy">URL 策略</param>
    public MultilingualService(
        IEnumerable<LanguageConfig> languages,
        string defaultLanguageCode = "en",
        MultilingualUrlStrategy urlStrategy = MultilingualUrlStrategy.PathPrefix)
    {
        _languages = languages.ToDictionary(l => l.Code, StringComparer.OrdinalIgnoreCase);
        _urlStrategy = urlStrategy;

        if (!_languages.TryGetValue(defaultLanguageCode, out var defaultLang))
        {
            defaultLang = new LanguageConfig
            {
                Code = defaultLanguageCode,
                Name = defaultLanguageCode,
                Weight = 0
            };
            _languages[defaultLanguageCode] = defaultLang;
        }
        _defaultLanguage = defaultLang;
    }

    /// <summary>
    /// 获取默认语言
    /// </summary>
    public LanguageConfig DefaultLanguage => _defaultLanguage;

    /// <summary>
    /// 获取所有配置的语言
    /// </summary>
    public IReadOnlyList<LanguageConfig> Languages =>
        [.. _languages.Values.OrderBy(l => l.Weight)];

    /// <summary>
    /// 是否为多语言站点
    /// </summary>
    public bool IsMultilingual => _languages.Count > 1;

    /// <summary>
    /// 获取指定语言配置
    /// </summary>
    public LanguageConfig? GetLanguage(string code)
    {
        return _languages.TryGetValue(code, out var lang) ? lang : null;
    }

    /// <summary>
    /// 生成多语言 URL
    /// </summary>
    /// <param name="path">原始路径</param>
    /// <param name="languageCode">语言代码</param>
    /// <returns>带语言标识的 URL</returns>
    public string GenerateUrl(string path, string languageCode)
    {
        if (!_languages.ContainsKey(languageCode))
        {
            return path;
        }

        var isDefault = string.Equals(languageCode, _defaultLanguage.Code, StringComparison.OrdinalIgnoreCase);

        return _urlStrategy switch
        {
            MultilingualUrlStrategy.PathPrefix => GeneratePathPrefixUrl(path, languageCode, isDefault),
            MultilingualUrlStrategy.Subdomain => GenerateSubdomainUrl(path, languageCode),
            MultilingualUrlStrategy.QueryParameter => GenerateQueryParameterUrl(path, languageCode),
            _ => path
        };
    }

    /// <summary>
    /// 生成路径前缀 URL
    /// </summary>
    private string GeneratePathPrefixUrl(string path, string languageCode, bool isDefault)
    {
        // 默认语言不添加前缀（可配置）
        if (isDefault && !_defaultLanguage.IncludeInUrl)
        {
            return NormalizePath(path);
        }

        path = NormalizePath(path);

        // 移除已有的语言前缀
        path = RemoveLanguagePrefix(path);

        return $"/{languageCode}{path}";
    }

    /// <summary>
    /// 生成子域名 URL
    /// </summary>
    private static string GenerateSubdomainUrl(string path, string languageCode)
    {
        // 子域名策略需要完整的 URL，这里只返回路径部分
        // 实际的子域名处理在站点构建时完成
        return $"{languageCode}:{NormalizePath(path)}";
    }

    /// <summary>
    /// 生成查询参数 URL
    /// </summary>
    private static string GenerateQueryParameterUrl(string path, string languageCode)
    {
        path = NormalizePath(path);
        var separator = path.Contains('?') ? "&" : "?";
        return $"{path}{separator}lang={languageCode}";
    }

    /// <summary>
    /// 从路径中提取语言代码
    /// </summary>
    public string? ExtractLanguageCode(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        path = path.TrimStart('/');
        var firstSegment = path.Split('/').FirstOrDefault();

        if (!string.IsNullOrEmpty(firstSegment) && _languages.ContainsKey(firstSegment))
        {
            return firstSegment;
        }

        return null;
    }

    /// <summary>
    /// 移除路径中的语言前缀
    /// </summary>
    public string RemoveLanguagePrefix(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return "/";
        }

        var langCode = ExtractLanguageCode(path);
        if (langCode == null)
        {
            return path;
        }

        path = path.TrimStart('/');
        if (path.StartsWith(langCode, StringComparison.OrdinalIgnoreCase))
        {
            path = path[langCode.Length..].TrimStart('/');
        }

        return "/" + path;
    }

    /// <summary>
    /// 生成语言切换链接
    /// </summary>
    /// <param name="currentPath">当前路径</param>
    /// <param name="currentLanguage">当前语言</param>
    /// <returns>所有语言的切换链接</returns>
    public IReadOnlyList<LanguageSwitchLink> GenerateLanguageSwitchLinks(
        string currentPath,
        string currentLanguage)
    {
        var links = new List<LanguageSwitchLink>();
        var basePath = RemoveLanguagePrefix(currentPath);

        foreach (var lang in Languages)
        {
            var url = GenerateUrl(basePath, lang.Code);
            links.Add(new LanguageSwitchLink
            {
                LanguageCode = lang.Code,
                LanguageName = lang.Name,
                NativeName = lang.NativeName ?? lang.Name,
                Url = url,
                IsCurrent = string.Equals(lang.Code, currentLanguage, StringComparison.OrdinalIgnoreCase),
                IsDefault = string.Equals(lang.Code, _defaultLanguage.Code, StringComparison.OrdinalIgnoreCase)
            });
        }

        return links;
    }

    /// <summary>
    /// 获取内容的翻译版本路径
    /// </summary>
    /// <param name="contentPath">内容文件路径</param>
    /// <returns>所有语言版本的路径</returns>
    public IReadOnlyDictionary<string, string> GetTranslationPaths(string contentPath)
    {
        var result = new Dictionary<string, string>();
        var basePath = GetBaseContentPath(contentPath);
        var extension = Path.GetExtension(contentPath);

        foreach (var lang in _languages.Keys)
        {
            var isDefault = string.Equals(lang, _defaultLanguage.Code, StringComparison.OrdinalIgnoreCase);

            if (isDefault)
            {
                result[lang] = basePath + extension;
            }
            else
            {
                result[lang] = $"{basePath}.{lang}{extension}";
            }
        }

        return result;
    }

    /// <summary>
    /// 从文件名中提取语言代码
    /// </summary>
    public string? ExtractLanguageFromFileName(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            return null;
        }

        var match = LanguageInFileNameRegex().Match(fileName);
        if (match.Success)
        {
            var langCode = match.Groups[1].Value;
            if (_languages.ContainsKey(langCode))
            {
                return langCode;
            }
        }

        return null;
    }

    /// <summary>
    /// 获取基础内容路径（不含语言后缀）
    /// </summary>
    private string GetBaseContentPath(string contentPath)
    {
        var directory = Path.GetDirectoryName(contentPath) ?? "";
        var fileName = Path.GetFileNameWithoutExtension(contentPath);

        // 移除语言后缀（如 .zh, .en）
        var match = LanguageInFileNameRegex().Match(fileName);
        if (match.Success && _languages.ContainsKey(match.Groups[1].Value))
        {
            fileName = fileName[..^(match.Groups[1].Value.Length + 1)];
        }

        return string.IsNullOrEmpty(directory)
            ? fileName
            : Path.Combine(directory, fileName);
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return "/";
        }

        path = path.Trim();
        if (!path.StartsWith('/'))
        {
            path = "/" + path;
        }

        return path;
    }

    [GeneratedRegex(@"\.([a-z]{2}(?:-[A-Z]{2})?)$", RegexOptions.IgnoreCase)]
    private static partial Regex LanguageInFileNameRegex();
}

/// <summary>
/// 语言配置
/// </summary>
public sealed class LanguageConfig
{
    /// <summary>
    /// 语言代码（如 en, zh, ja）
    /// </summary>
    public required string Code { get; init; }

    /// <summary>
    /// 语言名称（英文）
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// 语言本地名称（如 中文, 日本語）
    /// </summary>
    public string? NativeName { get; init; }

    /// <summary>
    /// 排序权重
    /// </summary>
    public int Weight { get; init; }

    /// <summary>
    /// 是否在 URL 中包含语言代码
    /// </summary>
    public bool IncludeInUrl { get; init; }

    /// <summary>
    /// 语言方向（ltr 或 rtl）
    /// </summary>
    public string Direction { get; init; } = "ltr";

    /// <summary>
    /// 日期格式
    /// </summary>
    public string? DateFormat { get; init; }

    /// <summary>
    /// 时间格式
    /// </summary>
    public string? TimeFormat { get; init; }
}

/// <summary>
/// 多语言 URL 策略
/// </summary>
public enum MultilingualUrlStrategy
{
    /// <summary>
    /// 路径前缀（如 /zh/about/）
    /// </summary>
    PathPrefix,

    /// <summary>
    /// 子域名（如 zh.example.com）
    /// </summary>
    Subdomain,

    /// <summary>
    /// 查询参数（如 /about/?lang=zh）
    /// </summary>
    QueryParameter
}

/// <summary>
/// 语言切换链接
/// </summary>
public sealed class LanguageSwitchLink
{
    /// <summary>
    /// 语言代码
    /// </summary>
    public required string LanguageCode { get; init; }

    /// <summary>
    /// 语言名称
    /// </summary>
    public required string LanguageName { get; init; }

    /// <summary>
    /// 语言本地名称
    /// </summary>
    public required string NativeName { get; init; }

    /// <summary>
    /// 链接 URL
    /// </summary>
    public required string Url { get; init; }

    /// <summary>
    /// 是否为当前语言
    /// </summary>
    public required bool IsCurrent { get; init; }

    /// <summary>
    /// 是否为默认语言
    /// </summary>
    public required bool IsDefault { get; init; }
}

/// <summary>
/// 翻译内容信息
/// </summary>
public sealed class TranslationInfo
{
    /// <summary>
    /// 语言代码
    /// </summary>
    public required string LanguageCode { get; init; }

    /// <summary>
    /// 内容路径
    /// </summary>
    public required string ContentPath { get; init; }

    /// <summary>
    /// 永久链接
    /// </summary>
    public required string Permalink { get; init; }

    /// <summary>
    /// 是否存在翻译
    /// </summary>
    public required bool Exists { get; init; }
}
