// Flint 静态站点生成器
// 多语言属性测试

using Flint.Core.Site;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace Flint.Core.Tests.Site;

/// <summary>
/// 多语言属性测试
/// **Property 14: 多语言页面生成**
/// **验证: 需求 5.1**
/// </summary>
public class MultilingualPropertyTests
{
    /// <summary>
    /// **Property 14: 每种语言应该生成独立的页面集合**
    /// URL 结构应该包含语言标识
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(MultilingualArbitrary)])]
    public Property EachLanguage_ShouldHaveDistinctUrls(
        ValidLanguageList languages,
        ValidPath path)
    {
        ArgumentNullException.ThrowIfNull(languages);
        ArgumentNullException.ThrowIfNull(path);

        // Arrange
        var service = new MultilingualService(
            languages.Value,
            languages.Value.First().Code);

        // Act - 为每种语言生成 URL
        var urls = languages.Value
            .Select(lang => service.GenerateUrl(path.Value, lang.Code))
            .ToList();

        // Assert - 所有 URL 应该是唯一的（除非只有一种语言）
        if (languages.Value.Count > 1)
        {
            var uniqueUrls = urls.Distinct().Count();
            return (uniqueUrls == urls.Count)
                .ToProperty()
                .Label($"所有语言的 URL 应该唯一: {string.Join(", ", urls)}");
        }

        return true.ToProperty().Label("单语言站点");
    }

    /// <summary>
    /// **Property 14: 非默认语言的 URL 应该包含语言标识**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(MultilingualArbitrary)])]
    public Property NonDefaultLanguage_UrlShouldContainLanguageCode(
        ValidLanguageList languages,
        ValidPath path)
    {
        ArgumentNullException.ThrowIfNull(languages);
        ArgumentNullException.ThrowIfNull(path);

        if (languages.Value.Count <= 1)
        {
            return true.ToProperty().Label("单语言站点跳过");
        }

        // Arrange
        var defaultLang = languages.Value.First();
        var service = new MultilingualService(languages.Value, defaultLang.Code);

        // Act & Assert - 非默认语言的 URL 应该包含语言代码
        foreach (var lang in languages.Value.Skip(1))
        {
            var url = service.GenerateUrl(path.Value, lang.Code);
            if (!url.Contains(lang.Code, StringComparison.OrdinalIgnoreCase))
            {
                return false.ToProperty()
                    .Label($"语言 '{lang.Code}' 的 URL '{url}' 应该包含语言代码");
            }
        }

        return true.ToProperty()
            .Label($"所有非默认语言的 URL 都包含语言代码");
    }

    /// <summary>
    /// **Property 14: 语言切换链接应该覆盖所有语言**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(MultilingualArbitrary)])]
    public Property LanguageSwitchLinks_ShouldCoverAllLanguages(
        ValidLanguageList languages,
        ValidPath path)
    {
        ArgumentNullException.ThrowIfNull(languages);
        ArgumentNullException.ThrowIfNull(path);

        // Arrange
        var defaultLang = languages.Value.First();
        var service = new MultilingualService(languages.Value, defaultLang.Code);

        // Act
        var links = service.GenerateLanguageSwitchLinks(path.Value, defaultLang.Code);

        // Assert
        var allLanguagesCovered = languages.Value.All(lang =>
            links.Any(link => string.Equals(link.LanguageCode, lang.Code, StringComparison.OrdinalIgnoreCase)));

        var exactlyOneCurrentLink = links.Count(l => l.IsCurrent) == 1;

        return (allLanguagesCovered && exactlyOneCurrentLink)
            .ToProperty()
            .Label($"语言切换链接覆盖所有 {languages.Value.Count} 种语言，且只有一个当前语言");
    }

    /// <summary>
    /// **Property 14: 从 URL 提取的语言代码应该正确**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(MultilingualArbitrary)])]
    public Property ExtractedLanguageCode_ShouldMatchGenerated(
        ValidLanguageList languages,
        ValidPath path)
    {
        ArgumentNullException.ThrowIfNull(languages);
        ArgumentNullException.ThrowIfNull(path);

        // Arrange
        var defaultLang = languages.Value.First();
        var service = new MultilingualService(languages.Value, defaultLang.Code);

        // Act & Assert - 对于非默认语言，生成的 URL 应该能正确提取语言代码
        foreach (var lang in languages.Value.Where(l => l.IncludeInUrl || l.Code != defaultLang.Code))
        {
            var url = service.GenerateUrl(path.Value, lang.Code);
            var extracted = service.ExtractLanguageCode(url);

            // 如果是默认语言且不包含在 URL 中，则提取结果为 null
            if (lang.Code == defaultLang.Code && !lang.IncludeInUrl)
            {
                continue;
            }

            if (extracted != lang.Code)
            {
                return false.ToProperty()
                    .Label($"从 URL '{url}' 提取的语言代码 '{extracted}' 应该是 '{lang.Code}'");
            }
        }

        return true.ToProperty()
            .Label("所有语言代码都能正确提取");
    }

    /// <summary>
    /// **Property 14: 移除语言前缀后应该得到原始路径**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(MultilingualArbitrary)])]
    public Property RemoveLanguagePrefix_ShouldReturnBasePath(
        ValidLanguageList languages,
        ValidPath path)
    {
        ArgumentNullException.ThrowIfNull(languages);
        ArgumentNullException.ThrowIfNull(path);

        // Arrange
        var defaultLang = languages.Value.First();
        var service = new MultilingualService(languages.Value, defaultLang.Code);
        var normalizedPath = NormalizePath(path.Value);

        // Act & Assert
        foreach (var lang in languages.Value)
        {
            var url = service.GenerateUrl(path.Value, lang.Code);
            var basePath = service.RemoveLanguagePrefix(url);

            // 基础路径应该与原始路径相同（规范化后）
            if (basePath != normalizedPath)
            {
                return false.ToProperty()
                    .Label($"移除语言前缀后 '{basePath}' 应该等于原始路径 '{normalizedPath}'");
            }
        }

        return true.ToProperty()
            .Label("移除语言前缀后得到正确的基础路径");
    }

    /// <summary>
    /// **Property 14: 翻译路径应该覆盖所有语言**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(MultilingualArbitrary)])]
    public Property TranslationPaths_ShouldCoverAllLanguages(
        ValidLanguageList languages,
        ValidContentPath contentPath)
    {
        ArgumentNullException.ThrowIfNull(languages);
        ArgumentNullException.ThrowIfNull(contentPath);

        // Arrange
        var defaultLang = languages.Value.First();
        var service = new MultilingualService(languages.Value, defaultLang.Code);

        // Act
        var translationPaths = service.GetTranslationPaths(contentPath.Value);

        // Assert
        var allLanguagesCovered = languages.Value.All(lang =>
            translationPaths.ContainsKey(lang.Code));

        return allLanguagesCovered
            .ToProperty()
            .Label($"翻译路径覆盖所有 {languages.Value.Count} 种语言");
    }

    /// <summary>
    /// **Property 14: IsMultilingual 属性应该正确反映语言数量**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(MultilingualArbitrary)])]
    public Property IsMultilingual_ShouldReflectLanguageCount(ValidLanguageList languages)
    {
        ArgumentNullException.ThrowIfNull(languages);

        // Arrange
        var service = new MultilingualService(languages.Value, languages.Value.First().Code);

        // Assert
        var expected = languages.Value.Count > 1;
        return (service.IsMultilingual == expected)
            .ToProperty()
            .Label($"IsMultilingual={service.IsMultilingual}, 语言数={languages.Value.Count}");
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
}

