// Flint 静态站点生成器
// 配置验证器实现

using System.Text.RegularExpressions;
using Flint.Core.Abstractions;

namespace Flint.Core.Configuration;

/// <summary>
/// 配置验证器 - 验证站点配置的有效性
/// </summary>
public sealed partial class ConfigValidator : IConfigValidator
{
    // URL 验证正则表达式
    [GeneratedRegex(@"^https?://[^\s/$.?#].[^\s]*$", RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegex();

    // 语言代码验证正则表达式（BCP 47：language[-Script][-Region]），
    // IgnoreCase：RFC 5646 规定语言标签大小写不敏感（en-US 与 en-us 等价），
    // 旧版 ^[a-z]{2}(-[A-Z]{2})?$ 会误杀 zh-Hans、zh-hans-cn 等合法标签
    [GeneratedRegex(@"^[a-zA-Z]{2,3}(-[A-Z][a-z]{3})?(-([A-Z]{2}|\d{3}))?$", RegexOptions.IgnoreCase)]
    private static partial Regex LanguageCodeRegex();

    // 路径验证正则表达式（不允许绝对路径或父目录引用）
    [GeneratedRegex(@"^(?!/)(?!.*\.\.)[\w\-./]+$", RegexOptions.None)]
    private static partial Regex SafePathRegex();

    /// <inheritdoc />
    public ValidationResult Validate(SiteConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var errors = new List<ValidationError>();

        // 验证必填字段
        ValidateRequired(errors, config);

        // 验证 URL 格式
        ValidateUrls(errors, config);

        // 验证数值范围
        ValidateNumericRanges(errors, config);

        // 验证路径安全性
        ValidatePaths(errors, config);

        // 验证语言代码
        ValidateLanguageCode(errors, config);

        return errors.Count == 0
            ? ValidationResult.Success
            : new ValidationResult { IsValid = false, Errors = errors };
    }

    /// <summary>
    /// 验证必填字段
    /// </summary>
    private static void ValidateRequired(List<ValidationError> errors, SiteConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.BaseURL))
        {
            errors.Add(new ValidationError
            {
                PropertyPath = "baseURL",
                Message = "基础 URL 不能为空",
                ErrorCode = "CFG001",
                ExpectedValue = "有效的 URL",
                ActualValue = config.BaseURL
            });
        }

        if (string.IsNullOrWhiteSpace(config.Title))
        {
            errors.Add(new ValidationError
            {
                PropertyPath = "title",
                Message = "站点标题不能为空",
                ErrorCode = "CFG002",
                ExpectedValue = "非空字符串",
                ActualValue = config.Title
            });
        }
    }

    /// <summary>
    /// 验证 URL 格式
    /// </summary>
    private static void ValidateUrls(List<ValidationError> errors, SiteConfig config)
    {
        if (!string.IsNullOrWhiteSpace(config.BaseURL) && !UrlRegex().IsMatch(config.BaseURL))
        {
            errors.Add(new ValidationError
            {
                PropertyPath = "baseURL",
                Message = "基础 URL 格式无效，必须以 http:// 或 https:// 开头",
                ErrorCode = "CFG003",
                ExpectedValue = "http(s)://example.com/",
                ActualValue = config.BaseURL
            });
        }

        // 验证作者 URL
        if (config.Author?.URL is not null && !string.IsNullOrWhiteSpace(config.Author.URL))
        {
            if (!UrlRegex().IsMatch(config.Author.URL))
            {
                errors.Add(new ValidationError
                {
                    PropertyPath = "author.url",
                    Message = "作者 URL 格式无效",
                    ErrorCode = "CFG003",
                    ExpectedValue = "http(s)://example.com/",
                    ActualValue = config.Author.URL
                });
            }
        }
    }

