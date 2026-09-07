// Flint 静态站点生成器
// FsCheck 收缩器（Shrinker）实现
// 确保失败用例能被最小化，便于调试

using Flint.Core.Abstractions;
using Flint.Core.Configuration;
using Flint.Core.Models;
using FsCheck;
using FsCheck.Fluent;

namespace Flint.IntegrationTests.Generators;

/// <summary>
/// Flint 类型的 FsCheck 收缩器
/// 当属性测试失败时，收缩器会尝试找到最小的失败用例
/// </summary>
public static class FlintShrinkers
{
    #region SiteConfig 收缩器

    /// <summary>
    /// SiteConfig 收缩器
    /// 尝试简化 SiteConfig 以找到最小失败用例
    /// </summary>
    public static IEnumerable<SiteConfig> ShrinkSiteConfig(SiteConfig config)
    {
        // 尝试简化 BaseURL
        foreach (var baseUrl in ShrinkUrl(config.BaseURL))
        {
            yield return CreateSiteConfig(baseUrl, config.Title, config.LanguageCode, config.Theme,
                config.BuildDrafts, config.BuildFuture, config.BuildExpired, config.Paginate,
                config.SummaryLength, config.EnableGitInfo, config.Copyright, config.Author);
        }

        // 尝试简化 Title
        foreach (var title in ShrinkString(config.Title))
        {
            yield return CreateSiteConfig(config.BaseURL, title, config.LanguageCode, config.Theme,
                config.BuildDrafts, config.BuildFuture, config.BuildExpired, config.Paginate,
                config.SummaryLength, config.EnableGitInfo, config.Copyright, config.Author);
        }

        // 尝试简化 Theme
        if (!string.IsNullOrEmpty(config.Theme))
        {
            yield return CreateSiteConfig(config.BaseURL, config.Title, config.LanguageCode, "",
                config.BuildDrafts, config.BuildFuture, config.BuildExpired, config.Paginate,
                config.SummaryLength, config.EnableGitInfo, config.Copyright, config.Author);
        }

        // 尝试简化 LanguageCode
        if (config.LanguageCode != "en")
        {
            yield return CreateSiteConfig(config.BaseURL, config.Title, "en", config.Theme,
                config.BuildDrafts, config.BuildFuture, config.BuildExpired, config.Paginate,
                config.SummaryLength, config.EnableGitInfo, config.Copyright, config.Author);
        }

        // 尝试简化布尔值
        if (config.BuildDrafts)
        {
            yield return CreateSiteConfig(config.BaseURL, config.Title, config.LanguageCode, config.Theme,
                false, config.BuildFuture, config.BuildExpired, config.Paginate,
                config.SummaryLength, config.EnableGitInfo, config.Copyright, config.Author);
        }
        if (config.BuildFuture)
        {
            yield return CreateSiteConfig(config.BaseURL, config.Title, config.LanguageCode, config.Theme,
                config.BuildDrafts, false, config.BuildExpired, config.Paginate,
                config.SummaryLength, config.EnableGitInfo, config.Copyright, config.Author);
        }
        if (config.BuildExpired)
        {
            yield return CreateSiteConfig(config.BaseURL, config.Title, config.LanguageCode, config.Theme,
                config.BuildDrafts, config.BuildFuture, false, config.Paginate,
                config.SummaryLength, config.EnableGitInfo, config.Copyright, config.Author);
        }
        if (config.EnableGitInfo)
        {
            yield return CreateSiteConfig(config.BaseURL, config.Title, config.LanguageCode, config.Theme,
                config.BuildDrafts, config.BuildFuture, config.BuildExpired, config.Paginate,
                config.SummaryLength, false, config.Copyright, config.Author);
        }

        // 尝试简化 Paginate
        if (config.Paginate > 10)
        {
            yield return CreateSiteConfig(config.BaseURL, config.Title, config.LanguageCode, config.Theme,
                config.BuildDrafts, config.BuildFuture, config.BuildExpired, 10,
                config.SummaryLength, config.EnableGitInfo, config.Copyright, config.Author);
        }

        // 尝试移除可选字段
        if (config.Copyright != null)
        {
            yield return CreateSiteConfig(config.BaseURL, config.Title, config.LanguageCode, config.Theme,
                config.BuildDrafts, config.BuildFuture, config.BuildExpired, config.Paginate,
                config.SummaryLength, config.EnableGitInfo, null, config.Author);
        }
        if (config.Author != null)
        {
            yield return CreateSiteConfig(config.BaseURL, config.Title, config.LanguageCode, config.Theme,
                config.BuildDrafts, config.BuildFuture, config.BuildExpired, config.Paginate,
                config.SummaryLength, config.EnableGitInfo, config.Copyright, null);
        }
    }