#region 测试数据类型

/// <summary>
/// 有效语言列表
/// </summary>
public sealed class ValidLanguageList
{
    public IReadOnlyList<LanguageConfig> Value { get; }

    public ValidLanguageList(IReadOnlyList<LanguageConfig> value)
    {
        Value = value;
    }

    public override string ToString() => $"Languages({string.Join(", ", Value.Select(l => l.Code))})";
}

/// <summary>
/// 有效路径
/// </summary>
public sealed class ValidPath
{
    public string Value { get; }

    public ValidPath(string value)
    {
        Value = value;
    }

    public override string ToString() => $"Path({Value})";
}

/// <summary>
/// 有效内容路径
/// </summary>
public sealed class ValidContentPath
{
    public string Value { get; }

    public ValidContentPath(string value)
    {
        Value = value;
    }

    public override string ToString() => $"ContentPath({Value})";
}

#endregion

#region 生成器

/// <summary>
/// 多语言测试数据生成器
/// </summary>
public static class MultilingualArbitrary
{
    /// <summary>
    /// 生成有效的语言配置
    /// </summary>
    private static Gen<LanguageConfig> GenLanguageConfig(string code, string name, string? nativeName, int weight)
    {
        return Gen.Constant(new LanguageConfig
        {
            Code = code,
            Name = name,
            NativeName = nativeName,
            Weight = weight,
            IncludeInUrl = weight > 0 // 非默认语言包含在 URL 中
        });
    }

    /// <summary>
    /// 生成有效的语言列表
    /// </summary>
    public static Arbitrary<ValidLanguageList> ValidLanguageList()
    {
        var allLanguages = new[]
        {
            ("en", "English", "English", 0),
            ("zh", "Chinese", "中文", 1),
            ("ja", "Japanese", "日本語", 2),
            ("de", "German", "Deutsch", 3),
            ("fr", "French", "Français", 4),
            ("es", "Spanish", "Español", 5),
            ("ko", "Korean", "한국어", 6),
            ("ru", "Russian", "Русский", 7)
        };

        var gen = Gen.Choose(1, 5)
            .SelectMany(count =>
            {
                var selectedIndices = Enumerable.Range(0, allLanguages.Length)
                    .OrderBy(_ => Guid.NewGuid())
                    .Take(count)
                    .ToList();

                var configs = selectedIndices
                    .Select((idx, i) =>
                    {
                        var (code, name, native, _) = allLanguages[idx];
                        return new LanguageConfig
                        {
                            Code = code,
                            Name = name,
                            NativeName = native,
                            Weight = i,
                            IncludeInUrl = i > 0
                        };
                    })
                    .ToList();

                return Gen.Constant(new ValidLanguageList(configs));
            });

        return gen.ToArbitrary();
    }

    /// <summary>
    /// 生成有效的路径
    /// </summary>
    public static Arbitrary<ValidPath> ValidPath()
    {
        var gen = Gen.Elements(
            "/",
            "/about/",
            "/blog/",
            "/posts/hello-world/",
            "/docs/getting-started/",
            "/category/tech/",
            "about",
            "blog/post-1"
        ).Select(path => new ValidPath(path));

        return gen.ToArbitrary();
    }

    /// <summary>
    /// 生成有效的内容路径
    /// </summary>
    public static Arbitrary<ValidContentPath> ValidContentPath()
    {
        var gen = Gen.Elements(
            "content/posts/hello.md",
            "content/about.md",
            "content/docs/intro.md",
            "posts/my-post.md",
            "pages/contact.md"
        ).Select(path => new ValidContentPath(path));

        return gen.ToArbitrary();
    }
}

#endregion