    /// <summary>
    /// 验证数值范围
    /// </summary>
    private static void ValidateNumericRanges(List<ValidationError> errors, SiteConfig config)
    {
        if (config.Paginate < 1 || config.Paginate > 1000)
        {
            errors.Add(new ValidationError
            {
                PropertyPath = "paginate",
                Message = "分页数量必须在 1-1000 之间",
                ErrorCode = "CFG004",
                ExpectedValue = "1-1000",
                ActualValue = config.Paginate.ToString(System.Globalization.CultureInfo.InvariantCulture)
            });
        }

        if (config.SummaryLength < 0 || config.SummaryLength > 1000)
        {
            errors.Add(new ValidationError
            {
                PropertyPath = "summaryLength",
                Message = "摘要长度必须在 0-1000 之间",
                ErrorCode = "CFG004",
                ExpectedValue = "0-1000",
                ActualValue = config.SummaryLength.ToString(System.Globalization.CultureInfo.InvariantCulture)
            });
        }

        if (config.Security.HttpTimeout < 1 || config.Security.HttpTimeout > 300)
        {
            errors.Add(new ValidationError
            {
                PropertyPath = "security.httpTimeout",
                Message = "HTTP 超时时间必须在 1-300 秒之间",
                ErrorCode = "CFG004",
                ExpectedValue = "1-300",
                ActualValue = config.Security.HttpTimeout.ToString(System.Globalization.CultureInfo.InvariantCulture)
            });
        }

        if (config.Caches.MaxSize < 1 || config.Caches.MaxSize > 10000)
        {
            errors.Add(new ValidationError
            {
                PropertyPath = "caches.maxSize",
                Message = "缓存大小必须在 1-10000 MB 之间",
                ErrorCode = "CFG004",
                ExpectedValue = "1-10000",
                ActualValue = config.Caches.MaxSize.ToString(System.Globalization.CultureInfo.InvariantCulture)
            });
        }

        // 验证 Markup 配置
        if (config.Markup.TableOfContents.StartLevel < 1 || config.Markup.TableOfContents.StartLevel > 6)
        {
            errors.Add(new ValidationError
            {
                PropertyPath = "markup.tableOfContents.startLevel",
                Message = "目录起始级别必须在 1-6 之间",
                ErrorCode = "CFG004",
                ExpectedValue = "1-6",
                ActualValue = config.Markup.TableOfContents.StartLevel.ToString(System.Globalization.CultureInfo.InvariantCulture)
            });
        }

        if (config.Markup.TableOfContents.EndLevel < 1 || config.Markup.TableOfContents.EndLevel > 6)
        {
            errors.Add(new ValidationError
            {
                PropertyPath = "markup.tableOfContents.endLevel",
                Message = "目录结束级别必须在 1-6 之间",
                ErrorCode = "CFG004",
                ExpectedValue = "1-6",
                ActualValue = config.Markup.TableOfContents.EndLevel.ToString(System.Globalization.CultureInfo.InvariantCulture)
            });
        }

        if (config.Markup.TableOfContents.StartLevel > config.Markup.TableOfContents.EndLevel)
        {
            errors.Add(new ValidationError
            {
                PropertyPath = "markup.tableOfContents",
                Message = "目录起始级别不能大于结束级别",
                ErrorCode = "CFG005",
                ExpectedValue = $"startLevel <= endLevel",
                ActualValue = $"startLevel={config.Markup.TableOfContents.StartLevel}, endLevel={config.Markup.TableOfContents.EndLevel}"
            });
        }
    }

    /// <summary>
    /// 验证路径安全性
    /// </summary>
    private static void ValidatePaths(List<ValidationError> errors, SiteConfig config)
    {
        ValidatePath(errors, "contentDir", config.ContentDir);
        ValidatePath(errors, "layoutDir", config.LayoutDir);
        ValidatePath(errors, "staticDir", config.StaticDir);
        ValidatePath(errors, "assetDir", config.AssetDir);
        ValidatePath(errors, "dataDir", config.DataDir);
        ValidatePath(errors, "publishDir", config.PublishDir);
        ValidatePath(errors, "archetypeDir", config.ArchetypeDir);

        if (!string.IsNullOrEmpty(config.Caches.Dir))
        {
            ValidatePath(errors, "caches.dir", config.Caches.Dir);
        }
    }

    private static void ValidatePath(List<ValidationError> errors, string propertyPath, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            errors.Add(new ValidationError
            {
                PropertyPath = propertyPath,
                Message = "路径不能为空",
                ErrorCode = "CFG006",
                ExpectedValue = "有效的相对路径",
                ActualValue = path
            });
            return;
        }

        // 检查是否为绝对路径
        if (Path.IsPathRooted(path))
        {
            errors.Add(new ValidationError
            {
                PropertyPath = propertyPath,
                Message = "路径必须是相对路径，不能是绝对路径",
                ErrorCode = "CFG007",
                ExpectedValue = "相对路径",
                ActualValue = path
            });
            return;
        }

        // 检查是否包含父目录引用
        if (path.Contains("..", StringComparison.Ordinal))
        {
            errors.Add(new ValidationError
            {
                PropertyPath = propertyPath,
                Message = "路径不能包含父目录引用 (..)",
                ErrorCode = "CFG008",
                ExpectedValue = "不包含 .. 的路径",
                ActualValue = path
            });
        }
    }

    /// <summary>
    /// 验证语言代码
    /// </summary>
    private static void ValidateLanguageCode(List<ValidationError> errors, SiteConfig config)
    {
        if (!string.IsNullOrWhiteSpace(config.LanguageCode) && !LanguageCodeRegex().IsMatch(config.LanguageCode))
        {
            errors.Add(new ValidationError
            {
                PropertyPath = "languageCode",
                Message = "语言代码格式无效，应为 ISO 639-1 格式（如 en、zh）或 BCP 47 格式（如 en-US、zh-CN）",
                ErrorCode = "CFG009",
                ExpectedValue = "en, zh, en-US, zh-CN 等",
                ActualValue = config.LanguageCode
            });
        }

        // 验证多语言配置中的语言代码
        foreach (var lang in config.Languages)
        {
            if (!LanguageCodeRegex().IsMatch(lang.Key))
            {
                errors.Add(new ValidationError
                {
                    PropertyPath = $"languages.{lang.Key}",
                    Message = $"语言代码 '{lang.Key}' 格式无效",
                    ErrorCode = "CFG009",
                    ExpectedValue = "en, zh, en-US, zh-CN 等",
                    ActualValue = lang.Key
                });
            }
        }
    }
}