    private static SiteConfig CreateSiteConfig(
        string baseUrl, string title, string languageCode, string theme,
        bool buildDrafts, bool buildFuture, bool buildExpired, int paginate,
        int summaryLength, bool enableGitInfo, string? copyright, AuthorConfig? author)
    {
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
            SummaryLength = summaryLength,
            EnableGitInfo = enableGitInfo,
            Copyright = copyright,
            Author = author
        };
    }

    /// <summary>
    /// 带收缩器的 SiteConfig 生成器
    /// </summary>
    public static Arbitrary<SiteConfig> SiteConfigWithShrinker() =>
        Arb.From(
            ArbMap.Default.ArbFor<SiteConfig>().Generator,
            ShrinkSiteConfig);

    #endregion

    #region FrontMatter 收缩器

    /// <summary>
    /// FrontMatter 收缩器
    /// </summary>
    public static IEnumerable<FrontMatter> ShrinkFrontMatter(FrontMatter fm)
    {
        // 尝试简化 Title
        foreach (var title in ShrinkString(fm.Title))
        {
            yield return CreateFrontMatter(title, fm.Date, fm.Draft, fm.Tags, fm.Categories, fm.Weight, fm.Format);
        }

        // 尝试移除 Date
        if (fm.Date.HasValue)
        {
            yield return CreateFrontMatter(fm.Title, null, fm.Draft, fm.Tags, fm.Categories, fm.Weight, fm.Format);
        }

        // 尝试简化 Draft
        if (fm.Draft)
        {
            yield return CreateFrontMatter(fm.Title, fm.Date, false, fm.Tags, fm.Categories, fm.Weight, fm.Format);
        }

        // 尝试简化 Tags
        if (fm.Tags.Count > 0)
        {
            // 移除一个 tag
            for (int i = 0; i < fm.Tags.Count; i++)
            {
                var newTags = fm.Tags.Where((_, idx) => idx != i).ToList();
                yield return CreateFrontMatter(fm.Title, fm.Date, fm.Draft, newTags, fm.Categories, fm.Weight, fm.Format);
            }

            // 清空所有 tags
            yield return CreateFrontMatter(fm.Title, fm.Date, fm.Draft, [], fm.Categories, fm.Weight, fm.Format);
        }

        // 尝试简化 Categories
        if (fm.Categories.Count > 0)
        {
            // 移除一个 category
            for (int i = 0; i < fm.Categories.Count; i++)
            {
                var newCategories = fm.Categories.Where((_, idx) => idx != i).ToList();
                yield return CreateFrontMatter(fm.Title, fm.Date, fm.Draft, fm.Tags, newCategories, fm.Weight, fm.Format);
            }

            // 清空所有 categories
            yield return CreateFrontMatter(fm.Title, fm.Date, fm.Draft, fm.Tags, [], fm.Weight, fm.Format);
        }

        // 尝试简化 Weight
        if (fm.Weight > 0)
        {
            yield return CreateFrontMatter(fm.Title, fm.Date, fm.Draft, fm.Tags, fm.Categories, fm.Weight / 2, fm.Format);
            yield return CreateFrontMatter(fm.Title, fm.Date, fm.Draft, fm.Tags, fm.Categories, 0, fm.Format);
        }
    }

    private static FrontMatter CreateFrontMatter(
        string title, DateTimeOffset? date, bool draft,
        IReadOnlyList<string> tags, IReadOnlyList<string> categories,
        int weight, FrontMatterFormat format)
    {
        return new FrontMatter
        {
            Title = title,
            Date = date,
            Draft = draft,
            Tags = tags,
            Categories = categories,
            Weight = weight,
            Format = format
        };
    }

    /// <summary>
    /// 带收缩器的 FrontMatter 生成器
    /// </summary>
    public static Arbitrary<FrontMatter> FrontMatterWithShrinker() =>
        Arb.From(
            FlintArbitraries.FrontMatterArb().Generator,
            ShrinkFrontMatter);

    #endregion

    #region ContentFile 收缩器

    /// <summary>
    /// ContentFile 收缩器
    /// </summary>
    public static IEnumerable<ContentFile> ShrinkContentFile(ContentFile cf)
    {
        // 尝试简化路径
        foreach (var path in ShrinkPath(cf.Path))
        {
            yield return new ContentFile
            {
                Path = path,
                RawContent = cf.RawContent,
                ModifiedTime = cf.ModifiedTime
            };
        }

        // 尝试简化内容
        var content = cf.GetContentAsString();
        foreach (var shrunkContent in ShrinkString(content))
        {
            if (!string.IsNullOrEmpty(shrunkContent))
            {
                yield return new ContentFile
                {
                    Path = cf.Path,
                    RawContent = System.Text.Encoding.UTF8.GetBytes(shrunkContent),
                    ModifiedTime = cf.ModifiedTime
                };
            }
        }

        // 尝试使用最小内容
        var minimalContent = "---\ntitle: \"Test\"\n---\n\nContent";
        if (content != minimalContent)
        {
            yield return new ContentFile
            {
                Path = cf.Path,
                RawContent = System.Text.Encoding.UTF8.GetBytes(minimalContent),
                ModifiedTime = cf.ModifiedTime
            };
        }
    }

    /// <summary>
    /// 带收缩器的 ContentFile 生成器
    /// </summary>
    public static Arbitrary<ContentFile> ContentFileWithShrinker() =>
        Arb.From(
            FlintArbitraries.ContentFileArb().Generator,
            ShrinkContentFile);

    #endregion

    #region BuildOptions 收缩器

    /// <summary>
    /// BuildOptions 收缩器
    /// </summary>
    public static IEnumerable<BuildOptions> ShrinkBuildOptions(BuildOptions opts)
    {
        // 尝试简化布尔选项
        if (opts.Minify)
        {
            yield return CreateBuildOptions(opts.SourcePath, opts.OutputPath, false, opts.IncludeDrafts,
                opts.IncludeFuture, opts.IncludeExpired, opts.Environment, opts.Parallelism,
                opts.EnableCache, opts.CleanOutput, opts.GenerateSourceMaps, opts.Verbose, opts.Debug,
                opts.BaseUrl, opts.CachePath);
        }
        if (opts.IncludeDrafts)
        {
            yield return CreateBuildOptions(opts.SourcePath, opts.OutputPath, opts.Minify, false,
                opts.IncludeFuture, opts.IncludeExpired, opts.Environment, opts.Parallelism,
                opts.EnableCache, opts.CleanOutput, opts.GenerateSourceMaps, opts.Verbose, opts.Debug,
                opts.BaseUrl, opts.CachePath);
        }
        if (opts.IncludeFuture)
        {
            yield return CreateBuildOptions(opts.SourcePath, opts.OutputPath, opts.Minify, opts.IncludeDrafts,
                false, opts.IncludeExpired, opts.Environment, opts.Parallelism,
                opts.EnableCache, opts.CleanOutput, opts.GenerateSourceMaps, opts.Verbose, opts.Debug,
                opts.BaseUrl, opts.CachePath);
        }
        if (opts.IncludeExpired)
        {
            yield return CreateBuildOptions(opts.SourcePath, opts.OutputPath, opts.Minify, opts.IncludeDrafts,
                opts.IncludeFuture, false, opts.Environment, opts.Parallelism,
                opts.EnableCache, opts.CleanOutput, opts.GenerateSourceMaps, opts.Verbose, opts.Debug,
                opts.BaseUrl, opts.CachePath);
        }
        if (opts.EnableCache)
        {
            yield return CreateBuildOptions(opts.SourcePath, opts.OutputPath, opts.Minify, opts.IncludeDrafts,
                opts.IncludeFuture, opts.IncludeExpired, opts.Environment, opts.Parallelism,
                false, opts.CleanOutput, opts.GenerateSourceMaps, opts.Verbose, opts.Debug,
                opts.BaseUrl, opts.CachePath);
        }
        if (opts.CleanOutput)
        {
            yield return CreateBuildOptions(opts.SourcePath, opts.OutputPath, opts.Minify, opts.IncludeDrafts,
                opts.IncludeFuture, opts.IncludeExpired, opts.Environment, opts.Parallelism,
                opts.EnableCache, false, opts.GenerateSourceMaps, opts.Verbose, opts.Debug,
                opts.BaseUrl, opts.CachePath);
        }
        if (opts.GenerateSourceMaps)
        {
            yield return CreateBuildOptions(opts.SourcePath, opts.OutputPath, opts.Minify, opts.IncludeDrafts,
                opts.IncludeFuture, opts.IncludeExpired, opts.Environment, opts.Parallelism,
                opts.EnableCache, opts.CleanOutput, false, opts.Verbose, opts.Debug,
                opts.BaseUrl, opts.CachePath);
        }
        if (opts.Verbose)
        {
            yield return CreateBuildOptions(opts.SourcePath, opts.OutputPath, opts.Minify, opts.IncludeDrafts,
                opts.IncludeFuture, opts.IncludeExpired, opts.Environment, opts.Parallelism,
                opts.EnableCache, opts.CleanOutput, opts.GenerateSourceMaps, false, opts.Debug,
                opts.BaseUrl, opts.CachePath);
        }
        if (opts.Debug)
        {
            yield return CreateBuildOptions(opts.SourcePath, opts.OutputPath, opts.Minify, opts.IncludeDrafts,
                opts.IncludeFuture, opts.IncludeExpired, opts.Environment, opts.Parallelism,
                opts.EnableCache, opts.CleanOutput, opts.GenerateSourceMaps, opts.Verbose, false,
                opts.BaseUrl, opts.CachePath);
        }

        // 尝试简化 Parallelism
        if (opts.Parallelism > 1)
        {
            yield return CreateBuildOptions(opts.SourcePath, opts.OutputPath, opts.Minify, opts.IncludeDrafts,
                opts.IncludeFuture, opts.IncludeExpired, opts.Environment, 1,
                opts.EnableCache, opts.CleanOutput, opts.GenerateSourceMaps, opts.Verbose, opts.Debug,
                opts.BaseUrl, opts.CachePath);
        }

        // 尝试移除 Environment
        if (opts.Environment != null)
        {
            yield return CreateBuildOptions(opts.SourcePath, opts.OutputPath, opts.Minify, opts.IncludeDrafts,
                opts.IncludeFuture, opts.IncludeExpired, null, opts.Parallelism,
                opts.EnableCache, opts.CleanOutput, opts.GenerateSourceMaps, opts.Verbose, opts.Debug,
                opts.BaseUrl, opts.CachePath);
        }

        // 尝试移除 BaseUrl
        if (opts.BaseUrl != null)
        {
            yield return CreateBuildOptions(opts.SourcePath, opts.OutputPath, opts.Minify, opts.IncludeDrafts,
                opts.IncludeFuture, opts.IncludeExpired, opts.Environment, opts.Parallelism,
                opts.EnableCache, opts.CleanOutput, opts.GenerateSourceMaps, opts.Verbose, opts.Debug,
                null, opts.CachePath);
        }

        // 尝试移除 CachePath
        if (opts.CachePath != null)
        {
            yield return CreateBuildOptions(opts.SourcePath, opts.OutputPath, opts.Minify, opts.IncludeDrafts,
                opts.IncludeFuture, opts.IncludeExpired, opts.Environment, opts.Parallelism,
                opts.EnableCache, opts.CleanOutput, opts.GenerateSourceMaps, opts.Verbose, opts.Debug,
                opts.BaseUrl, null);
        }
    }

    private static BuildOptions CreateBuildOptions(
        string sourcePath, string outputPath, bool minify, bool includeDrafts,
        bool includeFuture, bool includeExpired, string? environment, int parallelism,
        bool enableCache, bool cleanOutput, bool generateSourceMaps, bool verbose, bool debug,
        string? baseUrl, string? cachePath)
    {
        return new BuildOptions
        {
            SourcePath = sourcePath,
            OutputPath = outputPath,
            Minify = minify,
            IncludeDrafts = includeDrafts,
            IncludeFuture = includeFuture,
            IncludeExpired = includeExpired,
            Environment = environment,
            Parallelism = parallelism,
            EnableCache = enableCache,
            CleanOutput = cleanOutput,
            GenerateSourceMaps = generateSourceMaps,
            Verbose = verbose,
            Debug = debug,
            BaseUrl = baseUrl,
            CachePath = cachePath
        };
    }

    /// <summary>
    /// 带收缩器的 BuildOptions 生成器
    /// </summary>
    public static Arbitrary<BuildOptions> BuildOptionsWithShrinker() =>
        Arb.From(
            FlintArbitraries.BuildOptionsArb().Generator,
            ShrinkBuildOptions);

    #endregion

    #region AssetFile 收缩器

    /// <summary>
    /// AssetFile 收缩器
    /// </summary>
    public static IEnumerable<AssetFile> ShrinkAssetFile(AssetFile asset)
    {
        // 尝试简化路径
        foreach (var path in ShrinkPath(asset.SourcePath))
        {
            yield return new AssetFile
            {
                SourcePath = path,
                MediaType = asset.MediaType,
                Content = asset.Content,
                ModifiedTime = asset.ModifiedTime
            };
        }

        // 尝试简化内容（减半）
        if (asset.Content.Length > 8)
        {
            var halfContent = asset.Content.Slice(0, asset.Content.Length / 2);
            yield return new AssetFile
            {
                SourcePath = asset.SourcePath,
                MediaType = asset.MediaType,
                Content = halfContent,
                ModifiedTime = asset.ModifiedTime
            };
        }

        // 尝试使用最小内容
        if (asset.Content.Length > 1)
        {
            yield return new AssetFile
            {
                SourcePath = asset.SourcePath,
                MediaType = asset.MediaType,
                Content = new byte[] { 0 },
                ModifiedTime = asset.ModifiedTime
            };
        }
    }

    /// <summary>
    /// 带收缩器的 AssetFile 生成器
    /// </summary>
    public static Arbitrary<AssetFile> AssetFileWithShrinker() =>
        Arb.From(
            FlintArbitraries.AssetFileArb().Generator,
            ShrinkAssetFile);

    #endregion

    #region 辅助收缩方法

    /// <summary>
    /// 字符串收缩器
    /// </summary>
    private static IEnumerable<string> ShrinkString(string s)
    {
        if (string.IsNullOrEmpty(s))
            yield break;

        // 尝试减半
        if (s.Length > 1)
        {
            yield return s[..(s.Length / 2)];
        }

        // 尝试移除首尾字符
        if (s.Length > 2)
        {
            yield return s[1..];
            yield return s[..^1];
        }

        // 尝试使用简单字符串
        if (s.Length > 4)
        {
            yield return "test";
            yield return "a";
        }
    }

    /// <summary>
    /// URL 收缩器
    /// </summary>
    private static IEnumerable<string> ShrinkUrl(string url)
    {
        if (string.IsNullOrEmpty(url))
            yield break;

        // 尝试使用简单 URL
        if (url != "https://example.com")
        {
            yield return "https://example.com";
        }

        if (url != "http://localhost")
        {
            yield return "http://localhost";
        }
    }

    /// <summary>
    /// 路径收缩器
    /// </summary>
    private static IEnumerable<string> ShrinkPath(string path)
    {
        if (string.IsNullOrEmpty(path))
            yield break;

        // 尝试移除目录层级
        var parts = path.Split('/');
        if (parts.Length > 1)
        {
            // 只保留文件名
            yield return parts[^1];

            // 移除一层目录
            yield return string.Join("/", parts.Skip(1));
        }

        // 尝试使用简单路径
        if (path != "index.md")
        {
            yield return "index.md";
        }

        if (path != "test.md")
        {
            yield return "test.md";
        }
    }

    #endregion
}
